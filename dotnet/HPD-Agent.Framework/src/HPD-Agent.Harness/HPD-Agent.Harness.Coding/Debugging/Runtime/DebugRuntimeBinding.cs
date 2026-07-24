using HPD.Agent.Middleware;
using HPD.Environment.Contracts;

namespace HPD.Agent.ToolHarness.Coding.Debugging;

public sealed record DebugEventScope(
    string? TraceId,
    string SessionId,
    string ThreadId,
    string? DebugTreeId = null,
    string? DebugSessionId = null,
    string? AdapterId = null);

public sealed class DebugRuntimeBindingState
{
    private string? _reasonCode;
    private Action<string>? _invalidated;

    public bool IsAvailable => Volatile.Read(ref _reasonCode) is null;
    public string? ReasonCode => Volatile.Read(ref _reasonCode);

    public bool Invalidate(string reasonCode)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reasonCode);
        if (Interlocked.CompareExchange(ref _reasonCode, reasonCode, null) is not null) return false;
        Volatile.Read(ref _invalidated)?.Invoke(reasonCode);
        return true;
    }

    public IDisposable OnInvalidated(Action<string> callback)
    {
        ArgumentNullException.ThrowIfNull(callback);
        Action<string>? current;
        Action<string>? updated;
        do
        {
            current = Volatile.Read(ref _invalidated);
            updated = current + callback;
        } while (Interlocked.CompareExchange(ref _invalidated, updated, current) != current);
        if (ReasonCode is { } reason) callback(reason);
        return new InvalidationRegistration(this, callback);
    }

    private void Remove(Action<string> callback)
    {
        Action<string>? current;
        Action<string>? updated;
        do
        {
            current = Volatile.Read(ref _invalidated);
            updated = current - callback;
        } while (Interlocked.CompareExchange(ref _invalidated, updated, current) != current);
    }

    private sealed class InvalidationRegistration(DebugRuntimeBindingState owner, Action<string> callback) : IDisposable
    {
        private Action<string>? _callback = callback;
        public void Dispose()
        {
            var value = Interlocked.Exchange(ref _callback, null);
            if (value is not null) owner.Remove(value);
        }
    }

    public void ThrowIfUnavailable()
    {
        if (!IsAvailable)
            throw new InvalidOperationException($"The debug runtime binding is unavailable ({ReasonCode ?? "unknown"}).");
    }
}

public sealed record DebugRuntimeBinding
{
    public required string AgentRuntimeRegistrationId { get; init; }
    public required string SessionId { get; init; }
    public required string ThreadId { get; init; }
    public required IDebugSessionManager SessionManager { get; init; }
    public RuntimeProcessExecutionBinding? ProcessExecution { get; init; }
    /// <summary>Gets the invocation-wide process sandbox policy selected by the host.</summary>
    public AgentProcessSandboxPolicy ProcessSandbox { get; init; } = new();
    public IEnvironmentRuntime? EnvironmentRuntime => ProcessExecution?.EnvironmentRuntime;
    public required DebugEventScope EventScope { get; init; }
    public required DebugRuntimeBindingState State { get; init; }

    public static DebugRuntimeBinding Capture(FunctionExecutionContext context, bool requireProcessExecution)
    {
        ArgumentNullException.ThrowIfNull(context);
        var sessionId = context.SessionId;
        var threadId = context.ThreadId;
        if (string.IsNullOrWhiteSpace(sessionId) || string.IsNullOrWhiteSpace(threadId))
            throw new InvalidOperationException("Debugging requires an invocation scoped to an HPD session and thread.");

        var manager = context.RuntimeCapabilities.GetRequired<IDebugSessionManager>();
        var bindingState = context.RuntimeCapabilities.GetRequired<DebugRuntimeBindingState>();
        if (!manager.IsAvailable)
            throw new InvalidOperationException("The runtime debug session manager is unavailable.");

        context.RuntimeCapabilities.TryGet<RuntimeProcessExecutionBinding>(out var processExecution);
        if (requireProcessExecution && processExecution is null)
        {
            throw new InvalidOperationException(
                "The selected runtime does not expose an authorized process execution binding for debug adapters.");
        }

        return new DebugRuntimeBinding
        {
            AgentRuntimeRegistrationId = manager.RuntimeId,
            SessionId = sessionId,
            ThreadId = threadId,
            SessionManager = manager,
            ProcessExecution = processExecution,
            ProcessSandbox = AgentProcessSandboxPolicy.FromRunConfig(context.RunConfig),
            EventScope = new DebugEventScope(context.TraceId, sessionId, threadId),
            State = bindingState
        };
    }
}
