using System.Net.Http.Headers;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using KetoanMini.Api.Data;
using KetoanMini.Api.Models;
using KetoanMini.Api.Security;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace KetoanMini.Api.Tests;

/// <summary>
/// HỢP ĐỒNG RESPONSE — thứ mà OpenAPI KHÔNG mô tả được.
///
/// Vì sao cần: handler minimal API phần lớn trả <c>Results.Ok(new { … })</c> (đối tượng ẩn danh) nên
/// ApiExplorer không suy ra được kiểu. Đo trên chính spec sinh từ ứng dụng đang chạy: 396 operation,
/// 172 có mô tả request body, <b>0</b> có mô tả response. Nghĩa là hình dạng JSON trả về hiện chỉ tồn
/// tại trong mã C#. Viết lại backend bằng ngôn ngữ khác mà không có tài liệu này thì bản mới có thể
/// "đúng nghiệp vụ" nhưng vẫn làm trắng màn hình web và crash app.
///
/// Cách làm: gọi thật từng endpoint bằng nhiều vai trò rồi ghi lại <b>BỘ KHUNG</b> của phản hồi —
/// đường dẫn từng trường kèm KIỂU, không kèm giá trị. Chọn khung thay vì ảnh chụp nguyên văn là có
/// chủ ý: giá trị đổi theo dữ liệu trong CSDL test (và các lớp test khác cùng chạy), còn khung thì
/// ổn định — mà khung mới đúng là thứ người viết backend mới cần.
///
/// Hai việc test này làm:
///   1. SINH tệp trong <c>golden/</c> — đây là tài liệu bàn giao, commit vào repo.
///   2. CHỐNG TRÔI: mọi trường đã ghi trong tệp gốc phải còn nguyên và đúng kiểu ở lần chạy sau.
///      Trường MỚI xuất hiện thì không coi là lỗi (thêm dữ liệu là chuyện bình thường); trường BIẾN
///      MẤT hoặc ĐỔI KIỂU mới là hỏng hợp đồng.
///
/// Chạy lại từ đầu (sau khi cố ý đổi API): đặt biến môi trường <c>GOLDEN_UPDATE=1</c>.
///
/// PHẠM VI HIỆN TẠI: các endpoint <b>GET không có tham số đường dẫn</b>. Nhóm POST/PUT/DELETE và các
/// GET có <c>{id}</c> cần dữ liệu dựng sẵn theo từng nghiệp vụ — mở rộng bằng cách cho các lớp test
/// nghiệp vụ sẵn có gọi <see cref="GoldenSkeleton.Describe"/> rồi ghi thêm vào cùng thư mục.
/// </summary>
[Collection(ApiCollection.Name)]
public sealed class GoldenContractTests : IAsyncLifetime
{
    private readonly ApiFactory _factory;
    private readonly Dictionary<string, string> _tokens = new(StringComparer.Ordinal);
    private readonly List<string> _seededUsers = [];

    /// <summary>Vai trò dùng để quét. Mỗi vai trò cho một lát cắt phân quyền khác nhau của cùng endpoint.</summary>
    private static readonly string[] Roles =
        [AppRoles.Admin, AppRoles.Accounting, AppRoles.Hr, AppRoles.Driver, AppRoles.Employee];

    public GoldenContractTests(ApiFactory factory) => _factory = factory;

    private static string GoldenDirectory([CallerFilePath] string thisFile = "")
        => Path.Combine(Path.GetDirectoryName(thisFile)!, "golden");

    public async Task InitializeAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<Database>();
        var tokens = scope.ServiceProvider.GetRequiredService<TokenService>();
        await using var conn = await db.OpenAsync();

        foreach (var role in Roles)
        {
            var username = $"__golden_{role.ToLowerInvariant()}_{Guid.NewGuid():N}__";
            var id = (Guid)(await conn.Cmd(
                @"INSERT INTO app_users
                     (id, username, full_name, email, role, password_hash, is_active,
                      approval_status, approved_at, approved_by, created_at, is_deleted)
                  VALUES (@id, @u, @name, '', @role, @ph, TRUE, 'Approved',
                          CURRENT_TIMESTAMP, 'golden', CURRENT_TIMESTAMP, FALSE)
                  RETURNING id")
                .With("@id", Guid.NewGuid()).With("@u", username)
                .With("@name", $"Golden {role}").With("@role", role)
                .With("@ph", PasswordHasher.Hash($"golden-{Guid.NewGuid():N}"))
                .ExecuteScalarAsync())!;

            _seededUsers.Add(username);
            var employeeId = await KetoanMini.Api.Endpoints.HrEndpoints.EnsureEmployeeForUser(conn, username);
            await conn.Cmd("UPDATE hr_employees SET avatar=@avatar WHERE id=@id")
                .With("@id", employeeId)
                .With("@avatar", "data:image/png;base64,iVBORw0KGgo=")
                .ExecuteNonQueryAsync();
            _tokens[role] = tokens.CreateToken(
                new UserDto(id, username, $"Golden {role}", "", role, true, "Approved", DateTime.UtcNow));
        }
    }

    public async Task DisposeAsync()
    {
        try
        {
            using var scope = _factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<Database>();
            await using var conn = await db.OpenAsync();
            foreach (var u in _seededUsers)
            {
                await conn.Cmd("DELETE FROM hr_employees WHERE username = @u").With("@u", u)
                    .ExecuteNonQueryAsync();
                await conn.Cmd("DELETE FROM app_users WHERE username = @u").With("@u", u)
                    .ExecuteNonQueryAsync();
            }
        }
        catch { /* dọn dẹp best-effort, không để lỗi cleanup che lỗi thật của test */ }
    }

    /// <summary>GET không tham số đường dẫn — quét tự động được mà không cần dựng dữ liệu trước.</summary>
    private IReadOnlyList<string> ParameterlessGetRoutes()
        => _factory.Services.GetRequiredService<EndpointDataSource>().Endpoints
            .OfType<RouteEndpoint>()
            .Where(e => e.RoutePattern.RawText?.StartsWith("/api/", StringComparison.OrdinalIgnoreCase) == true)
            .Where(e => !e.RoutePattern.RawText!.Contains('{'))
            .Where(e => e.Metadata.GetMetadata<HttpMethodMetadata>()?.HttpMethods.Contains("GET") == true)
            .Select(e => e.RoutePattern.RawText!)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(x => x, StringComparer.Ordinal)
            .ToList();

    [Fact]
    public async Task ResponseShapes_AreCapturedAndDoNotDrift()
    {
        var directory = GoldenDirectory();
        Directory.CreateDirectory(directory);
        var refresh = Environment.GetEnvironmentVariable("GOLDEN_UPDATE") == "1";

        var routes = ParameterlessGetRoutes();
        Assert.True(routes.Count > 40,
            $"Chỉ tìm thấy {routes.Count} endpoint GET không tham số — nghi ngờ bộ lọc route hỏng.");

        var serverErrors = new List<string>();
        var drift = new List<string>();

        foreach (var route in routes)
        {
            var captured = new StringBuilder();
            captured.AppendLine($"# {route}");
            captured.AppendLine("# Khung phản hồi theo vai trò. Sinh bởi GoldenContractTests; đừng sửa tay.");

            foreach (var role in Roles)
            {
                using var client = _factory.CreateClient();
                client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _tokens[role]);

                using var response = await client.GetAsync(route);
                var status = (int)response.StatusCode;
                var mediaType = response.Content.Headers.ContentType?.MediaType ?? "";

                // 5xx trên một GET đơn thuần là lỗi thật, không phải chuyện phân quyền. Chính loại quét
                // này đã lộ ra /swagger/v1/swagger.json trả 500 suốt nhiều tháng mà không ai biết.
                // Kèm luôn dòng đầu của thân phản hồi: fixture chạy ở Development nên trang lỗi mang
                // theo tên ngoại lệ — báo cáo có nó thì khỏi phải dò lại bằng tay.
                if (status >= 500)
                {
                    var raw = await response.Content.ReadAsStringAsync();
                    var firstLine = raw.Split('\n').FirstOrDefault(l => l.Trim().Length > 0)?.Trim() ?? "";
                    if (firstLine.Length > 220) firstLine = firstLine[..220];
                    serverErrors.Add($"{route} [{role}] → {status}  {firstLine}");

                    // Toàn văn (fixture chạy ở Development nên có stack trace) ra tệp riêng: thông báo
                    // test giữ ngắn để đọc được, còn người sửa vẫn có đủ dấu vết để lần ra dòng lỗi.
                    var errorDir = Path.Combine(GoldenDirectory(), "_errors");
                    Directory.CreateDirectory(errorDir);
                    await File.WriteAllTextAsync(
                        Path.Combine(errorDir, $"{Sanitize(route)}.{role}.txt"), raw, new UTF8Encoding(false));
                }

                captured.AppendLine();
                captured.AppendLine($"[{role}] {status} {mediaType}");

                if (mediaType == "application/json")
                {
                    var body = await response.Content.ReadAsStringAsync();
                    foreach (var line in GoldenSkeleton.Describe(body))
                        captured.AppendLine("  " + line);
                }
                else if (mediaType.Length > 0)
                {
                    captured.AppendLine("  (thân phản hồi không phải JSON)");
                }
            }

            var file = Path.Combine(directory, Sanitize(route) + ".txt");
            var current = captured.ToString().ReplaceLineEndings("\n");

            if (refresh || !File.Exists(file))
            {
                await File.WriteAllTextAsync(file, current, new UTF8Encoding(false));
                continue;
            }

            var baseline = (await File.ReadAllTextAsync(file)).ReplaceLineEndings("\n");
            drift.AddRange(GoldenSkeleton.Regressions(baseline, current).Select(d => $"{route}: {d}"));
        }

        Assert.True(serverErrors.Count == 0,
            "GET trả lỗi máy chủ (5xx):\n  " + string.Join("\n  ", serverErrors));

        Assert.True(drift.Count == 0,
            "Hợp đồng response đã thay đổi — trường biến mất hoặc đổi kiểu so với tệp trong golden/.\n" +
            "Nếu đây là thay đổi CÓ CHỦ Ý thì chạy lại với GOLDEN_UPDATE=1 và commit tệp mới.\n  "
            + string.Join("\n  ", drift));
    }

    private static string Sanitize(string route)
        => route.Trim('/').Replace('/', '_').Replace(':', '-');
}

/// <summary>
/// Rút một phản hồi JSON thành danh sách "đường dẫn : kiểu" đã sắp xếp.
/// Mảng được GỘP khung của mọi phần tử (hợp các khoá) — mảng một phần tử không được phép báo thiếu
/// những trường mà phần tử khác có.
/// </summary>
public static class GoldenSkeleton
{
    public static IReadOnlyList<string> Describe(string json)
    {
        var shape = new SortedDictionary<string, SortedSet<string>>(StringComparer.Ordinal);
        try
        {
            using var doc = JsonDocument.Parse(json);
            Walk("$", doc.RootElement, shape);
        }
        catch (JsonException)
        {
            return ["(thân phản hồi không phải JSON hợp lệ)"];
        }

        return shape.Select(kv => $"{kv.Key} : {string.Join("|", kv.Value)}").ToList();
    }

    private static void Walk(string path, JsonElement el, SortedDictionary<string, SortedSet<string>> shape)
    {
        switch (el.ValueKind)
        {
            case JsonValueKind.Object:
                Record(path, "object", shape);
                foreach (var p in el.EnumerateObject())
                    Walk($"{path}.{p.Name}", p.Value, shape);
                break;

            case JsonValueKind.Array:
                Record(path, "array", shape);
                foreach (var item in el.EnumerateArray())
                    Walk($"{path}[]", item, shape);
                break;

            case JsonValueKind.String: Record(path, "string", shape); break;
            case JsonValueKind.Number: Record(path, "number", shape); break;
            case JsonValueKind.True:
            case JsonValueKind.False: Record(path, "bool", shape); break;
            // Serializer đặt DefaultIgnoreCondition = WhenWritingNull nên null hầu như không xuất hiện;
            // gặp thì vẫn ghi lại để bên viết lại biết trường này có thể null tường minh.
            default: Record(path, "null", shape); break;
        }
    }

    private static void Record(string path, string type, SortedDictionary<string, SortedSet<string>> shape)
    {
        if (!shape.TryGetValue(path, out var types))
            shape[path] = types = new SortedSet<string>(StringComparer.Ordinal);
        types.Add(type);
    }

    /// <summary>
    /// So khung mới với khung gốc. CHỈ báo lỗi khi một dòng đã ghi trong gốc biến mất — tức trường bị
    /// xoá, đổi tên hoặc đổi kiểu. Dòng mới xuất hiện KHÔNG bị coi là lỗi: thêm trường là chuyện bình
    /// thường, và mảng rỗng trong lần chạy này không được phép làm đỏ một trường vốn có thật.
    /// </summary>
    public static IReadOnlyList<string> Regressions(string baseline, string current)
    {
        static HashSet<string> Fields(string text) =>
            new(text.Split('\n')
                    .Select(l => l.Trim())
                    .Where(l => l.Contains(" : ") && l.StartsWith('$')),
                StringComparer.Ordinal);

        // Mảng rỗng ở lần chạy này: không có phần tử nào để mô tả, nên mọi trường con "vắng mặt" là do
        // DỮ LIỆU chứ không phải do hợp đồng đổi. Bỏ qua nhánh đó thay vì báo đỏ oan.
        var currentFields = Fields(current);
        var emptyArrays = currentFields
            .Where(f => f.EndsWith(" : array", StringComparison.Ordinal))
            .Select(f => f[..f.IndexOf(" : ", StringComparison.Ordinal)] + "[]")
            .Where(prefix => !currentFields.Any(f => f.StartsWith(prefix, StringComparison.Ordinal)))
            .ToArray();

        return Fields(baseline)
            .Where(f => !currentFields.Contains(f))
            .Where(f => !emptyArrays.Any(prefix => f.StartsWith(prefix, StringComparison.Ordinal)))
            .OrderBy(f => f, StringComparer.Ordinal)
            .ToList();
    }
}
