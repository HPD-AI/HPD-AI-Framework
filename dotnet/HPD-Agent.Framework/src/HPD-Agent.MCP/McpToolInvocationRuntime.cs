using System.ComponentModel;
using System.Text.Json;
using HPD.Agent.Middleware;
using Microsoft.Extensions.AI;

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
        public required MCPServerConfig ServerConfig { get; init; }

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
            return AgentInvocationModes.CreateReceiptResult(
                request.ToolName,
                BackgroundTaskSourceKind.McpTool,
                ex.Message,
                "invalid_invocation_mode");
        }

        if (mode == AgentInvocationMode.Background)
            return RegisterBackgroundInvocation(request, sanitizedArguments);

        var result = await request.InvokeToolAsync(
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
        McpToolInvocationRequest request,
        AIFunctionArguments sanitizedArguments)
    {
        var parentContext = request.ParentContext;
        if (parentContext is null || !parentContext.CanRegisterBackgroundTasks)
        {
            return AgentInvocationModes.CreateReceiptResult(
                request.ToolName,
                BackgroundTaskSourceKind.McpTool,
                "Background invocation requires an active agent runtime.");
        }

        var registration = parentContext.RegisterBackgroundTask(
            new BackgroundTaskDescriptor
            {
                Name = request.ToolName,
                SourceKind = BackgroundTaskSourceKind.McpTool,
                SourceId = parentContext.FunctionCallId,
                SessionId = parentContext.SessionId,
                ThreadId = parentContext.ThreadId,
                Invocation = parentContext.InvocationSnapshot,
                Notification = request.ServerConfig.BackgroundNotification,
                Metadata = CreateDescriptorMetadata(request.ServerConfig, request.ToolName)
            },
            async (backgroundContext, runtimeToken) =>
            {
                var result = await request.InvokeToolAsync(
                    sanitizedArguments,
                    parentContext,
                    runtimeToken).ConfigureAwait(false);

                backgroundContext.SetCompletion(
                    summary: ToolResultText.FromResult(result),
                    metadata: new Dictionary<string, string>(StringComparer.Ordinal)
                    {
                        ["mcp.serverName"] = request.ServerConfig.Name,
                        ["mcp.toolName"] = request.ToolName
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
                Message = $"Started MCP tool {request.ToolName} in the background."
            }
        };
    }

    private static IReadOnlyDictionary<string, string> CreateDescriptorMetadata(
        MCPServerConfig serverConfig,
        string toolName)
        => new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["invocation.kind"] = "mcp-tool",
            ["invocation.mode"] = "background",
            ["mcp.serverName"] = serverConfig.Name,
            ["mcp.toolName"] = toolName
        };

    internal static AgentInvocationModePolicy ResolveInvocationModePolicy(
        MCPServerConfig serverConfig,
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
