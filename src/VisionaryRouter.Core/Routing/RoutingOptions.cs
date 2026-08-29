namespace VisionaryRouter.Core.Routing;

public sealed class RoutingOptions
{
    /// <summary>How long to let a switch settle before asking the decoder what it is following.</summary>
    public TimeSpan VerifyDelay { get; set; } = TimeSpan.FromMilliseconds(400);

    /// <summary>How many times to re-ask before calling a switch failed. Switches are not instant.</summary>
    public int VerifyRetries { get; set; } = 3;

    /// <summary>Gap between verification attempts.</summary>
    public TimeSpan VerifyInterval { get; set; } = TimeSpan.FromMilliseconds(400);

    /// <summary>Decoders addressed simultaneously during a group route.</summary>
    public int MaxConcurrency { get; set; } = 8;
}
