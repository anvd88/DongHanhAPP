using Microsoft.AspNetCore.SignalR.Client;

namespace KetoanMini;

internal sealed class RealtimeChangesService : IDisposable
{
    private readonly string _hubUrl;
    private readonly CancellationTokenSource _cts = new();
    private HubConnection? _connection;
    private int _started;

    public event EventHandler? Changed;

    public RealtimeChangesService(string hubUrl)
    {
        _hubUrl = hubUrl;
    }

    public void Start()
    {
        if (Interlocked.Exchange(ref _started, 1) == 1)
        {
            return;
        }

        _connection = new HubConnectionBuilder()
            .WithUrl(_hubUrl)
            .WithAutomaticReconnect([TimeSpan.Zero, TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(10)])
            .Build();

        _connection.On<string>("changed", _ => Changed?.Invoke(this, EventArgs.Empty));
        _ = Task.Run(ConnectUntilReadyAsync);
    }

    private async Task ConnectUntilReadyAsync()
    {
        while (!_cts.IsCancellationRequested)
        {
            try
            {
                if (_connection is not null)
                {
                    await _connection.StartAsync(_cts.Token);
                }

                return;
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch
            {
                try { await Task.Delay(TimeSpan.FromSeconds(3), _cts.Token); }
                catch (OperationCanceledException) { return; }
            }
        }
    }

    public void Dispose()
    {
        _cts.Cancel();
        try
        {
            _connection?.DisposeAsync().AsTask().GetAwaiter().GetResult();
        }
        catch
        {
            // Real-time is best-effort; closing the app should not be blocked by hub shutdown errors.
        }

        _cts.Dispose();
    }
}
