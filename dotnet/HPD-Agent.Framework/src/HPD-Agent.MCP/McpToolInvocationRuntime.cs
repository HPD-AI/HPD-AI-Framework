using System.ComponentModel;
using System.Text.Json;
using HPD.Agent.Middleware;
using Microsoft.Extensions.AI;
using ModelContextProtocol.Client;

namespace HPD.Agent.MCP;

/// <summary>
/// Runtime services for invoking MCP tools through HPD tool adapters.
/// </summary>
[EditorBrowsable(EditorBrowsableState.Never)]
internal static class McpToolInvocationRuntime
{
    /// <summary>
    /// Describes a single MCP tool invocation.
    /// </summary>
    internal sealed record McpToolInvocationRequest
    {
        /// <summary>
        /// Gets the MCP server configuration that owns the tool.
        /// </summary>
        public required McpServerConfig ServerConfig { get; init; }

        /// <summary>
        /// Gets the MCP tool name.
        /// </summary>
        public required string ToolName { get; init; }

        /// <summary>
        /// Gets the model-provided tool arguments.
        /// </summary>
        public required AIFunctionArguments Arguments { get; init; }

        /// <summary>
        /// Gets the parent function execution context.
        /// </summary>
        public FunctionExecutionContext? ParentContext { get; init; }

        /// <summary>Gets the owning SDK client for Tasks-enabled calls.</summary>
        public required McpClient Client { get; init; }

        /// <summary>Gets final invocation policy.</summary>
        public required McpInvocationOptions InvocationOptions { get; init; }

        /// <summary>
        /// Invokes the underlying MCP tool synchronously.
        /// </summary>
        public required Func<AIFunctionArguments, FunctionExecutionContext?, CancellationToken, Task<object?>> InvokeToolAsync { get; init; }
    }

    /// <summary>
    /// Invokes an MCP tool synchronously or as runtime-owned background work.
    /// </summary>
    /// <param name="request">The MCP tool invocation request.</param>
    /// <param name="cancellationToken">A token that cancels synchronous invocation.</param>
    /// <returns>The model-facing invocation result.</returns>
    internal static async Task<AgentInvocationResult> InvokeAsync(
        McpToolInvocationRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.ServerConfig);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.ToolName);
        ArgumentNullException.ThrowIfNull(request.Arguments);
        ArgumentNullException.ThrowIfNull(request.InvokeToolAsync);

        var sanitizedArguments = AgentInvocationModes.CreateSanitizedArguments(
            request.Arguments,
            out var requestedMode);
        AgentInvocationMode mode;
        try
        {
            mode = AgentInvocationModes.Resolve(
                ResolveInvocationModePolicy(request.ServerConfig, request.ToolName),
                requestedMode);
        }
        catch (InvalidOperationException ex)
        {
            return AgentInvocationModes.CreateFailureResult(
                request.ToolName,
                AgentOperationSourceKind.McpTask,
                ex.Message,
                "invalid_invocation_mode");
        }

        if (mode == AgentInvocationMode.Background)
        {
            var operationResult = await RegisterRemoteTaskInvocationAsync(
                request,
                sanitizedArguments,
                cancellationToken).ConfigureAwait(false);
            if (operationResult is not null)
                return operationResult;
            return await RegisterBackgroundInvocationAsync(request, sanitizedArguments).ConfigureAwait(false);
        }

        var result = await request.InvokeToolAsync(
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

    private static async ValueTask<AgentInvocationResult?> RegisterRemoteTaskInvocationAsync(
        McpToolInvocationRequest request,
        AIFunctionArguments arguments,
        CancellationToken cancellationToken)
    {
        if (!request.InvocationOptions.EnableRemoteTasks ||
            request.InvocationOptions.RemoteTaskAdapter is not { } adapter)
            return null;
        return await adapter.TryStartAsync(request, arguments, cancellationToken).ConfigureAwait(false);
    }

    private static async Task<AgentInvocationResult> RegisterBackgroundInvocationAsync(
        McpToolInvocationRequest request,
        AIFunctionArguments sanitizedArguments)
    {
        var parentContext = request.ParentContext;
        if (parentContext?.OperationRegistry is not { } operations ||
            parentContext.SessionId is null || parentContext.ThreadId is null)
        {
            return AgentInvocationModes.CreateFailureResult(
                request.ToolName,
                AgentOperationSourceKind.McpTask,
                "Background invocation requires an active agent runtime.");
        }

        var receipt = await AgentLocalOperationScheduler.StartAsync(
            operations,
            AgentOperationSourceKind.LocalTool,
            request.ToolName,
            new AgentExecutionAddress(parentContext.AgentName, parentContext.SessionId, parentContext.ThreadId),
            parentContext.ThreadExecutionId,
            parentContext.InvocationSnapshot,
            CreateDescriptorMetadata(request.ServerConfig, request.ToolName),
            new AgentOperationNotificationPolicy(),
            async (_, runtimeToken) =>
            {
                var result = await request.InvokeToolAsync(
                    sanitizedArguments,
                    parentContext,
                    runtimeToken).ConfigureAwait(false);

                return new AgentOperationCompletion(ToolResultText.FromResult(result));
            }).ConfigureAwait(false);

        return new AgentInvocationResult
        {
            Mode = AgentInvocationMode.Background,
            Operation = receipt
        };
    }

    private static IReadOnlyDictionary<string, string> CreateDescriptorMetadata(
        McpServerConfig serverConfig,
        string toolName)
        => new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["invocation.kind"] = "mcp-tool",
            ["invocation.mode"] = "background",
            ["mcp.serverName"] = serverConfig.Name,
            ["mcp.toolName"] = toolName
        };

    internal static AgentInvocationModePolicy ResolveInvocationModePolicy(
        McpServerConfig serverConfig,
        string toolName)
    {
        ArgumentNullException.ThrowIfNull(serverConfig);
        ArgumentException.ThrowIfNullOrWhiteSpace(toolName);

        return serverConfig.ToolInvocationModePolicies.TryGetValue(toolName, out var policy)
            ? policy
            : serverConfig.InvocationModePolicy;
    }

    private static class ToolResultText
    {
        internal static string FromResult(object? result)
        {
            return result switch
            {
                null => string.Empty,
                string text => text,
                JsonElement json => json.GetRawText(),
                ToolResultPayload payload => payload.Text ?? payload.Json?.GetRawText() ?? string.Empty,
                ClientTools.TextContent text => text.Text,
                ClientTools.JsonContent json => json.Value.GetRawText(),
                ClientTools.BinaryContent binary => binary.Filename ?? binary.Id ?? binary.Url ?? binary.MimeType ?? string.Empty,
                IEnumerable<ClientTools.IToolResultContent> contents => string.Join(
                    System.Environment.NewLine,
                    contents.Select(FromResult)),
                _ => result.ToString() ?? string.Empty
            };
        }
    }
}
