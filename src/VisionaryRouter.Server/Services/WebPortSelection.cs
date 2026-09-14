using System.Net.NetworkInformation;

namespace VisionaryRouter.Server.Services;

public static class WebPortSelection
{
    public const int DefaultPort = 8088;
    public const int MinimumPort = 1024;
    public const int MaximumPort = 65535;

    public static bool IsValid(int port) => port is >= MinimumPort and <= MaximumPort;

    public static int ParseOrDefault(string? value, int fallback)
    {
        return int.TryParse(value, out var port) && IsValid(port) ? port : fallback;
    }

    public static int FindAvailable(int preferredPort)
    {
        var occupied = IPGlobalProperties.GetIPGlobalProperties()
            .GetActiveTcpListeners()
            .Select(endpoint => endpoint.Port)
            .ToHashSet();

        return FindAvailable(preferredPort, occupied);
    }

    public static int FindAvailable(int preferredPort, IReadOnlySet<int> occupiedPorts)
    {
        if (!IsValid(preferredPort))
        {
            throw new ArgumentOutOfRangeException(nameof(preferredPort));
        }

        var candidate = preferredPort;
        var portCount = MaximumPort - MinimumPort + 1;

        for (var attempt = 0; attempt < portCount; attempt++)
        {
            if (!occupiedPorts.Contains(candidate))
            {
                return candidate;
            }

            candidate = candidate == MaximumPort ? MinimumPort : candidate + 1;
        }

        throw new InvalidOperationException("No available Web UI port could be found.");
    }
}
