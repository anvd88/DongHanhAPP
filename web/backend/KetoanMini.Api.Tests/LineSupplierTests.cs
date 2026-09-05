using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using KetoanMini.Api.Data;
using KetoanMini.Api.Models;
using KetoanMini.Api.Security;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Xunit;

namespace KetoanMini.Api.Tests;

/// <summary>
/// NGUỒN HÀNG trên dòng phiếu bán: cuộn vừa xuất là hàng nhập của nhà cung cấp nào.
///
/// Hai bất biến, và cái thứ hai mới là cái dễ vỡ:
///   1. Tồn theo từng nguồn phải đúng. Cùng một mặt hàng nhập của hai nơi với giá khác nhau, xuất
///      mà không ghi lấy của ai thì tổng tồn vẫn đúng nhưng tồn từng nguồn sai — mà đó mới là số
///      thủ kho cầm đi đếm hàng thật. Hàng khách trả về phải cộng ngược vào đúng nguồn cũ.
///   2. Khách KHÔNG được thấy. Nguồn hàng là chuyện nội bộ: không in trên phiếu giao cho khách,
///      không nằm trong ảnh chụp lúc phát hành, không có trong sổ công nợ PDF. Rò ra là khách biết
///      mình mua lại của ai và mua với giá nào.
/// </summary>
[Collection(ApiCollection.Name)]
public sealed class LineSupplierTests
{
    private readonly ApiFactory _factory;
    public LineSupplierTests(ApiFactory factory) => _factory = factory;

    [Fact]
    public async Task ASaleLineRemembersWhichSupplierTheGoodsCameFrom()
    {
        var world = await SetupAsync();
        using var accountant = Client(world.AccountantToken);

        var created = await accountant.PostAsJsonAsync("/api/documents", new
        {
            voucherNo = "",
            date = DateOnly.FromDateTime(DateTime.Today),
            customerName = world.CustomerName,
            content = "Bán thép tấm",
            note = "",
            documentType = "document",
            lines = new[]
            {
                new
                {
                    lineContent = world.ProductName,
                    spec = "10mm",
                    quantity = 100m,
                    unitPrice = 15_000m,
                    note = "",
                    productId = world.ProductId,
                    supplierId = world.SupplierId,
                    supplierName = world.SupplierName,
                },
            },
        });
        Assert.Equal(HttpStatusCode.OK, created.StatusCode);
        var id = (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();

        var detail = await accountant.GetFromJsonAsync<JsonElement>($"/api/documents/{id}");
        var line = detail.GetProperty("lines").EnumerateArray().Single();
        Assert.Equal(world.SupplierId, line.GetProperty("supplierId").GetGuid());
        Assert.Equal(world.SupplierName, line.GetProperty("supplierName").GetString());
    }

    /// <summary>
    /// Nhập 500 của nhà cung cấp A, bán 200 của A ⇒ A còn 300. Nhà cung cấp B chưa bán gì thì vẫn
    /// còn nguyên, dù cùng bán một mặt hàng.
    /// </summary>
    [Fact]
    public async Task StockIsCountedPerSupplier_NotJustPerProduct()
    {
        var world = await SetupAsync();
        var other = await AddSupplierAsync("__ncc_b_");
        await InsertPurchaseAsync(world, world.SupplierId, 500m, 12_000m);
        await InsertPurchaseAsync(world, other.Id, 400m, 13_500m);
        await InsertSaleAsync(world, world.SupplierId, 200m);

        using var accountant = Client(world.AccountantToken);
        var sources = await accountant.GetFromJsonAsync<JsonElement>($"/api/products/{world.ProductId}/sources");
        var items = sources.GetProperty("items").EnumerateArray().ToList();

        var first = items.Single(x => x.GetProperty("supplierId").GetGuid() == world.SupplierId);
        Assert.Equal(500m, first.GetProperty("bought").GetDecimal());
        Assert.Equal(200m, first.GetProperty("sold").GetDecimal());
        Assert.Equal(300m, first.GetProperty("remaining").GetDecimal());
        Assert.Equal(12_000m, first.GetProperty("lastCost").GetDecimal());

        var second = items.Single(x => x.GetProperty("supplierId").GetGuid() == other.Id);
        Assert.Equal(400m, second.GetProperty("remaining").GetDecimal());
    }

    /// <summary>
    /// Nhìn theo chiều ngược: mở hồ sơ một nhà cung cấp thì thấy hàng của họ còn lại những gì. Phải
    /// khớp từng con số với bảng nguồn hàng của mặt hàng, nếu không hai màn hình sẽ cãi nhau.
    /// </summary>
    [Fact]
    public async Task TheSupplierStockView_MatchesTheProductSourceView()
    {
        var world = await SetupAsync();
        await InsertPurchaseAsync(world, world.SupplierId, 500m, 12_000m);
        await InsertSaleAsync(world, world.SupplierId, 120m);

        using var accountant = Client(world.AccountantToken);
        var stock = await accountant.GetFromJsonAsync<JsonElement>($"/api/suppliers/{world.SupplierId}/stock");
        var row = stock.GetProperty("items").EnumerateArray()
            .Single(x => x.GetProperty("productId").GetGuid() == world.ProductId);

        Assert.Equal(world.ProductName, row.GetProperty("name").GetString());
        Assert.Equal(500m, row.GetProperty("bought").GetDecimal());
        Assert.Equal(120m, row.GetProperty("sold").GetDecimal());
        Assert.Equal(380m, row.GetProperty("remaining").GetDecimal());
        Assert.Equal(12_000m, row.GetProperty("lastCost").GetDecimal());

        var sources = await accountant.GetFromJsonAsync<JsonElement>($"/api/products/{world.ProductId}/sources");
        var mirrored = sources.GetProperty("items").EnumerateArray()
            .Single(x => x.GetProperty("supplierId").GetGuid() == world.SupplierId);
        foreach (var field in new[] { "bought", "sold", "remaining" })
            Assert.Equal(mirrored.GetProperty(field).GetDecimal(), row.GetProperty(field).GetDecimal());
    }

    /// <summary>Khách trả hàng là hàng quay lại kho: phải cộng ngược vào đúng nguồn đã xuất.</summary>
    [Fact]
    public async Task GoodsComingBack_AreAddedBackToTheSupplierTheyWentOutFrom()
    {
        var world = await SetupAsync();
        await InsertPurchaseAsync(world, world.SupplierId, 500m, 12_000m);
        var sale = await InsertSaleAsync(world, world.SupplierId, 200m, settled: true);

        using var accountant = Client(world.AccountantToken);
        var returned = await accountant.PostAsJsonAsync("/api/returns", new
        {
            reason = "Khách trả lại hàng thừa",
            lines = new[] { new { sourceDocumentId = sale, sourceLineNo = 1, quantity = 50m } },
        });
        Assert.Equal(HttpStatusCode.OK, returned.StatusCode);

        var sources = await accountant.GetFromJsonAsync<JsonElement>($"/api/products/{world.ProductId}/sources");
        var row = sources.GetProperty("items").EnumerateArray()
            .Single(x => x.GetProperty("supplierId").GetGuid() == world.SupplierId);

        Assert.Equal(150m, row.GetProperty("sold").GetDecimal());
        Assert.Equal(350m, row.GetProperty("remaining").GetDecimal());
    }

    /// <summary>
    /// Hai đường giấy tờ đi ra ngoài công ty: tờ phiếu in cho khách ký (chụp lại ở
    /// document_issued_lines) và sổ công nợ PDF. Cả hai đều không được mang theo nguồn hàng.
    /// </summary>
    [Fact]
    public async Task NeitherThePrintedVoucherNorTheCustomerStatement_CarriesTheSupplier()
    {
        await using var conn = await OpenAsync();
        var printed = new List<string>();
        await using (var r = await conn.Cmd(
            @"SELECT column_name FROM information_schema.columns
              WHERE table_schema = current_schema() AND table_name = 'document_issued_lines'")
            .ExecuteReaderAsync())
        {
            while (await r.ReadAsync()) printed.Add(r.Str("column_name"));
        }

        Assert.NotEmpty(printed);
        Assert.DoesNotContain(printed, c => c.Contains("supplier", StringComparison.OrdinalIgnoreCase));

        // Dòng hàng trong sổ công nợ PDF: kiểu dữ liệu là chốt chặn, không ai vô tình thêm được.
        var statementFields = typeof(DebtVoucherLineDto).GetProperties().Select(x => x.Name).ToList();
        Assert.NotEmpty(statementFields);
        Assert.DoesNotContain(statementFields, f => f.Contains("supplier", StringComparison.OrdinalIgnoreCase));
    }

    // --- Dựng dữ liệu ------------------------------------------------------------------------------

    private async Task<NpgsqlConnection> OpenAsync()
    {
        using var scope = _factory.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<Database>().OpenAsync();
    }

    private async Task<World> SetupAsync()
    {
        var suffix = Guid.NewGuid().ToString("N")[..10];
        var dept = Guid.NewGuid();
        var customerId = Guid.NewGuid();
        var customerName = "Khách " + suffix;
        var productId = Guid.NewGuid();
        var productName = "Thép tấm " + suffix;
        var supplierId = Guid.NewGuid();
        var supplierName = "NCC A " + suffix;

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<Database>();
        var tokens = scope.ServiceProvider.GetRequiredService<TokenService>();
        await using var conn = await db.OpenAsync();
        await conn.Cmd("INSERT INTO hr_departments (id,code,name,is_accounting) VALUES (@id,@code,@name,TRUE)")
            .With("@id", dept).With("@code", "LS" + suffix[..5]).With("@name", "Kế toán " + suffix)
            .ExecuteNonQueryAsync();
        await conn.Cmd("INSERT INTO customers (id,name,is_active) VALUES (@id,@name,TRUE)")
            .With("@id", customerId).With("@name", customerName).ExecuteNonQueryAsync();
        await conn.Cmd("INSERT INTO products (id,code,name,spec,unit,is_active) VALUES (@id,@code,@name,'10mm','kg',TRUE)")
            .With("@id", productId).With("@code", "SP" + suffix[..6]).With("@name", productName)
            .ExecuteNonQueryAsync();
        await conn.Cmd("INSERT INTO suppliers (id,name,is_active) VALUES (@id,@name,TRUE)")
            .With("@id", supplierId).With("@name", supplierName).ExecuteNonQueryAsync();

        var userId = Guid.NewGuid();
        var username = "__ls_acc_" + suffix;
        await conn.Cmd("""
            INSERT INTO app_users (id,username,full_name,email,role,password_hash,is_active,
                approval_status,approved_at,approved_by,created_at,is_deleted)
            VALUES (@id,@u,@u,'',@role,@hash,TRUE,'Approved',CURRENT_TIMESTAMP,'test',CURRENT_TIMESTAMP,FALSE)
            """).With("@id", userId).With("@u", username).With("@role", AppRoles.Accounting)
            .With("@hash", PasswordHasher.Hash("test-pass")).ExecuteNonQueryAsync();
        await conn.Cmd("""
            INSERT INTO hr_employees (id,employee_code,user_id,username,full_name,department_id,status,position)
            VALUES (@id,@code,@uid,@u,@u,@dept,'Active','Nhân viên')
            """).With("@id", Guid.NewGuid()).With("@code", "LS" + Guid.NewGuid().ToString("N")[..8])
            .With("@uid", userId).With("@u", username).With("@dept", dept).ExecuteNonQueryAsync();

        var token = tokens.CreateToken(
            new UserDto(userId, username, username, "", AppRoles.Accounting, true, "Approved", DateTime.UtcNow),
            "app:ls:" + Guid.NewGuid().ToString("N")[..16]);
        return new World(customerId, customerName, productId, productName, supplierId, supplierName, token);
    }

    private async Task<(Guid Id, string Name)> AddSupplierAsync(string prefix)
    {
        var id = Guid.NewGuid();
        var name = prefix + Guid.NewGuid().ToString("N")[..8];
        await using var conn = await OpenAsync();
        await conn.Cmd("INSERT INTO suppliers (id,name,is_active) VALUES (@id,@name,TRUE)")
            .With("@id", id).With("@name", name).ExecuteNonQueryAsync();
        return (id, name);
    }

    private async Task InsertPurchaseAsync(World world, Guid supplierId, decimal quantity, decimal unitCost)
    {
        var id = Guid.NewGuid();
        await using var conn = await OpenAsync();
        await conn.Cmd("""
            INSERT INTO purchases (id,voucher_no,doc_date,supplier_id,supplier_name)
            VALUES (@id,@no,CURRENT_DATE,@sid,'')
            """).With("@id", id).With("@no", "PN" + Guid.NewGuid().ToString("N")[..10])
            .With("@sid", supplierId).ExecuteNonQueryAsync();
        await conn.Cmd("""
            INSERT INTO purchase_lines (purchase_id,line_no,product_id,line_content,spec,quantity,unit_price)
            VALUES (@id,1,@pid,@name,'10mm',@q,@p)
            """).With("@id", id).With("@pid", world.ProductId).With("@name", world.ProductName)
            .With("@q", quantity).With("@p", unitCost).ExecuteNonQueryAsync();
    }

    private async Task<Guid> InsertSaleAsync(World world, Guid supplierId, decimal quantity, bool settled = false)
    {
        var id = Guid.NewGuid();
        await using var conn = await OpenAsync();
        await conn.Cmd("""
            INSERT INTO documents (id,voucher_no,doc_date,customer_id,customer_name,document_type,
                content,issued_at,delivery_returned_at)
            VALUES (@id,@no,CURRENT_DATE,@cid,@cname,'document','Bán hàng',CURRENT_TIMESTAMP,@settled)
            """).With("@id", id).With("@no", "PX" + Guid.NewGuid().ToString("N")[..10])
            .With("@cid", world.CustomerId).With("@cname", world.CustomerName)
            .With("@settled", settled ? DateTime.UtcNow : (object)DBNull.Value).ExecuteNonQueryAsync();
        await conn.Cmd("""
            INSERT INTO document_lines (document_id,line_no,line_content,spec,quantity,unit_price,note,
                product_id,supplier_id,supplier_name)
            VALUES (@id,1,@name,'10mm',@q,15000,'',@pid,@sid,'')
            """).With("@id", id).With("@name", world.ProductName).With("@q", quantity)
            .With("@pid", world.ProductId).With("@sid", supplierId).ExecuteNonQueryAsync();
        return id;
    }

    private HttpClient Client(string token)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    private sealed record World(Guid CustomerId, string CustomerName, Guid ProductId, string ProductName,
        Guid SupplierId, string SupplierName, string AccountantToken);
}
