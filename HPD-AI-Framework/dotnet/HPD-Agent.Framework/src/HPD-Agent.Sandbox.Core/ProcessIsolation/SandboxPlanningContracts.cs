namespace HPD.Agent.Sandbox.ProcessIsolation;

using HPD.Environment.Contracts;

public sealed record SandboxExecutionContext
{
    public required PlatformSpec HostPlatform { get; init; }
    public required PlatformSpec ExecutionPlatform { get; init; }
    public required SandboxEnforcementLocation EnforcementLocation { get; init; }
    public ResourceScope? Scope { get; init; }
}

public enum SandboxEnforcementLocation
{
    Host,
    Guest,
    Container,
    Remote,
}

public sealed record SandboxPlanEnvelope
{
    public static SchemaId DefaultSchemaId { get; } = new("hpd.agent.sandbox.plan");

    public required SchemaId SchemaId { get; init; } = DefaultSchemaId;
    public required PlatformSpec ExecutionPlatform { get; init; }
    public required SandboxEnforcementLocation EnforcementLocation { get; init; }
    public required SandboxIsolationPlan Plan { get; init; }
    public IReadOnlyList<Diagnostic> Diagnostics { get; init; } = [];
    public IReadOnlyList<ProviderExtensionData> ProviderExtensions { get; init; } = [];
}

public interface ISandboxPlanner
{
    ValueTask<SandboxPlanEnvelope> PlanAsync(
        ProcessInvocationSpec invocation,
        SandboxExecutionContext context,
        CancellationToken cancellationToken = default);
}

public interface ISandboxApplicator
{
    ValueTask<PreparedSandboxCommand> ApplyAsync(
        CommandInvocation command,
        SandboxPlanEnvelope plan,
        CancellationToken cancellationToken = default);
}
