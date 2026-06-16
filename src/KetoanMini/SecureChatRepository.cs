using System.Globalization;
using Microsoft.Data.SqlClient;

namespace KetoanMini;

public sealed class SecureChatRepository
{
    private readonly string _connectionString;

    public SecureChatRepository(string connectionString)
    {
        _connectionString = connectionString;
    }

    public AppUser EnsureUserKeyPair(AppUser user)
    {
        if (!string.IsNullOrWhiteSpace(user.PublicKey) && KeyStorageService.TryLoadPrivateKey(user.Id) is not null)
        {
            return user;
        }

        if (!string.IsNullOrWhiteSpace(user.PublicKey))
        {
            return user;
        }

        var keyPair = ChatCryptoService.GenerateUserKeyPair();
        KeyStorageService.SavePrivateKey(user.Id, keyPair.PrivateKeyPem);

        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            UPDATE app_users
            SET public_key = @publicKey
            WHERE id = @id
              AND is_deleted = 0
              AND (public_key IS NULL OR public_key = N'');
            """;
        command.Parameters.AddWithValue("@id", user.Id);
        command.Parameters.AddWithValue("@publicKey", keyPair.PublicKeyPem);
        command.ExecuteNonQuery();

        user.PublicKey = keyPair.PublicKeyPem;
        return user;
    }

    public void UpsertPeerPublicKey(Guid userId, string username, string displayName, string publicKey)
    {
        if (userId == Guid.Empty || string.IsNullOrWhiteSpace(publicKey))
        {
            return;
        }

        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            UPDATE app_users
            SET public_key = CASE WHEN public_key IS NULL OR public_key = N'' THEN @publicKey ELSE public_key END,
                full_name = CASE WHEN full_name IS NULL OR full_name = N'' THEN @displayName ELSE full_name END
            WHERE id = @id
              AND is_deleted = 0;
            """;
        command.Parameters.AddWithValue("@id", userId);
        command.Parameters.AddWithValue("@displayName", displayName.Trim());
        command.Parameters.AddWithValue("@publicKey", publicKey);
        command.ExecuteNonQuery();
    }

    public IReadOnlyList<EmbeddedChatMessage> GetMessages(AppUser currentUser, EmbeddedChatConversation conversation, int take = 200)
    {
        if (currentUser.Id == Guid.Empty || conversation.UserId == Guid.Empty)
        {
            return [];
        }

        var privateKey = KeyStorageService.TryLoadPrivateKey(currentUser.Id);
        if (string.IsNullOrWhiteSpace(privateKey))
        {
            return [];
        }

        var conversationId = FindConversationId(currentUser.Id, conversation.UserId);
        if (conversationId is null)
        {
            return [];
        }

        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT TOP (@take)
                id,
                conversation_id,
                sender_id,
                receiver_id,
                sender_username,
                receiver_username,
                message_type,
                cipher_text,
                nonce,
                auth_tag,
                encrypted_key_for_sender,
                encrypted_key_for_receiver,
                created_at,
                status
            FROM chat_messages
            WHERE conversation_id = @conversationId
              AND is_deleted = 0
              AND sender_id IS NOT NULL
              AND receiver_id IS NOT NULL
              AND ((sender_id = @currentUserId AND receiver_id = @peerUserId)
                   OR (sender_id = @peerUserId AND receiver_id = @currentUserId))
            ORDER BY created_at DESC, id DESC;
            """;
        command.Parameters.AddWithValue("@take", Math.Clamp(take, 1, 500));
        command.Parameters.AddWithValue("@conversationId", conversationId.Value);
        command.Parameters.AddWithValue("@currentUserId", currentUser.Id);
        command.Parameters.AddWithValue("@peerUserId", conversation.UserId);

        using var reader = command.ExecuteReader();
        var messages = new List<EmbeddedChatMessage>();
        while (reader.Read())
        {
            messages.Add(ReadMessage(reader, currentUser, conversation, privateKey));
        }

        messages.Reverse();
        return messages;
    }

    public Guid SaveTextMessage(AppUser sender, Guid receiverId, string receiverUsername, string receiverName, string receiverPublicKey, Guid messageId, string plainText, DateTime sentAt, string status = "Sent")
    {
        EnsureUserKeyPair(sender);
        if (receiverId == Guid.Empty)
        {
            throw new InvalidOperationException("Người nhận không có UserId hợp lệ.");
        }

        if (string.IsNullOrWhiteSpace(receiverPublicKey))
        {
            throw new InvalidOperationException("Người nhận chưa có khóa chat bảo mật. Hãy để người nhận đăng nhập lại app.");
        }

        var conversationId = GetOrCreateConversationId(sender.Id, sender.Username, receiverId, receiverUsername);
        var encrypted = ChatCryptoService.EncryptForUsers(plainText, sender.PublicKey, receiverPublicKey);
        InsertMessage(
            messageId,
            conversationId,
            sender.Id,
            receiverId,
            sender.Username,
            receiverUsername,
            "Text",
            encrypted,
            sentAt,
            status);
        return conversationId;
    }

    public Guid SaveIncomingTextMessage(AppUser receiver, EmbeddedLanPeer sender, Guid messageId, string plainText, DateTime sentAt, string status = "Received")
    {
        EnsureUserKeyPair(receiver);
        if (sender.UserId == Guid.Empty)
        {
            throw new InvalidOperationException("Người gửi không có UserId hợp lệ.");
        }

        var conversationId = GetOrCreateConversationId(receiver.Id, receiver.Username, sender.UserId, sender.Username);
        var encrypted = ChatCryptoService.EncryptForUsers(plainText, sender.PublicKey, receiver.PublicKey);
        InsertMessage(
            messageId,
            conversationId,
            sender.UserId,
            receiver.Id,
            sender.Username,
            receiver.Username,
            "Text",
            encrypted,
            sentAt,
            status);
        return conversationId;
    }

    public Guid SaveFileHistory(AppUser sender, Guid receiverId, string receiverUsername, string receiverPublicKey, Guid messageId, string fileName, long fileSize, DateTime sentAt, string status = "Sent")
    {
        EnsureUserKeyPair(sender);
        var conversationId = GetOrCreateConversationId(sender.Id, sender.Username, receiverId, receiverUsername);
        var encrypted = ChatCryptoService.EncryptForUsers(Path.GetFileName(fileName), sender.PublicKey, receiverPublicKey);
        InsertMessage(
            messageId,
            conversationId,
            sender.Id,
            receiverId,
            sender.Username,
            receiverUsername,
            "File",
            encrypted,
            sentAt,
            status,
            fileSize);
        return conversationId;
    }

    public Guid SaveIncomingFileHistory(AppUser receiver, EmbeddedLanPeer sender, Guid messageId, string fileName, long fileSize, DateTime sentAt, string status = "Received")
    {
        EnsureUserKeyPair(receiver);
        if (sender.UserId == Guid.Empty)
        {
            throw new InvalidOperationException("Người gửi không có UserId hợp lệ.");
        }

        var conversationId = GetOrCreateConversationId(receiver.Id, receiver.Username, sender.UserId, sender.Username);
        var encrypted = ChatCryptoService.EncryptForUsers(Path.GetFileName(fileName), sender.PublicKey, receiver.PublicKey);
        InsertMessage(
            messageId,
            conversationId,
            sender.UserId,
            receiver.Id,
            sender.Username,
            receiver.Username,
            "File",
            encrypted,
            sentAt,
            status,
            fileSize);
        return conversationId;
    }

    public void MarkConversationRead(Guid currentUserId, Guid peerUserId)
    {
        if (currentUserId == Guid.Empty || peerUserId == Guid.Empty)
        {
            return;
        }

        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            UPDATE chat_messages
            SET status = N'Read'
            WHERE receiver_id = @currentUserId
              AND sender_id = @peerUserId
              AND is_deleted = 0
              AND status <> N'Read';
            """;
        command.Parameters.AddWithValue("@currentUserId", currentUserId);
        command.Parameters.AddWithValue("@peerUserId", peerUserId);
        command.ExecuteNonQuery();
    }

    private EmbeddedChatMessage ReadMessage(SqlDataReader reader, AppUser currentUser, EmbeddedChatConversation conversation, string privateKey)
    {
        var senderId = GetGuid(reader, "sender_id");
        var receiverId = GetGuid(reader, "receiver_id");
        var type = GetString(reader, "message_type");
        var isMine = senderId == currentUser.Id;
        var payload = new E2eeChatPayload(
            GetString(reader, "cipher_text"),
            GetString(reader, "nonce"),
            GetString(reader, "auth_tag"),
            GetString(reader, "encrypted_key_for_sender"),
            GetString(reader, "encrypted_key_for_receiver"));

        var plain = "";
        try
        {
            plain = ChatCryptoService.DecryptText(payload, currentUser.Id, senderId, receiverId, privateKey);
        }
        catch
        {
            plain = "[Không giải mã được]";
        }

        var sentAt = ParseDateTime(GetString(reader, "created_at"));
        var status = ParseStatus(GetString(reader, "status"));
        var message = new EmbeddedChatMessage
        {
            Id = GetGuid(reader, "id"),
            ConversationId = conversation.DeviceId,
            SenderId = senderId.ToString("D"),
            SenderUserId = senderId,
            SenderName = isMine ? currentUser.DisplayName : conversation.Name,
            SenderAvatarText = isMine ? currentUser.DisplayName : conversation.AvatarText,
            Text = string.Equals(type, "File", StringComparison.OrdinalIgnoreCase) ? "" : plain,
            SentAt = sentAt,
            IsMine = isMine,
            Kind = string.Equals(type, "File", StringComparison.OrdinalIgnoreCase) ? EmbeddedChatMessageKind.File : EmbeddedChatMessageKind.Text,
            DeliveryStatus = status
        };

        if (message.Kind == EmbeddedChatMessageKind.File)
        {
            message.Attachment = new EmbeddedFileAttachment
            {
                FileName = plain,
                Extension = Path.GetExtension(plain),
                SizeBytes = GetInt64(reader, "file_size"),
                SizeText = LanChatModuleService.FormatFileSize(GetInt64(reader, "file_size")),
                Status = "Đã lưu lịch sử"
            };
        }

        return message;
    }

    private Guid GetOrCreateConversationId(Guid currentUserId, string currentUsername, Guid peerUserId, string peerUsername)
    {
        var pair = PairUsers(currentUserId, currentUsername, peerUserId, peerUsername);
        var existing = FindConversationId(pair.UserAId, pair.UserBId);
        if (existing is Guid id)
        {
            return id;
        }

        var conversationId = Guid.NewGuid();
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO chat_conversations
                (id, user_a, user_b, user_a_id, user_b_id, created_at, is_deleted)
            VALUES
                (@id, @userA, @userB, @userAId, @userBId, @createdAt, 0);
            """;
        command.Parameters.AddWithValue("@id", conversationId);
        command.Parameters.AddWithValue("@userA", pair.UserAUsername);
        command.Parameters.AddWithValue("@userB", pair.UserBUsername);
        command.Parameters.AddWithValue("@userAId", pair.UserAId);
        command.Parameters.AddWithValue("@userBId", pair.UserBId);
        command.Parameters.AddWithValue("@createdAt", DateTime.Now);
        try
        {
            command.ExecuteNonQuery();
            return conversationId;
        }
        catch (SqlException ex) when (ex.Number is 2601 or 2627)
        {
            return FindConversationId(pair.UserAId, pair.UserBId) ?? conversationId;
        }
    }

    private Guid? FindConversationId(Guid userAId, Guid userBId)
    {
        var pair = PairUsers(userAId, "", userBId, "");
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT TOP (1) id
            FROM chat_conversations
            WHERE user_a_id = @userAId
              AND user_b_id = @userBId
              AND is_deleted = 0;
            """;
        command.Parameters.AddWithValue("@userAId", pair.UserAId);
        command.Parameters.AddWithValue("@userBId", pair.UserBId);
        var value = Convert.ToString(command.ExecuteScalar(), CultureInfo.InvariantCulture);
        return Guid.TryParse(value, out var id) ? id : null;
    }

    private void InsertMessage(
        Guid messageId,
        Guid conversationId,
        Guid senderId,
        Guid receiverId,
        string senderUsername,
        string receiverUsername,
        string messageType,
        E2eeChatPayload encrypted,
        DateTime createdAt,
        string status,
        long fileSize = 0)
    {
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            IF NOT EXISTS (SELECT 1 FROM chat_messages WHERE id = @id)
            BEGIN
                INSERT INTO chat_messages
                    (id, conversation_id, sender_id, receiver_id, sender_username, receiver_username, message_type,
                     cipher_text, nonce, auth_tag, encrypted_key_for_sender, encrypted_key_for_receiver, created_at, status, file_size, is_deleted)
                VALUES
                    (@id, @conversationId, @senderId, @receiverId, @senderUsername, @receiverUsername, @messageType,
                     @cipherText, @nonce, @authTag, @encryptedKeyForSender, @encryptedKeyForReceiver, @createdAt, @status, @fileSize, 0);
            END
            """;
        command.Parameters.AddWithValue("@id", messageId);
        command.Parameters.AddWithValue("@conversationId", conversationId);
        command.Parameters.AddWithValue("@senderId", senderId);
        command.Parameters.AddWithValue("@receiverId", receiverId);
        command.Parameters.AddWithValue("@senderUsername", senderUsername);
        command.Parameters.AddWithValue("@receiverUsername", receiverUsername);
        command.Parameters.AddWithValue("@messageType", messageType);
        command.Parameters.AddWithValue("@cipherText", encrypted.CipherText);
        command.Parameters.AddWithValue("@nonce", encrypted.Nonce);
        command.Parameters.AddWithValue("@authTag", encrypted.AuthTag);
        command.Parameters.AddWithValue("@encryptedKeyForSender", encrypted.EncryptedKeyForSender);
        command.Parameters.AddWithValue("@encryptedKeyForReceiver", encrypted.EncryptedKeyForReceiver);
        command.Parameters.AddWithValue("@createdAt", createdAt);
        command.Parameters.AddWithValue("@status", status);
        command.Parameters.AddWithValue("@fileSize", fileSize);
        command.ExecuteNonQuery();
    }

    private SqlConnection OpenConnection()
    {
        var connection = new SqlConnection(_connectionString);
        connection.Open();
        return connection;
    }

    private static (Guid UserAId, Guid UserBId, string UserAUsername, string UserBUsername) PairUsers(Guid user1Id, string username1, Guid user2Id, string username2)
    {
        return string.CompareOrdinal(user1Id.ToString("D"), user2Id.ToString("D")) <= 0
            ? (user1Id, user2Id, username1.Trim(), username2.Trim())
            : (user2Id, user1Id, username2.Trim(), username1.Trim());
    }

    private static EmbeddedMessageDeliveryStatus ParseStatus(string value)
    {
        return Enum.TryParse<EmbeddedMessageDeliveryStatus>(value, true, out var status)
            ? status
            : EmbeddedMessageDeliveryStatus.Sent;
    }

    private static DateTime ParseDateTime(string value)
    {
        return DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var dateTime)
            ? dateTime
            : DateTime.Now;
    }

    private static Guid GetGuid(SqlDataReader reader, string columnName)
    {
        return Guid.TryParse(GetString(reader, columnName), out var id) ? id : Guid.Empty;
    }

    private static string GetString(SqlDataReader reader, string columnName)
    {
        int ordinal;
        try
        {
            ordinal = reader.GetOrdinal(columnName);
        }
        catch (IndexOutOfRangeException)
        {
            return "";
        }

        if (reader.IsDBNull(ordinal))
        {
            return "";
        }

        var value = reader.GetValue(ordinal);
        return value switch
        {
            DateTime dateTime => dateTime.ToString("O", CultureInfo.InvariantCulture),
            Guid guid => guid.ToString("D"),
            bool boolean => boolean ? "1" : "0",
            _ => Convert.ToString(value, CultureInfo.InvariantCulture) ?? ""
        };
    }

    private static long GetInt64(SqlDataReader reader, string columnName)
    {
        var value = GetString(reader, columnName);
        return long.TryParse(value, NumberStyles.Any, CultureInfo.InvariantCulture, out var number) ? number : 0;
    }
}
