using HPD.Agent;
using HPD.Agent.Providers;
using HPD.Agent.Security;
using HPD.Agent.Serialization;

namespace HPD.MultiAgent;

public sealed record WorkflowNodeIdentity(string WorkflowName, string NodeId, string? ExecutionId = null, int Attempt = 0);

/// <summary>One trusted phase for runtime services, output contracts, and per-attempt authority.</summary>
public class WorkflowHostBinding
{
    public ProviderComposition? Providers { get; }
    public AgentEventComposition Events { get; }
    public ISessionStore SessionStore { get; }
    public IServiceProvider? Services { get; }
    private readonly AgentSecurityRunConfig _authorization;

    public WorkflowHostBinding(ProviderComposition? providers, AgentEventComposition events, ISessionStore sessionStore,
        AgentSecurityRunConfig authorization, IServiceProvider? services = null)
    {
        Providers = providers;
        Events = events ?? throw new ArgumentNullException(nameof(events));
        SessionStore = sessionStore ?? throw new ArgumentNullException(nameof(sessionStore));
        if (!ReferenceEquals(sessionStore.EventCodec, events.Codec))
            throw new ArgumentException("The session store must use the exact host event codec.", nameof(sessionStore));
        Services = services;
        _authorization = AgentRunConfigSnapshot.Capture(new AgentRunConfig { Security = authorization }, providers)!.Security;
    }

    /// <summary>Hosts can resolve preserved/AOT-known types without reflective discovery.</summary>
    public virtual Type? ResolveType(string name) => Type.GetType(name, throwOnError: false);
    public virtual void ConfigureAgent(WorkflowNodeIdentity identity, AgentBuilder builder) { }
    public virtual void ValidateNode(WorkflowNodeIdentity identity, AgentConfig? definition, AgentNodeOptions options) { }

    /// <summary>Creates attempt-owned context and resources. The lease is disposed on every exit path.</summary>
    public virtual ValueTask<WorkflowRunBinding> BindAttemptAsync(WorkflowNodeIdentity identity, AgentRunConfig intent, CancellationToken cancellationToken)
        => ValueTask.FromResult(new WorkflowRunBinding(intent));

    internal void Apply(WorkflowNodeIdentity identity, AgentBuilder builder)
    {
        ConfigureAgent(identity, builder);
        builder.WithEventComposition(Events).WithSessionStore(SessionStore);
        if (Services is not null) builder.WithServiceProvider(Services);
    }

    internal AgentRunConfig Authorize(AgentRunConfig requested)
    {
        var security = requested.Security;
        if (_authorization.Sandbox.Mode == AgentSandboxPolicy.Enforced && security.Sandbox.Mode == AgentSandboxPolicy.Disabled
            || _authorization.Approval != AgentApprovalPolicy.AutoApprove && security.Approval == AgentApprovalPolicy.AutoApprove
            || security.PermissionOverrides?.Any(permission => !permission.RequiresPermission && _authorization.PermissionOverrides?.Contains(permission) != true) == true)
            throw new AgentRunConfigurationException("CapabilityCeilingExceeded", "Security", "Run security exceeds host authorization.");
        AgentSandboxAuthorization.Validate(security.Sandbox.Capabilities, _authorization.Sandbox.Capabilities);
        requested.Security = security with
        {
            CapabilityCeiling = AgentSandboxAuthorization.Snapshot(_authorization.Sandbox.Capabilities),
            Sandbox = security.Sandbox with
            {
                Escape = _authorization.Sandbox.Escape == AgentSandboxEscapePolicy.Deny ? AgentSandboxEscapePolicy.Deny : security.Sandbox.Escape
            }
        };
        return requested;
    }
}

/// <summary>A host-owned attempt scope; resource disposal is separate from configuration snapshotting.</summary>
public sealed class WorkflowRunBinding : IAsyncDisposable
{
    public AgentRunConfig Config { get; }
    private IAsyncDisposable? _lifetime;
    public WorkflowRunBinding(AgentRunConfig config, IAsyncDisposable? lifetime = null)
    { Config = config ?? throw new ArgumentNullException(nameof(config)); _lifetime = lifetime; }
    public ValueTask DisposeAsync() => Interlocked.Exchange(ref _lifetime, null)?.DisposeAsync() ?? ValueTask.CompletedTask;
}
