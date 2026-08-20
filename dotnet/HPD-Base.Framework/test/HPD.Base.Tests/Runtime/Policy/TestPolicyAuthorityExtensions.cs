namespace HPD.Base;

using Microsoft.Extensions.DependencyInjection;

internal static class TestPolicyAuthorityExtensions
{
    private static BasePolicyAuthorityDefinition Definition => new()
    {
        Id = "hpd.base.tests.policy",
        Version = 1,
        OwningModuleId = "hpd.base.tests",
        EvaluatorContractId = "hpd.base.tests.policy-evaluator",
        EvaluatorContractVersion = 1,
        CompositionOrder = 0,
    };

    public static HPDBaseBuilder AddTestPolicyAuthority<T>(this HPDBaseBuilder builder)
        where T : class, IPolicyEvaluator, new() =>
        builder.AddPolicyAuthority<T>(Definition);

    public static HPDBaseBuilder AddTestPolicyAuthority(
        this HPDBaseBuilder builder,
        IPolicyEvaluator evaluator) =>
        builder.AddPolicyAuthority(Definition, evaluator);

    public static HPDBaseBuilder AddTestStaticGrant(
        this HPDBaseBuilder builder,
        string id)
    {
        builder.AddStaticGrantAuthority(new BaseGrantAuthorityDefinition
        {
            Id = id,
            Version = 1,
            OwningModuleId = "hpd.base.tests",
            SourceContractId = "hpd.base.tests.static-grant",
            SourceContractVersion = 1,
        }, new AccessGrant
        {
            Id = id,
            Subject = new AccessSubject { Kind = AccessSubjectKind.ServicePrincipal, Id = "hpd.base.tests" },
            Action = "*",
            Scope = new ResourceScope { Kind = ResourceScopeKind.Runtime },
        });
        return builder;
    }

    public static HPDBaseBuilder AddTestSubjectLifecycleGrant(
        this HPDBaseBuilder builder,
        string id,
        string applicationId,
        string moduleId,
        string action,
        string contractId,
        int contractVersion,
        string subjectId = "service-1",
        string? tenantId = "tenant-a",
        HPDBaseEndpointAudience audience = HPDBaseEndpointAudience.Application)
    {
        builder.AddStaticGrantAuthority(GrantDefinition(id), SubjectLifecycleGrant(
            id, applicationId, moduleId, action, contractId, contractVersion, subjectId, tenantId) with { Audience = audience });
        return builder;
    }

    public static IHPDBaseRuntimeBuilder UseTestPolicyAuthority(
        this IHPDBaseRuntimeBuilder builder,
        IPolicyEvaluator evaluator) =>
        builder.UsePolicyAuthority("hpd-base-tests", Definition, evaluator);

    public static IServiceCollection AddTestPolicyAuthority(
        this IServiceCollection services,
        IPolicyEvaluator evaluator,
        params string[] grantIds)
    {
        var authority = new BasePolicyAuthorityBuilder();
        authority.AddPolicy(Definition, evaluator);
        foreach (string id in grantIds)
        {
            authority.AddStaticGrant(new BaseGrantAuthorityDefinition
            {
                Id = id, Version = 1, OwningModuleId = "hpd.base.tests",
                SourceContractId = "hpd.base.tests.static-grant", SourceContractVersion = 1,
            }, new AccessGrant
            {
                Id = id,
                Subject = new AccessSubject { Kind = AccessSubjectKind.ServicePrincipal, Id = "hpd.base.tests" },
                Action = "*",
                Scope = new ResourceScope { Kind = ResourceScopeKind.Runtime },
            });
        }
        services.AddSingleton(authority.Freeze("hpd-base-tests"));
        return services;
    }

    public static IServiceCollection AddTestSubjectLifecyclePolicyAuthority(
        this IServiceCollection services,
        IPolicyEvaluator evaluator,
        params AccessGrant[] grants)
    {
        var authority = new BasePolicyAuthorityBuilder();
        authority.AddPolicy(Definition, evaluator);
        foreach (AccessGrant grant in grants)
            authority.AddStaticGrant(GrantDefinition(grant.Id), grant);
        services.AddSingleton(authority.Freeze(grants.Select(static grant => grant.ApplicationId).FirstOrDefault(static value => value is not null) ?? "hpd.base.application"));
        return services;
    }

    public static AccessGrant TestSubjectLifecycleGrant(
        string id,
        string applicationId,
        string moduleId,
        string action,
        string contractId,
        int contractVersion,
        string subjectId = "service-1",
        string? tenantId = "tenant-a") =>
        SubjectLifecycleGrant(id, applicationId, moduleId, action, contractId, contractVersion, subjectId, tenantId);

    public static AccessGrant TestRuntimeGrant(string id) => new()
    {
        Id = id,
        Subject = new AccessSubject { Kind = AccessSubjectKind.ServicePrincipal, Id = "hpd.base.tests" },
        Action = "*",
        Scope = new ResourceScope { Kind = ResourceScopeKind.Runtime },
    };

    private static BaseGrantAuthorityDefinition GrantDefinition(string id) => new()
    {
        Id = id, Version = 1, OwningModuleId = "hpd.base.tests",
        SourceContractId = "hpd.base.tests.static-grant", SourceContractVersion = 1,
    };

    private static AccessGrant SubjectLifecycleGrant(string id, string applicationId, string moduleId, string action,
        string contractId, int contractVersion, string subjectId, string? tenantId) => new()
    {
        Id = id, ApplicationId = applicationId, ModuleId = moduleId, Audience = HPDBaseEndpointAudience.Application,
        Subject = new AccessSubject { Kind = AccessSubjectKind.ServicePrincipal, Id = subjectId, TenantId = tenantId },
        Action = action, Effect = GrantEffect.Allow,
        Scope = new ResourceScope
        {
            Kind = ResourceScopeKind.SubjectContract, SubjectContractId = contractId,
            SubjectContractVersion = contractVersion, TenantId = tenantId,
        },
    };
}
