using KetoanMini.Api.Services;
using Xunit;

namespace KetoanMini.Api.Tests;

public sealed class ChatAttachmentPolicyTests
{
    [Theory]
    [InlineData("voice", "ghi-am-1.ogg", "audio/ogg")]
    [InlineData("VOICE", "ghi-am-2.m4a", "audio/mpeg")]
    [InlineData("voice", "voice.opus", "application/octet-stream")]
    public void ExplicitVoice_WithAudioMetadata_IsAccepted(string requested, string name, string mime)
    {
        var accepted = ChatAttachmentPolicy.TryResolveKind(requested, name, mime, out var kind);

        Assert.True(accepted);
        Assert.Equal(ChatAttachmentPolicy.VoiceKind, kind);
    }

    [Theory]
    [InlineData("voice", "invoice.pdf", "application/pdf")]
    [InlineData("video", "ghi-am-1.ogg", "audio/ogg")]
    public void InvalidKindOrNonAudioVoice_IsRejected(string requested, string name, string mime)
    {
        var accepted = ChatAttachmentPolicy.TryResolveKind(requested, name, mime, out var kind);

        Assert.False(accepted);
        Assert.Equal(ChatAttachmentPolicy.FileKind, kind);
    }

    [Fact]
    public void MissingKind_RemainsFile_ForOldClientCompatibility()
    {
        var accepted = ChatAttachmentPolicy.TryResolveKind(null, "ghi-am-100.ogg", "audio/ogg", out var kind);

        Assert.True(accepted);
        Assert.Equal(ChatAttachmentPolicy.FileKind, kind);
    }

    [Fact]
    public void LegacyRecorderFile_IsPersistentWithoutChangingItsKind()
    {
        Assert.True(ChatAttachmentPolicy.IsPersistentVoice("file", "ghi-am-1720000000000.m4a", "audio/mpeg"));
        Assert.False(ChatAttachmentPolicy.DeleteAfterRecipientDownload("file", "ghi-am-1720000000000.m4a", "audio/mpeg"));
    }

    [Theory]
    [InlineData("song.mp3", "audio/mpeg")]
    [InlineData("ghi-am-demo.ogg", "audio/ogg")]
    [InlineData("ghi-am-123.pdf", "application/pdf")]
    public void OrdinaryAttachment_IsNotMistakenForLegacyRecorder(string name, string mime)
    {
        Assert.False(ChatAttachmentPolicy.IsPersistentVoice("file", name, mime));
    }

    [Fact]
    public void VoiceHasNoExpiryAndSurvivesRecipientDownload()
    {
        var now = new DateTime(2026, 7, 15, 0, 0, 0, DateTimeKind.Utc);

        Assert.Null(ChatAttachmentPolicy.BlobExpiresAt("voice", "voice.ogg", "audio/ogg", now, TimeSpan.FromDays(7)));
        Assert.False(ChatAttachmentPolicy.DeleteAfterRecipientDownload("voice", "voice.ogg", "audio/ogg"));
    }

    [Fact]
    public void OrdinaryFileKeepsExistingTtlAndOneShotPolicy()
    {
        var now = new DateTime(2026, 7, 15, 0, 0, 0, DateTimeKind.Utc);

        Assert.Equal(now.AddDays(7), ChatAttachmentPolicy.BlobExpiresAt("file", "report.pdf", "application/pdf", now, TimeSpan.FromDays(7)));
        Assert.True(ChatAttachmentPolicy.DeleteAfterRecipientDownload("file", "report.pdf", "application/pdf"));
    }
}
