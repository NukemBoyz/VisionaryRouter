using System.Net;
using System.Net.Sockets;
using System.Text;
using VisionaryRouter.Core.Protocol;

namespace VisionaryRouter.Simulator;

/// <summary>
/// A fake Visionary endpoint that answers the API commands this application sends.
///
/// It exists because the real hardware is not on hand, and because a simulator can be made
/// to misbehave on demand - drop packets, go silent, refuse to switch - which is exactly the
/// behaviour that is hard to test against a rack of working equipment.
///
/// Real endpoints all listen on UDP 8000. Windows will not hand out extra loopback addresses
/// without reconfiguring the adapter, so simulated endpoints are separated by port instead.
/// </summary>
public sealed class SimulatedEndpoint
{
    private readonly SimulatedDeviceConfig _config;
    private readonly Action<string> _log;
    private readonly Lock _stateLock = new();

    /// <summary>The encoder this decoder is currently following. Meaningless on an encoder.</summary>
    private string _streamHost;
    private string _streamVideo = "HDMI1";
    private bool _streamConnected;

    /// <summary>
    /// A switch that has been accepted but not yet applied. Real endpoints answer
    /// 'queue_next_switch' and change source a moment later, so the simulator does too -
    /// otherwise verification would always pass on the first try and we would never find
    /// out whether the retry loop works.
    /// </summary>
    private string? _pendingStreamHost;
    private DateTime _pendingAppliesAtUtc;

    public SimulatedEndpoint(SimulatedDeviceConfig config, Action<string> log)
    {
        _config = config;
        _log = log;
        _streamHost = config.InitialStreamHost ?? string.Empty;
        _streamConnected = !string.IsNullOrWhiteSpace(_streamHost);
    }

    public SimulatedDeviceConfig Config => _config;

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        using var socket = new UdpClient(new IPEndPoint(IPAddress.Parse(_config.Address), VisionaryCommand.DefaultPort));

        _log($"{_config.Name} ({_config.Model}) listening on {_config.Address}:{VisionaryCommand.DefaultPort}");

        while (!cancellationToken.IsCancellationRequested)
        {
            UdpReceiveResult received;

            try
            {
                received = await socket.ReceiveAsync(cancellationToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }

            var request = Encoding.ASCII.GetString(received.Buffer);

            if (_config.Unresponsive)
            {
                // Deliberately silent: stands in for a powered-down or unplugged endpoint.
                _log($"{_config.Name} RX (ignored, endpoint set unresponsive) {request}");
                continue;
            }

            _log($"{_config.Name} RX {request}");

            var response = HandleCommand(request);

            if (response is null)
            {
                continue;
            }

            _log($"{_config.Name} TX {response}");
            await socket.SendAsync(Encoding.ASCII.GetBytes(response), received.RemoteEndPoint, cancellationToken);
        }
    }

    private string? HandleCommand(string request)
    {
        var command = VisionaryResponse.Parse(request);

        // A queued switch lands once its delay has elapsed, whatever the current command is.
        ApplyPendingSwitch();

        if (command.Values.ContainsKey("QUERY.ALL"))
        {
            return BuildFullDump();
        }

        if (command.TryGet("QUERY.KEY", out var key))
        {
            var value = ReadKey(key);
            return value is null
                ? $"&{VisionaryKeys.ApiStatus}=FAILURE_UNKNOWN_KEY"
                : $"&{key}={value}&{VisionaryKeys.ApiStatus}=SUCCESS_QUERY";
        }

        if (command.Values.ContainsKey("QUERY.STATUS"))
        {
            return $"&{VisionaryKeys.UnitId}={_config.UnitId}" +
                   $"&{VisionaryKeys.UnitModel}={_config.Model}" +
                   $"&{VisionaryKeys.ApiStatus}=SUCCESS";
        }

        if (command.Values.ContainsKey("QUERY.VIDEO_TIMING"))
        {
            return _config.Type == SimulatedDeviceType.Encoder
                ? $"&{VisionaryKeys.VideoTiming}=Timing Table: [1920]X[1080] [60]Hz&QUERY.VIDEO_TIMING=OK&{VisionaryKeys.ApiStatus}=SUCCESS_QUERY"
                : $"&{VisionaryKeys.VideoTiming}=Not Available&QUERY.VIDEO_TIMING=OK&{VisionaryKeys.ApiStatus}=SUCCESS_QUERY";
        }

        if (command.Values.ContainsKey("UNIT.FU"))
        {
            _log($"{_config.Name} *** front panel LEDs flashing for 10 seconds ***");
            return $"&{VisionaryKeys.ApiStatus}=SUCCESS";
        }

        if (command.TryGet(VisionaryKeys.StreamHost, out var requestedHost))
        {
            return HandleRoute(requestedHost);
        }

        if (command.TryGet(VisionaryKeys.StreamConnect, out var connect) &&
            string.Equals(connect, "FALSE", StringComparison.OrdinalIgnoreCase))
        {
            if (_config.Type != SimulatedDeviceType.Decoder)
                return $"&{VisionaryKeys.ApiStatus}=FAILURE_NOT_A_DECODER";
            lock (_stateLock)
            {
                _streamHost = string.Empty;
                _streamConnected = false;
                _pendingStreamHost = null;
            }
            return $"&{VisionaryKeys.StreamConnect}=FALSE&{VisionaryKeys.ApiStatus}=SUCCESS";
        }

        if (command.TryGet(VisionaryKeys.StreamVideo, out var requestedInput))
        {
            if (_config.Type != SimulatedDeviceType.Encoder)
            {
                return $"&{VisionaryKeys.ApiStatus}=FAILURE_NOT_AN_ENCODER";
            }

            if (!VisionaryCommand.VideoInputs.Contains(requestedInput, StringComparer.Ordinal))
            {
                return $"&{VisionaryKeys.ApiStatus}=FAILURE_INVALID_VALUE";
            }

            _streamVideo = requestedInput;
            return $"&{VisionaryKeys.StreamConnect}=queue_next_switch&{VisionaryKeys.ApiStatus}=SUCCESS";
        }

        return $"&{VisionaryKeys.ApiStatus}=FAILURE_UNKNOWN_COMMAND";
    }

    private string HandleRoute(string requestedHost)
    {
        if (_config.Type == SimulatedDeviceType.Encoder)
        {
            // Encoders are never told where to send video, so a route aimed at one is a bug
            // on our side. Say so loudly rather than quietly accepting it.
            _log($"{_config.Name} refused a route command - this endpoint is an encoder");
            return $"&{VisionaryKeys.ApiStatus}=FAILURE_NOT_A_DECODER";
        }

        if (_config.RefuseToSwitch)
        {
            // Accepts the command, never changes source: the failure mode where a decoder
            // answers happily but the display never changes.
            _log($"{_config.Name} accepted the route but is configured never to switch");
            return $"&{VisionaryKeys.StreamConnect}=queue_next_switch&{VisionaryKeys.ApiStatus}=SUCCESS";
        }

        lock (_stateLock)
        {
            _pendingStreamHost = requestedHost;
            _streamConnected = true;
            _pendingAppliesAtUtc = DateTime.UtcNow + _config.SwitchDelay;
        }

        return $"&{VisionaryKeys.StreamConnect}=queue_next_switch&{VisionaryKeys.ApiStatus}=SUCCESS";
    }

    private void ApplyPendingSwitch()
    {
        lock (_stateLock)
        {
            if (_pendingStreamHost is not null && DateTime.UtcNow >= _pendingAppliesAtUtc)
            {
                _streamHost = _pendingStreamHost;
                _pendingStreamHost = null;
                _log($"{_config.Name} now following {_streamHost}");
            }
        }
    }

    private string? ReadKey(string key) => key switch
    {
        VisionaryKeys.StreamHost => CurrentStreamHost(),
        VisionaryKeys.StreamConnect when _config.Type == SimulatedDeviceType.Decoder => _streamConnected.ToString().ToUpperInvariant(),
        VisionaryKeys.StreamMode => _config.StreamMode,
        VisionaryKeys.StreamVideo when _config.Type == SimulatedDeviceType.Encoder => _streamVideo,
        VisionaryKeys.UnitId => _config.UnitId,
        VisionaryKeys.UnitModel => _config.Model,
        VisionaryKeys.UnitSerial => _config.Serial,
        VisionaryKeys.UnitFirmware => _config.Firmware,
        VisionaryKeys.IpAddress => _config.Address,
        _ => null
    };

    private string CurrentStreamHost()
    {
        lock (_stateLock)
        {
            return _streamHost;
        }
    }

    private string BuildFullDump()
    {
        var fields = new List<string>
        {
            $"{VisionaryKeys.UnitId}={_config.UnitId}",
            $"{VisionaryKeys.UnitModel}={_config.Model}",
            $"{VisionaryKeys.UnitSerial}={_config.Serial}",
            $"{VisionaryKeys.UnitFirmware}={_config.Firmware}",
            $"{VisionaryKeys.UnitFirmwareDate}=2025-11-04",
            $"{VisionaryKeys.UnitMacAddress}={_config.MacAddress}",
            $"{VisionaryKeys.IpAddress}={_config.Address}",
            $"{VisionaryKeys.StreamMode}={_config.StreamMode}",

            // Real 4-Series firmware (2.3.152, observed 2026-09-13) returns the endpoint's own
            // administrative password in QUERY.ALL. Reproduced here so that redaction can be
            // exercised without hardware - a simulator that omitted it would let a regression
            // through unnoticed until a live unit leaked a real credential.
            $"UNIT.PASSWORD={_config.Password}"
        };

        if (_config.Type == SimulatedDeviceType.Decoder)
        {
            fields.Add($"{VisionaryKeys.StreamHost}={CurrentStreamHost()}");
        }
        else
        {
            fields.Add($"{VisionaryKeys.StreamVideo}={_streamVideo}");
        }

        fields.Add($"{VisionaryKeys.ApiStatus}=SUCCESS_QUERY");

        return "&" + string.Join('&', fields);
    }
}
