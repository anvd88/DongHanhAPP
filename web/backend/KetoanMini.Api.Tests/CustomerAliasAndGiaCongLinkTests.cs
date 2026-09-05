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
/// Hai khoá liên kết vừa nối lại.
///
///   • BÍ DANH KHÁCH HÀNG: bảng có sẵn từ thời app desktop nhưng chưa từng có chỗ nào đọc, nên mọi
///     cách gọi khác của một khách vẫn đẻ ra khách mới và chẻ nhỏ công nợ phải thu.
///   • GIA CÔNG: đối tác và tên hàng đều là chữ tự do, phiếu gia công đứng ngoài mọi thống kê. Nối
///     vào danh mục nhà cung cấp và danh mục hàng hoá thì dữ liệu mới ghép được với hai vế kia.
/// </summary>
[Collection(ApiCollection.Name)]
public sealed class CustomerAliasAndGiaCongLinkTests
{
    private readonly ApiFactory _factory;
    public CustomerAliasAndGiaCongLinkTests(ApiFactory factory) => _factory = factory;

    [Fact]
    public async Task ASaleTypedWithAnAlias_LandsOnTheExistingCustomer_WithoutCreatingASecondOne()
    {
        var world = await SetupAsync();
        var alias = "anh Ba - Hoà Phát " + world.Suffix;
        using var accountant = Client(world.Token);

        var added = await accountant.PostAsJsonAsync($"/api/customers/{world.CustomerId}/aliases", new { alias });
        Assert.Equal(HttpStatusCode.OK, added.StatusCode);

        var created = await accountant.PostAsJsonAsync("/api/documents", NewSale(alias));
        Assert.Equal(HttpStatusCode.OK, created.StatusCode);
        var id = (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();

        await using var conn = await OpenAsync();
        var linked = await conn.Cmd("SELECT customer_id FROM documents WHERE id=@id")
            .With("@id", id).ExecuteScalarAsync();
        Assert.Equal(world.CustomerId, (Guid)linked!);

        var customers = await accountant.GetFromJsonAsync<JsonElement>("/api/customers");
        Assert.DoesNotContain(customers.EnumerateArray(), x => x.GetProperty("name").GetString() == alias);

        var row = customers.EnumerateArray().Single(x => x.GetProperty("id").GetGuid() == world.CustomerId);
        Assert.Contains(alias, row.GetProperty("aliases").EnumerateArray().Select(x => x.GetString()));
    }

    [Fact]
    public async Task ACustomerAlias_CannotBeStolen_NorShadowAnotherCustomersRealName()
    {
        var world = await SetupAsync();
        var otherName = "Khách khác " + world.Suffix;
        var other = await AddCustomerAsync(otherName);
        var alias = "kho đầu làng " + world.Suffix;
        using var accountant = Client(world.Token);

        Assert.Equal(HttpStatusCode.OK,
            (await accountant.PostAsJsonAsync($"/api/customers/{world.CustomerId}/aliases", new { alias })).StatusCode);

        var stolen = await accountant.PostAsJsonAsync($"/api/customers/{other}/aliases", new { alias });
        Assert.Equal(HttpStatusCode.Conflict, stolen.StatusCode);

        var shadowing = await accountant.PostAsJsonAsync(
            $"/api/customers/{world.CustomerId}/aliases", new { alias = otherName });
        Assert.Equal(HttpStatusCode.Conflict, shadowing.StatusCode);
    }

    /// <summary>
    /// Phiếu gia công gõ tay tên xưởng và tên hàng: cả hai phải bám được vào danh mục, nếu không nó
    /// lại đứng ngoài như trước.
    /// </summary>
    [Fact]
    public async Task AGiaCongVoucher_LinksItsPartnerToASupplier_AndItsLinesToTheProductCatalog()
    {
        var world = await SetupAsync();
        var partner = "Xưởng mạ " + world.Suffix;
        using var accountant = Client(world.Token);

        var created = await accountant.PostAsJsonAsync("/api/giacong", new
        {
            loaiPhieu = "Xuất gia công",
            doiTac = partner,
            nhanVienPhuTrach = "",
            ngayLap = DateOnly.FromDateTime(DateTime.Today),
            hanHoanThanh = (DateOnly?)null,
            ghiChu = "",
            lines = new[]
            {
                new
                {
                    id = 1,
                    loaiDong = "Xuất gia công",
                    maHang = "",
                    tenHang = world.ProductName,
                    quyCach = "10mm",
                    donViTinh = "kg",
                    soLuong = 300m,
                    donGiaGiaCong = 0m,
                    ghiChu = "",
                    productId = (Guid?)null,
                },
            },
        });
        Assert.Equal(HttpStatusCode.OK, created.StatusCode);

        await using var conn = await OpenAsync();
        // Đối tác thành hồ sơ nhà cung cấp thật.
        var supplierId = await conn.Cmd(
            "SELECT doi_tac_id FROM gia_cong_phieu WHERE doi_tac=@dt ORDER BY id DESC LIMIT 1")
            .With("@dt", partner).ExecuteScalarAsync();
        Assert.IsType<Guid>(supplierId);
        var supplierName = await conn.Cmd("SELECT name FROM suppliers WHERE id=@id")
            .With("@id", (Guid)supplierId!).ExecuteScalarAsync() as string;
        Assert.Equal(partner, supplierName);

        // Dòng hàng tự khớp mã hàng theo tên cộng quy cách, dù máy khách không gửi.
        var productId = await conn.Cmd("""
            SELECT h.product_id FROM gia_cong_hang_hoa h
            JOIN gia_cong_phieu p ON p.id = h.phieu_id
            WHERE p.doi_tac = @dt ORDER BY h.id DESC LIMIT 1
            """).With("@dt", partner).ExecuteScalarAsync();
        Assert.Equal(world.ProductId, (Guid)productId!);
    }

    /// <summary>Xưởng đã có bí danh thì phiếu gia công cũng phải về đúng hồ sơ đó.</summary>
    [Fact]
    public async Task AGiaCongPartnerTypedAsAnAlias_LandsOnTheExistingSupplier()
    {
        var world = await SetupAsync();
        var real = "Công ty Mạ Kẽm " + world.Suffix;
        var alias = "xưởng chú Tư " + world.Suffix;
        using var accountant = Client(world.Token);

        var supplierId = Guid.NewGuid();
        await using (var conn = await OpenAsync())
        {
            await conn.Cmd("INSERT INTO suppliers (id,name,is_active) VALUES (@id,@n,TRUE)")
                .With("@id", supplierId).With("@n", real).ExecuteNonQueryAsync();
        }
        await accountant.PostAsJsonAsync($"/api/suppliers/{supplierId}/aliases", new { alias });

        var created = await accountant.PostAsJsonAsync("/api/giacong", new
        {
            loaiPhieu = "Xuất gia công",
            doiTac = alias,
            nhanVienPhuTrach = "",
            ngayLap = DateOnly.FromDateTime(DateTime.Today),
            hanHoanThanh = (DateOnly?)null,
            ghiChu = "",
            lines = new[]
            {
                new { id = 1, loaiDong = "Xuất gia công", maHang = "", tenHang = "Tôn cuộn", quyCach = "0.45",
                      donViTinh = "kg", soLuong = 100m, donGiaGiaCong = 0m, ghiChu = "", productId = (Guid?)null },
            },
        });
        Assert.Equal(HttpStatusCode.OK, created.StatusCode);

        await using var check = await OpenAsync();
        var linked = await check.Cmd(
            "SELECT doi_tac_id FROM gia_cong_phieu WHERE doi_tac=@dt ORDER BY id DESC LIMIT 1")
            .With("@dt", alias).ExecuteScalarAsync();
        Assert.Equal(supplierId, (Guid)linked!);
    }

    // --- Dựng dữ liệu ------------------------------------------------------------------------------

    private object NewSale(string customerName) => new
    {
        voucherNo = "",
        date = DateOnly.FromDateTime(DateTime.Today),
        customerName,
        content = "Bán hàng",
        note = "",
        documentType = "document",
        lines = new[]
        {
            new { lineContent = "Thép tấm", spec = "10mm", quantity = 10m, unitPrice = 15_000m, note = "" },
        },
    };

    private async Task<NpgsqlConnection> OpenAsync()
    {
        using var scope = _factory.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<Database>().OpenAsync();
    }

    private async Task<Guid> AddCustomerAsync(string name)
    {
        var id = Guid.NewGuid();
        await using var conn = await OpenAsync();
        await conn.Cmd("INSERT INTO customers (id,name,is_active) VALUES (@id,@n,TRUE)")
            .With("@id", id).With("@n", name).ExecuteNonQueryAsync();
        return id;
    }

    private async Task<World> SetupAsync()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var dept = Guid.NewGuid();
        var customerId = Guid.NewGuid();
        var productId = Guid.NewGuid();
        var productName = "Thép tấm " + suffix;

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<Database>();
        var tokens = scope.ServiceProvider.GetRequiredService<TokenService>();
        await using var conn = await db.OpenAsync();
        await conn.Cmd("INSERT INTO hr_departments (id,code,name,is_accounting) VALUES (@id,@code,@name,TRUE)")
            .With("@id", dept).With("@code", "CA" + suffix[..5]).With("@name", "Kế toán " + suffix)
            .ExecuteNonQueryAsync();
        await conn.Cmd("INSERT INTO customers (id,name,is_active) VALUES (@id,@name,TRUE)")
            .With("@id", customerId).With("@name", "Công ty Hoà Phát " + suffix).ExecuteNonQueryAsync();
        await conn.Cmd("INSERT INTO products (id,code,name,spec,unit,is_active) VALUES (@id,@code,@name,'10mm','kg',TRUE)")
            .With("@id", productId).With("@code", "GC" + suffix[..6]).With("@name", productName)
            .ExecuteNonQueryAsync();

        var userId = Guid.NewGuid();
        var username = "__calias_" + suffix;
        await conn.Cmd("""
            INSERT INTO app_users (id,username,full_name,email,role,password_hash,is_active,
                approval_status,approved_at,approved_by,created_at,is_deleted)
            VALUES (@id,@u,@u,'',@role,@hash,TRUE,'Approved',CURRENT_TIMESTAMP,'test',CURRENT_TIMESTAMP,FALSE)
            """).With("@id", userId).With("@u", username).With("@role", AppRoles.Accounting)
            .With("@hash", PasswordHasher.Hash("test-pass")).ExecuteNonQueryAsync();
        await conn.Cmd("""
            INSERT INTO hr_employees (id,employee_code,user_id,username,full_name,department_id,status,position)
            VALUES (@id,@code,@uid,@u,@u,@dept,'Active','Nhân viên')
            """).With("@id", Guid.NewGuid()).With("@code", "CA" + Guid.NewGuid().ToString("N")[..8])
            .With("@uid", userId).With("@u", username).With("@dept", dept).ExecuteNonQueryAsync();

        var token = tokens.CreateToken(
            new UserDto(userId, username, username, "", AppRoles.Accounting, true, "Approved", DateTime.UtcNow),
            "app:calias:" + Guid.NewGuid().ToString("N")[..16]);
        return new World(suffix, customerId, productId, productName, token);
    }

    private HttpClient Client(string token)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    private sealed record World(string Suffix, Guid CustomerId, Guid ProductId, string ProductName, string Token);
}
