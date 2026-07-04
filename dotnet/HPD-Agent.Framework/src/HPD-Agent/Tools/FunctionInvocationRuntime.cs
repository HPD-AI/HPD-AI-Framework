using System.ComponentModel;
using System.Text.Json;
using HPD.Agent.Middleware;
using Microsoft.Extensions.AI;

namespace HPD.Agent;

/// <summary>
/// Runtime services for invoking ordinary AI functions synchronously or as background work.
/// </summary>
[EditorBrowsable(EditorBrowsableState.Never)]
internal static class FunctionInvocationRuntime
{
    /// <summary>
    /// Describes a single function invocation.
    /// </summary>
    internal sealed record FunctionInvocationRequest
    {
        /// <summary>
        /// Gets the model-facing function name.
        /// </summary>
        public required string Name { get; init; }

        /// <summary>
        /// Gets the model-provided tool arguments.
        /// </summary>
        public required AIFunctionArguments Arguments { get; init; }

        /// <summary>
        /// Gets the parent function execution context.
        /// </summary>
        public required FunctionExecutionContext ParentContext { get; init; }

        /// <summary>
        /// Gets the invocation mode policy for this function.
        /// </summary>
        public AgentInvocationModePolicy InvocationModePolicy { get; init; } =
            AgentInvocationModePolicy.SynchronousOnly;

        /// <summary>
        /// Gets the background notification rule for this function.
        /// </summary>
        public BackgroundTaskNotificationRule BackgroundNotification { get; init; } =
            new BackgroundTaskNotificationRule.OnFinalStateRule(Completed: true, Faulted: true);

        /// <summary>
        /// Invokes the underlying function body synchronously.
        /// </summary>
        public required Func<AIFunctionArguments, FunctionExecutionContext, CancellationToken, Task<object?>> InvokeFunctionAsync { get; init; }
    }

    /// <summary>
    /// Invokes a function synchronously or as runtime-owned background work.
    /// </summary>
    /// <param name="request">The function invocation request.</param>
    /// <param name="cancellationToken">A token that cancels synchronous invocation.</param>
    /// <returns>The model-facing invocation result.</returns>
    internal static async Task<AgentInvocationResult> InvokeAsync(
        FunctionInvocationRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.Name);
        ArgumentNullException.ThrowIfNull(request.Arguments);
        ArgumentNullException.ThrowIfNull(request.ParentContext);
        ArgumentNullException.ThrowIfNull(request.InvokeFunctionAsync);

        var sanitizedArguments = AgentInvocationModes.CreateSanitizedArguments(
            request.Arguments,
            out var requestedMode);
        AgentInvocationMode mode;
        try
        {
            mode = AgentInvocationModes.Resolve(
                request.InvocationModePolicy,
                requestedMode);
        }
        catch (InvalidOperationException ex)
        {
            return AgentInvocationModes.CreateReceiptResult(
                request.Name,
                BackgroundTaskSourceKind.Function,
                ex.Message,
                "invalid_invocation_mode");
        }

        if (mode == AgentInvocationMode.Background)
            return RegisterBackgroundInvocation(request, sanitizedArguments);

        var result = await request.InvokeFunctionAsync(
            sanitizedArguments,
            request.ParentContext,
            cancellationToken).ConfigureAwait(false);

        return new AgentInvocationResult
        {
            Mode = AgentInvocationMode.Synchronous,
            Text = ToolResultText.FromResult(result),
            ToolResult = result,
            Background = null
        };
    }

    private static AgentInvocationResult RegisterBackgroundInvocation(
        FunctionInvocationRequest request,
        AIFunctionArguments sanitizedArguments)
    {
        var parentContext = request.ParentContext;
        if (!parentContext.CanRegisterBackgroundTasks)
        {
            return AgentInvocationModes.CreateReceiptResult(
                request.Name,
                BackgroundTaskSourceKind.Function,
                "Background invocation requires an active agent runtime.");
        }

        var registration = parentContext.RegisterBackgroundTask(
            new BackgroundTaskDescriptor
            {
                Name = request.Name,
                SourceKind = BackgroundTaskSourceKind.Function,
                SourceId = parentContext.FunctionCallId,
                SessionId = parentContext.SessionId,
                ThreadId = parentContext.ThreadId,
                Invocation = parentContext.InvocationSnapshot,
                Notification = request.BackgroundNotification,
                Metadata = CreateDescriptorMetadata(request.Name)
            },
            async (backgroundContext, runtimeToken) =>
            {
                var result = await request.InvokeFunctionAsync(
                    sanitizedArguments,
                    parentContext,
                    runtimeToken).ConfigureAwait(false);

                backgroundContext.SetCompletion(
                    summary: ToolResultText.FromResult(result),
                    metadata: new Dictionary<string, string>(StringComparer.Ordinal)
                    {
                        ["function.name"] = request.Name
                    });
            });

        return new AgentInvocationResult
        {
            Mode = AgentInvocationMode.Background,
            Background = new AgentBackgroundInvocationReceipt
            {
                Status = "background_started",
                TaskId = registration.TaskId,
                Name = registration.Name,
                SourceKind = registration.SourceKind,
                SessionId = parentContext.SessionId,
                ThreadId = parentContext.ThreadId,
                Message = $"Started function {request.Name} in the background."
            }
        };
    }

    private static IReadOnlyDictionary<string, string> CreateDescriptorMetadata(string functionName)
        => new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["invocation.kind"] = "function",
            ["invocation.mode"] = "background",
            ["function.name"] = functionName
        };
}
