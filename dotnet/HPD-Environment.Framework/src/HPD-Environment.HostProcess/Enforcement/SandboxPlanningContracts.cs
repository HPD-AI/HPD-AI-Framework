namespace HPD.Environment.HostProcess.ProcessIsolation;

using HPD.Environment.Contracts;

public interface ISandboxApplicator
{
    ValueTask<PreparedSandboxCommand> ApplyAsync(
        CommandInvocation command,
        SandboxPlanEnvelope plan,
        CancellationToken cancellationToken = default);
}
