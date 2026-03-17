using System;

/// <summary>
/// Marks a function as conditionally available based on context properties.
/// The function will only be included in the Toolkit when the condition evaluates to true.
/// The context type is determined by the AIFunction&lt;TMetadata&gt; attribute on the method.
/// </summary>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
public sealed class ConditionalFunctionAttribute : Attribute
{
    /// <summary>
    /// The property expression that must evaluate to true for the function to be included.
    /// Examples: "HasTavilyProvider", "ProviderCount > 0", "IsEnabled && HasPermission"
    /// </summary>
    public string PropertyExpression { get; }
    
    /// <summary>
    /// Initializes a new instance of the ConditionalFunctionAttribute.
    /// </summary>
    /// <param name="propertyExpression">Property expression using the function's context type</param>
    public ConditionalFunctionAttribute(string propertyExpression)
    {
        PropertyExpression = propertyExpression ?? throw new ArgumentNullException(nameof(propertyExpression));
    }
}
