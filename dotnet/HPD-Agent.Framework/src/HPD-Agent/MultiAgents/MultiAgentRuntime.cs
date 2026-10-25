using System.ComponentModel;
using System.Text;
using HPD.Agent.Middleware;
using HPD.Events;
using Microsoft.Extensions.AI;

namespace HPD.Agent;

/// <summary>
/// Runtime services for invoking multi-agent workflow capabilities.
/// </summary>
[EditorBrowsable(EditorBrowsableState.Never)]
public static class MultiAgentRuntime
{
    /// <summary>
    /// Describes a multi-agent workflow invocation request.
    /// </summary>
    public sealed record MultiAgentInvocationRequest
    {
        /// <summary>
        /// Gets the workflow factory invoked by the <see cref="MultiAgentAttribute"/> method.
        /// Construction runs under the operation deadline and activation ownership.
        /// </summary>
        public required Func<Task<IMultiAgentWorkflow>> WorkflowFactory { get; init; }

        /// <summary>Gets the complete construction and execution deadline.</summary>
        public TimeSpan Timeout { get; init; } = TimeSpan.FromMinutes(5);

        /// <summary>
        /// Gets the model-facing workflow capability name.
        /// </summary>
        public required string Name { get; init; }

        /// <summary>
        /// Gets the model-provided input for the workflow.
        /// </summary>
        public required string Input { get; init; }

        /// <summary>
        /// Gets the parent function execution context, when the workflow is invoked from a tool call.
        /// </summary>
        public FunctionExecutionContext? ParentContext { get; init; }

        /// <summary>
        /// Gets a value indicating whether workflow events should be streamed and collected.
        /// </summary>
        public bool StreamEvents { get; init; } = true;

        /// <summary>
        /// Gets the workflow's author-defined invocation mode policy.
        /// </summary>
        public AgentInvocationModePolicy InvocationModePolicy { get; init; } =
            AgentInvocationModePolicy.SynchronousOnly;

        /// <summary>
        /// Gets the model-requested invocation mode, when the workflow allows model choice.
        /// </summary>
        public AgentInvocationMode? RequestedMode { get; init; }
    }

    /// <summary>
    /// Describes the result returned from a multi-agent workflow invocation.
    /// </summary>
    public sealed record MultiAgentInvocationResult
    {
        /// <summary>
        /// Gets the text returned to the parent tool call.
        /// </summary>
        public required string Text { get; init; }
    }

    /// <summary>
    /// Invokes a multi-agent workflow using the shared runtime path used by generated and reflection wrappers.
    /// </summary>
    /// <param name="request">The workflow invocation request.</param>
    /// <param name="cancellationToken">A token that cancels workflow execution.</param>
    /// <returns>The workflow invocation result.</returns>
    public static async Task<AgentInvocationResult> InvokeAsync(
        MultiAgentInvocationRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.WorkflowFactory);
        if (request.Timeout <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(request), "Workflow timeout must be positive.");

        AgentInvocationMode mode;
        try
        {
            mode = AgentInvocationModes.Resolve(
                request.InvocationModePolicy,
                request.RequestedMode);
        }
        catch (InvalidOperationException ex)
        {
            return AgentInvocationModes.CreateFailureResult(
                request.Name,
                AgentOperationSourceKind.MultiAgent,
                ex.Message,
                "invalid_invocation_mode");
        }

        if (mode == AgentInvocationMode.Background)
            return await RegisterBackgroundInvocationAsync(request).ConfigureAwait(false);

        var result = await InvokeSynchronousCoreAsync(request, cancellationToken).ConfigureAwait(false);
        return new AgentInvocationResult
        {
            Mode = AgentInvocationMode.Synchronous,
            Text = result.Text
        };
    }

    private static async Task<AgentInvocationResult> RegisterBackgroundInvocationAsync(MultiAgentInvocationRequest request)
    {
        var parentContext = request.ParentContext;
        if (parentContext?.OperationRegistry is not { } operations ||
            parentContext.SessionId is null || parentContext.ThreadId is null)
        {
            return AgentInvocationModes.CreateFailureResult(
                request.Name,
                AgentOperationSourceKind.MultiAgent,
                "Background invocation requires an active agent runtime.");
        }

        var receipt = await AgentLocalOperationScheduler.StartAsync(
            operations,
            AgentOperationSourceKind.MultiAgent,
            request.Name,
            new AgentExecutionAddress(parentContext.AgentName, parentContext.SessionId, parentContext.ThreadId),
            parentContext.ThreadExecutionId,
            parentContext.InvocationSnapshot,
            new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["invocation.kind"] = "multi-agent",
                    ["invocation.mode"] = "background",
                    ["workflow.name"] = request.Name,
                    ["workflow.streamEvents"] = request.StreamEvents.ToString().ToLowerInvariant()
                },
            new AgentOperationNotificationPolicy(),
            async (_, runtimeToken) =>
            {
                var result = await InvokeSynchronousCoreAsync(
                    request with { RequestedMode = AgentInvocationMode.Synchronous },
                    runtimeToken).ConfigureAwait(false);

                return new AgentOperationCompletion(result.Text);
            }).ConfigureAwait(false);

        return new AgentInvocationResult
        {
            Mode = AgentInvocationMode.Background,
            Operation = receipt
        };
    }

    private static async Task<MultiAgentInvocationResult> InvokeSynchronousCoreAsync(
        MultiAgentInvocationRequest request,
        CancellationToken cancellationToken)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(request.Timeout);

        var factoryTask = Task.Run(request.WorkflowFactory, CancellationToken.None);
        IMultiAgentWorkflow workflow;
        try
        {
            workflow = await factoryTask.WaitAsync(deadline.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            _ = DisposeLateFactoryResultAsync(factoryTask);
            throw new TimeoutException(
                $"Workflow '{request.Name}' did not finish construction within {request.Timeout}.");
        }

        try
        {
            var input = ResolveInput(request.Input, request.ParentContext);
            string text;
            try
            {
                text = request.StreamEvents
                    ? await InvokeStreamingAsync(workflow, input, request.ParentContext, deadline.Token)
                        .ConfigureAwait(false)
                    : await InvokeNonStreamingAsync(workflow, input, request.ParentContext, deadline.Token)
                        .ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                throw new TimeoutException(
                    $"Workflow '{request.Name}' exceeded its {request.Timeout} deadline.");
            }

            return new MultiAgentInvocationResult { Text = text };
        }
        finally
        {
            await DisposeWorkflowAsync(workflow).ConfigureAwait(false);
        }
    }

    private static async Task DisposeLateFactoryResultAsync(Task<IMultiAgentWorkflow> factoryTask)
    {
        try
        {
            var workflow = await factoryTask.ConfigureAwait(false);
            await DisposeWorkflowAsync(workflow).ConfigureAwait(false);
        }
        catch
        {
            // The original timeout is authoritative. Observation prevents an unobserved fault
            // when a parameterless factory completes after its operation has already expired.
        }
    }

    private static async ValueTask DisposeWorkflowAsync(IMultiAgentWorkflow workflow)
    {
        if (workflow is IAsyncDisposable asyncDisposable)
            await asyncDisposable.DisposeAsync().ConfigureAwait(false);
        else if (workflow is IDisposable disposable)
            disposable.Dispose();
    }

    private static string ResolveInput(string input, FunctionExecutionContext? functionContext)
    {
        if (!string.IsNullOrEmpty(input) || functionContext == null)
            return input;

        var messages = functionContext.Analyze(state => state.CurrentMessages);
        var lastUserMessage = messages?.LastOrDefault(message => message.Role == ChatRole.User);
        return lastUserMessage?.Text ?? string.Empty;
    }

    private static async Task<string> InvokeStreamingAsync(
        IMultiAgentWorkflow workflow,
        string input,
        FunctionExecutionContext? functionContext,
        CancellationToken cancellationToken)
    {
        var textResult = new StringBuilder();
        await foreach (var evt in workflow.ExecuteStreamingAsync(input, functionContext, cancellationToken)
            .WithCancellation(cancellationToken).ConfigureAwait(false))
        {
            if (evt is TextDeltaEvent textDelta)
                textResult.Append(textDelta.Text);
        }

        return textResult.ToString();
    }

    private static async Task<string> InvokeNonStreamingAsync(
        IMultiAgentWorkflow workflow,
        string input,
        FunctionExecutionContext? functionContext,
        CancellationToken cancellationToken)
    {
        return await workflow.RunAsync(input, functionContext, cancellationToken).ConfigureAwait(false);
    }

}
