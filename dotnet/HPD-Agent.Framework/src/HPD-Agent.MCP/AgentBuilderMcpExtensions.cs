using HPD.Agent;
using System.Runtime.CompilerServices;

namespace HPD.Agent.MCP;

/// <summary>Registers MCP as an independently refreshed agent capability source.</summary>
public static class AgentBuilderMcpExtensions
{
    private static readonly ConditionalWeakTable<AgentBuilder, McpOptions> BuilderOptions = new();

    /// <summary>Configures final MCP policy for subsequently registered MCP sources.</summary>
    /// <param name="builder">The agent builder.</param>
    /// <param name="configure">The policy mutation.</param>
    /// <returns>The same builder.</returns>
    public static AgentBuilder WithMcpOptions(this AgentBuilder builder, Action<McpOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(configure);
        configure(BuilderOptions.GetValue(builder, static _ => new McpOptions()));
        return builder;
    }

    /// <summary>Registers an MCP manifest file as a required capability source.</summary>
    /// <param name="builder">The agent builder.</param>
    /// <param name="manifestPath">The path to the final MCP manifest.</param>
    /// <param name="configure">Optional final runtime-policy configuration.</param>
    /// <returns>The same builder.</returns>
    public static AgentBuilder WithMcp(
        this AgentBuilder builder,
        string manifestPath,
        Action<McpOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentException.ThrowIfNullOrWhiteSpace(manifestPath);
        var fullPath = Path.GetFullPath(manifestPath);
        var options = CreateOptions(builder, configure);
        builder.AddCapabilitySource(new AgentCapabilitySourceRegistration(
            new McpCapabilitySourceFactory(
                CapabilitySourceId.Create($"mcp.file:{fullPath}"),
                fullPath,
                null,
                options,
                builder.Logger,
                builder.SecretResolver),
            options.Catalog.LoadMode == McpCatalogLoadMode.Deferred
                ? CapabilitySourceInitialLoadPolicy.Deferred
                : CapabilitySourceInitialLoadPolicy.Required,
            CapabilitySourceRefreshFailurePolicy.RetainLastKnownGood));
        return builder;
    }

    /// <summary>Registers final MCP manifest JSON as a required capability source.</summary>
    /// <param name="builder">The agent builder.</param>
    /// <param name="manifestContent">The final manifest JSON.</param>
    /// <param name="sourceName">A stable application-defined source name.</param>
    /// <param name="configure">Optional final runtime-policy configuration.</param>
    /// <returns>The same builder.</returns>
    public static AgentBuilder WithMcpContent(
        this AgentBuilder builder,
        string manifestContent,
        string sourceName,
        Action<McpOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentException.ThrowIfNullOrWhiteSpace(manifestContent);
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceName);
        var options = CreateOptions(builder, configure);
        builder.AddCapabilitySource(new AgentCapabilitySourceRegistration(
            new McpCapabilitySourceFactory(
                CapabilitySourceId.Create($"mcp.content:{sourceName}"),
                null,
                manifestContent,
                options,
                builder.Logger,
                builder.SecretResolver),
            options.Catalog.LoadMode == McpCatalogLoadMode.Deferred
                ? CapabilitySourceInitialLoadPolicy.Deferred
                : CapabilitySourceInitialLoadPolicy.Required,
            CapabilitySourceRefreshFailurePolicy.RetainLastKnownGood));
        return builder;
    }

    /// <summary>Registers final MCP manifest JSON using a deterministic content identity.</summary>
    public static AgentBuilder WithMcpContent(
        this AgentBuilder builder,
        string manifestContent,
        Action<McpOptions>? configure = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(manifestContent);
        var digest = Convert.ToHexString(
            System.Security.Cryptography.SHA256.HashData(
                System.Text.Encoding.UTF8.GetBytes(manifestContent)))[..16];
        return builder.WithMcpContent(manifestContent, digest, configure);
    }

    private static McpOptions CreateOptions(AgentBuilder builder, Action<McpOptions>? configure)
    {
        var options = BuilderOptions.GetValue(builder, static _ => new McpOptions());
        configure?.Invoke(options);
        return options;
    }
}
