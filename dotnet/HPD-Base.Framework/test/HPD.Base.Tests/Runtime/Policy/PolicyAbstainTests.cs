using HPD.Base;
using Microsoft.Extensions.DependencyInjection;

namespace HPD.Base.Tests.Policy;

public sealed class PolicyAbstainTests
{
    [Fact]
    public async Task AbstainFailsClosedByDefault()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IPolicyEvaluator, AbstainPolicyEvaluator>();
        services.AddHPDBaseRuntime();
        using var provider = services.BuildServiceProvider();

        var result = await provider.GetRequiredService<IBasePolicyOrchestrator>().EvaluateReadAsync(Request());

        Assert.Equal(OperationStatus.PolicyDenied, result.Status);
    }

    [Fact]
    public async Task TrustedHostCanAllowAbstain()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IPolicyEvaluator, AbstainPolicyEvaluator>();
        services.AddHPDBaseRuntime()
            .UseDevelopmentPolicyAbstainAsAllow();
        using var provider = services.BuildServiceProvider();

        var result = await provider.GetRequiredService<IBasePolicyOrchestrator>().EvaluateReadAsync(Request());

        Assert.Equal(OperationStatus.Ok, result.Status);
    }

    [Fact]
    public async Task RequiredObligationFailsClosed()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IPolicyEvaluator, RequiredObligationPolicyEvaluator>();
        services.AddHPDBaseRuntime();
        using var provider = services.BuildServiceProvider();

        var result = await provider.GetRequiredService<IBasePolicyOrchestrator>().EvaluateReadAsync(Request());

        Assert.Equal(OperationStatus.Unsupported, result.Status);
        Assert.Equal("base.runtime.policy.obligation.unsupported", result.Error!.Code);
        Assert.Equal("base.obligation.redact", result.Error.Target);
    }

    private static BasePolicyRequest Request() => new()
    {
        Principal = RuntimeTestData.AnonymousPrincipal,
        Operation = RuntimeTestData.Operation(BaseOperationKind.Get),
        ResourceKind = PolicyResourceKind.Record,
        Collection = new CollectionDefinition
        {
            Id = "items",
            Name = "items",
            Kind = BaseCollectionKinds.Document,
            SchemaMode = SchemaMode.Loose,
            UnknownFields = UnknownFieldPolicy.Preserve
        }
    };

    private sealed class RequiredObligationPolicyEvaluator : IPolicyEvaluator
    {
        public ValueTask<PolicyDecision> EvaluateAsync(
            PolicyEvaluationRequest request,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            _ = request;
            return ValueTask.FromResult(new PolicyDecision
            {
                Effect = PolicyEffect.Allow,
                Outcome = PolicyOutcome.AllowedWithConstraints,
                Obligations =
                [
                    new PolicyObligation
                    {
                        Kind = "base.obligation.redact",
                        Code = "mustRedact",
                        Enforcement = ObligationEnforcement.Required
                    }
                ]
            });
        }
    }
}
