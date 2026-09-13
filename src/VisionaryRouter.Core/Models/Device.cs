namespace VisionaryRouter.Core.Models;

/// <summary>
/// A Visionary endpoint as this application knows it.
///
/// Name is ours, not the endpoint's: renaming a device here never touches the hardware.
/// The metadata fields are refreshed from the network and are not authoritative config.
/// </summary>
public sealed class Device
{
    public required Guid Id { get; init; }
    public required string Name { get; set; }
    public required DeviceType Type { get; set; }
    public required string IpAddress { get; set; }

    /// <summary>
    /// UDP control port. Visionary endpoints use 8000; this is configurable only to cover an
    /// endpoint behind port translation, which is not a normal deployment.
    /// </summary>
    public int ControlPort { get; set; } = 8000;
    public bool Enabled { get; set; } = true;
    public int SortOrder { get; set; }

    // Read back from the endpoint during a probe. Null until we have talked to it.
    public string? UnitId { get; set; }
    public string? Model { get; set; }
    public string? SerialNumber { get; set; }
    public string? MacAddress { get; set; }
    public string? Firmware { get; set; }
    public string? FirmwareDate { get; set; }
    public string? StreamMode { get; set; }

    /// <summary>Encoders only: physical input currently feeding the AV stream.</summary>
    public string? VideoInput { get; set; }
    public bool? VideoSignalPresent { get; set; }
    public string? VideoTiming { get; set; }

    /// <summary>Decoders only: the encoder IP this decoder reports it is following.</summary>
    public string? CurrentEncoderIp { get; set; }
    public bool? StreamConnected { get; set; }
    public bool RouteCleared { get; set; }

    /// <summary>
    /// Decoders only: the encoder this application last sent this decoder to.
    ///
    /// Deliberately separate from <see cref="CurrentEncoderIp"/>, which is what the decoder
    /// says it is doing. The two agree on healthy hardware and part company on an endpoint
    /// that accepts a route and never switches - keeping both means the UI can show the
    /// assignment while the reported state still tells us whether it took.
    /// </summary>
    public string? AssignedEncoderIp { get; set; }

    public OnlineState OnlineState { get; set; } = OnlineState.Unknown;
    public DateTime? LastSeenUtc { get; set; }
    public string? LastError { get; set; }

    public DateTime CreatedUtc { get; init; } = DateTime.UtcNow;
    public DateTime UpdatedUtc { get; set; } = DateTime.UtcNow;
}
