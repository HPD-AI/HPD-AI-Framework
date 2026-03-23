// <summary>
/// Factory for creating Skill objects with type-safe function references
/// Mirrors AIFunctionFactory.Create() pattern from Microsoft.Extensions.AI
/// </summary>
public static class SkillFactory
{
    /// <summary>
    /// Creates a skill with type-safe function/skill references and dual-context instructions.
    /// </summary>
    /// <param name="name">Skill name (REQUIRED - becomes AIFunction name)</param>
    /// <param name="description">Description shown before activation (REQUIRED - becomes AIFunction description shown to agent in tools list)</param>
    /// <param name="functionResult">Instructions returned as function result when activated (ephemeral, one-time)</param>
    /// <param name="systemPrompt">Instructions injected into system prompt (persistent, every iteration)</param>
    /// <param name="references">Function or skill references in "ToolkitName.FunctionName" format</param>
    /// <returns>Skill object processed by source generator</returns>
    public static Skill Create(
        string name,
        string description,
        string? functionResult = null,
        string? systemPrompt = null,
        params string[] references)
    {
        return Create(name, description, functionResult, systemPrompt, null, references);
    }

    /// <summary>
    /// Creates a skill with type-safe function/skill references, dual-context instructions, and options.
    /// </summary>
    /// <param name="name">Skill name (REQUIRED - becomes AIFunction name)</param>
    /// <param name="description">Description shown before activation (REQUIRED - becomes AIFunction description shown to agent in tools list)</param>
    /// <param name="functionResult">Instructions returned as function result when activated (ephemeral, one-time)</param>
    /// <param name="systemPrompt">Instructions injected into system prompt (persistent, every iteration)</param>
    /// <param name="options">Skill configuration options (Collapsing, documents, etc.)</param>
    /// <param name="references">Function or skill references in "ToolkitName.FunctionName" format</param>
    /// <returns>Skill object processed by source generator</returns>
    public static Skill Create(
        string name,
        string description,
        string? functionResult,
        string? systemPrompt,
        SkillOptions? options,
        params string[] references)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Skill name cannot be empty", nameof(name));

        if (string.IsNullOrWhiteSpace(description))
            throw new ArgumentException("Skill description cannot be empty", nameof(description));

        // At least one of FunctionResult or SystemPrompt should be provided for the skill to be useful
        if (string.IsNullOrWhiteSpace(functionResult) && string.IsNullOrWhiteSpace(systemPrompt))
            throw new ArgumentException(
                "At least one of FunctionResult or SystemPrompt must be provided. " +
                "FunctionResult is shown once when skill is activated. " +
                "SystemPrompt is injected into the system prompt persistently.",
                nameof(functionResult));

        return new Skill
        {
            Name = name,
            Description = description,
            FunctionResult = functionResult,
            SystemPrompt = systemPrompt,
            References = references ?? Array.Empty<string>(),
            Options = options ?? new SkillOptions()
        };
    }
}
