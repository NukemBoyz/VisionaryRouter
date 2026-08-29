namespace VisionaryRouter.Core.Protocol;

/// <summary>
/// Tuning for endpoint communication. These are our defaults, not Visionary requirements -
/// expect to revisit them once the application has run against real hardware.
/// </summary>
public sealed class VisionaryTransportOptions
{
    public int Port { get; set; } = VisionaryCommand.DefaultPort;

    /// <summary>How long to wait for a reply before giving up on one attempt.</summary>
    public TimeSpan QueryTimeout { get; set; } = TimeSpan.FromMilliseconds(1000);

    /// <summary>Extra attempts after the first. UDP drops packets silently, so one retry is cheap insurance.</summary>
    public int RetryCount { get; set; } = 1;

    /// <summary>Ceiling on endpoints talked to at once during bulk operations. Keeps us polite on the network.</summary>
    public int MaxConcurrency { get; set; } = 8;
}
