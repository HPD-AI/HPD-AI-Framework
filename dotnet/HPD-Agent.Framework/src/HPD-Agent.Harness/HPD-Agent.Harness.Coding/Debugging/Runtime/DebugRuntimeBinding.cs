using HPD.Agent.Middleware;
using HPD.Agent.Security;
using HPD.Agent.Sandbox;
using HPD.Agent.ToolHarness.Coding.Security;
using HPD.Environment.Contracts;

namespace HPD.Agent.ToolHarness.Coding.Debugging;

public sealed record DebugEventScope(
    string? TraceId,
    string SessionId,
    string ThreadId,
    string? ToolCallId = null,
    string? DebugTreeId = null,
    string? DebugSessionId = null,
    string? AdapterId = null);

/// <summary>Stable capability identifiers used by portable debugging.</summary>
public static class DebugEnvironmentCapabilityIds
{
    public const string AdapterLaunch = "hpd.environment.debug.adapter.launch";
    public const string AdapterStdio = "hpd.environment.debug.adapter.stdio";
    public const string Launch = "hpd.environment.debug.launch";
    public const string Attach = "hpd.environment.debug.attach";
    public const string ProcessControl = "hpd.environment.debug.process-control";
    public const string UnitOwnership = "hpd.environment.debug.unit-ownership";
    public const string Ptrace = "hpd.environment.debug.ptrace";
    public const string Symbols = "hpd.environment.debug.symbols";
    public const string SourceMapping = "hpd.environment.debug.source-mapping";
    public const string Breakpoints = "hpd.environment.debug.breakpoint-observation";
    public const string Exceptions = "hpd.environment.debug.exception-inspection";
}

/// <summary>
/// Effective debug capabilities of one selected Environment session.
/// </summary>
public sealed record DebugEnvironmentCapabilities
{
    public bool AdapterLaunch { get; init; }
    public bool AdapterStdio { get; init; }
    public bool Launch { get; init; }
    public bool Attach { get; init; }
    public bool ProcessControl { get; init; }
    public bool UnitOwnership { get; init; }
    public bool Ptrace { get; init; }
    public bool Symbols { get; init; }
    public bool SourceMapping { get; init; }
    public bool BreakpointObservation { get; init; }
    public bool ExceptionInspection { get; init; }
    public string Topology { get; init; } = "unavailable";

    /// <summary>Capabilities that can be proven from the captured bindings.</summary>
    public static DebugEnvironmentCapabilities From(
        RuntimeProcessExecutionBinding? processExecution,
        RuntimeWorkspaceBinding workspace,
        ISandboxSession? session)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        bool process = processExecution is not null;
        Dictionary<string, CapabilityState> facts = session?
            .Capabilities.ProviderReport.Capabilities
            .Where(fact => fact.Id.Value.StartsWith(
                "hpd.environment.debug.",
                StringComparison.Ordinal))
            .GroupBy(fact => fact.Id.Value, StringComparer.Ordinal)
            .ToDictionary(
                group => group.Key,
                group => group.Last().State,
                StringComparer.Ordinal) ??
            new Dictionary<string, CapabilityState>(
                StringComparer.Ordinal);
        bool Supports(string id, bool baseline) =>
            facts.TryGetValue(id, out CapabilityState state)
                ? state == CapabilityState.Supported
                : baseline;

        return new DebugEnvironmentCapabilities
        {
            AdapterLaunch = process &&
                Supports(DebugEnvironmentCapabilityIds.AdapterLaunch, true),
            AdapterStdio = process &&
                Supports(DebugEnvironmentCapabilityIds.AdapterStdio, true) &&
                facts.GetValueOrDefault(
                    DebugEnvironmentCapabilityIds.Attach,
                    CapabilityState.Supported) !=
                    CapabilityState.Unsupported,
            Launch = process &&
                Supports(DebugEnvironmentCapabilityIds.Launch, true),
            Attach = process && Supports(
                DebugEnvironmentCapabilityIds.Attach,
                baseline: true),
            ProcessControl = process &&
                Supports(DebugEnvironmentCapabilityIds.ProcessControl, true),
            UnitOwnership = process &&
                Supports(DebugEnvironmentCapabilityIds.UnitOwnership, true),
            Ptrace = process &&
                Supports(DebugEnvironmentCapabilityIds.Ptrace, true),
            Symbols = Supports(
                DebugEnvironmentCapabilityIds.Symbols,
                baseline: false),
            SourceMapping = workspace.Paths is not null &&
                Supports(
                    DebugEnvironmentCapabilityIds.SourceMapping,
                    baseline: true),
            BreakpointObservation = process &&
                Supports(
                    DebugEnvironmentCapabilityIds.Breakpoints,
                    baseline: true),
            ExceptionInspection = process &&
                Supports(
                    DebugEnvironmentCapabilityIds.Exceptions,
                    baseline: true),
            Topology = session?.Backend switch
            {
                SandboxBackendKind.HostProcess => "host-process",
                SandboxBackendKind.HostContainers => "retained-container",
                SandboxBackendKind.VirtualMachine => "guest-vm",
                _ => "process-binding",
            },
        };
    }
}

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
    /// <summary>Workspace shared by debug planning, adapters, and the debuggee.</summary>
    public required RuntimeWorkspaceBinding Workspace { get; init; }
    /// <summary>Effective debug capabilities reported by the Environment.</summary>
    public DebugEnvironmentCapabilities Capabilities { get; init; } =
        new();
    /// <summary>Gets the invocation-wide process sandbox policy selected by the host.</summary>
    public AgentSandboxRuntime ProcessSandbox { get; init; } = AgentSandboxRuntime.Default;
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
        if (!context.RuntimeCapabilities.TryGet<RuntimeWorkspaceBinding>(
                out var workspace))
        {
            throw new InvalidOperationException(
                "The selected runtime does not expose a workspace binding for debug planning.");
        }
        context.RuntimeCapabilities.TryGet<ISandboxSession>(
            out var sandboxSession);
        if (requireProcessExecution && processExecution is null)
        {
            throw new InvalidOperationException(
                "The selected runtime does not expose an authorized process execution binding for debug adapters.");
        }
        if (processExecution?.WorkspaceContent is { } processContent &&
            processContent != workspace.Content)
        {
            throw new InvalidOperationException(
                "Debug process and workspace bindings refer to different content instances.");
        }

        return new DebugRuntimeBinding
        {
            AgentRuntimeRegistrationId = manager.RuntimeId,
            SessionId = sessionId,
            ThreadId = threadId,
            SessionManager = manager,
            ProcessExecution = processExecution,
            Workspace = workspace,
            Capabilities = DebugEnvironmentCapabilities.From(
                processExecution,
                workspace,
                sandboxSession),
            ProcessSandbox = CodingSandboxRuntime.Capture(context.RunConfig),
            EventScope = new DebugEventScope(
                context.TraceId,
                sessionId,
                threadId,
                context.FunctionCallId),
            State = bindingState
        };
    }
}
