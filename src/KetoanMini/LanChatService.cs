using System.Net;
using System.IO;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace KetoanMini;

public sealed class LanChatSignalEventArgs : EventArgs
{
    public LanChatSignalEventArgs(string senderUsername, string signalType)
    {
        SenderUsername = senderUsername;
        SignalType = signalType;
    }

    public string SenderUsername { get; }
    public string SignalType { get; }
}

public sealed class LanChatService : IDisposable
{
    private const string Protocol = "KETOAN_MINI_LAN_CHAT_V1";
    private const int PresencePort = 51519;
    private const int FirstFilePort = 51520;
    private const int LastFilePort = 51540;
    private const int FirstChatPort = 51620;
    private const int LastChatPort = 51640;
    private static readonly TimeSpan PeerTimeout = TimeSpan.FromSeconds(10);

    private sealed class PresencePacket
    {
        public string Protocol { get; set; } = "";
        public string MachineName { get; set; } = "";
        public string Username { get; set; } = "";
        public string DisplayName { get; set; } = "";
        public int FilePort { get; set; }
        public int ChatPort { get; set; }
    }

    private sealed class ChatSignalPacket
    {
        public string Protocol { get; set; } = "";
        public string SenderUsername { get; set; } = "";
        public string SignalType { get; set; } = "";
        public DateTime SentAt { get; set; } = DateTime.Now;
    }

    private sealed record OutgoingFile(string Path, DateTime ExpiresAt);

    private readonly object _sync = new();
    private readonly Dictionary<string, LanChatPeer> _peers = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, OutgoingFile> _outgoingFiles = new(StringComparer.OrdinalIgnoreCase);
    private CancellationTokenSource? _cts;
    private UdpClient? _presenceReceiver;
    private UdpClient? _presenceSender;
    private TcpListener? _fileListener;
    private TcpListener? _chatSignalListener;
    private AppUser? _currentUser;
    private int _filePort;
    private int _chatPort;
    private bool _disposed;

    public event EventHandler<LanChatSignalEventArgs>? ChatSignalReceived;

    public int FilePort => _filePort;
    public int ChatPort => _chatPort;

    public void Start(AppUser currentUser)
    {
        if (_cts is not null)
        {
            return;
        }

        _currentUser = currentUser;
        _cts = new CancellationTokenSource();
        StartFileListener(_cts.Token);
        StartChatSignalListener(_cts.Token);
        StartPresence(_cts.Token);
    }

    public IReadOnlyList<LanChatPeer> GetPeers()
    {
        lock (_sync)
        {
            PruneLocked();
            return _peers.Values
                .OrderBy(peer => peer.Username, StringComparer.CurrentCultureIgnoreCase)
                .Select(ClonePeer)
                .ToList();
        }
    }

    public LanChatPeer? FindPeer(string username)
    {
        lock (_sync)
        {
            PruneLocked();
            return _peers.TryGetValue(username.Trim(), out var peer) ? ClonePeer(peer) : null;
        }
    }

    public string RegisterOutgoingFile(string filePath, TimeSpan ttl)
    {
        if (!File.Exists(filePath))
        {
            throw new FileNotFoundException("Không tìm thấy file cần gửi.", filePath);
        }

        var token = Convert.ToHexString(RandomNumberGenerator.GetBytes(24));
        lock (_sync)
        {
            _outgoingFiles[token] = new OutgoingFile(filePath, DateTime.Now.Add(ttl));
        }

        return token;
    }

    public async Task SendChatSignalAsync(LanChatPeer peer, string signalType, CancellationToken cancellationToken = default)
    {
        if (_currentUser is null || peer.ChatPort <= 0 || string.IsNullOrWhiteSpace(peer.Address))
        {
            return;
        }

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(3));

        using var socket = new ClientWebSocket();
        var uri = new Uri($"ws://{peer.Address}:{peer.ChatPort}/ketoan-mini-chat/");
        await socket.ConnectAsync(uri, timeout.Token);

        var packet = new ChatSignalPacket
        {
            Protocol = Protocol,
            SenderUsername = _currentUser.Username,
            SignalType = string.IsNullOrWhiteSpace(signalType) ? "chat_changed" : signalType,
            SentAt = DateTime.Now
        };
        var payload = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(packet));
        await socket.SendAsync(payload, WebSocketMessageType.Text, true, timeout.Token);
        try
        {
            await socket.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, "done", timeout.Token);
        }
        catch
        {
            // The signal is fire-and-forget. A close-frame error is not user-visible.
        }
    }

    public async Task ReceiveFileAsync(ChatMessage offer, string savePath, CancellationToken cancellationToken)
    {
        if (!offer.IsPendingFileOffer)
        {
            throw new InvalidOperationException("Lời mời nhận file đã hết hạn hoặc không còn chờ nhận.");
        }

        if (string.IsNullOrWhiteSpace(offer.SenderAddress) || offer.SenderPort <= 0 || string.IsNullOrWhiteSpace(offer.TransferToken))
        {
            throw new InvalidOperationException("Thiếu thông tin máy gửi file.");
        }

        var directory = Path.GetDirectoryName(savePath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        using var client = new TcpClient();
        await client.ConnectAsync(offer.SenderAddress, offer.SenderPort, cancellationToken);
        await using var stream = client.GetStream();
        var request = Encoding.UTF8.GetBytes($"GET|{offer.TransferToken}\n");
        await stream.WriteAsync(request, cancellationToken);
        await stream.FlushAsync(cancellationToken);

        var lengthBytes = new byte[sizeof(long)];
        await stream.ReadExactlyAsync(lengthBytes, cancellationToken);
        var length = BitConverter.ToInt64(lengthBytes, 0);
        if (length < 0)
        {
            throw new InvalidOperationException("Máy gửi từ chối truyền file.");
        }

        await using var output = File.Create(savePath);
        var buffer = new byte[64 * 1024];
        long remaining = length;
        while (remaining > 0)
        {
            var read = await stream.ReadAsync(buffer.AsMemory(0, (int)Math.Min(buffer.Length, remaining)), cancellationToken);
            if (read == 0)
            {
                throw new IOException("Kết nối bị ngắt khi đang nhận file.");
            }

            await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
            remaining -= read;
        }
    }

    public string GetLocalAddressForPeer(string peerAddress)
    {
        try
        {
            using var socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
            socket.Connect(peerAddress, 9);
            return ((IPEndPoint)socket.LocalEndPoint!).Address.ToString();
        }
        catch
        {
            return GetFirstLocalIpv4Address();
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _cts?.Cancel();
        _presenceReceiver?.Dispose();
        _presenceSender?.Dispose();
        _fileListener?.Stop();
        _chatSignalListener?.Stop();
        _cts?.Dispose();
    }

    private void StartPresence(CancellationToken cancellationToken)
    {
        try
        {
            _presenceReceiver = new UdpClient(AddressFamily.InterNetwork);
            _presenceReceiver.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
            _presenceReceiver.Client.Bind(new IPEndPoint(IPAddress.Any, PresencePort));
            _presenceSender = new UdpClient { EnableBroadcast = true };

            _ = Task.Run(() => ReceivePresenceLoopAsync(cancellationToken), cancellationToken);
            _ = Task.Run(() => BroadcastPresenceLoopAsync(cancellationToken), cancellationToken);
        }
        catch
        {
            _presenceReceiver?.Dispose();
            _presenceSender?.Dispose();
            _presenceReceiver = null;
            _presenceSender = null;
        }
    }

    private void StartFileListener(CancellationToken cancellationToken)
    {
        for (var port = FirstFilePort; port <= LastFilePort; port++)
        {
            try
            {
                _fileListener = new TcpListener(IPAddress.Any, port);
                _fileListener.Start();
                _filePort = port;
                _ = Task.Run(() => AcceptFileClientsLoopAsync(cancellationToken), cancellationToken);
                return;
            }
            catch
            {
                _fileListener?.Stop();
                _fileListener = null;
            }
        }
    }

    private void StartChatSignalListener(CancellationToken cancellationToken)
    {
        for (var port = FirstChatPort; port <= LastChatPort; port++)
        {
            try
            {
                _chatSignalListener = new TcpListener(IPAddress.Any, port);
                _chatSignalListener.Start();
                _chatPort = port;
                _ = Task.Run(() => AcceptChatSignalClientsLoopAsync(cancellationToken), cancellationToken);
                return;
            }
            catch
            {
                _chatSignalListener?.Stop();
                _chatSignalListener = null;
            }
        }
    }

    private async Task BroadcastPresenceLoopAsync(CancellationToken cancellationToken)
    {
        var endpoint = new IPEndPoint(IPAddress.Broadcast, PresencePort);
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                if (_presenceSender is not null && _currentUser is not null && _filePort > 0 && _chatPort > 0)
                {
                    var packet = new PresencePacket
                    {
                        Protocol = Protocol,
                        MachineName = Environment.MachineName,
                        Username = _currentUser.Username,
                        DisplayName = _currentUser.DisplayName,
                        FilePort = _filePort,
                        ChatPort = _chatPort
                    };
                    var bytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(packet));
                    await _presenceSender.SendAsync(bytes, endpoint, cancellationToken);
                }
            }
            catch
            {
                // Presence is best-effort. The timer will try again.
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
                if (_presenceReceiver is null)
                {
                    return;
                }

                var result = await _presenceReceiver.ReceiveAsync(cancellationToken);
                var packet = JsonSerializer.Deserialize<PresencePacket>(Encoding.UTF8.GetString(result.Buffer));
                if (packet is null ||
                    !string.Equals(packet.Protocol, Protocol, StringComparison.Ordinal) ||
                    string.IsNullOrWhiteSpace(packet.Username) ||
                    packet.FilePort <= 0 ||
                    packet.ChatPort <= 0)
                {
                    continue;
                }

                if (_currentUser is not null && string.Equals(packet.Username, _currentUser.Username, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                lock (_sync)
                {
                    _peers[packet.Username] = new LanChatPeer
                    {
                        Username = packet.Username,
                        DisplayName = packet.DisplayName,
                        MachineName = packet.MachineName,
                        Address = result.RemoteEndPoint.Address.ToString(),
                        FilePort = packet.FilePort,
                        ChatPort = packet.ChatPort,
                        LastSeen = DateTime.Now
                    };
                }
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch
            {
                // Ignore malformed packets and keep listening.
            }
        }
    }

    private async Task AcceptChatSignalClientsLoopAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                if (_chatSignalListener is null)
                {
                    return;
                }

                var client = await _chatSignalListener.AcceptTcpClientAsync(cancellationToken);
                _ = Task.Run(() => HandleChatSignalClientAsync(client, cancellationToken), cancellationToken);
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch
            {
                await Task.Delay(1_000, cancellationToken);
            }
        }
    }

    private async Task HandleChatSignalClientAsync(TcpClient client, CancellationToken cancellationToken)
    {
        using var clientScope = client;
        try
        {
            await using var stream = client.GetStream();
            var headers = await ReadHttpHeaderAsync(stream, cancellationToken);
            var key = GetHeaderValue(headers, "Sec-WebSocket-Key");
            if (string.IsNullOrWhiteSpace(key))
            {
                return;
            }

            var response =
                "HTTP/1.1 101 Switching Protocols\r\n" +
                "Upgrade: websocket\r\n" +
                "Connection: Upgrade\r\n" +
                $"Sec-WebSocket-Accept: {ComputeWebSocketAcceptKey(key)}\r\n\r\n";
            await stream.WriteAsync(Encoding.ASCII.GetBytes(response), cancellationToken);

            var text = await ReadWebSocketTextFrameAsync(stream, cancellationToken);
            if (string.IsNullOrWhiteSpace(text))
            {
                return;
            }

            var packet = JsonSerializer.Deserialize<ChatSignalPacket>(text);
            if (packet is null ||
                !string.Equals(packet.Protocol, Protocol, StringComparison.Ordinal) ||
                string.IsNullOrWhiteSpace(packet.SenderUsername))
            {
                return;
            }

            ChatSignalReceived?.Invoke(this, new LanChatSignalEventArgs(packet.SenderUsername, packet.SignalType));
            await WriteWebSocketTextFrameAsync(stream, "ok", cancellationToken);
        }
        catch
        {
            // Realtime chat signals are best-effort. SQL history remains the source of truth.
        }
    }

    private async Task AcceptFileClientsLoopAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                if (_fileListener is null)
                {
                    return;
                }

                var client = await _fileListener.AcceptTcpClientAsync(cancellationToken);
                _ = Task.Run(() => HandleFileClientAsync(client, cancellationToken), cancellationToken);
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch
            {
                await Task.Delay(1_000, cancellationToken);
            }
        }
    }

    private async Task HandleFileClientAsync(TcpClient client, CancellationToken cancellationToken)
    {
        using var clientScope = client;
        try
        {
            await using var stream = client.GetStream();
            using var reader = new StreamReader(stream, Encoding.UTF8, false, 1024, leaveOpen: true);
            var line = await reader.ReadLineAsync(cancellationToken);
            var parts = (line ?? "").Split('|', 2);
            if (parts.Length != 2 || !string.Equals(parts[0], "GET", StringComparison.OrdinalIgnoreCase))
            {
                await stream.WriteAsync(BitConverter.GetBytes(-1L), cancellationToken);
                return;
            }

            OutgoingFile? file;
            lock (_sync)
            {
                PruneLocked();
                _outgoingFiles.TryGetValue(parts[1], out file);
            }

            if (file is null || !File.Exists(file.Path))
            {
                await stream.WriteAsync(BitConverter.GetBytes(-1L), cancellationToken);
                return;
            }

            await using var input = File.OpenRead(file.Path);
            await stream.WriteAsync(BitConverter.GetBytes(input.Length), cancellationToken);
            await input.CopyToAsync(stream, cancellationToken);
        }
        catch
        {
            // The receiver will show the transfer error.
        }
    }

    private void PruneLocked()
    {
        var stalePeers = _peers
            .Where(item => item.Value.LastSeen < DateTime.Now.Subtract(PeerTimeout))
            .Select(item => item.Key)
            .ToList();
        foreach (var key in stalePeers)
        {
            _peers.Remove(key);
        }

        var expiredFiles = _outgoingFiles
            .Where(item => item.Value.ExpiresAt <= DateTime.Now)
            .Select(item => item.Key)
            .ToList();
        foreach (var key in expiredFiles)
        {
            _outgoingFiles.Remove(key);
        }
    }

    private static LanChatPeer ClonePeer(LanChatPeer peer)
    {
        return new LanChatPeer
        {
            Username = peer.Username,
            DisplayName = peer.DisplayName,
            MachineName = peer.MachineName,
            Address = peer.Address,
            FilePort = peer.FilePort,
            ChatPort = peer.ChatPort,
            LastSeen = peer.LastSeen
        };
    }

    private static async Task<string> ReadHttpHeaderAsync(Stream stream, CancellationToken cancellationToken)
    {
        var bytes = new List<byte>(1024);
        var buffer = new byte[1];
        while (bytes.Count < 8192)
        {
            var read = await stream.ReadAsync(buffer, cancellationToken);
            if (read == 0)
            {
                break;
            }

            bytes.Add(buffer[0]);
            if (bytes.Count >= 4 &&
                bytes[^4] == '\r' &&
                bytes[^3] == '\n' &&
                bytes[^2] == '\r' &&
                bytes[^1] == '\n')
            {
                break;
            }
        }

        return Encoding.ASCII.GetString(bytes.ToArray());
    }

    private static string GetHeaderValue(string headers, string name)
    {
        foreach (var line in headers.Split(["\r\n"], StringSplitOptions.RemoveEmptyEntries))
        {
            var separator = line.IndexOf(':');
            if (separator <= 0)
            {
                continue;
            }

            if (string.Equals(line[..separator].Trim(), name, StringComparison.OrdinalIgnoreCase))
            {
                return line[(separator + 1)..].Trim();
            }
        }

        return "";
    }

    private static string ComputeWebSocketAcceptKey(string clientKey)
    {
        const string magic = "258EAFA5-E914-47DA-95CA-C5AB0DC85B11";
        var hash = SHA1.HashData(Encoding.ASCII.GetBytes(clientKey.Trim() + magic));
        return Convert.ToBase64String(hash);
    }

    private static async Task<string> ReadWebSocketTextFrameAsync(Stream stream, CancellationToken cancellationToken)
    {
        var header = new byte[2];
        await ReadExactBytesAsync(stream, header, cancellationToken);

        var opcode = header[0] & 0x0F;
        if (opcode == 8)
        {
            return "";
        }

        if (opcode != 1)
        {
            throw new InvalidOperationException("Unsupported websocket frame.");
        }

        var masked = (header[1] & 0x80) != 0;
        ulong length = (ulong)(header[1] & 0x7F);
        if (length == 126)
        {
            var lengthBytes = new byte[2];
            await ReadExactBytesAsync(stream, lengthBytes, cancellationToken);
            length = (ulong)((lengthBytes[0] << 8) | lengthBytes[1]);
        }
        else if (length == 127)
        {
            var lengthBytes = new byte[8];
            await ReadExactBytesAsync(stream, lengthBytes, cancellationToken);
            length = 0;
            foreach (var item in lengthBytes)
            {
                length = (length << 8) | item;
            }
        }

        if (length > 64 * 1024)
        {
            throw new InvalidOperationException("Websocket signal too large.");
        }

        var mask = new byte[4];
        if (masked)
        {
            await ReadExactBytesAsync(stream, mask, cancellationToken);
        }

        var payload = new byte[(int)length];
        if (payload.Length > 0)
        {
            await ReadExactBytesAsync(stream, payload, cancellationToken);
        }

        if (masked)
        {
            for (var index = 0; index < payload.Length; index++)
            {
                payload[index] = (byte)(payload[index] ^ mask[index % 4]);
            }
        }

        return Encoding.UTF8.GetString(payload);
    }

    private static async Task WriteWebSocketTextFrameAsync(Stream stream, string text, CancellationToken cancellationToken)
    {
        var payload = Encoding.UTF8.GetBytes(text);
        var header = payload.Length <= 125
            ? new byte[] { 0x81, (byte)payload.Length }
            : new byte[] { 0x81, 126, (byte)(payload.Length >> 8), (byte)payload.Length };
        await stream.WriteAsync(header, cancellationToken);
        await stream.WriteAsync(payload, cancellationToken);
    }

    private static async Task ReadExactBytesAsync(Stream stream, byte[] buffer, CancellationToken cancellationToken)
    {
        var offset = 0;
        while (offset < buffer.Length)
        {
            var read = await stream.ReadAsync(buffer.AsMemory(offset, buffer.Length - offset), cancellationToken);
            if (read == 0)
            {
                throw new EndOfStreamException();
            }

            offset += read;
        }
    }

    private static string GetFirstLocalIpv4Address()
    {
        try
        {
            return Dns.GetHostAddresses(Dns.GetHostName())
                .FirstOrDefault(address => address.AddressFamily == AddressFamily.InterNetwork && !IPAddress.IsLoopback(address))
                ?.ToString() ?? "127.0.0.1";
        }
        catch
        {
            return "127.0.0.1";
        }
    }
}
