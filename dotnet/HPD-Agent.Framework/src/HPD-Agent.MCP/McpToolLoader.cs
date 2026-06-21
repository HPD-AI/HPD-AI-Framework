using HPD.Agent;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using System.Text.Json;

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
        int maxFunctionNames,
        CancellationToken cancellationToken)
    {
        return ((MCPClientManager)manager).LoadToolsFromManifestAsync(
            manifestPath,
            enableCollapsing: false,
            maxFunctionNames,
            cancellationToken);
    }

    public Task<List<AIFunction>> LoadFromManifestContentAsync(
        object manager,
        string manifestContent,
        int maxFunctionNames,
        CancellationToken cancellationToken)
    {
        return ((MCPClientManager)manager).LoadToolsFromManifestContentAsync(
            manifestContent,
            enableCollapsing: false,
            maxFunctionNames,
            cancellationToken);
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
            serverConfig,
            maxFunctionNames,
            cancellationToken);
    }
}
