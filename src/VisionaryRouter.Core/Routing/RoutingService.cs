using Microsoft.Extensions.Logging;
using VisionaryRouter.Core.Devices;
using VisionaryRouter.Core.Models;

namespace VisionaryRouter.Core.Routing;

/// <summary>
/// Turns "put this source on these displays" into per-decoder commands and verifies the result.
///
/// One-to-one and one-to-many are the same operation here. Visionary routing works by telling
/// each decoder which encoder to follow, so a single destination is just a list of length one -
/// there is no second code path to keep in step.
/// </summary>
public sealed class RoutingService
{
    private readonly VisionaryDeviceClient _client;
    private readonly RoutingOptions _options;
    private readonly ILogger<RoutingService> _logger;

    public RoutingService(VisionaryDeviceClient client, RoutingOptions options, ILogger<RoutingService> logger)
    {
        _client = client;
        _options = options;
        _logger = logger;
    }

    public async Task<RouteOperationResult> RouteAsync(
        Device encoder,
        IReadOnlyList<Device> decoders,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(encoder);
        ArgumentNullException.ThrowIfNull(decoders);

        _logger.LogInformation(
            "Routing {Encoder} [{EncoderIp}] to {Count} decoder(s)",
            encoder.Name, encoder.IpAddress, decoders.Count);

        using var gate = new SemaphoreSlim(_options.MaxConcurrency, _options.MaxConcurrency);

        var tasks = decoders.Select(async decoder =>
        {
            await gate.WaitAsync(cancellationToken);
            try
            {
                return await RouteOneAsync(encoder, decoder, cancellationToken);
            }
            finally
            {
                gate.Release();
            }
        });

        var results = await Task.WhenAll(tasks);

        var operation = new RouteOperationResult
        {
            EncoderName = encoder.Name,
            EncoderIp = encoder.IpAddress,
            Results = results
        };

        _logger.LogInformation(
            "Route of {Encoder} finished: {Success} verified, {Failed} failed",
            encoder.Name, operation.SuccessCount, operation.FailureCount);

        return operation;
    }

    public async Task<RouteResult> ClearAsync(Device decoder, CancellationToken cancellationToken = default)
    {
        if (!decoder.Enabled) return Fail(decoder, RouteStatus.Failed, "Decoder is disabled in this application");

        var sent = await _client.DisconnectAsync(decoder, cancellationToken);
        if (!sent.Success)
        {
            return Fail(decoder, sent.TimedOut ? RouteStatus.Offline : RouteStatus.Failed, sent.Error);
        }

        // 4-Series firmware 2.3.152 acknowledges the disconnect but retains STREAM.HOST,
        // and does not expose STREAM.CONNECT through QUERY.KEY. This is accepted, not an
        // independently verified state.
        decoder.RouteCleared = true;
        decoder.StreamConnected = false;
        decoder.AssignedEncoderIp = null;
        decoder.CurrentEncoderIp = null;
        return new RouteResult
        {
            DecoderId = decoder.Id, DecoderName = decoder.Name, Status = RouteStatus.Accepted,
            Commanded = true, Verified = false
        };
    }

    private async Task<RouteResult> RouteOneAsync(Device encoder, Device decoder, CancellationToken cancellationToken)
    {
        if (!decoder.Enabled)
        {
            return Fail(decoder, RouteStatus.Failed, "Decoder is disabled in this application");
        }

        // Record the assignment at the point the route is issued, before we know whether it
        // worked. This is what the operator asked for and it is what the UI colours by: the
        // question "what did I send here" has an answer even when the endpoint is unplugged.
        // Whether it took is a separate question, answered by the verification below.
        decoder.AssignedEncoderIp = encoder.IpAddress;
        decoder.RouteCleared = false;
        decoder.UpdatedUtc = DateTime.UtcNow;

        var send = await _client.SendRouteAsync(decoder, encoder.IpAddress, cancellationToken);

        if (!send.Success)
        {
            // A timeout here means we could not reach the decoder at all, which is a different
            // problem from a decoder that answered and then did not switch.
            var status = send.TimedOut ? RouteStatus.Offline : RouteStatus.Failed;
            _logger.LogWarning("Route command to {Decoder} failed: {Error}", decoder.Name, send.Error);
            return Fail(decoder, status, send.Error);
        }

        // The endpoint can accept a route, report SUCCESS, and do nothing - it answers
        // NO_CHANGE / ALREADY_CONNECTED when asked for the source it is already set to. After a
        // clear that leaves the decoder with the correct STREAM.HOST and a dead stream, and
        // because STREAM.HOST reads back correctly the verification below would call it good.
        // Force a real re-establish instead.
        if (send.Response?.RouteWasNoOp == true)
        {
            _logger.LogInformation(
                "{Decoder} reported no change for {EncoderIp}; forcing the stream to re-establish",
                decoder.Name, encoder.IpAddress);

            await ForceReestablishAsync(decoder, encoder.IpAddress, cancellationToken);
        }

        // Give the endpoint a moment before asking: the documented response to a switch is
        // 'queue_next_switch', so the decoder has accepted the command but has not acted yet.
        await Task.Delay(_options.VerifyDelay, cancellationToken);

        string? reported = null;

        for (var attempt = 1; attempt <= Math.Max(1, _options.VerifyRetries); attempt++)
        {
            reported = await _client.ReadCurrentSourceAsync(decoder, cancellationToken);

            if (string.Equals(reported, encoder.IpAddress, StringComparison.OrdinalIgnoreCase))
            {
                decoder.StreamConnected = true;

                // STREAM.HOST only says what the decoder is set to. Ask whether video is
                // actually arriving, because the two can disagree - that disagreement is the
                // whole reason this check exists.
                var receiving = await _client.IsReceivingVideoAsync(decoder, cancellationToken);

                if (receiving == false)
                {
                    // Not reported as a failure: the commonest cause is the encoder having no
                    // input signal, which is a source problem rather than a routing one. The
                    // caller is given the fact and can say so.
                    _logger.LogWarning(
                        "{Decoder} is set to {EncoderIp} but reports no incoming video",
                        decoder.Name, encoder.IpAddress);
                }
                else
                {
                    _logger.LogInformation(
                        "ROUTE VERIFIED {Decoder} STREAM.HOST={EncoderIp}",
                        decoder.Name, encoder.IpAddress);
                }

                return new RouteResult
                {
                    DecoderId = decoder.Id,
                    DecoderName = decoder.Name,
                    Status = RouteStatus.Verified,
                    Commanded = true,
                    Verified = true,
                    ReportedEncoderIp = reported,
                    ReceivingVideo = receiving
                };
            }

            if (attempt < _options.VerifyRetries)
            {
                await Task.Delay(_options.VerifyInterval, cancellationToken);
            }
        }

        // The command was accepted but the decoder is not reporting the source we asked for.
        // Say so plainly rather than calling it a success because the send worked.
        _logger.LogWarning(
            "Route to {Decoder} was accepted but did not verify: expected {Expected}, decoder reports {Actual}",
            decoder.Name, encoder.IpAddress, reported ?? "nothing");

        return new RouteResult
        {
            DecoderId = decoder.Id,
            DecoderName = decoder.Name,
            Status = RouteStatus.Failed,
            Commanded = true,
            Verified = false,
            ReportedEncoderIp = reported,

            // An endpoint with no source reports STREAM.HOST as an empty value rather than
            // omitting it, so blank has to read the same as missing here.
            Error = string.IsNullOrWhiteSpace(reported)
                ? $"Decoder accepted the command but reports no source; expected {encoder.IpAddress}"
                : $"Decoder is following {reported}, expected {encoder.IpAddress}"
        };
    }

    /// <summary>
    /// Makes a decoder genuinely reconnect to a source it is already set to.
    ///
    /// The endpoint only acts on STREAM.HOST when the value changes, so the stream is moved to
    /// a parking address and then back. Each step waits until the decoder reports the address
    /// it was given: switches are queued, and issuing the second command while the first is
    /// still in flight gets them coalesced - which on tested hardware left the decoder sitting
    /// on the parking address with nothing on screen.
    /// </summary>
    private async Task ForceReestablishAsync(Device decoder, string encoderIp, CancellationToken cancellationToken)
    {
        var parking = _options.ReestablishVia;

        var moved = await SendAndAwaitHostAsync(decoder, parking, cancellationToken);

        if (!moved)
        {
            // Never reached the parking address, so sending the target now would be the same
            // no-op we are trying to escape. Leave it for the verification loop to report.
            _logger.LogWarning(
                "{Decoder} did not move to the parking address {Parking}; cannot force a re-establish",
                decoder.Name, parking);
            return;
        }

        await SendAndAwaitHostAsync(decoder, encoderIp, cancellationToken);
    }

    /// <summary>Sends a route and waits until the decoder reports that address, or gives up.</summary>
    private async Task<bool> SendAndAwaitHostAsync(Device decoder, string address, CancellationToken cancellationToken)
    {
        await _client.SendRouteAsync(decoder, address, cancellationToken);

        for (var attempt = 0; attempt < Math.Max(1, _options.ReestablishPollAttempts); attempt++)
        {
            await Task.Delay(_options.ReestablishPollInterval, cancellationToken);

            var reported = await _client.ReadCurrentSourceAsync(decoder, cancellationToken);

            if (string.Equals(reported, address, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static RouteResult Fail(Device decoder, RouteStatus status, string? error) => new()
    {
        DecoderId = decoder.Id,
        DecoderName = decoder.Name,
        Status = status,
        Commanded = false,
        Verified = false,
        Error = error
    };
}
