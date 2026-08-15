using System.Text.Json.Serialization;

namespace HPD.Base.AotSmoke;

[BaseRegisteredModuleMutation("hpd.base.aot.module.increment", typeof(ModuleMutationSmokeJsonContext), typeof(ModuleMutationSmokeRequest), typeof(ModuleMutationSmokeResult), Version = 1, OwningModuleId = "hpd.base.aot.module", GrantId = "hpd.base.aot.module.increment")]
public static partial class ModuleMutationSmoke
{
    internal static BaseRegisteredModuleMutationDefinition Definition { get; } = BaseModuleMutationContract.Seal(new()
    {
        Id = "hpd.base.aot.module.increment", Version = 1, OwningModuleId = "hpd.base.aot.module", GrantId = "hpd.base.aot.module.increment",
        Audience = BaseModuleMutationAudience.Service, RequestTypeId = "hpd.base.aot.module.request", ResultTypeId = "hpd.base.aot.module.result",
        SystemCollectionIds = [], GenerationCellIds = ["hpd.base.aot.module.generation"], ImportedSubjectContractIds = [],
        Template = new BaseModuleMutationTemplate
        {
            Captures = [new BaseModuleGenerationCapture { Id = "generation", CellId = "hpd.base.aot.module.generation", Absence = BaseModuleGenerationAbsenceBehavior.AllowEither }],
            Guards = [], Body = new BaseModuleMutationBlock { Statements = [new BaseModuleIncrementGenerationStatement { Id = "increment", CaptureId = "generation", CreateIfAbsent = true }] },
            Result = new BaseModuleResultProjection { Value = new BaseModuleObjectExpression
            {
                Id = "result", ResultTypeId = "hpd.base.aot.module.result", Properties = [new BaseModuleObjectPropertyExpression
                {
                    StablePropertyId = "hpd.base.aot.module.result.generation", Value = new BaseModuleResultingGenerationExpression { Id = "result-generation", ResultTypeId = "string", CaptureId = "generation" },
                }],
            } },
        },
        Limits = Limits(), ReceiptPolicy = new BaseModuleMutationReceiptPolicy { FormatVersion = 1, Lifetime = TimeSpan.FromDays(1) },
        Checksum = BaseModuleMutationChecksum.Create(System.Security.Cryptography.SHA256.HashData("hpd.base.aot.module.increment.v1"u8)),
    });

    internal static BaseModuleGenerationCellDefinition Cell { get; } = new()
    {
        Id = "hpd.base.aot.module.generation", Version = 1, OwningModuleId = "hpd.base.aot.module", Scope = BaseModuleGenerationScope.Application,
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

public sealed record ModuleMutationSmokeRequest { [BaseField("hpd.base.aot.module.request.marker")] public string? Marker { get; init; } }
public sealed record ModuleMutationSmokeResult { [BaseField("hpd.base.aot.module.result.generation")] public required string Generation { get; init; } }
[JsonSerializable(typeof(ModuleMutationSmokeRequest))]
[JsonSerializable(typeof(ModuleMutationSmokeResult))]
public sealed partial class ModuleMutationSmokeJsonContext : JsonSerializerContext;
