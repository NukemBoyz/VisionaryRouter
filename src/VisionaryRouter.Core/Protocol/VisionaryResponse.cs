namespace VisionaryRouter.Core.Protocol;

/// <summary>
/// A parsed endpoint response, plus the raw text it came from.
///
/// Responses are ampersand-separated key/value pairs, typically with a leading '&amp;':
///   &amp;STREAM.HOST=192.168.8.101&amp;API.STATUS=SUCCESS_QUERY
///
/// Parsing is deliberately forgiving. Unknown keys are preserved rather than dropped, so a
/// firmware update that adds fields cannot break us, and malformed input yields an empty
/// result instead of an exception.
/// </summary>
public sealed class VisionaryResponse
{
    private readonly Dictionary<string, string> _values;

    private VisionaryResponse(string raw, Dictionary<string, string> values)
    {
        Raw = raw;
        _values = values;
    }

    /// <summary>
    /// The response as received, with credential values replaced. Always logged, never
    /// discarded - it is the record of what the endpoint actually said, and the only part
    /// withheld is the part no diagnosis needs.
    /// </summary>
    public string Raw { get; }

    /// <summary>Every key/value pair found, including ones this application does not understand.</summary>
    public IReadOnlyDictionary<string, string> Values => _values;

    /// <summary>Value of API.STATUS, or null if the endpoint did not send one.</summary>
    public string? ApiStatus => Get(VisionaryKeys.ApiStatus);

    /// <summary>
    /// True when the endpoint reported success. Visionary uses several SUCCESS_* variants
    /// (SUCCESS, SUCCESS_QUERY, ...), so this matches on the prefix.
    /// </summary>
    public bool IsSuccess =>
        ApiStatus is { } status && status.StartsWith("SUCCESS", StringComparison.Ordinal);

    public string? Get(string key) => _values.TryGetValue(key, out var value) ? value : null;

    public bool TryGet(string key, out string value) => _values.TryGetValue(key, out value!);

    public static VisionaryResponse Parse(string? raw)
    {
        raw ??= string.Empty;

        // Credentials are stripped here, at the boundary, so no sensitive value ever exists
        // inside the application to be logged, serialised or displayed later. See
        // VisionaryRedaction for why this is done during parsing rather than at each output.
        var redacted = VisionaryRedaction.Redact(raw);

        // Keys are case-sensitive, so the dictionary is too.
        var values = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var field in redacted.Split('&', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            // Split on the first '=' only: values may legitimately contain '='.
            var separator = field.IndexOf('=');
            if (separator <= 0)
            {
                continue;
            }

            var key = field[..separator].Trim();
            var value = field[(separator + 1)..].Trim();

            // First occurrence wins, matching how the endpoints echo their own envelope keys.
            values.TryAdd(key, value);
        }

        return new VisionaryResponse(redacted, values);
    }
}
