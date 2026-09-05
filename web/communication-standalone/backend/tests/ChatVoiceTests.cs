using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using KetoanMini.Api.Data;
using KetoanMini.Api.Endpoints;
using KetoanMini.Api.Models;
using KetoanMini.Api.Security;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace KetoanMini.Api.Tests;

[Collection(ApiCollection.Name)]
public sealed class ChatVoiceTests : IAsyncLifetime
{
    private const string Sender = "__test_voice_sender__";
    private const string Recipient = "__test_voice_recipient__";
    private readonly ApiFactory _factory;
    private readonly Guid _conversationId = Guid.NewGuid();
    private long _messageId;

    public ChatVoiceTests(ApiFactory factory) => _factory = factory;

    public async Task InitializeAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<Database>();
        await using var conn = await db.OpenAsync();
        await UpsertUser(conn, Sender);
        await UpsertUser(conn, Recipient);
        await conn.Cmd(
            @"INSERT INTO web_chat_conversations (id, is_group, title, created_by)
              VALUES (@id, FALSE, '', @sender);
              INSERT INTO web_chat_members (conversation_id, username)
              VALUES (@id, @sender), (@id, @recipient);")
            .With("@id", _conversationId).With("@sender", Sender).With("@recipient", Recipient)
            .ExecuteNonQueryAsync();
    }

    public async Task DisposeAsync()
    {
        if (_messageId > 0) ChatEndpoints.TryDeleteBlob(_messageId);
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<Database>();
        await using var conn = await db.OpenAsync();
        await conn.Cmd("DELETE FROM web_chat_reactions WHERE message_id IN (SELECT id FROM web_chat_messages WHERE conversation_id=@id)")
            .With("@id", _conversationId).ExecuteNonQueryAsync();
        await conn.Cmd("DELETE FROM web_chat_messages WHERE conversation_id=@id").With("@id", _conversationId).ExecuteNonQueryAsync();
        await conn.Cmd("DELETE FROM web_chat_members WHERE conversation_id=@id").With("@id", _conversationId).ExecuteNonQueryAsync();
        await conn.Cmd("DELETE FROM web_chat_conversations WHERE id=@id").With("@id", _conversationId).ExecuteNonQueryAsync();
        await conn.Cmd("DELETE FROM app_users WHERE username = ANY(@users)")
            .With("@users", new[] { Sender, Recipient }).ExecuteNonQueryAsync();
    }

    [Fact]
    public async Task Voice_DownloadAndReadPersist_RetryIsIdempotent_DeleteIsExplicit()
    {
        var sender = await ClientAsAsync(Sender);
        var recipient = await ClientAsAsync(Recipient);
        var audio = Enumerable.Range(0, 8192).Select(i => (byte)(i % 251)).ToArray();
        const string retryKey = "android-voice:test-stable-retry-key";

        var create = await sender.PostAsJsonAsync($"/api/chat/conversations/{_conversationId}/messages/file", new
        {
            fileName = "ghi-am-1784100000000.ogg",
            fileSize = audio.Length,
            fileMime = "audio/ogg",
            kind = "voice",
            clientMessageId = retryKey,
        });
        Assert.Equal(HttpStatusCode.OK, create.StatusCode);
        var message = await create.Content.ReadFromJsonAsync<ChatMessageDto>();
        Assert.NotNull(message);
        _messageId = message!.Id;
        Assert.Equal("voice", message.Kind);
        Assert.False(message.HasBlob);

        using (var upload = new ByteArrayContent(audio))
        {
            upload.Headers.ContentType = new MediaTypeHeaderValue("audio/ogg");
            var uploaded = await sender.PostAsync(
                $"/api/chat/conversations/{_conversationId}/messages/{_messageId}/upload", upload);
            Assert.Equal(HttpStatusCode.OK, uploaded.StatusCode);
        }

        // Mất response rồi retry metadata: cùng id, không tạo dòng/push thứ hai và không upload lại.
        var retry = await (await sender.PostAsJsonAsync($"/api/chat/conversations/{_conversationId}/messages/file", new
        {
            fileName = "ghi-am-1784100000000.ogg",
            fileSize = audio.Length,
            fileMime = "audio/ogg",
            kind = "voice",
            clientMessageId = retryKey,
        })).Content.ReadFromJsonAsync<ChatMessageDto>();
        Assert.Equal(_messageId, retry!.Id);
        Assert.True(retry.HasBlob);

        // GET messages đánh dấu đã đọc nhưng không được thay đổi availability.
        var messages = await recipient.GetFromJsonAsync<List<ChatMessageDto>>(
            $"/api/chat/conversations/{_conversationId}/messages");
        Assert.Contains(messages!, m => m.Id == _messageId && m.Kind == "voice" && m.HasBlob);

        var first = await recipient.GetByteArrayAsync(
            $"/api/chat/conversations/{_conversationId}/messages/{_messageId}/download");
        var second = await recipient.GetByteArrayAsync(
            $"/api/chat/conversations/{_conversationId}/messages/{_messageId}/download");
        Assert.Equal(audio, first);
        Assert.Equal(audio, second);
        Assert.True(File.Exists(ChatEndpoints.BlobPath(_messageId)));

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<Database>();
            await using var conn = await db.OpenAsync();
            await using var row = await conn.Cmd(
                "SELECT kind, has_blob, blob_expires_at FROM web_chat_messages WHERE id=@id")
                .With("@id", _messageId).ExecuteReaderAsync();
            Assert.True(await row.ReadAsync());
            Assert.Equal("voice", row.GetString(0));
            Assert.True(row.GetBoolean(1));
            Assert.True(row.IsDBNull(2));
        }

        var removed = await sender.DeleteAsync(
            $"/api/chat/conversations/{_conversationId}/messages/{_messageId}");
        Assert.Equal(HttpStatusCode.NoContent, removed.StatusCode);
        Assert.False(File.Exists(ChatEndpoints.BlobPath(_messageId)));
        var afterDelete = await recipient.GetAsync(
            $"/api/chat/conversations/{_conversationId}/messages/{_messageId}/download");
        Assert.Equal(HttpStatusCode.NotFound, afterDelete.StatusCode);
    }

    /// <summary>
    /// Voice 20MB vượt trần body JSON (16MB) nhưng vẫn dưới trần voice (25MB) → phải upload được.
    /// Trước đây chốt chặn 413 chỉ miễn trừ "/api/releases", nên MỌI blob chat lớn hơn 16MB bị trả
    /// 413 ngay ở middleware, trước khi handler (vốn cho tới 100MB tệp / 25MB voice) kịp chạy.
    /// </summary>
    [Fact]
    public async Task Upload_VoiceLargerThanJsonBodyLimit_IsAccepted()
    {
        var sender = await ClientAsAsync(Sender);
        var audio = new byte[20 * 1024 * 1024];
        for (var i = 0; i < audio.Length; i++) audio[i] = (byte)(i % 251);
        Assert.True(audio.LongLength > PayloadLimits.MaxJsonBodyBytes); // vượt trần JSON
        Assert.True(audio.LongLength < ChatEndpoints.MaxVoiceBlobBytes); // nhưng handler vẫn nhận

        var create = await sender.PostAsJsonAsync($"/api/chat/conversations/{_conversationId}/messages/file", new
        {
            fileName = "ghi-am-1784100000002.ogg",
            fileSize = audio.Length,
            fileMime = "audio/ogg",
            kind = "voice",
        });
        Assert.Equal(HttpStatusCode.OK, create.StatusCode);
        var message = await create.Content.ReadFromJsonAsync<ChatMessageDto>();
        _messageId = message!.Id;

        using var upload = new ByteArrayContent(audio);
        upload.Headers.ContentType = new MediaTypeHeaderValue("audio/ogg");
        var uploaded = await sender.PostAsync(
            $"/api/chat/conversations/{_conversationId}/messages/{_messageId}/upload", upload);

        Assert.Equal(HttpStatusCode.OK, uploaded.StatusCode);
        Assert.Equal(audio.LongLength, new FileInfo(ChatEndpoints.BlobPath(_messageId)).Length);
    }

    [Fact]
    public async Task LegacyRecorderStoredAsFile_AlsoSurvivesRepeatedDownload()
    {
        var audio = new byte[] { 9, 8, 7, 6, 5, 4 };
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<Database>();
            await using var conn = await db.OpenAsync();
            _messageId = Convert.ToInt64(await conn.Cmd(
                @"INSERT INTO web_chat_messages
                      (conversation_id, sender_username, body, kind, file_name, file_size, file_mime,
                       has_blob, blob_expires_at, created_at)
                  VALUES (@cid, @sender, '', 'file', 'ghi-am-1784100000001.m4a', @size, 'audio/mpeg',
                          TRUE, CURRENT_TIMESTAMP + INTERVAL '1 day', CURRENT_TIMESTAMP)
                  RETURNING id")
                .With("@cid", _conversationId).With("@sender", Sender).With("@size", audio.Length)
                .ExecuteScalarAsync());
        }
        await File.WriteAllBytesAsync(ChatEndpoints.BlobPath(_messageId), audio);

        var recipient = await ClientAsAsync(Recipient);
        var first = await recipient.GetByteArrayAsync(
            $"/api/chat/conversations/{_conversationId}/messages/{_messageId}/download");
        var second = await recipient.GetByteArrayAsync(
            $"/api/chat/conversations/{_conversationId}/messages/{_messageId}/download");

        Assert.Equal(audio, first);
        Assert.Equal(audio, second);
        Assert.True(File.Exists(ChatEndpoints.BlobPath(_messageId)));
        using var verifyScope = _factory.Services.CreateScope();
        var verifyDb = verifyScope.ServiceProvider.GetRequiredService<Database>();
        await using var verify = await verifyDb.OpenAsync();
        var hasBlob = await verify.Cmd("SELECT has_blob FROM web_chat_messages WHERE id=@id")
            .With("@id", _messageId).ExecuteScalarAsync();
        Assert.True(Convert.ToBoolean(hasBlob));
    }

    private static async Task UpsertUser(Npgsql.NpgsqlConnection conn, string username) =>
        await conn.Cmd(
            @"INSERT INTO app_users
                 (id, username, full_name, email, role, password_hash, is_active, approval_status, approved_at, approved_by, created_at, is_deleted)
              VALUES (@id, @u, @u, '', 'Employee', @ph, TRUE, 'Approved', CURRENT_TIMESTAMP, 'test', CURRENT_TIMESTAMP, FALSE)
              ON CONFLICT (username) DO UPDATE
                  SET is_active=TRUE, is_deleted=FALSE, role='Employee', approval_status='Approved'")
            .With("@id", Guid.NewGuid()).With("@u", username).With("@ph", PasswordHasher.Hash("test-pass"))
            .ExecuteNonQueryAsync();

    private async Task<HttpClient> ClientAsAsync(string username)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<Database>();
        await using var conn = await db.OpenAsync();
        var id = (Guid)(await conn.Cmd("SELECT id FROM app_users WHERE username=@u")
            .With("@u", username).ExecuteScalarAsync())!;
        var tokens = scope.ServiceProvider.GetRequiredService<TokenService>();
        var token = tokens.CreateToken(new UserDto(id, username, username, "", "Employee", true, "Approved", DateTime.UtcNow));
        var client = _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }
}
