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
/// Mua hàng — nhà cung cấp + phiếu nhập mua (vế NHẬP mà hệ thống chưa từng có).
///
/// Những chỗ đáng canh: số "còn nợ nhà cung cấp" phải là tổng phiếu chưa hủy trừ đã trả (hủy phiếu
/// mà tiền còn treo là đòi nhầm), sửa phiếu phải THAY dòng chứ không cộng dồn, và mã hàng phải tự
/// gắn theo tên — nếu chiều mua và chiều bán gọi tên khác nhau thì tồn kho sau này không cộng đúng.
/// </summary>
[Collection(ApiCollection.Name)]
public sealed class PurchaseTests
{
    private readonly ApiFactory _factory;
    public PurchaseTests(ApiFactory factory) => _factory = factory;

    [Fact]
    public async Task CreatingAPurchase_ComputesTheTotal_AndLinksCatalogItemsByName()
    {
        var world = await SetupAsync();
        using var accountant = Client(world.AccountantToken);

        // Mặt hàng đã có trong danh mục ⇒ dòng phiếu nhập phải tự mang mã, dù người nhập gõ tay.
        var productName = "Thép tấm " + world.Suffix;
        await accountant.PostAsJsonAsync("/api/products", new { name = productName, spec = "10mm" });

        var created = await accountant.PostAsJsonAsync("/api/purchases", new
        {
            supplierId = world.SupplierId,
            supplierInvoiceNo = "HD" + world.Suffix,
            paidAmount = 0m,
            lines = new object[]
            {
                new { lineContent = productName.ToUpperInvariant(), spec = "10MM", quantity = 1_000m, unitPrice = 11_000m, note = "" },
                new { lineContent = "Hàng lạ " + world.Suffix, spec = "", quantity = 5m, unitPrice = 100_000m, note = "" },
            },
        });
        Assert.Equal(HttpStatusCode.OK, created.StatusCode);
        var body = await created.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(11_000_000m + 500_000m, body.GetProperty("total").GetDecimal());
        Assert.StartsWith("PN", body.GetProperty("voucherNo").GetString());

        var purchaseId = body.GetProperty("id").GetGuid();
        Assert.NotNull(await LineProductIdAsync(purchaseId, 1));
        Assert.Null(await LineProductIdAsync(purchaseId, 2));

        // Giá mua vào hiện ngay trong danh mục — nửa còn lại để sau này tính giá vốn.
        var products = await accountant.GetFromJsonAsync<JsonElement>($"/api/products?q={productName}");
        var product = products.GetProperty("items").EnumerateArray().Single();
        Assert.Equal(1_000m, product.GetProperty("boughtQuantity").GetDecimal());
        Assert.Equal(11_000m, product.GetProperty("lastCost").GetDecimal());
    }

    /// <summary>Còn nợ = tổng phiếu chưa hủy − đã trả. Hủy phiếu phải nhả tiền nợ ra ngay.</summary>
    [Fact]
    public async Task TheSupplierBalance_IsWhatIsStillOwed_AndCancellingAVoucherReleasesIt()
    {
        var world = await SetupAsync();
        using var accountant = Client(world.AccountantToken);

        var first = await CreatePurchaseAsync(accountant, world, quantity: 100m, price: 10_000m, paid: 400_000m);
        await CreatePurchaseAsync(accountant, world, quantity: 50m, price: 20_000m, paid: 0m);

        // 1.000.000 − 400.000 + 1.000.000 = 1.600.000
        Assert.Equal(1_600_000m, await SupplierBalanceAsync(accountant, world.SupplierId));

        var cancelled = await accountant.PutAsJsonAsync($"/api/purchases/{first}/cancel",
            new { reason = "Nhập nhầm nhà cung cấp" });
        Assert.Equal(HttpStatusCode.NoContent, cancelled.StatusCode);

        // Phiếu đã hủy không còn tính nợ, nhưng vẫn nằm trong sổ.
        Assert.Equal(1_000_000m, await SupplierBalanceAsync(accountant, world.SupplierId));
        var list = await accountant.GetFromJsonAsync<JsonElement>($"/api/purchases?supplierId={world.SupplierId}");
        Assert.Contains(list.GetProperty("items").EnumerateArray(),
            x => x.GetProperty("id").GetGuid() == first && x.GetProperty("cancelledAt").ValueKind != JsonValueKind.Null);

        // Hủy hai lần không được cộng dồn.
        var again = await accountant.PutAsJsonAsync($"/api/purchases/{first}/cancel", new { reason = "Lại hủy" });
        Assert.Equal(HttpStatusCode.Conflict, again.StatusCode);
    }

    [Fact]
    public async Task PayingMoreThanTheVoucherIsWorth_IsRefused()
    {
        var world = await SetupAsync();
        using var accountant = Client(world.AccountantToken);

        var response = await accountant.PostAsJsonAsync("/api/purchases", new
        {
            supplierId = world.SupplierId,
            paidAmount = 5_000_000m,
            lines = new[] { new { lineContent = "Thép ống", spec = "D60", quantity = 10m, unitPrice = 100_000m, note = "" } },
        });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("không được lớn hơn", await MessageAsync(response));
        Assert.Equal(0, await PurchaseCountAsync(world.SupplierId));
    }

    /// <summary>Sửa phiếu là THAY dòng, không cộng dồn — nếu không sửa hai lần là hàng nhân đôi.</summary>
    [Fact]
    public async Task EditingAPurchase_ReplacesItsLines_InsteadOfAppending()
    {
        var world = await SetupAsync();
        using var accountant = Client(world.AccountantToken);
        var purchaseId = await CreatePurchaseAsync(accountant, world, quantity: 100m, price: 10_000m, paid: 0m);

        var edited = await accountant.PutAsJsonAsync($"/api/purchases/{purchaseId}", new
        {
            supplierId = world.SupplierId,
            paidAmount = 0m,
            lines = new[] { new { lineContent = "Tôn cuộn", spec = "0.45", quantity = 20m, unitPrice = 25_000m, note = "" } },
        });
        Assert.Equal(HttpStatusCode.OK, edited.StatusCode);
        Assert.Equal(500_000m, (await edited.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("total").GetDecimal());
        Assert.Equal(1, await LineCountAsync(purchaseId));

        // Phiếu đã hủy thì khóa lại để giữ lịch sử.
        await accountant.PutAsJsonAsync($"/api/purchases/{purchaseId}/cancel", new { reason = "Hủy" });
        var afterCancel = await accountant.PutAsJsonAsync($"/api/purchases/{purchaseId}", new
        {
            supplierId = world.SupplierId,
            paidAmount = 0m,
            lines = new[] { new { lineContent = "Sửa lén", spec = "", quantity = 1m, unitPrice = 1m, note = "" } },
        });
        Assert.Equal(HttpStatusCode.Conflict, afterCancel.StatusCode);
    }

    [Fact]
    public async Task TwoSuppliersWithTheSameName_CannotCoexist()
    {
        var world = await SetupAsync();
        using var accountant = Client(world.AccountantToken);
        var duplicate = await accountant.PostAsJsonAsync("/api/suppliers",
            new { name = world.SupplierName.ToUpperInvariant() });
        Assert.Equal(HttpStatusCode.Conflict, duplicate.StatusCode);
    }

    [Fact]
    public async Task ReadOnlyAccountingRoles_CanBrowsePurchases_ButNotCreateThem()
    {
        var world = await SetupAsync();
        using var executive = Client(world.ExecutiveToken);

        Assert.Equal(HttpStatusCode.OK, (await executive.GetAsync("/api/purchases")).StatusCode);
        var write = await executive.PostAsJsonAsync("/api/purchases", new
        {
            supplierId = world.SupplierId,
            lines = new[] { new { lineContent = "Thép", spec = "", quantity = 1m, unitPrice = 1m, note = "" } },
        });
        Assert.Equal(HttpStatusCode.Forbidden, write.StatusCode);
        Assert.Equal(0, await PurchaseCountAsync(world.SupplierId));
    }

    [Fact]
    public async Task PeopleOutsideAccounting_SeeNeitherSuppliersNorPurchases()
    {
        var world = await SetupAsync();
        using var driver = Client(world.DriverToken);
        Assert.Equal(HttpStatusCode.Forbidden, (await driver.GetAsync("/api/suppliers")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await driver.GetAsync("/api/purchases")).StatusCode);
    }

    // ── Hạ tầng dựng dữ liệu ─────────────────────────────────────────────────────────────────

    private sealed record World(string Suffix, Guid SupplierId, string SupplierName,
        string AccountantToken, string ExecutiveToken, string DriverToken);

    private HttpClient Client(string token)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    private static async Task<string> MessageAsync(HttpResponseMessage response)
    {
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        return body.TryGetProperty("message", out var m) ? m.GetString() ?? "" : "";
    }

    private static async Task<Guid> CreatePurchaseAsync(HttpClient client, World world,
        decimal quantity, decimal price, decimal paid)
    {
        var response = await client.PostAsJsonAsync("/api/purchases", new
        {
            supplierId = world.SupplierId,
            paidAmount = paid,
            lines = new[] { new { lineContent = "Thép tấm", spec = "10mm", quantity, unitPrice = price, note = "" } },
        });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
    }

    private static async Task<decimal> SupplierBalanceAsync(HttpClient client, Guid supplierId)
    {
        var list = await client.GetFromJsonAsync<JsonElement>("/api/suppliers");
        return list.GetProperty("items").EnumerateArray()
            .Single(x => x.GetProperty("id").GetGuid() == supplierId)
            .GetProperty("balance").GetDecimal();
    }

    private async Task<World> SetupAsync()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var dept = Guid.NewGuid();
        var supplierId = Guid.NewGuid();
        var supplierName = "NCC " + suffix;

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<Database>();
        var tokens = scope.ServiceProvider.GetRequiredService<TokenService>();
        await using var conn = await db.OpenAsync();
        await conn.Cmd("INSERT INTO hr_departments (id,code,name,is_accounting) VALUES (@id,@code,@name,TRUE)")
            .With("@id", dept).With("@code", "PU" + suffix[..5]).With("@name", "Kế toán " + suffix)
            .ExecuteNonQueryAsync();
        await conn.Cmd("INSERT INTO suppliers (id,name) VALUES (@id,@name)")
            .With("@id", supplierId).With("@name", supplierName).ExecuteNonQueryAsync();

        return new World(suffix, supplierId, supplierName,
            await MakeUser(conn, tokens, "__pu_acc_" + suffix, AppRoles.Accounting, dept),
            await MakeUser(conn, tokens, "__pu_exe_" + suffix, AppRoles.Executive, dept),
            await MakeUser(conn, tokens, "__pu_drv_" + suffix, AppRoles.Driver, dept));
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
            """).With("@id", Guid.NewGuid()).With("@code", "PU" + Guid.NewGuid().ToString("N")[..8])
            .With("@uid", userId).With("@u", username).With("@dept", departmentId)
            .ExecuteNonQueryAsync();
        return tokens.CreateToken(new UserDto(userId, username, username, "", role, true, "Approved", DateTime.UtcNow),
            "app:pu:" + Guid.NewGuid().ToString("N")[..16]);
    }

    private async Task<Guid?> LineProductIdAsync(Guid purchaseId, int lineNo)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<Database>();
        await using var conn = await db.OpenAsync();
        return await conn.Cmd("SELECT product_id FROM purchase_lines WHERE purchase_id=@id AND line_no=@no")
            .With("@id", purchaseId).With("@no", lineNo).ExecuteScalarAsync() as Guid?;
    }

    private async Task<int> LineCountAsync(Guid purchaseId)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<Database>();
        await using var conn = await db.OpenAsync();
        return Convert.ToInt32(await conn.Cmd("SELECT COUNT(*) FROM purchase_lines WHERE purchase_id=@id")
            .With("@id", purchaseId).ExecuteScalarAsync() ?? 0);
    }

    private async Task<int> PurchaseCountAsync(Guid supplierId)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<Database>();
        await using var conn = await db.OpenAsync();
        return Convert.ToInt32(await conn.Cmd("SELECT COUNT(*) FROM purchases WHERE supplier_id=@id")
            .With("@id", supplierId).ExecuteScalarAsync() ?? 0);
    }
}
