namespace HPD.Agent.Sandbox.ProcessIsolation;

public sealed class HostSandboxApplicator : ISandboxApplicator
{
    private readonly SandboxIsolationManager _isolationManager;

    public HostSandboxApplicator(SandboxIsolationManager isolationManager)
    {
        _isolationManager = isolationManager ?? throw new ArgumentNullException(nameof(isolationManager));
    }

    public ValueTask<PreparedSandboxCommand> ApplyAsync(
        CommandInvocation command,
        SandboxPlanEnvelope plan,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(plan);

        if (plan.EnforcementLocation is not SandboxEnforcementLocation.Host)
            throw new InvalidOperationException($"Host sandbox applicator cannot apply a {plan.EnforcementLocation} sandbox plan.");

        return new ValueTask<PreparedSandboxCommand>(
            _isolationManager.WrapCommandAsync(command, plan.Plan, cancellationToken));
    }
}

