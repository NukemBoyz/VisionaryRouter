using System.Text.Json;
using System.Threading.Channels;

namespace VisionaryRouter.Server.Services;

/// <summary>
/// Fans server-side events out to connected browsers.
///
/// The backend polls the endpoints; browsers are told when something changed rather than
/// asking every second. Server-Sent Events carry this rather than a websocket library:
/// the traffic is one-directional, and the browser side is a few lines of built-in
/// EventSource with nothing to install or build.
/// </summary>
public sealed class EventStream
{
    private readonly List<Channel<string>> _subscribers = [];
    private readonly Lock _subscribersLock = new();

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public IDisposable Subscribe(out ChannelReader<string> reader)
    {
        // Bounded and drop-oldest: a browser that stops reading must not grow a queue
        // forever, and stale status frames are not worth keeping anyway.
        var channel = Channel.CreateBounded<string>(new BoundedChannelOptions(64)
        {
            FullMode = BoundedChannelFullMode.DropOldest,
            SingleReader = true
        });

        lock (_subscribersLock)
        {
            _subscribers.Add(channel);
        }

        reader = channel.Reader;
        return new Subscription(this, channel);
    }

    public void Publish(string eventName, object payload)
    {
        var json = JsonSerializer.Serialize(payload, JsonOptions);
        var frame = $"event: {eventName}\ndata: {json}\n\n";

        lock (_subscribersLock)
        {
            foreach (var subscriber in _subscribers)
            {
                subscriber.Writer.TryWrite(frame);
            }
        }
    }

    private void Unsubscribe(Channel<string> channel)
    {
        lock (_subscribersLock)
        {
            _subscribers.Remove(channel);
        }

        channel.Writer.TryComplete();
    }

    private sealed class Subscription : IDisposable
    {
        private readonly EventStream _stream;
        private readonly Channel<string> _channel;

        public Subscription(EventStream stream, Channel<string> channel)
        {
            _stream = stream;
            _channel = channel;
        }

        public void Dispose() => _stream.Unsubscribe(_channel);
    }
}
