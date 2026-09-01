using HPD.Base;

namespace HPD.Auth.Base.ConsumerProof;

[BaseRegisteredModuleMutation("proof.request-control.v1", typeof(ConsumerJsonSerializerContext),
    typeof(RequestControlRequest), typeof(RequestControlResult), Version = 1,
    OwningModuleId = "proof.module", GrantId = "proof.request-control.execute")]
internal static partial class RequestControlProof
{
    internal static BaseRegisteredModuleMutationDefinition Definition { get; } = BaseModuleMutationContract.Seal(new()
    {
        Id = "proof.request-control.v1", Version = 1, OwningModuleId = "proof.module",
        GrantId = "proof.request-control.execute", Audience = BaseModuleMutationAudience.Service,
        RequestTypeId = "proof.request-control.request", ResultTypeId = "proof.request-control.result",
        SystemCollectionIds = [ProofOwner.Collection.Id, ProofWorkItem.Collection.Id],
        SystemSourceGrants =
        [
            new BaseModuleSystemSourceGrant { CollectionId = ProofOwner.Collection.Id, GrantId = "proof.owner.source" },
            new BaseModuleSystemSourceGrant { CollectionId = ProofWorkItem.Collection.Id, GrantId = "proof.work.source" },
        ],
        GenerationCellIds = [], ImportedSubjectContractIds = [],
        Template = new BaseModuleMutationTemplate
        {
            Captures = [HostileCapture(), TargetCapture()],
            Guards = [EnableHostileGuard(), ExplicitNullGuard(), OrderedGuard()],
            Preconditions =
            [
                BaseModuleMutationTemplateBuilder.Precondition("require-explicit-null", "explicit-null", "proof.request-control.explicit-null"),
                BaseModuleMutationTemplateBuilder.Precondition("require-order", "ordered", "proof.request-control.ordered"),
            ],
            Body = new BaseModuleMutationBlock { Statements = [PatchStatement()] },
            Result = BaseModuleMutationTemplateBuilder.Result(
                BaseModuleMutationTemplateBuilder.ResultObject("request-control-result",
                    BaseModuleMutationTemplateBuilder.Property(ResultProperties.Accepted,
                        BaseModuleMutationTemplateBuilder.Request("request-control-result-accepted", RequestProperties.Accepted)))),
        },
        Limits = IdentityAndGenerationProofLimits.Create(),
        ReceiptPolicy = new BaseModuleMutationReceiptPolicy { FormatVersion = 1, Lifetime = TimeSpan.FromDays(1) },
        Checksum = BaseModuleMutationChecksum.Create(System.Security.Cryptography.SHA256.HashData("proof.request-control.v1"u8)),
    });

    private static BaseModuleValue<BaseRecordId<ProofWorkItem>> TargetId() =>
        BaseModuleMutationTemplateBuilder.RecordIdFromString<ProofWorkItem>("target-id",
            BaseModuleMutationTemplateBuilder.Request("target-id-source", RequestProperties.TargetId));

    private static BaseModuleValue<BaseRecordId<ProofOwner>> OwnerId() =>
        BaseModuleMutationTemplateBuilder.RecordIdFromGuid<ProofOwner>("target-owner-id",
            BaseModuleMutationTemplateBuilder.Request("target-owner-id-source", RequestProperties.OwnerId));

    private static BaseModuleRecordCapture TargetCapture() => BaseModuleMutationTemplateBuilder.CaptureRecord(
        "target-record", TargetId(), BaseModuleCapturePresence.RequirePresent);

    private static BaseModuleRecordCapture HostileCapture() => BaseModuleMutationTemplateBuilder.CaptureRecord(
        "hostile-record", BaseModuleMutationTemplateBuilder.RecordIdFromString<ProofWorkItem>("hostile-id",
            BaseModuleMutationTemplateBuilder.Request("hostile-id-source", RequestProperties.HostileId)),
        BaseModuleCapturePresence.AllowEither, "enable-hostile");

    private static BaseModuleValueEqualsGuard EnableHostileGuard() => BaseModuleMutationTemplateBuilder.ValueEquals(
        "enable-hostile", BaseModuleMutationTemplateBuilder.Request("enable-hostile-left", RequestProperties.EnableHostile),
        BaseModuleMutationTemplateBuilder.Constant("enable-hostile-right", RequestProperties.EnableHostile.ConstantAuthority, true));

    private static BaseModuleValuePresenceGuard ExplicitNullGuard() => BaseModuleMutationTemplateBuilder.ValuePresence(
        "explicit-null", BaseModuleMutationTemplateBuilder.Request("optional-note", RequestProperties.OptionalNote),
        BaseModuleFieldPresenceTest.Null);

    private static BaseModuleValueComparisonGuard OrderedGuard() => BaseModuleMutationTemplateBuilder.ValueCompare(
        "ordered", BaseModuleMutationTemplateBuilder.Request("ordered-left", RequestProperties.Left),
        BaseModuleOrderedComparisonKind.LessThan,
        BaseModuleMutationTemplateBuilder.Request("ordered-right", RequestProperties.Right));

    private static BaseModulePatchStatement PatchStatement() => BaseModuleMutationTemplateBuilder.Patch(
        "patch", TargetId(), BaseModuleMutationTemplateBuilder.Object<ProofWorkItem>("patch-payload",
            BaseModuleMutationTemplateBuilder.Field(ProofWorkItem.Fields.Name,
                BaseModuleMutationTemplateBuilder.Request("patch-name", RequestProperties.Name)),
            BaseModuleMutationTemplateBuilder.Field(ProofWorkItem.Fields.OwnerId, OwnerId())));
}

internal static class IdentityAndGenerationProofLimits
{
    internal static BaseModuleMutationLimits Create() => new()
    {
        MaximumCaptures = 16, MaximumRecordCaptures = 12, MaximumRelationTargetCaptures = 8,
        MaximumGenerationCaptures = 4, MaximumRecordMutations = 8, MaximumGenerationReads = 4,
        MaximumGenerationComparisons = 4, MaximumGenerationIncrements = 4, MaximumGuardNodes = 16,
        MaximumGuardDepth = 8, MaximumPreconditions = 8, MaximumRequestGuardEvaluations = 32,
        MaximumStaticSetMembers = 16, MaximumStaticSetComparisons = 120, MaximumDisabledCaptures = 8,
        MaximumRemovedFields = 8, MaximumStatements = 16, MaximumBranches = 8,
        MaximumExpressionNodes = 128, MaximumReadIntervals = 128, MaximumSubjectValidations = 8,
        MaximumAuthorityReads = 128, MaximumRelationChecks = 32, MaximumUniqueConstraintChecks = 32,
        MaximumRequestBytes = 65_536, MaximumSelectedBytes = 131_072, MaximumGenerationBytes = 16_384,
        MaximumEvidenceBytes = 131_072, MaximumWrittenBytes = 131_072, MaximumFactBytes = 131_072,
        MaximumJournalBytes = 131_072, MaximumReceiptBytes = 131_072, MaximumResultBytes = 16_384,
        MaximumTransientBytes = 1_048_576,
        Deadlines = new BaseAtomicMutationDeadlines
        {
            AcquisitionTimeout = TimeSpan.FromSeconds(5), TransactionTimeout = TimeSpan.FromSeconds(5),
            CommitObservationTimeout = TimeSpan.FromSeconds(5), ReceiptResolutionTimeout = TimeSpan.FromSeconds(5),
        },
    };
}

internal sealed record RequestControlRequest
{
    [BaseField("proof.request-control.request.accepted")]
    public required bool Accepted { get; init; }

    [BaseField("proof.request-control.request.enable-hostile")]
    public required bool EnableHostile { get; init; }

    [BaseField("proof.request-control.request.hostile-id", MinimumUtf8Bytes = 1, MaximumUtf8Bytes = 256)]
    public required string HostileId { get; init; }

    [BaseField("proof.request-control.request.left")]
    public required long Left { get; init; }

    [BaseField("proof.request-control.request.name", MaximumUtf8Bytes = 64)]
    public required string Name { get; init; }

    [BaseField("proof.request-control.request.optional-note", Nullability = BaseFieldNullability.Nullable, MaximumUtf8Bytes = 64)]
    public string? OptionalNote { get; init; }

    [BaseField("proof.request-control.request.owner-id")]
    [System.Text.Json.Serialization.JsonConverter(typeof(BaseCanonicalGuidJsonConverter))]
    public required Guid OwnerId { get; init; }

    [BaseField("proof.request-control.request.right")]
    public required long Right { get; init; }

    [BaseField("proof.request-control.request.target-id", MinimumUtf8Bytes = 1, MaximumUtf8Bytes = 256)]
    public required string TargetId { get; init; }
}

internal sealed record RequestControlResult
{
    [BaseField("proof.request-control.result.accepted")]
    public required bool Accepted { get; init; }
}
