using HPD.Agent.Middleware;

namespace HPD.Agent.MCP;

/// <summary>Contains immutable invocation facts visible to SDK client handlers.</summary>
internal sealed record McpInvocationContext(
    string InvocationId,
    string ServerName,
    string ToolName,
    string? FunctionCallId,
    string? SessionId,
    string? ThreadId);

/// <summary>Maintains a strict execution-context-local MCP invocation stack.</summary>
internal sealed class McpInvocationContextScope : IDisposable
{
    private static readonly AsyncLocal<Frame?> CurrentFrame = new();
    private readonly Frame _frame;
    private int _disposed;

    private McpInvocationContextScope(McpInvocationContext context)
    {
        _frame = new Frame(context, CurrentFrame.Value);
        CurrentFrame.Value = _frame;
    }

    internal static McpInvocationContext? Current => CurrentFrame.Value?.Context;

    internal static McpInvocationContextScope Push(
        string serverName,
        string toolName,
        FunctionExecutionContext? functionContext) =>
        new(new McpInvocationContext(
            Guid.NewGuid().ToString("N"),
            serverName,
            toolName,
            functionContext?.FunctionCallId,
            functionContext?.SessionId,
            functionContext?.ThreadId));

    public void Dispose()
    {
        if (Volatile.Read(ref _disposed) != 0)
            return;
        if (!ReferenceEquals(CurrentFrame.Value, _frame))
            throw new InvalidOperationException("MCP invocation scopes must be disposed in stack order.");
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
            return;
        CurrentFrame.Value = _frame.Parent;
    }

    private sealed record Frame(McpInvocationContext Context, Frame? Parent);
}
