namespace VisionaryRouter.Core.Routing;

public enum RouteStatus
{
    Pending = 0,
    Switching = 1,
    Verified = 2,
    Failed = 3,
    Offline = 4,
    Accepted = 5
}

/// <summary>Outcome of routing one decoder.</summary>
public sealed record RouteResult
{
    public required Guid DecoderId { get; init; }
    public required string DecoderName { get; init; }
    public required RouteStatus Status { get; init; }

    /// <summary>The endpoint accepted the command.</summary>
    public bool Commanded { get; init; }

    /// <summary>The decoder reported back the encoder we asked for. This is the one that matters.</summary>
    public bool Verified { get; init; }

    public string? ReportedEncoderIp { get; init; }
    public string? Error { get; init; }

    /// <summary>
    /// Whether the decoder reports video actually arriving, or null if we could not tell.
    ///
    /// Separate from <see cref="Verified"/> on purpose. Verified means the decoder is set to
    /// the requested encoder; this means a picture is reaching it. A false here with Verified
    /// true is usually the source having no input signal rather than a routing failure, so it
    /// is reported rather than treated as an error.
    /// </summary>
    public bool? ReceivingVideo { get; init; }

    public bool Success => Status is RouteStatus.Verified or RouteStatus.Accepted;
}

/// <summary>
/// Outcome of a whole routing operation, which may cover many decoders.
///
/// A group route is not all-or-nothing. One dead display must not make three working ones
/// look like a failure, and it must not be buried under a cheerful "route complete" either -
/// so every decoder reports its own result and the caller is expected to show them.
/// </summary>
public sealed record RouteOperationResult
{
    public required string EncoderName { get; init; }
    public required string EncoderIp { get; init; }
    public required IReadOnlyList<RouteResult> Results { get; init; }

    public int SuccessCount => Results.Count(r => r.Success);
    public int FailureCount => Results.Count(r => !r.Success);
    public bool AllSucceeded => Results.Count > 0 && FailureCount == 0;
}
