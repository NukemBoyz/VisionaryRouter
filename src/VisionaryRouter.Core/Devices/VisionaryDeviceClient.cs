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

    public async Task<bool?> ReadStreamConnectedAsync(Device decoder, CancellationToken cancellationToken = default)
    {
        if (!TryGetEndpoint(decoder, out var endpoint, out _)) return null;
        var result = await _transport.QueryAsync(
            endpoint, VisionaryCommand.QueryKey(VisionaryKeys.StreamConnect), cancellationToken);
        if (!result.Success || result.Response is null)
        {
            MarkOffline(decoder, result.Error);
            return null;
        }

        var raw = result.Response.Get(VisionaryKeys.StreamConnect);
        if (!bool.TryParse(raw, out var connected)) return null;
        decoder.StreamConnected = connected;
        if (!connected) decoder.CurrentEncoderIp = null;
        MarkOnline(decoder);
        return connected;
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

    public async Task<VisionaryQueryResult> DisconnectAsync(
        Device decoder,
        CancellationToken cancellationToken = default)
    {
        if (!TryGetEndpoint(decoder, out var endpoint, out var failure))
        {
            return failure;
        }

        _logger.LogInformation("CLEAR ROUTE REQUEST decoder {Decoder} [{DecoderIp}]", decoder.Name, decoder.IpAddress);
        return await _transport.QueryAsync(endpoint, VisionaryCommand.Disconnect(), cancellationToken);
    }

    /// <summary>Reads the physical video input currently selected on an encoder.</summary>
    public async Task<string?> ReadVideoInputAsync(Device encoder, CancellationToken cancellationToken = default)
    {
        if (!TryGetEndpoint(encoder, out var endpoint, out _))
        {
            return null;
        }

        var result = await _transport.QueryAsync(
            endpoint,
            VisionaryCommand.QueryKey(VisionaryKeys.StreamVideo),
            cancellationToken);

        if (!result.Success || result.Response is null)
        {
            MarkOffline(encoder, result.Error);
            return null;
        }

        var input = result.Response.Get(VisionaryKeys.StreamVideo);
        encoder.VideoInput = input;
        MarkOnline(encoder);
        return input;
    }

    public async Task<bool?> ReadVideoSignalAsync(Device encoder, CancellationToken cancellationToken = default)
    {
        if (!TryGetEndpoint(encoder, out var endpoint, out _))
        {
            return null;
        }

        var result = await _transport.QueryAsync(endpoint, VisionaryCommand.QueryVideoTiming(), cancellationToken);
        if (!result.Success || result.Response is null)
        {
            MarkOffline(encoder, result.Error);
            return null;
        }

        var timing = result.Response.Get(VisionaryKeys.VideoTiming) ?? result.Response.Raw;
        encoder.VideoTiming = SummarizeTiming(timing);
        encoder.VideoSignalPresent = !timing.Contains("Not Available", StringComparison.OrdinalIgnoreCase);
        MarkOnline(encoder);
        return encoder.VideoSignalPresent;
    }

    /// <summary>
    /// Whether a decoder is actually receiving video, as opposed to merely being set to a source.
    ///
    /// Same query as the encoder signal check - on a decoder VIDEO.TIMING describes the incoming
    /// stream, and "Not Available" means nothing is arriving. This is the only reliable proof a
    /// route took effect: STREAM.HOST is a setting and reads back correctly even when the
    /// endpoint is wedged and passing no video.
    /// </summary>
    public Task<bool?> IsReceivingVideoAsync(Device decoder, CancellationToken cancellationToken = default) =>
        ReadVideoSignalAsync(decoder, cancellationToken);

    private static string SummarizeTiming(string timing)
    {
        if (timing.Contains("Not Available", StringComparison.OrdinalIgnoreCase)) return "No signal";
        var match = System.Text.RegularExpressions.Regex.Match(timing, @"\[(\d+)\]X\[(\d+)\].*?\[(\d+)\]Hz");
        return match.Success ? $"{match.Groups[1].Value}×{match.Groups[2].Value} {match.Groups[3].Value}Hz" : "Signal detected";
    }

    /// <summary>Sends an operational encoder input switch. Verification is done by the API layer.</summary>
    public async Task<VisionaryQueryResult> SelectVideoInputAsync(
        Device encoder,
        string input,
        CancellationToken cancellationToken = default)
    {
        if (!TryGetEndpoint(encoder, out var endpoint, out var failure))
        {
            return failure;
        }

        _logger.LogInformation(
            "VIDEO INPUT REQUEST encoder {Encoder} [{EncoderIp}] -> {Input}",
            encoder.Name, encoder.IpAddress, input);

        return await _transport.QueryAsync(endpoint, VisionaryCommand.SelectVideoInput(input), cancellationToken);
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
        device.VideoInput = response.Get(VisionaryKeys.StreamVideo) ?? device.VideoInput;

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
