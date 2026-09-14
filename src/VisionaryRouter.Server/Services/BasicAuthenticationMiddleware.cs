using System.Security.Cryptography;
using System.Text;

namespace VisionaryRouter.Server.Services;

public sealed record BasicAuthenticationOptions(string? PasswordHash);

public static class PasswordCredential
{
    private const int Iterations = 150_000;

    public static string Hash(string password)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(password);
        var salt = RandomNumberGenerator.GetBytes(16);
        var hash = Rfc2898DeriveBytes.Pbkdf2(password, salt, Iterations, HashAlgorithmName.SHA256, 32);
        return $"pbkdf2-sha256${Iterations}${Convert.ToBase64String(salt)}${Convert.ToBase64String(hash)}";
    }

    public static bool Verify(string password, string encoded)
    {
        try
        {
            var parts = encoded.Split('$');
            if (parts.Length != 4 || parts[0] != "pbkdf2-sha256" || !int.TryParse(parts[1], out var iterations))
                return false;
            var salt = Convert.FromBase64String(parts[2]);
            var expected = Convert.FromBase64String(parts[3]);
            var supplied = Rfc2898DeriveBytes.Pbkdf2(password, salt, iterations, HashAlgorithmName.SHA256, expected.Length);
            return CryptographicOperations.FixedTimeEquals(supplied, expected);
        }
        catch (FormatException)
        {
            return false;
        }
    }
}

public sealed class BasicAuthenticationMiddleware
{
    private readonly RequestDelegate _next;
    private readonly BasicAuthenticationOptions _options;

    public BasicAuthenticationMiddleware(RequestDelegate next, BasicAuthenticationOptions options)
    {
        _next = next;
        _options = options;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        if (string.IsNullOrEmpty(_options.PasswordHash) || IsValid(context.Request.Headers.Authorization.ToString()))
        {
            await _next(context);
            return;
        }

        context.Response.StatusCode = StatusCodes.Status401Unauthorized;
        context.Response.Headers.WWWAuthenticate = "Basic realm=\"Visionary Router\", charset=\"UTF-8\"";
        await context.Response.WriteAsync("Authentication required");
    }

    private bool IsValid(string header)
    {
        if (!header.StartsWith("Basic ", StringComparison.OrdinalIgnoreCase)) return false;
        try
        {
            var decoded = Encoding.UTF8.GetString(Convert.FromBase64String(header[6..].Trim()));
            var separator = decoded.IndexOf(':');
            if (separator < 0 || decoded[..separator] != "admin") return false;
            return PasswordCredential.Verify(decoded[(separator + 1)..], _options.PasswordHash!);
        }
        catch (FormatException)
        {
            return false;
        }
    }
}
