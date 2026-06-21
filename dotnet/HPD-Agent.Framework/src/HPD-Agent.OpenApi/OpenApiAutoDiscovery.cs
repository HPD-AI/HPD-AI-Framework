using System.Runtime.CompilerServices;

namespace HPD.Agent.OpenApi;

/// <summary>
/// Registers the OpenAPI loader hook into AgentBuilder via [ModuleInitializer].
/// Triggered automatically when HPD-Agent.OpenApi.dll is loaded.
/// Same pattern as provider modules and HPD-Agent.MCP.
/// </summary>
internal static class OpenApiAutoDiscovery
{
#pragma warning disable CA2255 // ModuleInitializer is intentionally used in library for auto-discovery
    [ModuleInitializer]
    public static void Initialize()
#pragma warning restore CA2255
    {
        AgentBuilder.RegisterOpenApiLoader(new OpenApiLoader());
    }
}
