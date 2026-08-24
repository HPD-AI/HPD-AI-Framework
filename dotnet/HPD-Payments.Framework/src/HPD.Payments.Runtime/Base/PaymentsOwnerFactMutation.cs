using System.Collections.Immutable;
using System.Text.Json.Serialization;
using HPD.Base;

namespace HPD.Payments.Runtime.Base;

/// <summary>Private immutable Payments authority event stored by HPD.Base.</summary>
[BaseCollection("hpd.payments.owner-facts", typeof(PaymentsOwnerFactJsonContext), SystemOwnerModuleId = "hpd.payments", MutationMode = BaseCollectionMutationMode.AppendOnlyWithAdministrativePurge)]
public sealed partial record PaymentsOwnerFactEvent
{
    /// <summary>Gets the canonical authority and subject owner key.</summary>
    [BaseField("hpd.payments.owner-fact.owner-key", Operators = BaseFieldOperator.Equal)]
    public required string OwnerKey { get; init; }
    /// <summary>Gets the resulting Payments owner generation.</summary>
    [BaseField("hpd.payments.owner-fact.generation", Operators = BaseFieldOperator.Equal | BaseFieldOperator.Order)]
    public required string Generation { get; init; }
    /// <summary>Gets the canonical semantic digest.</summary>
    [BaseField("hpd.payments.owner-fact.digest")]
    public required string SemanticDigest { get; init; }
    /// <summary>Gets the closed codec type identifier.</summary>
    [BaseField("hpd.payments.owner-fact.type")]
    public required string FactType { get; init; }
    /// <summary>Gets the canonical Base64 encoded fact payload.</summary>
    [BaseField("hpd.payments.owner-fact.payload")]
    public required string Payload { get; init; }
}

/// <summary>Private current owner head used with the provider-owned generation cell.</summary>
[BaseCollection("hpd.payments.owner-fact-heads", typeof(PaymentsOwnerFactJsonContext), SystemOwnerModuleId = "hpd.payments")]
public sealed partial record PaymentsOwnerFactHead
{
    /// <summary>Gets the canonical authority and subject owner key.</summary>
    [BaseField("hpd.payments.owner-fact-head.owner-key")]
    public required string OwnerKey { get; init; }
    /// <summary>Gets the latest committed Payments owner generation.</summary>
    [BaseField("hpd.payments.owner-fact-head.generation")]
    public required string Generation { get; init; }
    /// <summary>Gets the latest semantic digest.</summary>
    [BaseField("hpd.payments.owner-fact-head.digest")]
    public required string SemanticDigest { get; init; }
}

/// <summary>Atomically creates one immutable fact event, advances its owner head, and increments the Base generation cell.</summary>
[BaseRegisteredModuleMutation(
    "hpd.payments.owner-fact.append", typeof(PaymentsOwnerFactJsonContext),
    typeof(AppendOwnerFactRequest), typeof(AppendOwnerFactResult), Version = 1,
    OwningModuleId = "hpd.payments", GrantId = "hpd.payments.owner-fact.append")]
public static partial class PaymentsOwnerFactMutation
{
    private const string CellId = "hpd.payments.owner-fact-generation";

    /// <summary>Gets the provider-owned owner-fact generation cell.</summary>
    public static BaseModuleGenerationCellDefinition Cell { get; } = new()
    {
        Id = CellId, Version = 1, OwningModuleId = "hpd.payments",
        Scope = BaseModuleGenerationScope.TenantAndKey, MaximumKeyUtf8Bytes = 128,
        MaximumCellsPerOperation = 2,
    };

    /// <summary>Gets the immutable append operation definition.</summary>
    public static BaseRegisteredModuleMutationDefinition Definition { get; } = BaseModuleMutationContract.Seal(new()
    {
        Id = "hpd.payments.owner-fact.append", Version = 1, OwningModuleId = "hpd.payments",
        GrantId = "hpd.payments.owner-fact.append", Audience = BaseModuleMutationAudience.Service,
        RequestTypeId = "hpd.payments.owner-fact.append.request",
        ResultTypeId = "hpd.payments.owner-fact.append.result",
        SystemCollectionIds = [PaymentsOwnerFactHead.Collection.Id, PaymentsOwnerFactEvent.Collection.Id],
        SystemSourceGrants =
        [
            new() { CollectionId = PaymentsOwnerFactHead.Collection.Id, GrantId = "hpd.payments.owner-fact-head.source" },
            new() { CollectionId = PaymentsOwnerFactEvent.Collection.Id, GrantId = "hpd.payments.owner-fact.source" },
        ],
        GenerationCellIds = [CellId], ImportedSubjectContractIds = [],
        Template = Template(), Limits = Limits(),
        ReceiptPolicy = new BaseModuleMutationReceiptPolicy { FormatVersion = 1, Lifetime = TimeSpan.FromDays(30) },
        Checksum = BaseModuleMutationChecksum.Create(new byte[BaseModuleMutationChecksum.Length]),
    });

    private static BaseModuleMutationTemplate Template()
    {
        BaseModuleValueExpression Request(string id, string path, string type) => new BaseModuleRequestPropertyExpression
        {
            Id = id, ResultTypeId = type,
            Property = new BaseModuleRequestPropertyReference { StablePropertyPath = [path], DeclaredTypeId = type },
        };
        BaseModuleObjectExpression Event(string id) => new()
        {
            Id = id, ResultTypeId = "hpd.payments.owner-fact",
            Properties =
            [
                Property("hpd.payments.owner-fact.digest", Request(id + ".digest", "hpd.payments.owner-fact.append.digest", "string")),
                Property("hpd.payments.owner-fact.generation", Request(id + ".generation", "hpd.payments.owner-fact.append.result-generation", "string")),
                Property("hpd.payments.owner-fact.owner-key", Request(id + ".owner", "hpd.payments.owner-fact.append.owner-key", "string")),
                Property("hpd.payments.owner-fact.payload", Request(id + ".payload", "hpd.payments.owner-fact.append.payload", "string")),
                Property("hpd.payments.owner-fact.type", Request(id + ".type", "hpd.payments.owner-fact.append.fact-type", "string")),
            ],
        };
        BaseModuleObjectExpression Head(string id) => new()
        {
            Id = id, ResultTypeId = "hpd.payments.owner-fact-head",
            Properties =
            [
                Property("hpd.payments.owner-fact-head.digest", Request(id + ".digest", "hpd.payments.owner-fact.append.digest", "string")),
                Property("hpd.payments.owner-fact-head.generation", Request(id + ".generation", "hpd.payments.owner-fact.append.result-generation", "string")),
                Property("hpd.payments.owner-fact-head.owner-key", Request(id + ".owner", "hpd.payments.owner-fact.append.owner-key", "string")),
            ],
        };
        return new BaseModuleMutationTemplate
        {
            Captures =
            [
                new BaseModuleRecordCapture { Id = "event", CollectionId = PaymentsOwnerFactEvent.Collection.Id, RecordId = Request("capture-event-id", "hpd.payments.owner-fact.append.event-id", "string"), Presence = BaseModuleCapturePresence.RequireMissing },
                new BaseModuleGenerationCapture { Id = "generation", CellId = CellId, Key = Request("capture-generation-key", "hpd.payments.owner-fact.append.owner-key", "string"), Absence = BaseModuleGenerationAbsenceBehavior.AllowEither },
                new BaseModuleRecordCapture { Id = "head", CollectionId = PaymentsOwnerFactHead.Collection.Id, RecordId = Request("capture-head-id", "hpd.payments.owner-fact.append.owner-key", "string"), Presence = BaseModuleCapturePresence.AllowEither },
            ],
            Guards =
            [
                new BaseModuleLogicalGuard { Id = "generation-admitted", Kind = BaseModuleLogicalGuardKind.Or, ChildGuardIds = ["generation-equal", "generation-missing"] },
                new BaseModuleGenerationGuard { Id = "generation-equal", CaptureId = "generation", Comparison = BaseModuleGenerationComparisonKind.MustEqual, Expected = Request("guard-expected-generation", "hpd.payments.owner-fact.append.expected-generation", "base.moduleGeneration?") },
                new BaseModuleGenerationGuard { Id = "generation-missing", CaptureId = "generation", Comparison = BaseModuleGenerationComparisonKind.MustBeMissing },
            ],
            Body = new BaseModuleMutationBlock
            {
                Statements =
                [
                    new BaseModuleRequireStatement { Id = "require-generation", GuardId = "generation-admitted", RequirementId = "hpd.payments.owner-fact.generation-conflict" },
                    new BaseModuleCreateStatement { Id = "create-event", CollectionId = PaymentsOwnerFactEvent.Collection.Id, RecordId = Request("write-event-id", "hpd.payments.owner-fact.append.event-id", "string"), Payload = Event("event-payload") },
                    new BaseModuleUpsertStatement { Id = "write-head", CollectionId = PaymentsOwnerFactHead.Collection.Id, RecordId = Request("write-head-id", "hpd.payments.owner-fact.append.owner-key", "string"), Create = Head("head-create"), Update = Head("head-update"), UpdateMode = RecordUpsertUpdateMode.Replace },
                    new BaseModuleIncrementGenerationStatement { Id = "advance-generation", CaptureId = "generation", CreateIfAbsent = true },
                ],
            },
            Result = new BaseModuleResultProjection
            {
                Value = new BaseModuleObjectExpression
                {
                    Id = "result", ResultTypeId = "hpd.payments.owner-fact.append.result",
                    Properties =
                    [
                        Property("hpd.payments.owner-fact.result.generation", new BaseModuleResultingGenerationExpression { Id = "result-generation", ResultTypeId = "base.moduleGeneration", CaptureId = "generation" }),
                    ],
                },
            },
        };
    }

    private static BaseModuleObjectPropertyExpression Property(string id, BaseModuleValueExpression value) => new() { StablePropertyId = id, Value = value };

    private static BaseModuleMutationLimits Limits() => new()
    {
        MaximumCaptures = 3, MaximumRecordCaptures = 2, MaximumRelationTargetCaptures = 1, MaximumGenerationCaptures = 1,
        MaximumRecordMutations = 2, MaximumGenerationReads = 1, MaximumGenerationComparisons = 2, MaximumGenerationIncrements = 1,
        MaximumGuardNodes = 3, MaximumGuardDepth = 2, MaximumStatements = 4, MaximumBranches = 1, MaximumExpressionNodes = 48,
        MaximumReadIntervals = 4, MaximumSubjectValidations = 1, MaximumAuthorityReads = 8, MaximumRelationChecks = 1,
        MaximumUniqueConstraintChecks = 1, MaximumRequestBytes = 65_536, MaximumSelectedBytes = 65_536,
        MaximumGenerationBytes = 4096, MaximumEvidenceBytes = 65_536, MaximumWrittenBytes = 65_536,
        MaximumFactBytes = 65_536, MaximumJournalBytes = 65_536, MaximumReceiptBytes = 65_536,
        MaximumResultBytes = 4096, MaximumTransientBytes = 262_144,
        Deadlines = new BaseAtomicMutationDeadlines
        {
            AcquisitionTimeout = TimeSpan.FromSeconds(5), TransactionTimeout = TimeSpan.FromSeconds(30),
            CommitObservationTimeout = TimeSpan.FromSeconds(30), ReceiptResolutionTimeout = TimeSpan.FromSeconds(30),
        },
    };
}

/// <summary>Installs the owner-fact append collections, cell, and operation.</summary>
public static class PaymentsOwnerFactModuleExtensions
{
    /// <summary>Adds the complete owner-fact persistence graph.</summary>
    public static HPDBaseBuilder AddPaymentsOwnerFactPersistence(this HPDBaseBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        return builder.AddCollection(PaymentsOwnerFactEvent.Collection)
            .AddCollection(PaymentsOwnerFactHead.Collection)
            .AddModuleGenerationCell(PaymentsOwnerFactMutation.Cell)
            .AddModuleMutation(PaymentsOwnerFactMutation.Definition, PaymentsOwnerFactMutation.Identity);
    }
}

/// <summary>Supplies one exact immutable fact append.</summary>
public sealed record AppendOwnerFactRequest
{
    /// <summary>Gets the canonical owner key.</summary>
    [BaseField("hpd.payments.owner-fact.append.owner-key")] public required string OwnerKey { get; init; }
    /// <summary>Gets the immutable event record identity.</summary>
    [BaseField("hpd.payments.owner-fact.append.event-id")] public required string EventId { get; init; }
    /// <summary>Gets the semantic digest.</summary>
    [BaseField("hpd.payments.owner-fact.append.digest")] public required string SemanticDigest { get; init; }
    /// <summary>Gets the closed codec identifier.</summary>
    [BaseField("hpd.payments.owner-fact.append.fact-type")] public required string FactType { get; init; }
    /// <summary>Gets the canonical Base64 payload.</summary>
    [BaseField("hpd.payments.owner-fact.append.payload")] public required string Payload { get; init; }
    /// <summary>Gets the prior Base generation, or null for first creation.</summary>
    [BaseField("hpd.payments.owner-fact.append.expected-generation")] public BaseModuleGeneration? ExpectedGeneration { get; init; }
    /// <summary>Gets the resulting Payments generation stored in event and head records.</summary>
    [BaseField("hpd.payments.owner-fact.append.result-generation")] public required string ResultGeneration { get; init; }
}

/// <summary>Returns the provider-owned resulting generation.</summary>
public sealed record AppendOwnerFactResult
{
    /// <summary>Gets the resulting Base generation.</summary>
    [BaseField("hpd.payments.owner-fact.result.generation")] public required BaseModuleGeneration Generation { get; init; }
}

[JsonSerializable(typeof(PaymentsOwnerFactEvent))]
[JsonSerializable(typeof(PaymentsOwnerFactHead))]
[JsonSerializable(typeof(AppendOwnerFactRequest))]
[JsonSerializable(typeof(AppendOwnerFactResult))]
internal sealed partial class PaymentsOwnerFactJsonContext : JsonSerializerContext;
