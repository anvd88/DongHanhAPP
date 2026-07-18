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
/// Kiểm thử tích hợp Khảo sát /api/surveys — Đợt 6, nhiệm vụ 20:
///  • Admin tạo khảo sát; nhân viên gửi phản hồi.
///  • Khảo sát một-lần: gửi lần 2 bị chặn (409) — chống trùng.
///  • Kết quả tổng hợp chỉ ở dạng số đếm (ẩn danh, không kèm người trả lời).
/// </summary>
[Collection(ApiCollection.Name)]
public sealed class SurveyTests : IAsyncLifetime
{
    private readonly ApiFactory _factory;
    private const string Admin = "__test_survey_admin__";
    private const string Emp = "__test_survey_emp__";
    private Guid _surveyId;

    public SurveyTests(ApiFactory factory) => _factory = factory;

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
        if (_surveyId != Guid.Empty)
            await conn.Cmd("DELETE FROM surveys WHERE id=@id").With("@id", _surveyId).ExecuteNonQueryAsync();
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

    private sealed record CreatedSurvey(Guid Id);
    private sealed record SurveyQ(Guid Id, string Question, string Qtype, string[] Options, bool Required);
    private sealed record SurveyDetail(object Survey, List<SurveyQ> Questions);
    private sealed record ResultItem(Guid QuestionId, string Question, string Qtype, string[] Options, int[] OptionCounts, List<string> Texts, double? RatingAvg);
    private sealed record SurveyResults(int Total, List<ResultItem> Results);

    [Fact]
    public async Task Survey_Create_Respond_DedupAndAggregate()
    {
        var admin = await ClientAsAsync(Admin, "Admin");

        // 1) Admin tạo khảo sát một-lần, câu hỏi chọn 1 trong 2.
        var created = await (await admin.PostAsJsonAsync("/api/surveys", new
        {
            title = "Mức hài lòng canteen",
            isAnonymous = true,
            allowMultiple = false,
            questions = new[] { new { question = "Bạn thấy sao?", qtype = "single", options = new[] { "Tốt", "Tệ" }, required = true } },
        })).Content.ReadFromJsonAsync<CreatedSurvey>();
        _surveyId = created!.Id;
        Assert.NotEqual(Guid.Empty, _surveyId);

        // Lấy questionId để trả lời.
        var detail = await admin.GetFromJsonAsync<SurveyDetail>($"/api/surveys/{_surveyId}");
        var qid = detail!.Questions.Single().Id;

        // 2) Nhân viên gửi phản hồi lần đầu → OK.
        var emp = await ClientAsAsync(Emp, "Employee");
        var first = await emp.PostAsJsonAsync($"/api/surveys/{_surveyId}/respond", new
        {
            answers = new[] { new { questionId = qid, optionIndices = new[] { 0 } } },
        });
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);

        // 3) Gửi lần 2 → 409 (chống trùng cho khảo sát một-lần).
        var second = await emp.PostAsJsonAsync($"/api/surveys/{_surveyId}/respond", new
        {
            answers = new[] { new { questionId = qid, optionIndices = new[] { 0 } } },
        });
        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);

        // 4) Kết quả tổng hợp: 1 phản hồi, 1 phiếu cho "Tốt" (index 0).
        var results = await admin.GetFromJsonAsync<SurveyResults>($"/api/surveys/{_surveyId}/results");
        Assert.Equal(1, results!.Total);
        var item = results.Results.Single();
        Assert.Equal(1, item.OptionCounts[0]);
        Assert.Equal(0, item.OptionCounts[1]);
    }
}
