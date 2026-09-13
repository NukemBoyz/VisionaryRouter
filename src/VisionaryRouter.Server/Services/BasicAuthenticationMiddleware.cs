using System.Security.Cryptography;
using System.Text;

namespace VisionaryRouter.Server.Services;

public sealed class BasicAuthenticationMiddleware
{
    private readonly RequestDelegate _next;
    private readonly string? _password;

    public BasicAuthenticationMiddleware(RequestDelegate next, string? password)
    {
        _next = next;
        _password = password;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        if (string.IsNullOrEmpty(_password) || IsValid(context.Request.Headers.Authorization.ToString()))
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
            var supplied = SHA256.HashData(Encoding.UTF8.GetBytes(decoded[(separator + 1)..]));
            var expected = SHA256.HashData(Encoding.UTF8.GetBytes(_password!));
            return CryptographicOperations.FixedTimeEquals(supplied, expected);
        }
        catch (FormatException)
        {
            return false;
        }
    }
}
