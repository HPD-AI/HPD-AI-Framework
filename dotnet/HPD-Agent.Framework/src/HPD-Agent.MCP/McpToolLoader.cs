using HPD.Agent;
using HPD.Agent.Secrets;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using System.Text.Json;
using HPD.Events;

namespace HPD.Agent.MCP;

internal sealed class McpToolLoader : IMcpToolLoader
{
    public object CreateManager(ILogger logger, object? options)
    {
        return new MCPClientManager(logger, options as MCPOptions);
    }

    public Task<List<AIFunction>> LoadFromManifestAsync(
        object manager,
        string manifestPath,
        object? secretResolver,
        int maxFunctionNames,
        CancellationToken cancellationToken)
    {
        return ((MCPClientManager)manager).LoadToolsFromManifestAsync(
            manifestPath: manifestPath,
            enableCollapsing: false,
            maxFunctionNamesInDescription: maxFunctionNames,
            secretResolver: secretResolver as ISecretResolver,
            cancellationToken: cancellationToken);
    }

    public Task<List<AIFunction>> LoadFromManifestContentAsync(
        object manager,
        string manifestContent,
        object? secretResolver,
        int maxFunctionNames,
        CancellationToken cancellationToken)
    {
        return ((MCPClientManager)manager).LoadToolsFromManifestContentAsync(
            manifestContent: manifestContent,
            enableCollapsing: false,
            maxFunctionNamesInDescription: maxFunctionNames,
            secretResolver: secretResolver as ISecretResolver,
            cancellationToken: cancellationToken);
    }

    public async Task<object?> LoadConfigFromManifestAsync(
        string manifestPath,
        string serverName,
        CancellationToken cancellationToken)
    {
        if (!File.Exists(manifestPath))
            return null;

        var json = await File.ReadAllTextAsync(manifestPath, cancellationToken).ConfigureAwait(false);
        var manifest = JsonSerializer.Deserialize(json, MCPJsonSerializerContext.Default.MCPManifest);

        return manifest?.Servers.FirstOrDefault(server =>
            string.Equals(server.Name, serverName, StringComparison.OrdinalIgnoreCase));
    }

    public Task<List<AIFunction>> LoadForToolHarnessAsync(
        object manager,
        object config,
        McpServerSource source,
        object? secretResolver,
        int maxFunctionNames,
        CancellationToken cancellationToken)
    {
        var serverConfig = (MCPServerConfig)config;
        serverConfig.ParentToolHarness = source.ParentToolHarness;
        serverConfig.CollapseWithinToolHarness = source.CollapseWithinToolHarness;

        if (source.RequiresPermissionOverride.HasValue)
            serverConfig.RequiresPermission = source.RequiresPermissionOverride.Value;

        if (!string.IsNullOrWhiteSpace(source.Description) && string.IsNullOrWhiteSpace(serverConfig.Description))
            serverConfig.Description = source.Description;

        return ((MCPClientManager)manager).LoadToolsForToolHarnessAsync(
            config: serverConfig,
            maxFunctionNamesInDescription: maxFunctionNames,
            secretResolver: secretResolver as ISecretResolver,
            cancellationToken: cancellationToken);
    }

    public IDisposable AttachLiveUpdates(object manager, IEventCoordinator eventCoordinator)
    {
        return ((MCPClientManager)manager).AttachLiveUpdates(eventCoordinator);
    }
}
