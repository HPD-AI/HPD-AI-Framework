namespace HPD.Agent.Sandbox;

using HPD.Agent.Middleware;
using HPD.Environment.Contracts;

/// <summary>
/// Creates backend-neutral sandbox sessions after reporting the capabilities
/// that can be enforced by the concrete backend.
/// </summary>
public interface ISandboxBackendFactory
{
    /// <summary>Gets the placement mechanism implemented by this factory.</summary>
    SandboxEnforcementLocation EnforcementLocation { get; }

    /// <summary>Queries provider and sandbox capabilities for a prospective session.</summary>
    ValueTask<SandboxBackendCapabilities> GetCapabilitiesAsync(
        SandboxCapabilitiesQuery query,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Creates a session or fails closed when its required capabilities cannot
    /// be enforced.
    /// </summary>
    ValueTask<ISandboxSession> CreateSessionAsync(
        SandboxSessionRequest request,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Agent-facing sandbox session that binds process execution and workspace file
/// access to one selected Environment execution unit.
/// </summary>
public interface ISandboxSession : IAsyncDisposable
{
    /// <summary>Selected placement mechanism.</summary>
    SandboxEnforcementLocation EnforcementLocation { get; }

    /// <summary>Single authorized binding used by command-backed tools.</summary>
    RuntimeProcessExecutionBinding Execution { get; }

    /// <summary>Workspace binding shared by file tools and trusted tooling services.</summary>
    RuntimeWorkspaceBinding Workspace { get; }

    /// <summary>Capabilities used to admit and describe this session.</summary>
    SandboxBackendCapabilities Capabilities { get; }
}
