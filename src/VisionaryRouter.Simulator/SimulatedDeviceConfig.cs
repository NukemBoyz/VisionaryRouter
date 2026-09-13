namespace VisionaryRouter.Simulator;

public enum SimulatedDeviceType
{
    Encoder,
    Decoder
}

public sealed class SimulatedDeviceConfig
{
    public required string Name { get; init; }
    public required SimulatedDeviceType Type { get; init; }

    /// <summary>
    /// Loopback address this endpoint answers on. Each simulated device gets its own address
    /// rather than its own port, because routing identifies an encoder by IP - endpoints
    /// sharing one address would be indistinguishable, and every route would appear to verify
    /// no matter which source was picked.
    /// </summary>
    public required string Address { get; init; }

    public required string Model { get; init; }

    public string UnitId { get; init; } = "ALL";
    public string Serial { get; init; } = "SIM000000";
    public string Firmware { get; init; } = "2.4.1-sim";
    public string MacAddress { get; init; } = "00:00:00:00:00:00";
    public string StreamMode { get; init; } = "multicast";

    /// <summary>
    /// Stands in for the administrative password real firmware discloses in QUERY.ALL. It is
    /// deliberately obvious: if this string ever appears in a log, a console or the browser,
    /// redaction has stopped working.
    /// </summary>
    public string Password { get; init; } = "sim-password-should-never-be-displayed";

    /// <summary>Decoders only: the encoder this unit is following at startup.</summary>
    public string? InitialStreamHost { get; init; }

    /// <summary>How long a queued switch takes to apply, mirroring 'queue_next_switch' on real units.</summary>
    public TimeSpan SwitchDelay { get; init; } = TimeSpan.FromMilliseconds(600);

    /// <summary>Never answers. Stands in for an endpoint that is powered down or unplugged.</summary>
    public bool Unresponsive { get; init; }

    /// <summary>Answers normally but never changes source. The 'display never changed' failure.</summary>
    public bool RefuseToSwitch { get; init; }
}
