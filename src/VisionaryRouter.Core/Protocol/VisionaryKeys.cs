namespace VisionaryRouter.Core.Protocol;

/// <summary>
/// Visionary API key names we read or write by name. Keys are case-sensitive on the wire.
///
/// This is not an exhaustive list of what an endpoint can return, and it is not meant to be.
/// The parser preserves every key it receives; these are only the ones we act on.
/// </summary>
public static class VisionaryKeys
{
    /// <summary>On a decoder, the IP of the encoder it is following. Writing it performs a route.</summary>
    public const string StreamHost = "STREAM.HOST";

    public const string StreamMode = "STREAM.MODE";
    public const string StreamConnect = "STREAM.CONNECT";
    public const string VideoTiming = "VIDEO.TIMING";
    public const string StreamVideo = "STREAM.VIDEO";

    public const string UnitId = "UNIT.ID";
    public const string UnitModel = "UNIT.MODEL";
    public const string UnitSerial = "UNIT.SERIAL";
    public const string UnitFirmware = "UNIT.FIRMWARE";
    public const string UnitFirmwareDate = "UNIT.FIRMWARE_DATE";
    public const string UnitMacAddress = "UNIT.MAC_ADDRESS";

    public const string IpAddress = "IP.ADDRESS";

    /// <summary>Result marker present on most responses, e.g. SUCCESS or SUCCESS_QUERY.</summary>
    public const string ApiStatus = "API.STATUS";
}
