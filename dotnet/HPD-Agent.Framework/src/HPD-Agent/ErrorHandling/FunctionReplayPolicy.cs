using Microsoft.Extensions.AI;

namespace HPD.Agent.ErrorHandling;

/// <summary>Resolves the immutable replay policy captured for one admitted function invocation.</summary>
public static class FunctionReplayPolicy
{
    /// <summary>
    /// Resolves function and action replay metadata. Unknown and external functions default to
    /// <see cref="ReplayPolicy.Never"/>.
    /// </summary>
    /// <param name="function">The admitted HPD or external function.</param>
    /// <param name="action">The validated action discriminator, when present.</param>
    /// <returns>The effective replay policy.</returns>
    public static ReplayPolicy Resolve(AIFunction function, string? action)
    {
        ArgumentNullException.ThrowIfNull(function);
        if (function is not HPDAIFunctionFactory.HPDAIFunction hpdFunction)
            return ReplayPolicy.Never;

        if (!string.IsNullOrWhiteSpace(action) &&
            hpdFunction.OperationContract?.Actions.TryGetValue(action, out var actionPolicy) == true)
        {
            return actionPolicy.Replay;
        }

        return hpdFunction.Replay;
    }

    /// <summary>
    /// Returns whether an exact harness, function, or action selector excludes the invocation.
    /// </summary>
    /// <param name="request">The function request being checked.</param>
    /// <param name="exclusions">Configured exact replay exclusions.</param>
    /// <returns><see langword="true"/> when automatic replay is excluded.</returns>
    public static bool IsExcluded(
        HPD.Agent.Middleware.FunctionRequest request,
        RetryExclusionConfig exclusions)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(exclusions);

        if (request.ToolHarnessName is { } harness &&
            exclusions.Harnesses?.Contains(harness, StringComparer.Ordinal) == true)
        {
            return true;
        }

        if (exclusions.Functions?.Contains(request.FunctionName, StringComparer.Ordinal) == true)
            return true;

        var action = request.InvocationMode?.Action;
        return action is not null &&
            exclusions.Actions?.Any(selector =>
                string.Equals(selector.Function, request.FunctionName, StringComparison.Ordinal) &&
                string.Equals(selector.Action, action, StringComparison.Ordinal)) == true;
    }
}
