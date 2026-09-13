using System.Text.Json;
using VisionaryRouter.Server.Data;

namespace VisionaryRouter.Server.Services;

public sealed record DecoderGroup(Guid Id, string Name, Guid[] DecoderIds);
public sealed record PresetRoute(Guid DecoderId, Guid EncoderId);
public sealed record RoutePreset(Guid Id, string Name, PresetRoute[] Routes);

/// <summary>Small operator-created routing collections persisted in SQLite Settings as JSON.</summary>
public sealed class RoutingConfigurationStore
{
    private const string GroupsKey = "DecoderGroups";
    private const string PresetsKey = "RoutePresets";
    private readonly DeviceStore _store;
    private readonly Lock _gate = new();
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public RoutingConfigurationStore(DeviceStore store) => _store = store;

    public IReadOnlyList<DecoderGroup> Groups() => Load<DecoderGroup>(GroupsKey);
    public IReadOnlyList<RoutePreset> Presets() => Load<RoutePreset>(PresetsKey);

    public DecoderGroup SaveGroup(string name, Guid[] decoderIds)
    {
        lock (_gate)
        {
            var items = Load<DecoderGroup>(GroupsKey).ToList();
            var item = new DecoderGroup(Guid.NewGuid(), name.Trim(), decoderIds.Distinct().ToArray());
            items.Add(item);
            Save(GroupsKey, items);
            return item;
        }
    }

    public RoutePreset SavePreset(string name, PresetRoute[] routes)
    {
        lock (_gate)
        {
            var items = Load<RoutePreset>(PresetsKey).ToList();
            var item = new RoutePreset(Guid.NewGuid(), name.Trim(), routes.DistinctBy(r => r.DecoderId).ToArray());
            items.Add(item);
            Save(PresetsKey, items);
            return item;
        }
    }

    public bool DeleteGroup(Guid id) => Delete<DecoderGroup>(GroupsKey, x => x.Id == id);
    public bool DeletePreset(Guid id) => Delete<RoutePreset>(PresetsKey, x => x.Id == id);

    private bool Delete<T>(string key, Func<T, bool> match)
    {
        lock (_gate)
        {
            var items = Load<T>(key).ToList();
            var removed = items.RemoveAll(x => match(x)) > 0;
            if (removed) Save(key, items);
            return removed;
        }
    }

    private IReadOnlyList<T> Load<T>(string key)
    {
        var value = _store.GetSetting(key);
        return string.IsNullOrWhiteSpace(value)
            ? []
            : JsonSerializer.Deserialize<List<T>>(value, Json) ?? [];
    }

    private void Save<T>(string key, IReadOnlyList<T> values) =>
        _store.SetSetting(key, JsonSerializer.Serialize(values, Json));
}
