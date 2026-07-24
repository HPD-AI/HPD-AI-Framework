namespace HPD.Agent.ToolHarness.Coding.Debugging;

internal sealed record DebugRuntimeServices(
    DebugSessionManager Manager,
    DebugSemanticService Semantics,
    DebugBreakpointService Breakpoints,
    DebugLifecycleService Lifecycle);

/// <summary>
/// Stateless DI-owned bridge that constructs debugger services around the manager captured from
/// one agent runtime. Manager-bound services must never be registered as application singletons.
/// </summary>
internal sealed class DebugRuntimeServiceFactory
{
    public DebugRuntimeServices Create(DebugRuntimeBinding runtime)
    {
        ArgumentNullException.ThrowIfNull(runtime);
        runtime.State.ThrowIfUnavailable();
        if (runtime.SessionManager is not DebugSessionManager manager)
            throw new InvalidOperationException("The runtime debug session manager implementation is unsupported.");
        if (!string.Equals(runtime.AgentRuntimeRegistrationId, manager.RuntimeId, StringComparison.Ordinal))
            throw new InvalidOperationException("The captured runtime identity does not match its debug session manager.");

        var semantics = new DebugSemanticService(manager);
        return new(
            manager,
            semantics,
            new DebugBreakpointService(manager),
            new DebugLifecycleService(manager, semantics));
    }
}
