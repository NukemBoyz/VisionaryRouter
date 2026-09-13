namespace VisionaryRouter.Core.Protocol;

/// <summary>
/// Builds Visionary PacketAV API command strings.
///
/// The wire format is human-readable key/value pairs separated by '&amp;', opened with
/// CMD=START and closed with CMD=END. Keys are case-sensitive.
///
/// Every command string in the application must originate here. Nothing else in the
/// codebase should concatenate raw protocol text.
/// </summary>
public static class VisionaryCommand
{
    public const string Start = "CMD=START";
    public const string End = "CMD=END";

    /// <summary>Default UDP control port for the Visionary API.</summary>
    public const int DefaultPort = 8000;

    /// <summary>Documented multicast address the API also listens on.</summary>
    public const string ApiMulticastAddress = "226.0.0.19";

    /// <summary>
    /// Keys that write endpoint configuration to nonvolatile storage. Routing is
    /// deliberately volatile, so these must never appear in a command we generate.
    /// </summary>
    private static readonly string[] ForbiddenKeys = ["SAVE", "UNIT.SAVE", "UNIT.RESET", "UNIT.REBOOT"];

    public static readonly IReadOnlyList<string> VideoInputs = ["HDMI1", "HDMI2", "USB-C"];

    /// <summary>Lightweight liveness/status probe.</summary>
    public static string StatusQuery() => Build("UNIT.ID=ALL", "QUERY.STATUS=TRUE");

    /// <summary>Full configuration dump. Heavier than <see cref="StatusQuery"/>; use for probing, not polling.</summary>
    public static string QueryAll() => Build("UNIT.ID=ALL", "QUERY.ALL=TRUE");

    /// <summary>Reads a single key from the endpoint.</summary>
    public static string QueryKey(string key)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        return Build("UNIT.ID=ALL", $"QUERY.KEY={key}");
    }

    /// <summary>Reads the encoder a decoder is currently following.</summary>
    public static string QueryCurrentRoute() => QueryKey(VisionaryKeys.StreamHost);

    public static string QueryVideoTiming() => Build("UNIT.ID=ALL", "QUERY.VIDEO_TIMING=TRUE");

    /// <summary>
    /// Points a decoder at an encoder. Sent to the *decoder*, not the encoder:
    /// each decoder is told which source to receive.
    /// </summary>
    public static string Route(string encoderIp)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(encoderIp);
        return Build(
            "UNIT.ID=ALL",
            $"{VisionaryKeys.StreamHost}={encoderIp}",
            "VW.ACTIVE=FALSE",
            "STREAM.CONNECT=TRUE");
    }

    /// <summary>Disconnects a decoder from its current video stream.</summary>
    public static string Disconnect() => Build("UNIT.ID=ALL", "STREAM.CONNECT=FALSE");

    /// <summary>Selects which physical encoder input is sent into the AV stream.</summary>
    public static string SelectVideoInput(string input)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(input);

        var normalized = input.Trim().ToUpperInvariant();
        if (!VideoInputs.Contains(normalized, StringComparer.Ordinal))
        {
            throw new ArgumentOutOfRangeException(nameof(input), input,
                $"Video input must be one of: {string.Join(", ", VideoInputs)}");
        }

        return Build(
            "UNIT.ID=ALL",
            $"{VisionaryKeys.StreamVideo}={normalized}",
            "STREAM.CONNECT=TRUE");
    }

    /// <summary>Flashes the endpoint's front-panel LEDs for roughly ten seconds.</summary>
    public static string Identify() => Build("UNIT.ID=ALL", "UNIT.FU=TRUE");

    /// <summary>
    /// Assembles a command from key=value fields, wrapping them in the CMD=START/CMD=END envelope.
    /// </summary>
    public static string Build(params string[] fields)
    {
        ArgumentNullException.ThrowIfNull(fields);

        foreach (var field in fields)
        {
            var key = field.Split('=', 2)[0].Trim();
            if (ForbiddenKeys.Contains(key, StringComparer.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    $"Refusing to build a command containing '{key}'. Routing must stay volatile and " +
                    "this application does not write endpoint configuration.");
            }
        }

        return string.Join('&', [Start, .. fields, End]);
    }
}
