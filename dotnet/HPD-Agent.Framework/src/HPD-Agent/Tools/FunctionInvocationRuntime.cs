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

        /// <summary>Gets invocation facts already resolved during function preparation.</summary>
        public ResolvedFunctionInvocation? ResolvedInvocation { get; init; }

        /// <summary>
        /// Gets the background notification rule for this function.
        /// </summary>
        public AgentOperationNotificationPolicy OperationNotification { get; init; } = new();

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

        var sanitizedArguments = request.Arguments;
        AgentInvocationMode mode;
        if (request.ResolvedInvocation is { } resolved)
        {
            mode = resolved.Mode;
        }
        else try
        {
            sanitizedArguments = AgentInvocationModes.CreateSanitizedArguments(
                request.Arguments, out var requestedMode);
            mode = AgentInvocationModes.Resolve(
                request.InvocationModePolicy,
                requestedMode);
        }
        catch (InvalidOperationException ex)
        {
            return AgentInvocationModes.CreateFailureResult(
                request.Name,
                AgentOperationSourceKind.LocalTool,
                ex.Message,
                "invalid_invocation_mode");
        }

        if (mode == AgentInvocationMode.Background)
            return await RegisterBackgroundInvocationAsync(request, sanitizedArguments).ConfigureAwait(false);

        var result = await request.InvokeFunctionAsync(
            sanitizedArguments,
            request.ParentContext,
            cancellationToken).ConfigureAwait(false);

        return new AgentInvocationResult
        {
            Mode = AgentInvocationMode.Synchronous,
            Text = ToolResultText.FromResult(result),
            ToolResult = result,
            Operation = null
        };
    }

    private static async Task<AgentInvocationResult> RegisterBackgroundInvocationAsync(
        FunctionInvocationRequest request,
        AIFunctionArguments sanitizedArguments)
    {
        var parentContext = request.ParentContext;
        if (parentContext.OperationRegistry is not { } operations ||
            parentContext.SessionId is null || parentContext.ThreadId is null)
        {
            return AgentInvocationModes.CreateFailureResult(
                request.Name,
                AgentOperationSourceKind.LocalTool,
                "Background invocation requires an active agent runtime.");
        }

        var receipt = await AgentLocalOperationScheduler.StartAsync(
            operations,
            AgentOperationSourceKind.LocalTool,
            request.Name,
            new AgentExecutionAddress(parentContext.AgentName, parentContext.SessionId, parentContext.ThreadId),
            parentContext.ThreadExecutionId,
            parentContext.InvocationSnapshot,
            CreateDescriptorMetadata(request.Name),
            request.OperationNotification,
            async (_, runtimeToken) =>
            {
                var operationContext = parentContext.CreateOperationProjection();
                var result = await request.InvokeFunctionAsync(
                    sanitizedArguments,
                    operationContext,
                    runtimeToken).ConfigureAwait(false);

                return new AgentOperationCompletion(ToolResultText.FromResult(result));
            },
            parentContext.ToolHarnessExecutionScope).ConfigureAwait(false);

        return new AgentInvocationResult
        {
            Mode = AgentInvocationMode.Background,
            Operation = receipt
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
