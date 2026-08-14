using HPD.Base;
using Microsoft.Extensions.Options;

namespace HPD.Base.Tests.Policy;

public sealed class PolicyAuthorityGraphTests
{
    [Fact]
    public async Task InstalledPoliciesPreserveCanonicalEvaluatorOrderInAuthority()
    {
        var calls = new List<string>();
        var builder = new BasePolicyAuthorityBuilder();
        builder.AddPolicy(Definition("z-policy", 20), new RecordingPolicy("z", calls, PolicyDecision.Allow()));
        builder.AddPolicy(Definition("a-policy", 10), new RecordingPolicy("a", calls, PolicyDecision.Allow()));
        BasePolicyAuthorityOwner owner = builder.Freeze("policy-test");
        var orchestrator = new DefaultBasePolicyOrchestrator([], Options.Create(HPDBaseRuntimeOptions.CreateDefault()), owner);

        OperationResult<BasePolicyEvaluation> result = await orchestrator.EvaluateWriteAsync(Request());

        Assert.Equal(OperationStatus.Ok, result.Status);
        Assert.Equal(["a", "z"], calls);
        Assert.Equal(["a-policy", "z-policy"], result.Value!.Authority!.AppliedPolicies.Select(static value => value.PolicyId));
        Assert.Equal([10, 20], result.Value.Authority.AppliedPolicies.Select(static value => value.CompositionOrder));
        Assert.Equal(owner.Checksum, result.Value.Authority.PolicyOwnerChecksum);
    }

    [Fact]
    public async Task DenyStopsLaterInstalledEvaluators()
    {
        var calls = new List<string>();
        var builder = new BasePolicyAuthorityBuilder();
        builder.AddPolicy(Definition("allow", 0), new RecordingPolicy("allow", calls, PolicyDecision.Allow()));
        builder.AddPolicy(Definition("deny", 1), new RecordingPolicy("deny", calls, PolicyDecision.Deny("denied", "Denied.")));
        builder.AddPolicy(Definition("later", 2), new RecordingPolicy("later", calls, PolicyDecision.Allow()));
        var orchestrator = new DefaultBasePolicyOrchestrator([], Options.Create(HPDBaseRuntimeOptions.CreateDefault()), builder.Freeze("policy-test"));

        OperationResult<BasePolicyEvaluation> result = await orchestrator.EvaluateWriteAsync(Request());

        Assert.Equal(OperationStatus.PolicyDenied, result.Status);
        Assert.Equal(["allow", "deny"], calls);
    }

    private static BasePolicyAuthorityDefinition Definition(string id, int order) => new()
    {
        Id = id,
        Version = 1,
        OwningModuleId = "test-module",
        EvaluatorContractId = id + "-evaluator",
        EvaluatorContractVersion = 1,
        CompositionOrder = order,
    };

    private static BasePolicyRequest Request() => new()
    {
        Principal = RuntimeTestData.AnonymousPrincipal,
        Operation = RuntimeTestData.Operation(BaseOperationKind.Create),
        ResourceKind = PolicyResourceKind.CreatePayload,
        Collection = new CollectionDefinition
        {
            Id = "items", Name = "items", Kind = BaseCollectionKinds.Document,
            SchemaMode = SchemaMode.Loose, UnknownFields = UnknownFieldPolicy.Preserve,
        },
    };

    private sealed class RecordingPolicy(string name, List<string> calls, PolicyDecision decision) : IPolicyEvaluator
    {
        public ValueTask<PolicyDecision> EvaluateAsync(PolicyEvaluationRequest request, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            _ = request;
            calls.Add(name);
            return ValueTask.FromResult(decision);
        }
    }
}
