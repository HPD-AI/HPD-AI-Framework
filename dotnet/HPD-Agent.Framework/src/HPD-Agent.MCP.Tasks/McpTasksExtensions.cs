using System.Text.Json;
using HPD.Agent.Middleware;
using Microsoft.Extensions.AI;
using ModelContextProtocol.Protocol;
using HPD.Agent.MCP;

namespace HPD.Agent.MCP.Tasks;

/// <summary>Activates the optional stable SDK Tasks extension for an MCP source.</summary>
public static class McpTasksExtensions
{
    /// <summary>Enables remote MCP Tasks for this source's task-eligible tool invocations.</summary>
    /// <param name="options">The MCP options being configured.</param>
    /// <returns>The same options instance.</returns>
    public static McpOptions AddTasksExtension(this McpOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        options.Invocation.EnableRemoteTasks = true;
        options.Invocation.RemoteTaskAdapter = McpRemoteTaskAdapter.Instance;
        return options;
    }
}

internal sealed class McpRemoteTaskAdapter : IMcpRemoteTaskAdapter
{
    internal static McpRemoteTaskAdapter Instance { get; } = new();

    public async ValueTask<AgentInvocationResult?> TryStartAsync(
        McpToolInvocationRuntime.McpToolInvocationRequest request,
        AIFunctionArguments arguments,
        CancellationToken cancellationToken)
    {
        var context = request.ParentContext;
        if (context?.OperationRegistry is not { } operations ||
            string.IsNullOrWhiteSpace(context.SessionId) ||
            string.IsNullOrWhiteSpace(context.ThreadId))
            return null;

        var provider = new McpTaskProvider(
            operations, request.InvocationOptions.RecoveryReferenceProtector);
        var started = await provider.StartAsync(
            request.Client,
            request.ServerConfig.Name,
            new CallToolRequestParams
            {
                Name = request.ToolName,
                Arguments = arguments.ToDictionary(
                    static pair => pair.Key,
                    static pair => ToJsonElement(pair.Value),
                    StringComparer.Ordinal)
            },
            new AgentExecutionAddress(context.AgentName, context.SessionId, context.ThreadId),
            context.InvocationSnapshot,
            context.ThreadExecutionId,
            context,
            cancellationToken).ConfigureAwait(false);

        return started.Receipt is not null
            ? new AgentInvocationResult
            {
                Mode = AgentInvocationMode.Background,
                Operation = started.Receipt
            }
            : new AgentInvocationResult
            {
                Mode = AgentInvocationMode.Synchronous,
                ToolResult = started.Result
            };
    }

    public bool CanRecover(AgentOperationRecoveryReference recoveryReference) =>
        recoveryReference.Kind == "mcp-task-v1";

    public async ValueTask<bool> TryRecoverAsync(
        AgentOperation operation,
        McpRuntime runtime,
        IMcpRecoveryReferenceProtector protector,
        AgentCapabilityLease revisionLease,
        CancellationToken cancellationToken)
    {
        var recovery = operation.Snapshot.Recovery;
        if (recovery is null)
            return false;
        var json = await protector.UnprotectAsync(
            recovery.ProtectedReference, cancellationToken).ConfigureAwait(false);
        var reference = JsonSerializer.Deserialize(
            json, McpTaskRecoveryJsonContext.Default.McpTaskRecoveryReference);
        if (reference is null)
            return false;
        var client = await runtime.TryGetRecoveryClientAsync(
            reference.ServerName, cancellationToken).ConfigureAwait(false);
        if (client is null)
            return false;
        await McpTaskProvider.AttachRecoveredAsync(
            operation, client, reference.TaskId, revisionLease, cancellationToken).ConfigureAwait(false);
        return true;
    }

    private static JsonElement ToJsonElement(object? value) => value switch
    {
        null => JsonSerializer.SerializeToElement((string?)null, McpJsonSerializerContext.Default.String),
        JsonElement element => element.Clone(),
        string text => JsonSerializer.SerializeToElement(text, McpJsonSerializerContext.Default.String),
        bool boolean => JsonSerializer.SerializeToElement(boolean, McpJsonSerializerContext.Default.Boolean),
        int number => JsonSerializer.SerializeToElement(number, McpJsonSerializerContext.Default.Int32),
        long number => JsonSerializer.SerializeToElement(number, McpJsonSerializerContext.Default.Int64),
        double number => JsonSerializer.SerializeToElement(number, McpJsonSerializerContext.Default.Double),
        decimal number => JsonSerializer.SerializeToElement(number, McpJsonSerializerContext.Default.Decimal),
        _ => throw new InvalidOperationException(
            $"MCP Task argument type '{value.GetType().FullName}' is not a normalized JSON value.")
    };
}
