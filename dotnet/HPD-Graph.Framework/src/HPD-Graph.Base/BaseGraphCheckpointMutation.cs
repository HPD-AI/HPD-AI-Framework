using System.Collections.Immutable;
using System.Text.Json.Serialization;
using HPD.Base;

namespace HPD.Graph.Base;

/// <summary>Private authoritative Graph checkpoint stored by HPD.Base.</summary>
[BaseCollection("hpd.graph.checkpoints", typeof(BaseGraphActivationJsonContext), SystemOwnerModuleId = "hpd.graph")]
public sealed partial record BaseGraphCheckpointRecord
{
    /// <summary>Gets the stable checkpoint identity.</summary>
    [BaseField("hpd.graph.checkpoint.id")]
    public required string CheckpointId { get; init; }

    /// <summary>Gets the logical execution identity.</summary>
    [BaseField("hpd.graph.checkpoint.execution-id")]
    public required string ExecutionId { get; init; }

    /// <summary>Gets the exact graph identity.</summary>
    [BaseField("hpd.graph.checkpoint.graph-id")]
    public required string GraphId { get; init; }

    /// <summary>Gets the exact graph version.</summary>
    [BaseField("hpd.graph.checkpoint.graph-version")]
    public required string GraphVersion { get; init; }

    /// <summary>Gets the graph-definition checksum encoded as lowercase hexadecimal.</summary>
    [BaseField("hpd.graph.checkpoint.graph-checksum")]
    public required string GraphChecksum { get; init; }

    /// <summary>Gets the canonical serialized checkpoint.</summary>
    [BaseField("hpd.graph.checkpoint.payload")]
    public required string CanonicalCheckpoint { get; init; }
}

/// <summary>Persists one graph checkpoint through the shared atomic module protocol.</summary>
[BaseRegisteredModuleMutation(
    "hpd.graph.checkpoint.persist",
    typeof(BaseGraphActivationJsonContext),
    typeof(BaseGraphCheckpointPersistRequest),
    typeof(BaseGraphCheckpointPersistResult),
    Version = 1,
    OwningModuleId = "hpd.graph",
    GrantId = "hpd.graph.checkpoint.persist")]
public static partial class BaseGraphCheckpointMutation
{
    /// <summary>Gets the sealed checkpoint-persistence operation.</summary>
    public static BaseRegisteredModuleMutationDefinition Definition { get; } = BaseModuleMutationContract.Seal(new()
    {
        Id = "hpd.graph.checkpoint.persist",
        Version = 1,
        OwningModuleId = "hpd.graph",
        GrantId = "hpd.graph.checkpoint.persist",
        Audience = BaseModuleMutationAudience.Service,
        RequestTypeId = "hpd.graph.checkpoint.persist.request",
        ResultTypeId = "hpd.graph.checkpoint.persist.result",
        SystemCollectionIds = [BaseGraphCheckpointRecord.Collection.Id],
        SystemSourceGrants =
        [
            new()
            {
                CollectionId = BaseGraphCheckpointRecord.Collection.Id,
                GrantId = "hpd.graph.checkpoint.source",
            },
        ],
        GenerationCellIds = [],
        ImportedSubjectContractIds = [],
        Template = Template(),
        Limits = Limits(),
        ReceiptPolicy = new BaseModuleMutationReceiptPolicy
        {
            FormatVersion = 1,
            Lifetime = TimeSpan.FromDays(30),
        },
        Checksum = BaseModuleMutationChecksum.Create(new byte[BaseModuleMutationChecksum.Length]),
    });

    private static BaseModuleMutationTemplate Template()
    {
        BaseModuleValue<string> CheckpointId(string id) => BaseModuleMutationTemplateBuilder.Request(id, RequestProperties.CheckpointId);
        BaseModuleValue<BaseRecordId<BaseGraphCheckpointRecord>> RecordId(string id) =>
            BaseModuleMutationTemplateBuilder.RecordIdFromString<BaseGraphCheckpointRecord>(id + ".record", CheckpointId(id + ".value"));
        BaseModuleRecordObject<BaseGraphCheckpointRecord> Payload(string id) =>
            BaseModuleMutationTemplateBuilder.Object<BaseGraphCheckpointRecord>(id,
                BaseModuleMutationTemplateBuilder.Field(BaseGraphCheckpointRecord.Fields.ExecutionId,
                    BaseModuleMutationTemplateBuilder.Request(id + ".execution", RequestProperties.ExecutionId)),
                BaseModuleMutationTemplateBuilder.Field(BaseGraphCheckpointRecord.Fields.GraphChecksum,
                    BaseModuleMutationTemplateBuilder.Request(id + ".checksum", RequestProperties.GraphChecksum)),
                BaseModuleMutationTemplateBuilder.Field(BaseGraphCheckpointRecord.Fields.GraphId,
                    BaseModuleMutationTemplateBuilder.Request(id + ".graph", RequestProperties.GraphId)),
                BaseModuleMutationTemplateBuilder.Field(BaseGraphCheckpointRecord.Fields.GraphVersion,
                    BaseModuleMutationTemplateBuilder.Request(id + ".version", RequestProperties.GraphVersion)),
                BaseModuleMutationTemplateBuilder.Field(BaseGraphCheckpointRecord.Fields.CheckpointId, CheckpointId(id + ".id")),
                BaseModuleMutationTemplateBuilder.Field(BaseGraphCheckpointRecord.Fields.CanonicalCheckpoint,
                    BaseModuleMutationTemplateBuilder.Request(id + ".payload", RequestProperties.CanonicalCheckpoint)));
        return new BaseModuleMutationTemplate
        {
            Captures =
            [
                BaseModuleMutationTemplateBuilder.CaptureRecord(
                    "checkpoint", RecordId("capture.id"), BaseModuleCapturePresence.AllowEither),
            ],
            Guards = [],
            Preconditions = [],
            Body = new BaseModuleMutationBlock
            {
                Statements =
                [
                    BaseModuleMutationTemplateBuilder.Upsert(
                        "persist", RecordId("write.id"), Payload("create"), Payload("update"), RecordUpsertUpdateMode.Replace),
                ],
            },
            Result = BaseModuleMutationTemplateBuilder.Result(
                BaseModuleMutationTemplateBuilder.ResultObject(
                    "result",
                    BaseModuleMutationTemplateBuilder.Property(ResultProperties.CheckpointId, CheckpointId("result.id")))),
        };
    }

    private static BaseModuleMutationLimits Limits() => new()
    {
        MaximumCaptures = 1,
        MaximumDisabledCaptures = 0,
        MaximumRecordCaptures = 1,
        MaximumRelationTargetCaptures = 1,
        MaximumGenerationCaptures = 1,
        MaximumRecordMutations = 1,
        MaximumGenerationReads = 1,
        MaximumGenerationComparisons = 1,
        MaximumGenerationIncrements = 1,
        MaximumGuardNodes = 1,
        MaximumGuardDepth = 8,
        MaximumPreconditions = 0,
        MaximumRequestGuardEvaluations = 0,
        MaximumStaticSetMembers = 0,
        MaximumStaticSetComparisons = 0,
        MaximumStatements = 1,
        MaximumBranches = 1,
        MaximumRemovedFields = 0,
        MaximumExpressionNodes = 32,
        MaximumReadIntervals = 4,
        MaximumSubjectValidations = 1,
        MaximumAuthorityReads = 4,
        MaximumRelationChecks = 1,
        MaximumUniqueConstraintChecks = 1,
        MaximumRequestBytes = 1_048_576,
        MaximumSelectedBytes = 1_048_576,
        MaximumGenerationBytes = 1,
        MaximumEvidenceBytes = 65_536,
        MaximumWrittenBytes = 1_048_576,
        MaximumFactBytes = 1_048_576,
        MaximumJournalBytes = 1_048_576,
        MaximumReceiptBytes = 1_048_576,
        MaximumResultBytes = 4_096,
        MaximumTransientBytes = 4_194_304,
        Deadlines = new BaseAtomicMutationDeadlines
        {
            AcquisitionTimeout = TimeSpan.FromSeconds(5),
            TransactionTimeout = TimeSpan.FromSeconds(30),
            CommitObservationTimeout = TimeSpan.FromSeconds(30),
            ReceiptResolutionTimeout = TimeSpan.FromSeconds(30),
        },
    };
}

/// <summary>Contains the canonical checkpoint state persisted by Graph.</summary>
public sealed record BaseGraphCheckpointPersistRequest
{
    /// <summary>Gets the stable checkpoint identity.</summary>
    [BaseField("hpd.graph.checkpoint.persist.checkpoint-id")]
    public required string CheckpointId { get; init; }
    /// <summary>Gets the logical execution identity.</summary>
    [BaseField("hpd.graph.checkpoint.persist.execution-id")]
    public required string ExecutionId { get; init; }
    /// <summary>Gets the exact graph identity.</summary>
    [BaseField("hpd.graph.checkpoint.persist.graph-id")]
    public required string GraphId { get; init; }
    /// <summary>Gets the exact graph version.</summary>
    [BaseField("hpd.graph.checkpoint.persist.graph-version")]
    public required string GraphVersion { get; init; }
    /// <summary>Gets the graph checksum as lowercase hexadecimal.</summary>
    [BaseField("hpd.graph.checkpoint.persist.graph-checksum")]
    public required string GraphChecksum { get; init; }
    /// <summary>Gets the canonical checkpoint JSON.</summary>
    [BaseField("hpd.graph.checkpoint.persist.payload")]
    public required string CanonicalCheckpoint { get; init; }
}

/// <summary>Returns the checkpoint identity committed by the atomic operation.</summary>
public sealed record BaseGraphCheckpointPersistResult
{
    /// <summary>Gets the committed checkpoint identity.</summary>
    [BaseField("hpd.graph.checkpoint.persist.result.checkpoint-id")]
    public required string CheckpointId { get; init; }
}
