using VisionaryRouter.Core.Devices;
using VisionaryRouter.Core.Models;

namespace VisionaryRouter.Server.Services;

public sealed class PollingOptions
{
    /// <summary>How often decoders are asked what they are currently following.</summary>
    public TimeSpan RoutePollInterval { get; set; } = TimeSpan.FromSeconds(3);

    /// <summary>How often every endpoint gets a liveness check.</summary>
    public TimeSpan HeartbeatInterval { get; set; } = TimeSpan.FromSeconds(10);

    /// <summary>Endpoints contacted at once. Keeps a large system from being hammered.</summary>
    public int MaxConcurrency { get; set; } = 8;
}

/// <summary>
/// Keeps the application's picture of the system matched to reality.
///
/// The router asks the decoders what they are actually following rather than assuming the
/// last command it sent is still in force. That is what lets the UI notice a route changed
/// by Vision Lite, a control system, or a second copy of this application - and it means a
/// failed switch shows as failed instead of as whatever we hoped would happen.
/// </summary>
public sealed class PollingService : BackgroundService
{
    private readonly DeviceRegistry _registry;
    private readonly VisionaryDeviceClient _client;
    private readonly EventStream _events;
    private readonly PollingOptions _options;
    private readonly ILogger<PollingService> _logger;

    private DateTime _lastHeartbeatUtc = DateTime.MinValue;

    public PollingService(
        DeviceRegistry registry,
        VisionaryDeviceClient client,
        EventStream events,
        PollingOptions options,
        ILogger<PollingService> logger)
    {
        _registry = registry;
        _client = client;
        _events = events;
        _options = options;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Polling started: routes every {Route}s, heartbeat every {Heartbeat}s",
            _options.RoutePollInterval.TotalSeconds, _options.HeartbeatInterval.TotalSeconds);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var changed = await PollRoutesAsync(stoppingToken);

                if (DateTime.UtcNow - _lastHeartbeatUtc >= _options.HeartbeatInterval)
                {
                    _lastHeartbeatUtc = DateTime.UtcNow;
                    changed |= await HeartbeatAsync(stoppingToken);
                }

                if (changed)
                {
                    _events.Publish("devices", DeviceView.From(_registry));
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                // One bad poll must never take the loop down - it is the only thing keeping
                // the UI honest about what the hardware is doing.
                _logger.LogError(ex, "Polling pass failed; continuing");
            }

            try
            {
                await Task.Delay(_options.RoutePollInterval, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }

        _logger.LogInformation("Polling stopped");
    }

    private async Task<bool> PollRoutesAsync(CancellationToken cancellationToken)
    {
        var decoders = _registry.Decoders().Where(d => d.Enabled).ToList();

        if (decoders.Count == 0)
        {
            return false;
        }

        var changed = false;
        using var gate = new SemaphoreSlim(_options.MaxConcurrency, _options.MaxConcurrency);

        await Task.WhenAll(decoders.Select(async decoder =>
        {
            await gate.WaitAsync(cancellationToken);
            try
            {
                var previousSource = decoder.CurrentEncoderIp;
                var previousState = decoder.OnlineState;
                var previousConnected = decoder.StreamConnected;

                bool? connected;
                string? current;
                if (decoder.RouteCleared)
                {
                    // Cleared 4-Series decoders retain their previous STREAM.HOST and expose
                    // no readable connection key, so a health probe must not resurrect it.
                    connected = false;
                    current = null;
                    await _client.PingAsync(decoder, cancellationToken);
                }
                else
                {
                    connected = await _client.ReadStreamConnectedAsync(decoder, cancellationToken);
                    current = connected == false ? null : await _client.ReadCurrentSourceAsync(decoder, cancellationToken);
                }

                if (current != previousSource || decoder.OnlineState != previousState || connected != previousConnected)
                {
                    if (current != previousSource && previousSource is not null)
                    {
                        _logger.LogInformation(
                            "Decoder {Decoder} source changed externally: {Old} -> {New}",
                            decoder.Name, previousSource, current ?? "none");
                    }

                    changed = true;
                }
            }
            finally
            {
                gate.Release();
            }
        }));

        return changed;
    }

    private async Task<bool> HeartbeatAsync(CancellationToken cancellationToken)
    {
        // Decoders already proved themselves alive by answering the route poll, so the
        // heartbeat only needs to cover encoders.
        var devices = _registry.All()
            .Where(d => d.Enabled && d.Type != DeviceType.Decoder)
            .ToList();

        if (devices.Count == 0)
        {
            return false;
        }

        var changed = false;
        using var gate = new SemaphoreSlim(_options.MaxConcurrency, _options.MaxConcurrency);

        await Task.WhenAll(devices.Select(async device =>
        {
            await gate.WaitAsync(cancellationToken);
            try
            {
                var previousState = device.OnlineState;
                var previousInput = device.VideoInput;
                var previousSignal = device.VideoSignalPresent;
                var previousTiming = device.VideoTiming;

                if (device.Type == DeviceType.Encoder)
                {
                    await _client.ReadVideoInputAsync(device, cancellationToken);
                    if (device.OnlineState == OnlineState.Online)
                    {
                        await _client.ReadVideoSignalAsync(device, cancellationToken);
                    }
                }
                else
                {
                    await _client.PingAsync(device, cancellationToken);
                }

                if (device.OnlineState != previousState || device.VideoInput != previousInput ||
                    device.VideoSignalPresent != previousSignal || device.VideoTiming != previousTiming)
                {
                    _logger.LogInformation(
                        "Device {Device} is now {State}; input {Input}",
                        device.Name, device.OnlineState, device.VideoInput ?? "unknown");
                    changed = true;
                }
            }
            finally
            {
                gate.Release();
            }
        }));

        return changed;
    }
}
