using HPD.Base;

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
        var orchestrator = new DefaultBasePolicyOrchestrator(owner);

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
        var orchestrator = new DefaultBasePolicyOrchestrator(builder.Freeze("policy-test"));

        OperationResult<BasePolicyEvaluation> result = await orchestrator.EvaluateWriteAsync(Request());

        Assert.Equal(OperationStatus.PolicyDenied, result.Status);
        Assert.Equal(["allow", "deny"], calls);
    }

    [Fact]
    public async Task Static_grant_semantics_are_bound_into_owner_and_evaluation_authority()
    {
        var first = new BasePolicyAuthorityBuilder();
        first.AddPolicy(Definition("allow", 0), new RecordingPolicy("allow", [], PolicyDecision.Allow()));
        first.AddStaticGrant(GrantDefinition(), Grant("items"));
        BasePolicyAuthorityOwner firstOwner = first.Freeze("policy-test");
        var second = new BasePolicyAuthorityBuilder();
        second.AddPolicy(Definition("allow", 0), new RecordingPolicy("allow", [], PolicyDecision.Allow()));
        second.AddStaticGrant(GrantDefinition(), Grant("other-items"));
        BasePolicyAuthorityOwner secondOwner = second.Freeze("policy-test");
        Assert.NotEqual(Convert.ToHexString(firstOwner.Checksum), Convert.ToHexString(secondOwner.Checksum));

        var orchestrator = new DefaultBasePolicyOrchestrator(firstOwner);
        OperationResult<BasePolicyEvaluation> result = await orchestrator.EvaluateWriteAsync(Request());

        BaseAdmittedGrantAuthority admitted = Assert.Single(result.Value!.Authority!.AdmittedGrants);
        Assert.Equal("grant.items", admitted.GrantId);
        Assert.Equal(32, admitted.GrantRegistrationChecksum.Length);
        Assert.Equal(32, admitted.GrantChecksum.Length);
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

    private static BaseGrantAuthorityDefinition GrantDefinition() => new()
    {
        Id = "grant.items", Version = 1, OwningModuleId = "test-module",
        SourceContractId = "test.grants", SourceContractVersion = 1,
    };

    private static AccessGrant Grant(string collectionId) => new()
    {
        Id = "grant.items", Subject = new AccessSubject { Kind = AccessSubjectKind.Anonymous },
        Action = "write", Scope = new ResourceScope { Kind = ResourceScopeKind.Collection, CollectionId = collectionId },
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
