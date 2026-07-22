using HPD.Agent;
using HPD.Agent.Middleware;

/// <summary>Provides skill instructions for a specific invocation context.</summary>
/// <param name="context">The current skill instruction context.</param>
/// <param name="cancellationToken">A token that cancels instruction resolution.</param>
/// <returns>The resolved instruction text.</returns>
public delegate ValueTask<string> SkillInstructionProvider(
    SkillInstructionContext context,
    CancellationToken cancellationToken);

/// <summary>Context supplied while resolving skill instructions.</summary>
/// <param name="FunctionContext">The current function execution context.</param>
/// <param name="Services">The invocation service provider, when available.</param>
/// <param name="ContentStore">The content store available to the skill, when configured.</param>
public sealed record SkillInstructionContext(
    FunctionExecutionContext FunctionContext,
    IServiceProvider? Services,
    IContentStore? ContentStore);

/// <summary>Creates common instruction providers without runtime reflection.</summary>
public static class SkillInstructions
{
    /// <summary>Creates a provider that always returns the supplied text.</summary>
    /// <param name="text">The non-empty instruction text.</param>
    /// <returns>A static instruction provider.</returns>
    public static SkillInstructionProvider FromText(string text)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(text);
        return (_, _) => ValueTask.FromResult(text);
    }
}
