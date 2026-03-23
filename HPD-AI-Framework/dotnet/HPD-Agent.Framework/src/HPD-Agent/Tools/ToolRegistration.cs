namespace HPD.Agent;

/// <summary>
/// Lightweight registration record for DI-required tools.
/// Only used for tools that cannot be instantiated via the AOT-compatible ToolRegistry
/// (e.g., tools requiring dependency injection like AgentPlanTools or DynamicMemoryTools).
///
/// For all other tools, use the generated ToolRegistry.All catalog via WithTools&lt;T&gt;().
/// </summary>
public record ToolInstanceRegistration(
    /// <summary>
    /// The tool instance (pre-created, typically via DI)
    /// </summary>
    object Instance,

    /// <summary>
    /// The tool type name (for lookup in generated Registration classes)
    /// </summary>
    string ToolTypeName,

    /// <summary>
    /// Optional function filter - if set, only these functions will be included.
    /// Phase 4.5: Used for selective function registration from skills.
    /// </summary>
    string[]? FunctionFilter = null
);
