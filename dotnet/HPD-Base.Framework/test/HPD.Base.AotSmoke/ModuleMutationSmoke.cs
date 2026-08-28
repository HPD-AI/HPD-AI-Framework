using System.Text.Json.Serialization;

namespace HPD.Base.AotSmoke;

[BaseRegisteredModuleMutation("hpd.base.aot.module.increment", typeof(ModuleMutationSmokeJsonContext), typeof(ModuleMutationSmokeRequest), typeof(ModuleMutationSmokeResult), Version = 1, OwningModuleId = "hpd.base.aot", GrantId = "hpd.base.aot.module.increment")]
public static partial class ModuleMutationSmoke
{
    internal static BaseRegisteredModuleMutationDefinition Definition { get; } = BaseModuleMutationContract.Seal(new()
    {
        Id = "hpd.base.aot.module.increment", Version = 1, OwningModuleId = "hpd.base.aot", GrantId = "hpd.base.aot.module.increment",
        Audience = BaseModuleMutationAudience.Service, RequestTypeId = "hpd.base.aot.module.request", ResultTypeId = "hpd.base.aot.module.result",
        SystemCollectionIds = ["hpd.base.aot.module.records"],
        SystemSourceGrants = [new BaseModuleSystemSourceGrant { CollectionId = "hpd.base.aot.module.records", GrantId = "hpd.base.aot.module.records.source" }],
        GenerationCellIds = ["hpd.base.aot.module.generation", "hpd.base.aot.module.hostile-generation"], ImportedSubjectContractIds = [],
        Template = new BaseModuleMutationTemplate
        {
            Captures = [new BaseModuleGenerationCapture { Id = "generation", CellId = "hpd.base.aot.module.generation", Absence = BaseModuleGenerationAbsenceBehavior.AllowEither }, HostileGenerationCapture(), HostileRecordCapture(), CreateCapture(), DeleteCapture(), RecordCapture()],
            Guards = [HostileEnabledGuard()], Preconditions = [], Body = new BaseModuleMutationBlock { Statements = [new BaseModuleIncrementGenerationStatement { Id = "increment", CaptureId = "generation", CreateIfAbsent = true }, CreateRecord(), RemovalPatch(), DeleteRecord()] },
            Result = BaseModuleMutationTemplateBuilder.Result(BaseModuleMutationTemplateBuilder.ResultObject("result",
                BaseModuleMutationTemplateBuilder.Property(ResultProperties.Generation, BaseModuleMutationTemplateBuilder.ResultingGeneration("result-generation", "generation")),
                BaseModuleMutationTemplateBuilder.Property(ResultProperties.Id, BaseModuleMutationTemplateBuilder.Request("result-id", RequestProperties.Id)),
                BaseModuleMutationTemplateBuilder.Property(ResultProperties.Metadata, BaseModuleMutationTemplateBuilder.Request("result-metadata", RequestProperties.Metadata)),
                BaseModuleMutationTemplateBuilder.Property(ResultProperties.Mode, BaseModuleMutationTemplateBuilder.Request("result-mode", RequestProperties.Mode)),
                BaseModuleMutationTemplateBuilder.Property(ResultProperties.Payload, BaseModuleMutationTemplateBuilder.Request("result-payload", RequestProperties.Payload)))),
        },
        Limits = Limits(), ReceiptPolicy = new BaseModuleMutationReceiptPolicy { FormatVersion = 1, Lifetime = TimeSpan.FromDays(1) },
        Checksum = BaseModuleMutationChecksum.Create(System.Security.Cryptography.SHA256.HashData("hpd.base.aot.module.increment.v1"u8)),
    });

    private static BaseModuleValue<BaseRecordId<AotModuleRecord>> RecordId() =>
        BaseModuleMutationTemplateBuilder.RecordIdFromGuid<AotModuleRecord>("module-record-id",
            BaseModuleMutationTemplateBuilder.Request("module-request-id", RequestProperties.Id));

    private static BaseModuleRecordCapture RecordCapture() => BaseModuleMutationTemplateBuilder.CaptureRecord(
        "module-record", RecordId(), BaseModuleCapturePresence.RequirePresent);

    private static BaseModuleValue<BaseRecordId<AotModuleRecord>> CreateId() =>
        BaseModuleMutationTemplateBuilder.RecordIdFromGuid<AotModuleRecord>("module-create-record-id",
            BaseModuleMutationTemplateBuilder.Request("module-create-request-id", RequestProperties.CreateId));

    private static BaseModuleValue<BaseRecordId<AotModuleRecord>> DeleteId() =>
        BaseModuleMutationTemplateBuilder.RecordIdFromGuid<AotModuleRecord>("module-delete-record-id",
            BaseModuleMutationTemplateBuilder.Request("module-delete-request-id", RequestProperties.DeleteId));

    private static BaseModuleRecordCapture CreateCapture() => BaseModuleMutationTemplateBuilder.CaptureRecord(
        "module-create-record", CreateId(), BaseModuleCapturePresence.RequireMissing);

    private static BaseModuleRecordCapture DeleteCapture() => BaseModuleMutationTemplateBuilder.CaptureRecord(
        "module-delete-record", DeleteId(), BaseModuleCapturePresence.RequirePresent);

    private static BaseModuleValue<string> HostileId() =>
        BaseModuleMutationTemplateBuilder.Request("hostile-id", RequestProperties.HostileId);

    private static BaseModuleValueEqualsGuard HostileEnabledGuard() => BaseModuleMutationTemplateBuilder.ValueEquals(
        "hostile-enabled", BaseModuleMutationTemplateBuilder.Request("hostile-enabled-request", RequestProperties.EnableHostile),
        BaseModuleMutationTemplateBuilder.Constant("hostile-enabled-true", RequestProperties.EnableHostile.ConstantAuthority, true));

    private static BaseModuleRecordCapture HostileRecordCapture() => BaseModuleMutationTemplateBuilder.CaptureRecord(
        "hostile-record", BaseModuleMutationTemplateBuilder.RecordIdFromString<AotModuleRecord>("hostile-record-id", HostileId()),
        BaseModuleCapturePresence.AllowEither, "hostile-enabled");

    private static BaseModuleGenerationCapture HostileGenerationCapture() => BaseModuleMutationTemplateBuilder.CaptureGeneration(
        "hostile-generation", "hpd.base.aot.module.hostile-generation", HostileId(),
        BaseModuleGenerationAbsenceBehavior.AllowEither, "hostile-enabled");

    private static BaseModulePatchStatement RemovalPatch() => BaseModuleMutationTemplateBuilder.Patch(
        "module-remove-status", RecordId(), BaseModuleMutationTemplateBuilder.Object<AotModuleRecord>(
            "module-removal",
            BaseModuleMutationTemplateBuilder.Field(AotModuleRecord.Fields.ProcessedAt,
                BaseModuleMutationTemplateBuilder.LiftOptional("module-processed-at", AotModuleRecord.Fields.ProcessedAt.ModuleMutation,
                    BaseModuleMutationTemplateBuilder.Request("module-event-at", RequestProperties.EventAt))),
            BaseModuleMutationTemplateBuilder.Remove(AotModuleRecord.Fields.Status.ModuleMutation)));

    private static BaseModuleCreateStatement CreateRecord() => BaseModuleMutationTemplateBuilder.Create(
        "module-create", CreateId(), BaseModuleMutationTemplateBuilder.Object<AotModuleRecord>("module-create-payload",
            BaseModuleMutationTemplateBuilder.Field(AotModuleRecord.Fields.Name,
                BaseModuleMutationTemplateBuilder.Constant("module-create-name", AotModuleRecord.Fields.Name.ConstantAuthority, "created"))));

    private static BaseModuleDeleteStatement DeleteRecord() =>
        BaseModuleMutationTemplateBuilder.Delete("module-delete", DeleteId());

    internal static BaseModuleGenerationCellDefinition Cell { get; } = new()
    {
        Id = "hpd.base.aot.module.generation", Version = 1, OwningModuleId = "hpd.base.aot", Scope = BaseModuleGenerationScope.Application,
        MaximumKeyUtf8Bytes = 32, MaximumCellsPerOperation = 1,
    };

    internal static BaseModuleGenerationCellDefinition HostileCell { get; } = new()
    {
        Id = "hpd.base.aot.module.hostile-generation", Version = 1, OwningModuleId = "hpd.base.aot",
        Scope = BaseModuleGenerationScope.TenantAndKey, MaximumKeyUtf8Bytes = 32, MaximumCellsPerOperation = 1,
    };

    internal static BaseModuleMutationLimits Limits() => new()
    {
        MaximumCaptures = 8, MaximumRecordCaptures = 8, MaximumRelationTargetCaptures = 8, MaximumGenerationCaptures = 8, MaximumRecordMutations = 8,
        MaximumGenerationReads = 8, MaximumGenerationComparisons = 8, MaximumGenerationIncrements = 8, MaximumGuardNodes = 8, MaximumGuardDepth = 8,
        MaximumPreconditions = 8, MaximumRequestGuardEvaluations = 16, MaximumStaticSetMembers = 16, MaximumStaticSetComparisons = 120, MaximumDisabledCaptures = 8, MaximumRemovedFields = 8,
        MaximumStatements = 8, MaximumBranches = 8, MaximumExpressionNodes = 32, MaximumReadIntervals = 64, MaximumSubjectValidations = 8,
        MaximumAuthorityReads = 64, MaximumRelationChecks = 32, MaximumUniqueConstraintChecks = 32, MaximumRequestBytes = 65_536,
        MaximumSelectedBytes = 65_536, MaximumGenerationBytes = 65_536, MaximumEvidenceBytes = 65_536, MaximumWrittenBytes = 65_536,
        MaximumFactBytes = 65_536, MaximumJournalBytes = 65_536, MaximumReceiptBytes = 65_536, MaximumResultBytes = 65_536, MaximumTransientBytes = 1_048_576,
        Deadlines = new BaseAtomicMutationDeadlines { AcquisitionTimeout = TimeSpan.FromSeconds(5), TransactionTimeout = TimeSpan.FromSeconds(5), CommitObservationTimeout = TimeSpan.FromSeconds(5), ReceiptResolutionTimeout = TimeSpan.FromSeconds(5) },
    };
}

public enum AotModuleMode { [JsonStringEnumMemberName("ready")] Ready = 0, [JsonStringEnumMemberName("done")] Done = 1 }
[BaseCollection("hpd.base.aot.module.records", typeof(ModuleMutationSmokeJsonContext), SystemOwnerModuleId = "hpd.base.aot")]
public sealed partial record AotModuleRecord
{
    [BaseField("hpd.base.aot.module.record.name", MaximumUtf8Bytes = 64)] public required string Name { get; init; }
    [BaseField("hpd.base.aot.module.record.status", Presence = BaseFieldPresence.Optional, Nullability = BaseFieldNullability.NonNullable, MaximumUtf8Bytes = 32)] public string? Status { get; init; }
    [BaseField("hpd.base.aot.module.record.processed-at", Presence = BaseFieldPresence.Optional, Nullability = BaseFieldNullability.NonNullable)]
    [JsonConverter(typeof(BaseUtcDateTimeJsonConverter))] public DateTimeOffset? ProcessedAt { get; init; }
}
public sealed record ModuleMutationSmokeRequest
{
    [BaseField("hpd.base.aot.module.request.event-at")]
    [JsonConverter(typeof(BaseUtcDateTimeJsonConverter))]
    public required DateTimeOffset EventAt { get; init; }
    [BaseField("hpd.base.aot.module.request.optional-at", Presence = BaseFieldPresence.Optional, Nullability = BaseFieldNullability.NonNullable)]
    [JsonConverter(typeof(BaseUtcDateTimeJsonConverter))]
    public DateTimeOffset? OptionalAt { get; init; }
    [BaseField("hpd.base.aot.module.request.optional-target", Presence = BaseFieldPresence.Optional, Nullability = BaseFieldNullability.NonNullable)]
    public BaseRecordId<AotModuleRecord>? OptionalTarget { get; init; }
    [BaseField("hpd.base.aot.module.request.enable-hostile")] public required bool EnableHostile { get; init; }
    [BaseField("hpd.base.aot.module.request.hostile-id", MinimumUtf8Bytes = 1, MaximumUtf8Bytes = 32)] public required string HostileId { get; init; }
    [BaseField("hpd.base.aot.module.request.id")][JsonConverter(typeof(BaseCanonicalGuidJsonConverter))] public required Guid Id { get; init; }
    [BaseField("hpd.base.aot.module.request.create-id")][JsonConverter(typeof(BaseCanonicalGuidJsonConverter))] public required Guid CreateId { get; init; }
    [BaseField("hpd.base.aot.module.request.delete-id")][JsonConverter(typeof(BaseCanonicalGuidJsonConverter))] public required Guid DeleteId { get; init; }
    [BaseField("hpd.base.aot.module.request.payload", MaximumBytes = 32)] public required BaseBinary Payload { get; init; }
    [BaseField("hpd.base.aot.module.request.metadata", MaximumCanonicalJsonBytes = 256, JsonShape = BaseJsonShape.Object, MaximumJsonDepth = 4, MaximumJsonArrayItems = 8, MaximumJsonObjectProperties = 8, MaximumJsonTotalNodes = 16, MaximumJsonTotalStringUtf8Bytes = 64, MaximumJsonTotalNameUtf8Bytes = 64)] public required BaseCanonicalJson Metadata { get; init; }
    [BaseField("hpd.base.aot.module.request.mode", AllowedEnumLiterals = ["ready", "done"])][JsonConverter(typeof(BaseClosedEnumJsonConverter<AotModuleMode>))] public required AotModuleMode Mode { get; init; }
}
public sealed record ModuleMutationSmokeResult
{
    [BaseField("hpd.base.aot.module.result.generation")] public required BaseModuleGeneration Generation { get; init; }
    [BaseField("hpd.base.aot.module.result.id")][JsonConverter(typeof(BaseCanonicalGuidJsonConverter))] public required Guid Id { get; init; }
    [BaseField("hpd.base.aot.module.result.payload", MaximumBytes = 32)] public required BaseBinary Payload { get; init; }
    [BaseField("hpd.base.aot.module.result.metadata", MaximumCanonicalJsonBytes = 256, JsonShape = BaseJsonShape.Object, MaximumJsonDepth = 4, MaximumJsonArrayItems = 8, MaximumJsonObjectProperties = 8, MaximumJsonTotalNodes = 16, MaximumJsonTotalStringUtf8Bytes = 64, MaximumJsonTotalNameUtf8Bytes = 64)] public required BaseCanonicalJson Metadata { get; init; }
    [BaseField("hpd.base.aot.module.result.mode", AllowedEnumLiterals = ["ready", "done"])][JsonConverter(typeof(BaseClosedEnumJsonConverter<AotModuleMode>))] public required AotModuleMode Mode { get; init; }
}
[JsonSerializable(typeof(ModuleMutationSmokeRequest))]
[JsonSerializable(typeof(ModuleMutationSmokeResult))]
[JsonSerializable(typeof(AotModuleRecord))]
[JsonSerializable(typeof(BaseRecordId<AotModuleRecord>))]
[JsonSourceGenerationOptions(DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
public sealed partial class ModuleMutationSmokeJsonContext : JsonSerializerContext;
