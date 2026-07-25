using HPD.Agent.Middleware;
using HPD.Agent.Security;

namespace HPD.Agent.ToolHarness.Coding.Security;

/// <summary>Authorizes in-process filesystem access against the run security profile.</summary>
internal static class AgentFilesystemAccess
{
    public static async ValueTask<string> AuthorizeReadAsync(
        string path,
        string operationId,
        FunctionExecutionContext context,
        CancellationToken cancellationToken)
        => (await AuthorizeAsync(
            path,
            operationId,
            context,
            AgentCapabilityKind.FilesystemRead,
            cancellationToken).ConfigureAwait(false)).Path;

    public static async ValueTask<string> AuthorizeWriteAsync(
        string path,
        string operationId,
        FunctionExecutionContext context,
        CancellationToken cancellationToken)
        => (await AuthorizeAsync(
            path,
            operationId,
            context,
            AgentCapabilityKind.FilesystemWrite,
            cancellationToken).ConfigureAwait(false)).Path;

    public static ValueTask<AgentFilesystemAuthorization> AuthorizeReadCapabilityAsync(
        string path,
        string operationId,
        FunctionExecutionContext context,
        CancellationToken cancellationToken)
        => AuthorizeAsync(
            path,
            operationId,
            context,
            AgentCapabilityKind.FilesystemRead,
            cancellationToken);

    public static ValueTask<AgentFilesystemAuthorization> AuthorizeWriteCapabilityAsync(
        string path,
        string operationId,
        FunctionExecutionContext context,
        CancellationToken cancellationToken)
        => AuthorizeAsync(
            path,
            operationId,
            context,
            AgentCapabilityKind.FilesystemWrite,
            cancellationToken);

    private static async ValueTask<AgentFilesystemAuthorization> AuthorizeAsync(
        string path,
        string operationId,
        FunctionExecutionContext context,
        AgentCapabilityKind capability,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var canonical = Path.GetFullPath(path);
        // Direct library calls have no agent run and therefore no host sandbox
        // boundary to enforce. Agent tool invocation always supplies this context.
        if (context is null)
            return new AgentFilesystemAuthorization(canonical, Escalated: false);

        var security = context.RunConfig.Security;
        if (security.Sandbox == AgentSandboxPolicy.Disabled)
            return new AgentFilesystemAuthorization(canonical, Escalated: false);

        var sandbox = CodingSandboxRuntime.Capture(context.RunConfig);
        if (HasGrant(sandbox.Filesystem, canonical, capability))
            return new AgentFilesystemAuthorization(canonical, Escalated: false);

        if (security.SandboxEscape == AgentSandboxEscapePolicy.Deny)
            throw new AgentCapabilityDeniedException(capability, canonical);

        var requestId = Guid.NewGuid().ToString("N");
        AgentCapabilityResponseEvent response;
        try
        {
            response = await context
                .RequestAsync<AgentCapabilityRequestEvent, AgentCapabilityResponseEvent>(
                    new AgentCapabilityRequestEvent(
                        requestId,
                        nameof(AgentFilesystemAccess),
                        context.FunctionCallId,
                        operationId,
                        capability,
                        new AgentCapabilityResource
                        {
                            Value = canonical,
                            DisplayName = Path.GetFileName(canonical)
                        },
                        capability == AgentCapabilityKind.FilesystemRead
                            ? "The operation needs to read outside the active sandbox grants."
                            : "The operation needs to write outside the active sandbox grants."),
                    timeout: null)
                .WaitAsync(cancellationToken)
                .ConfigureAwait(false);
        }
        catch (TimeoutException)
        {
            throw new AgentCapabilityDeniedException(capability, canonical);
        }

        if (!response.Approved)
            throw new AgentCapabilityDeniedException(capability, canonical);

        return new AgentFilesystemAuthorization(canonical, Escalated: true);
    }

    private static bool HasGrant(
        IReadOnlyList<AgentSandboxPathGrant> grants,
        string canonical,
        AgentCapabilityKind capability)
    {
        foreach (var grant in grants)
        {
            var root = Path.GetFullPath(grant.Path);
            if (!AgentWorkspace.IsPathUnderDirectory(root, canonical))
                continue;

            if (capability == AgentCapabilityKind.FilesystemRead ||
                grant.Access == AgentSandboxPathAccess.Write)
                return true;
        }

        return false;
    }
}

internal readonly record struct AgentFilesystemAuthorization(
    string Path,
    bool Escalated);

internal sealed class AgentCapabilityDeniedException(
    AgentCapabilityKind capability,
    string resource)
    : Exception($"The {capability} capability was not granted for '{resource}'.")
{
    public AgentCapabilityKind Capability { get; } = capability;
    public string Resource { get; } = resource;
}
