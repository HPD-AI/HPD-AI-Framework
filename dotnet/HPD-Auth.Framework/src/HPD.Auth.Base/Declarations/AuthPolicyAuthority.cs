using HPD.Base;

namespace HPD.Auth.Base;

internal static class AuthGrantIds
{
    internal static readonly string[] Runtime =
    [
        "auth.identity.read", "auth.identity.mutate", "auth.identity.secret.password",
        "auth.identity.secret.twoFactor", "auth.identity.secret.passkey", "auth.identity.secret.provider",
        "auth.session.read", "auth.session.mutate", "auth.token.read", "auth.token.mutate",
        "auth.token.delivery", "auth.admin.read", "auth.admin.mutate", "auth.audit.append",
        "auth.audit.read", "auth.dataProtection.read", "auth.dataProtection.write", "auth.cleanup.execute",
    ];

    internal static readonly string[] SubjectLifecycle =
    [
        "auth.subject.user.acquire", "auth.subject.user.validate", "auth.subject.user.admin",
        "auth.subject.role.acquire", "auth.subject.role.validate", "auth.subject.role.admin",
    ];

    internal static bool IsSystemGrant(string id) => id is
        "auth.admin.read" or "auth.admin.mutate" or
        "auth.dataProtection.read" or "auth.dataProtection.write" or
        "auth.cleanup.execute" or
        "auth.subject.user.admin" or "auth.subject.role.admin";
}

internal sealed class AuthGrantAuthoritySource(string grantId) : IBaseGrantAuthoritySource
{
    private BaseInstalledGrantRegistration? _registration;

    internal void Bind(BaseInstalledGrantRegistration registration) =>
        _registration = registration ?? throw new ArgumentNullException(nameof(registration));

    public ValueTask EmitAsync(
        BaseGrantAuthorityEmissionContext context,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        BaseInstalledGrantRegistration registration = _registration
            ?? throw new InvalidOperationException("The Auth grant authority is not bound.");

        bool service = context.Principal.AuthenticationState == PrincipalAuthenticationState.Service
            && context.Principal.SubjectKind == AccessSubjectKind.ServicePrincipal
            && string.Equals(context.Principal.SubjectId, AuthBaseContract.ModuleId, StringComparison.Ordinal);
        bool system = context.Principal.AuthenticationState == PrincipalAuthenticationState.System
            && context.Principal.SubjectKind == AccessSubjectKind.System
            && AuthGrantIds.IsSystemGrant(grantId);
        if (!service && !system)
            return ValueTask.CompletedTask;

        context.Emit(registration, CreateGrant(context));
        return ValueTask.CompletedTask;
    }

    private AccessGrant CreateGrant(BaseGrantAuthorityEmissionContext context)
    {
        (string? contractId, int? contractVersion, string action) = grantId switch
        {
            "auth.subject.user.acquire" => ("hpd.auth.user-subject", (int?)1, "subject.acquire"),
            "auth.subject.user.validate" => ("hpd.auth.user-subject", (int?)1, "subject.validate"),
            "auth.subject.user.admin" => ("hpd.auth.user-subject", (int?)1, grantId),
            "auth.subject.role.acquire" => ("hpd.auth.role-subject", (int?)1, "subject.acquire"),
            "auth.subject.role.validate" => ("hpd.auth.role-subject", (int?)1, "subject.validate"),
            "auth.subject.role.admin" => ("hpd.auth.role-subject", (int?)1, grantId),
            _ => ((string?)null, (int?)null, context.Operation.CollectionId),
        };
        bool subjectGrant = contractId is not null;
        return new AccessGrant
        {
            Id = grantId,
            ApplicationId = context.Operation.ApplicationId,
            ModuleId = AuthBaseContract.ModuleId,
            Audience = context.Operation.Audience,
            Subject = new AccessSubject
            {
                Kind = context.Principal.SubjectKind,
                Id = context.Principal.SubjectId,
                TenantId = context.Principal.CurrentTenantId,
            },
            Action = action,
            Scope = new ResourceScope
            {
                Kind = subjectGrant ? ResourceScopeKind.SubjectContract : ResourceScopeKind.Runtime,
                SubjectContractId = contractId,
                SubjectContractVersion = contractVersion,
                TenantId = context.Operation.TenantId,
                ProjectId = context.Operation.ProjectId,
            },
            Effect = GrantEffect.Allow,
            Source = "hpd.auth.grants.v1",
        };
    }
}

internal sealed class AuthTenantPolicyEvaluator : IPolicyEvaluator
{
    public ValueTask<PolicyDecision> EvaluateAsync(
        PolicyEvaluationRequest request,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (request.Grants is not { Length: > 0 })
            return ValueTask.FromResult(PolicyDecision.Deny(
                "auth.policy.authorityDenied", "Auth authority denied the operation."));

        if (request.Collection is null || !AuthPolicyCollections.IsTenantOwned(request.Collection.Id))
            return ValueTask.FromResult(PolicyDecision.Allow());

        if (!Guid.TryParseExact(request.Principal.CurrentTenantId, "D", out Guid tenantId))
            return ValueTask.FromResult(PolicyDecision.Deny(
                "auth.policy.tenantRequired", "A valid Auth tenant is required."));

        FilterExpression tenant = new()
        {
            Kind = FilterNodeKind.Compare,
            Field = "tenantId",
            Operator = FilterOperator.Equal,
            Value = new QueryValue { Kind = QueryValueKind.Id, Id = tenantId.ToString("D") },
        };
        return ValueTask.FromResult(PolicyDecision.Allow().WithRecordFilter(tenant).WithWriteCheck(tenant));
    }
}

internal static class AuthPolicyCollections
{
    internal static bool IsTenantOwned(string id) => id is
        "auth.users" or "auth.roles" or "auth.userClaims" or "auth.roleClaims" or
        "auth.userRoles" or "auth.userLogins" or "auth.userTokens" or "auth.recoveryCodes" or
        "auth.passkeys" or "auth.refreshTokens" or "auth.refreshTokenDeliveries" or "auth.sessions" or
        "auth.ssoProviders" or "auth.userIdentities" or "auth.tenantSettings" or "auth.securityAudit" or
        "auth.cleanupWork";
}

internal static class AuthPolicyAuthorityInstaller
{
    internal static void Install(HPDBaseBuilder builder)
    {
        builder.AddPolicyAuthority<AuthTenantPolicyEvaluator>(new BasePolicyAuthorityDefinition
        {
            Id = "hpd.auth.policy.tenant.v1",
            Version = 1,
            OwningModuleId = AuthBaseContract.ModuleId,
            EvaluatorContractId = "hpd.auth.policy.tenant-evaluator",
            EvaluatorContractVersion = 1,
            CompositionOrder = 0,
        });

        foreach (string grantId in AuthGrantIds.Runtime.Concat(AuthGrantIds.SubjectLifecycle))
        {
            var source = new AuthGrantAuthoritySource(grantId);
            BaseInstalledGrantRegistration registration = builder.AddGrantAuthority(
                new BaseGrantAuthorityDefinition
                {
                    Id = grantId,
                    Version = 1,
                    OwningModuleId = AuthBaseContract.ModuleId,
                    SourceContractId = "hpd.auth.grants",
                    SourceContractVersion = 1,
                }, source);
            source.Bind(registration);
        }
    }
}
