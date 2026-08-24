using HPD.AI.Platform.Studio;
using Microsoft.AspNetCore.Http;

namespace HPD.Base.Studio;

/// <summary>Maps one host-authenticated Studio session to BASE's exact principal contract.</summary>
public interface IBaseStudioPrincipalContextResolver
{
    /// <summary>Resolves the current principal without exposing credentials to Studio modules.</summary>
    ValueTask<PrincipalContext?> ResolveAsync(HttpContext httpContext,
        BaseStudioSessionObservation session, CancellationToken cancellationToken);
    /// <summary>Resolves the exact currently authorized BASE tenant, project, or global scope.</summary>
    ValueTask<BaseOwnedSubjectScopeEvidence?> ResolveScopeAsync(HttpContext httpContext,
        BaseStudioSessionObservation session, CancellationToken cancellationToken);
}

internal sealed class BaseStudioAuthorization
{
    private readonly IBasePolicyOrchestrator _policy;
    private readonly IBaseStudioPrincipalContextResolver _principals;
    private readonly HPDBaseStudioAuthoritySnapshot _authority;
    private readonly TimeProvider _timeProvider;

    public BaseStudioAuthorization(IBasePolicyOrchestrator policy, IBaseStudioPrincipalContextResolver principals,
        HPDBaseStudioAuthoritySnapshot authority, TimeProvider timeProvider)
    { _policy = policy; _principals = principals; _authority = authority; _timeProvider = timeProvider; }

    internal ValueTask<PrincipalContext?> ResolvePrincipalAsync(BaseStudioBootstrapInvocation invocation,
        CancellationToken cancellationToken) => _principals.ResolveAsync(invocation.HttpContext,
            invocation.Authorization.Session, cancellationToken);

    internal async ValueTask<BasePolicyEvaluationAuthority?> AdmitAsync(BaseStudioBootstrapInvocation invocation,
        BaseStudioGrantRequirement requirement, CancellationToken cancellationToken)
    {
        PrincipalContext? principal = await ResolvePrincipalAsync(invocation, cancellationToken).ConfigureAwait(false);
        if (principal is null) return null;
        OperationResult<BasePolicyEvaluation> result = await _policy.EvaluateStudioAsync(new BaseStudioPolicyRequest
        {
            Principal = principal,
            Operation = new OperationContext
            {
                ApplicationId = _authority.ApplicationId, Audience = HPDBaseEndpointAudience.ControlPlane,
                Operation = BaseOperationKind.AdminInspect, CollectionId = "base.studio", Mode = OperationMode.User,
                Now = _timeProvider.GetUtcNow(),
            },
            StudioOperationId = requirement.OperationId,
            StudioModuleId = requirement.OwningModuleId,
            StudioResourceKind = requirement.ResourceKind?.ToString(),
        }, cancellationToken).ConfigureAwait(false);
        BasePolicyEvaluationAuthority? authority = result.IsSuccess() && result.Value?.Decision.Effect == PolicyEffect.Allow
            ? result.Value.Authority : null;
        if (authority is null || authority.PolicyGraphGeneration != _authority.PolicyOwnerGeneration ||
            !System.Security.Cryptography.CryptographicOperations.FixedTimeEquals(authority.PolicyOwnerChecksum.AsSpan(), _authority.GetPolicyOwnerChecksum()))
            return null;
        return authority.AdmittedGrants.Any(grant => grant.GrantId == requirement.GrantId && grant.GrantVersion == requirement.Version &&
            System.Security.Cryptography.CryptographicOperations.FixedTimeEquals(grant.GrantRegistrationChecksum.AsSpan(), requirement.RegistrationChecksum.ToArray()))
            ? authority : null;
    }
}
