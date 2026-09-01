using HPD.Base;

namespace HPD.Auth.Base.ConsumerProof;

[BaseRegisteredModuleMutation("proof.static-set.v1", typeof(ConsumerJsonSerializerContext),
    typeof(StaticSetRequest), typeof(StaticSetResult), Version = 1,
    OwningModuleId = "proof.module", GrantId = "proof.static-set.execute")]
internal static partial class StaticSetProof
{
    internal const int CohortSize = 64;

    internal static BaseRegisteredModuleMutationDefinition Definition { get; } = CreateDefinition();

    private static BaseRegisteredModuleMutationDefinition CreateDefinition()
    {
        BaseModuleGuard[] enableGuards =
        [
            .. Enumerable.Range(0, CohortSize).Select(index => DisabledGuard("new", index)),
            .. Enumerable.Range(0, CohortSize).Select(index => EnabledGuard("new", index)),
            .. Enumerable.Range(0, CohortSize).Select(index => DisabledGuard("prior", index)),
            .. Enumerable.Range(0, CohortSize).Select(index => EnabledGuard("prior", index)),
        ];
        BaseModuleGuard[] setGuards =
        [
            BaseModuleMutationTemplateBuilder.Disjoint("set-disjoint",
                ValueSet("new", "disjoint-new"), ValueSet("prior", "disjoint-prior")),
            BaseModuleMutationTemplateBuilder.StrictlyIncreasing("set-new-ordered", ValueSet("new", "ordered-new")),
            BaseModuleMutationTemplateBuilder.StrictlyIncreasing("set-prior-ordered", ValueSet("prior", "ordered-prior")),
        ];
        return BaseModuleMutationContract.Seal(new BaseRegisteredModuleMutationDefinition
        {
            Id = "proof.static-set.v1", Version = 1, OwningModuleId = "proof.module",
            GrantId = "proof.static-set.execute", Audience = BaseModuleMutationAudience.Service,
            RequestTypeId = "proof.static-set.request", ResultTypeId = "proof.static-set.result",
            SystemCollectionIds = [ProofOwner.Collection.Id, ProofWorkItem.Collection.Id],
            SystemSourceGrants =
            [
                new BaseModuleSystemSourceGrant { CollectionId = ProofOwner.Collection.Id, GrantId = "proof.owner.source" },
                new BaseModuleSystemSourceGrant { CollectionId = ProofWorkItem.Collection.Id, GrantId = "proof.work.source" },
            ],
            GenerationCellIds = [], ImportedSubjectContractIds = [],
            Template = new BaseModuleMutationTemplate
            {
                Captures =
                [
                    .. Enumerable.Range(0, CohortSize).Select(NewCapture),
                    .. Enumerable.Range(0, CohortSize).Select(PriorCapture),
                ],
                Guards = [.. enableGuards, .. setGuards],
                Preconditions = [.. new[]
                {
                    "set-disjoint", "set-new-ordered", "set-prior-ordered",
                }.Select(id => BaseModuleMutationTemplateBuilder.Precondition(
                    "require-" + id, id, "proof.static-set." + id))],
                Body = new BaseModuleMutationBlock
                {
                    Statements =
                    [
                        NewBranch(0),
                        PriorBranch(0),
                    ],
                },
                Result = BaseModuleMutationTemplateBuilder.Result(
                    BaseModuleMutationTemplateBuilder.ResultObject("static-set-result",
                        BaseModuleMutationTemplateBuilder.Property(ResultProperties.NewCount,
                            BaseModuleMutationTemplateBuilder.Request("result-new-count", RequestProperties.NewCount)),
                        BaseModuleMutationTemplateBuilder.Property(ResultProperties.PriorCount,
                            BaseModuleMutationTemplateBuilder.Request("result-prior-count", RequestProperties.PriorCount)))),
            },
            Limits = Limits(),
            ReceiptPolicy = new BaseModuleMutationReceiptPolicy { FormatVersion = 1, Lifetime = TimeSpan.FromDays(1) },
            Checksum = BaseModuleMutationChecksum.Create(System.Security.Cryptography.SHA256.HashData("proof.static-set.v1"u8)),
        });
    }

    private static BaseModuleValue<string> IdValue(string cohort, int index) =>
        BaseModuleMutationTemplateBuilder.Constant($"{cohort}-id-{index:D3}",
            RequestProperties.StaticAuthority.ConstantAuthority, $"{cohort}-{index:D3}");

    private static BaseModuleValue<BaseRecordId<ProofWorkItem>> RecordId(string cohort, int index) =>
        BaseModuleMutationTemplateBuilder.RecordIdFromString<ProofWorkItem>($"{cohort}-record-id-{index:D3}",
            IdValue(cohort, index));

    private static BaseModuleValue<BaseRecordId<ProofOwner>> OwnerId(int index) =>
        BaseModuleMutationTemplateBuilder.RecordIdFromGuid<ProofOwner>($"new-owner-id-{index:D3}",
            BaseModuleMutationTemplateBuilder.Request($"new-owner-source-{index:D3}", RequestProperties.OwnerId));

    private static BaseModuleValue<string> NameValue(int index, string usage) =>
        BaseModuleMutationTemplateBuilder.Constant($"new-{usage}-{index:D3}",
            ProofWorkItem.Fields.Name.ConstantAuthority, $"new-{index:D3}");

    private static BaseModuleGuard EnabledGuard(string cohort, int index)
    {
        BaseModuleValue<int> count = BaseModuleMutationTemplateBuilder.Request(
            $"{cohort}-count-{index:D3}", cohort == "new" ? RequestProperties.NewCount : RequestProperties.PriorCount);
        BaseModuleConstantAuthority<int> authority = cohort == "new"
            ? RequestProperties.NewCount.ConstantAuthority : RequestProperties.PriorCount.ConstantAuthority;
        return BaseModuleMutationTemplateBuilder.ValueCompare($"{cohort}-enabled-{index:D3}", count,
            BaseModuleOrderedComparisonKind.GreaterThan,
            BaseModuleMutationTemplateBuilder.Constant($"{cohort}-index-{index:D3}", authority, index));
    }

    private static BaseModuleGuard DisabledGuard(string cohort, int index) =>
        BaseModuleMutationTemplateBuilder.Not($"{cohort}-disabled-{index:D3}", $"{cohort}-enabled-{index:D3}");

    private static BaseModuleStaticValueSet<string> ValueSet(string cohort, string usage) =>
        BaseModuleMutationTemplateBuilder.ValueSet($"{usage}-set",
            [.. Enumerable.Range(0, CohortSize).Select(index =>
                BaseModuleMutationTemplateBuilder.ValueMember($"{usage}-member-{index:D3}",
                    BaseModuleMutationTemplateBuilder.Constant($"{usage}-value-{index:D3}",
                        RequestProperties.StaticAuthority.ConstantAuthority, $"{cohort}-{index:D3}"),
                    $"{cohort}-enabled-{index:D3}"))]);

    private static BaseModuleRecordCapture NewCapture(int index) => BaseModuleMutationTemplateBuilder.CaptureRecord(
        $"new-record-{index:D3}", RecordId("new", index), BaseModuleCapturePresence.RequireMissing,
        $"new-enabled-{index:D3}");

    private static BaseModuleRecordCapture PriorCapture(int index) => BaseModuleMutationTemplateBuilder.CaptureRecord(
        $"prior-record-{index:D3}", RecordId("prior", index), BaseModuleCapturePresence.RequirePresent,
        $"prior-enabled-{index:D3}");

    private static BaseModuleIfStatement NewBranch(int index) => BaseModuleMutationTemplateBuilder.If(
        $"new-if-{index:D3}", $"new-enabled-{index:D3}",
        new BaseModuleMutationBlock
        {
            Statements = index + 1 == CohortSize
                ? [NewCreate(index)]
                : [NewCreate(index), NewBranch(index + 1)],
        },
        new BaseModuleMutationBlock { Statements = [BaseModuleMutationTemplateBuilder.Require(
            $"new-disabled-require-{index:D3}", $"new-disabled-{index:D3}", "proof.static-set.new-disabled")] });

    private static BaseModuleCreateStatement NewCreate(int index) => BaseModuleMutationTemplateBuilder.Create(
        $"new-create-{index:D3}", RecordId("new", index),
        BaseModuleMutationTemplateBuilder.Object<ProofWorkItem>($"new-payload-{index:D3}",
            BaseModuleMutationTemplateBuilder.Field(ProofWorkItem.Fields.Name, NameValue(index, "name")),
            BaseModuleMutationTemplateBuilder.Field(ProofWorkItem.Fields.OwnerId, OwnerId(index))));

    private static BaseModuleIfStatement PriorBranch(int index) => BaseModuleMutationTemplateBuilder.If(
        $"prior-if-{index:D3}", $"prior-enabled-{index:D3}",
        new BaseModuleMutationBlock
        {
            Statements = index + 1 == CohortSize
                ? [BaseModuleMutationTemplateBuilder.Delete($"prior-delete-{index:D3}", RecordId("prior", index))]
                :
                [
                    BaseModuleMutationTemplateBuilder.Delete($"prior-delete-{index:D3}", RecordId("prior", index)),
                    PriorBranch(index + 1),
                ],
        },
        new BaseModuleMutationBlock { Statements = [BaseModuleMutationTemplateBuilder.Require(
            $"prior-disabled-require-{index:D3}", $"prior-disabled-{index:D3}", "proof.static-set.prior-disabled")] });

    private static BaseModuleMutationLimits Limits() => IdentityAndGenerationProofLimits.Create() with
    {
        MaximumCaptures = 128, MaximumRecordCaptures = 128, MaximumRelationTargetCaptures = 64,
        MaximumRecordMutations = 128, MaximumGuardNodes = 300, MaximumGuardDepth = 8,
        MaximumPreconditions = 8, MaximumRequestGuardEvaluations = 8_192,
        MaximumStaticSetMembers = 256, MaximumStaticSetComparisons = 4_222,
        MaximumDisabledCaptures = 128, MaximumStatements = 384, MaximumBranches = 128,
        MaximumExpressionNodes = 2_048, MaximumReadIntervals = 1_024, MaximumAuthorityReads = 2_048,
        MaximumRelationChecks = 4_096, MaximumUniqueConstraintChecks = 4_096,
        MaximumSelectedBytes = 8_388_608, MaximumEvidenceBytes = 8_388_608,
        MaximumWrittenBytes = 8_388_608, MaximumFactBytes = 8_388_608,
        MaximumJournalBytes = 8_388_608, MaximumReceiptBytes = 8_388_608,
        MaximumTransientBytes = 16_777_216,
    };
}

internal sealed record StaticSetRequest
{
    [BaseField("proof.static-set.request.new-count", HasMinimumInt32 = true, MinimumInt32 = 0, HasMaximumInt32 = true, MaximumInt32 = 64)]
    public required int NewCount { get; init; }

    [BaseField("proof.static-set.request.owner-id")]
    [System.Text.Json.Serialization.JsonConverter(typeof(BaseCanonicalGuidJsonConverter))]
    public required Guid OwnerId { get; init; }

    [BaseField("proof.static-set.request.prior-count", HasMinimumInt32 = true, MinimumInt32 = 0, HasMaximumInt32 = true, MaximumInt32 = 64)]
    public required int PriorCount { get; init; }

    [BaseField("proof.static-set.request.static-authority", MinimumUtf8Bytes = 1, MaximumUtf8Bytes = 256)]
    public required string StaticAuthority { get; init; }
}

internal sealed record StaticSetResult
{
    [BaseField("proof.static-set.result.new-count", HasMinimumInt32 = true, MinimumInt32 = 0, HasMaximumInt32 = true, MaximumInt32 = 64)]
    public required int NewCount { get; init; }

    [BaseField("proof.static-set.result.prior-count", HasMinimumInt32 = true, MinimumInt32 = 0, HasMaximumInt32 = true, MaximumInt32 = 64)]
    public required int PriorCount { get; init; }
}
