using System.Runtime.CompilerServices;
using System.ComponentModel;
using System.Text.Json;
using HPD.Agent.Secrets;
using Microsoft.Extensions.Logging;

namespace HPD.Agent.MCP;

/// <summary>Creates MCP capability sources from portable AgentConfig declarations.</summary>
internal sealed class McpConfigurationActivator : IMcpConfigurationActivator
{
    internal static McpConfigurationActivator Instance { get; } = new();

    /// <inheritdoc />
    public IReadOnlyList<AgentCapabilitySourceRegistration> Create(
        ResolvedAgentMcpConfig config,
        McpConfigurationActivationContext context)
    {
        ArgumentNullException.ThrowIfNull(config);
        ArgumentNullException.ThrowIfNull(context);
        var options = AgentBuilderMcpExtensions.CreateOptions(context.Builder, null);
        options.Validate();
        ValidateServerNames(config);
        var registrations = new List<AgentCapabilitySourceRegistration>(
            config.Manifests.Count + (config.Servers.Count > 0 ? 1 : 0));

        if (config.Servers.Count > 0)
        {
            registrations.Add(new AgentCapabilitySourceRegistration(
                McpCapabilitySourceFactory.FromServers(
                    $"mcp.inline:{config.AgentId}",
                    config.Servers,
                    options),
                options.Catalog.LoadMode == McpCatalogLoadMode.Deferred
                    ? CapabilitySourceInitialLoadPolicy.Deferred
                    : CapabilitySourceInitialLoadPolicy.Required,
                CapabilitySourceRefreshFailurePolicy.RetainLastKnownGood));
        }

        foreach (var manifest in config.Manifests)
        {
            registrations.Add(AgentBuilderMcpExtensions.CreateManifestRegistration(
                manifest.Path,
                options,
                context.LoggerFactory,
                context.SecretResolver));
        }

        return registrations;
    }

    private static void ValidateServerNames(ResolvedAgentMcpConfig config)
    {
        var names = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var server in config.Servers.Where(static server => server.Enabled))
            names.Add(server.Name, server.SourcePath ?? $"inline server '{server.Name}'");

        foreach (var reference in config.Manifests)
        {
            McpManifest manifest;
            try
            {
                manifest = JsonSerializer.Deserialize(
                    File.ReadAllText(reference.Path),
                    McpJsonSerializerContext.Default.McpManifest)
                    ?? throw new JsonException("The MCP manifest is empty.");
                manifest.Validate();
            }
            catch (McpPermissionConfigurationException exception)
            {
                var manifestPath = $"{reference.SourcePath ?? "mcp.manifests"}.{exception.Path}";
                throw McpConfigurationDiagnostics.At(
                    new AgentRunConfigurationException(
                        exception.Code,
                        manifestPath,
                        exception.Message,
                        innerException: exception),
                    manifestPath,
                    reference.SourceLocations);
            }
            catch (Exception exception) when (
                exception is IOException or UnauthorizedAccessException)
            {
                throw McpConfigurationDiagnostics.At(
                    new AgentRunConfigurationException(
                        "McpManifestUnreadable",
                        (reference.SourcePath ?? "mcp.manifests") + ".path",
                        $"MCP manifest '{reference.Path}' could not be read: {exception.Message}",
                        innerException: exception),
                    (reference.SourcePath ?? "mcp.manifests") + ".path",
                    reference.SourceLocations);
            }
            catch (Exception exception) when (
                exception is JsonException or ArgumentException or InvalidOperationException)
            {
                throw McpConfigurationDiagnostics.At(
                    new AgentRunConfigurationException(
                        "McpManifestInvalid",
                        reference.SourcePath ?? "mcp.manifests",
                        $"MCP manifest '{reference.Path}' is invalid: {exception.Message}",
                        innerException: exception),
                    reference.SourcePath ?? "mcp.manifests",
                    reference.SourceLocations);
            }

            foreach (var server in manifest.Servers.Where(static server => server.Enabled))
            {
                if (names.TryGetValue(server.Name, out var existingSource))
                {
                    throw McpConfigurationDiagnostics.At(
                        new AgentRunConfigurationException(
                            "McpServerNameConflict",
                            (reference.SourcePath ?? "mcp.manifests") + ".servers",
                            $"MCP server '{server.Name}' is declared by both {existingSource} and " +
                            $"manifest '{reference.SourceName}' ('{reference.Path}')."),
                        (reference.SourcePath ?? "mcp.manifests") + ".servers",
                        reference.SourceLocations);
                }
                names.Add(
                    server.Name,
                    $"manifest '{reference.SourceName}' ('{reference.Path}')");
            }
        }
    }
}

internal static class McpConfigurationComposition
{
#pragma warning disable CA2255
    [ModuleInitializer]
    public static void Initialize()
#pragma warning restore CA2255
        => McpConfigurationPackage.Register();
}

/// <summary>
/// Source-generated application composition entry point for MCP AgentConfig activation.
/// Application code should not call this type directly.
/// </summary>
[EditorBrowsable(EditorBrowsableState.Never)]
public static class McpConfigurationPackage
{
    /// <summary>Registers the MCP package activator with core agent construction.</summary>
    public static void Register()
        => McpConfigurationActivation.Register(McpConfigurationActivator.Instance);
}
