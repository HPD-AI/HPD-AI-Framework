using System.Text.Json;
using System.Text.Json.Serialization;

namespace HPDOS.Core.Shell;

public class UserPreferences
{
    [JsonPropertyName("defaultProvider")]
    public string? DefaultProvider { get; set; }

    [JsonPropertyName("defaultModel")]
    public string? DefaultModel { get; set; }
}

[JsonSerializable(typeof(UserPreferences))]
[JsonSourceGenerationOptions(WriteIndented = true, PropertyNameCaseInsensitive = true)]
internal partial class UserPreferencesJsonContext : JsonSerializerContext { }

/// <summary>
/// Reads and writes the global default provider/model preference to
/// HpdosDataPaths.Preferences (preferences.json). Thread-safe.
/// </summary>
public class UserPreferencesStore
{
    private readonly SemaphoreSlim _lock = new(1, 1);

    public async Task<UserPreferences> GetAsync()
    {
        await _lock.WaitAsync();
        try
        {
            var path = HpdosDataPaths.Preferences;
            if (!File.Exists(path)) return new UserPreferences();
            var json = await File.ReadAllTextAsync(path);
            return JsonSerializer.Deserialize(json, UserPreferencesJsonContext.Default.UserPreferences)
                   ?? new UserPreferences();
        }
        catch
        {
            return new UserPreferences();
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task SetAsync(UserPreferences prefs)
    {
        await _lock.WaitAsync();
        try
        {
            var path = HpdosDataPaths.Preferences;
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            var json = JsonSerializer.Serialize(prefs, UserPreferencesJsonContext.Default.UserPreferences);
            await File.WriteAllTextAsync(path, json);
        }
        finally
        {
            _lock.Release();
        }
    }
}
