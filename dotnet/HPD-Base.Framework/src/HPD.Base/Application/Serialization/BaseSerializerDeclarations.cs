using System.ComponentModel;

namespace HPD.Base;

/// <summary>Infrastructure declaration emitted for one reachable serializer property.</summary>
[EditorBrowsable(EditorBrowsableState.Never)]
public sealed record BaseSerializerPropertyDeclaration
{
    /// <summary>Gets the declaring DTO type.</summary>
    internal Type DeclaringType { get; init; } = null!;
    /// <summary>Gets the exact application property identity.</summary>
    public required string ApplicationName { get; init; }
    /// <summary>Gets the declared property type.</summary>
    internal Type PropertyType { get; init; } = null!;
    /// <summary>Gets an explicit serializer name, or null when the naming policy owns it.</summary>
    public string? ExplicitWireName { get; init; }
    /// <summary>Gets whether the property is required.</summary>
    public bool Required { get; init; }
    /// <summary>Gets whether the getter may return null.</summary>
    public bool Nullable { get; init; }
    /// <summary>Gets the closed stable converter contract identity.</summary>
    public string ConverterIdentity { get; init; } = "stj-built-in";
    /// <summary>Gets the exact explicit converter type, or null for a framework built-in.</summary>
    internal Type? ConverterType { get; init; }

    /// <summary>Creates one generated serializer declaration.</summary>
    [EditorBrowsable(EditorBrowsableState.Never)]
    public static BaseSerializerPropertyDeclaration Create(
        Type declaringType, string applicationName, Type propertyType, string? explicitWireName,
        bool required, bool nullable, string converterIdentity, Type? converterType) => new()
    {
        DeclaringType = declaringType,
        ApplicationName = applicationName,
        PropertyType = propertyType,
        ExplicitWireName = explicitWireName,
        Required = required,
        Nullable = nullable,
        ConverterIdentity = converterIdentity,
        ConverterType = converterType,
    };
}
