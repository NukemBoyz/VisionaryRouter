using System.Net;
using Microsoft.Extensions.Logging;
using VisionaryRouter.Core.Models;
using VisionaryRouter.Core.Protocol;

namespace VisionaryRouter.Core.Devices;

/// <summary>
/// Endpoint-level operations expressed in terms of devices rather than command strings.
///
/// Everything above this layer - controllers, routing, polling - talks to endpoints through
/// here. Nothing above this file constructs protocol text or touches a socket.
/// </summary>
public sealed class VisionaryDeviceClient
{
    private readonly IVisionaryTransport _transport;
    private readonly ILogger<VisionaryDeviceClient> _logger;

    public VisionaryDeviceClient(IVisionaryTransport transport, ILogger<VisionaryDeviceClient> logger)
    {
        _transport = transport;
        _logger = logger;
    }

    /// <summary>
    /// Reads a full configuration dump and folds what we understand into the device record.
    /// Fields the endpoint does not report are left as they were rather than blanked.
    /// </summary>
    public async Task<VisionaryQueryResult> ProbeAsync(Device device, CancellationToken cancellationToken = default)
    {
        if (!TryGetEndpoint(device, out var endpoint, out var failure))
        {
            return failure;
        }

        var result = await _transport.QueryAsync(endpoint, VisionaryCommand.QueryAll(), cancellationToken);

        if (!result.Success || result.Response is null)
        {
            MarkOffline(device, result.Error);
            return result;
        }

        ApplyMetadata(device, result.Response);
        return result;
    }

    /// <summary>Lightweight liveness check. Cheaper than a probe, suitable for the heartbeat loop.</summary>
    public async Task<VisionaryQueryResult> PingAsync(Device device, CancellationToken cancellationToken = default)
    {
        if (!TryGetEndpoint(device, out var endpoint, out var failure))
        {
            return failure;
        }

        var result = await _transport.QueryAsync(endpoint, VisionaryCommand.StatusQuery(), cancellationToken);

        if (result.Success)
        {
            MarkOnline(device);
        }
        else
        {
            MarkOffline(device, result.Error);
        }

        return result;
    }

    /// <summary>
    /// Asks a decoder which encoder it is actually following.
    ///
    /// This is the application's source of truth for routing state. We never treat the last
    /// command we sent as proof of what a decoder is doing - another control system may have
    /// changed it since.
    /// </summary>
    public async Task<string?> ReadCurrentSourceAsync(Device decoder, CancellationToken cancellationToken = default)
    {
        if (!TryGetEndpoint(decoder, out var endpoint, out _))
        {
            return null;
        }

        var result = await _transport.QueryAsync(endpoint, VisionaryCommand.QueryCurrentRoute(), cancellationToken);

        if (!result.Success || result.Response is null)
        {
            MarkOffline(decoder, result.Error);
            return null;
        }

        var host = result.Response.Get(VisionaryKeys.StreamHost);

        decoder.CurrentEncoderIp = host;
        MarkOnline(decoder);

        return host;
    }

    /// <summary>
    /// Sends the routing command to a decoder. Does not verify - verification is the routing
    /// service's job, because it has to happen after a settling delay.
    /// </summary>
    public async Task<VisionaryQueryResult> SendRouteAsync(
        Device decoder,
        string encoderIp,
        CancellationToken cancellationToken = default)
    {
        if (!TryGetEndpoint(decoder, out var endpoint, out var failure))
        {
            return failure;
        }

        _logger.LogInformation(
            "ROUTE REQUEST decoder {Decoder} [{DecoderIp}] -> encoder [{EncoderIp}]",
            decoder.Name, decoder.IpAddress, encoderIp);

        return await _transport.QueryAsync(endpoint, VisionaryCommand.Route(encoderIp), cancellationToken);
    }

    /// <summary>Flashes the endpoint's front-panel LEDs so an installer can find it in a rack.</summary>
    public async Task<VisionaryQueryResult> IdentifyAsync(Device device, CancellationToken cancellationToken = default)
    {
        if (!TryGetEndpoint(device, out var endpoint, out var failure))
        {
            return failure;
        }

        return await _transport.QueryAsync(endpoint, VisionaryCommand.Identify(), cancellationToken);
    }

    private static bool TryGetEndpoint(Device device, out IPEndPoint endpoint, out VisionaryQueryResult failure)
    {
        if (!IPAddress.TryParse(device.IpAddress, out var address))
        {
            endpoint = null!;
            failure = VisionaryQueryResult.Failure($"'{device.IpAddress}' is not a valid IP address");
            return false;
        }

        endpoint = new IPEndPoint(address, device.ControlPort);
        failure = null!;
        return true;
    }

    private static void MarkOnline(Device device)
    {
        device.OnlineState = OnlineState.Online;
        device.LastSeenUtc = DateTime.UtcNow;
        device.LastError = null;
        device.UpdatedUtc = DateTime.UtcNow;
    }

    private static void MarkOffline(Device device, string? error)
    {
        device.OnlineState = OnlineState.Offline;
        device.LastError = error;
        device.UpdatedUtc = DateTime.UtcNow;
    }

    private static void ApplyMetadata(Device device, VisionaryResponse response)
    {
        device.UnitId = response.Get(VisionaryKeys.UnitId) ?? device.UnitId;
        device.Model = response.Get(VisionaryKeys.UnitModel) ?? device.Model;
        device.SerialNumber = response.Get(VisionaryKeys.UnitSerial) ?? device.SerialNumber;
        device.MacAddress = response.Get(VisionaryKeys.UnitMacAddress) ?? device.MacAddress;
        device.Firmware = response.Get(VisionaryKeys.UnitFirmware) ?? device.Firmware;
        device.FirmwareDate = response.Get(VisionaryKeys.UnitFirmwareDate) ?? device.FirmwareDate;
        device.StreamMode = response.Get(VisionaryKeys.StreamMode) ?? device.StreamMode;

        if (response.TryGet(VisionaryKeys.StreamHost, out var streamHost))
        {
            device.CurrentEncoderIp = streamHost;
        }

        // Only infer the type when the user has not already classified it, so a manual
        // correction is never silently overwritten by a probe.
        if (device.Type == DeviceType.Unknown)
        {
            device.Type = DeviceClassifier.FromModel(device.Model);
        }

        MarkOnline(device);
    }
}
