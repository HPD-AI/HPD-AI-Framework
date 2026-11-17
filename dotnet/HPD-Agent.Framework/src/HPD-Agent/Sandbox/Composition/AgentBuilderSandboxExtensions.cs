namespace HPD.Agent.Sandbox;

using HPD.Environment.Contracts;

/// <summary>Attaches an Environment-backed sandbox to an agent.</summary>
public static class AgentBuilderSandboxExtensions
{
    public static AgentBuilder WithSandbox(
        this AgentBuilder builder,
        ISandboxProvisioningProvider provider,
        SandboxSessionRequest? request = null,
        Func<AgentWorkspaceReference, CancellationToken, ValueTask<SandboxSessionRequest>>? resolveWorkspace = null)
    {
        ArgumentNullException.ThrowIfNull(builder);
        if (request is null && resolveWorkspace is null)
            throw new ArgumentException("Configure a default sandbox request or a workspace resolver.", nameof(request));
        return builder.ReplaceMiddleware<EnvironmentSandboxMiddleware>(
            new EnvironmentSandboxMiddleware(provider, request, resolveWorkspace));
    }
}
