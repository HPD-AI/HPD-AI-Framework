using System.Text.Json;
using System.Collections.Immutable;
using FluentAssertions;
using HPD.Base.Tests.Application.Generation;
using Microsoft.Extensions.DependencyInjection;
using System.Text.Json.Serialization;
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
        BaseResult<BaseRecord<GeneratedProject>> createdOne = await collection.CreateAsync(RecordId.Create("one"), new GeneratedProject { OrganizationId = "org", Name = "a" });
        BaseResult<BaseRecord<GeneratedProject>> createdTwo = await collection.CreateAsync(RecordId.Create("two"), new GeneratedProject { OrganizationId = "org", Name = "b" });
        createdOne.Should().BeOfType<BaseSuccess<BaseRecord<GeneratedProject>>>(createdOne is BaseFailure<BaseRecord<GeneratedProject>> firstFailure ? firstFailure.Error.Code : string.Empty);
        createdTwo.Should().BeOfType<BaseSuccess<BaseRecord<GeneratedProject>>>(createdTwo is BaseFailure<BaseRecord<GeneratedProject>> secondFailure ? secondFailure.Error.Code : string.Empty);
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
    public async Task SqliteSelectionPatchExecutesInsideOneAuthoritativeTransaction()
    {
        string path = Path.Combine(Path.GetTempPath(), $"hpd-l43-{Guid.NewGuid():N}.db");
        try
        {
            await using ServiceProvider provider = Build(path);
            IBaseSchemaManager schemas = provider.GetRequiredService<IBaseSchemaManager>();
            OperationResult<BaseSchemaPlan> planned = await schemas.PlanAsync(new BaseSchemaPlanRequest { StoreId = "sqlite-l43" });
            planned.IsSuccess().Should().BeTrue();
            BaseSchemaPlan plan = planned.Value!;
            (await schemas.ApplyAsync(new BaseSchemaApplyRequest { ProtectedArtifact = plan.ProtectedArtifact })).IsSuccess().Should().BeTrue();
            (await provider.GetRequiredService<IHPDBaseApplication>().InitializeAsync()).IsSuccess().Should().BeTrue();
            BaseCollectionSession<GeneratedProject> collection = provider.GetRequiredService<IBaseSessionFactory>().For(Admin()).Collection(GeneratedProject.Collection);
            await collection.CreateAsync(RecordId.Create("sqlite-one"), new GeneratedProject { OrganizationId = "org", Name = "ready" });
            BaseResult<BaseSelectionMutationResult> selected = await collection.Query().Where(GeneratedProject.Fields.OrganizationId.Equal("org"))
                .OrderBy(GeneratedProject.Fields.Name).ThenByRecordId().Take(1)
                .PatchSelectedAsync(collection.GetMergePatchSelectionProfile(PatchIdentity()), Patch("sqlite-claimed"), BasePreviousStateRequirement.None);
            selected.Should().BeOfType<BaseSuccess<BaseSelectionMutationResult>>(
                selected is BaseFailure<BaseSelectionMutationResult> failure ? failure.Error.Code : string.Empty);
            BaseSelectionMutationResult result = selected.RequireValue();
            result.MutatedCount.Should().Be(1);
            (await collection.GetAsync(RecordId.Create("sqlite-one"))).RequireValue().Value.Name.Should().Be("sqlite-claimed");
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }

    [Fact]
    public async Task SqliteDeleteSelectionOwnsPartialAndZeroCohortsWithoutCapacityAssumptions()
    {
        string path = Path.Combine(Path.GetTempPath(), $"hpd-l43-partial-{Guid.NewGuid():N}.db");
        try
        {
            await using ServiceProvider provider = Build(path);
            IBaseSchemaManager schemas = provider.GetRequiredService<IBaseSchemaManager>();
            BaseSchemaPlan plan = (await schemas.PlanAsync(new BaseSchemaPlanRequest { StoreId = "sqlite-l43" })).Value!;
            (await schemas.ApplyAsync(new BaseSchemaApplyRequest { ProtectedArtifact = plan.ProtectedArtifact })).IsSuccess().Should().BeTrue();
            (await provider.GetRequiredService<IHPDBaseApplication>().InitializeAsync()).IsSuccess().Should().BeTrue();
            BaseCollectionSession<GeneratedProject> collection = provider.GetRequiredService<IBaseSessionFactory>()
                .For(Admin()).Collection(GeneratedProject.Collection);
            for (int index = 0; index < 2; index++)
                (await collection.CreateAsync(RecordId.Create($"partial-{index}"),
                    new GeneratedProject { OrganizationId = "partial", Name = $"item-{index}" })).RequireValue();
            BaseDeleteSelectionProfile<GeneratedProject> profile = collection.GetDeleteSelectionProfile(DeleteIdentity());
            BaseSelectionMutationResult partial = (await collection.Query()
                .Where(GeneratedProject.Fields.OrganizationId.Equal("partial"))
                .OrderBy(GeneratedProject.Fields.Name).ThenByRecordId().Take(8)
                .DeleteSelectedAsync(profile, BasePreviousStateRequirement.None, Identity("partial"))).RequireValue();
            partial.SelectedCount.Should().Be(2);
            partial.MutatedCount.Should().Be(2);

            BaseSelectionMutationResult zero = (await collection.Query()
                .Where(GeneratedProject.Fields.OrganizationId.Equal("absent"))
                .OrderBy(GeneratedProject.Fields.Name).ThenByRecordId().Take(8)
                .DeleteSelectedAsync(profile, BasePreviousStateRequirement.None, Identity("zero-sqlite"))).RequireValue();
            zero.SelectedCount.Should().Be(0);
            zero.MutatedCount.Should().Be(0);
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }

    [Fact]
    public async Task ConcurrentSqliteSelectionsRemainSerializable()
    {
        string path = Path.Combine(Path.GetTempPath(), $"hpd-l43-concurrent-{Guid.NewGuid():N}.db");
        try
        {
            await using ServiceProvider provider = Build(path);
            IBaseSchemaManager schemas = provider.GetRequiredService<IBaseSchemaManager>();
            OperationResult<BaseSchemaPlan> planned = await schemas.PlanAsync(new BaseSchemaPlanRequest { StoreId = "sqlite-l43" });
            planned.IsSuccess().Should().BeTrue();
            (await schemas.ApplyAsync(new BaseSchemaApplyRequest { ProtectedArtifact = planned.Value!.ProtectedArtifact })).IsSuccess().Should().BeTrue();
            (await provider.GetRequiredService<IHPDBaseApplication>().InitializeAsync()).IsSuccess().Should().BeTrue();
            BaseCollectionSession<GeneratedProject> collection = provider.GetRequiredService<IBaseSessionFactory>().For(Admin()).Collection(GeneratedProject.Collection);
            await collection.CreateAsync(RecordId.Create("contended"), new GeneratedProject { OrganizationId = "org", Name = "ready" });
            BaseMergePatchSelectionProfile<GeneratedProject> profile = collection.GetMergePatchSelectionProfile(PatchIdentity());
            BasePreviousStateRequirement previous = new()
            {
                Revision = new BaseRevisionRequirement { Kind = BaseRevisionRequirementKind.None },
                Fields = [new BasePreviousFieldRequirement { FieldId = "name", Kind = BasePreviousFieldRequirementKind.Equal, Value = new QueryValue { Kind = QueryValueKind.String, String = "ready" } }],
            };
            Task<BaseResult<BaseSelectionMutationResult>> First() => collection.Query().Where(GeneratedProject.Fields.OrganizationId.Equal("org"))
                .OrderBy(GeneratedProject.Fields.Name).ThenByRecordId().Take(1).PatchSelectedAsync(profile, Patch("first"), previous).AsTask();
            Task<BaseResult<BaseSelectionMutationResult>> Second() => collection.Query().Where(GeneratedProject.Fields.OrganizationId.Equal("org"))
                .OrderBy(GeneratedProject.Fields.Name).ThenByRecordId().Take(1).PatchSelectedAsync(profile, Patch("second"), previous).AsTask();

            BaseResult<BaseSelectionMutationResult>[] results = await Task.WhenAll(First(), Second());
            results.Count(result => result is BaseSuccess<BaseSelectionMutationResult>).Should().Be(1);
            results.Count(result => result is BaseFailure<BaseSelectionMutationResult>).Should().Be(1);
            (await collection.GetAsync(RecordId.Create("contended"))).RequireValue().Value.Name.Should().BeOneOf("first", "second");
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SqliteConstraintAttributionUsesTheStableNonEnumeratingL54Failure(bool ambiguous)
    {
        string path = Path.Combine(Path.GetTempPath(), $"hpd-l43-constraint-{Guid.NewGuid():N}.db");
        try
        {
            await using ServiceProvider provider = Build(path);
            IBaseSchemaManager schemas = provider.GetRequiredService<IBaseSchemaManager>();
            OperationResult<BaseSchemaPlan> planned = await schemas.PlanAsync(new BaseSchemaPlanRequest { StoreId = "sqlite-l43" });
            (await schemas.ApplyAsync(new BaseSchemaApplyRequest { ProtectedArtifact = planned.Value!.ProtectedArtifact })).IsSuccess().Should().BeTrue();
            (await provider.GetRequiredService<IHPDBaseApplication>().InitializeAsync()).IsSuccess().Should().BeTrue();
            BaseCollectionSession<L43UniqueItem> collection = provider.GetRequiredService<IBaseSessionFactory>().For(Admin()).Collection(L43UniqueItem.Collection);
            (await collection.CreateAsync(RecordId.Create("existing"), new L43UniqueItem { Group = "keep", Name = "taken", Code = "used" })).RequireValue();
            (await collection.CreateAsync(RecordId.Create("selected"), new L43UniqueItem { Group = "change", Name = "free", Code = "unused" })).RequireValue();
            BaseSelectionOperationProfile installed = Profile("unique-patch", BaseSelectionMutationKind.MergePatch) with { CollectionId = "l43-unique" };
            BaseMergePatchSelectionProfile<L43UniqueItem> profile = collection.GetMergePatchSelectionProfile(Identity(installed));
            string nameWire = L43UniqueItem.Collection.Definition.Fields!.Single(field => field.Id == "unique-name").WireName;
            string codeWire = L43UniqueItem.Collection.Definition.Fields!.Single(field => field.Id == "unique-code").WireName;
            Dictionary<string, JsonElement> fields = new(StringComparer.Ordinal) { [nameWire] = JsonSerializer.SerializeToElement("taken") };
            if (ambiguous) fields[codeWire] = JsonSerializer.SerializeToElement("used");
            BaseResult<BaseSelectionMutationResult> result = await collection.Query().Where(L43UniqueItem.Fields.Group.Equal("change"))
                .OrderBy(L43UniqueItem.Fields.Name).ThenByRecordId().Take(1)
                .PatchSelectedAsync(profile, new RecordPatchRequest { Patch = new RecordPayload { Kind = RecordPayloadKind.FieldMap, Fields = fields }, RemovedFieldIds = [] }, BasePreviousStateRequirement.None);

            BaseFailure<BaseSelectionMutationResult> failure = result.Should().BeOfType<BaseFailure<BaseSelectionMutationResult>>().Subject;
            failure.Error.Code.Should().Be(BaseSchemaErrorCodes.UniqueConstraintViolated);
            (await collection.GetAsync(RecordId.Create("selected"))).RequireValue().Value.Name.Should().Be("free");
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }

    [Fact]
    public async Task SqliteRequiredIndexPointExecutesThroughTheCompleteTypedL43Runtime()
    {
        string path = Path.Combine(Path.GetTempPath(), $"hpd-l43-required-point-{Guid.NewGuid():N}-constraint.db");
        try
        {
            await using ServiceProvider provider = Build(path);
            IBaseSchemaManager schemas = provider.GetRequiredService<IBaseSchemaManager>();
            BaseSchemaPlan plan = (await schemas.PlanAsync(
                new BaseSchemaPlanRequest { StoreId = "sqlite-l43" })).Value!;
            (await schemas.ApplyAsync(new BaseSchemaApplyRequest
            {
                ProtectedArtifact = plan.ProtectedArtifact,
            })).IsSuccess().Should().BeTrue();
            (await provider.GetRequiredService<IHPDBaseApplication>().InitializeAsync())
                .IsSuccess().Should().BeTrue();
            BaseCollectionSession<L43UniqueItem> collection = provider
                .GetRequiredService<IBaseSessionFactory>().For(Admin()).Collection(L43UniqueItem.Collection);
            (await collection.CreateAsync(RecordId.Create("point-item"), new L43UniqueItem
            {
                Group = "point", Name = "ready", Code = "point-code",
            })).RequireValue();
            BaseSelectionOperationProfile installed = Profile(
                "unique-patch", BaseSelectionMutationKind.MergePatch) with { CollectionId = "l43-unique" };
            string nameWire = L43UniqueItem.Collection.Definition.Fields!
                .Single(field => field.Id == "unique-name").WireName;

            BaseResult<BaseSelectionMutationResult> selected = await collection.Query()
                .Where(L43UniqueItem.Fields.Name.Equal("ready"))
                .OrderBy(L43UniqueItem.Fields.Name).ThenByRecordId().Take(1)
                .PatchSelectedAsync(
                    collection.GetMergePatchSelectionProfile(Identity(installed)),
                    new RecordPatchRequest
                    {
                        Patch = new RecordPayload
                        {
                            Kind = RecordPayloadKind.FieldMap,
                            Fields = new Dictionary<string, JsonElement>(StringComparer.Ordinal)
                            {
                                [nameWire] = JsonSerializer.SerializeToElement("claimed"),
                            },
                        },
                        RemovedFieldIds = [],
                    },
                    BasePreviousStateRequirement.None,
                    Identity("required-point"));

            selected.Should().BeOfType<BaseSuccess<BaseSelectionMutationResult>>(
                selected is BaseFailure<BaseSelectionMutationResult> failure ? failure.Error.Code : string.Empty);
            selected.RequireValue().MutatedCount.Should().Be(1);
            (await collection.GetAsync(RecordId.Create("point-item"))).RequireValue().Value.Name
                .Should().Be("claimed");

            BaseSelectionMutationResult oldKey = (await collection.Query()
                .Where(L43UniqueItem.Fields.Name.Equal("ready"))
                .OrderBy(L43UniqueItem.Fields.Name).ThenByRecordId().Take(1)
                .PatchSelectedAsync(
                    collection.GetMergePatchSelectionProfile(Identity(installed)),
                    new RecordPatchRequest
                    {
                        Patch = new RecordPayload
                        {
                            Kind = RecordPayloadKind.FieldMap,
                            Fields = new Dictionary<string, JsonElement>(StringComparer.Ordinal)
                            {
                                [nameWire] = JsonSerializer.SerializeToElement("incorrect"),
                            },
                        },
                        RemovedFieldIds = [],
                    },
                    BasePreviousStateRequirement.None,
                    Identity("required-point-old-key"))).RequireValue();
            oldKey.SelectedCount.Should().Be(0);
            oldKey.MutatedCount.Should().Be(0);

            BaseSelectionMutationResult newKey = (await collection.Query()
                .Where(L43UniqueItem.Fields.Name.Equal("claimed"))
                .OrderBy(L43UniqueItem.Fields.Name).ThenByRecordId().Take(1)
                .PatchSelectedAsync(
                    collection.GetMergePatchSelectionProfile(Identity(installed)),
                    new RecordPatchRequest
                    {
                        Patch = new RecordPayload
                        {
                            Kind = RecordPayloadKind.FieldMap,
                            Fields = new Dictionary<string, JsonElement>(StringComparer.Ordinal)
                            {
                                [nameWire] = JsonSerializer.SerializeToElement("final"),
                            },
                        },
                        RemovedFieldIds = [],
                    },
                    BasePreviousStateRequirement.None,
                    Identity("required-point-new-key"))).RequireValue();
            newKey.SelectedCount.Should().Be(1);
            newKey.MutatedCount.Should().Be(1);
            (await collection.GetAsync(RecordId.Create("point-item"))).RequireValue().Value.Name
                .Should().Be("final");
        }
        finally { if (File.Exists(path)) File.Delete(path); }
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
        await collection.CreateAsync(RecordId.Create("later"), new GeneratedProject { OrganizationId = "later", Name = "later" });
        BaseSelectionMutationResult duplicate = (await query.DeleteSelectedAsync(profile, BasePreviousStateRequirement.None, identity)).RequireValue();

        duplicate.RequestDisposition.Should().Be(BaseMutationRequestDisposition.Duplicate);
        duplicate.SelectedCount.Should().Be(0);
        (await collection.GetAsync(RecordId.Create("later"))).TryGetValue(out _).Should().BeTrue();
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
            CollectionId = "projects", Id = RecordId.Create("one"),
            Payload = new RecordPayload { Kind = RecordPayloadKind.FieldMap, Fields = fields },
            Metadata = new RecordMetadata { Revision = new RevisionToken("mem:1") },
        };
        BaseOwnedSelectedRecord owned = BaseOwnedSelectedRecord.Freeze(envelope, 0, 1);
        fields["name"] = JsonSerializer.SerializeToElement("changed");
        owned.MaterializeOwned().Payload.Fields!["name"].ValueKind.Should().Be(JsonValueKind.Array);
        owned.CopyCanonicalBytes().Should().NotBeSameAs(owned.CopyCanonicalBytes());
    }

    [Theory]
    [InlineData("foreign-collection")]
    [InlineData("policy-excluded")]
    [InlineData("duplicate-id")]
    [InlineData("invalid-order")]
    [InlineData("invalid-boundary")]
    [InlineData("missing-interval")]
    public void EachHostileSelectionEvidenceDefectIsIndependentlyRejected(string defect)
    {
        CollectionDefinition collection = GeneratedProject.Collection.Definition;
        BaseSelectionOperationProfile profile = Profile("claim", BaseSelectionMutationKind.MergePatch);
        BaseAtomicMutationAuthorityRequirement authority = new()
        {
            ApplicationId = profile.ApplicationId,
            StoreInstanceId = "authority",
            RestoreEpoch = 0,
            SchemaGeneration = 1,
            LogicalSchemaChecksum = BaseSchemaAuthorityChecksum.Create(new byte[32]),
            Collections = [new BaseCollectionGenerationRequirement { CollectionId = collection.Id, CollectionGeneration = 0 }],
        };
        RecordQuery query = new()
        {
            Filter = new FilterExpression { Kind = FilterNodeKind.Compare, Field = "organizationId", Operator = FilterOperator.Equal, Value = new QueryValue { Kind = QueryValueKind.String, String = "allowed" } },
            Sort = [new QuerySort { Field = "name", Direction = QuerySortDirection.Asc }, new QuerySort { Field = "id", Direction = QuerySortDirection.Asc }],
            Page = new QueryPage { Mode = QueryPaginationMode.Offset, Offset = 0, Limit = 2 },
        };
        RecordEnvelope firstEnvelope = Envelope("one", "allowed", "a");
        RecordEnvelope secondEnvelope = Envelope("two", "allowed", "b");
        if (defect == "foreign-collection") firstEnvelope = firstEnvelope with { CollectionId = "foreign" };
        if (defect == "policy-excluded") firstEnvelope = Envelope("one", "denied", "a");
        if (defect == "duplicate-id") secondEnvelope = Envelope("one", "allowed", "b");
        if (defect == "invalid-order") (firstEnvelope, secondEnvelope) = (secondEnvelope, firstEnvelope);
        BaseOwnedSelectedRecord first = BaseOwnedSelectedRecord.Freeze(firstEnvelope, 0, 1);
        BaseOwnedSelectedRecord second = BaseOwnedSelectedRecord.Freeze(secondEnvelope, 1, 1);
        byte[] boundary = BaseSelectionOrderTuple.Encode(secondEnvelope, query.Sort);
        ImmutableArray<BaseAtomicReadIntervalEvidence> intervals =
        [new BaseAtomicReadIntervalEvidence
        {
            LogicalAccessPathId = "collection:projects", CanonicalLowerBound = [], LowerInclusive = true,
            CanonicalUpperBound = boundary.ToImmutableArray(), UpperInclusive = true,
        }];
        if (defect == "missing-interval") intervals = [];
        byte[] reportedBoundary = defect == "invalid-boundary" ? [0x7f] : boundary;
        BaseAtomicMutationAuthorityEvidence capturedAuthority = new()
        {
            ApplicationId = profile.ApplicationId,
            StoreInstanceId = "authority",
            RestoreEpoch = 0,
            SchemaGeneration = 1,
            LogicalSchemaChecksum = authority.LogicalSchemaChecksum,
            Collections = [new BaseCollectionGenerationRequirement { CollectionId = collection.Id, CollectionGeneration = 0 }],
            Isolation = BaseAtomicSelectionIsolationClass.WriteOwningSerializable,
            TransactionEvidenceToken = [1],
        };
        BaseCapturedAtomicExecution capture = new()
        {
            Kind = BaseAtomicMutationExecutionKind.SelectionMutation,
            IntentDigest = "hostile-selection-intent",
            CaptureDigest = new string('a', 64),
            Authority = capturedAuthority,
            Selection = null,
            Items = [],
            ModuleRecords = [],
            ModuleRelationTargets = [],
            Generations = [],
            ReadIntervals = intervals,
            Accounting = new BaseAtomicCaptureAccounting
            {
                Records = 2, RelationTargetReads = 0, GenerationReads = 0,
                SelectedBytes = first.CanonicalBytes + second.CanonicalBytes,
                RelationTargetBytes = 0, GenerationBytes = 0,
                ReadIntervals = intervals.Length,
                EvidenceBytes = intervals.Sum(interval => (long)interval.CanonicalLowerBound.Length + interval.CanonicalUpperBound.Length),
                TransientBytes = first.CanonicalBytes + second.CanonicalBytes,
                RetirementBarrierReads = 0, RetirementAcknowledgementReads = 0,
                RetirementProjections = 0, RetirementPublications = 0,
                RetirementEvidenceBytes = 0, RetirementPublicationBytes = 0,
            },
        };
        BaseValidatedSelection baseline = new()
        {
            MutationCapture = capture,
            Authority = capturedAuthority,
            Records = [first, second], ReadIntervals = intervals, CanonicalOrderBoundary = reportedBoundary.ToImmutableArray(),
            LogicalIndexEvidence = null,
            Accounting = new BaseAtomicSelectionAccounting { SelectedRecords = 2, SelectedBytes = first.CanonicalBytes + second.CanonicalBytes, ReadIntervals = intervals.Length, EvidenceBytes = intervals.Sum(interval => (long)interval.CanonicalLowerBound.Length + interval.CanonicalUpperBound.Length) },
        };

        BaseSelectionMutationProcessor.ValidateSelectionEvidence(baseline, profile, authority, collection, query).Should().BeFalse();
    }

    [Fact]
    public async Task PreviousStateIsNormalizedAndRollsBackTheCompleteSelection()
    {
        await using ServiceProvider provider = Build();
        (await provider.GetRequiredService<IHPDBaseApplication>().InitializeAsync()).IsSuccess().Should().BeTrue();
        BaseCollectionSession<GeneratedProject> collection = provider.GetRequiredService<IBaseSessionFactory>().For(Admin()).Collection(GeneratedProject.Collection);
        await collection.CreateAsync(RecordId.Create("one"), new GeneratedProject { OrganizationId = "org", Name = "ready" });
        await collection.CreateAsync(RecordId.Create("two"), new GeneratedProject { OrganizationId = "org", Name = "blocked" });

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

    private static ServiceProvider Build(string? sqlitePath = null)
    {
        var services = new ServiceCollection().AddLogging();
        services.AddHPDBase(builder =>
        {
            builder
            .ConfigureSchema(options =>
            {
                options.ApplicationId = "hpd.base.application";
                if (sqlitePath is not null) options.PlanProtectionKey = Enumerable.Repeat((byte)0x43, 32).ToArray();
            })
            .ConfigureSelectionMutations(new HPDBaseSelectionMutationOptions
            {
                HostMaxima = Limits(), MaximumReceiptIdentityBytes = 512,
                MaximumEvidenceTokenBytes = 512, MaximumRouteNameBytes = 96,
                MaximumRequestBodyBytes = 1_048_576,
            })
            .AddPolicyAuthority<AllowAll>(PolicyDefinition("l43.allow"))
            .AddCollection(GeneratedProject.Collection)
            .AddSelectionOperationProfile(Profile("claim", BaseSelectionMutationKind.MergePatch))
            .AddSelectionOperationProfile(Profile("remove", BaseSelectionMutationKind.Delete));
            if (sqlitePath?.Contains("constraint", StringComparison.Ordinal) == true)
            {
                builder.AddCollection(L43UniqueItem.Collection)
                    .AddSelectionOperationProfile(Profile("unique-patch", BaseSelectionMutationKind.MergePatch) with { CollectionId = "l43-unique" });
            }
            if (sqlitePath is not null) builder.UseStore(HPD.Base.Sqlite.SqliteStore.Configure(options => { options.DataSource = sqlitePath; options.StoreId = "sqlite-l43"; }));
            builder.PolicyAuthority.AddStaticGrant(new BaseGrantAuthorityDefinition
            {
                Id = "projects.selection", Version = 1, OwningModuleId = "hpd.base.tests",
                SourceContractId = "hpd.base.tests.grants", SourceContractVersion = 1,
            }, new AccessGrant
            {
                Id = "projects.selection", Subject = new AccessSubject { Kind = AccessSubjectKind.User },
                Action = "selectionMutation", Scope = new ResourceScope { Kind = ResourceScopeKind.Collection, CollectionId = "projects" },
            });
        });
        return services.BuildServiceProvider();
    }

    private static BaseSelectionOperationProfile Profile(string id, BaseSelectionMutationKind kind) => new()
    {
        Id = id, Version = 1, ApplicationId = "hpd.base.application", CollectionId = "projects",
        RequiredGrantId = "projects.selection", MutationKind = kind, Limits = Limits(),
    };
    private static BasePolicyAuthorityDefinition PolicyDefinition(string id) => new()
    {
        Id = id, Version = 1, OwningModuleId = "hpd.base.tests",
        EvaluatorContractId = "hpd.base.tests.allow", EvaluatorContractVersion = 1, CompositionOrder = 0,
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
        RemovedFieldIds = [],
    };
    private static RecordEnvelope Envelope(string id, string organization, string name) => new()
    {
        Id = RecordId.Create(id), CollectionId = "projects",
        Payload = new RecordPayload { Kind = RecordPayloadKind.FieldMap, Fields = new Dictionary<string, JsonElement> { ["organizationId"] = JsonSerializer.SerializeToElement(organization), ["name"] = JsonSerializer.SerializeToElement(name) } },
        Metadata = new RecordMetadata { Revision = new RevisionToken("test:1"), CreatedAt = DateTimeOffset.UnixEpoch, UpdatedAt = DateTimeOffset.UnixEpoch },
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

[BaseCollection("l43-unique", typeof(L43SelectionJsonContext))]
[BaseIndex("unique-name", Unique = true)]
[BaseIndexPart("unique-name", 0, nameof(L43UniqueItem.Name))]
[BaseIndex("unique-code", Unique = true)]
[BaseIndexPart("unique-code", 0, nameof(L43UniqueItem.Code))]
internal sealed partial record L43UniqueItem
{
    [BaseField("unique-group", Operators = BaseFieldOperator.Equal)] public required string Group { get; init; }
    [BaseField("unique-name", Operators = BaseFieldOperator.Equal | BaseFieldOperator.Order)] public required string Name { get; init; }
    [BaseField("unique-code", Operators = BaseFieldOperator.Equal)] public required string Code { get; init; }
}

[JsonSerializable(typeof(L43UniqueItem))]
internal sealed partial class L43SelectionJsonContext : JsonSerializerContext;
