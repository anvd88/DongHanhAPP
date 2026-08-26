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
/// Danh mục hàng hoá — tên chuẩn cho chủng loại + quy cách.
///
/// Hai bất biến đáng canh: (1) danh mục phải GỢI Ý chứ không CHẶN — gõ tay một mặt hàng lạ vẫn lưu
/// được phiếu, nếu không thì cả xưởng đứng hình vì một mặt hàng chưa kịp khai; (2) danh mục phải tự
/// gắn được vào dữ liệu cũ, nếu không thống kê theo mặt hàng chỉ có số liệu từ ngày khai báo trở đi
/// và không ai thèm dùng.
/// </summary>
[Collection(ApiCollection.Name)]
public sealed class ProductCatalogTests
{
    private readonly ApiFactory _factory;
    public ProductCatalogTests(ApiFactory factory) => _factory = factory;

    /// <summary>
    /// Dựng danh mục từ chính dữ liệu cũ: thêm xong thì dòng phiếu cũ khớp đúng tên+quy cách phải
    /// được đóng dấu mã hàng ngay, không đợi phiếu mới.
    /// </summary>
    [Fact]
    public async Task ImportingFromSuggestions_StampsOldVoucherLines_SoStatisticsStartFromHistory()
    {
        var world = await SetupAsync();
        var voucher = await InsertVoucherAsync(world, "Thép tấm " + world.Suffix, "10mm", 1_000m, 12_000m);
        using var accountant = Client(world.AccountantToken);

        // Mặt hàng đã gõ trên phiếu nhưng chưa có trong danh mục ⇒ phải nằm trong gợi ý.
        var suggestions = await accountant.GetFromJsonAsync<JsonElement>("/api/products/suggestions");
        var mine = suggestions.GetProperty("items").EnumerateArray()
            .Single(x => x.GetProperty("name").GetString() == "Thép tấm " + world.Suffix);
        Assert.Equal(1, mine.GetProperty("timesUsed").GetInt32());

        var imported = await accountant.PostAsJsonAsync("/api/products/import", new
        {
            items = new[] { new { name = "Thép tấm " + world.Suffix, spec = "10mm" } },
        });
        Assert.Equal(HttpStatusCode.OK, imported.StatusCode);
        var body = await imported.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(1, body.GetProperty("added").GetInt32());
        Assert.True(body.GetProperty("linkedLines").GetInt32() >= 1);

        // Dòng phiếu CŨ giờ có mã hàng, và số liệu bán hàng hiện ngay trong danh mục.
        Assert.NotNull(await LineProductIdAsync(voucher));
        var products = await accountant.GetFromJsonAsync<JsonElement>(
            $"/api/products?q=Thép tấm {world.Suffix}");
        var product = products.GetProperty("items").EnumerateArray().Single();
        Assert.Equal(1_000m, product.GetProperty("soldQuantity").GetDecimal());
        Assert.Equal(12_000m, product.GetProperty("lastPrice").GetDecimal());

        // Đã vào danh mục thì thôi gợi ý lại.
        var after = await accountant.GetFromJsonAsync<JsonElement>("/api/products/suggestions");
        Assert.DoesNotContain(after.GetProperty("items").EnumerateArray(),
            x => x.GetProperty("name").GetString() == "Thép tấm " + world.Suffix);
    }

    /// <summary>Chống trùng chính là lý do danh mục tồn tại — kể cả khi chỉ khác hoa/thường.</summary>
    [Fact]
    public async Task TheSameItemInDifferentCasing_CannotBeAddedTwice()
    {
        var world = await SetupAsync();
        using var accountant = Client(world.AccountantToken);
        var name = "Tôn cuộn " + world.Suffix;

        var first = await accountant.PostAsJsonAsync("/api/products", new { name, spec = "0.45" });
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);

        var duplicate = await accountant.PostAsJsonAsync("/api/products",
            new { name = name.ToUpperInvariant(), spec = "0.45" });
        Assert.Equal(HttpStatusCode.Conflict, duplicate.StatusCode);
        Assert.Equal(1, await ProductCountAsync(name));
    }

    /// <summary>
    /// Danh mục là GỢI Ý, không phải rào chắn: mặt hàng lạ vẫn lập phiếu được. Nhưng nếu gõ trùng
    /// khít một mặt hàng đã khai thì máy chủ vẫn tự đóng dấu mã — người nhập không phải nhớ bấm chọn.
    /// </summary>
    [Fact]
    public async Task FreeTextLinesStillSave_AndAnExactNameMatchGetsLinkedAutomatically()
    {
        var world = await SetupAsync();
        using var accountant = Client(world.AccountantToken);
        var known = "Xà gồ " + world.Suffix;
        await accountant.PostAsJsonAsync("/api/products", new { name = known, spec = "C200" });

        var created = await accountant.PostAsJsonAsync("/api/documents", new
        {
            voucherNo = "",
            documentType = "document",
            date = DateTime.Today.ToString("yyyy-MM-dd"),
            customerName = world.CustomerName,
            content = "Bán hàng",
            note = "",
            lines = new object[]
            {
                // Gõ tay, khớp khít mặt hàng đã khai (chữ thường khác đi) ⇒ tự gắn mã.
                new { lineContent = known.ToLowerInvariant(), spec = "c200", quantity = 10m, unitPrice = 30_000m, note = "" },
                // Hàng lạ chưa từng khai ⇒ vẫn lưu được, chỉ là không có mã.
                new { lineContent = "Hàng gia công lẻ " + world.Suffix, spec = "", quantity = 1m, unitPrice = 500_000m, note = "" },
            },
        });
        Assert.Equal(HttpStatusCode.OK, created.StatusCode);
        var documentId = (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();

        Assert.NotNull(await LineProductIdAsync(documentId, lineNo: 1));
        Assert.Null(await LineProductIdAsync(documentId, lineNo: 2));
    }

    /// <summary>Xem được danh mục không có nghĩa là sửa được nó.</summary>
    [Fact]
    public async Task ReadOnlyAccountingRoles_CanBrowseTheCatalog_ButNotChangeIt()
    {
        var world = await SetupAsync();
        using var executive = Client(world.ExecutiveToken);

        var read = await executive.GetAsync("/api/products");
        Assert.Equal(HttpStatusCode.OK, read.StatusCode);

        var write = await executive.PostAsJsonAsync("/api/products",
            new { name = "Thép ống " + world.Suffix, spec = "D60" });
        Assert.Equal(HttpStatusCode.Forbidden, write.StatusCode);
        Assert.Equal(0, await ProductCountAsync("Thép ống " + world.Suffix));
    }

    [Fact]
    public async Task PeopleOutsideAccounting_CannotSeeTheCatalogAtAll()
    {
        var world = await SetupAsync();
        using var driver = Client(world.DriverToken);
        var response = await driver.GetAsync("/api/products");
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    // ── Hạ tầng dựng dữ liệu ─────────────────────────────────────────────────────────────────

    private sealed record World(string Suffix, Guid CustomerId, string CustomerName,
        string AccountantToken, string ExecutiveToken, string DriverToken);

    private HttpClient Client(string token)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    private async Task<World> SetupAsync()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var dept = Guid.NewGuid();
        var customerId = Guid.NewGuid();
        var customerName = "Khách " + suffix;

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<Database>();
        var tokens = scope.ServiceProvider.GetRequiredService<TokenService>();
        await using var conn = await db.OpenAsync();
        await conn.Cmd("INSERT INTO hr_departments (id,code,name,is_accounting) VALUES (@id,@code,@name,TRUE)")
            .With("@id", dept).With("@code", "PC" + suffix[..5]).With("@name", "Kế toán " + suffix)
            .ExecuteNonQueryAsync();
        await conn.Cmd("INSERT INTO customers (id,name,is_active) VALUES (@id,@name,TRUE)")
            .With("@id", customerId).With("@name", customerName).ExecuteNonQueryAsync();

        return new World(suffix, customerId, customerName,
            await MakeUser(conn, tokens, "__pc_acc_" + suffix, AppRoles.Accounting, dept),
            await MakeUser(conn, tokens, "__pc_exe_" + suffix, AppRoles.Executive, dept),
            await MakeUser(conn, tokens, "__pc_drv_" + suffix, AppRoles.Driver, dept));
    }

    private static async Task<string> MakeUser(NpgsqlConnection conn, TokenService tokens,
        string username, string role, Guid departmentId)
    {
        var userId = Guid.NewGuid();
        await conn.Cmd("""
            INSERT INTO app_users (id,username,full_name,email,role,password_hash,is_active,
                approval_status,approved_at,approved_by,created_at,is_deleted)
            VALUES (@id,@u,@u,'',@role,@hash,TRUE,'Approved',CURRENT_TIMESTAMP,'test',CURRENT_TIMESTAMP,FALSE)
            """).With("@id", userId).With("@u", username).With("@role", role)
            .With("@hash", PasswordHasher.Hash("test-pass")).ExecuteNonQueryAsync();
        await conn.Cmd("""
            INSERT INTO hr_employees (id,employee_code,user_id,username,full_name,department_id,status,position)
            VALUES (@id,@code,@uid,@u,@u,@dept,'Active','Nhân viên')
            """).With("@id", Guid.NewGuid()).With("@code", "PC" + Guid.NewGuid().ToString("N")[..8])
            .With("@uid", userId).With("@u", username).With("@dept", departmentId)
            .ExecuteNonQueryAsync();
        return tokens.CreateToken(new UserDto(userId, username, username, "", role, true, "Approved", DateTime.UtcNow),
            "app:pc:" + Guid.NewGuid().ToString("N")[..16]);
    }

    private async Task<Guid> InsertVoucherAsync(World world, string content, string spec,
        decimal quantity, decimal unitPrice)
    {
        var id = Guid.NewGuid();
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<Database>();
        await using var conn = await db.OpenAsync();
        await conn.Cmd("""
            INSERT INTO documents (id,voucher_no,doc_date,customer_id,customer_name,document_type,content,issued_at)
            VALUES (@id,@no,CURRENT_DATE,@cid,@cname,'document','Bán hàng',CURRENT_TIMESTAMP)
            """).With("@id", id).With("@no", "PX" + Guid.NewGuid().ToString("N")[..10])
            .With("@cid", world.CustomerId).With("@cname", world.CustomerName).ExecuteNonQueryAsync();
        await conn.Cmd("""
            INSERT INTO document_lines (document_id,line_no,line_content,spec,quantity,unit_price,note)
            VALUES (@id,1,@content,@spec,@q,@p,'')
            """).With("@id", id).With("@content", content).With("@spec", spec)
            .With("@q", quantity).With("@p", unitPrice).ExecuteNonQueryAsync();
        return id;
    }

    private async Task<Guid?> LineProductIdAsync(Guid documentId, int lineNo = 1)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<Database>();
        await using var conn = await db.OpenAsync();
        return await conn.Cmd("SELECT product_id FROM document_lines WHERE document_id=@id AND line_no=@no")
            .With("@id", documentId).With("@no", lineNo).ExecuteScalarAsync() as Guid?;
    }

    private async Task<int> ProductCountAsync(string name)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<Database>();
        await using var conn = await db.OpenAsync();
        return Convert.ToInt32(await conn.Cmd("SELECT COUNT(*) FROM products WHERE lower(name)=lower(@n)")
            .With("@n", name).ExecuteScalarAsync() ?? 0);
    }
}
