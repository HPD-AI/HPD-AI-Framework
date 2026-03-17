using System.Text.Json;
using System.Text.Json.Serialization;

namespace HPDOS.Core.Shell;

/// <summary>
/// Persists per-provider runtime options to provider-options.json.
/// These are injected into Chat.AdditionalProperties on every stream request,
/// overriding whatever the agent's AgentConfig.Provider.ProviderOptionsJson says.
///
/// Format: { "anthropic": { "thinkingBudgetTokens": 4096 }, "openai": { ... } }
/// </summary>
public class ProviderOptionsStore
{
    private static readonly string FilePath =
        Path.Combine(HpdosDataPaths.Root, "provider-options.json");

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    // In-memory store: providerKey → { paramKey → value }
    private Dictionary<string, Dictionary<string, object>> _store = new();

    // ── Load / Save ───────────────────────────────────────────────────────────

    public static async Task<ProviderOptionsStore> LoadAsync()
    {
        var store = new ProviderOptionsStore();
        if (!File.Exists(FilePath))
            return store;

        try
        {
            var json = await File.ReadAllTextAsync(FilePath);
            var raw = JsonSerializer.Deserialize<Dictionary<string, Dictionary<string, JsonElement>>>(json);
            if (raw != null)
            {
                foreach (var (provider, opts) in raw)
                {
                    store._store[provider] = opts.ToDictionary(
                        kv => kv.Key,
                        kv => (object)kv.Value);
                }
            }
        }
        catch { /* corrupt file — start fresh */ }

        return store;
    }

    public async Task SaveAsync()
    {
        Directory.CreateDirectory(HpdosDataPaths.Root);
        var json = JsonSerializer.Serialize(_store, JsonOpts);
        await File.WriteAllTextAsync(FilePath, json);
    }

    // ── Read ──────────────────────────────────────────────────────────────────

    /// <summary>Returns the stored options for a provider, or empty dict.</summary>
    public Dictionary<string, object> GetOptions(string providerKey)
    {
        if (_store.TryGetValue(providerKey, out var opts))
            return new Dictionary<string, object>(opts);
        return new Dictionary<string, object>();
    }

    /// <summary>Returns true if there are any stored options for this provider.</summary>
    public bool HasOptions(string providerKey) =>
        _store.TryGetValue(providerKey, out var d) && d.Count > 0;

    // ── Write ─────────────────────────────────────────────────────────────────

    /// <summary>Sets a single option value. Pass null to clear it.</summary>
    public async Task SetOptionAsync(string providerKey, string key, object? value)
    {
        if (!_store.ContainsKey(providerKey))
            _store[providerKey] = new Dictionary<string, object>();

        if (value is null)
            _store[providerKey].Remove(key);
        else
            _store[providerKey][key] = value;

        // Prune empty provider entries.
        if (_store[providerKey].Count == 0)
            _store.Remove(providerKey);

        await SaveAsync();
    }

    /// <summary>Replaces all options for a provider atomically.</summary>
    public async Task SetAllOptionsAsync(string providerKey, Dictionary<string, object?> options)
    {
        var cleaned = options
            .Where(kv => kv.Value is not null)
            .ToDictionary(kv => kv.Key, kv => kv.Value!);

        if (cleaned.Count == 0)
            _store.Remove(providerKey);
        else
            _store[providerKey] = cleaned;

        await SaveAsync();
    }

    /// <summary>Clears all stored options for a provider.</summary>
    public async Task ClearAsync(string providerKey)
    {
        _store.Remove(providerKey);
        await SaveAsync();
    }

    // ── Coerce helpers (JsonElement → .NET primitives) ────────────────────────

    /// <summary>
    /// Unwraps JsonElement values (produced by deserialization) into primitives.
    /// Call this before inserting into Chat.AdditionalProperties.
    /// </summary>
    public static Dictionary<string, object> Coerce(Dictionary<string, object> raw)
    {
        var result = new Dictionary<string, object>(raw.Count);
        foreach (var (k, v) in raw)
        {
            result[k] = v is JsonElement el ? CoerceElement(el) : v;
        }
        return result;
    }

    private static object CoerceElement(JsonElement el) => el.ValueKind switch
    {
        JsonValueKind.True    => (object)true,
        JsonValueKind.False   => false,
        JsonValueKind.Number  => el.TryGetInt64(out var i) ? i : (object)el.GetDouble(),
        JsonValueKind.String  => el.GetString() ?? string.Empty,
        JsonValueKind.Null    => null!,
        _                     => el.GetRawText()
    };
}
