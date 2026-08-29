using System.Collections.Concurrent;
using VisionaryRouter.Core.Models;
using VisionaryRouter.Server.Data;

namespace VisionaryRouter.Server.Services;

/// <summary>
/// The live device list: in memory for reads, written through to SQLite on change.
///
/// Polling updates these objects continuously, so serving reads from memory keeps the UI
/// fast and the database quiet. Configuration edits persist immediately; status churn does not.
/// </summary>
public sealed class DeviceRegistry
{
    private readonly DeviceStore _store;
    private readonly ConcurrentDictionary<Guid, Device> _devices = new();

    public DeviceRegistry(DeviceStore store)
    {
        _store = store;
    }

    public void Load()
    {
        foreach (var device in _store.LoadAll())
        {
            _devices[device.Id] = device;
        }
    }

    public IReadOnlyList<Device> All() => _devices.Values.OrderBy(d => d.Name, StringComparer.OrdinalIgnoreCase).ToList();

    public IReadOnlyList<Device> Encoders() => All().Where(d => d.Type == DeviceType.Encoder).ToList();

    public IReadOnlyList<Device> Decoders() => All().Where(d => d.Type == DeviceType.Decoder).ToList();

    public Device? Find(Guid id) => _devices.TryGetValue(id, out var device) ? device : null;

    /// <summary>Finds a device by IP so a decoder's reported source can be shown as a name.</summary>
    public Device? FindByIp(string? ipAddress) =>
        string.IsNullOrWhiteSpace(ipAddress)
            ? null
            : _devices.Values.FirstOrDefault(d =>
                string.Equals(d.IpAddress, ipAddress, StringComparison.OrdinalIgnoreCase));

    public void Add(Device device)
    {
        _devices[device.Id] = device;
        _store.Upsert(device);
    }

    /// <summary>Persists configuration changes made to a device already in the registry.</summary>
    public void Save(Device device)
    {
        device.UpdatedUtc = DateTime.UtcNow;
        _devices[device.Id] = device;
        _store.Upsert(device);
    }

    public bool Remove(Guid id)
    {
        if (!_devices.TryRemove(id, out _))
        {
            return false;
        }

        _store.Delete(id);
        return true;
    }
}
