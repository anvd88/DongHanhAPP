using System.Net;
using System.Net.Sockets;
using System.Text.Json;

namespace KetoanMini;

public sealed class SessionControlEventArgs : EventArgs
{
    public SessionControlEventArgs(string reason) => Reason = reason;

    /// <summary>User-facing message explaining why the session ended.</summary>
    public string Reason { get; }
}

/// <summary>
/// Event-driven (push) session control over the LAN. Instead of polling the
/// database on a fixed interval, a client broadcasts a small UDP signal the
/// instant it takes over a login or an admin locks an account; every running
/// client listens and logs out immediately when the signal targets its own
/// user. This is the WebSocket-style "listen for events" path the user asked
/// for — it reuses the same UDP broadcast pattern the LAN chat presence uses.
///
/// UDP broadcast is best-effort (a datagram can be dropped, blocked by a
/// firewall, or fail to cross a subnet), so the DB heartbeat in MainForm stays
/// in place as a slower safety net. The push makes the common case instant.
/// </summary>
public sealed class SessionControlService : IDisposable
{
    private const string Protocol = "KETOAN_MINI_SESSION_CTRL_V1";
    private const int ControlPort = 51517;

    public const string SignalLogin = "login";
    public const string SignalLock = "lock";

    private sealed class ControlPacket
    {
        public string Protocol { get; set; } = "";
        public string Signal { get; set; } = "";
        public string Username { get; set; } = "";
        public string SessionToken { get; set; } = "";
    }

    private CancellationTokenSource? _cts;
    private UdpClient? _receiver;
    private UdpClient? _sender;
    private string _username = "";
    private string _sessionToken = "";
    private bool _disposed;

    /// <summary>Raised (on a background thread) when this client's user must log out now.</summary>
    public event EventHandler<SessionControlEventArgs>? ForceLogout;

    public void Start(string username, string sessionToken)
    {
        if (_cts is not null) return;
        _username = (username ?? "").Trim();
        _sessionToken = sessionToken ?? "";
        _cts = new CancellationTokenSource();
        try
        {
            _receiver = new UdpClient(AddressFamily.InterNetwork);
            _receiver.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
            _receiver.Client.Bind(new IPEndPoint(IPAddress.Any, ControlPort));
            _sender = new UdpClient { EnableBroadcast = true };
            _ = Task.Run(() => ReceiveLoopAsync(_cts.Token), _cts.Token);
        }
        catch
        {
            // Port busy / firewall blocked — push is disabled, but the DB
            // heartbeat fallback in MainForm still logs the user out.
            _receiver?.Dispose();
            _sender?.Dispose();
            _receiver = null;
            _sender = null;
        }
    }

    /// <summary>Tell any other live session of <paramref name="username"/> to log out (single login).</summary>
    public void BroadcastLoginTakeover(string username, string newSessionToken)
        => Broadcast(SignalLogin, username, newSessionToken);

    /// <summary>Tell the user (if online on any machine) that they were locked and must log out now.</summary>
    public void BroadcastAccountLocked(string username)
        => Broadcast(SignalLock, username, "");

    private void Broadcast(string signal, string username, string sessionToken)
    {
        if (_sender is null || string.IsNullOrWhiteSpace(username)) return;
        try
        {
            var packet = new ControlPacket
            {
                Protocol = Protocol,
                Signal = signal,
                Username = username.Trim(),
                SessionToken = sessionToken ?? ""
            };
            var bytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(packet));
            var endpoint = new IPEndPoint(IPAddress.Broadcast, ControlPort);
            // Send a few times — a single UDP datagram can be dropped on a busy LAN.
            for (var i = 0; i < 3; i++) _sender.Send(bytes, bytes.Length, endpoint);
        }
        catch
        {
            // Best-effort push; the heartbeat covers a lost signal.
        }
    }

    private async Task ReceiveLoopAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                if (_receiver is null) return;

                var result = await _receiver.ReceiveAsync(cancellationToken);
                var packet = JsonSerializer.Deserialize<ControlPacket>(Encoding.UTF8.GetString(result.Buffer));
                if (packet is null ||
                    !string.Equals(packet.Protocol, Protocol, StringComparison.Ordinal) ||
                    !string.Equals(packet.Username, _username, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (string.Equals(packet.Signal, SignalLogin, StringComparison.Ordinal))
                {
                    // Another machine logged in as me. If it isn't my own session, end this one.
                    if (!string.Equals(packet.SessionToken, _sessionToken, StringComparison.Ordinal))
                    {
                        Raise("Tài khoản của bạn vừa đăng nhập ở một máy khác.\nPhiên làm việc tại đây đã kết thúc.");
                        return;
                    }
                }
                else if (string.Equals(packet.Signal, SignalLock, StringComparison.Ordinal))
                {
                    Raise("Tài khoản của bạn đã bị khoá.");
                    return;
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

    private void Raise(string reason) => ForceLogout?.Invoke(this, new SessionControlEventArgs(reason));

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _cts?.Cancel();
        _receiver?.Dispose();
        _sender?.Dispose();
        _cts?.Dispose();
    }
}
