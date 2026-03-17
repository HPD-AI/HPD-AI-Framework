using System.Collections.Concurrent;
using HPDOS.Core.Platform.Exceptions;
using Microsoft.Extensions.Logging;

namespace HPDOS.Core.Platform.Resources;

/// <summary>
/// Centralized resource management — tracks open file handles per app.
/// </summary>
public sealed class ResourceManager
{
    private readonly ConcurrentDictionary<ResourceId, FileResource> _files = new();
    private readonly ConcurrentDictionary<ResourceId, string> _ownership = new();
    private readonly ResourceLimits _limits;
    private readonly ILogger<ResourceManager>? _logger;

    public ResourceManager(ResourceLimits? limits = null, ILogger<ResourceManager>? logger = null)
    {
        _limits = limits ?? ResourceLimits.Unlimited;
        _logger = logger;
    }

    public ResourceStats GetStats() => new()
    {
        TotalFiles     = _files.Count,
        TotalResources = _ownership.Count
    };

    public ResourceId RegisterFile(string appId, string path, FileStream handle, PlatformFileMode mode)
    {
        CheckCanRegisterFile(appId);

        var id = ResourceId.New();
        var resource = new FileResource(path, mode, handle);

        if (!_files.TryAdd(id, resource))
            throw new InvalidOperationException($"Failed to register file resource: {id}");

        _ownership[id] = appId;
        _logger?.LogDebug("Registered file resource: {Id} (app: {AppId})", id, appId);
        return id;
    }

    public FileResource? GetFile(ResourceId id) => _files.GetValueOrDefault(id);

    public async ValueTask ReleaseAsync(ResourceId id)
    {
        if (_files.TryRemove(id, out var resource))
            await resource.DisposeAsync();
        _ownership.TryRemove(id, out _);
        _logger?.LogDebug("Released resource: {Id}", id);
    }

    public async ValueTask ReleaseAppResourcesAsync(string appId)
    {
        var ids = _ownership
            .Where(kv => kv.Value == appId)
            .Select(kv => kv.Key)
            .ToList();

        foreach (var id in ids)
            await ReleaseAsync(id);

        _logger?.LogInformation("Released all resources for app: {AppId}", appId);
    }

    public int GetAppResourceCount(string appId) =>
        _ownership.Values.Count(owner => owner == appId);

    private void CheckCanRegisterFile(string appId)
    {
        if (_limits.MaxTotalFiles > 0 && _files.Count >= _limits.MaxTotalFiles)
            throw new ResourceLimitExceededException("file", "global", _limits.MaxTotalFiles);

        if (_limits.MaxFilesPerApp > 0)
        {
            var count = _ownership.Count(kv => kv.Value == appId && _files.ContainsKey(kv.Key));
            if (count >= _limits.MaxFilesPerApp)
                throw new ResourceLimitExceededException("file", $"per-app ({appId})", _limits.MaxFilesPerApp);
        }
    }
}
