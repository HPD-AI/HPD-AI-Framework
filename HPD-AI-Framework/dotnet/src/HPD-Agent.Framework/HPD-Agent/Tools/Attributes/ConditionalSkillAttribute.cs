using System;

/// <summary>
/// Marks a skill as conditionally available based on metadata properties.
/// The skill will only be included in the Toolkit when the condition evaluates to true.
/// The metadata type is determined by the Skill&lt;TMetadata&gt; attribute on the method.
/// </summary>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
public sealed class ConditionalSkillAttribute : Attribute
{
    /// <summary>
    /// The property expression that must evaluate to true for the skill to be included.
    /// Examples: "HasAdvancedFeatures", "FeatureCount > 0", "IsEnabled && HasPermission"
    /// </summary>
    public string PropertyExpression { get; }

    /// <summary>
    /// Initializes a new instance of the ConditionalSkillAttribute.
    /// </summary>
    /// <param name="propertyExpression">Property expression using the skill's metadata type</param>
    public ConditionalSkillAttribute(string propertyExpression)
    {
        PropertyExpression = propertyExpression ?? throw new ArgumentNullException(nameof(propertyExpression));
    }
}
