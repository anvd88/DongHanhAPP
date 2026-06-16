using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Security.Cryptography;
using System.Text.Json;

namespace KetoanMini;

public sealed class EmbeddedLanPeersChangedEventArgs : EventArgs
{
    public required IReadOnlyList<EmbeddedLanPeer> Peers { get; init; }
}

public sealed class EmbeddedLanMessageEventArgs : EventArgs
{
    public required EmbeddedLanPeer Peer { get; init; }
    public required EmbeddedChatMessage Message { get; init; }
}

public sealed class EmbeddedLanReceiptEventArgs : EventArgs
{
    public required Guid MessageId { get; init; }
    public required EmbeddedMessageDeliveryStatus Status { get; init; }
}

public sealed class LanChatModuleService : IDisposable
{
    private const string Protocol = "KETOAN_MINI_CHAT_MODULE_V1";
    private const int DiscoveryPort = 52210;
    private const int FirstChatPort = 52220;
    private const int LastChatPort = 52240;
    private static readonly TimeSpan PeerTimeout = TimeSpan.FromSeconds(15);

    private readonly object _sync = new();
    private readonly Dictionary<string, EmbeddedLanPeer> _peers = new(StringComparer.OrdinalIgnoreCase);
    private readonly JsonSerializerOptions _jsonOptions = new(JsonSerializerDefaults.Web);
    private CancellationTokenSource? _cts;
    private UdpClient? _udpReceiver;
    private UdpClient? _udpSender;
    private TcpListener? _listener;
    private EmbeddedUserProfile? _profile;

    public event EventHandler<EmbeddedLanPeersChangedEventArgs>? PeersChanged;
    public event EventHandler<EmbeddedLanMessageEventArgs>? MessageReceived;
    public event EventHandler<EmbeddedLanReceiptEventArgs>? ReceiptReceived;
    public event EventHandler<string>? StatusChanged;

    public int ChatPort { get; private set; }

    public Task StartAsync(EmbeddedUserProfile profile)
    {
        _profile = profile;
        if (_cts is not null)
        {
            return Task.CompletedTask;
        }

        _cts = new CancellationTokenSource();
        StartWebSocketListener(_cts.Token);
        StartDiscovery(_cts.Token);
        return Task.CompletedTask;
    }

    public async Task SendTextAsync(EmbeddedChatConversation conversation, string text, Guid messageId, CancellationToken cancellationToken = default)
    {
        var envelope = CreateEnvelope(conversation, "Text", messageId);
        var encrypted = ChatCryptoService.EncryptForUsers(text.Trim(), _profile?.PublicKey ?? "", conversation.PublicKey);
        ApplyEncryptedPayload(envelope, encrypted);
        await SendEnvelopeAsync(conversation.Address, conversation.ChatPort, envelope, null, cancellationToken);
    }

    public async Task SendFileAsync(EmbeddedChatConversation conversation, string filePath, Guid messageId, IProgress<double>? progress = null, CancellationToken cancellationToken = default)
    {
        if (!File.Exists(filePath))
        {
            throw new FileNotFoundException("File không tồn tại.", filePath);
        }

        if (_profile is null)
        {
            throw new InvalidOperationException("Chat chưa khởi động.");
        }

        var file = new FileInfo(filePath);
        var encrypted = ChatCryptoService.EncryptBytes(await File.ReadAllBytesAsync(file.FullName, cancellationToken), _profile.PublicKey, conversation.PublicKey);
        var encryptedBytes = Convert.FromBase64String(encrypted.CipherText);
        var envelope = CreateEnvelope(conversation, "File", messageId);
        envelope.FileName = file.Name;
        envelope.Extension = file.Extension;
        envelope.FileSize = file.Length;
        envelope.EncryptedFileSize = encryptedBytes.Length;
        ApplyEncryptedPayload(envelope, encrypted, includeCipherText: false);
        await SendEnvelopeAsync(conversation.Address, conversation.ChatPort, envelope, encryptedBytes, cancellationToken, progress);
    }

    public async Task SendReceiptAsync(EmbeddedChatConversation conversation, Guid messageId, EmbeddedMessageDeliveryStatus status, CancellationToken cancellationToken = default)
    {
        if (conversation.ChatPort <= 0 || string.IsNullOrWhiteSpace(conversation.Address))
        {
            return;
        }

        var envelope = CreateEnvelope(conversation, "Receipt", Guid.NewGuid());
        envelope.ReceiptForMessageId = messageId;
        envelope.ReceiptStatus = status.ToString();
        await SendEnvelopeAsync(conversation.Address, conversation.ChatPort, envelope, null, cancellationToken);
    }

    private void StartDiscovery(CancellationToken cancellationToken)
    {
        try
        {
            _udpReceiver = new UdpClient(AddressFamily.InterNetwork);
            _udpReceiver.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
            _udpReceiver.Client.Bind(new IPEndPoint(IPAddress.Any, DiscoveryPort));
            _udpSender = new UdpClient { EnableBroadcast = true };

            _ = Task.Run(() => ReceivePresenceLoopAsync(cancellationToken), cancellationToken);
            _ = Task.Run(() => BroadcastPresenceLoopAsync(cancellationToken), cancellationToken);
            _ = Task.Run(() => PrunePeersLoopAsync(cancellationToken), cancellationToken);
        }
        catch (Exception ex)
        {
            Debug.WriteLine(ex);
            StatusChanged?.Invoke(this, "Không phát hiện được máy khác trong LAN. Hãy cho phép ứng dụng qua Windows Firewall.");
        }
    }

    private void StartWebSocketListener(CancellationToken cancellationToken)
    {
        for (var port = FirstChatPort; port <= LastChatPort; port++)
        {
            try
            {
                _listener = new TcpListener(IPAddress.Any, port);
                _listener.Start();
                ChatPort = port;
                _ = Task.Run(() => AcceptWebSocketLoopAsync(cancellationToken), cancellationToken);
                return;
            }
            catch
            {
                _listener?.Stop();
                _listener = null;
            }
        }

        StatusChanged?.Invoke(this, "Không mở được cổng chat. Hãy kiểm tra Windows Firewall hoặc app chat đang chạy trùng.");
    }

    private async Task BroadcastPresenceLoopAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                if (_udpSender is not null && _profile is not null && ChatPort > 0)
                {
                    var packet = new PresencePacket
                    {
                        Protocol = Protocol,
                        UserId = _profile.UserId,
                        DeviceId = _profile.DeviceId,
                        Username = _profile.Username,
                        DisplayName = _profile.DisplayName,
                        PublicKey = _profile.PublicKey,
                        AvatarText = _profile.AvatarText,
                        ChatPort = ChatPort
                    };
                    var bytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(packet, _jsonOptions));
                    foreach (var endpoint in GetBroadcastEndpoints())
                    {
                        await _udpSender.SendAsync(bytes, endpoint, cancellationToken);
                    }
                }
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch
            {
            }

            await Task.Delay(2_000, cancellationToken);
        }
    }

    private async Task ReceivePresenceLoopAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                if (_udpReceiver is null || _profile is null)
                {
                    return;
                }

                var result = await _udpReceiver.ReceiveAsync(cancellationToken);
                var packet = JsonSerializer.Deserialize<PresencePacket>(Encoding.UTF8.GetString(result.Buffer), _jsonOptions);
                if (packet is null ||
                    !string.Equals(packet.Protocol, Protocol, StringComparison.Ordinal) ||
                    string.Equals(packet.DeviceId, _profile.DeviceId, StringComparison.OrdinalIgnoreCase) ||
                    packet.ChatPort <= 0)
                {
                    continue;
                }

                lock (_sync)
                {
                    _peers[packet.DeviceId] = new EmbeddedLanPeer
                    {
                        DeviceId = packet.DeviceId,
                        UserId = packet.UserId,
                        Username = packet.Username,
                        DisplayName = string.IsNullOrWhiteSpace(packet.DisplayName) ? packet.Username : packet.DisplayName,
                        PublicKey = packet.PublicKey,
                        AvatarText = string.IsNullOrWhiteSpace(packet.AvatarText) ? TextUtil.Initials(packet.DisplayName) : packet.AvatarText,
                        Address = result.RemoteEndPoint.Address.ToString(),
                        ChatPort = packet.ChatPort,
                        LastSeen = DateTime.Now
                    };
                }

                RaisePeersChanged();
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch
            {
            }
        }
    }

    private async Task PrunePeersLoopAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            await Task.Delay(3_000, cancellationToken);
            var changed = false;
            lock (_sync)
            {
                var stale = _peers
                    .Where(item => item.Value.LastSeen < DateTime.Now.Subtract(PeerTimeout))
                    .Select(item => item.Key)
                    .ToList();

                foreach (var key in stale)
                {
                    _peers.Remove(key);
                    changed = true;
                }
            }

            if (changed)
            {
                RaisePeersChanged();
            }
        }
    }

    private void RaisePeersChanged()
    {
        IReadOnlyList<EmbeddedLanPeer> peers;
        lock (_sync)
        {
            peers = _peers.Values
                .OrderBy(peer => peer.DisplayName)
                .Select(ClonePeer)
                .ToList();
        }

        PeersChanged?.Invoke(this, new EmbeddedLanPeersChangedEventArgs { Peers = peers });
    }

    private async Task AcceptWebSocketLoopAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                if (_listener is null)
                {
                    return;
                }

                var client = await _listener.AcceptTcpClientAsync(cancellationToken);
                _ = Task.Run(() => HandleWebSocketClientAsync(client, cancellationToken), cancellationToken);
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch
            {
                await Task.Delay(500, cancellationToken);
            }
        }
    }

    private async Task HandleWebSocketClientAsync(TcpClient client, CancellationToken cancellationToken)
    {
        using var clientScope = client;
        try
        {
            var stream = client.GetStream();
            var request = await ReadHttpRequestAsync(stream, cancellationToken);
            if (!TryGetWebSocketKey(request, out var key))
            {
                return;
            }

            await WriteWebSocketHandshakeAsync(stream, key, cancellationToken);
            using var webSocket = WebSocket.CreateFromStream(stream, true, null, TimeSpan.FromSeconds(30));
            var envelopeText = await ReceiveTextMessageAsync(webSocket, cancellationToken);
            if (string.IsNullOrWhiteSpace(envelopeText))
            {
                return;
            }

            var envelope = JsonSerializer.Deserialize<ModuleEnvelope>(envelopeText, _jsonOptions);
            if (envelope is null || !string.Equals(envelope.Protocol, Protocol, StringComparison.Ordinal))
            {
                return;
            }

            var peer = CreatePeer(envelope, ((IPEndPoint)client.Client.RemoteEndPoint!).Address.ToString());
            if (string.Equals(envelope.Type, "Receipt", StringComparison.OrdinalIgnoreCase))
            {
                if (Enum.TryParse<EmbeddedMessageDeliveryStatus>(envelope.ReceiptStatus, out var status))
                {
                    ReceiptReceived?.Invoke(this, new EmbeddedLanReceiptEventArgs { MessageId = envelope.ReceiptForMessageId, Status = status });
                }

                return;
            }

            if (string.Equals(envelope.Type, "Text", StringComparison.OrdinalIgnoreCase))
            {
                var text = envelope.Content;
                if (!string.IsNullOrWhiteSpace(envelope.CipherText))
                {
                    text = DecryptEnvelopeText(envelope);
                }

                MessageReceived?.Invoke(this, new EmbeddedLanMessageEventArgs
                {
                    Peer = peer,
                    Message = new EmbeddedChatMessage
                    {
                        Id = envelope.MessageId,
                        ConversationId = peer.DeviceId,
                        SenderId = peer.DeviceId,
                        SenderUserId = peer.UserId,
                        SenderName = peer.DisplayName,
                        SenderAvatarText = peer.AvatarText,
                        Text = text,
                        SentAt = envelope.Timestamp,
                        DeliveryStatus = EmbeddedMessageDeliveryStatus.Received,
                        IsMine = false,
                        Kind = EmbeddedChatMessageKind.Text
                    }
                });
                return;
            }

            if (string.Equals(envelope.Type, "File", StringComparison.OrdinalIgnoreCase))
            {
                var targetPath = UniqueReceivedPath(envelope.FileName);
                Directory.CreateDirectory(Path.GetDirectoryName(targetPath)!);
                var encryptedBytes = await ReceiveBinaryPayloadAsync(webSocket, envelope.EncryptedFileSize > 0 ? envelope.EncryptedFileSize : envelope.FileSize, cancellationToken);
                var fileBytes = DecryptEnvelopeBytes(envelope, encryptedBytes);
                await File.WriteAllBytesAsync(targetPath, fileBytes, cancellationToken);

                MessageReceived?.Invoke(this, new EmbeddedLanMessageEventArgs
                {
                    Peer = peer,
                    Message = new EmbeddedChatMessage
                    {
                        Id = envelope.MessageId,
                        ConversationId = peer.DeviceId,
                        SenderId = peer.DeviceId,
                        SenderUserId = peer.UserId,
                        SenderName = peer.DisplayName,
                        SenderAvatarText = peer.AvatarText,
                        SentAt = envelope.Timestamp,
                        DeliveryStatus = EmbeddedMessageDeliveryStatus.Received,
                        IsMine = false,
                        Kind = EmbeddedChatMessageKind.File,
                        Attachment = new EmbeddedFileAttachment
                        {
                            FileName = envelope.FileName,
                            Extension = envelope.Extension,
                            SizeBytes = envelope.FileSize,
                            SizeText = FormatFileSize(envelope.FileSize),
                            LocalPath = targetPath,
                            Status = "Đã có trên máy"
                        }
                    }
                });
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine(ex);
            StatusChanged?.Invoke(this, "Nhận tin nhắn hoặc file thất bại. Hãy kiểm tra Windows Firewall và mạng LAN.");
        }
    }

    private async Task SendEnvelopeAsync(string address, int port, ModuleEnvelope envelope, byte[]? binaryPayload, CancellationToken cancellationToken, IProgress<double>? progress = null)
    {
        if (string.IsNullOrWhiteSpace(address) || port <= 0)
        {
            throw new InvalidOperationException("Người nhận chưa online trong LAN.");
        }

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(20));
        using var webSocket = new ClientWebSocket();
        await webSocket.ConnectAsync(new Uri($"ws://{address}:{port}/chat"), timeout.Token);
        await SendTextMessageAsync(webSocket, JsonSerializer.Serialize(envelope, _jsonOptions), timeout.Token);

        if (binaryPayload is not null)
        {
            await SendBinaryPayloadAsync(webSocket, binaryPayload, timeout.Token, progress);
        }

        if (webSocket.State is WebSocketState.Open or WebSocketState.CloseReceived)
        {
            await webSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, "OK", CancellationToken.None);
        }
    }

    private ModuleEnvelope CreateEnvelope(EmbeddedChatConversation conversation, string type, Guid messageId)
    {
        if (_profile is null)
        {
            throw new InvalidOperationException("Chat chưa khởi động.");
        }

        return new ModuleEnvelope
        {
            Protocol = Protocol,
            MessageId = messageId,
            SenderUserId = _profile.UserId,
            SenderId = _profile.DeviceId,
            SenderUsername = _profile.Username,
            SenderName = _profile.DisplayName,
            SenderPublicKey = _profile.PublicKey,
            SenderAvatarText = _profile.AvatarText,
            SenderChatPort = ChatPort,
            ReceiverUserId = conversation.UserId,
            ReceiverId = conversation.DeviceId,
            Type = type,
            Timestamp = DateTime.Now
        };
    }

    private static void ApplyEncryptedPayload(ModuleEnvelope envelope, E2eeChatPayload payload, bool includeCipherText = true)
    {
        envelope.Content = "";
        envelope.CipherText = includeCipherText ? payload.CipherText : "";
        envelope.Nonce = payload.Nonce;
        envelope.AuthTag = payload.AuthTag;
        envelope.EncryptedKeyForSender = payload.EncryptedKeyForSender;
        envelope.EncryptedKeyForReceiver = payload.EncryptedKeyForReceiver;
    }

    private string DecryptEnvelopeText(ModuleEnvelope envelope)
    {
        return Encoding.UTF8.GetString(DecryptEnvelopeBytes(envelope, Convert.FromBase64String(envelope.CipherText)));
    }

    private byte[] DecryptEnvelopeBytes(ModuleEnvelope envelope, byte[] cipherBytes)
    {
        if (_profile is null)
        {
            throw new InvalidOperationException("Chat chưa khởi động.");
        }

        var privateKey = KeyStorageService.TryLoadPrivateKey(_profile.UserId);
        if (string.IsNullOrWhiteSpace(privateKey))
        {
            throw new InvalidOperationException("Không tìm thấy khóa riêng để giải mã tin nhắn.");
        }

        var payload = new E2eeChatPayload(
            Convert.ToBase64String(cipherBytes),
            envelope.Nonce,
            envelope.AuthTag,
            envelope.EncryptedKeyForSender,
            envelope.EncryptedKeyForReceiver);
        return ChatCryptoService.DecryptBytes(payload, _profile.UserId, envelope.SenderUserId, envelope.ReceiverUserId, privateKey);
    }

    private static EmbeddedLanPeer CreatePeer(ModuleEnvelope envelope, string address)
    {
        return new EmbeddedLanPeer
        {
            DeviceId = envelope.SenderId,
            UserId = envelope.SenderUserId,
            Username = envelope.SenderUsername,
            DisplayName = string.IsNullOrWhiteSpace(envelope.SenderName) ? envelope.SenderUsername : envelope.SenderName,
            PublicKey = envelope.SenderPublicKey,
            AvatarText = string.IsNullOrWhiteSpace(envelope.SenderAvatarText) ? TextUtil.Initials(envelope.SenderName) : envelope.SenderAvatarText,
            Address = address,
            ChatPort = envelope.SenderChatPort,
            LastSeen = DateTime.Now
        };
    }

    private static async Task<string> ReadHttpRequestAsync(Stream stream, CancellationToken cancellationToken)
    {
        var bytes = new List<byte>(4096);
        var buffer = new byte[1];
        while (bytes.Count < 32 * 1024)
        {
            var read = await stream.ReadAsync(buffer, cancellationToken);
            if (read == 0)
            {
                break;
            }

            bytes.Add(buffer[0]);
            var count = bytes.Count;
            if (count >= 4 && bytes[count - 4] == '\r' && bytes[count - 3] == '\n' && bytes[count - 2] == '\r' && bytes[count - 1] == '\n')
            {
                break;
            }
        }

        return Encoding.ASCII.GetString(bytes.ToArray());
    }

    private static bool TryGetWebSocketKey(string request, out string key)
    {
        key = "";
        foreach (var line in request.Split("\r\n", StringSplitOptions.RemoveEmptyEntries).Skip(1))
        {
            var index = line.IndexOf(':');
            if (index <= 0)
            {
                continue;
            }

            if (string.Equals(line[..index].Trim(), "Sec-WebSocket-Key", StringComparison.OrdinalIgnoreCase))
            {
                key = line[(index + 1)..].Trim();
                return key.Length > 0;
            }
        }

        return false;
    }

    private static async Task WriteWebSocketHandshakeAsync(Stream stream, string key, CancellationToken cancellationToken)
    {
        const string magic = "258EAFA5-E914-47DA-95CA-C5AB0DC85B11";
        var accept = Convert.ToBase64String(SHA1.HashData(Encoding.ASCII.GetBytes(key.Trim() + magic)));
        var response =
            "HTTP/1.1 101 Switching Protocols\r\n" +
            "Upgrade: websocket\r\n" +
            "Connection: Upgrade\r\n" +
            $"Sec-WebSocket-Accept: {accept}\r\n\r\n";
        await stream.WriteAsync(Encoding.ASCII.GetBytes(response), cancellationToken);
        await stream.FlushAsync(cancellationToken);
    }

    private static async Task SendTextMessageAsync(WebSocket webSocket, string text, CancellationToken cancellationToken)
    {
        var bytes = Encoding.UTF8.GetBytes(text);
        await webSocket.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, cancellationToken);
    }

    private static async Task<string> ReceiveTextMessageAsync(WebSocket webSocket, CancellationToken cancellationToken)
    {
        var buffer = new byte[8192];
        using var output = new MemoryStream();
        while (true)
        {
            var result = await webSocket.ReceiveAsync(new ArraySegment<byte>(buffer), cancellationToken);
            if (result.MessageType == WebSocketMessageType.Close)
            {
                return "";
            }

            if (result.MessageType != WebSocketMessageType.Text)
            {
                throw new InvalidDataException("Gói đầu tiên phải là JSON text.");
            }

            output.Write(buffer, 0, result.Count);
            if (result.EndOfMessage)
            {
                break;
            }
        }

        return Encoding.UTF8.GetString(output.ToArray());
    }

    private static async Task SendBinaryPayloadAsync(WebSocket webSocket, byte[] payload, CancellationToken cancellationToken, IProgress<double>? progress)
    {
        const int chunkSize = 128 * 1024;
        if (payload.Length == 0)
        {
            await webSocket.SendAsync(ArraySegment<byte>.Empty, WebSocketMessageType.Binary, true, cancellationToken);
            progress?.Report(100);
            return;
        }

        var sent = 0;
        while (sent < payload.Length)
        {
            var count = Math.Min(chunkSize, payload.Length - sent);
            sent += count;
            await webSocket.SendAsync(new ArraySegment<byte>(payload, sent - count, count), WebSocketMessageType.Binary, sent >= payload.Length, cancellationToken);
            progress?.Report(sent * 100D / payload.Length);
        }
    }

    private static async Task<byte[]> ReceiveBinaryPayloadAsync(WebSocket webSocket, long expectedLength, CancellationToken cancellationToken)
    {
        var buffer = new byte[128 * 1024];
        long received = 0;
        using var output = new MemoryStream(expectedLength is > 0 and <= int.MaxValue ? (int)expectedLength : 0);
        while (true)
        {
            var result = await webSocket.ReceiveAsync(new ArraySegment<byte>(buffer), cancellationToken);
            if (result.MessageType == WebSocketMessageType.Close)
            {
                break;
            }

            if (result.MessageType != WebSocketMessageType.Binary)
            {
                throw new InvalidDataException("File phải được gửi bằng frame binary.");
            }

            output.Write(buffer, 0, result.Count);
            received += result.Count;
            if (result.EndOfMessage)
            {
                break;
            }
        }

        if (expectedLength > 0 && received != expectedLength)
        {
            throw new EndOfStreamException("Dung lượng file nhận không khớp.");
        }

        return output.ToArray();
    }

    private static async Task SendBinaryFileAsync(WebSocket webSocket, string filePath, long fileSize, CancellationToken cancellationToken, IProgress<double>? progress)
    {
        await using var input = File.OpenRead(filePath);
        var buffer = new byte[128 * 1024];
        long sent = 0;

        if (input.Length == 0)
        {
            await webSocket.SendAsync(ArraySegment<byte>.Empty, WebSocketMessageType.Binary, true, cancellationToken);
            progress?.Report(100);
            return;
        }

        int read;
        while ((read = await input.ReadAsync(buffer, cancellationToken)) > 0)
        {
            sent += read;
            await webSocket.SendAsync(new ArraySegment<byte>(buffer, 0, read), WebSocketMessageType.Binary, sent >= input.Length, cancellationToken);
            progress?.Report(fileSize <= 0 ? 0 : sent * 100D / fileSize);
        }
    }

    private static async Task ReceiveBinaryFileAsync(WebSocket webSocket, Stream output, long expectedLength, CancellationToken cancellationToken)
    {
        var buffer = new byte[128 * 1024];
        long received = 0;
        while (true)
        {
            var result = await webSocket.ReceiveAsync(new ArraySegment<byte>(buffer), cancellationToken);
            if (result.MessageType == WebSocketMessageType.Close)
            {
                break;
            }

            if (result.MessageType != WebSocketMessageType.Binary)
            {
                throw new InvalidDataException("File phải được gửi bằng frame binary.");
            }

            await output.WriteAsync(buffer.AsMemory(0, result.Count), cancellationToken);
            received += result.Count;
            if (result.EndOfMessage)
            {
                break;
            }
        }

        if (expectedLength > 0 && received != expectedLength)
        {
            throw new EndOfStreamException("Dung lượng file nhận không khớp.");
        }
    }

    private static IReadOnlyList<IPEndPoint> GetBroadcastEndpoints()
    {
        var endpoints = new List<IPEndPoint> { new(IPAddress.Broadcast, DiscoveryPort) };
        foreach (var item in GetActiveIPv4Interfaces())
        {
            endpoints.Add(new IPEndPoint(GetBroadcastAddress(item.Address, item.Mask), DiscoveryPort));
        }

        return endpoints.DistinctBy(endpoint => endpoint.Address.ToString()).ToList();
    }

    private static IReadOnlyList<(IPAddress Address, IPAddress Mask)> GetActiveIPv4Interfaces()
    {
        var blocked = new[] { "virtualbox", "vmware", "wsl", "hyper-v", "docker", "loopback" };
        return NetworkInterface.GetAllNetworkInterfaces()
            .Where(nic => nic.OperationalStatus == OperationalStatus.Up)
            .Where(nic => nic.NetworkInterfaceType is not NetworkInterfaceType.Loopback and not NetworkInterfaceType.Tunnel)
            .Where(nic => !blocked.Any(term => nic.Description.Contains(term, StringComparison.OrdinalIgnoreCase) || nic.Name.Contains(term, StringComparison.OrdinalIgnoreCase)))
            .SelectMany(nic => nic.GetIPProperties().UnicastAddresses
                .Where(address => address.Address.AddressFamily == AddressFamily.InterNetwork && !IPAddress.IsLoopback(address.Address))
                .Select(address => (address.Address, address.IPv4Mask)))
            .Where(item => item.IPv4Mask is not null)
            .Select(item => (item.Address, item.IPv4Mask!))
            .ToList();
    }

    private static IPAddress GetBroadcastAddress(IPAddress address, IPAddress subnetMask)
    {
        var ip = address.GetAddressBytes();
        var mask = subnetMask.GetAddressBytes();
        var broadcast = new byte[ip.Length];
        for (var index = 0; index < broadcast.Length; index++)
        {
            broadcast[index] = (byte)(ip[index] | ~mask[index]);
        }

        return new IPAddress(broadcast);
    }

    private static EmbeddedLanPeer ClonePeer(EmbeddedLanPeer peer)
    {
        return new EmbeddedLanPeer
        {
            DeviceId = peer.DeviceId,
            UserId = peer.UserId,
            Username = peer.Username,
            DisplayName = peer.DisplayName,
            PublicKey = peer.PublicKey,
            AvatarText = peer.AvatarText,
            Address = peer.Address,
            ChatPort = peer.ChatPort,
            LastSeen = peer.LastSeen
        };
    }

    private static string UniqueReceivedPath(string fileName)
    {
        var safeName = string.Join("_", fileName.Split(Path.GetInvalidFileNameChars(), StringSplitOptions.RemoveEmptyEntries));
        if (string.IsNullOrWhiteSpace(safeName))
        {
            safeName = "received-file";
        }

        var directory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "LAN Chat", "Received");
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, safeName);
        if (!File.Exists(path))
        {
            return path;
        }

        var name = Path.GetFileNameWithoutExtension(safeName);
        var ext = Path.GetExtension(safeName);
        return Path.Combine(directory, $"{name}-{DateTime.Now:yyyyMMddHHmmss}{ext}");
    }

    public static string FormatFileSize(long size)
    {
        string[] units = ["B", "KB", "MB", "GB"];
        var value = (double)Math.Max(0, size);
        var unit = 0;
        while (value >= 1024 && unit < units.Length - 1)
        {
            value /= 1024;
            unit++;
        }

        return unit == 0 ? $"{value:0} {units[unit]}" : $"{value:0.##} {units[unit]}";
    }

    public void Dispose()
    {
        _cts?.Cancel();
        _udpReceiver?.Dispose();
        _udpSender?.Dispose();
        _listener?.Stop();
        _cts?.Dispose();
    }

    private sealed class PresencePacket
    {
        public string Protocol { get; set; } = "";
        public Guid UserId { get; set; }
        public string DeviceId { get; set; } = "";
        public string Username { get; set; } = "";
        public string DisplayName { get; set; } = "";
        public string PublicKey { get; set; } = "";
        public string AvatarText { get; set; } = "";
        public int ChatPort { get; set; }
    }

    private sealed class ModuleEnvelope
    {
        public string Protocol { get; set; } = "";
        public Guid MessageId { get; set; } = Guid.NewGuid();
        public Guid SenderUserId { get; set; }
        public string SenderId { get; set; } = "";
        public string SenderUsername { get; set; } = "";
        public string SenderName { get; set; } = "";
        public string SenderPublicKey { get; set; } = "";
        public string SenderAvatarText { get; set; } = "";
        public int SenderChatPort { get; set; }
        public Guid ReceiverUserId { get; set; }
        public string ReceiverId { get; set; } = "";
        public string Type { get; set; } = "Text";
        public string Content { get; set; } = "";
        public string FileName { get; set; } = "";
        public string Extension { get; set; } = "";
        public long FileSize { get; set; }
        public long EncryptedFileSize { get; set; }
        public string CipherText { get; set; } = "";
        public string Nonce { get; set; } = "";
        public string AuthTag { get; set; } = "";
        public string EncryptedKeyForSender { get; set; } = "";
        public string EncryptedKeyForReceiver { get; set; } = "";
        public Guid ReceiptForMessageId { get; set; }
        public string ReceiptStatus { get; set; } = "";
        public DateTime Timestamp { get; set; } = DateTime.Now;
    }
}
