using System.Collections.Concurrent;
using System.Net;
using VisionaryRouter.Core.Devices;
using VisionaryRouter.Core.Models;
using VisionaryRouter.Core.Protocol;

namespace VisionaryRouter.Server.Services;

public sealed record DiscoveryDevice(
    Guid Id,
    string Name,
    string IpAddress,
    string Type,
    string? Model,
    string Action);

public sealed record DiscoveryResult(
    string Subnet,
    int AddressesScanned,
    IReadOnlyList<DiscoveryDevice> Devices);

/// <summary>
/// Explicit, operator-triggered discovery. V1 deliberately limits scans to one IPv4 /24 so
/// an accidental broad CIDR cannot generate thousands of endpoint queries.
/// </summary>
public sealed class DiscoveryService
{
    private const int MaxConcurrency = 32;

    private readonly IVisionaryTransport _transport;
    private readonly VisionaryDeviceClient _client;
    private readonly DeviceRegistry _registry;
    private readonly ILogger<DiscoveryService> _logger;

    public DiscoveryService(
        IVisionaryTransport transport,
        VisionaryDeviceClient client,
        DeviceRegistry registry,
        ILogger<DiscoveryService> logger)
    {
        _transport = transport;
        _client = client;
        _registry = registry;
        _logger = logger;
    }

    public async Task<DiscoveryResult> ScanAndAddAsync(
        string subnet,
        CancellationToken cancellationToken = default)
    {
        var (normalized, prefix) = Parse24(subnet);
        var discovered = new ConcurrentBag<(string Ip, VisionaryResponse Response)>();
        using var gate = new SemaphoreSlim(MaxConcurrency, MaxConcurrency);

        await Task.WhenAll(Enumerable.Range(1, 254).Select(async host =>
        {
            await gate.WaitAsync(cancellationToken);
            try
            {
                var ip = $"{prefix}.{host}";
                var result = await _transport.QueryAsync(
                    new IPEndPoint(IPAddress.Parse(ip), VisionaryCommand.DefaultPort),
                    VisionaryCommand.StatusQuery(),
                    cancellationToken);

                if (result.Success && result.Response is { IsSuccess: true } response &&
                    (response.Get(VisionaryKeys.UnitModel) is not null ||
                     response.Get(VisionaryKeys.UnitId) is not null))
                {
                    discovered.Add((ip, response));
                }
            }
            finally
            {
                gate.Release();
            }
        }));

        var devices = new List<DiscoveryDevice>();

        foreach (var found in discovered.OrderBy(d => IPAddress.Parse(d.Ip).GetAddressBytes()[3]))
        {
            var existing = _registry.FindByIp(found.Ip);

            if (existing is not null)
            {
                await _client.ProbeAsync(existing, cancellationToken);
                _registry.Save(existing);
                devices.Add(ToResult(existing, "refreshed"));
                continue;
            }

            var model = found.Response.Get(VisionaryKeys.UnitModel);
            var unitId = found.Response.Get(VisionaryKeys.UnitId);
            var device = new Device
            {
                Id = Guid.NewGuid(),
                Name = string.IsNullOrWhiteSpace(unitId) ? found.Ip : unitId,
                Type = DeviceClassifier.FromModel(model),
                IpAddress = found.Ip,
                Model = model,
                UnitId = unitId,
                OnlineState = OnlineState.Online,
                LastSeenUtc = DateTime.UtcNow
            };

            // Enrich the lightweight discovery response with firmware, stream mode, input,
            // and other safe metadata. A status response is already enough to confirm and add
            // the endpoint if the heavier full query happens to fail.
            await _client.ProbeAsync(device, cancellationToken);
            _registry.Add(device);
            devices.Add(ToResult(device, "added"));
        }

        _logger.LogInformation(
            "Discovery scan {Subnet} completed: {Found} Visionary device(s), {Added} added",
            normalized, devices.Count, devices.Count(d => d.Action == "added"));

        return new DiscoveryResult(normalized, 254, devices);
    }

    private static DiscoveryDevice ToResult(Device device, string action) => new(
        device.Id,
        device.Name,
        device.IpAddress,
        device.Type.ToString(),
        device.Model,
        action);

    private static (string Normalized, string Prefix) Parse24(string subnet)
    {
        if (string.IsNullOrWhiteSpace(subnet))
        {
            throw new ArgumentException("Enter one IPv4 /24 subnet, for example 192.168.0.0/24.");
        }

        var parts = subnet.Trim().Split('/', 2);
        if (parts.Length != 2 || parts[1] != "24" || !IPAddress.TryParse(parts[0], out var address) ||
            address.AddressFamily != System.Net.Sockets.AddressFamily.InterNetwork)
        {
            throw new ArgumentException("Enter one IPv4 /24 subnet, for example 192.168.0.0/24.");
        }

        var bytes = address.GetAddressBytes();
        var prefix = $"{bytes[0]}.{bytes[1]}.{bytes[2]}";
        return ($"{prefix}.0/24", prefix);
    }
}
