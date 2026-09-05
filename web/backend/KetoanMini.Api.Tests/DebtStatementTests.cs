using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using KetoanMini.Api.Data;
using KetoanMini.Api.Models;
using KetoanMini.Api.Security;
using KetoanMini.Api.Services;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Xunit;

namespace KetoanMini.Api.Tests;

/// <summary>
/// Công nợ xem theo kỳ.
///
/// Bất biến đắt nhất: dư đầu kỳ. Công nợ là số dư luỹ kế, nên cắt phẳng theo tháng rồi cộng những
/// gì nhìn thấy là ra một con số vô nghĩa — bảng phải bắt đầu từ số mang sang của mọi phát sinh
/// trước đó. Kéo theo đó là bất biến nối kỳ: dư cuối tháng này phải bằng đúng dư đầu tháng sau,
/// nếu không hai tờ giấy gửi khách sẽ chửi nhau.
/// </summary>
[Collection(ApiCollection.Name)]
public sealed class DebtStatementTests
{
    private readonly ApiFactory _factory;
    public DebtStatementTests(ApiFactory factory) => _factory = factory;

    [Fact]
    public async Task LookingAtOneMonth_CarriesTheOlderBalanceForward_AndCountsOnlyThatMonthsMovements()
    {
        var world = await SetupAsync();
        var thisMonth = DateOnly.FromDateTime(DateTime.Today);
        var periodStart = new DateOnly(thisMonth.Year, thisMonth.Month, 1);
        var periodEnd = periodStart.AddMonths(1).AddDays(-1);

        await InsertSaleAsync(world, periodStart.AddDays(-5), 1_000_000m);
        await InsertSaleAsync(world, thisMonth, 500_000m);
        await InsertPaymentAsync(world, thisMonth, 300_000m);

        using var accountant = Client(world.AccountantToken);
        var detail = await accountant.GetFromJsonAsync<JsonElement>(
            $"/api/debts/{world.CustomerId}?from={periodStart:yyyy-MM-dd}&to={periodEnd:yyyy-MM-dd}");
        var summary = detail.GetProperty("summary");

        Assert.Equal(1_000_000m, summary.GetProperty("carriedBalance").GetDecimal());
        Assert.Equal(500_000m, summary.GetProperty("salesTotal").GetDecimal());
        Assert.Equal(300_000m, summary.GetProperty("collectedTotal").GetDecimal());
        Assert.Equal(1_200_000m, summary.GetProperty("balance").GetDecimal());

        // Sổ trả về mới nhất trước, nên dòng mang sang nằm cuối và mang đúng số dư khởi điểm.
        var rows = detail.GetProperty("transactions").EnumerateArray().ToList();
        var carried = rows[^1];
        Assert.Equal("carried", carried.GetProperty("kind").GetString());
        Assert.Equal(1_000_000m, carried.GetProperty("runningBalance").GetDecimal());
        // Phiếu bán cũ nằm ngoài kỳ thì không được liệt kê lại.
        Assert.Equal(3, rows.Count);
    }

    /// <summary>Dư cuối tháng trước phải khớp dư đầu tháng này, nếu không hai bản in sẽ đá nhau.</summary>
    [Fact]
    public async Task TheClosingBalanceOfOneMonth_IsTheOpeningBalanceOfTheNext()
    {
        var world = await SetupAsync();
        var thisMonth = DateOnly.FromDateTime(DateTime.Today);
        var periodStart = new DateOnly(thisMonth.Year, thisMonth.Month, 1);
        var previousStart = periodStart.AddMonths(-1);

        await InsertSaleAsync(world, previousStart.AddDays(3), 800_000m);
        await InsertPaymentAsync(world, previousStart.AddDays(9), 200_000m);
        await InsertSaleAsync(world, thisMonth, 450_000m);

        using var accountant = Client(world.AccountantToken);
        var previous = await accountant.GetFromJsonAsync<JsonElement>(
            $"/api/debts/{world.CustomerId}?from={previousStart:yyyy-MM-dd}&to={periodStart.AddDays(-1):yyyy-MM-dd}");
        var current = await accountant.GetFromJsonAsync<JsonElement>(
            $"/api/debts/{world.CustomerId}?from={periodStart:yyyy-MM-dd}");

        Assert.Equal(600_000m, previous.GetProperty("summary").GetProperty("balance").GetDecimal());
        Assert.Equal(600_000m, current.GetProperty("summary").GetProperty("carriedBalance").GetDecimal());
        Assert.Equal(1_050_000m, current.GetProperty("summary").GetProperty("balance").GetDecimal());
    }

    /// <summary>Không truyền kỳ thì sổ phải giữ nguyên cách đọc cũ: không có số mang sang.</summary>
    [Fact]
    public async Task WithoutAPeriod_TheLedgerReadsExactlyAsItDidBefore()
    {
        var world = await SetupAsync();
        var today = DateOnly.FromDateTime(DateTime.Today);
        await InsertSaleAsync(world, today.AddDays(-40), 700_000m);
        await InsertSaleAsync(world, today, 300_000m);

        using var accountant = Client(world.AccountantToken);
        var detail = await accountant.GetFromJsonAsync<JsonElement>($"/api/debts/{world.CustomerId}");
        var summary = detail.GetProperty("summary");

        Assert.Equal(0m, summary.GetProperty("carriedBalance").GetDecimal());
        Assert.Equal(1_000_000m, summary.GetProperty("salesTotal").GetDecimal());
        Assert.Equal(1_000_000m, summary.GetProperty("balance").GetDecimal());
        Assert.Equal(2, detail.GetProperty("transactions").GetArrayLength());
    }

    /// <summary>Danh sách công nợ và sổ chi tiết của cùng một khách phải nói cùng một con số.</summary>
    [Fact]
    public async Task TheDebtListAndTheCustomerLedger_AgreeOnThePeriodFigures()
    {
        var world = await SetupAsync();
        var thisMonth = DateOnly.FromDateTime(DateTime.Today);
        var periodStart = new DateOnly(thisMonth.Year, thisMonth.Month, 1);
        var periodEnd = periodStart.AddMonths(1).AddDays(-1);

        await InsertSaleAsync(world, periodStart.AddDays(-20), 2_000_000m);
        await InsertSaleAsync(world, thisMonth, 750_000m);
        await InsertPaymentAsync(world, thisMonth, 1_250_000m);

        using var accountant = Client(world.AccountantToken);
        var query = $"from={periodStart:yyyy-MM-dd}&to={periodEnd:yyyy-MM-dd}";
        var overview = await accountant.GetFromJsonAsync<JsonElement>($"/api/debts?{query}");
        var row = overview.GetProperty("customers").EnumerateArray()
            .Single(x => x.GetProperty("customer").GetProperty("id").GetGuid() == world.CustomerId);
        var detail = await accountant.GetFromJsonAsync<JsonElement>($"/api/debts/{world.CustomerId}?{query}");
        var summary = detail.GetProperty("summary");

        foreach (var field in new[] { "carriedBalance", "salesTotal", "collectedTotal", "balance" })
            Assert.Equal(summary.GetProperty(field).GetDecimal(), row.GetProperty(field).GetDecimal());
        Assert.Equal(2_000_000m, row.GetProperty("carriedBalance").GetDecimal());
        Assert.Equal(1_500_000m, row.GetProperty("balance").GetDecimal());
    }

    [Fact]
    public async Task TheStatementEndpoint_ReturnsAPrintablePdfNamedAfterTheCustomer()
    {
        var world = await SetupAsync();
        var today = DateOnly.FromDateTime(DateTime.Today);
        await InsertSaleAsync(world, today, 640_000m);

        using var accountant = Client(world.AccountantToken);
        var response = await accountant.GetAsync($"/api/debts/{world.CustomerId}/statement.pdf");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/pdf", response.Content.Headers.ContentType?.MediaType);
        var bytes = await response.Content.ReadAsByteArrayAsync();
        Assert.StartsWith("%PDF-", Encoding.ASCII.GetString(bytes, 0, 5));
        Assert.True(bytes.Length > 2000, $"Tệp PDF chỉ có {bytes.Length} byte, nhiều khả năng rỗng.");
    }

    /// <summary>Tên tệp phải bỏ dấu: trình duyệt và máy in mạng hay làm hỏng tên có dấu tiếng Việt.</summary>
    [Fact]
    public void TheDownloadFileName_DropsVietnameseDiacritics()
    {
        var customer = new CustomerDto(Guid.NewGuid(), "Công ty Đầu tư Hà Nội", "", "", "", true);
        var summary = new DebtSummaryDto(customer, 0, null, "", 0, 0, 0, 0, 0, null, 0);
        var detail = new DebtDetailDto(customer, summary, [],
            new DateOnly(2026, 9, 1), new DateOnly(2026, 9, 30));

        Assert.Equal("Cong-no-Cong-ty-Dau-tu-Ha-Noi-20260901-20260930.pdf", DebtStatementPdf.FileName(detail));
    }

    private async Task<World> SetupAsync()
    {
        var suffix = Guid.NewGuid().ToString("N")[..10];
        var dept = Guid.NewGuid();
        var customerId = Guid.NewGuid();
        var customerName = "Khách nợ " + suffix;

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<Database>();
        var tokens = scope.ServiceProvider.GetRequiredService<TokenService>();
        await using var conn = await db.OpenAsync();
        await conn.Cmd("INSERT INTO hr_departments (id,code,name,is_accounting) VALUES (@id,@code,@name,TRUE)")
            .With("@id", dept).With("@code", "DS" + suffix[..5]).With("@name", "Kế toán " + suffix)
            .ExecuteNonQueryAsync();
        await conn.Cmd("INSERT INTO customers (id,name,is_active) VALUES (@id,@name,TRUE)")
            .With("@id", customerId).With("@name", customerName).ExecuteNonQueryAsync();

        var userId = Guid.NewGuid();
        var username = "__debt_acc_" + suffix;
        await conn.Cmd("""
            INSERT INTO app_users (id,username,full_name,email,role,password_hash,is_active,
                approval_status,approved_at,approved_by,created_at,is_deleted)
            VALUES (@id,@u,@u,'',@role,@hash,TRUE,'Approved',CURRENT_TIMESTAMP,'test',CURRENT_TIMESTAMP,FALSE)
            """).With("@id", userId).With("@u", username).With("@role", AppRoles.Accounting)
            .With("@hash", PasswordHasher.Hash("test-pass")).ExecuteNonQueryAsync();
        await conn.Cmd("""
            INSERT INTO hr_employees (id,employee_code,user_id,username,full_name,department_id,status,position)
            VALUES (@id,@code,@uid,@u,@u,@dept,'Active','Nhân viên')
            """).With("@id", Guid.NewGuid()).With("@code", "DS" + Guid.NewGuid().ToString("N")[..8])
            .With("@uid", userId).With("@u", username).With("@dept", dept).ExecuteNonQueryAsync();

        var token = tokens.CreateToken(
            new UserDto(userId, username, username, "", AppRoles.Accounting, true, "Approved", DateTime.UtcNow),
            "app:debt:" + Guid.NewGuid().ToString("N")[..16]);
        return new World(customerId, customerName, token);
    }

    private async Task InsertSaleAsync(World world, DateOnly date, decimal amount)
    {
        var id = Guid.NewGuid();
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<Database>();
        await using var conn = await db.OpenAsync();
        await conn.Cmd("""
            INSERT INTO documents (id,voucher_no,doc_date,customer_id,customer_name,document_type,
                content,issued_at)
            VALUES (@id,@no,@date,@cid,@cname,'document','Bán hàng',CURRENT_TIMESTAMP)
            """)
            .With("@id", id).With("@no", "PX" + Guid.NewGuid().ToString("N")[..10]).With("@date", date)
            .With("@cid", world.CustomerId).With("@cname", world.CustomerName).ExecuteNonQueryAsync();
        await conn.Cmd("""
            INSERT INTO document_lines (document_id,line_no,line_content,spec,quantity,unit_price,note)
            VALUES (@id,1,'Thép tấm','10mm',1,@amount,'')
            """).With("@id", id).With("@amount", amount).ExecuteNonQueryAsync();
    }

    private async Task InsertPaymentAsync(World world, DateOnly date, decimal amount)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<Database>();
        await using var conn = await db.OpenAsync();
        await conn.Cmd("""
            INSERT INTO payments (id,customer_id,customer_name,amount,pay_date,note)
            VALUES (@id,@cid,@cname,@amount,@date,'Khách trả tiền')
            """).With("@id", Guid.NewGuid()).With("@cid", world.CustomerId)
            .With("@cname", world.CustomerName).With("@amount", amount).With("@date", date)
            .ExecuteNonQueryAsync();
    }

    private HttpClient Client(string token)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    private sealed record World(Guid CustomerId, string CustomerName, string AccountantToken);
}
