namespace VisionaryRouter.Core.Protocol;

/// <summary>
/// Strips credentials out of endpoint responses.
///
/// QUERY.ALL on tested 4-Series firmware (2.3.152) returns UNIT.PASSWORD - the endpoint's own
/// administrative password - alongside ordinary metadata. Raw responses reach a console, a
/// debug log, an HTTP response and the browser's activity panel, so a credential arriving in
/// one is a credential in all four.
///
/// Redaction happens during parsing rather than at each of those call sites. A sensitive value
/// therefore does not survive into the application at all, and no future code path can leak
/// one by forgetting to sanitise. This application has no use for the password: changing
/// endpoint credentials is explicitly outside what it does.
/// </summary>
public static class VisionaryRedaction
{
    public const string Placeholder = "***REDACTED***";

    /// <summary>
    /// Substrings that mark a key as carrying a secret.
    ///
    /// Matched against the key only, never the value. Note the absence of a bare "KEY": the
    /// protocol's own QUERY.KEY is a command parameter naming which field to read, not a
    /// credential, and blanking it would break route verification.
    /// </summary>
    private static readonly string[] SensitiveMarkers =
    [
        "PASSWORD",
        "PASSWD",
        "PASSPHRASE",
        "SECRET",
        "CREDENTIAL",
        "TOKEN"
    ];

    public static bool IsSensitive(string key) =>
        SensitiveMarkers.Any(marker => key.Contains(marker, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Replaces the value of every sensitive field in a raw response, leaving the rest of the
    /// text byte for byte as the endpoint sent it.
    ///
    /// Substitution is done in place rather than by reassembling the response from parsed
    /// fields, so diagnostic output keeps the original spacing, ordering and delimiters - the
    /// details that matter when a firmware revision starts formatting things differently.
    /// </summary>
    public static string Redact(string raw)
    {
        if (string.IsNullOrEmpty(raw))
        {
            return raw;
        }

        var result = raw;

        foreach (var field in raw.Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var separator = field.IndexOf('=');
            if (separator <= 0)
            {
                continue;
            }

            var key = field[..separator].Trim();
            var value = field[(separator + 1)..];

            if (value.Length == 0 || !IsSensitive(key))
            {
                continue;
            }

            result = result.Replace(field, $"{key}={Placeholder}", StringComparison.Ordinal);
        }

        return result;
    }
}
