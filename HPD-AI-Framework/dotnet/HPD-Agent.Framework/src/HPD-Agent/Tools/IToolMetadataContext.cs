namespace HPD.Agent;

/// <summary>
/// Base interface that any tool context must implement.
/// Provides dynamic property access for conditional functions and descriptions.
/// </summary>
public interface IToolMetadata
{
    /// <summary>
    /// Gets a property value by name with optional default
    /// </summary>
    T? GetProperty<T>(string propertyName, T? defaultValue = default);

    /// <summary>
    /// Checks if a property exists
    /// </summary>
    bool HasProperty(string propertyName);

    /// <summary>
    /// Gets all available property names (for DSL validation)
    /// </summary>
    IEnumerable<string> GetPropertyNames();
}
