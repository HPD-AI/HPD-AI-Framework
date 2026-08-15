using System.Collections.Immutable;
using System.Text.Json.Serialization;
using HPD.Base;

namespace HPD.Payments.Runtime.Base;

/// <summary>Private Payments aggregate state guarded by the registered BASE operation.</summary>
[BaseCollection("hpd.payments.owner-state", typeof(PaymentsBaseJsonContext), SystemOwnerModuleId = "hpd.payments")]
public sealed partial record PaymentsOwnerState
{
    /// <summary>Gets the canonical Payments owner identity.</summary>
    [BaseField("hpd.payments.owner-state.owner-id")]
    public required string OwnerId { get; init; }

    /// <summary>Gets the last identified operation applied to this owner.</summary>
    [BaseField("hpd.payments.owner-state.last-operation")]
    public required string LastOperation { get; init; }
}

/// <summary>Private Payments ledger-head state guarded by the registered BASE operation.</summary>
[BaseCollection("hpd.payments.ledger-head", typeof(PaymentsBaseJsonContext), SystemOwnerModuleId = "hpd.payments")]
public sealed partial record PaymentsLedgerHead
{
    /// <summary>Gets the canonical Payments owner identity.</summary>
    [BaseField("hpd.payments.ledger-head.owner-id")]
    public required string OwnerId { get; init; }

    /// <summary>Gets the last identified operation applied to this ledger head.</summary>
    [BaseField("hpd.payments.ledger-head.last-operation")]
    public required string LastOperation { get; init; }
}

/// <summary>Atomically compare-binds both Payments aggregate generations and private records.</summary>
[BaseRegisteredModuleMutation(
    "hpd.payments.owner-ledger.advance",
    typeof(PaymentsBaseJsonContext),
    typeof(AdvanceOwnerGenerationRequest),
    typeof(AdvanceOwnerGenerationResult),
    Version = 1,
    OwningModuleId = "hpd.payments",
    GrantId = "hpd.payments.owner-ledger.advance")]
public static partial class PaymentsOwnerGenerationMutation
{
    private const string OwnerCellId = "hpd.payments.owner-generation";
    private const string LedgerCellId = "hpd.payments.ledger-generation";

    /// <summary>Gets the immutable operation definition installed by a Payments host.</summary>
    public static BaseRegisteredModuleMutationDefinition Definition { get; } = BaseModuleMutationContract.Seal(new()
    {
        Id = "hpd.payments.owner-ledger.advance",
        Version = 1,
        OwningModuleId = "hpd.payments",
        GrantId = "hpd.payments.owner-ledger.advance",
        Audience = BaseModuleMutationAudience.Service,
        RequestTypeId = "hpd.payments.owner-ledger.advance.request",
        ResultTypeId = "hpd.payments.owner-ledger.advance.result",
        SystemCollectionIds = [PaymentsLedgerHead.Collection.Id, PaymentsOwnerState.Collection.Id],
        GenerationCellIds = [LedgerCellId, OwnerCellId],
        ImportedSubjectContractIds = [],
        Template = Template(),
        Limits = Limits(),
        ReceiptPolicy = new BaseModuleMutationReceiptPolicy { FormatVersion = 1, Lifetime = TimeSpan.FromDays(30) },
        Checksum = BaseModuleMutationChecksum.Create(new byte[BaseModuleMutationChecksum.Length]),
    });

    /// <summary>Gets the provider-owned owner-generation cell installed by a Payments host.</summary>
    public static BaseModuleGenerationCellDefinition OwnerCell { get; } = Cell(OwnerCellId);

    /// <summary>Gets the provider-owned ledger-generation cell installed by a Payments host.</summary>
    public static BaseModuleGenerationCellDefinition LedgerCell { get; } = Cell(LedgerCellId);

    private static BaseModuleGenerationCellDefinition Cell(string id) => new()
    {
        Id = id, Version = 1, OwningModuleId = "hpd.payments",
        Scope = BaseModuleGenerationScope.TenantAndKey, MaximumKeyUtf8Bytes = 128, MaximumCellsPerOperation = 2,
    };

    private static BaseModuleMutationTemplate Template()
    {
        BaseModuleValueExpression OwnerId(string id) => Request(id, "hpd.payments.owner-ledger.owner-id", "string");
        BaseModuleValueExpression OperationId(string id) => Request(id, "hpd.payments.owner-ledger.operation-id", "string");
        BaseModuleValueExpression Expected(string id, string path) => Request(id, path, "base.moduleGeneration?");
        BaseModuleObjectExpression Payload(string id, string ownerField, string operationField) => new()
        {
            Id = id, ResultTypeId = id,
            Properties =
            [
                new() { StablePropertyId = ownerField, Value = OwnerId(id + ".owner") },
                new() { StablePropertyId = operationField, Value = OperationId(id + ".operation") },
            ],
        };

        return new BaseModuleMutationTemplate
        {
            Captures =
            [
                new BaseModuleRecordCapture { Id = "owner-record", CollectionId = PaymentsOwnerState.Collection.Id, RecordId = OwnerId("capture.owner-record"), Presence = BaseModuleCapturePresence.AllowEither },
                new BaseModuleRecordCapture { Id = "ledger-record", CollectionId = PaymentsLedgerHead.Collection.Id, RecordId = OwnerId("capture.ledger-record"), Presence = BaseModuleCapturePresence.AllowEither },
                new BaseModuleGenerationCapture { Id = "owner-generation", CellId = OwnerCellId, Key = OwnerId("capture.owner-generation"), Absence = BaseModuleGenerationAbsenceBehavior.AllowEither },
                new BaseModuleGenerationCapture { Id = "ledger-generation", CellId = LedgerCellId, Key = OwnerId("capture.ledger-generation"), Absence = BaseModuleGenerationAbsenceBehavior.AllowEither },
            ],
            Guards =
            [
                new BaseModuleGenerationGuard { Id = "owner-missing", CaptureId = "owner-generation", Comparison = BaseModuleGenerationComparisonKind.MustBeMissing },
                new BaseModuleGenerationGuard { Id = "owner-equal", CaptureId = "owner-generation", Comparison = BaseModuleGenerationComparisonKind.MustEqual, Expected = Expected("guard.owner.expected", "hpd.payments.owner-ledger.expected-owner-generation") },
                new BaseModuleLogicalGuard { Id = "owner-admitted", Kind = BaseModuleLogicalGuardKind.Or, ChildGuardIds = ["owner-missing", "owner-equal"] },
                new BaseModuleGenerationGuard { Id = "ledger-missing", CaptureId = "ledger-generation", Comparison = BaseModuleGenerationComparisonKind.MustBeMissing },
                new BaseModuleGenerationGuard { Id = "ledger-equal", CaptureId = "ledger-generation", Comparison = BaseModuleGenerationComparisonKind.MustEqual, Expected = Expected("guard.ledger.expected", "hpd.payments.owner-ledger.expected-ledger-generation") },
                new BaseModuleLogicalGuard { Id = "ledger-admitted", Kind = BaseModuleLogicalGuardKind.Or, ChildGuardIds = ["ledger-missing", "ledger-equal"] },
            ],
            Body = new BaseModuleMutationBlock
            {
                Statements =
                [
                    new BaseModuleRequireStatement { Id = "require-owner", GuardId = "owner-admitted", RequirementId = "hpd.payments.owner-generation.conflict" },
                    new BaseModuleRequireStatement { Id = "require-ledger", GuardId = "ledger-admitted", RequirementId = "hpd.payments.ledger-generation.conflict" },
                    new BaseModuleUpsertStatement
                    {
                        Id = "write-owner", CollectionId = PaymentsOwnerState.Collection.Id, RecordId = OwnerId("write.owner.id"),
                        Create = Payload("owner.create", "hpd.payments.owner-state.owner-id", "hpd.payments.owner-state.last-operation"),
                        Update = Payload("owner.update", "hpd.payments.owner-state.owner-id", "hpd.payments.owner-state.last-operation"),
                        UpdateMode = RecordUpsertUpdateMode.Replace,
                    },
                    new BaseModuleUpsertStatement
                    {
                        Id = "write-ledger", CollectionId = PaymentsLedgerHead.Collection.Id, RecordId = OwnerId("write.ledger.id"),
                        Create = Payload("ledger.create", "hpd.payments.ledger-head.owner-id", "hpd.payments.ledger-head.last-operation"),
                        Update = Payload("ledger.update", "hpd.payments.ledger-head.owner-id", "hpd.payments.ledger-head.last-operation"),
                        UpdateMode = RecordUpsertUpdateMode.Replace,
                    },
                    new BaseModuleIncrementGenerationStatement { Id = "advance-owner", CaptureId = "owner-generation", CreateIfAbsent = true },
                    new BaseModuleIncrementGenerationStatement { Id = "advance-ledger", CaptureId = "ledger-generation", CreateIfAbsent = true },
                ],
            },
            Result = new BaseModuleResultProjection
            {
                Value = new BaseModuleObjectExpression
                {
                    Id = "result", ResultTypeId = "hpd.payments.owner-ledger.advance.result",
                    Properties =
                    [
                        new() { StablePropertyId = "hpd.payments.owner-ledger.result.owner-generation", Value = new BaseModuleResultingGenerationExpression { Id = "result.owner-generation", ResultTypeId = "base.moduleGeneration", CaptureId = "owner-generation" } },
                        new() { StablePropertyId = "hpd.payments.owner-ledger.result.ledger-generation", Value = new BaseModuleResultingGenerationExpression { Id = "result.ledger-generation", ResultTypeId = "base.moduleGeneration", CaptureId = "ledger-generation" } },
                    ],
                },
            },
        };
    }

    private static BaseModuleRequestPropertyExpression Request(string id, string path, string type) => new()
    {
        Id = id, ResultTypeId = type,
        Property = new BaseModuleRequestPropertyReference { StablePropertyPath = [path], DeclaredTypeId = type },
    };

    private static BaseModuleMutationLimits Limits() => new()
    {
        MaximumCaptures = 4, MaximumRecordCaptures = 2, MaximumRelationTargetCaptures = 1, MaximumGenerationCaptures = 2, MaximumRecordMutations = 2,
        MaximumGenerationReads = 2, MaximumGenerationComparisons = 4, MaximumGenerationIncrements = 2, MaximumGuardNodes = 8, MaximumGuardDepth = 4,
        MaximumStatements = 8, MaximumBranches = 2, MaximumExpressionNodes = 64, MaximumReadIntervals = 8, MaximumSubjectValidations = 1,
        MaximumAuthorityReads = 16, MaximumRelationChecks = 1, MaximumUniqueConstraintChecks = 1, MaximumRequestBytes = 4096,
        MaximumSelectedBytes = 65_536, MaximumGenerationBytes = 4096, MaximumEvidenceBytes = 65_536, MaximumWrittenBytes = 65_536,
        MaximumFactBytes = 65_536, MaximumJournalBytes = 65_536, MaximumReceiptBytes = 65_536, MaximumResultBytes = 4096, MaximumTransientBytes = 262_144,
        Deadlines = new BaseAtomicMutationDeadlines
        {
            AcquisitionTimeout = TimeSpan.FromSeconds(5), TransactionTimeout = TimeSpan.FromSeconds(30),
            CommitObservationTimeout = TimeSpan.FromSeconds(30), ReceiptResolutionTimeout = TimeSpan.FromSeconds(30),
        },
    };
}

/// <summary>Installs the complete Payments BASE persistence graph.</summary>
public static class PaymentsBaseModuleExtensions
{
    /// <summary>Adds the private Payments collections, guarded cells, and generated operation.</summary>
    public static HPDBaseBuilder AddPaymentsModuleMutations(this HPDBaseBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        return builder
            .AddCollection(PaymentsOwnerState.Collection)
            .AddCollection(PaymentsLedgerHead.Collection)
            .AddModuleGenerationCell(PaymentsOwnerGenerationMutation.OwnerCell)
            .AddModuleGenerationCell(PaymentsOwnerGenerationMutation.LedgerCell)
            .AddModuleMutation(PaymentsOwnerGenerationMutation.Definition, PaymentsOwnerGenerationMutation.Identity);
    }
}

/// <summary>Executes the installed Payments owner/ledger mutation through a principal-bound BASE session.</summary>
public sealed class PaymentsOwnerLedgerMutationClient
{
    /// <summary>Atomically updates both private records and compare-bound generations.</summary>
    public static ValueTask<BaseResult<BaseModuleMutationExecutionResult<AdvanceOwnerGenerationResult>>> ExecuteAsync(
        BaseSession session,
        AdvanceOwnerGenerationRequest request,
        BaseMutationRequestIdentity identity,
        BaseModuleMutationExecutionOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(identity);
        return session.ModuleMutations.Get(PaymentsOwnerGenerationMutation.Identity)
            .ExecuteAsync(request, identity, options, cancellationToken);
    }
}

/// <summary>Identifies one Payments aggregate mutation and its expected guarded generations.</summary>
public sealed record AdvanceOwnerGenerationRequest
{
    /// <summary>Gets the canonical Payments owner identity.</summary>
    [BaseField("hpd.payments.owner-ledger.owner-id")]
    public required string OwnerId { get; init; }

    /// <summary>Gets the module-owned semantic operation identity.</summary>
    [BaseField("hpd.payments.owner-ledger.operation-id")]
    public required string OperationId { get; init; }

    /// <summary>Gets the expected owner generation, or null for first creation.</summary>
    [BaseField("hpd.payments.owner-ledger.expected-owner-generation")]
    public BaseModuleGeneration? ExpectedOwnerGeneration { get; init; }

    /// <summary>Gets the expected ledger generation, or null for first creation.</summary>
    [BaseField("hpd.payments.owner-ledger.expected-ledger-generation")]
    public BaseModuleGeneration? ExpectedLedgerGeneration { get; init; }
}

/// <summary>Returns both newly committed opaque Payments generations.</summary>
public sealed record AdvanceOwnerGenerationResult
{
    /// <summary>Gets the newly committed owner generation.</summary>
    [BaseField("hpd.payments.owner-ledger.result.owner-generation")]
    public required BaseModuleGeneration OwnerGeneration { get; init; }

    /// <summary>Gets the newly committed ledger generation.</summary>
    [BaseField("hpd.payments.owner-ledger.result.ledger-generation")]
    public required BaseModuleGeneration LedgerGeneration { get; init; }
}

[JsonSerializable(typeof(PaymentsOwnerState))]
[JsonSerializable(typeof(PaymentsLedgerHead))]
[JsonSerializable(typeof(AdvanceOwnerGenerationRequest))]
[JsonSerializable(typeof(AdvanceOwnerGenerationResult))]
internal sealed partial class PaymentsBaseJsonContext : JsonSerializerContext;
