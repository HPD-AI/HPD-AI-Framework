namespace HPD.Agent.Sandbox.ProcessIsolation;

using HPD.Environment.Contracts;

public interface ISandboxApplicator
{
    ValueTask<PreparedSandboxCommand> ApplyAsync(
        CommandInvocation command,
        SandboxPlanEnvelope plan,
        CancellationToken cancellationToken = default);
}
