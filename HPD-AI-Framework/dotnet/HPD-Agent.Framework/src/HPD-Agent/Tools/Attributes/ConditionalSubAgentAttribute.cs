using System;

/// <summary>
/// Marks a sub-agent as conditionally available based on metadata properties.
/// The sub-agent will only be included in the Toolkit when the condition evaluates to true.
/// The metadata type is determined by the SubAgent&lt;TMetadata&gt; attribute on the method.
/// </summary>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
public sealed class ConditionalSubAgentAttribute : Attribute
{
    /// <summary>
    /// The property expression that must evaluate to true for the sub-agent to be included.
    /// Examples: "HasDelegationEnabled", "AgentCount > 0", "IsEnabled && HasPermission"
    /// </summary>
    public string PropertyExpression { get; }

    /// <summary>
    /// Initializes a new instance of the ConditionalSubAgentAttribute.
    /// </summary>
    /// <param name="propertyExpression">Property expression using the sub-agent's metadata type</param>
    public ConditionalSubAgentAttribute(string propertyExpression)
    {
        PropertyExpression = propertyExpression ?? throw new ArgumentNullException(nameof(propertyExpression));
    }
}
