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

    /// <summary>
    /// Address used to force a decoder to actually re-establish its stream.
    ///
    /// Tested firmware ignores a route to the encoder a decoder is already set to, answering
    /// NO_CHANGE / ALREADY_CONNECTED. The only way found to make it reconnect is to move
    /// STREAM.HOST to a different value and back. This is that different value, and it is
    /// deliberately not another encoder: parking on a real source would flash the wrong
    /// picture onto the display mid-switch.
    /// </summary>
    public string ReestablishVia { get; set; } = "0.0.0.0";

    /// <summary>
    /// How many times to re-read STREAM.HOST while waiting for a step of the re-establish to
    /// land. Switches are queued and asynchronous, so each step is confirmed by reading the
    /// value back rather than by sleeping and hoping - sending the next command too early gets
    /// it coalesced with the previous one, which can leave the decoder parked on the
    /// intermediate address.
    /// </summary>
    public int ReestablishPollAttempts { get; set; } = 10;

    public TimeSpan ReestablishPollInterval { get; set; } = TimeSpan.FromMilliseconds(500);
}
