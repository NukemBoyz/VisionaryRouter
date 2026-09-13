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

    public IReadOnlyList<Device> All() => _devices.Values.OrderBy(d => d.SortOrder).ThenBy(d => d.Name, StringComparer.OrdinalIgnoreCase).ToList();

    public IReadOnlyList<Device> Encoders() => All().Where(d => d.Type == DeviceType.Encoder).ToList();

    public IReadOnlyList<Device> Decoders() => All().Where(d => d.Type == DeviceType.Decoder).ToList();

    public Device? Find(Guid id) => _devices.TryGetValue(id, out var device) ? device : null;

    /// <summary>Finds a device by IP so a decoder's reported source can be shown as a name.</summary>
    public Device? FindByIp(string? ipAddress) =>
        string.IsNullOrWhiteSpace(ipAddress)
            ? null
            : _devices.Values.FirstOrDefault(d =>
                string.Equals(d.IpAddress, ipAddress, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Which colour slot an encoder occupies in the UI, or null if it is not an encoder.
    ///
    /// Encoders are ordered by when they were added rather than by name, so adding a source
    /// or renaming one never recolours the sources already on screen. Deleting an encoder does
    /// shift the ones added after it, which is the accepted cost of assigning slots in order
    /// instead of hashing the id - hashing is stable through deletes but collides, and two
    /// sources sharing a colour defeats the point.
    /// </summary>
    public int? ColorIndexFor(Device device)
    {
        if (device.Type != DeviceType.Encoder)
        {
            return null;
        }

        var ordered = _devices.Values
            .Where(d => d.Type == DeviceType.Encoder)
            .OrderBy(d => d.CreatedUtc)
            .ThenBy(d => d.Id)
            .ToList();

        var position = ordered.FindIndex(d => d.Id == device.Id);

        return position < 0 ? null : position;
    }

    public void Add(Device device)
    {
        if (_devices.Count > 0 && device.SortOrder == 0)
        {
            device.SortOrder = _devices.Values.Max(d => d.SortOrder) + 1;
        }
        _devices[device.Id] = device;
        _store.Upsert(device);
    }


    public bool Reorder(DeviceType type, IReadOnlyList<Guid> ids)
    {
        var expected = _devices.Values.Where(d => d.Type == type).Select(d => d.Id).ToHashSet();
        if (ids.Count != expected.Count || !ids.ToHashSet().SetEquals(expected)) return false;

        for (var index = 0; index < ids.Count; index++)
        {
            var device = _devices[ids[index]];
            device.SortOrder = index;
            Save(device);
        }
        return true;
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
