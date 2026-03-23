using System.Collections.Immutable;
using HPD.Agent;

namespace HPD.Sandbox.Local.State;

/// <summary>
/// Sandbox middleware state data.
/// </summary>
/// <remarks>
/// <para>Tracks functions blocked due to violations.</para>
/// <para>Stored in <c>MiddlewareState.Sandbox</c>.</para>
/// <para>
/// The <c>[MiddlewareState]</c> attribute triggers source generation of:
/// <list type="bullet">
/// <item><c>MiddlewareState.Sandbox</c> property</item>
/// <item><c>MiddlewareState.WithSandbox(SandboxStateData)</c> method</item>
/// </list>
/// </para>
/// </remarks>
[MiddlewareState]  // Required for source generator integration
public sealed record SandboxStateData
{
    /// <summary>
    /// Functions blocked from execution due to sandbox violations.
    /// </summary>
    public ImmutableHashSet<string> BlockedFunctions { get; init; } = [];

    /// <summary>
    /// Total violation count for this session.
    /// </summary>
    public int ViolationCount { get; init; } = 0;

    /// <summary>
    /// Adds a function to the blocked list.
    /// </summary>
    public SandboxStateData WithBlockedFunction(string functionName) =>
        this with
        {
            BlockedFunctions = BlockedFunctions.Add(functionName),
            ViolationCount = ViolationCount + 1
        };

    /// <summary>
    /// Resets the state (e.g., at start of new session).
    /// </summary>
    public SandboxStateData Reset() =>
        this with
        {
            BlockedFunctions = [],
            ViolationCount = 0
        };
}
