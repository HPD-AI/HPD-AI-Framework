using System;

/// <summary>
/// Marks a parameter as conditionally visible to the AI based on context properties.
/// The parameter will only appear in the function schema when the condition evaluates to true.
/// The context type is determined by the AIFunction&lt;TMetadata&gt; attribute on the method.
/// </summary>
[AttributeUsage(AttributeTargets.Parameter, AllowMultiple = false)]
public sealed class ConditionalParameterAttribute : Attribute
{
    /// <summary>
    /// The property expression that must evaluate to true for the parameter to be visible.
    /// Examples: "NumberOfProviders > 1", "HasAdvancedFeatures", "ProviderCount >= 2 && IsEnabled"
    /// </summary>
    public string PropertyExpression { get; }
    
    /// <summary>
    /// Initializes a new instance of the ConditionalParameterAttribute.
    /// </summary>
    /// <param name="propertyExpression">Property expression using the function's context type</param>
    public ConditionalParameterAttribute(string propertyExpression)
    {
        PropertyExpression = propertyExpression ?? throw new ArgumentNullException(nameof(propertyExpression));
    }
}
