namespace HPD.Agent;

/// <summary>
/// Lightweight registration record for DI-required tools.
/// Only used for tools that cannot be instantiated via the AOT-compatible ToolRegistry
/// (e.g., tools requiring dependency injection like AgentPlanTools or DynamicMemoryTools).
///
/// For all other tools, use the generated ToolHarnessRegistry catalog via WithToolHarness&lt;T&gt;().
/// </summary>
/// <param name="Instance">The tool instance, pre-created and typically via DI.</param>
/// <param name="ToolTypeName">The tool type name for lookup in generated registration classes.</param>
internal sealed record ToolInstanceRegistration(
    object Instance,
    string ToolTypeName
);
