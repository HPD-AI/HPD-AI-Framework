using HPD.Agent;

/// <summary>
/// Marks a method as a skill with typed context for source generator detection.
/// Use this when you need context-aware dynamic descriptions or conditional evaluation.
/// </summary>
/// <typeparam name="TMetadata">The context type that implements IToolMetadata</typeparam>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false, Inherited = false)]
public sealed class SkillAttribute<TMetadata> : Attribute where TMetadata : IToolMetadata
{
    /// <summary>
    /// The context type used by this skill for compile-time validation.
    /// </summary>
    public Type ContextType => typeof(TMetadata);
}

/// <summary>
/// Marks a method as a skill for source generator detection.
/// Required for explicit intent and preventing false positives with helper methods.
/// Skills are automatically containers - they group related functions together.
/// Use Skill&lt;TMetadata&gt; if you need context-aware features.
/// </summary>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false, Inherited = false)]
public class SkillAttribute : Attribute
{
}
