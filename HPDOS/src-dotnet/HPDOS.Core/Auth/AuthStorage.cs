using System.Text.Json;
using System.Text.Json.Serialization;
using HPDOS.Core.Shell;

namespace HPDOS.Core.Auth;

[JsonSerializable(typeof(Dictionary<string, AuthSlot>))]
[JsonSerializable(typeof(Dictionary<string, AuthEntry>))]   // legacy migration
[JsonSourceGenerationOptions(
    WriteIndented = true,
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    UseStringEnumConverter = true)]
internal partial class AuthStorageJsonContext : JsonSerializerContext { }

// ── Data model ───────────────────────────────────────────────────────────────

/// <summary>One stored credential entry — has a stable ID and the raw AuthEntry payload.</summary>
public sealed class StoredAuthEntry
{
    [JsonPropertyName("id")]
    public required string Id { get; init; }

    [JsonPropertyName("entry")]
    public required AuthEntry Entry { get; set; }   // mutable for in-place token refresh
}

/// <summary>
/// All stored credentials for a single provider.
/// Exactly one entry is "active" — that is the one used for API calls.
/// </summary>
public sealed class AuthSlot
{
    [JsonPropertyName("activeEntryId")]
    public string? ActiveEntryId { get; set; }

    [JsonPropertyName("entries")]
    public List<StoredAuthEntry> Entries { get; init; } = [];

    [JsonIgnore]
    public StoredAuthEntry? ActiveStored =>
        ActiveEntryId is null ? null : Entries.Find(e => e.Id == ActiveEntryId);

    [JsonIgnore]
    public AuthEntry? ActiveEntry => ActiveStored?.Entry;
}

// ── Storage ───────────────────────────────────────────────────────────────────

public class AuthStorage
{
    private static readonly string DefaultPath =
        Path.Combine(HpdosDataPaths.Root, "providers.json");

    private readonly string _filePath;
    private readonly SemaphoreSlim _lock = new(1, 1);
    private Dictionary<string, AuthSlot>? _cache;

    public AuthStorage() : this(DefaultPath) { }
    public AuthStorage(string filePath) => _filePath = filePath;

    public string FilePath => _filePath;

    // ── Public API ────────────────────────────────────────────────────────────

    /// <summary>Returns the active entry for a provider, or null.</summary>
    public async Task<AuthEntry?> GetAsync(string providerId)
    {
        var all = await LoadAsync();
        return all.TryGetValue(Key(providerId), out var slot) ? slot.ActiveEntry : null;
    }

    /// <summary>
    /// Adds a new stored entry for this provider and makes it active.
    /// Does NOT remove existing entries — they remain available for switching.
    /// </summary>
    public async Task SetAsync(string providerId, AuthEntry entry)
    {
        await _lock.WaitAsync();
        try
        {
            var all = await LoadInternalAsync();
            var key = Key(providerId);

            if (!all.TryGetValue(key, out var slot))
            {
                slot = new AuthSlot();
                all[key] = slot;
            }

            var stored = new StoredAuthEntry
            {
                Id = GenerateId(),
                Entry = entry
            };
            slot.Entries.Add(stored);
            slot.ActiveEntryId = stored.Id;

            await SaveInternalAsync(all);
            _cache = all;
        }
        finally { _lock.Release(); }
    }

    /// <summary>
    /// Updates the active entry's payload in-place (used for token refresh).
    /// Does NOT add a new entry or change the active pointer.
    /// </summary>
    internal async Task UpdateActiveEntryAsync(string providerId, AuthEntry updatedEntry)
    {
        await _lock.WaitAsync();
        try
        {
            var all = await LoadInternalAsync();
            var key = Key(providerId);
            if (!all.TryGetValue(key, out var slot)) return;

            var active = slot.ActiveStored;
            if (active is null) return;

            active.Entry = updatedEntry;
            await SaveInternalAsync(all);
            _cache = all;
        }
        finally { _lock.Release(); }
    }

    /// <summary>Removes the active entry. If others remain, the most-recently-added becomes active.</summary>
    public async Task<bool> RemoveAsync(string providerId)
    {
        await _lock.WaitAsync();
        try
        {
            var all = await LoadInternalAsync();
            var key = Key(providerId);
            if (!all.TryGetValue(key, out var slot)) return false;

            var activeId = slot.ActiveEntryId;
            var removed = slot.Entries.RemoveAll(e => e.Id == activeId) > 0;
            if (!removed) return false;

            if (slot.Entries.Count == 0)
            {
                all.Remove(key);
            }
            else
            {
                slot.ActiveEntryId = slot.Entries[^1].Id;
            }

            await SaveInternalAsync(all);
            _cache = all;
            return true;
        }
        finally { _lock.Release(); }
    }

    /// <summary>Removes a specific entry by ID. If it was active, promotes the most-recently-added remaining one.</summary>
    public async Task<bool> RemoveEntryAsync(string providerId, string entryId)
    {
        await _lock.WaitAsync();
        try
        {
            var all = await LoadInternalAsync();
            var key = Key(providerId);
            if (!all.TryGetValue(key, out var slot)) return false;

            var wasActive = slot.ActiveEntryId == entryId;
            var removed = slot.Entries.RemoveAll(e => e.Id == entryId) > 0;
            if (!removed) return false;

            if (slot.Entries.Count == 0)
            {
                all.Remove(key);
            }
            else if (wasActive)
            {
                slot.ActiveEntryId = slot.Entries[^1].Id;
            }

            await SaveInternalAsync(all);
            _cache = all;
            return true;
        }
        finally { _lock.Release(); }
    }

    /// <summary>Promotes an existing stored entry to be the active one.</summary>
    public async Task<bool> SetActiveAsync(string providerId, string entryId)
    {
        await _lock.WaitAsync();
        try
        {
            var all = await LoadInternalAsync();
            var key = Key(providerId);
            if (!all.TryGetValue(key, out var slot)) return false;
            if (!slot.Entries.Any(e => e.Id == entryId)) return false;

            slot.ActiveEntryId = entryId;
            await SaveInternalAsync(all);
            _cache = all;
            return true;
        }
        finally { _lock.Release(); }
    }

    /// <summary>Returns the full slot (all entries + active pointer) for a provider, or null.</summary>
    public async Task<AuthSlot?> GetSlotAsync(string providerId)
    {
        var all = await LoadAsync();
        return all.TryGetValue(Key(providerId), out var slot) ? slot : null;
    }

    public async Task<IReadOnlyDictionary<string, AuthSlot>> GetAllAsync() => await LoadAsync();

    public async Task ClearAsync()
    {
        await _lock.WaitAsync();
        try
        {
            if (File.Exists(_filePath)) File.Delete(_filePath);
            _cache = new Dictionary<string, AuthSlot>();
        }
        finally { _lock.Release(); }
    }

    public async Task<bool> HasCredentialsAsync(string providerId) =>
        (await GetAsync(providerId)) != null;

    // ── Internals ─────────────────────────────────────────────────────────────

    private async Task<Dictionary<string, AuthSlot>> LoadAsync()
    {
        await _lock.WaitAsync();
        try { return await LoadInternalAsync(); }
        finally { _lock.Release(); }
    }

    private async Task<Dictionary<string, AuthSlot>> LoadInternalAsync()
    {
        if (_cache != null) return _cache;
        if (!File.Exists(_filePath))
        {
            _cache = new Dictionary<string, AuthSlot>();
            return _cache;
        }

        try
        {
            var json = await File.ReadAllTextAsync(_filePath);

            if (IsLegacyFormat(json))
            {
                // Migrate: old format is Dictionary<string, AuthEntry>
                var legacy = JsonSerializer.Deserialize(json, AuthStorageJsonContext.Default.DictionaryStringAuthEntry)
                             ?? new Dictionary<string, AuthEntry>();

                _cache = new Dictionary<string, AuthSlot>(StringComparer.OrdinalIgnoreCase);
                foreach (var (k, entry) in legacy)
                {
                    var stored = new StoredAuthEntry { Id = GenerateId(), Entry = entry };
                    var slot = new AuthSlot { ActiveEntryId = stored.Id };
                    slot.Entries.Add(stored);
                    _cache[k] = slot;
                }

                // Write migrated format back immediately.
                await SaveInternalAsync(_cache);
                return _cache;
            }

            _cache = JsonSerializer.Deserialize(json, AuthStorageJsonContext.Default.DictionaryStringAuthSlot)
                     ?? new Dictionary<string, AuthSlot>(StringComparer.OrdinalIgnoreCase);
            return _cache;
        }
        catch (JsonException)
        {
            _cache = new Dictionary<string, AuthSlot>();
            return _cache;
        }
    }

    private async Task SaveInternalAsync(Dictionary<string, AuthSlot> data)
    {
        var directory = Path.GetDirectoryName(_filePath);
        if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
            Directory.CreateDirectory(directory);

        var json = JsonSerializer.Serialize(data, AuthStorageJsonContext.Default.DictionaryStringAuthSlot);
        await File.WriteAllTextAsync(_filePath, json);

        if (!OperatingSystem.IsWindows())
        {
            try { File.SetUnixFileMode(_filePath, UnixFileMode.UserRead | UnixFileMode.UserWrite); }
            catch { }
        }
    }

    private static bool IsLegacyFormat(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            foreach (var prop in doc.RootElement.EnumerateObject())
            {
                if (prop.Value.ValueKind == JsonValueKind.Object &&
                    prop.Value.TryGetProperty("type", out _))
                    return true;
            }
        }
        catch { }
        return false;
    }

    private static string GenerateId() => Guid.NewGuid().ToString("N")[..8];
    private static string Key(string providerId) => providerId.ToLowerInvariant();
}
