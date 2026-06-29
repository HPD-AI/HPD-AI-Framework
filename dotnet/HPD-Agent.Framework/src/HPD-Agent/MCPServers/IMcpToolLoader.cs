using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using HPD.Events;

namespace HPD.Agent;

/// <summary>
/// Neutral MCP server source captured by generated toolharness registration code.
/// </summary>
public sealed record McpServerSource(
    string Name,
    string? Description,
    string ParentToolHarness,
    bool CollapseWithinToolHarness,
    string? FromManifest,
    string? ManifestServerName,
    bool? RequiresPermissionOverride,
    Func<object?, object?>? ConfigProvider);

/// <summary>
/// Bridge implemented by HPD-Agent.MCP and registered through a module initializer.
/// Keeps HPD-Agent core free of a hard MCP dependency while avoiding reflection.
/// </summary>
internal interface IMcpToolLoader
{
    object CreateManager(ILogger logger, object? options);

    Task<List<AIFunction>> LoadFromManifestAsync(
        object manager,
        string manifestPath,
        object? secretResolver,
        int maxFunctionNames,
        CancellationToken cancellationToken);

    Task<List<AIFunction>> LoadFromManifestContentAsync(
        object manager,
        string manifestContent,
        object? secretResolver,
        int maxFunctionNames,
        CancellationToken cancellationToken);

    Task<object?> LoadConfigFromManifestAsync(
        string manifestPath,
        string serverName,
        CancellationToken cancellationToken);

    Task<List<AIFunction>> LoadForToolHarnessAsync(
        object manager,
        object config,
        McpServerSource source,
        object? secretResolver,
        int maxFunctionNames,
        CancellationToken cancellationToken);

    IDisposable AttachLiveUpdates(object manager, IEventCoordinator eventCoordinator);
}
