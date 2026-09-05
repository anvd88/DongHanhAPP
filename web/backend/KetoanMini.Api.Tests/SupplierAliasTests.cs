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
/// Nhà cung cấp gõ tay và BÍ DANH.
///
/// Hai lỗi cùng một gốc: một nhà cung cấp bị chẻ thành nhiều hồ sơ, hoặc không thành hồ sơ nào.
///   • Gõ tên chưa có mà phiếu vẫn lưu với supplier_id rỗng thì công nợ phải trả không cộng vào ai,
///     và tồn theo nguồn hàng coi như lô hàng đó chưa từng nhập.
///   • Cùng một nơi nhưng mỗi người gọi một kiểu ("Đại Phát", "anh A - Đại Phát") mà không có bí
///     danh thì mỗi cách gõ đẻ ra một hồ sơ, tiền nợ nằm rải rác không cộng lại được.
/// </summary>
[Collection(ApiCollection.Name)]
public sealed class SupplierAliasTests
{
    private readonly ApiFactory _factory;
    public SupplierAliasTests(ApiFactory factory) => _factory = factory;

    [Fact]
    public async Task TypingASupplierNameThatDoesNotExistYet_CreatesTheSupplierAndLinksTheVoucher()
    {
        var world = await SetupAsync();
        var name = "Công ty Vật tư " + world.Suffix;
        using var accountant = Client(world.Token);

        var created = await accountant.PostAsJsonAsync("/api/purchases", NewPurchase(name, null));
        Assert.Equal(HttpStatusCode.OK, created.StatusCode);
        var purchaseId = (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();

        var suppliers = await accountant.GetFromJsonAsync<JsonElement>("/api/suppliers");
        var supplier = suppliers.GetProperty("items").EnumerateArray()
            .Single(x => x.GetProperty("name").GetString() == name);

        // Điểm chốt: phiếu phải TRỎ vào hồ sơ vừa dựng, không chỉ chép cái tên vào ô chữ.
        var detail = await accountant.GetFromJsonAsync<JsonElement>($"/api/purchases/{purchaseId}");
        Assert.Equal(supplier.GetProperty("id").GetGuid(),
            detail.GetProperty("purchase").GetProperty("supplierId").GetGuid());
        Assert.Equal(1, supplier.GetProperty("purchaseCount").GetInt32());
        Assert.Equal(1_500_000m, supplier.GetProperty("balance").GetDecimal());
    }

    [Fact]
    public async Task TypingAnAlias_LandsOnTheExistingSupplier_WithoutCreatingASecondOne()
    {
        var world = await SetupAsync();
        var real = "Công ty TNHH Đại Phát " + world.Suffix;
        var alias = "anh A - Đại Phát " + world.Suffix;
        using var accountant = Client(world.Token);

        var supplierId = await AddSupplierAsync(real);
        var added = await accountant.PostAsJsonAsync($"/api/suppliers/{supplierId}/aliases", new { alias });
        Assert.Equal(HttpStatusCode.OK, added.StatusCode);

        var created = await accountant.PostAsJsonAsync("/api/purchases", NewPurchase(alias, null));
        Assert.Equal(HttpStatusCode.OK, created.StatusCode);
        var purchaseId = (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();

        var detail = await accountant.GetFromJsonAsync<JsonElement>($"/api/purchases/{purchaseId}");
        var purchase = detail.GetProperty("purchase");
        Assert.Equal(supplierId, purchase.GetProperty("supplierId").GetGuid());
        // Phiếu ghi tên trên giấy tờ, không phải chữ vừa gõ.
        Assert.Equal(real, purchase.GetProperty("supplierName").GetString());

        var suppliers = await accountant.GetFromJsonAsync<JsonElement>("/api/suppliers");
        Assert.DoesNotContain(suppliers.GetProperty("items").EnumerateArray(),
            x => x.GetProperty("name").GetString() == alias);
    }

    /// <summary>Một bí danh chỉ được trỏ về một nơi, nếu không lúc gán tự động máy phải đoán.</summary>
    [Fact]
    public async Task AnAliasCannotPointAtTwoSuppliers_NorShadowAnotherSuppliersRealName()
    {
        var world = await SetupAsync();
        var first = await AddSupplierAsync("NCC Một " + world.Suffix);
        var secondName = "NCC Hai " + world.Suffix;
        var second = await AddSupplierAsync(secondName);
        var alias = "kho bên sông " + world.Suffix;
        using var accountant = Client(world.Token);

        Assert.Equal(HttpStatusCode.OK,
            (await accountant.PostAsJsonAsync($"/api/suppliers/{first}/aliases", new { alias })).StatusCode);

        var stolen = await accountant.PostAsJsonAsync($"/api/suppliers/{second}/aliases", new { alias });
        Assert.Equal(HttpStatusCode.Conflict, stolen.StatusCode);

        var shadowing = await accountant.PostAsJsonAsync($"/api/suppliers/{first}/aliases", new { alias = secondName });
        Assert.Equal(HttpStatusCode.Conflict, shadowing.StatusCode);
    }

    [Fact]
    public async Task ListingSuppliers_CarriesTheirAliases_AndFindsThemByAlias()
    {
        var world = await SetupAsync();
        var name = "Thép Hoà Bình " + world.Suffix;
        var alias = "chú Ba thép " + world.Suffix;
        var supplierId = await AddSupplierAsync(name);
        using var accountant = Client(world.Token);
        await accountant.PostAsJsonAsync($"/api/suppliers/{supplierId}/aliases", new { alias });

        var found = await accountant.GetFromJsonAsync<JsonElement>(
            $"/api/suppliers?q={Uri.EscapeDataString("chú Ba thép")}");
        var row = found.GetProperty("items").EnumerateArray()
            .Single(x => x.GetProperty("id").GetGuid() == supplierId);
        Assert.Equal(name, row.GetProperty("name").GetString());
        Assert.Contains(alias, row.GetProperty("aliases").EnumerateArray().Select(x => x.GetString()));

        var aliases = await accountant.GetFromJsonAsync<JsonElement>($"/api/suppliers/{supplierId}/aliases");
        var aliasId = aliases.GetProperty("items").EnumerateArray().Single().GetProperty("id").GetInt64();
        var removed = await accountant.DeleteAsync($"/api/suppliers/{supplierId}/aliases/{aliasId}");
        Assert.Equal(HttpStatusCode.NoContent, removed.StatusCode);
    }

    // --- Dựng dữ liệu ------------------------------------------------------------------------------

    private static object NewPurchase(string supplierName, Guid? supplierId) => new
    {
        voucherNo = "",
        date = DateOnly.FromDateTime(DateTime.Today),
        supplierId,
        supplierName,
        supplierInvoiceNo = "",
        note = "",
        paidAmount = 0m,
        lines = new[]
        {
            new { lineContent = "Thép tấm", spec = "10mm", quantity = 100m, unitPrice = 15_000m, note = "", productId = (Guid?)null },
        },
    };

    private async Task<Guid> AddSupplierAsync(string name)
    {
        var id = Guid.NewGuid();
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<Database>();
        await using var conn = await db.OpenAsync();
        await conn.Cmd("INSERT INTO suppliers (id,name,is_active) VALUES (@id,@name,TRUE)")
            .With("@id", id).With("@name", name).ExecuteNonQueryAsync();
        return id;
    }

    private async Task<World> SetupAsync()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var dept = Guid.NewGuid();
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<Database>();
        var tokens = scope.ServiceProvider.GetRequiredService<TokenService>();
        await using var conn = await db.OpenAsync();
        await conn.Cmd("INSERT INTO hr_departments (id,code,name,is_accounting) VALUES (@id,@code,@name,TRUE)")
            .With("@id", dept).With("@code", "SA" + suffix[..5]).With("@name", "Kế toán " + suffix)
            .ExecuteNonQueryAsync();

        var userId = Guid.NewGuid();
        var username = "__alias_acc_" + suffix;
        await conn.Cmd("""
            INSERT INTO app_users (id,username,full_name,email,role,password_hash,is_active,
                approval_status,approved_at,approved_by,created_at,is_deleted)
            VALUES (@id,@u,@u,'',@role,@hash,TRUE,'Approved',CURRENT_TIMESTAMP,'test',CURRENT_TIMESTAMP,FALSE)
            """).With("@id", userId).With("@u", username).With("@role", AppRoles.Accounting)
            .With("@hash", PasswordHasher.Hash("test-pass")).ExecuteNonQueryAsync();
        await conn.Cmd("""
            INSERT INTO hr_employees (id,employee_code,user_id,username,full_name,department_id,status,position)
            VALUES (@id,@code,@uid,@u,@u,@dept,'Active','Nhân viên')
            """).With("@id", Guid.NewGuid()).With("@code", "SA" + Guid.NewGuid().ToString("N")[..8])
            .With("@uid", userId).With("@u", username).With("@dept", dept).ExecuteNonQueryAsync();

        var token = tokens.CreateToken(
            new UserDto(userId, username, username, "", AppRoles.Accounting, true, "Approved", DateTime.UtcNow),
            "app:alias:" + Guid.NewGuid().ToString("N")[..16]);
        return new World(suffix, token);
    }

    private HttpClient Client(string token)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    private sealed record World(string Suffix, string Token);
}
