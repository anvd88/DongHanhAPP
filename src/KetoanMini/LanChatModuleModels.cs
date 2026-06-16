namespace KetoanMini;

public enum EmbeddedChatMessageKind
{
    Text,
    File
}

public enum EmbeddedMessageDeliveryStatus
{
    Sending,
    Sent,
    Received,
    Read,
    Failed
}

public sealed class EmbeddedUserProfile
{
    public Guid UserId { get; set; }
    public string DeviceId { get; set; } = "";
    public string Username { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public string PublicKey { get; set; } = "";
    public string DeviceName { get; set; } = Environment.MachineName;
    public string AvatarText => TextUtil.Initials(DisplayName);
}

public sealed class EmbeddedLanPeer
{
    public Guid UserId { get; set; }
    public string DeviceId { get; set; } = "";
    public string Username { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public string PublicKey { get; set; } = "";
    public string AvatarText { get; set; } = "?";
    public string Address { get; set; } = "";
    public int ChatPort { get; set; }
    public DateTime LastSeen { get; set; } = DateTime.Now;
}

public sealed class EmbeddedChatConversation
{
    public Guid UserId { get; set; }
    public string DeviceId { get; set; } = "";
    public string Username { get; set; } = "";
    public string Name { get; set; } = "";
    public string PublicKey { get; set; } = "";
    public string AvatarText { get; set; } = "?";
    public string Address { get; set; } = "";
    public int ChatPort { get; set; }
    public bool IsOnline { get; set; }
    public int UnreadCount { get; set; }
    public string LastMessage { get; set; } = "Đang online trong LAN";
    public DateTime? LastMessageAt { get; set; }
    public DateTime LastSeen { get; set; } = DateTime.Now;

    public string LastTimeText
    {
        get
        {
            if (LastMessageAt is null)
            {
                return LastSeen.ToString("HH:mm");
            }

            var time = LastMessageAt.Value;
            return time.Date == DateTime.Today ? time.ToString("HH:mm") : time.ToString("dd/MM");
        }
    }
}

public sealed class EmbeddedFileAttachment
{
    public string FileName { get; set; } = "";
    public string Extension { get; set; } = "";
    public long SizeBytes { get; set; }
    public string SizeText { get; set; } = "";
    public string LocalPath { get; set; } = "";
    public string Status { get; set; } = "";
    public double Progress { get; set; }
    public bool IsTransferring { get; set; }
}

public sealed class EmbeddedChatMessage
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string ConversationId { get; set; } = "";
    public string SenderId { get; set; } = "";
    public Guid SenderUserId { get; set; }
    public string SenderName { get; set; } = "";
    public string SenderAvatarText { get; set; } = "?";
    public string Text { get; set; } = "";
    public DateTime SentAt { get; set; } = DateTime.Now;
    public bool IsMine { get; set; }
    public EmbeddedChatMessageKind Kind { get; set; } = EmbeddedChatMessageKind.Text;
    public EmbeddedMessageDeliveryStatus DeliveryStatus { get; set; } = EmbeddedMessageDeliveryStatus.Sending;
    public EmbeddedFileAttachment? Attachment { get; set; }
}
