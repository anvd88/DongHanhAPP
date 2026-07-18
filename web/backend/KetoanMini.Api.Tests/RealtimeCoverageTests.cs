using System.Text.RegularExpressions;
using KetoanMini.Api.Realtime;
using Xunit;

namespace KetoanMini.Api.Tests;

/// <summary>
/// Giữ kỷ luật "MỘT ĐƯỜNG" cho realtime: mọi bảng mà API có ghi đều phải phát tín hiệu qua trigger
/// PostgreSQL (DatabaseChangePublisher), trừ các ngoại lệ được kê rõ kèm lý do bên dưới.
///
/// Vì sao cần test này: trước đây mỗi endpoint tự gọi hub, nên thêm bảng mới rất dễ QUÊN báo cho máy
/// khách — màn hình đứng im mà không ai biết, tới khi người dùng phàn nàn. Nay quy tắc chỉ có một:
/// thêm bảng vào danh sách trigger. Ai quên thì test này đỏ ngay, kèm tên bảng.
/// </summary>
public sealed class RealtimeCoverageTests
{
    /// <summary>
    /// Bảng CỐ Ý không có trigger. Thêm vào đây là một quyết định có ý thức — phải kèm lý do.
    /// </summary>
    private static readonly Dictionary<string, string> Exempt = new(StringComparer.Ordinal)
    {
        // Tín hiệu chat phải nhắm ĐÚNG thành viên cuộc trò chuyện, không broadcast cả công ty.
        // Trigger chỉ biết bảng chứ không biết ai là thành viên → dùng ChatEndpoints.NotifyChat.
        ["web_chat_conversations"] = "chat nhắm đúng thành viên qua NotifyChat",
        ["web_chat_members"] = "chat nhắm đúng thành viên qua NotifyChat",
        ["web_chat_messages"] = "chat nhắm đúng thành viên qua NotifyChat",
        ["web_chat_reactions"] = "chat nhắm đúng thành viên qua NotifyChat",

        // Không hiện trên giao diện nào: hạ tầng nội bộ.
        ["hr_device_tokens"] = "token FCM, không hiển thị",
        ["web_call_events"] = "bản ghi tín hiệu WebRTC, không hiển thị",
        ["web_call_history"] = "lịch sử cuộc gọi nằm trong luồng chat đã nhắm riêng",
        ["web_user_preferences"] = "tuỳ chọn riêng của từng người, chính máy đó vừa đặt",
        ["web_login_settings"] = "cấu hình trang đăng nhập, đọc lúc mở trang",
        ["password_recovery_codes"] = "mã dùng một lần trong luồng khôi phục, không hiển thị",
    };

    private static string EndpointsDir()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidate = Path.Combine(dir.FullName, "KetoanMini.Api", "Endpoints");
            if (Directory.Exists(candidate)) return candidate;
            dir = dir.Parent;
        }
        throw new DirectoryNotFoundException("Không tìm thấy thư mục Endpoints để soi mã nguồn.");
    }

    private static HashSet<string> TablesWrittenByApi()
    {
        // Vài chỗ cần chú ý, đừng đơn giản hoá:
        //   • "\bSET\b" — thiếu ranh giới từ thì "SET" khớp luôn 3 ký tự đầu của "setting_value",
        //     làm bảng bị nhận nhầm thành "set".
        //   • "(?<!DO\s)" — bỏ qua "ON CONFLICT ... DO UPDATE SET" của câu UPSERT (bảng thật đã được
        //     bắt ở vế INSERT INTO rồi).
        //   • Bí danh tuỳ chọn — "UPDATE hr_penalties p SET ..." phải ra hr_penalties, không phải "p".
        var pattern = new Regex(
            @"INSERT\s+INTO\s+([a-z_][a-z0-9_]*)" +
            @"|(?<!DO\s)UPDATE\s+([a-z_][a-z0-9_]*)(?:\s+[a-z][a-z0-9_]*)?\s+SET\b" +
            @"|DELETE\s+FROM\s+([a-z_][a-z0-9_]*)",
            RegexOptions.IgnoreCase);
        var tables = new HashSet<string>(StringComparer.Ordinal);
        foreach (var file in Directory.EnumerateFiles(EndpointsDir(), "*.cs"))
            foreach (Match m in pattern.Matches(File.ReadAllText(file)))
            {
                var name = m.Groups[1].Success ? m.Groups[1].Value
                    : m.Groups[2].Success ? m.Groups[2].Value
                    : m.Groups[3].Value;
                tables.Add(name.ToLowerInvariant());
            }
        return tables;
    }

    private static HashSet<string> TablesWithTrigger()
        => new(DatabaseChangePublisher.Watched.Select(w => w.Table), StringComparer.Ordinal);

    [Fact]
    public void EveryWrittenTable_PublishesRealtimeOrIsExplicitlyExempt()
    {
        var written = TablesWrittenByApi();
        var triggered = TablesWithTrigger();

        var missing = written.Where(t => !triggered.Contains(t) && !Exempt.ContainsKey(t))
            .OrderBy(t => t, StringComparer.Ordinal).ToArray();

        Assert.True(missing.Length == 0,
            "Các bảng sau được API ghi nhưng KHÔNG phát tín hiệu realtime. Thêm vào danh sách trigger " +
            "trong DatabaseChangePublisher, hoặc khai báo ngoại lệ kèm lý do trong RealtimeCoverageTests: " +
            string.Join(", ", missing));
    }

    /// <summary>
    /// Ngoại lệ để lâu dễ thành rác: bảng đã bỏ mà vẫn nằm trong danh sách miễn trừ sẽ che mất bảng
    /// mới trùng tên sau này. Bắt danh sách miễn trừ luôn khớp thực tế.
    /// </summary>
    [Fact]
    public void ExemptionList_HasNoStaleEntries()
    {
        var written = TablesWrittenByApi();
        var stale = Exempt.Keys.Where(t => !written.Contains(t))
            .OrderBy(t => t, StringComparer.Ordinal).ToArray();

        Assert.True(stale.Length == 0,
            "Ngoại lệ realtime trỏ tới bảng mà API không còn ghi nữa — hãy xoá khỏi danh sách: " +
            string.Join(", ", stale));
    }

    /// <summary>
    /// Endpoint KHÔNG được tự gọi hub nữa. Chỉ còn bốn ngoại lệ, mỗi cái vì một lý do trigger không
    /// làm thay được: nhắm đúng người (chat, đá thiết bị, trả lời góp ý) hoặc dữ liệu chỉ nằm trong
    /// bộ nhớ (số đo liveness). Thêm lời gọi hub mới ngoài danh sách này là đi ngược "một đường".
    /// </summary>
    [Fact]
    public void EndpointsDoNotPublishRealtimeByHand()
    {
        var allowed = new[]
        {
            ("AuthEndpoints.cs", "kicked"),           // đá thiết bị cũ: nhắm đúng một người
            ("ChatEndpoints.cs", "chat"),              // nhắm đúng thành viên cuộc trò chuyện
            ("FeedbackEndpoints.cs", "feedbackResolved"), // báo riêng người đã gửi góp ý
            ("ChamCongEndpoints.cs", "liveness"),      // số đo trong RAM, không có bảng để gắn trigger
        };

        var offenders = new List<string>();
        foreach (var file in Directory.EnumerateFiles(EndpointsDir(), "*.cs"))
        {
            var name = Path.GetFileName(file);
            foreach (var line in File.ReadAllLines(file))
            {
                if (!line.Contains("Clients.All", StringComparison.Ordinal) &&
                    !line.Contains("Clients.User", StringComparison.Ordinal) &&
                    !line.Contains("Clients.Users", StringComparison.Ordinal)) continue;
                if (allowed.Any(a => a.Item1 == name && line.Contains(a.Item2, StringComparison.Ordinal))) continue;
                offenders.Add($"{name}: {line.Trim()}");
            }
        }

        Assert.True(offenders.Count == 0,
            "Endpoint tự phát tín hiệu realtime thay vì để trigger lo. Hãy thêm bảng vào " +
            "DatabaseChangePublisher: " + string.Join(" | ", offenders));
    }
}
