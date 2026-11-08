using Microsoft.AspNetCore.Http;

namespace HPD.Agent.AspNetCore.RuntimeExtensions;

/// <summary>Authorizes a tenant/application request to publish a runtime schema.</summary>
public interface IRuntimeAgentExtensionMutationAuthorizer
{
    ValueTask<RuntimeAgentExtensionAuthorizationResult> AuthorizeAsync(
        HttpContext context,
        RuntimeAgentExtensionSchema schema,
        CancellationToken cancellationToken);
}

/// <summary>Result of one runtime schema mutation authorization decision.</summary>
public sealed record RuntimeAgentExtensionAuthorizationResult(
    bool Allowed,
    string? Reason = null)
{
    public static RuntimeAgentExtensionAuthorizationResult Deny(string reason) =>
        new(false, reason);
}
