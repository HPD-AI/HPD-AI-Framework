using HPD.Agent.Middleware;
using Microsoft.Extensions.AI;

namespace HPD.Agent;

/// <summary>
/// Requests user permission to continue execution when the maximum iteration limit is reached.
/// </summary>
/// <remarks>
/// <para><b>STATELESS MIDDLEWARE:</b></para>
/// <para>
/// This middleware is stateless - all state flows through the context via
/// <see cref="ContinuationPermissionStateData"/>. This preserves Agent's thread-safety
/// guarantee for concurrent RunAsync() calls.
/// </para>
///
/// <para><b>Behavior:</b></para>
/// <para>
/// This middleware runs BEFORE each LLM call and checks if we've reached the iteration limit.
/// If the limit is reached, it emits a continuation request event and waits for user response.
/// </para>
///
/// <para>Unlike function-level permission, this middleware:</para>
/// <list type="bullet">
/// <item>Runs before EVERY LLM call (not just tool invocations)</item>
/// <item>Checks iteration count rather than function-specific permissions</item>
/// <item>Can terminate the agent loop before the LLM call happens</item>
/// </list>
/// </remarks>
public class ContinuationPermissionMiddleware : IAgentMiddleware
{
    //
    // CONFIGURATION (not state - set at registration time)
    //

    private readonly int _maxIterations;
    private readonly int _extensionAmount;
    private readonly string _middlewareName;

    /// <summary>
    /// Creates a new continuation permission middleware.
    /// </summary>
    /// <param name="maxIterations">Base maximum iterations before requiring permission</param>
    /// <param name="extensionAmount">How many iterations to add when user approves continuation</param>
    /// <param name="middlewareName">Name for this middleware instance (for event correlation)</param>
    public ContinuationPermissionMiddleware(
        int maxIterations = 20,
        int extensionAmount = 3,
        string? middlewareName = null)
    {
        _maxIterations = maxIterations;
        _extensionAmount = extensionAmount;
        _middlewareName = middlewareName ?? "ContinuationPermissionMiddleware";
    }

    //     
    // HOOKS
    //     

    /// <summary>
    /// Called BEFORE the LLM call begins.
    /// Checks if iteration limit reached and requests continuation permission if needed.
    /// </summary>
    public async Task BeforeIterationAsync(
        BeforeIterationContext context,
        CancellationToken cancellationToken)
    {
        // Get or initialize the current extended limit from state
        var permState = context.Analyze(s =>
            s.MiddlewareState.ContinuationPermission() ?? new()
        );

        // Initialize state with configured max iterations if this is the first check
        if (permState.CurrentExtendedLimit == 20 && _maxIterations != 20)
        {
            // Update state to use the configured limit
            var newState = ContinuationPermissionStateData.WithInitialLimit(_maxIterations);
            context.UpdateState(s => s with
            {
                MiddlewareState = s.MiddlewareState.WithContinuationPermission(newState)
            });
            permState = context.Analyze(s => s.MiddlewareState.ContinuationPermission() ?? new());
        }

        // Check if we've EXCEEDED the iteration limit
        // Use > because iterations are 0-based: limit=2 means iterations 0,1 allowed; 2 exceeds it
        if (context.Iteration > permState.CurrentExtendedLimit - 1)
        {
            // Request continuation permission
            var shouldContinue = await RequestContinuationPermissionAsync(context, permState, cancellationToken)
                .ConfigureAwait(false);

            if (!shouldContinue)
            {
                // User denied continuation - terminate execution
                context.SkipLLMCall = true;

                // Provide a final response explaining termination
                context.OverrideResponse = new ChatMessage(
                    ChatRole.Assistant,
                    "Execution terminated: Maximum iteration limit reached and continuation was not approved.");

                // V2 NOTE: Termination signaled via state update instead of context.Properties
                context.UpdateState(s => s with
                {
                    IsTerminated = true,
                    TerminationReason = "Continuation permission denied at iteration limit"
                });
            }
            // If approved, execution continues normally
        }
    }

    //     
    // HELPERS
    //     

    /// <summary>
    /// Requests continuation permission via bidirectional events.
    /// </summary>
    /// <returns>True if user approves continuation, false otherwise</returns>
    private async Task<bool> RequestContinuationPermissionAsync(
        BeforeIterationContext context,
        ContinuationPermissionStateData currentState,
        CancellationToken cancellationToken)
    {
        var continuationId = Guid.NewGuid().ToString();

        try
        {
            var evt = new ContinuationRequestEvent(
                continuationId,
                _middlewareName,
                context.Iteration + 1,  // Display as 1-based for user
                currentState.CurrentExtendedLimit);

            // Emit continuation request event
            context.Emit(evt);

            // Wait for response from external handler (BLOCKS during user interaction)
            var response = await context.Base.WaitForResponseAsync<ContinuationResponseEvent>(continuationId)
                .ConfigureAwait(false);

            if (response.Approved)
            {
                // Extend the limit for future iterations via state update
                var extension = response.ExtensionAmount > 0
                    ? response.ExtensionAmount
                    : _extensionAmount;

                var newState = currentState.ExtendLimit(extension);
                context.UpdateState(s => s with
                {
                    MiddlewareState = s.MiddlewareState.WithContinuationPermission(newState)
                });
                return true;
            }

            return false;
        }
        catch (TimeoutException)
        {
            // Timeout - default to deny
            return false;
        }
        catch (OperationCanceledException)
        {
            // Cancelled - default to deny
            return false;
        }
        catch (InvalidOperationException)
        {
            // EventCoordinator not configured
            return false;
        }
        catch
        {
            // Catch any other exception
            return false;
        }
    }
}
