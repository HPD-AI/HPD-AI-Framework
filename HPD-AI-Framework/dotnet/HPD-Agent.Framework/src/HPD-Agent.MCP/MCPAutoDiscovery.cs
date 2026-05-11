using HPD.Agent;
using System.Runtime.CompilerServices;

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
    public static void Initialize()
    {
        lock (_lock)
        {
            if (_initialized) return;

            AgentBuilder.RegisterMcpToolLoader(new McpToolLoader());

            _initialized = true;
        }
    }
}
