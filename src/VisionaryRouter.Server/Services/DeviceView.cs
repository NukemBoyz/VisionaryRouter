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
    public string? VideoInput { get; init; }
    public bool? VideoSignalPresent { get; init; }
    public string? VideoTiming { get; init; }
    public int SortOrder { get; init; }
    public string? LastError { get; init; }
    public DateTime? LastSeenUtc { get; init; }

    /// <summary>Decoders only: the encoder IP the device reports it is following.</summary>
    public string? CurrentEncoderIp { get; init; }
    public bool? StreamConnected { get; init; }
    public bool RouteCleared { get; init; }

    /// <summary>
    /// Friendly name for <see cref="CurrentEncoderIp"/>, when that IP belongs to a device we
    /// know. Null means the decoder is following something outside this application's list -
    /// worth showing as the raw IP rather than hiding.
    /// </summary>
    public string? CurrentSourceName { get; init; }

    /// <summary>Encoders only: which colour slot this source occupies in the UI.</summary>
    public int? ColorIndex { get; init; }

    /// <summary>
    /// Decoders only: the colour the UI paints this destination - the encoder routed to it.
    ///
    /// Resolved from what the decoder reports when it reports anything, falling back to what
    /// this application last sent it. The fallback is what keeps an unreachable decoder, or
    /// one that accepted a route and never switched, showing the source it was assigned
    /// rather than going blank. Null means nothing has been routed here and the decoder is
    /// not following anything we know about.
    /// </summary>
    public int? RoutedSourceColorIndex { get; init; }

    /// <summary>Decoders only: the encoder this decoder was assigned by this application.</summary>
    public string? AssignedEncoderIp { get; init; }

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
        VideoInput = device.VideoInput,
        VideoSignalPresent = device.VideoSignalPresent,
        VideoTiming = device.VideoTiming,
        SortOrder = device.SortOrder,
        LastError = device.LastError,
        LastSeenUtc = device.LastSeenUtc,
        CurrentEncoderIp = device.CurrentEncoderIp,
        StreamConnected = device.StreamConnected,
        RouteCleared = device.RouteCleared,
        CurrentSourceName = registry.FindByIp(device.CurrentEncoderIp)?.Name,
        AssignedEncoderIp = device.AssignedEncoderIp,
        ColorIndex = registry.ColorIndexFor(device),

        // Reported source first, so a route made outside this application still colours the
        // destination; our own assignment second, so a decoder that was sent somewhere still
        // shows where even when it never confirmed.
        RoutedSourceColorIndex =
            registry.FindByIp(device.CurrentEncoderIp) is { } reported ? registry.ColorIndexFor(reported)
            : registry.FindByIp(device.AssignedEncoderIp) is { } assigned ? registry.ColorIndexFor(assigned)
            : null
    };

    public static IReadOnlyList<DeviceView> From(DeviceRegistry registry) =>
        registry.All().Select(device => From(device, registry)).ToList();
}
