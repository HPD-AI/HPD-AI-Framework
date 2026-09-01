using System.Text.Json.Serialization;
using HPD.Base;
using HPD.Payments.Supporting.Custody;
using HPD.Payments.Supporting.Relations;
using HPD.Payments.Persistence.Ports;

namespace HPD.Payments.Runtime.Base;

/// <summary>Stores one immutable, generation-guarded Payments supporting relation.</summary>
[BaseCollection("hpd.payments.relations", typeof(PaymentsSupportingJsonContext), SystemOwnerModuleId = "hpd.payments", MutationMode = BaseCollectionMutationMode.AppendOnlyWithAdministrativePurge)]
public sealed partial record PaymentsRelationRecord
{
    /// <summary>Gets the canonical relation collision scope.</summary>
    [BaseField("hpd.payments.relation.scope", Operators = BaseFieldOperator.Equal)] public required string Scope { get; init; }
    /// <summary>Gets the exact AOT-safe relation payload.</summary>
    [BaseField("hpd.payments.relation.payload")] public required string Payload { get; init; }
}

/// <summary>Stores one immutable, owner-guarded continuation declaration.</summary>
[BaseCollection("hpd.payments.continuations", typeof(PaymentsSupportingJsonContext), SystemOwnerModuleId = "hpd.payments", MutationMode = BaseCollectionMutationMode.AppendOnlyWithAdministrativePurge)]
public sealed partial record PaymentsContinuationRecord
{
    /// <summary>Gets the canonical discovery scope.</summary>
    [BaseField("hpd.payments.continuation.scope", Operators = BaseFieldOperator.Equal)] public required string Scope { get; init; }
    /// <summary>Gets the exact AOT-safe continuation payload.</summary>
    [BaseField("hpd.payments.continuation.payload")] public required string Payload { get; init; }
}

/// <summary>Stores one immutable, owner- and instance-guarded custody observation.</summary>
[BaseCollection("hpd.payments.custody-events", typeof(PaymentsSupportingJsonContext), SystemOwnerModuleId = "hpd.payments", MutationMode = BaseCollectionMutationMode.AppendOnlyWithAdministrativePurge)]
public sealed partial record PaymentsCustodyRecord
{
    /// <summary>Gets the canonical represented owner key.</summary>
    [BaseField("hpd.payments.custody.owner-key", Operators = BaseFieldOperator.Equal)] public required string OwnerKey { get; init; }
    /// <summary>Gets the canonical custody-instance key.</summary>
    [BaseField("hpd.payments.custody.instance-key", Operators = BaseFieldOperator.Equal)] public required string InstanceKey { get; init; }
    /// <summary>Gets the fixed-width inventory generation.</summary>
    [BaseField("hpd.payments.custody.inventory-generation", Operators = BaseFieldOperator.Equal | BaseFieldOperator.Order)] public required string InventoryGeneration { get; init; }
    /// <summary>Gets the exact AOT-safe custody payload.</summary>
    [BaseField("hpd.payments.custody.payload")] public required string Payload { get; init; }
}

/// <summary>Supplies one relation and the exact generations of both endpoints.</summary>
public sealed record PersistRelationRequest
{
    /// <summary>Gets the immutable relation record identity.</summary>
    [BaseField("hpd.payments.relation.persist.record-id")] public required string RecordId { get; init; }
    /// <summary>Gets the canonical relation scope.</summary>
    [BaseField("hpd.payments.relation.persist.scope")] public required string Scope { get; init; }
    /// <summary>Gets the source owner generation-cell key.</summary>
    [BaseField("hpd.payments.relation.persist.source-owner-key")] public required string SourceOwnerKey { get; init; }
    /// <summary>Gets the expected source Base generation.</summary>
    [BaseField("hpd.payments.relation.persist.source-generation")] public required BaseModuleGeneration SourceGeneration { get; init; }
    /// <summary>Gets the target owner generation-cell key.</summary>
    [BaseField("hpd.payments.relation.persist.target-owner-key")] public required string TargetOwnerKey { get; init; }
    /// <summary>Gets the expected target Base generation.</summary>
    [BaseField("hpd.payments.relation.persist.target-generation")] public required BaseModuleGeneration TargetGeneration { get; init; }
    /// <summary>Gets the exact encoded relation.</summary>
    [BaseField("hpd.payments.relation.persist.payload")] public required string Payload { get; init; }
}

/// <summary>Supplies one continuation and its exact owner generation.</summary>
public sealed record PersistContinuationRequest
{
    /// <summary>Gets the immutable continuation record identity.</summary>
    [BaseField("hpd.payments.continuation.persist.record-id")] public required string RecordId { get; init; }
    /// <summary>Gets the canonical discovery scope.</summary>
    [BaseField("hpd.payments.continuation.persist.scope")] public required string Scope { get; init; }
    /// <summary>Gets the owner generation-cell key.</summary>
    [BaseField("hpd.payments.continuation.persist.owner-key")] public required string OwnerKey { get; init; }
    /// <summary>Gets the expected owner Base generation.</summary>
    [BaseField("hpd.payments.continuation.persist.owner-generation")] public required BaseModuleGeneration OwnerGeneration { get; init; }
    /// <summary>Gets the exact encoded continuation.</summary>
    [BaseField("hpd.payments.continuation.persist.payload")] public required string Payload { get; init; }
}

/// <summary>Supplies one custody observation and its exact owner and instance generations.</summary>
public sealed record PersistCustodyRequest
{
    /// <summary>Gets the immutable custody-event identity.</summary>
    [BaseField("hpd.payments.custody.persist.record-id")] public required string RecordId { get; init; }
    /// <summary>Gets the represented owner generation-cell key.</summary>
    [BaseField("hpd.payments.custody.persist.owner-key")] public required string OwnerKey { get; init; }
    /// <summary>Gets the expected represented owner Base generation.</summary>
    [BaseField("hpd.payments.custody.persist.owner-generation")] public required BaseModuleGeneration OwnerGeneration { get; init; }
    /// <summary>Gets the custody-instance generation-cell key.</summary>
    [BaseField("hpd.payments.custody.persist.instance-key")] public required string InstanceKey { get; init; }
    /// <summary>Gets the prior custody-instance generation, or null for its first observation.</summary>
    [BaseField("hpd.payments.custody.persist.expected-instance-generation")] public BaseModuleGeneration? ExpectedInstanceGeneration { get; init; }
    /// <summary>Gets the fixed-width Payments inventory generation.</summary>
    [BaseField("hpd.payments.custody.persist.inventory-generation")] public required string InventoryGeneration { get; init; }
    /// <summary>Gets the exact encoded custody observation.</summary>
    [BaseField("hpd.payments.custody.persist.payload")] public required string Payload { get; init; }
}

/// <summary>Returns the immutable record identity admitted by a supporting operation.</summary>
public sealed record PaymentsPersistenceResult
{
    /// <summary>Gets the admitted record identity.</summary>
    [BaseField("hpd.payments.persistence.result.record-id")] public required string RecordId { get; init; }
}

/// <summary>Provides the generated client identity for guarded relation persistence.</summary>
[BaseRegisteredModuleMutation("hpd.payments.relation.persist", typeof(PaymentsSupportingJsonContext), typeof(PersistRelationRequest), typeof(PaymentsPersistenceResult), Version = 1, OwningModuleId = "hpd.payments", GrantId = "hpd.payments.relation.persist")]
public static partial class PaymentsRelationMutation
{
    /// <summary>Gets the sealed guarded relation definition.</summary>
    public static BaseRegisteredModuleMutationDefinition Definition => PaymentsSupportingPersistenceMutations.Relation;
}

/// <summary>Provides the generated client identity for continuation persistence.</summary>
[BaseRegisteredModuleMutation("hpd.payments.continuation.persist", typeof(PaymentsSupportingJsonContext), typeof(PersistContinuationRequest), typeof(PaymentsPersistenceResult), Version = 1, OwningModuleId = "hpd.payments", GrantId = "hpd.payments.continuation.persist")]
public static partial class PaymentsContinuationMutation
{
    /// <summary>Gets the sealed continuation definition.</summary>
    public static BaseRegisteredModuleMutationDefinition Definition => PaymentsSupportingPersistenceMutations.Continuation;
}

/// <summary>Provides the generated client identity for custody persistence.</summary>
[BaseRegisteredModuleMutation("hpd.payments.custody.persist", typeof(PaymentsSupportingJsonContext), typeof(PersistCustodyRequest), typeof(PaymentsPersistenceResult), Version = 1, OwningModuleId = "hpd.payments", GrantId = "hpd.payments.custody.persist")]
public static partial class PaymentsCustodyMutation
{
    /// <summary>Gets the sealed custody definition.</summary>
    public static BaseRegisteredModuleMutationDefinition Definition => PaymentsSupportingPersistenceMutations.Custody;
}

/// <summary>Declares the permanent Base operations for relation, continuation, and custody state.</summary>
public static class PaymentsSupportingPersistenceMutations
{
    private const string OwnerCellId = "hpd.payments.owner-fact-generation";
    private const string CustodyCellId = "hpd.payments.custody-generation";

    /// <summary>Gets the per-instance custody generation cell.</summary>
    public static BaseModuleGenerationCellDefinition CustodyCell { get; } = new()
    {
        Id = CustodyCellId, Version = 1, OwningModuleId = "hpd.payments", Scope = BaseModuleGenerationScope.TenantAndKey,
        MaximumKeyUtf8Bytes = 128, MaximumCellsPerOperation = 1,
    };

    /// <summary>Gets the guarded relation operation.</summary>
    public static BaseRegisteredModuleMutationDefinition Relation { get; } = SealRelation();
    /// <summary>Gets the owner-guarded continuation operation.</summary>
    public static BaseRegisteredModuleMutationDefinition Continuation { get; } = SealContinuation();
    /// <summary>Gets the owner- and instance-guarded custody operation.</summary>
    public static BaseRegisteredModuleMutationDefinition Custody { get; } = SealCustody();

    private static BaseRegisteredModuleMutationDefinition SealRelation() => BaseModuleMutationContract.Seal(new()
    {
        Id = "hpd.payments.relation.persist", Version = 1, OwningModuleId = "hpd.payments", GrantId = "hpd.payments.relation.persist",
        Audience = BaseModuleMutationAudience.Service, RequestTypeId = "hpd.payments.relation.persist.request", ResultTypeId = "hpd.payments.persistence.result",
        SystemCollectionIds = [PaymentsRelationRecord.Collection.Id], SystemSourceGrants = [new() { CollectionId = PaymentsRelationRecord.Collection.Id, GrantId = "hpd.payments.relation.source" }],
        GenerationCellIds = [OwnerCellId], ImportedSubjectContractIds = [], Template = RelationTemplate(), Limits = Limits(3, 2), ReceiptPolicy = Receipts(),
        Checksum = BaseModuleMutationChecksum.Create(new byte[BaseModuleMutationChecksum.Length]),
    });

    private static BaseRegisteredModuleMutationDefinition SealContinuation() => BaseModuleMutationContract.Seal(new()
    {
        Id = "hpd.payments.continuation.persist", Version = 1, OwningModuleId = "hpd.payments", GrantId = "hpd.payments.continuation.persist",
        Audience = BaseModuleMutationAudience.Service, RequestTypeId = "hpd.payments.continuation.persist.request", ResultTypeId = "hpd.payments.persistence.result",
        SystemCollectionIds = [PaymentsContinuationRecord.Collection.Id], SystemSourceGrants = [new() { CollectionId = PaymentsContinuationRecord.Collection.Id, GrantId = "hpd.payments.continuation.source" }],
        GenerationCellIds = [OwnerCellId], ImportedSubjectContractIds = [], Template = ContinuationTemplate(), Limits = Limits(2, 1), ReceiptPolicy = Receipts(),
        Checksum = BaseModuleMutationChecksum.Create(new byte[BaseModuleMutationChecksum.Length]),
    });

    private static BaseRegisteredModuleMutationDefinition SealCustody() => BaseModuleMutationContract.Seal(new()
    {
        Id = "hpd.payments.custody.persist", Version = 1, OwningModuleId = "hpd.payments", GrantId = "hpd.payments.custody.persist",
        Audience = BaseModuleMutationAudience.Service, RequestTypeId = "hpd.payments.custody.persist.request", ResultTypeId = "hpd.payments.persistence.result",
        SystemCollectionIds = [PaymentsCustodyRecord.Collection.Id], SystemSourceGrants = [new() { CollectionId = PaymentsCustodyRecord.Collection.Id, GrantId = "hpd.payments.custody.source" }],
        GenerationCellIds = [CustodyCellId, OwnerCellId], ImportedSubjectContractIds = [], Template = CustodyTemplate(), Limits = Limits(3, 2, 1), ReceiptPolicy = Receipts(),
        Checksum = BaseModuleMutationChecksum.Create(new byte[BaseModuleMutationChecksum.Length]),
    });

    private static BaseModuleMutationTemplate RelationTemplate()
    {
        return new()
        {
            Captures =
            [
                new BaseModuleRecordCapture { Id = "relation", CollectionId = PaymentsRelationRecord.Collection.Id, RecordId = Request("relation.capture-record-id", "hpd.payments.relation.persist.record-id", "string"), Presence = BaseModuleCapturePresence.RequireMissing },
                Generation("source", "hpd.payments.relation.persist.source-owner-key"), Generation("target", "hpd.payments.relation.persist.target-owner-key"),
            ],
            Guards =
            [
                Equal("source-equal", "source", "hpd.payments.relation.persist.source-generation"),
                Equal("target-equal", "target", "hpd.payments.relation.persist.target-generation"),
            ],
            Body = new() { Statements = [Require("source", "source-equal"), Require("target", "target-equal"), CreateRelation(Request("relation.write-record-id", "hpd.payments.relation.persist.record-id", "string"))] },
            Result = Result(Request("relation.result-record-id", "hpd.payments.relation.persist.record-id", "string")),
        };
    }

    private static BaseModuleMutationTemplate ContinuationTemplate()
    {
        return new()
        {
            Captures = [new BaseModuleRecordCapture { Id = "continuation", CollectionId = PaymentsContinuationRecord.Collection.Id, RecordId = Request("continuation.capture-record-id", "hpd.payments.continuation.persist.record-id", "string"), Presence = BaseModuleCapturePresence.RequireMissing }, Generation("owner", "hpd.payments.continuation.persist.owner-key")],
            Guards = [Equal("owner-equal", "owner", "hpd.payments.continuation.persist.owner-generation")],
            Body = new() { Statements = [Require("owner", "owner-equal"), CreateContinuation(Request("continuation.write-record-id", "hpd.payments.continuation.persist.record-id", "string"))] }, Result = Result(Request("continuation.result-record-id", "hpd.payments.continuation.persist.record-id", "string")),
        };
    }

    private static BaseModuleMutationTemplate CustodyTemplate()
    {
        return new()
        {
            Captures =
            [
                new BaseModuleRecordCapture { Id = "custody", CollectionId = PaymentsCustodyRecord.Collection.Id, RecordId = Request("custody.capture-record-id", "hpd.payments.custody.persist.record-id", "string"), Presence = BaseModuleCapturePresence.RequireMissing },
                new BaseModuleGenerationCapture { Id = "instance", CellId = CustodyCellId, Key = Request("custody.instance-key", "hpd.payments.custody.persist.instance-key", "string"), Absence = BaseModuleGenerationAbsenceBehavior.AllowEither },
                Generation("owner", "hpd.payments.custody.persist.owner-key"),
            ],
            Guards =
            [
                new BaseModuleLogicalGuard { Id = "instance-admitted", Kind = BaseModuleLogicalGuardKind.Or, ChildGuardIds = ["instance-equal", "instance-missing"] },
                Equal("instance-equal", "instance", "hpd.payments.custody.persist.expected-instance-generation", "base.moduleGeneration?"),
                new BaseModuleGenerationGuard { Id = "instance-missing", CaptureId = "instance", Comparison = BaseModuleGenerationComparisonKind.MustBeMissing },
                Equal("owner-equal", "owner", "hpd.payments.custody.persist.owner-generation"),
            ],
            Body = new() { Statements = [Require("owner", "owner-equal"), Require("instance", "instance-admitted"), CreateCustody(Request("custody.write-record-id", "hpd.payments.custody.persist.record-id", "string")), new BaseModuleIncrementGenerationStatement { Id = "advance-instance", CaptureId = "instance", CreateIfAbsent = true }] },
            Result = Result(Request("custody.result-record-id", "hpd.payments.custody.persist.record-id", "string")),
        };
    }

    private static BaseModuleGenerationCapture Generation(string id, string keyPath) => new() { Id = id, CellId = OwnerCellId, Key = Request(id + ".key", keyPath, "string"), Absence = BaseModuleGenerationAbsenceBehavior.AllowEither };
    private static BaseModuleGenerationGuard Equal(string id, string capture, string path, string type = "base.moduleGeneration") => new() { Id = id, CaptureId = capture, Comparison = BaseModuleGenerationComparisonKind.MustEqual, Expected = Request(id + ".expected", path, type) };
    private static BaseModuleRequireStatement Require(string id, string guard) => new() { Id = "require-" + id, GuardId = guard, RequirementId = "hpd.payments." + id + ".generation-conflict" };
    private static BaseModuleRequestPropertyExpression Request(string id, string path, string type) => new() { Id = id, ResultTypeId = type, Property = new() { StablePropertyPath = [path], DeclaredTypeId = type } };
    private static BaseModuleObjectPropertyExpression Property(string id, BaseModuleValueExpression value) => new() { StablePropertyId = id, Value = value };
    private static BaseModuleCreateStatement CreateRelation(BaseModuleValueExpression id) => new() { Id = "create-relation", CollectionId = PaymentsRelationRecord.Collection.Id, RecordId = id, Payload = new BaseModuleObjectExpression { Id = "relation-payload", ResultTypeId = "hpd.payments.relation", Properties = [Property("hpd.payments.relation.payload", Request("relation.payload", "hpd.payments.relation.persist.payload", "string")), Property("hpd.payments.relation.scope", Request("relation.scope", "hpd.payments.relation.persist.scope", "string"))] } };
    private static BaseModuleCreateStatement CreateContinuation(BaseModuleValueExpression id) => new() { Id = "create-continuation", CollectionId = PaymentsContinuationRecord.Collection.Id, RecordId = id, Payload = new BaseModuleObjectExpression { Id = "continuation-payload", ResultTypeId = "hpd.payments.continuation", Properties = [Property("hpd.payments.continuation.payload", Request("continuation.payload", "hpd.payments.continuation.persist.payload", "string")), Property("hpd.payments.continuation.scope", Request("continuation.scope", "hpd.payments.continuation.persist.scope", "string"))] } };
    private static BaseModuleCreateStatement CreateCustody(BaseModuleValueExpression id) => new() { Id = "create-custody", CollectionId = PaymentsCustodyRecord.Collection.Id, RecordId = id, Payload = new BaseModuleObjectExpression { Id = "custody-payload", ResultTypeId = "hpd.payments.custody", Properties = [Property("hpd.payments.custody.instance-key", Request("custody.instance", "hpd.payments.custody.persist.instance-key", "string")), Property("hpd.payments.custody.inventory-generation", Request("custody.generation", "hpd.payments.custody.persist.inventory-generation", "string")), Property("hpd.payments.custody.owner-key", Request("custody.owner", "hpd.payments.custody.persist.owner-key", "string")), Property("hpd.payments.custody.payload", Request("custody.payload", "hpd.payments.custody.persist.payload", "string"))] } };
    private static BaseModuleResultProjection Result(BaseModuleValueExpression recordId) => new() { Value = new BaseModuleObjectExpression { Id = "result", ResultTypeId = "hpd.payments.persistence.result", Properties = [Property("hpd.payments.persistence.result.record-id", recordId)] } };
    private static BaseModuleMutationReceiptPolicy Receipts() => new() { FormatVersion = 1, Lifetime = TimeSpan.FromDays(30) };
    private static BaseModuleMutationLimits Limits(int captures, int generations, int increments = 0) => new()
    {
        MaximumCaptures = captures, MaximumRecordCaptures = 1, MaximumRelationTargetCaptures = 1, MaximumGenerationCaptures = generations,
        MaximumRecordMutations = 1, MaximumGenerationReads = generations, MaximumGenerationComparisons = generations + 1, MaximumGenerationIncrements = Math.Max(1, increments),
        MaximumGuardNodes = generations + 2, MaximumGuardDepth = 2, MaximumStatements = generations + 2, MaximumBranches = 1, MaximumExpressionNodes = 48,
        MaximumReadIntervals = 4, MaximumSubjectValidations = 1, MaximumAuthorityReads = 8, MaximumRelationChecks = 1, MaximumUniqueConstraintChecks = 1,
        MaximumRequestBytes = 65_536, MaximumSelectedBytes = 65_536, MaximumGenerationBytes = 4096, MaximumEvidenceBytes = 65_536,
        MaximumWrittenBytes = 65_536, MaximumFactBytes = 65_536, MaximumJournalBytes = 65_536, MaximumReceiptBytes = 65_536,
        MaximumResultBytes = 4096, MaximumTransientBytes = 262_144,
        Deadlines = new() { AcquisitionTimeout = TimeSpan.FromSeconds(5), TransactionTimeout = TimeSpan.FromSeconds(30), CommitObservationTimeout = TimeSpan.FromSeconds(30), ReceiptResolutionTimeout = TimeSpan.FromSeconds(30) },
    };
}

/// <summary>Registers the complete Base-backed Payments supporting persistence graph.</summary>
public static class PaymentsSupportingPersistenceExtensions
{
    /// <summary>Adds the supporting collections, generation cell, and registered operations.</summary>
    public static HPDBaseBuilder AddPaymentsSupportingPersistence(this HPDBaseBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        return builder.AddCollection(PaymentsRelationRecord.Collection).AddCollection(PaymentsContinuationRecord.Collection)
            .AddCollection(PaymentsCustodyRecord.Collection).AddModuleGenerationCell(PaymentsSupportingPersistenceMutations.CustodyCell)
            .AddModuleMutation(PaymentsSupportingPersistenceMutations.Relation, PaymentsRelationMutation.Identity)
            .AddModuleMutation(PaymentsSupportingPersistenceMutations.Continuation, PaymentsContinuationMutation.Identity)
            .AddModuleMutation(PaymentsSupportingPersistenceMutations.Custody, PaymentsCustodyMutation.Identity);
    }
}

[JsonSerializable(typeof(PaymentsRelationRecord))]
[JsonSerializable(typeof(PaymentsContinuationRecord))]
[JsonSerializable(typeof(PaymentsCustodyRecord))]
[JsonSerializable(typeof(PersistRelationRequest))]
[JsonSerializable(typeof(PersistContinuationRequest))]
[JsonSerializable(typeof(PersistCustodyRequest))]
[JsonSerializable(typeof(PaymentsPersistenceResult))]
internal sealed partial class PaymentsSupportingJsonContext : JsonSerializerContext;
