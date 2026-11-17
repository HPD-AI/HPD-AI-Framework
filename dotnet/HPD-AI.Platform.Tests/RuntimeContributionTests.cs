using HPD.AI.Platform.Studio;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace HPD.AI.Platform.Tests;

/// <summary>Verifies the closed module-owned Runtime producer seam.</summary>
public sealed class StudioRuntimeContributionTests
{
    /// <summary>Proves outward resource identities are closed, purpose-bound, and kind-specific.</summary>
    [Fact]
    public void Outward_resource_authority_is_typed_and_checksums_all_identity_members()
    {
        StudioSha256 collection = StudioSha256.FromDigest(new byte[32]);
        var first = new StudioRecordResource("sample.application", "users", collection, "42");
        var second = new StudioRecordResource("sample.application", "users", collection, "43");

        Assert.Equal(StudioResourceKind.Record, first.Kind);
        Assert.Equal("users", first.CollectionId);
        Assert.Equal("42", first.RecordId);
        Assert.False(StudioSha256.FixedTimeEquals(first.AuthorityChecksum, second.AuthorityChecksum));
        Assert.Throws<ArgumentException>(() => new StudioApplicationResource("wrong\napplication"));
    }

    /// <summary>Proves every closed resource discriminator round-trips and tampering fails closed.</summary>
    [Fact]
    public void Every_resource_route_token_round_trips_and_rejects_tampering()
    {
        StudioSha256 hash = StudioSha256.FromDigest(new byte[32]); const string app = "sample.application";
        StudioResourceIdentity[] values =
        [
            new StudioApplicationResource(app), new StudioModuleResource(app,"module",1), new StudioCollectionResource(app,"items",hash),
            new StudioRecordResource(app,"items",hash,"record"), new StudioRelationResource(app,"items","one","edge","other","two"),
            new StudioFileBucketResource(app,"bucket"), new StudioFileResource(app,"bucket","object"), new StudioRegisteredReadResource(app,"read",1),
            new StudioSelectionOperationResource(app,"profile",1), new StudioModuleMutationResource(app,"operation",1),
            new StudioOperationExecutionResource(app,"mutation","operation","request"), new StudioReceiptResource(app,"mutation","operation","request"),
            new StudioActivationDefinitionResource(app,"definition",1), new StudioActivationResource(app,"definition",1,"activation"),
            new StudioScheduleResource(app,"schedule",1), new StudioOccurrenceResource(app,"schedule",1,"occurrence"),
            new StudioActivationAttemptResource(app,"activation",1), new StudioEffectResource(app,"activation",1,"effect"),
            new StudioExecutorResource(app,"host","process",1), new StudioSubjectContractResource(app,"contract",1),
            new StudioSubjectResource(app,"contract",1,"subject"), new StudioLifecycleConsumerResource(app,"consumer",1,"contract",1),
            new StudioLifecycleCheckpointResource(app,"consumer",1,"contract",1,"scope"), new StudioRetirementBarrierResource(app,"contract",1,"subject","AAAAAAAAAAAAAAAAAAAAAA","AAAAAAAAAAAAAAAAAAAAAA"),
            new StudioTextIndexResource(app,"items","text",1), new StudioVectorIndexResource(app,"items","vector",1),
            new StudioSearchRebuildResource(app,"text","items","text",1,"rebuild"), new StudioCertificationReceiptResource(app,"activation","provider",1,hash),
            new StudioPolicyResource(app,"policy",1), new StudioGrantResource(app,"grant",1), new StudioStoreResource(app,"store"),
            new StudioProviderResource(app,"store","provider",1), new StudioSchemaResource(app,"store",0), new StudioMigrationResource(app,"store","migration"),
            new StudioBackupResource(app,"store","artifact"), new StudioRestoreResource(app,"store","restore"),
            new StudioMaintenanceResource(app,"store","compact","operation"), new StudioHealthResource(app,"runtime","health"),
            new StudioDiagnosticResource(app,"runtime","diagnostic"), new StudioQuarantineItemResource(app,"provider","runtime","quarantine"),
            new StudioGraphDefinitionResource(app,"graph","1.0.0"), new StudioGraphExecutionResource(app,"graph","1.0.0","execution"),
            new StudioGraphNodeResource(app,"graph","1.0.0","execution","node"), new StudioGraphChannelResource(app,"graph","1.0.0","execution","channel"),
            new StudioGraphCheckpointResource(app,"graph","1.0.0","execution","checkpoint"),
            new StudioAgentResource(app,"agent"), new StudioAgentSessionResource(app,"session"),
            new StudioAgentThreadResource(app,"session","thread"), new StudioAgentExecutionResource(app,"session","thread","execution"),
            new StudioToolHarnessResource(app,"harness"), new StudioAgentTraceResource(app,"trace"),
            new StudioAIFunctionResource(app,"harness","search"), new StudioAIFunctionActionResource(app,"harness","search","order"),
        ];
        Assert.Equal(Enum.GetValues<StudioResourceKind>().Length, values.Length);
        foreach (StudioResourceIdentity value in values)
        {
            string token = StudioResourceRouteToken.Encode(value);
            Assert.True(StudioResourceRouteToken.TryDecode(token, out StudioResourceIdentity? decoded));
            Assert.Equal(value.Kind, decoded!.Kind); Assert.True(StudioSha256.FixedTimeEquals(value.AuthorityChecksum, decoded.AuthorityChecksum));
            Assert.False(StudioResourceRouteToken.TryDecode(token + "=", out _));
            char replacement = token[^1] == 'A' ? 'B' : 'A';
            Assert.False(StudioResourceRouteToken.TryDecode(token[..^1] + replacement, out _));
        }
    }

    /// <summary>Proves Runtime requests reject unknown, reordered, and duplicate members before producer dispatch.</summary>
    [Fact]
    public void Runtime_value_validation_enforces_exact_l41_members()
    {
        StudioNamedTypeContract text = StudioNamedTypeContract.Create("test.text",
            "{\"kind\":\"string\",\"minLength\":1,\"maxLength\":16,\"format\":\"plain\"}"u8);
        StudioNamedTypeContract request = StudioNamedTypeContract.Create("test.request",
            "{\"kind\":\"object\",\"properties\":[{\"name\":\"first\",\"wireName\":\"first\",\"typeId\":\"test.text\",\"required\":true,\"nullable\":false,\"disclosureShape\":\"none\"},{\"name\":\"second\",\"wireName\":\"second\",\"typeId\":\"test.text\",\"required\":true,\"nullable\":false,\"disclosureShape\":\"none\"}],\"additionalProperties\":false}"u8);

        StudioL41JsonValidator.Require(StudioCanonicalJson.Create("{\"first\":\"a\",\"second\":\"b\"}"u8, 128), request.TypeId, [request, text]);
        Assert.Throws<ArgumentException>(() => StudioL41JsonValidator.Require(
            StudioCanonicalJson.Create("{\"second\":\"b\",\"first\":\"a\"}"u8, 128), request.TypeId, [request, text]));
        Assert.Throws<ArgumentException>(() => StudioCanonicalJson.Create("{\"first\":\"a\",\"first\":\"b\"}"u8, 128));
    }

    /// <summary>Proves generated endpoint paths admit one embedded parameter and body authority is limit-derived.</summary>
    [Fact]
    public void Framework_operation_inventory_locks_mixed_segments_and_bodyless_posts()
    {
        StudioFrameworkSurfaceOperation provision = StudioFrameworkSurfaceOperation.Create(
            "gateway.target.provision", StudioTransportMethod.Post, "targets/{target}:provision",
            StudioTransportPurpose.CommandExecution, "gateway.target.provision", 0, 4_096, TimeSpan.FromSeconds(5),
            [], ["application/json"], ["Idempotency-Key"], ["ETag", "X-Correlation-ID"]);

        Assert.True(provision.Matches("targets/primary:provision"));
        Assert.False(provision.Matches("targets/:provision"));
        Assert.False(provision.Matches("targets/primary:activate"));
        Assert.Throws<ArgumentException>(() => StudioFrameworkSurfaceOperation.Create(
            "gateway.target.bad", StudioTransportMethod.Post, "targets/{target}:bad/{target}",
            StudioTransportPurpose.CommandExecution, "gateway.target.bad", 0, 4_096, TimeSpan.FromSeconds(5),
            [], ["application/json"], [], []));
        Assert.Throws<ArgumentException>(() => StudioFrameworkSurfaceOperation.Create(
            "gateway.target.body", StudioTransportMethod.Post, "targets",
            StudioTransportPurpose.CommandExecution, "gateway.target.body", 1, 4_096, TimeSpan.FromSeconds(5),
            [], ["application/json"], [], []));
    }

    /// <summary>Proves non-cooperative producer retention is bounded at exact and max-plus-one capacity.</summary>
    [Fact]
    public void Late_work_registry_is_bounded_and_releases_only_after_completion()
    {
        var registry = new StudioLateWorkRegistry();
        StudioLateWorkLease[] leases = Enumerable.Range(0, 32).Select(_ =>
        { Assert.True(registry.TryEnter(out StudioLateWorkLease lease)); return lease; }).ToArray();
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
        StudioModuleRegistration module = new HostingTestStudioContribution().Create(
            new ServiceCollection().AddSingleton(StudioShellContract.Current).BuildServiceProvider());
        StudioNamedTypeContract request = StudioNamedTypeContract.Create("request", "{\"kind\":\"object\",\"properties\":[],\"additionalProperties\":false}"u8);
        StudioNamedTypeContract result = StudioNamedTypeContract.Create("result", "{\"kind\":\"string\",\"minLength\":1,\"maxLength\":64,\"format\":\"plain\"}"u8);
        StudioNamedTypeContract error = StudioNamedTypeContract.Create("error", "{\"kind\":\"string\",\"minLength\":1,\"maxLength\":64,\"format\":\"plain\"}"u8);
        StudioEndpointContract endpoint = StudioEndpointContract.Create("base.page", 1, StudioTransportMethod.Post, "/base/studio/resources/page",
            StudioEndpointAudience.ControlPlane, StudioTransportKind.SameOriginHttp, request.TypeId, request.NodeChecksum,
            result.TypeId, result.NodeChecksum, error.TypeId, error.NodeChecksum, 1024, 1024, TimeSpan.FromSeconds(1));
        string owner = module.Pages[0].PageId;
        StudioMethodBinding method = StudioMethodBinding.Create("base.page.read", StudioMethodKind.Page,
            module.Identity.ModuleId, owner, endpoint.EndpointId, request.TypeId, result.TypeId);

        Assert.Throws<ArgumentException>(() => StudioModuleRuntimeContribution.Create(module,
            [error, request, result], [endpoint], [method], [new StudioResourceProducerBinding(method.RegisteredMethodId, new ResourceProducer())]));
        StudioModuleRuntimeContribution contribution = StudioModuleRuntimeContribution.Create(module,
            [error, request, result], [endpoint], [method], [new StudioViewProducerBinding(method.RegisteredMethodId, new ViewProducer())]);
        Assert.Single(contribution.Producers);
    }

    private sealed class ViewProducer : IStudioViewProducer
    {
        public ValueTask<StudioCanonicalJson?> ReadAsync(StudioProducerInvocation invocation, CancellationToken cancellationToken)
            => ValueTask.FromResult<StudioCanonicalJson?>(StudioCanonicalJson.Create("\"ok\""u8, 64));
    }
    private sealed class ResourceProducer : IStudioResourceProducer
    {
        public ValueTask<StudioCanonicalJson?> ResolveAsync(StudioProducerInvocation invocation, CancellationToken cancellationToken)
            => ValueTask.FromResult<StudioCanonicalJson?>(null);
    }
}
