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
        "base.subjectLifecycle.tombstone", "base.subjectLifecycle.feed.read",
        "base.subjectLifecycle.feed.checkpoint", "base.subjectRetirement.acknowledge",
        "base.subjectRetirement.barrier.inspect", "base.subjectRetirement.purge",
        "hpd.auth.user-subject.retirement.purge.source",
        "hpd.auth.role-subject.retirement.purge.source",
    ];

    internal static readonly string[] Operations =
    [
        "auth.operation.user.create", "auth.operation.user.update", "auth.operation.user.security",
        "auth.operation.role.mutate", "auth.operation.membership.mutate", "auth.operation.login.mutate",
        "auth.operation.passkey.mutate", "auth.operation.refresh.issue", "auth.operation.refresh.rotate",
        "auth.operation.session.mutate", "auth.operation.audit.append",
        "auth.operation.cleanup.initialize.user", "auth.operation.cleanup.initialize.role",
        "auth.operation.cleanup.advance", "auth.operation.cleanup.prepareRetirement",
        "auth.operation.cleanup.retire.user", "auth.operation.cleanup.retire.role",
    ];

    internal static readonly string[] Semantic =
    [
        "auth.semantic.cleanup.user.ensure", "auth.semantic.cleanup.role.ensure",
        "auth.semantic.cleanup.user.retire", "auth.semantic.cleanup.role.retire",
        "auth.semantic.cleanup.user.maintain", "auth.semantic.cleanup.role.maintain",
        "base.subjectLifecycle.finalizeRetirement",
    ];

    internal static readonly string[] ActivationDefinitions =
    [
        "hpd.auth.cleanup.user.v1", "hpd.auth.cleanup.role.v1",
        "hpd.auth.cleanup.bootstrap.user.v1", "hpd.auth.cleanup.bootstrap.role.v1",
        "hpd.auth.cleanup.semantic-retire.user.v1", "hpd.auth.cleanup.semantic-retire.role.v1",
        "hpd.auth.cleanup.reconcile.v1", "hpd.auth.expiration.sessions.v1",
        "hpd.auth.expiration.refresh-tokens.v1", "hpd.auth.expiration.deliveries.v1",
        "hpd.auth.data-protection.refresh.v1",
    ];

    internal static readonly string[] Activation = ActivationDefinitions
        .SelectMany(static id => new[]
        {
            id + ".enqueue", id + ".observe", id + ".claim", id + ".execute",
            id + ".renew", id + ".complete", id + ".fail", id + ".yield", id + ".cancel",
            id + ".inspect", id + ".replay", id + ".migrate", id + ".reconcile",
            id + ".retry", id + ".dispose", id + ".remove", id + ".repair",
        })
        .Order(StringComparer.Ordinal)
        .ToArray();

    internal static readonly string[] Schedule =
    [
        "hpd.auth.schedule.cleanup-reconcile.v1.manage", "hpd.auth.schedule.cleanup-reconcile.v1.materialize",
        "hpd.auth.schedule.session-expiration.v1.manage", "hpd.auth.schedule.session-expiration.v1.materialize",
        "hpd.auth.schedule.refresh-expiration.v1.manage", "hpd.auth.schedule.refresh-expiration.v1.materialize",
        "hpd.auth.schedule.delivery-expiration.v1.manage", "hpd.auth.schedule.delivery-expiration.v1.materialize",
        "hpd.auth.schedule.data-protection-refresh.v1.manage", "hpd.auth.schedule.data-protection-refresh.v1.materialize",
    ];

    internal static IEnumerable<string> All => Runtime
        .Concat(SubjectLifecycle)
        .Concat(Operations)
        .Concat(Semantic)
        .Concat(Activation)
        .Concat(Schedule);

    internal static bool IsSystemGrant(string id) => id is
        "auth.admin.read" or "auth.admin.mutate" or
        "auth.dataProtection.read" or "auth.dataProtection.write" or
        "auth.cleanup.execute" or "auth.identity.mutate" or
        "auth.session.mutate" or "auth.token.mutate" or "auth.token.delivery" or
        "base.subjectLifecycle.feed.read" or
        "base.subjectLifecycle.feed.checkpoint" or "base.subjectRetirement.acknowledge" or
        "base.subjectLifecycle.finalizeRetirement" or
        "base.subjectRetirement.barrier.inspect" or "base.subjectRetirement.purge" or
        "hpd.auth.user-subject.retirement.purge.source" or
        "hpd.auth.role-subject.retirement.purge.source" or
        "auth.subject.user.acquire" or "auth.subject.user.admin" or "auth.subject.user.validate" or
        "auth.subject.role.acquire" or "auth.subject.role.admin" or "auth.subject.role.validate";

    internal static bool IsSystemOnlyGrant(string id) =>
        id.StartsWith("auth.operation.cleanup.", StringComparison.Ordinal)
        || id.StartsWith("auth.semantic.cleanup.", StringComparison.Ordinal)
        || id.StartsWith("hpd.auth.", StringComparison.Ordinal);
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

        bool lifecycleDispatcher = context.Principal.AuthenticationState == PrincipalAuthenticationState.Service
            && context.Principal.SubjectKind == AccessSubjectKind.ServicePrincipal
            && string.Equals(context.Principal.SubjectId, "hpd.auth.lifecycle-dispatcher", StringComparison.Ordinal)
            && grantId is "hpd.auth.cleanup.bootstrap.user.v1.enqueue" or "hpd.auth.cleanup.bootstrap.role.v1.enqueue";
        bool service = !AuthGrantIds.IsSystemOnlyGrant(grantId)
            && context.Principal.AuthenticationState == PrincipalAuthenticationState.Service
            && context.Principal.SubjectKind == AccessSubjectKind.ServicePrincipal
            && string.Equals(context.Principal.SubjectId, AuthBaseContract.ModuleId, StringComparison.Ordinal);
        bool system = context.Principal.AuthenticationState == PrincipalAuthenticationState.System
            && context.Principal.SubjectKind == AccessSubjectKind.System
            && (AuthGrantIds.IsSystemGrant(grantId) || AuthGrantIds.IsSystemOnlyGrant(grantId));
        if (!lifecycleDispatcher && !service && !system)
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
            "base.subjectLifecycle.tombstone" when context.Operation.CollectionId is "hpd.auth.user-subject" or "hpd.auth.role-subject" =>
                (context.Operation.CollectionId, (int?)1, grantId),
            "base.subjectLifecycle.finalizeRetirement" when context.Operation.CollectionId is "hpd.auth.user-subject" or "hpd.auth.role-subject" =>
                (context.Operation.CollectionId, (int?)1, grantId),
            "base.subjectRetirement.barrier.inspect" or "base.subjectRetirement.purge"
                when context.Operation.CollectionId is "hpd.auth.user-subject" or "hpd.auth.role-subject" =>
                (context.Operation.CollectionId, (int?)1, grantId),
            _ => ((string?)null, (int?)null, context.Operation.CollectionId),
        };
        bool subjectGrant = contractId is not null;
        bool collectionGrant = !subjectGrant
            && AuthPolicyCollections.IsAuthOwned(context.Operation.CollectionId);
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
                Kind = subjectGrant
                    ? ResourceScopeKind.SubjectContract
                    : collectionGrant ? ResourceScopeKind.Collection : ResourceScopeKind.Runtime,
                CollectionId = collectionGrant ? context.Operation.CollectionId : null,
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

        FilterExpression tenantRecordFilter = new()
        {
            Kind = FilterNodeKind.Compare,
            Field = request.Resource.Kind == PolicyResourceKind.Query
                ? request.Collection.Id + ".tenantId"
                : "tenantId",
            Operator = FilterOperator.Equal,
            Value = new QueryValue { Kind = QueryValueKind.Id, Id = tenantId.ToString("D") },
        };
        FilterExpression tenantWriteCheck = new()
        {
            Kind = FilterNodeKind.Compare,
            Field = "tenantId",
            Operator = FilterOperator.Equal,
            Value = new QueryValue { Kind = QueryValueKind.Id, Id = tenantId.ToString("D") },
        };
        return ValueTask.FromResult(PolicyDecision.Allow()
            .WithRecordFilter(tenantRecordFilter)
            .WithWriteCheck(tenantWriteCheck));
    }
}

internal static class AuthPolicyCollections
{
    internal static bool IsAuthOwned(string id) => IsTenantOwned(id) || id is
        "auth.dataProtectionKeys" or
        "auth.maintenanceCursors" or "auth.maintenanceRuns";

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

        foreach (string grantId in AuthGrantIds.All)
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
