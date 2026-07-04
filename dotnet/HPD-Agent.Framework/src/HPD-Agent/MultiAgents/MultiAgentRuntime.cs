using System.Collections;
using System.ComponentModel;
using System.Reflection;
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
        /// Gets the workflow instance returned by the <see cref="MultiAgentAttribute"/> method.
        /// </summary>
        public required object Workflow { get; init; }

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
        ArgumentNullException.ThrowIfNull(request.Workflow);

        AgentInvocationMode mode;
        try
        {
            mode = AgentInvocationModes.Resolve(
                request.InvocationModePolicy,
                request.RequestedMode);
        }
        catch (InvalidOperationException ex)
        {
            return AgentInvocationModes.CreateReceiptResult(
                request.Name,
                BackgroundTaskSourceKind.MultiAgent,
                ex.Message,
                "invalid_invocation_mode");
        }

        if (mode == AgentInvocationMode.Background)
            return RegisterBackgroundInvocation(request);

        var result = await InvokeSynchronousCoreAsync(request, cancellationToken).ConfigureAwait(false);
        return new AgentInvocationResult
        {
            Mode = AgentInvocationMode.Synchronous,
            Text = result.Text
        };
    }

    private static AgentInvocationResult RegisterBackgroundInvocation(MultiAgentInvocationRequest request)
    {
        var parentContext = request.ParentContext;
        if (parentContext is null || !parentContext.CanRegisterBackgroundTasks)
        {
            return AgentInvocationModes.CreateReceiptResult(
                request.Name,
                BackgroundTaskSourceKind.MultiAgent,
                "Background invocation requires an active agent runtime.");
        }

        var registration = parentContext.RegisterBackgroundTask(
            new BackgroundTaskDescriptor
            {
                Name = request.Name,
                SourceKind = BackgroundTaskSourceKind.MultiAgent,
                SourceId = parentContext.FunctionCallId,
                SessionId = parentContext.SessionId,
                ThreadId = parentContext.ThreadId,
                Invocation = parentContext.InvocationSnapshot,
                Notification = new BackgroundTaskNotificationRule.OnFinalStateRule(
                    Completed: true,
                    Faulted: true),
                Metadata = new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["invocation.kind"] = "multi-agent",
                    ["invocation.mode"] = "background",
                    ["workflow.name"] = request.Name,
                    ["workflow.streamEvents"] = request.StreamEvents.ToString().ToLowerInvariant()
                }
            },
            async (backgroundContext, runtimeToken) =>
            {
                var result = await InvokeSynchronousCoreAsync(
                    request with { RequestedMode = AgentInvocationMode.Synchronous },
                    runtimeToken).ConfigureAwait(false);

                backgroundContext.SetCompletion(
                    summary: result.Text,
                    metadata: new Dictionary<string, string>(StringComparer.Ordinal)
                    {
                        ["workflow.name"] = request.Name
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
                Message = $"Started {request.Name} in the background."
            }
        };
    }

    private static async Task<MultiAgentInvocationResult> InvokeSynchronousCoreAsync(
        MultiAgentInvocationRequest request,
        CancellationToken cancellationToken)
    {
        var input = ResolveInput(request.Input, request.ParentContext);
        var text = request.StreamEvents
            ? await InvokeStreamingAsync(request.Workflow, input, request.ParentContext, cancellationToken)
                .ConfigureAwait(false)
            : await InvokeNonStreamingAsync(request.Workflow, input, cancellationToken)
                .ConfigureAwait(false);

        return new MultiAgentInvocationResult { Text = text };
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
        object workflow,
        string input,
        FunctionExecutionContext? functionContext,
        CancellationToken cancellationToken)
    {
        var executeStreamingAsync = workflow.GetType().GetMethod(
            "ExecuteStreamingAsync",
            [
                typeof(string),
                typeof(IEventCoordinator),
                typeof(AgentMetadata),
                typeof(IChatClient),
                typeof(CancellationToken)
            ]);

        if (executeStreamingAsync == null)
        {
            throw new InvalidOperationException(
                "Multi-agent workflow must expose ExecuteStreamingAsync(string, IEventCoordinator?, AgentMetadata?, IChatClient?, CancellationToken).");
        }

        var stream = executeStreamingAsync.Invoke(
            workflow,
            [
                input,
                functionContext?.GetParentEventCoordinator(),
                functionContext?.GetParentAgentMetadata(),
                functionContext?.GetParentChatClient(),
                cancellationToken
            ]);

        if (stream is not IAsyncEnumerable<Event> events)
        {
            throw new InvalidOperationException(
                "Multi-agent workflow ExecuteStreamingAsync did not return IAsyncEnumerable<Event>.");
        }

        var textResult = new StringBuilder();
        await foreach (var evt in events.WithCancellation(cancellationToken).ConfigureAwait(false))
        {
            if (evt is TextDeltaEvent textDelta)
                textResult.Append(textDelta.Text);
        }

        return textResult.ToString();
    }

    private static async Task<string> InvokeNonStreamingAsync(
        object workflow,
        string input,
        CancellationToken cancellationToken)
    {
        var runAsync = workflow.GetType().GetMethod(
            "RunAsync",
            [typeof(string), typeof(CancellationToken)])
            ?? throw new InvalidOperationException(
                "Multi-agent workflow must expose RunAsync(string, CancellationToken).");

        var result = await AwaitIfNeededAsync(
            runAsync.Invoke(workflow, [input, cancellationToken])).ConfigureAwait(false);

        if (result == null)
            return string.Empty;

        return result.GetType().GetProperty("FinalAnswer")?.GetValue(result) as string
            ?? FormatOutputs(result.GetType().GetProperty("Outputs")?.GetValue(result))
            ?? string.Empty;
    }

    private static async Task<object?> AwaitIfNeededAsync(object? result)
    {
        switch (result)
        {
            case Task task:
                await task.ConfigureAwait(false);
                return task.GetType().IsGenericType
                    ? task.GetType().GetProperty("Result")?.GetValue(task)
                    : null;

            case ValueTask valueTask:
                await valueTask.ConfigureAwait(false);
                return null;

            default:
                var type = result?.GetType();
                if (type?.IsGenericType == true && type.GetGenericTypeDefinition() == typeof(ValueTask<>))
                {
                    return await AwaitGenericValueTaskAsync(result!).ConfigureAwait(false);
                }

                return result;
        }
    }

    private static async Task<object?> AwaitGenericValueTaskAsync(object valueTask)
    {
        var asTask = valueTask.GetType().GetMethod("AsTask", BindingFlags.Instance | BindingFlags.Public)
            ?? throw new InvalidOperationException("ValueTask<T> did not expose AsTask().");
        var task = (Task)asTask.Invoke(valueTask, Array.Empty<object?>())!;
        await task.ConfigureAwait(false);
        return task.GetType().GetProperty("Result")?.GetValue(task);
    }

    private static string? FormatOutputs(object? outputs)
    {
        if (outputs is null)
            return null;

        if (outputs is IDictionary dictionary)
        {
            return string.Join(
                System.Environment.NewLine,
                dictionary.Keys.Cast<object?>()
                    .Select(key => $"{key}: {dictionary[key]}"));
        }

        return outputs.ToString();
    }
}
