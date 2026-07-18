using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using KetoanMini.Api.Data;
using KetoanMini.Api.Models;
using KetoanMini.Api.Security;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace KetoanMini.Api.Tests;

/// <summary>
/// Kiểm thử tích hợp Trung tâm trợ giúp /api/help — Đợt 7, nhiệm vụ 22:
///  • Admin biên tập FAQ; mọi người xem mục đã xuất bản; nhân viên không tạo được (403).
///  • Endpoint tình trạng dịch vụ báo DB ok.
/// </summary>
[Collection(ApiCollection.Name)]
public sealed class HelpTests : IAsyncLifetime
{
    private readonly ApiFactory _factory;
    private const string Admin = "__test_help_admin__";
    private const string Emp = "__test_help_emp__";
    private Guid _faqId;

    public HelpTests(ApiFactory factory) => _factory = factory;

    public async Task InitializeAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<Database>();
        await using var conn = await db.OpenAsync();
        await Upsert(conn, Admin, "Admin");
        await Upsert(conn, Emp, "Employee");
    }

    public async Task DisposeAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<Database>();
        await using var conn = await db.OpenAsync();
        if (_faqId != Guid.Empty)
            await conn.Cmd("DELETE FROM help_faqs WHERE id=@id").With("@id", _faqId).ExecuteNonQueryAsync();
        await conn.Cmd("DELETE FROM app_users WHERE username = ANY(@u)").With("@u", new[] { Admin, Emp }).ExecuteNonQueryAsync();
    }

    private static async Task Upsert(Npgsql.NpgsqlConnection conn, string username, string role) =>
        await conn.Cmd(
            @"INSERT INTO app_users
                 (id, username, full_name, email, role, password_hash, is_active, approval_status, approved_at, approved_by, created_at, is_deleted)
              VALUES (@id, @u, @u, '', @role, @ph, TRUE, 'Approved', CURRENT_TIMESTAMP, 'test', CURRENT_TIMESTAMP, FALSE)
              ON CONFLICT (username) DO UPDATE SET is_active=TRUE, is_deleted=FALSE, role=@role, approval_status='Approved'")
            .With("@id", Guid.NewGuid()).With("@u", username).With("@role", role)
            .With("@ph", PasswordHasher.Hash("test-pass")).ExecuteNonQueryAsync();

    private async Task<HttpClient> ClientAsAsync(string username, string role)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<Database>();
        await using var conn = await db.OpenAsync();
        var id = (Guid)(await conn.Cmd("SELECT id FROM app_users WHERE username=@u").With("@u", username).ExecuteScalarAsync())!;
        var tokens = scope.ServiceProvider.GetRequiredService<TokenService>();
        var token = tokens.CreateToken(new UserDto(id, username, username, "", role, true, "Approved", DateTime.UtcNow));
        var client = _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    private sealed record Created(Guid Id);
    private sealed record Faq(Guid Id, string Category, string Question, string Answer, int OrderNo, bool IsPublished);
    private sealed record Status(string Db, DateTime ServerTime);

    [Fact]
    public async Task Help_AdminManagesFaq_EmployeeReadsButCannotWrite()
    {
        var admin = await ClientAsAsync(Admin, "Admin");
        var created = await (await admin.PostAsJsonAsync("/api/help/faqs", new
        {
            category = "Chấm công", question = "Làm sao chấm công offline?", answer = "Vào mục Chấm công → Ngoại tuyến.", isPublished = true,
        })).Content.ReadFromJsonAsync<Created>();
        _faqId = created!.Id;
        Assert.NotEqual(Guid.Empty, _faqId);

        var emp = await ClientAsAsync(Emp, "Employee");
        var faqs = await emp.GetFromJsonAsync<List<Faq>>("/api/help/faqs");
        Assert.Contains(faqs!, f => f.Id == _faqId && f.Question.Contains("offline"));

        // Nhân viên không tạo được FAQ.
        var forbidden = await emp.PostAsJsonAsync("/api/help/faqs", new { question = "x" });
        Assert.Equal(HttpStatusCode.Forbidden, forbidden.StatusCode);

        var status = await emp.GetFromJsonAsync<Status>("/api/help/status");
        Assert.Equal("ok", status!.Db);
    }
}
