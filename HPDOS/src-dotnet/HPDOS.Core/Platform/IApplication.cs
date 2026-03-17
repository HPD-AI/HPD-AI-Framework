using System.Text.Json;

namespace HPDOS.Core.Platform;

/// <summary>
/// Interface that all application modules must implement.
/// </summary>
public interface IApplication
{
    string Id { get; }
    string Name { get; }
    string Version { get; }

    ValueTask InitializeAsync(PlatformContext context, CancellationToken ct = default);
    ValueTask<JsonElement> HandleCommandAsync(string command, JsonElement payload, CancellationToken ct = default);
    ValueTask ShutdownAsync(CancellationToken ct = default) => ValueTask.CompletedTask;
}
