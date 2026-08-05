namespace HPD.Base;

/// <summary>Represents a runtime descriptor.</summary>
public sealed record RuntimeDescriptor
{
    /// <summary>Gets or sets the ID.</summary>
    public required string Id { get; init; }
    /// <summary>Gets or sets the name.</summary>
    public required string Name { get; init; }
    /// <summary>Gets or sets the instance ID.</summary>
    public string? InstanceId { get; init; }
    /// <summary>Gets or sets the environment.</summary>
    public string? Environment { get; init; }
    /// <summary>Gets or sets the base path.</summary>
    public string? BasePath { get; init; }
    /// <summary>Gets or sets the mode.</summary>
    public RuntimeMode Mode { get; init; }
}

/// <summary>Defines the runtime mode contract.</summary>
public enum RuntimeMode
{
    /// <summary>Identifies development.</summary>
Development,
    /// <summary>Identifies production.</summary>
Production,
    /// <summary>Identifies test.</summary>
Test,
    /// <summary>Identifies read only.</summary>
ReadOnly,
    /// <summary>Identifies custom.</summary>
Custom
}
