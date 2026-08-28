using System.Text.Json.Serialization;
using HPD.Base;

namespace HPD.Auth.Base.ConsumerProof;

[BaseRegisteredModuleMutation("proof.identity-and-generation.v1", typeof(ConsumerJsonSerializerContext),
    typeof(IdentityAndGenerationRequest), typeof(IdentityAndGenerationResult), Version = 1,
    OwningModuleId = "proof.module", GrantId = "proof.identity-and-generation.execute")]
internal static partial class IdentityAndGenerationProof
{
    internal static BaseRegisteredModuleMutationDefinition Definition { get; } = BaseModuleMutationContract.Seal(new()
    {
        Id = "proof.identity-and-generation.v1", Version = 1, OwningModuleId = "proof.module",
        GrantId = "proof.identity-and-generation.execute", Audience = BaseModuleMutationAudience.Service,
        RequestTypeId = "proof.identity-and-generation.request",
        ResultTypeId = "proof.identity-and-generation.result",
        SystemCollectionIds = [ProofOwner.Collection.Id, ProofWorkItem.Collection.Id],
        SystemSourceGrants =
        [
            new BaseModuleSystemSourceGrant { CollectionId = ProofOwner.Collection.Id, GrantId = "proof.owner.source" },
            new BaseModuleSystemSourceGrant { CollectionId = ProofWorkItem.Collection.Id, GrantId = "proof.work.source" },
        ],
        GenerationCellIds = ["proof.identity-and-generation.cell"], ImportedSubjectContractIds = [],
        Template = new BaseModuleMutationTemplate
        {
            Captures = [CreateCapture(), DeleteCapture(), GenerationCapture(), PatchCapture()],
            Guards = [], Preconditions = [],
            Body = new BaseModuleMutationBlock
            {
                Statements = [CreateStatement(), DeleteStatement(), IncrementStatement(), PatchStatement()],
            },
            Result = BaseModuleMutationTemplateBuilder.Result(
                BaseModuleMutationTemplateBuilder.ResultObject("proof-result",
                    BaseModuleMutationTemplateBuilder.Property(ResultProperties.CreatedId,
                        BaseModuleMutationTemplateBuilder.Request("proof-result-created-id", RequestProperties.CreateId)),
                    BaseModuleMutationTemplateBuilder.Property(ResultProperties.Generation,
                        BaseModuleMutationTemplateBuilder.ResultingGeneration("proof-result-generation", "generation")))),
        },
        Limits = CreateLimits(),
        ReceiptPolicy = new BaseModuleMutationReceiptPolicy { FormatVersion = 1, Lifetime = TimeSpan.FromDays(1) },
        Checksum = BaseModuleMutationChecksum.Create(System.Security.Cryptography.SHA256.HashData(
            "proof.identity-and-generation.v1"u8)),
    });

    internal static BaseModuleGenerationCellDefinition GenerationCell { get; } = new()
    {
        Id = "proof.identity-and-generation.cell", Version = 1, OwningModuleId = "proof.module",
        Scope = BaseModuleGenerationScope.TenantAndKey, MaximumKeyUtf8Bytes = 36,
        MaximumCellsPerOperation = 1,
    };

    private static BaseModuleValue<BaseRecordId<ProofWorkItem>> CreateId() =>
        BaseModuleMutationTemplateBuilder.RecordIdFromGuid<ProofWorkItem>("create-id",
            BaseModuleMutationTemplateBuilder.Request("create-id-source", RequestProperties.CreateId));

    private static BaseModuleValue<BaseRecordId<ProofWorkItem>> PatchId() =>
        BaseModuleMutationTemplateBuilder.RecordIdFromString<ProofWorkItem>("patch-id",
            BaseModuleMutationTemplateBuilder.Request("patch-id-source", RequestProperties.PatchId));

    private static BaseModuleValue<BaseRecordId<ProofWorkItem>> DeleteId() =>
        BaseModuleMutationTemplateBuilder.RecordIdFromGuid<ProofWorkItem>("delete-id",
            BaseModuleMutationTemplateBuilder.Request("delete-id-source", RequestProperties.DeleteId));

    private static BaseModuleValue<BaseRecordId<ProofOwner>> OwnerId(string prefix) =>
        BaseModuleMutationTemplateBuilder.RecordIdFromGuid<ProofOwner>(prefix + "-owner-id",
            BaseModuleMutationTemplateBuilder.Request(prefix + "-owner-id-source", RequestProperties.OwnerId));

    private static BaseModuleRecordCapture CreateCapture() => BaseModuleMutationTemplateBuilder.CaptureRecord(
        "create-record", CreateId(), BaseModuleCapturePresence.RequireMissing);

    private static BaseModuleRecordCapture DeleteCapture() => BaseModuleMutationTemplateBuilder.CaptureRecord(
        "delete-record", DeleteId(), BaseModuleCapturePresence.RequirePresent);

    private static BaseModuleGenerationCapture GenerationCapture() => BaseModuleMutationTemplateBuilder.CaptureGeneration(
        "generation", "proof.identity-and-generation.cell",
        BaseModuleMutationTemplateBuilder.GenerationKeyFromGuid("generation-key",
            BaseModuleMutationTemplateBuilder.Request("generation-key-source", RequestProperties.GenerationKey)),
        BaseModuleGenerationAbsenceBehavior.AllowEither);

    private static BaseModuleRecordCapture PatchCapture() => BaseModuleMutationTemplateBuilder.CaptureRecord(
        "patch-record", PatchId(), BaseModuleCapturePresence.RequirePresent);

    private static BaseModuleCreateStatement CreateStatement() => BaseModuleMutationTemplateBuilder.Create(
        "create", CreateId(), BaseModuleMutationTemplateBuilder.Object<ProofWorkItem>("create-payload",
            BaseModuleMutationTemplateBuilder.Field(ProofWorkItem.Fields.Name,
                BaseModuleMutationTemplateBuilder.Request("create-name", RequestProperties.Name)),
            BaseModuleMutationTemplateBuilder.Field(ProofWorkItem.Fields.OwnerId, OwnerId("create"))));

    private static BaseModuleDeleteStatement DeleteStatement() =>
        BaseModuleMutationTemplateBuilder.Delete("delete", DeleteId());

    private static BaseModuleIncrementGenerationStatement IncrementStatement() =>
        BaseModuleMutationTemplateBuilder.IncrementGeneration("increment", "generation", true);

    private static BaseModulePatchStatement PatchStatement() => BaseModuleMutationTemplateBuilder.Patch(
        "patch", PatchId(), BaseModuleMutationTemplateBuilder.Object<ProofWorkItem>("patch-payload",
            BaseModuleMutationTemplateBuilder.Field(ProofWorkItem.Fields.Name,
                BaseModuleMutationTemplateBuilder.Request("patch-name", RequestProperties.Name)),
            BaseModuleMutationTemplateBuilder.Field(ProofWorkItem.Fields.OwnerId, OwnerId("patch"))));

    private static BaseModuleMutationLimits CreateLimits() => new()
    {
        MaximumCaptures = 8, MaximumRecordCaptures = 6, MaximumRelationTargetCaptures = 2,
        MaximumGenerationCaptures = 2, MaximumRecordMutations = 4, MaximumGenerationReads = 2,
        MaximumGenerationComparisons = 2, MaximumGenerationIncrements = 2, MaximumGuardNodes = 4,
        MaximumGuardDepth = 4, MaximumPreconditions = 4, MaximumRequestGuardEvaluations = 8,
        MaximumStaticSetMembers = 8, MaximumStaticSetComparisons = 28, MaximumDisabledCaptures = 4,
        MaximumRemovedFields = 4, MaximumStatements = 8, MaximumBranches = 4,
        MaximumExpressionNodes = 64, MaximumReadIntervals = 64, MaximumSubjectValidations = 4,
        MaximumAuthorityReads = 64, MaximumRelationChecks = 16, MaximumUniqueConstraintChecks = 16,
        MaximumRequestBytes = 65_536, MaximumSelectedBytes = 65_536, MaximumGenerationBytes = 16_384,
        MaximumEvidenceBytes = 65_536, MaximumWrittenBytes = 65_536, MaximumFactBytes = 65_536,
        MaximumJournalBytes = 65_536, MaximumReceiptBytes = 65_536, MaximumResultBytes = 16_384,
        MaximumTransientBytes = 1_048_576,
        Deadlines = new BaseAtomicMutationDeadlines
        {
            AcquisitionTimeout = TimeSpan.FromSeconds(5), TransactionTimeout = TimeSpan.FromSeconds(5),
            CommitObservationTimeout = TimeSpan.FromSeconds(5), ReceiptResolutionTimeout = TimeSpan.FromSeconds(5),
        },
    };
}

[BaseCollection("proof.owners", typeof(ProofOwnerJsonSerializerContext), SystemOwnerModuleId = "proof.module")]
internal sealed partial record ProofOwner
{
    [BaseField("proof.owner.name", MaximumUtf8Bytes = 64)]
    public required string Name { get; init; }

    [BaseField("proof.owner.note", Presence = BaseFieldPresence.Optional,
        Nullability = BaseFieldNullability.NonNullable, MaximumUtf8Bytes = 64)]
    public string? Note { get; init; }
}

internal sealed record ProofOwnerPatch
{
    [BaseField("proof.owner.patch.name", Presence = BaseFieldPresence.Optional, MaximumUtf8Bytes = 64)]
    public string? Name { get; init; }
}

[BaseCollection("proof.work-items", typeof(ConsumerJsonSerializerContext), SystemOwnerModuleId = "proof.module")]
internal sealed partial record ProofWorkItem
{
    [BaseField("proof.work.owner")]
    [BaseRelation("proof.work.owner", typeof(ProofOwner), LocalMultiplicity = BaseRelationMultiplicity.ExactlyOne,
        InverseNavigationId = "proof.owner.work")]
    public required BaseRecordId<ProofOwner> OwnerId { get; init; }

    [BaseField("proof.work.name", MaximumUtf8Bytes = 64,
        Operators = BaseFieldOperator.Equal | BaseFieldOperator.Order)]
    public required string Name { get; init; }

}

[JsonSerializable(typeof(ProofOwner))]
[JsonSerializable(typeof(ProofOwnerPatch))]
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow)]
internal sealed partial class ProofOwnerJsonSerializerContext : JsonSerializerContext;

internal sealed record IdentityAndGenerationRequest
{
    [BaseField("proof.identity.request.create-id")]
    [JsonConverter(typeof(BaseCanonicalGuidJsonConverter))]
    public required Guid CreateId { get; init; }

    [BaseField("proof.identity.request.patch-id", MinimumUtf8Bytes = 1, MaximumUtf8Bytes = 256)]
    public required string PatchId { get; init; }

    [BaseField("proof.identity.request.delete-id")]
    [JsonConverter(typeof(BaseCanonicalGuidJsonConverter))]
    public required Guid DeleteId { get; init; }

    [BaseField("proof.identity.request.owner-id")]
    [JsonConverter(typeof(BaseCanonicalGuidJsonConverter))]
    public required Guid OwnerId { get; init; }

    [BaseField("proof.identity.request.generation-key")]
    [JsonConverter(typeof(BaseCanonicalGuidJsonConverter))]
    public required Guid GenerationKey { get; init; }

    [BaseField("proof.identity.request.name", MaximumUtf8Bytes = 64)]
    public required string Name { get; init; }
}

internal sealed record IdentityAndGenerationResult
{
    [BaseField("proof.identity.result.generation")]
    public required BaseModuleGeneration Generation { get; init; }

    [BaseField("proof.identity.result.created-id")]
    [JsonConverter(typeof(BaseCanonicalGuidJsonConverter))]
    public required Guid CreatedId { get; init; }
}
