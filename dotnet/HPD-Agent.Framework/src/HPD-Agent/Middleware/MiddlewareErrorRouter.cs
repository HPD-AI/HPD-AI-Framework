using System.Runtime.CompilerServices;

namespace HPD.Agent.Middleware;

/// <summary>
/// Dispatches terminal middleware errors once per exception instance.
/// </summary>
internal static class MiddlewareErrorRouter
{
    private static readonly ConditionalWeakTable<Exception, ErrorObservation> Observations = new();

    public static async ValueTask ReportAsync(
        AgentContext context,
        AgentMiddlewarePipeline pipeline,
        Exception exception,
        ErrorSource source,
        CancellationToken cancellationToken,
        AgentRunConfig? runConfig = null)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(pipeline);
        ArgumentNullException.ThrowIfNull(exception);

        if (exception is OperationCanceledException && cancellationToken.IsCancellationRequested)
            return;

        if (!Observations.TryAdd(exception, ErrorObservation.Instance))
            return;

        await pipeline.ExecuteOnErrorAsync(
            context.AsError(
                exception,
                source,
                context.State.Iteration,
                runConfig),
            cancellationToken).ConfigureAwait(false);
    }

    private sealed class ErrorObservation
    {
        public static ErrorObservation Instance { get; } = new();
    }
}
