using System.Collections.Concurrent;
using System.Threading.Channels;

namespace KetoanMini.Api.BuildingBlocks.Realtime;

/// <summary>Bounded wake-up fanout. Durable data remains in PostgreSQL; dropped wakes are harmless.</summary>
public sealed class RealtimeWakeHub
{
    private readonly ConcurrentDictionary<Guid, Channel<long>> _subscribers = new();

    public Subscription Subscribe()
    {
        var id = Guid.NewGuid();
        var channel = Channel.CreateBounded<long>(new BoundedChannelOptions(1)
        {
            SingleReader = true,
            SingleWriter = false,
            FullMode = BoundedChannelFullMode.DropOldest,
        });
        _subscribers[id] = channel;
        return new Subscription(channel.Reader, () => _subscribers.TryRemove(id, out _));
    }

    public void Publish(long cursor)
    {
        foreach (var channel in _subscribers.Values) channel.Writer.TryWrite(cursor);
    }

    public sealed class Subscription(ChannelReader<long> reader, Action dispose) : IDisposable
    {
        public ChannelReader<long> Reader { get; } = reader;
        public void Dispose() => dispose();
    }
}
