using VisionaryRouter.Core.Models;

namespace VisionaryRouter.Server.Services;

/// <summary>
/// What the browser sees. Kept separate from <see cref="Device"/> so the wire shape is
/// deliberate rather than whatever the domain model happens to look like today.
/// </summary>
public sealed record DeviceView
{
    public required Guid Id { get; init; }
    public required string Name { get; init; }
    public required string Type { get; init; }
    public required string IpAddress { get; init; }
    public required int ControlPort { get; init; }
    public required bool Enabled { get; init; }
    public required string OnlineState { get; init; }

    public string? Model { get; init; }
    public string? SerialNumber { get; init; }
    public string? Firmware { get; init; }
    public string? MacAddress { get; init; }
    public string? StreamMode { get; init; }
    public string? LastError { get; init; }
    public DateTime? LastSeenUtc { get; init; }

    /// <summary>Decoders only: the encoder IP the device reports it is following.</summary>
    public string? CurrentEncoderIp { get; init; }

    /// <summary>
    /// Friendly name for <see cref="CurrentEncoderIp"/>, when that IP belongs to a device we
    /// know. Null means the decoder is following something outside this application's list -
    /// worth showing as the raw IP rather than hiding.
    /// </summary>
    public string? CurrentSourceName { get; init; }

    /// <summary>Encoders only: which colour slot this source occupies in the UI.</summary>
    public int? ColorIndex { get; init; }

    /// <summary>
    /// Decoders only: the colour of the source this decoder is following, so a destination
    /// carries its source's colour without the browser having to resolve it. Null when the
    /// decoder is following nothing, or something we do not have in the device list.
    /// </summary>
    public int? CurrentSourceColorIndex { get; init; }

    public static DeviceView From(Device device, DeviceRegistry registry) => new()
    {
        Id = device.Id,
        Name = device.Name,
        Type = device.Type.ToString(),
        IpAddress = device.IpAddress,
        ControlPort = device.ControlPort,
        Enabled = device.Enabled,
        OnlineState = device.OnlineState.ToString(),
        Model = device.Model,
        SerialNumber = device.SerialNumber,
        Firmware = device.Firmware,
        MacAddress = device.MacAddress,
        StreamMode = device.StreamMode,
        LastError = device.LastError,
        LastSeenUtc = device.LastSeenUtc,
        CurrentEncoderIp = device.CurrentEncoderIp,
        CurrentSourceName = registry.FindByIp(device.CurrentEncoderIp)?.Name,
        ColorIndex = registry.ColorIndexFor(device),
        CurrentSourceColorIndex = registry.FindByIp(device.CurrentEncoderIp) is { } source
            ? registry.ColorIndexFor(source)
            : null
    };

    public static IReadOnlyList<DeviceView> From(DeviceRegistry registry) =>
        registry.All().Select(device => From(device, registry)).ToList();
}
