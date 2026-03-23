using HPD.Agent;
using HPD.Agent.Middleware;
using HPD.Agent.Tests.Middleware.V2;
using Microsoft.Extensions.AI;

namespace HPD.Agent.Tests.Filters;

/// <summary>
/// Helper methods for creating test middleware contexts (V2).
/// Delegates to shared V2 test helpers.
/// </summary>
internal static class MiddlewareTestHelpers
{
    /// <summary>
    /// Creates a basic agent middleware context for testing (V2).
    /// Returns BeforeIterationContext since that's the most common test scenario.
    /// </summary>
    public static BeforeIterationContext CreateContext(int iteration = 0)
    {
        var state = AgentLoopState.InitialSafe(
            new List<ChatMessage>(),
            "test-run",
            "test-conv-id",
            "TestAgent") with { Iteration = iteration };

        return HPD.Agent.Tests.Middleware.V2.MiddlewareTestHelpers.CreateBeforeIterationContext(
            iteration: iteration,
            state: state);
    }
}
