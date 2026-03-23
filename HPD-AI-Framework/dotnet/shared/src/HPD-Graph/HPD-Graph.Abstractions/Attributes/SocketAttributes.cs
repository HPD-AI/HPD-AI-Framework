namespace HPDAgent.Graph.Abstractions.Attributes;

/// <summary>
/// Declares an input socket on a method parameter.
/// Source generator extracts value from HandlerInputs at compile-time.
/// </summary>
[AttributeUsage(AttributeTargets.Parameter)]
public sealed class InputSocketAttribute : Attribute
{
    /// <summary>
    /// Whether this input is optional (has a default value).
    /// </summary>
    public bool Optional { get; set; }

    /// <summary>
    /// Human-readable description of what this input represents.
    /// </summary>
    public string? Description { get; set; }
}

/// <summary>
/// Declares an output socket on a property of the return type.
/// Source generator writes value to NodeExecutionResult at compile-time.
/// </summary>
[AttributeUsage(AttributeTargets.Property)]
public sealed class OutputSocketAttribute : Attribute
{
    /// <summary>
    /// Human-readable description of what this output represents.
    /// </summary>
    public string? Description { get; set; }
}

/// <summary>
/// Marks a handler class for socket generation.
/// Triggers source generator to create HandlerInputs bridge code.
/// </summary>
[AttributeUsage(AttributeTargets.Class)]
public sealed class GraphNodeHandlerAttribute : Attribute
{
    /// <summary>
    /// The unique name for this handler node.
    /// Used in graph definitions to reference this handler.
    /// </summary>
    public string? NodeName { get; set; }
}
