using System.Text.Json;
using FluentAssertions;
using HPD.Base.Tests.Application.Generation;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace HPD.Base.Tests.Application.Selection;

public sealed class L43SelectionMutationTests
{
    [Fact]
    public async Task InMemorySelectionPatchIsAtomicAndTyped()
    {
        await using ServiceProvider provider = Build();
        (await provider.GetRequiredService<IHPDBaseApplication>().InitializeAsync()).IsSuccess().Should().BeTrue();
        var session = provider.GetRequiredService<IBaseSessionFactory>().For(Admin());
        BaseCollectionSession<GeneratedProject> collection = session.Collection(GeneratedProject.Collection);
        await collection.CreateAsync(new RecordId("one"), new GeneratedProject { OrganizationId = "org", Name = "a" });
        await collection.CreateAsync(new RecordId("two"), new GeneratedProject { OrganizationId = "org", Name = "b" });
        BaseMergePatchSelectionProfile<GeneratedProject> profile = collection.GetMergePatchSelectionProfile(PatchIdentity());

        BaseResult<BaseSelectionMutationResult> result = await collection.Query()
            .Where(GeneratedProject.Fields.OrganizationId.Equal("org"))
            .OrderBy(GeneratedProject.Fields.Name).ThenByRecordId().Take(2)
            .PatchSelectedAsync(profile, Patch("claimed"), BasePreviousStateRequirement.None);

        result.Should().BeOfType<BaseSuccess<BaseSelectionMutationResult>>(
            result is BaseFailure<BaseSelectionMutationResult> failed ? failed.Error.Code : string.Empty);
        result.RequireValue().MutatedCount.Should().Be(2);
        (await collection.Query().Where(GeneratedProject.Fields.Name.Equal("claimed")).Take(10).ToArrayAsync(10))
            .RequireValue().Should().HaveCount(2);
    }

    [Fact]
    public async Task IdentifiedZeroSelectionReplaysWithoutSelectingLaterInsert()
    {
        await using ServiceProvider provider = Build();
        (await provider.GetRequiredService<IHPDBaseApplication>().InitializeAsync()).IsSuccess().Should().BeTrue();
        BaseCollectionSession<GeneratedProject> collection = provider.GetRequiredService<IBaseSessionFactory>().For(Admin()).Collection(GeneratedProject.Collection);
        BaseDeleteSelectionProfile<GeneratedProject> profile = collection.GetDeleteSelectionProfile(DeleteIdentity());
        BaseMutationRequestIdentity identity = Identity("zero");
        BaseQuery<GeneratedProject> query = collection.Query()
            .Where(GeneratedProject.Fields.OrganizationId.Equal("later"))
            .OrderBy(GeneratedProject.Fields.Name).ThenByRecordId().Take(1);

        BaseResult<BaseSelectionMutationResult> first = await query.DeleteSelectedAsync(profile, BasePreviousStateRequirement.None, identity);
        first.Should().BeOfType<BaseSuccess<BaseSelectionMutationResult>>(
            first is BaseFailure<BaseSelectionMutationResult> failed ? failed.Error.Code : string.Empty);
        first.RequireValue().SelectedCount.Should().Be(0);
        await collection.CreateAsync(new RecordId("later"), new GeneratedProject { OrganizationId = "later", Name = "later" });
        BaseSelectionMutationResult duplicate = (await query.DeleteSelectedAsync(profile, BasePreviousStateRequirement.None, identity)).RequireValue();

        duplicate.RequestDisposition.Should().Be(BaseMutationRequestDisposition.Duplicate);
        duplicate.SelectedCount.Should().Be(0);
        (await collection.GetAsync(new RecordId("later"))).TryGetValue(out _).Should().BeTrue();
    }

    [Fact]
    public async Task ReceiptReplayIsBoundToTheOriginalTenantScope()
    {
        await using ServiceProvider provider = Build();
        (await provider.GetRequiredService<IHPDBaseApplication>().InitializeAsync()).IsSuccess().Should().BeTrue();
        IBaseSessionFactory sessions = provider.GetRequiredService<IBaseSessionFactory>();
        BaseCollectionSession<GeneratedProject> owner = sessions.For(Admin("tenant-a")).Collection(GeneratedProject.Collection);
        BaseDeleteSelectionProfile<GeneratedProject> profile = owner.GetDeleteSelectionProfile(DeleteIdentity());
        BaseMutationRequestIdentity identity = Identity("tenant-bound");
        BaseQuery<GeneratedProject> query = owner.Query().Where(GeneratedProject.Fields.OrganizationId.Equal("none"))
            .OrderBy(GeneratedProject.Fields.Name).ThenByRecordId().Take(1);
        (await query.DeleteSelectedAsync(profile, BasePreviousStateRequirement.None, identity)).RequireValue();

        BaseCollectionSession<GeneratedProject> other = sessions.For(Admin("tenant-b")).Collection(GeneratedProject.Collection);
        BaseResult<BaseSelectionMutationResult> replay = await other.Query().Where(GeneratedProject.Fields.OrganizationId.Equal("none"))
            .OrderBy(GeneratedProject.Fields.Name).ThenByRecordId().Take(1)
            .DeleteSelectedAsync(other.GetDeleteSelectionProfile(DeleteIdentity()), BasePreviousStateRequirement.None, identity);

        replay.Should().BeOfType<BaseFailure<BaseSelectionMutationResult>>();
    }

    [Fact]
    public void OwnedSelectedRecordDefensivelyCopiesNestedPayload()
    {
        var fields = new Dictionary<string, JsonElement> { ["name"] = JsonSerializer.SerializeToElement(new[] { "a", "b" }) };
        var envelope = new RecordEnvelope
        {
            CollectionId = "projects", Id = new RecordId("one"),
            Payload = new RecordPayload { Kind = RecordPayloadKind.FieldMap, Fields = fields },
            Metadata = new RecordMetadata { Revision = new RevisionToken("mem:1") },
        };
        BaseOwnedSelectedRecord owned = BaseOwnedSelectedRecord.Freeze(envelope, 0, 1);
        fields["name"] = JsonSerializer.SerializeToElement("changed");
        owned.MaterializeOwned().Payload.Fields!["name"].ValueKind.Should().Be(JsonValueKind.Array);
        owned.CopyCanonicalBytes().Should().NotBeSameAs(owned.CopyCanonicalBytes());
    }

    [Fact]
    public async Task PreviousStateIsNormalizedAndRollsBackTheCompleteSelection()
    {
        await using ServiceProvider provider = Build();
        (await provider.GetRequiredService<IHPDBaseApplication>().InitializeAsync()).IsSuccess().Should().BeTrue();
        BaseCollectionSession<GeneratedProject> collection = provider.GetRequiredService<IBaseSessionFactory>().For(Admin()).Collection(GeneratedProject.Collection);
        await collection.CreateAsync(new RecordId("one"), new GeneratedProject { OrganizationId = "org", Name = "ready" });
        await collection.CreateAsync(new RecordId("two"), new GeneratedProject { OrganizationId = "org", Name = "blocked" });

        BaseResult<BaseSelectionMutationResult> result = await collection.Query()
            .Where(GeneratedProject.Fields.OrganizationId.Equal("org"))
            .OrderBy(GeneratedProject.Fields.Name).ThenByRecordId().Take(2)
            .PatchSelectedAsync(collection.GetMergePatchSelectionProfile(PatchIdentity()), Patch("claimed"), new BasePreviousStateRequirement
            {
                Revision = new BaseRevisionRequirement { Kind = BaseRevisionRequirementKind.None },
                Fields = [new BasePreviousFieldRequirement
                {
                    FieldId = GeneratedProject.Fields.Name.Id,
                    Kind = BasePreviousFieldRequirementKind.Equal,
                    Value = new QueryValue { Kind = QueryValueKind.String, String = "ready" },
                }],
            });

        result.Should().BeOfType<BaseFailure<BaseSelectionMutationResult>>().Which.Error.Code.Should().Be(BaseSelectionErrorCodes.TransactionConflict);
        (await collection.Query().Where(GeneratedProject.Fields.Name.Equal("claimed")).Take(10).ToArrayAsync(10)).RequireValue().Should().BeEmpty();
    }

    [Fact]
    public async Task SelectionRejectsOrderWithoutFinalRecordIdentityBeforeProviderInfluence()
    {
        await using ServiceProvider provider = Build();
        (await provider.GetRequiredService<IHPDBaseApplication>().InitializeAsync()).IsSuccess().Should().BeTrue();
        BaseCollectionSession<GeneratedProject> collection = provider.GetRequiredService<IBaseSessionFactory>().For(Admin()).Collection(GeneratedProject.Collection);
        BaseResult<BaseSelectionMutationResult> result = await collection.Query()
            .Where(GeneratedProject.Fields.OrganizationId.Equal("org"))
            .OrderBy(GeneratedProject.Fields.Name).Take(1)
            .DeleteSelectedAsync(collection.GetDeleteSelectionProfile(DeleteIdentity()), BasePreviousStateRequirement.None);

        result.Should().BeOfType<BaseFailure<BaseSelectionMutationResult>>()
            .Which.Error.Code.Should().Be(BaseSelectionErrorCodes.ContractInvalid);
    }

    [Fact]
    public void GeneratedProfileIdentityRejectsAnotherModuleCollection()
    {
        BaseSelectionOperationProfile profile = Profile("claim", BaseSelectionMutationKind.MergePatch);
        Action action = () => BaseGeneratedSelectionProfiles.RegisterSelectionProfile(
            BaseGeneratedModules.RegisterCollectionModule(profile.ApplicationId, "another-collection"),
            new BaseGeneratedSelectionProfileDescriptor
            {
                ApplicationId = profile.ApplicationId, CollectionId = profile.CollectionId,
                ProfileId = profile.Id, Version = profile.Version, Kind = profile.MutationKind,
                Checksum = BaseSelectionProfileChecksum.Compute(profile),
            });
        action.Should().Throw<InvalidOperationException>().WithMessage(BaseSelectionErrorCodes.ProfileInvalid);
    }

    private static ServiceProvider Build()
    {
        var services = new ServiceCollection().AddLogging();
        services.AddHPDBase(builder => builder
            .ConfigureSchema(options => options.ApplicationId = "hpd.base.application")
            .ConfigureSelectionMutations(new HPDBaseSelectionMutationOptions
            {
                HostMaxima = Limits(), MaximumReceiptIdentityBytes = 512,
                MaximumEvidenceTokenBytes = 512, MaximumRouteNameBytes = 96,
                MaximumRequestBodyBytes = 1_048_576,
            })
            .ReplacePolicyEvaluator<AllowAll>()
            .AddCollection(GeneratedProject.Collection)
            .AddSelectionOperationProfile(Profile("claim", BaseSelectionMutationKind.MergePatch))
            .AddSelectionOperationProfile(Profile("remove", BaseSelectionMutationKind.Delete)));
        return services.BuildServiceProvider();
    }

    private static BaseSelectionOperationProfile Profile(string id, BaseSelectionMutationKind kind) => new()
    {
        Id = id, Version = 1, ApplicationId = "hpd.base.application", CollectionId = "projects",
        RequiredGrantId = "projects.selection", MutationKind = kind, Limits = Limits(),
    };
    private static BaseSelectionOperationLimits Limits() => new()
    {
        MaximumQueryNodes = 32, MaximumQueryDepth = 8, MaximumLiteralValues = 64,
        MaximumSelectedRecords = 10, MaximumSelectedBytes = 1_000_000,
        MaximumProducedMutations = 10, MaximumQueryExecutions = 1, MaximumReadIntervals = 10,
        MaximumWrittenBytes = 1_000_000, MaximumFactBytes = 1_000_000, MaximumJournalBytes = 1_000_000,
        MaximumReceiptBytes = 1_000_000, MaximumRelationChecks = 100, MaximumUniqueConstraintChecks = 100,
        MaximumPreviousStateRequirements = 10, MaximumTransientBytes = 2_000_000, MaximumResultBytes = 100_000,
        AcquisitionTimeout = TimeSpan.FromSeconds(5), ExecutionTimeout = TimeSpan.FromSeconds(5),
        CallerCommitObservationTimeout = TimeSpan.FromSeconds(5),
    };
    private static BaseGeneratedSelectionProfileIdentity PatchIdentity()
    {
        BaseSelectionOperationProfile profile = Profile("claim", BaseSelectionMutationKind.MergePatch);
        return Identity(profile);
    }
    private static BaseGeneratedSelectionProfileIdentity DeleteIdentity()
    {
        BaseSelectionOperationProfile profile = Profile("remove", BaseSelectionMutationKind.Delete);
        return Identity(profile);
    }
    private static BaseGeneratedSelectionProfileIdentity Identity(BaseSelectionOperationProfile profile) =>
        BaseGeneratedSelectionProfiles.RegisterSelectionProfile(
            BaseGeneratedModules.RegisterCollectionModule(profile.ApplicationId, profile.CollectionId),
            new BaseGeneratedSelectionProfileDescriptor
            {
                ApplicationId = profile.ApplicationId, CollectionId = profile.CollectionId,
                ProfileId = profile.Id, Version = profile.Version, Kind = profile.MutationKind,
                Checksum = BaseSelectionProfileChecksum.Compute(profile),
            });
    private static RecordPatchRequest Patch(string name) => new()
    {
        Patch = new RecordPayload { Kind = RecordPayloadKind.FieldMap, Fields = new Dictionary<string, JsonElement> { ["name"] = JsonSerializer.SerializeToElement(name) } },
    };
    private static BaseMutationRequestIdentity Identity(string key) => new()
    {
        Scope = "tests", Operation = "selection", IdempotencyKey = key,
        Fingerprint = BaseMutationRequestFingerprint.Create(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(key))),
    };
    private static PrincipalContext Admin(string? tenant = null) => new() { AuthenticationState = PrincipalAuthenticationState.Admin, SubjectKind = AccessSubjectKind.User, SubjectId = "admin", CurrentTenantId = tenant };
    private sealed class AllowAll : IPolicyEvaluator
    {
        public ValueTask<PolicyDecision> EvaluateAsync(PolicyEvaluationRequest request, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(new PolicyDecision
            {
                Effect = PolicyEffect.Allow,
                Outcome = PolicyOutcome.Allowed,
                Audit = new PolicyAuditInfo { MatchedGrantIds = ["projects.selection"] },
            });
    }
}
