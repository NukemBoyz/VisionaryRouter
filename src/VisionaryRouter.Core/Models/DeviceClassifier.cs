using VisionaryRouter.Core.Models;

namespace VisionaryRouter.Core.Protocol;

/// <summary>
/// Works out whether a model string names an encoder or a decoder.
///
/// Deliberately rule-based rather than a lookup table: a hardcoded model list would
/// misclassify every product Visionary releases after this was written. Anything the
/// rules cannot place comes back Unknown for the user to classify by hand.
/// </summary>
public static class DeviceClassifier
{
    public static DeviceType FromModel(string? model)
    {
        if (string.IsNullOrWhiteSpace(model))
        {
            return DeviceType.Unknown;
        }

        var normalized = model.Trim();

        // Duet parts are checked first: "DuetD-5" and "DuetE-5" both begin with 'D',
        // so the plain first-letter rule below would call every Duet a decoder.
        if (normalized.Contains("DuetE", StringComparison.OrdinalIgnoreCase))
        {
            return DeviceType.Encoder;
        }

        if (normalized.Contains("DuetD", StringComparison.OrdinalIgnoreCase))
        {
            return DeviceType.Decoder;
        }

        // A bare "Duet" with no E/D discriminator is ambiguous by design - those units
        // can be either - so leave it for the user rather than guessing.
        if (normalized.StartsWith("Duet", StringComparison.OrdinalIgnoreCase))
        {
            return DeviceType.Unknown;
        }

        // E4100 / E5200 / D4100 / D5200 and friends.
        return char.ToUpperInvariant(normalized[0]) switch
        {
            'E' => DeviceType.Encoder,
            'D' => DeviceType.Decoder,
            _ => DeviceType.Unknown
        };
    }
}
