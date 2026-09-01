using HPD.AI.Platform.Studio;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace HPD.AI.Platform.Tests;

/// <summary>Verifies the closed module-owned Runtime producer seam.</summary>
public sealed class BaseStudioRuntimeContributionTests
{
    /// <summary>Proves outward resource identities are closed, purpose-bound, and kind-specific.</summary>
    [Fact]
    public void Outward_resource_authority_is_typed_and_checksums_all_identity_members()
    {
        BaseStudioSha256 collection = BaseStudioSha256.FromDigest(new byte[32]);
        var first = new BaseStudioRecordResource("sample.application", "users", collection, "42");
        var second = new BaseStudioRecordResource("sample.application", "users", collection, "43");

        Assert.Equal(BaseStudioResourceKind.Record, first.Kind);
        Assert.Equal("users", first.CollectionId);
        Assert.Equal("42", first.RecordId);
        Assert.False(BaseStudioSha256.FixedTimeEquals(first.AuthorityChecksum, second.AuthorityChecksum));
        Assert.Throws<ArgumentException>(() => new BaseStudioApplicationResource("wrong\napplication"));
    }

    /// <summary>Proves every closed resource discriminator round-trips and tampering fails closed.</summary>
    [Fact]
    public void Every_resource_route_token_round_trips_and_rejects_tampering()
    {
        BaseStudioSha256 hash = BaseStudioSha256.FromDigest(new byte[32]); const string app = "sample.application";
        BaseStudioResourceIdentity[] values =
        [
            new BaseStudioApplicationResource(app), new BaseStudioModuleResource(app,"module",1), new BaseStudioCollectionResource(app,"items",hash),
            new BaseStudioRecordResource(app,"items",hash,"record"), new BaseStudioRelationResource(app,"items","one","edge","other","two"),
            new BaseStudioFileBucketResource(app,"bucket"), new BaseStudioFileResource(app,"bucket","object"), new BaseStudioRegisteredReadResource(app,"read",1),
            new BaseStudioSelectionOperationResource(app,"profile",1), new BaseStudioModuleMutationResource(app,"operation",1),
            new BaseStudioOperationExecutionResource(app,"mutation","operation","request"), new BaseStudioReceiptResource(app,"mutation","operation","request"),
            new BaseStudioActivationDefinitionResource(app,"definition",1), new BaseStudioActivationResource(app,"definition",1,"activation"),
            new BaseStudioScheduleResource(app,"schedule",1), new BaseStudioOccurrenceResource(app,"schedule",1,"occurrence"),
            new BaseStudioActivationAttemptResource(app,"activation",1), new BaseStudioEffectResource(app,"activation",1,"effect"),
            new BaseStudioExecutorResource(app,"host","process",1), new BaseStudioSubjectContractResource(app,"contract",1),
            new BaseStudioSubjectResource(app,"contract",1,"subject"), new BaseStudioLifecycleConsumerResource(app,"consumer",1,"contract",1),
            new BaseStudioLifecycleCheckpointResource(app,"consumer",1,"contract",1,"scope"), new BaseStudioRetirementBarrierResource(app,"contract",1,"subject","AAAAAAAAAAAAAAAAAAAAAA","AAAAAAAAAAAAAAAAAAAAAA"),
            new BaseStudioTextIndexResource(app,"items","text",1), new BaseStudioVectorIndexResource(app,"items","vector",1),
            new BaseStudioSearchRebuildResource(app,"text","items","text",1,"rebuild"), new BaseStudioCertificationReceiptResource(app,"activation","provider",1,hash),
            new BaseStudioPolicyResource(app,"policy",1), new BaseStudioGrantResource(app,"grant",1), new BaseStudioStoreResource(app,"store"),
            new BaseStudioProviderResource(app,"store","provider",1), new BaseStudioSchemaResource(app,"store",0), new BaseStudioMigrationResource(app,"store","migration"),
            new BaseStudioBackupResource(app,"store","artifact"), new BaseStudioRestoreResource(app,"store","restore"),
            new BaseStudioMaintenanceResource(app,"store","compact","operation"), new BaseStudioHealthResource(app,"runtime","health"),
            new BaseStudioDiagnosticResource(app,"runtime","diagnostic"), new BaseStudioQuarantineItemResource(app,"provider","runtime","quarantine"),
            new BaseStudioGraphDefinitionResource(app,"graph","1.0.0"), new BaseStudioGraphExecutionResource(app,"graph","1.0.0","execution"),
            new BaseStudioGraphNodeResource(app,"graph","1.0.0","execution","node"), new BaseStudioGraphChannelResource(app,"graph","1.0.0","execution","channel"),
            new BaseStudioGraphCheckpointResource(app,"graph","1.0.0","execution","checkpoint"),
        ];
        Assert.Equal(Enum.GetValues<BaseStudioResourceKind>().Length, values.Length);
        foreach (BaseStudioResourceIdentity value in values)
        {
            string token = BaseStudioResourceRouteToken.Encode(value);
            Assert.True(BaseStudioResourceRouteToken.TryDecode(token, out BaseStudioResourceIdentity? decoded));
            Assert.Equal(value.Kind, decoded!.Kind); Assert.True(BaseStudioSha256.FixedTimeEquals(value.AuthorityChecksum, decoded.AuthorityChecksum));
            Assert.False(BaseStudioResourceRouteToken.TryDecode(token + "=", out _));
            char replacement = token[^1] == 'A' ? 'B' : 'A';
            Assert.False(BaseStudioResourceRouteToken.TryDecode(token[..^1] + replacement, out _));
        }
    }

    /// <summary>Proves Runtime requests reject unknown, reordered, and duplicate members before producer dispatch.</summary>
    [Fact]
    public void Runtime_value_validation_enforces_exact_l41_members()
    {
        BaseStudioNamedTypeContract text = BaseStudioNamedTypeContract.Create("test.text",
            "{\"kind\":\"string\",\"minLength\":1,\"maxLength\":16,\"format\":\"plain\"}"u8);
        BaseStudioNamedTypeContract request = BaseStudioNamedTypeContract.Create("test.request",
            "{\"kind\":\"object\",\"properties\":[{\"name\":\"first\",\"wireName\":\"first\",\"typeId\":\"test.text\",\"required\":true,\"nullable\":false,\"disclosureShape\":\"none\"},{\"name\":\"second\",\"wireName\":\"second\",\"typeId\":\"test.text\",\"required\":true,\"nullable\":false,\"disclosureShape\":\"none\"}],\"additionalProperties\":false}"u8);

        BaseStudioL41JsonValidator.Require(BaseStudioCanonicalJson.Create("{\"first\":\"a\",\"second\":\"b\"}"u8, 128), request.TypeId, [request, text]);
        Assert.Throws<ArgumentException>(() => BaseStudioL41JsonValidator.Require(
            BaseStudioCanonicalJson.Create("{\"second\":\"b\",\"first\":\"a\"}"u8, 128), request.TypeId, [request, text]));
        Assert.Throws<ArgumentException>(() => BaseStudioCanonicalJson.Create("{\"first\":\"a\",\"first\":\"b\"}"u8, 128));
    }

    /// <summary>Proves generated endpoint paths admit one embedded parameter and body authority is limit-derived.</summary>
    [Fact]
    public void Framework_operation_inventory_locks_mixed_segments_and_bodyless_posts()
    {
        BaseStudioFrameworkSurfaceOperation provision = BaseStudioFrameworkSurfaceOperation.Create(
            "gateway.target.provision", BaseStudioTransportMethod.Post, "targets/{target}:provision",
            BaseStudioTransportPurpose.CommandExecution, "gateway.target.provision", 0, 4_096, TimeSpan.FromSeconds(5),
            [], ["application/json"], ["Idempotency-Key"], ["ETag", "X-Correlation-ID"]);

        Assert.True(provision.Matches("targets/primary:provision"));
        Assert.False(provision.Matches("targets/:provision"));
        Assert.False(provision.Matches("targets/primary:activate"));
        Assert.Throws<ArgumentException>(() => BaseStudioFrameworkSurfaceOperation.Create(
            "gateway.target.bad", BaseStudioTransportMethod.Post, "targets/{target}:bad/{target}",
            BaseStudioTransportPurpose.CommandExecution, "gateway.target.bad", 0, 4_096, TimeSpan.FromSeconds(5),
            [], ["application/json"], [], []));
        Assert.Throws<ArgumentException>(() => BaseStudioFrameworkSurfaceOperation.Create(
            "gateway.target.body", BaseStudioTransportMethod.Post, "targets",
            BaseStudioTransportPurpose.CommandExecution, "gateway.target.body", 1, 4_096, TimeSpan.FromSeconds(5),
            [], ["application/json"], [], []));
    }

    /// <summary>Proves non-cooperative producer retention is bounded at exact and max-plus-one capacity.</summary>
    [Fact]
    public void Late_work_registry_is_bounded_and_releases_only_after_completion()
    {
        var registry = new BaseStudioLateWorkRegistry();
        BaseStudioLateWorkLease[] leases = Enumerable.Range(0, 32).Select(_ =>
        { Assert.True(registry.TryEnter(out BaseStudioLateWorkLease lease)); return lease; }).ToArray();
        Assert.False(registry.TryEnter(out _));
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        leases[0].Retain(completion.Task);
        for (int index = 1; index < leases.Length; index++) leases[index].Dispose();
        Assert.Equal(1, registry.OutstandingCount);
        completion.SetResult();
        Assert.True(SpinWait.SpinUntil(() => registry.OutstandingCount == 0, TimeSpan.FromSeconds(1)));
    }

    /// <summary>Proves producer bindings require exact method kind and module ownership.</summary>
    [Fact]
    public void Producer_binding_rejects_kind_substitution()
    {
        BaseStudioModuleRegistration module = new HostingTestStudioContribution().Create(
            new ServiceCollection().AddSingleton(BaseStudioShellContract.Current).BuildServiceProvider());
        BaseStudioNamedTypeContract request = BaseStudioNamedTypeContract.Create("request", "{\"kind\":\"object\",\"properties\":[],\"additionalProperties\":false}"u8);
        BaseStudioNamedTypeContract result = BaseStudioNamedTypeContract.Create("result", "{\"kind\":\"string\",\"minLength\":1,\"maxLength\":64,\"format\":\"plain\"}"u8);
        BaseStudioNamedTypeContract error = BaseStudioNamedTypeContract.Create("error", "{\"kind\":\"string\",\"minLength\":1,\"maxLength\":64,\"format\":\"plain\"}"u8);
        BaseStudioEndpointContract endpoint = BaseStudioEndpointContract.Create("base.page", 1, BaseStudioTransportMethod.Post, "/base/studio/resources/page",
            BaseStudioEndpointAudience.ControlPlane, BaseStudioTransportKind.SameOriginHttp, request.TypeId, request.NodeChecksum,
            result.TypeId, result.NodeChecksum, error.TypeId, error.NodeChecksum, 1024, 1024, TimeSpan.FromSeconds(1));
        string owner = module.Pages[0].PageId;
        BaseStudioMethodBinding method = BaseStudioMethodBinding.Create("base.page.read", BaseStudioMethodKind.Page,
            module.Identity.ModuleId, owner, endpoint.EndpointId, request.TypeId, result.TypeId);

        Assert.Throws<ArgumentException>(() => BaseStudioModuleRuntimeContribution.Create(module,
            [error, request, result], [endpoint], [method], [new BaseStudioResourceProducerBinding(method.RegisteredMethodId, new ResourceProducer())]));
        BaseStudioModuleRuntimeContribution contribution = BaseStudioModuleRuntimeContribution.Create(module,
            [error, request, result], [endpoint], [method], [new BaseStudioViewProducerBinding(method.RegisteredMethodId, new ViewProducer())]);
        Assert.Single(contribution.Producers);
    }

    private sealed class ViewProducer : IBaseStudioViewProducer
    {
        public ValueTask<BaseStudioCanonicalJson?> ReadAsync(BaseStudioProducerInvocation invocation, CancellationToken cancellationToken)
            => ValueTask.FromResult<BaseStudioCanonicalJson?>(BaseStudioCanonicalJson.Create("\"ok\""u8, 64));
    }
    private sealed class ResourceProducer : IBaseStudioResourceProducer
    {
        public ValueTask<BaseStudioCanonicalJson?> ResolveAsync(BaseStudioProducerInvocation invocation, CancellationToken cancellationToken)
            => ValueTask.FromResult<BaseStudioCanonicalJson?>(null);
    }
}
