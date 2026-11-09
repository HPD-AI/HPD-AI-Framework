using System.Text.Json.Serialization;
using HPD.Agent.Secrets;
using HPD.Serialization;
using Microsoft.Extensions.Logging;

namespace HPD.Agent.MCP;

/// <summary>References one external JSON MCP manifest from an agent configuration document.</summary>
public sealed record McpManifestReference
{
    /// <summary>Gets or sets the manifest path as authored.</summary>
    public string Path { get; init; } = string.Empty;

    /// <summary>Gets or sets an optional source alias used for diagnostics.</summary>
    public string? SourceName { get; init; }

    [JsonIgnore]
    internal string? SourcePath { get; set; }

    [JsonIgnore]
    internal IReadOnlyDictionary<string, HpdConfigSourceLocation>? SourceLocations { get; set; }
}

/// <summary>Declares inline MCP servers and external MCP manifests for one agent.</summary>
public sealed record AgentMcpConfig
{
    /// <summary>Gets or sets inline MCP server declarations.</summary>
    public List<McpServerConfig> Servers { get; set; } = [];

    /// <summary>Gets or sets external MCP manifest references.</summary>
    public List<McpManifestReference> Manifests { get; set; } = [];
}

/// <summary>Runtime-only manifest reference with its resolved absolute path.</summary>
internal sealed record ResolvedMcpManifestReference
{
    /// <summary>Gets the resolved absolute manifest path.</summary>
    public required string Path { get; init; }
    /// <summary>Gets the stable source alias used in diagnostics.</summary>
    public required string SourceName { get; init; }
    /// <summary>Gets the configuration field path that referenced the manifest.</summary>
    public string? SourcePath { get; init; }
    /// <summary>Gets the source map from the original configuration document.</summary>
    public IReadOnlyDictionary<string, HpdConfigSourceLocation>? SourceLocations { get; init; }
}

/// <summary>Runtime-only MCP configuration resolved from the agent source document.</summary>
internal sealed record ResolvedAgentMcpConfig
{
    /// <summary>Gets validated inline server declarations.</summary>
    public required IReadOnlyList<McpServerConfig> Servers { get; init; }
    /// <summary>Gets resolved external manifest references.</summary>
    public required IReadOnlyList<ResolvedMcpManifestReference> Manifests { get; init; }
    /// <summary>Gets the owning agent identity used for inline source identity.</summary>
    public required string AgentId { get; init; }
}

/// <summary>Runtime facilities available while an optional MCP package activates configured sources.</summary>
internal sealed class McpConfigurationActivationContext
{
    /// <summary>Gets the builder requesting MCP source registration.</summary>
    public required AgentBuilder Builder { get; init; }
    /// <summary>Gets application services available during source construction.</summary>
    public IServiceProvider? Services { get; init; }
    /// <summary>Gets the application logger factory.</summary>
    public ILoggerFactory? LoggerFactory { get; init; }
    /// <summary>Gets the application secret resolver.</summary>
    public ISecretResolver? SecretResolver { get; init; }
}

/// <summary>Creates capability-source registrations from portable AgentConfig MCP declarations.</summary>
internal interface IMcpConfigurationActivator
{
    /// <summary>Creates capability-source registrations for the resolved declarations.</summary>
    /// <param name="config">The validated MCP declarations and resolved manifest paths.</param>
    /// <param name="context">The builder facilities available during activation.</param>
    /// <returns>Source registrations in deterministic declaration order.</returns>
    IReadOnlyList<AgentCapabilitySourceRegistration> Create(
        ResolvedAgentMcpConfig config,
        McpConfigurationActivationContext context);
}

/// <summary>Registers the optional MCP implementation through generated application composition.</summary>
internal static class McpConfigurationActivation
{
    private static readonly object s_gate = new();
    private static IMcpConfigurationActivator? s_activator;

    /// <summary>Registers the single optional-package activator for this process.</summary>
    /// <param name="activator">The implementation supplied by <c>HPD-Agent.MCP</c>.</param>
    public static void Register(IMcpConfigurationActivator activator)
    {
        ArgumentNullException.ThrowIfNull(activator);
        lock (s_gate)
        {
            if (s_activator is not null && !ReferenceEquals(s_activator, activator))
                throw new InvalidOperationException("An MCP configuration activator is already registered.");
            s_activator = activator;
        }
    }

    internal static IMcpConfigurationActivator? Current
    {
        get { lock (s_gate) return s_activator; }
    }

    internal static void ClearForTesting()
    {
        lock (s_gate) s_activator = null;
    }
}

internal static class McpConfigurationDiagnostics
{
    internal static AgentRunConfigurationException At(
        AgentRunConfigurationException error,
        string path,
        IReadOnlyDictionary<string, HpdConfigSourceLocation>? locations)
    {
        HpdConfigSourceLocation? location = null;
        if (locations is not null)
        {
            var lookup = path;
            while (!locations.TryGetValue(lookup, out location))
            {
                var separator = lookup.LastIndexOf('.');
                if (separator < 0)
                    break;
                lookup = lookup[..separator];
            }
        }

        return new AgentRunConfigurationException(
            error.Code,
            path,
            error.Message,
            error.ProviderKey,
            error.ExpectedType,
            error.ActualType,
            location?.Line,
            location?.Column,
            error);
    }
}
