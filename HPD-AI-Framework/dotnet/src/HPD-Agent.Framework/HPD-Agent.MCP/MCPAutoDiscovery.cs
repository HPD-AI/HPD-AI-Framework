using System.Runtime.CompilerServices;
using System.Diagnostics.CodeAnalysis;
using HPD.Agent;

namespace HPD.Agent.MCP;

/// <summary>
/// Auto-initializes MCP integration when HPD-Agent.MCP library is loaded.
/// This ModuleInitializer runs automatically in both JIT and AOT scenarios.
/// Registers MCP tools loading capability with the agent builder system.
/// </summary>
internal static class MCPAutoDiscovery
{
    private static bool _initialized = false;
    private static readonly object _lock = new();

    /// <summary>
    /// Module initializer that runs when HPD-Agent.MCP assembly is first loaded.
    /// Ensures MCP integration is available to AgentBuilder when needed.
    /// </summary>
#pragma warning disable CA2255 // The 'ModuleInitializer' attribute should not be used in libraries
    [ModuleInitializer]
#pragma warning restore CA2255 // The 'ModuleInitializer' attribute should not be used in libraries
    [DynamicDependency(DynamicallyAccessedMemberTypes.All, typeof(MCPClientManager))]
    [DynamicDependency(DynamicallyAccessedMemberTypes.All, typeof(MCPManifest))]
    [DynamicDependency(DynamicallyAccessedMemberTypes.All, typeof(MCPServerConfig))]
    [DynamicDependency(DynamicallyAccessedMemberTypes.All, typeof(MCPOptions))]
    [DynamicDependency(DynamicallyAccessedMemberTypes.All, typeof(AgentBuilderMcpExtensions))]
    public static void Initialize()
    {
        lock (_lock)
        {
            if (_initialized) return;

            // MCP module is now loaded and available
            // The AgentBuilderMcpExtensions.WithMCP() methods are now callable
            // via using HPD.Agent.MCP; in client code

            _initialized = true;
        }
    }
}
