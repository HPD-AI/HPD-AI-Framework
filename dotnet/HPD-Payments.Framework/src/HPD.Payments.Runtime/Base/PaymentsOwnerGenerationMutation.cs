using System.Text.Json.Serialization;
using HPD.Base;

namespace HPD.Payments.Runtime.Base;

/// <summary>Provides the BASE registration used to compare-bind one Payments owner generation.</summary>
[BaseRegisteredModuleMutation(
    "hpd.payments.owner-generation.advance",
    typeof(PaymentsBaseJsonContext),
    typeof(AdvanceOwnerGenerationRequest),
    typeof(AdvanceOwnerGenerationResult),
    Version = 1,
    OwningModuleId = "hpd.payments",
    GrantId = "hpd.payments.owner-generation.advance")]
public static partial class PaymentsOwnerGenerationMutation
{
    /// <summary>Gets the immutable operation definition installed by a Payments host.</summary>
    public static BaseRegisteredModuleMutationDefinition Definition { get; } = new()
    {
        Id = "hpd.payments.owner-generation.advance",
        Version = 1,
        OwningModuleId = "hpd.payments",
        GrantId = "hpd.payments.owner-generation.advance",
        Audience = BaseModuleMutationAudience.Service,
        RequestTypeId = "hpd.payments.owner-generation.advance.request",
        ResultTypeId = "hpd.payments.owner-generation.advance.result",
        SystemCollectionIds = [],
        GenerationCellIds = ["hpd.payments.owner-generation"],
        ImportedSubjectContractIds = [],
        Template = new BaseModuleMutationTemplate
        {
            Captures = [new BaseModuleGenerationCapture
            {
                Id = "owner-generation",
                CellId = "hpd.payments.owner-generation",
                Key = new BaseModuleRequestPropertyExpression
                {
                    Id = "owner-key",
                    ResultTypeId = "string",
                    Property = new BaseModuleRequestPropertyReference
                    {
                        StablePropertyPath = ["hpd.payments.owner-generation.owner-id"],
                        DeclaredTypeId = "string",
                    },
                },
                Absence = BaseModuleGenerationAbsenceBehavior.AllowEither,
            }],
            Guards = [],
            Body = new BaseModuleMutationBlock
            {
                Statements = [new BaseModuleIncrementGenerationStatement
                {
                    Id = "advance-owner-generation",
                    CaptureId = "owner-generation",
                    CreateIfAbsent = true,
                }],
            },
            Result = new BaseModuleResultProjection
            {
                Value = new BaseModuleObjectExpression
                {
                    Id = "result",
                    ResultTypeId = "hpd.payments.owner-generation.advance.result",
                    Properties = [new BaseModuleObjectPropertyExpression
                    {
                        StablePropertyId = "hpd.payments.owner-generation.result.generation",
                        Value = new BaseModuleResultingGenerationExpression
                        {
                            Id = "result-generation",
                            ResultTypeId = "string",
                            CaptureId = "owner-generation",
                        },
                    }],
                },
            },
        },
        Limits = Limits(),
        ReceiptPolicy = new BaseModuleMutationReceiptPolicy { FormatVersion = 1, Lifetime = TimeSpan.FromDays(30) },
        Checksum = BaseModuleMutationChecksum.Create(System.Security.Cryptography.SHA256.HashData("hpd.payments.owner-generation.advance.v1"u8)),
    };

    /// <summary>Gets the provider-owned owner-generation cell installed by a Payments host.</summary>
    public static BaseModuleGenerationCellDefinition Cell { get; } = new()
    {
        Id = "hpd.payments.owner-generation",
        Version = 1,
        OwningModuleId = "hpd.payments",
        Scope = BaseModuleGenerationScope.TenantAndKey,
        MaximumKeyUtf8Bytes = 128,
        MaximumCellsPerOperation = 1,
    };

    private static BaseModuleMutationLimits Limits() => new()
    {
        MaximumCaptures = 4, MaximumRecordCaptures = 0, MaximumRelationTargetCaptures = 0, MaximumGenerationCaptures = 1, MaximumRecordMutations = 0,
        MaximumGenerationReads = 1, MaximumGenerationComparisons = 1, MaximumGenerationIncrements = 1, MaximumGuardNodes = 4, MaximumGuardDepth = 4,
        MaximumStatements = 2, MaximumBranches = 2, MaximumExpressionNodes = 8, MaximumReadIntervals = 4, MaximumSubjectValidations = 0,
        MaximumAuthorityReads = 4, MaximumRelationChecks = 0, MaximumUniqueConstraintChecks = 0, MaximumRequestBytes = 4096,
        MaximumSelectedBytes = 0, MaximumGenerationBytes = 4096, MaximumEvidenceBytes = 4096, MaximumWrittenBytes = 4096,
        MaximumFactBytes = 4096, MaximumJournalBytes = 4096, MaximumReceiptBytes = 8192, MaximumResultBytes = 4096, MaximumTransientBytes = 65536,
        Deadlines = new BaseAtomicMutationDeadlines
        {
            AcquisitionTimeout = TimeSpan.FromSeconds(5), TransactionTimeout = TimeSpan.FromSeconds(5),
            CommitObservationTimeout = TimeSpan.FromSeconds(5), ReceiptResolutionTimeout = TimeSpan.FromSeconds(5),
        },
    };
}

/// <summary>Identifies the Payments owner whose generation must advance.</summary>
public sealed record AdvanceOwnerGenerationRequest
{
    /// <summary>Gets the module-owned canonical owner identity.</summary>
    [BaseField("hpd.payments.owner-generation.owner-id")]
    public required string OwnerId { get; init; }
}

/// <summary>Returns the newly committed opaque owner generation.</summary>
public sealed record AdvanceOwnerGenerationResult
{
    /// <summary>Gets the newly committed generation in its canonical wire representation.</summary>
    [BaseField("hpd.payments.owner-generation.result.generation")]
    public required string Generation { get; init; }
}

[JsonSerializable(typeof(AdvanceOwnerGenerationRequest))]
[JsonSerializable(typeof(AdvanceOwnerGenerationResult))]
internal sealed partial class PaymentsBaseJsonContext : JsonSerializerContext;
