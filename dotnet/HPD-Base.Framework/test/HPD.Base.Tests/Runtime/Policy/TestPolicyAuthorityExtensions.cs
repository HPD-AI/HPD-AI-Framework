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
}
