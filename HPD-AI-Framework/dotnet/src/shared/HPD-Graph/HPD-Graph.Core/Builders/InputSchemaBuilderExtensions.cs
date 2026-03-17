using HPDAgent.Graph.Abstractions.Validation;

namespace HPDAgent.Graph.Core.Builders;

/// <summary>
/// Fluent extensions for adding input validation schemas to nodes.
/// </summary>
public static class InputSchemaBuilderExtensions
{
    /// <summary>
    /// Add a required input with validation.
    /// </summary>
    public static NodeBuilder RequireInput<T>(
        this NodeBuilder builder,
        string inputName,
        IInputValidator? validator = null,
        string? description = null)
    {
        return builder.WithInputSchema(inputName, new InputSchema
        {
            Type = typeof(T),
            Required = true,
            Validator = validator,
            Description = description
        });
    }

    /// <summary>
    /// Add an optional input with default value.
    /// </summary>
    public static NodeBuilder OptionalInput<T>(
        this NodeBuilder builder,
        string inputName,
        T defaultValue,
        IInputValidator? validator = null,
        string? description = null)
    {
        return builder.WithInputSchema(inputName, new InputSchema
        {
            Type = typeof(T),
            Required = false,
            DefaultValue = defaultValue,
            Validator = validator,
            Description = description
        });
    }
}
