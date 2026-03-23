using System.Collections.Immutable;
using Microsoft.Extensions.AI;


namespace HPD.Agent;

/// <summary>
/// Crash recovery buffer for the current in-flight message turn.
/// Stores only the delta (messages generated during this turn) plus
/// the execution loop state needed to resume.
///
/// One per session. Overwritten after each tool batch and iteration. Deleted on turn completion.
/// Stateless, last-write-wins — if a new message turn starts, the previous
/// uncommitted turn is discarded and replaced.
/// </summary>
public sealed record UncommittedTurn
{
    /// <summary>
    /// Default branch ID used before multi-branch support exists.
    /// </summary>
    public const string DefaultBranch = "main";

    /// <summary>Session this turn belongs to.</summary>
    public required string SessionId { get; init; }

    /// <summary>
    /// Which branch this turn was executing on.
    /// Used on recovery to know which branch to load and append to.
    /// For now, always <see cref="DefaultBranch"/> until branching is implemented.
    /// </summary>
    public required string BranchId { get; init; }

    /// <summary>
    /// Messages generated during this turn only (the delta).
    /// Includes: user input message + all assistant responses + all tool calls/results.
    /// Does NOT include messages from previous turns (those are in the session).
    /// On recovery: Session.Messages + TurnMessages = full conversation state.
    /// </summary>
    public required IReadOnlyList<ChatMessage> TurnMessages { get; init; }

    /// <summary>Current iteration within this turn (0-based).</summary>
    public required int Iteration { get; init; }

    /// <summary>Functions that completed successfully during this turn.</summary>
    public required ImmutableHashSet<string> CompletedFunctions { get; init; }

    /// <summary>Middleware state at the time of the last save.</summary>
    public required MiddlewareState MiddlewareState { get; init; }

    /// <summary>Whether the agent loop has terminated.</summary>
    public required bool IsTerminated { get; init; }

    /// <summary>Reason for termination, if terminated.</summary>
    public string? TerminationReason { get; init; }

    /// <summary>When this uncommitted turn was first created.</summary>
    public required DateTime CreatedAt { get; init; }

    /// <summary>When this uncommitted turn was last overwritten.</summary>
    public required DateTime LastUpdatedAt { get; init; }

    /// <summary>Schema version for forward compatibility.</summary>
    public int Version { get; init; } = 1;
}
