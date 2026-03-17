using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text.Json;
using HPDOS.Core.Platform.Exceptions;
using HPDOS.Core.Platform.Resources;
using Microsoft.Extensions.Logging;

namespace HPDOS.Core.Platform;

/// <summary>
/// Manages all registered applications, routing commands and tracking capabilities.
/// </summary>
public sealed class HPDOSPlatform
{
    private readonly ConcurrentDictionary<string, IApplication> _apps = new();
    private readonly ConcurrentDictionary<string, AppCapabilities> _capabilities = new();
    private readonly ILogger<HPDOSPlatform>? _logger;

    public ResourceManager Resources { get; }
    public IEventEmitter? Emitter { get; private set; }

    public HPDOSPlatform(ResourceManager? resources = null, ILogger<HPDOSPlatform>? logger = null)
    {
        Resources = resources ?? new ResourceManager();
        _logger = logger;
    }

    public void SetEmitter(IEventEmitter emitter) => Emitter = emitter;

    public ValueTask RegisterAppAsync(IApplication app, CancellationToken ct = default)
        => RegisterAppWithCapabilitiesAsync(app, AppCapabilities.Unrestricted, ct);

    public async ValueTask RegisterAppWithCapabilitiesAsync(
        IApplication app,
        AppCapabilities capabilities,
        CancellationToken ct = default)
    {
        if (_apps.ContainsKey(app.Id))
            throw new InvalidOperationException($"App '{app.Id}' already registered");

        var ctx = new PlatformContext(Resources, Emitter);
        await app.InitializeAsync(ctx, ct);

        _capabilities[app.Id] = capabilities;

        if (!_apps.TryAdd(app.Id, app))
            throw new InvalidOperationException($"Failed to register app '{app.Id}'");

        _logger?.LogInformation("Registered app: {Name} ({Id})", app.Name, app.Id);
    }

    public AppCapabilities? GetCapabilities(string appId) => _capabilities.GetValueOrDefault(appId);

    public async ValueTask<JsonElement> RouteCommandAsync(
        string appId,
        string command,
        JsonElement payload,
        CancellationToken ct = default)
    {
        if (!_apps.TryGetValue(appId, out var app))
            throw new AppNotFoundException(appId);

        var sw = Stopwatch.StartNew();
        try
        {
            return await app.HandleCommandAsync(command, payload, ct);
        }
        catch (Exception ex) when (ex is not AppException)
        {
            _logger?.LogError(ex, "App '{AppId}' threw during '{Command}'", appId, command);
            throw new AppPanicException(appId, command, ex);
        }
        finally
        {
            sw.Stop();
            if (sw.Elapsed > TimeSpan.FromSeconds(1))
                _logger?.LogWarning("Slow command: {AppId}.{Command} took {Elapsed}", appId, command, sw.Elapsed);
            else
                _logger?.LogDebug("Command {AppId}.{Command} completed in {Elapsed}", appId, command, sw.Elapsed);
        }
    }

    public async ValueTask ShutdownAllAsync(CancellationToken ct = default)
    {
        foreach (var app in _apps.Values)
        {
            try { await app.ShutdownAsync(ct); }
            catch (Exception ex) { _logger?.LogError(ex, "Error shutting down '{AppId}'", app.Id); }
        }
        _logger?.LogInformation("All apps shut down");
    }

    public IReadOnlyList<string> ListApps() => [.. _apps.Keys];

    public IApplication? GetApp(string appId) => _apps.GetValueOrDefault(appId);
}
