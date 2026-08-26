using System.Text.Json.Serialization;

namespace HPD.Base.AotSmoke;

[BaseRegisteredModuleMutation("hpd.base.aot.module.increment", typeof(ModuleMutationSmokeJsonContext), typeof(ModuleMutationSmokeRequest), typeof(ModuleMutationSmokeResult), Version = 1, OwningModuleId = "hpd.base.aot", GrantId = "hpd.base.aot.module.increment")]
public static partial class ModuleMutationSmoke
{
    internal static BaseRegisteredModuleMutationDefinition Definition { get; } = BaseModuleMutationContract.Seal(new()
    {
        Id = "hpd.base.aot.module.increment", Version = 1, OwningModuleId = "hpd.base.aot", GrantId = "hpd.base.aot.module.increment",
        Audience = BaseModuleMutationAudience.Service, RequestTypeId = "hpd.base.aot.module.request", ResultTypeId = "hpd.base.aot.module.result",
        SystemCollectionIds = [], SystemSourceGrants = [], GenerationCellIds = ["hpd.base.aot.module.generation"], ImportedSubjectContractIds = [],
        Template = new BaseModuleMutationTemplate
        {
            Captures = [new BaseModuleGenerationCapture { Id = "generation", CellId = "hpd.base.aot.module.generation", Absence = BaseModuleGenerationAbsenceBehavior.AllowEither }],
            Guards = [], Body = new BaseModuleMutationBlock { Statements = [new BaseModuleIncrementGenerationStatement { Id = "increment", CaptureId = "generation", CreateIfAbsent = true }] },
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

    internal static BaseModuleGenerationCellDefinition Cell { get; } = new()
    {
        Id = "hpd.base.aot.module.generation", Version = 1, OwningModuleId = "hpd.base.aot", Scope = BaseModuleGenerationScope.Application,
        MaximumKeyUtf8Bytes = 32, MaximumCellsPerOperation = 1,
    };

    private static BaseModuleMutationLimits Limits() => new()
    {
        MaximumCaptures = 8, MaximumRecordCaptures = 8, MaximumRelationTargetCaptures = 8, MaximumGenerationCaptures = 8, MaximumRecordMutations = 8,
        MaximumGenerationReads = 8, MaximumGenerationComparisons = 8, MaximumGenerationIncrements = 8, MaximumGuardNodes = 8, MaximumGuardDepth = 8,
        MaximumStatements = 8, MaximumBranches = 8, MaximumExpressionNodes = 32, MaximumReadIntervals = 16, MaximumSubjectValidations = 8,
        MaximumAuthorityReads = 16, MaximumRelationChecks = 8, MaximumUniqueConstraintChecks = 8, MaximumRequestBytes = 4096,
        MaximumSelectedBytes = 4096, MaximumGenerationBytes = 4096, MaximumEvidenceBytes = 4096, MaximumWrittenBytes = 4096,
        MaximumFactBytes = 4096, MaximumJournalBytes = 4096, MaximumReceiptBytes = 4096, MaximumResultBytes = 4096, MaximumTransientBytes = 65536,
        Deadlines = new BaseAtomicMutationDeadlines { AcquisitionTimeout = TimeSpan.FromSeconds(5), TransactionTimeout = TimeSpan.FromSeconds(5), CommitObservationTimeout = TimeSpan.FromSeconds(5), ReceiptResolutionTimeout = TimeSpan.FromSeconds(5) },
    };
}

public enum AotModuleMode { [JsonStringEnumMemberName("ready")] Ready = 0, [JsonStringEnumMemberName("done")] Done = 1 }
public sealed record ModuleMutationSmokeRequest
{
    [BaseField("hpd.base.aot.module.request.id")][JsonConverter(typeof(BaseCanonicalGuidJsonConverter))] public required Guid Id { get; init; }
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
public sealed partial class ModuleMutationSmokeJsonContext : JsonSerializerContext;
