using System.Collections.Immutable;
using HPD.Agent;

namespace HPDOS.Toolkits.Middleware;

/// <summary>
/// Middleware state that tracks directories touched by ReadFile in the current turn.
/// Consumed by <see cref="FileContextInjectionMiddleware"/> at the start of the next turn
/// to inject any CONTEXT.md files found in those directories.
/// </summary>
[MiddlewareState(Version = 1)]
public sealed record FileAccessState
{
    /// <summary>
    /// Absolute paths of directories the agent accessed via ReadFile in the previous turn.
    /// Cleared after injection in BeforeMessageTurnAsync.
    /// </summary>
    public ImmutableHashSet<string> AccessedDirectories { get; init; } = ImmutableHashSet<string>.Empty;
}
