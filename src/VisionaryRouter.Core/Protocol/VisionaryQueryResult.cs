namespace VisionaryRouter.Core.Protocol;

/// <summary>
/// Outcome of one endpoint exchange.
///
/// A timeout is an ordinary result here rather than an exception: unreachable endpoints are
/// a normal steady state for this application, and the polling loop must not be built out of
/// try/catch. Callers get a value they have to look at.
/// </summary>
public sealed record VisionaryQueryResult
{
    public required bool Success { get; init; }
    public VisionaryResponse? Response { get; init; }
    public string? Error { get; init; }

    /// <summary>True when the endpoint never answered, as opposed to answering with a failure.</summary>
    public bool TimedOut { get; init; }

    public static VisionaryQueryResult Ok(VisionaryResponse response) =>
        new() { Success = true, Response = response };

    public static VisionaryQueryResult Timeout(string target, TimeSpan after) =>
        new() { Success = false, TimedOut = true, Error = $"No response from {target} after {after.TotalMilliseconds:F0} ms" };

    public static VisionaryQueryResult Failure(string error) =>
        new() { Success = false, Error = error };
}
