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

        // Give the endpoint a moment before asking: the documented response to a switch is
        // 'queue_next_switch', so the decoder has accepted the command but has not acted yet.
        await Task.Delay(_options.VerifyDelay, cancellationToken);

        string? reported = null;

        for (var attempt = 1; attempt <= Math.Max(1, _options.VerifyRetries); attempt++)
        {
            reported = await _client.ReadCurrentSourceAsync(decoder, cancellationToken);

            if (string.Equals(reported, encoder.IpAddress, StringComparison.OrdinalIgnoreCase))
            {
                _logger.LogInformation(
                    "ROUTE VERIFIED {Decoder} STREAM.HOST={EncoderIp}",
                    decoder.Name, encoder.IpAddress);

                return new RouteResult
                {
                    DecoderId = decoder.Id,
                    DecoderName = decoder.Name,
                    Status = RouteStatus.Verified,
                    Commanded = true,
                    Verified = true,
                    ReportedEncoderIp = reported
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
