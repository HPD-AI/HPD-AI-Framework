using System.Collections.Immutable;
using System.Text.Json;

namespace HPD.Base;

/// <summary>Contains the complete closed input for a registered selection-mutation activation target.</summary>
public sealed record BaseSelectionActivationRequest
{
    /// <summary>Gets the bounded typed selection query.</summary>
    public required RecordQuery Query { get; init; }
    /// <summary>Gets the merge patch only for merge-patch profiles.</summary>
    public RecordPatchRequest? Patch { get; init; }
    /// <summary>Gets exact prior-state requirements for every selected record.</summary>
    public required BasePreviousStateRequirement PreviousState { get; init; }
}

/// <summary>Identifies the closed result stored in one atomic mutation receipt.</summary>
public enum BaseAtomicReceiptResultKind
{
    /// <summary>An ordinary record-mutation result.</summary>
    RecordMutations,
    /// <summary>A transaction-bound selection mutation result.</summary>
    SelectionMutation,
    /// <summary>A registered module-mutation result.</summary>
    ModuleMutation,
    /// <summary>An identified durable subject-lifecycle checkpoint advancement.</summary>
    SubjectLifecycleCheckpoint,
    /// <summary>An identified subject-lifecycle maintenance publication.</summary>
    SubjectLifecycleMaintenance,
    /// <summary>An identified coordinated subject-retirement operation.</summary>
    SubjectRetirement,
    /// <summary>One atomic durable-activation creation result.</summary>
    ActivationCreation,
    /// <summary>One handler-free activation target and terminal transition committed together.</summary>
    ActivationTransactionalOperation,
}

/// <summary>Stores the closed durable result of atomic activation creation.</summary>
public sealed record BaseActivationCreationReceiptResult
{
    /// <summary>Gets created activation identities in request order.</summary>
    public required ImmutableArray<string> ActivationIds { get; init; }
}

/// <summary>Stores one handler-free target result under the outer activation receipt authority.</summary>
public sealed record BaseActivationTransactionalReceiptResult
{
    /// <summary>Gets the terminal activation identity.</summary>
    public required string ActivationId { get; init; }
    /// <summary>Gets the resulting activation generation.</summary>
    public required long ActivationGeneration { get; init; }
    /// <summary>Gets the installed target kind.</summary>
    public required string TargetKind { get; init; }
    /// <summary>Gets the installed target identity.</summary>
    public required string TargetId { get; init; }
    /// <summary>Gets the installed target version.</summary>
    public required int TargetVersion { get; init; }
    /// <summary>Gets the installed target checksum.</summary>
    public required string TargetChecksum { get; init; }
    /// <summary>Gets target-owned generation evidence without a separately resolvable receipt.</summary>
    public required ImmutableArray<BaseModuleCommittedGeneration> Generations { get; init; }
    /// <summary>Gets the exact graph-owned canonical target result bytes.</summary>
    public required ImmutableArray<byte> CanonicalResultBytes { get; init; }
    /// <summary>Gets the terminal control checksum.</summary>
    public required ImmutableArray<byte> ActivationControlChecksum { get; init; }
}

/// <summary>Stores one committed module generation without disclosing its scoped provider key.</summary>
public sealed record BaseModuleCommittedGeneration
{
    /// <summary>Gets the stable capture identity.</summary>
    public required string CaptureId { get; init; }
    /// <summary>Gets the installed cell identity.</summary>
    public required string CellId { get; init; }
    /// <summary>Gets the installed cell version.</summary>
    public required int CellVersion { get; init; }
    /// <summary>Gets the previous generation, or null when this commit created the cell.</summary>
    public BaseModuleGeneration? Previous { get; init; }
    /// <summary>Gets the exact committed generation.</summary>
    public required BaseModuleGeneration Resulting { get; init; }
}

/// <summary>Stores the closed durable result of one registered module mutation.</summary>
public sealed record BaseModuleMutationReceiptResult
{
    /// <summary>Gets the installed operation identity.</summary>
    public required string OperationId { get; init; }
    /// <summary>Gets the installed operation version.</summary>
    public required int OperationVersion { get; init; }
    /// <summary>Gets whether the request newly committed or resolved an earlier commit.</summary>
    public required BaseMutationRequestDisposition Disposition { get; init; }
    /// <summary>Gets the closed module outcome.</summary>
    public required BaseModuleMutationOutcome Outcome { get; init; }
    /// <summary>Gets committed generation evidence in canonical cell-key order.</summary>
    public required ImmutableArray<BaseModuleCommittedGeneration> Generations { get; init; }
    /// <summary>Gets the exact graph-owned canonical result bytes.</summary>
    public required ImmutableArray<byte> CanonicalResultBytes { get; init; }
    /// <summary>Gets child activations created atomically with this module operation.</summary>
    public ImmutableArray<string> CreatedActivationIds { get; init; } = [];
    /// <summary>Gets semantic activation evidence when the module operation carried L53 authority.</summary>
    public BaseSemanticActivationReceiptEvidence? SemanticActivation { get; init; }
}

/// <summary>Provides the source-generated persistence shape for one committed module generation.</summary>
public sealed record BaseModuleCommittedGenerationWire
{
    /// <summary>Gets the stable capture identity.</summary>
    public required string CaptureId { get; init; }
    /// <summary>Gets the installed cell identity.</summary>
    public required string CellId { get; init; }
    /// <summary>Gets the installed cell version.</summary>
    public required int CellVersion { get; init; }
    /// <summary>Gets the previous canonical positive decimal, when present.</summary>
    public string? Previous { get; init; }
    /// <summary>Gets the resulting canonical positive decimal.</summary>
    public required string Resulting { get; init; }
}

/// <summary>Provides the source-generated persistence shape for one module-mutation result.</summary>
public sealed record BaseModuleMutationReceiptResultWire
{
    /// <summary>Gets the installed operation identity.</summary>
    public required string OperationId { get; init; }
    /// <summary>Gets the installed operation version.</summary>
    public required int OperationVersion { get; init; }
    /// <summary>Gets the request disposition.</summary>
    public required BaseMutationRequestDisposition Disposition { get; init; }
    /// <summary>Gets the module outcome.</summary>
    public required BaseModuleMutationOutcome Outcome { get; init; }
    /// <summary>Gets committed generation wire evidence.</summary>
    public required BaseModuleCommittedGenerationWire[] Generations { get; init; }
    /// <summary>Gets the exact canonical result bytes.</summary>
    public required byte[] CanonicalResultBytes { get; init; }
    /// <summary>Gets child activation identities created by the same transaction.</summary>
    public string[] CreatedActivationIds { get; init; } = [];
    /// <summary>Gets semantic activation wire evidence when present.</summary>
    public BaseSemanticActivationReceiptEvidenceWire? SemanticActivation { get; init; }
}

/// <summary>Stores the bounded durable result of one selection mutation.</summary>
public sealed record BaseSelectionMutationReceiptResult
{
    /// <summary>Gets the application identifier.</summary>
    public required string ApplicationId { get; init; }
    /// <summary>Gets the collection identifier.</summary>
    public required string CollectionId { get; init; }
    /// <summary>Gets the profile identifier.</summary>
    public required string OperationProfileId { get; init; }
    /// <summary>Gets the profile version.</summary>
    public required int OperationProfileVersion { get; init; }
    /// <summary>Gets the non-enumerating receipt scope.</summary>
    public required string ReceiptScope { get; init; }
    /// <summary>Gets the selected count.</summary>
    public required int SelectedCount { get; init; }
    /// <summary>Gets the mutated count.</summary>
    public required int MutatedCount { get; init; }
    /// <summary>Gets the canonical batch outcome.</summary>
    public required BaseRecordBatchOutcome Outcome { get; init; }
}

/// <summary>Owns one canonical mutation fact through private copied bytes.</summary>
public sealed class BaseOwnedMutationFact
{
    private readonly byte[] _bytes;
    private BaseOwnedMutationFact(int version, byte[] bytes) { CodecVersion = version; _bytes = bytes; }
    /// <summary>Gets the canonical codec version.</summary>
    public int CodecVersion { get; }
    /// <summary>Gets the canonical byte length.</summary>
    public int EncodedLength => _bytes.Length;
    /// <summary>Validates and recursively freezes one mutation fact.</summary>
    public static BaseOwnedMutationFact Freeze(BaseRecordMutationFact fact, int codecVersion)
    {
        ArgumentNullException.ThrowIfNull(fact);
        ArgumentOutOfRangeException.ThrowIfLessThan(codecVersion, 1);
        byte[] bytes = JsonSerializer.SerializeToUtf8Bytes(fact, HPDBaseJsonSerializerContext.Default.BaseRecordMutationFact);
        _ = JsonSerializer.Deserialize(bytes, HPDBaseJsonSerializerContext.Default.BaseRecordMutationFact)
            ?? throw new ArgumentException("The mutation fact is invalid.", nameof(fact));
        return new BaseOwnedMutationFact(codecVersion, bytes.ToArray());
    }
    /// <summary>Materializes a fresh recursively owned mutation fact.</summary>
    public BaseRecordMutationFact MaterializeOwned() =>
        JsonSerializer.Deserialize(_bytes, HPDBaseJsonSerializerContext.Default.BaseRecordMutationFact)
        ?? throw new InvalidOperationException("The owned mutation fact is invalid.");
    /// <summary>Returns a new copy of the canonical fact bytes.</summary>
    public byte[] CopyCanonicalBytes() => _bytes.ToArray();
    internal static BaseOwnedMutationFact FromCanonicalBytes(byte[] bytes, int codecVersion)
    {
        ArgumentNullException.ThrowIfNull(bytes);
        BaseRecordMutationFact fact = JsonSerializer.Deserialize(bytes, HPDBaseJsonSerializerContext.Default.BaseRecordMutationFact)
            ?? throw new InvalidOperationException("The stored mutation fact is invalid.");
        return Freeze(fact, codecVersion);
    }
}

/// <summary>Provides the source-generated persistence representation of one receipt envelope.</summary>
public sealed record BaseAtomicReceiptWire
{
    /// <summary>Gets the receipt result kind.</summary>
    public required BaseAtomicReceiptResultKind Kind { get; init; }
    /// <summary>Gets canonical owned-fact wire values.</summary>
    public required BaseOwnedMutationFactWire[] Mutations { get; init; }
    /// <summary>Gets the optional selection result.</summary>
    public BaseSelectionMutationReceiptResult? SelectionMutation { get; init; }
    /// <summary>Gets the optional registered module-mutation result.</summary>
    public BaseModuleMutationReceiptResultWire? ModuleMutation { get; init; }
    /// <summary>Gets the optional subject-lifecycle checkpoint result.</summary>
    public BaseSubjectLifecycleCheckpointResult? SubjectLifecycleCheckpoint { get; init; }
    /// <summary>Gets the optional subject-lifecycle maintenance result.</summary>
    public BaseSubjectLifecycleMaintenanceResult? SubjectLifecycleMaintenance { get; init; }
    /// <summary>Gets the coordinated-retirement result.</summary>
    public BaseSubjectRetirementReceiptResult? SubjectRetirement { get; init; }
    /// <summary>Gets the optional activation-creation result.</summary>
    public BaseActivationCreationReceiptResult? ActivationCreation { get; init; }
    /// <summary>Gets the optional handler-free activation result.</summary>
    public BaseActivationTransactionalReceiptResult? ActivationTransactionalOperation { get; init; }
    internal static BaseAtomicReceiptWire From(BaseAtomicReceiptResult result)
    {
        ValidateShape(result);
        return new()
        {
            Kind = result.Kind,
            Mutations = result.Mutations.Select(static fact => new BaseOwnedMutationFactWire
            {
                CodecVersion = fact.CodecVersion,
                CanonicalBytes = fact.CopyCanonicalBytes(),
            }).ToArray(),
            SelectionMutation = result.SelectionMutation,
            ModuleMutation = result.ModuleMutation is null ? null : new BaseModuleMutationReceiptResultWire
            {
                OperationId = result.ModuleMutation.OperationId,
                OperationVersion = result.ModuleMutation.OperationVersion,
                Disposition = result.ModuleMutation.Disposition,
                Outcome = result.ModuleMutation.Outcome,
                Generations = result.ModuleMutation.Generations.Select(static generation => new BaseModuleCommittedGenerationWire
                {
                    CaptureId = generation.CaptureId,
                    CellId = generation.CellId,
                    CellVersion = generation.CellVersion,
                    Previous = generation.Previous?.ToCanonicalString(),
                    Resulting = generation.Resulting.ToCanonicalString(),
                }).ToArray(),
                CanonicalResultBytes = result.ModuleMutation.CanonicalResultBytes.ToArray(),
                CreatedActivationIds = result.ModuleMutation.CreatedActivationIds.ToArray(),
                SemanticActivation = ToWire(result.ModuleMutation.SemanticActivation),
            },
            SubjectLifecycleCheckpoint = result.SubjectLifecycleCheckpoint is null
                ? null
                : BaseSubjectLifecycleReceiptOwnership.Clone(result.SubjectLifecycleCheckpoint),
            SubjectLifecycleMaintenance = result.SubjectLifecycleMaintenance is null
                ? null
                : CloneMaintenance(result.SubjectLifecycleMaintenance),
            SubjectRetirement = result.SubjectRetirement is null ? null : CloneRetirement(result.SubjectRetirement),
            ActivationCreation = result.ActivationCreation is null ? null : new BaseActivationCreationReceiptResult
            { ActivationIds = result.ActivationCreation.ActivationIds.Select(static value => new string(value.AsSpan())).ToImmutableArray() },
            ActivationTransactionalOperation = CloneTransactional(result.ActivationTransactionalOperation),
        };
    }
    internal BaseAtomicReceiptResult Materialize()
    {
        BaseAtomicReceiptResult result = new()
        {
            Kind = Kind,
            Mutations = Mutations.Select(static fact => BaseOwnedMutationFact.FromCanonicalBytes(fact.CanonicalBytes, fact.CodecVersion)).ToImmutableArray(),
            SelectionMutation = SelectionMutation,
            ModuleMutation = ModuleMutation is null ? null : new BaseModuleMutationReceiptResult
            {
                OperationId = ModuleMutation.OperationId,
                OperationVersion = ModuleMutation.OperationVersion,
                Disposition = ModuleMutation.Disposition,
                Outcome = ModuleMutation.Outcome,
                Generations = ModuleMutation.Generations.Select(static generation => new BaseModuleCommittedGeneration
                {
                    CaptureId = generation.CaptureId,
                    CellId = generation.CellId,
                    CellVersion = generation.CellVersion,
                    Previous = generation.Previous is null ? null : BaseModuleGeneration.ParseCanonical(generation.Previous),
                    Resulting = BaseModuleGeneration.ParseCanonical(generation.Resulting),
                }).ToImmutableArray(),
                CanonicalResultBytes = ModuleMutation.CanonicalResultBytes.ToArray().ToImmutableArray(),
                CreatedActivationIds = ModuleMutation.CreatedActivationIds.ToImmutableArray(),
                SemanticActivation = FromWire(ModuleMutation.SemanticActivation),
            },
            SubjectLifecycleCheckpoint = SubjectLifecycleCheckpoint is null
                ? null
                : BaseSubjectLifecycleReceiptOwnership.Clone(SubjectLifecycleCheckpoint),
            SubjectLifecycleMaintenance = SubjectLifecycleMaintenance is null
                ? null
                : CloneMaintenance(SubjectLifecycleMaintenance),
            SubjectRetirement = SubjectRetirement is null ? null : CloneRetirement(SubjectRetirement),
            ActivationCreation = ActivationCreation is null ? null : new BaseActivationCreationReceiptResult
            { ActivationIds = ActivationCreation.ActivationIds.Select(static value => new string(value.AsSpan())).ToImmutableArray() },
            ActivationTransactionalOperation = CloneTransactional(ActivationTransactionalOperation),
        };
        ValidateShape(result);
        return result;
    }

    private static BaseSemanticActivationReceiptEvidenceWire? ToWire(BaseSemanticActivationReceiptEvidence? value) => value is null ? null : new()
    {
        Operation = (int)value.Operation,
        DefinitionId = value.DefinitionId,
        DefinitionVersion = value.DefinitionVersion,
        DefinitionChecksum = value.DefinitionChecksum.ToArray(),
        KeyDigest = value.Key.ToArray(),
        State = (int)value.State,
        SlotGeneration = value.SlotGeneration.ToString(System.Globalization.CultureInfo.InvariantCulture),
        EnsureDisposition = value.EnsureDisposition is null ? null : (int)value.EnsureDisposition.Value,
        RetirementDisposition = value.RetirementDisposition is null ? null : (int)value.RetirementDisposition.Value,
        ActivationId = value.ActivationId,
        SlotChecksum = value.SlotChecksum.ToArray(),
        JournalPosition = value.JournalPosition.ToString(System.Globalization.CultureInfo.InvariantCulture),
        CommitEvidenceChecksum = value.CommitEvidenceChecksum.ToArray(),
        RecoveryPublication = value.RecoveryPublication,
        Checksum = value.Checksum.ToArray(),
    };

    private static BaseSemanticActivationReceiptEvidence? FromWire(BaseSemanticActivationReceiptEvidenceWire? value) => value is null ? null : new()
    {
        Operation = Enum.IsDefined(typeof(BaseSemanticActivationOperationKind), value.Operation) ? (BaseSemanticActivationOperationKind)value.Operation : throw new InvalidOperationException("The semantic operation is invalid."),
        DefinitionId = value.DefinitionId,
        DefinitionVersion = value.DefinitionVersion,
        DefinitionChecksum = value.DefinitionChecksum.ToImmutableArray(),
        Key = BaseSemanticActivationKeyDigest.Create(value.KeyDigest),
        State = Enum.IsDefined(typeof(BaseSemanticActivationSlotState), value.State) ? (BaseSemanticActivationSlotState)value.State : throw new InvalidOperationException("The semantic state is invalid."),
        SlotGeneration = ParsePositiveCanonical(value.SlotGeneration),
        EnsureDisposition = value.EnsureDisposition is null ? null : Enum.IsDefined(typeof(BaseSemanticActivationEnsureDisposition), value.EnsureDisposition.Value) ? (BaseSemanticActivationEnsureDisposition)value.EnsureDisposition.Value : throw new InvalidOperationException("The semantic ensure disposition is invalid."),
        RetirementDisposition = value.RetirementDisposition is null ? null : Enum.IsDefined(typeof(BaseSemanticActivationRetirementDisposition), value.RetirementDisposition.Value) ? (BaseSemanticActivationRetirementDisposition)value.RetirementDisposition.Value : throw new InvalidOperationException("The semantic retirement disposition is invalid."),
        ActivationId = value.ActivationId,
        SlotChecksum = value.SlotChecksum.ToImmutableArray(),
        JournalPosition = ParsePositiveCanonical(value.JournalPosition),
        CommitEvidenceChecksum = value.CommitEvidenceChecksum.ToImmutableArray(),
        RecoveryPublication = value.RecoveryPublication,
        Checksum = value.Checksum.ToImmutableArray(),
    };

    private static long ParsePositiveCanonical(string value)
    {
        if (string.IsNullOrEmpty(value) || value[0] == '0' || value.Any(static character => character is < '0' or > '9')
            || !long.TryParse(value, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out long parsed)
            || parsed <= 0)
            throw new InvalidOperationException("base.mutation.receipt.invalid");
        return parsed;
    }

    private static void ValidateShape(BaseAtomicReceiptResult result)
    {
        bool valid = result.Kind switch
        {
            BaseAtomicReceiptResultKind.RecordMutations => result.SelectionMutation is null && result.ModuleMutation is null && result.SubjectLifecycleCheckpoint is null && result.SubjectLifecycleMaintenance is null && result.SubjectRetirement is null && result.ActivationCreation is null && result.ActivationTransactionalOperation is null,
            BaseAtomicReceiptResultKind.SelectionMutation => result.SelectionMutation is not null && result.ModuleMutation is null && result.SubjectLifecycleCheckpoint is null && result.SubjectLifecycleMaintenance is null && result.SubjectRetirement is null && result.ActivationCreation is null && result.ActivationTransactionalOperation is null,
            BaseAtomicReceiptResultKind.ModuleMutation => result.SelectionMutation is null && result.ModuleMutation is not null && result.SubjectLifecycleCheckpoint is null && result.SubjectLifecycleMaintenance is null && result.SubjectRetirement is null && result.ActivationCreation is null && result.ActivationTransactionalOperation is null,
            BaseAtomicReceiptResultKind.SubjectLifecycleCheckpoint => result.SelectionMutation is null && result.ModuleMutation is null && result.SubjectLifecycleCheckpoint is not null && result.SubjectLifecycleMaintenance is null && result.SubjectRetirement is null && result.ActivationCreation is null && result.ActivationTransactionalOperation is null,
            BaseAtomicReceiptResultKind.SubjectLifecycleMaintenance => result.SelectionMutation is null && result.ModuleMutation is null && result.SubjectLifecycleCheckpoint is null && result.SubjectLifecycleMaintenance is not null
                && (result.SubjectRetirement is null || result.SubjectRetirement.Operation == BaseSubjectRetirementReceiptOperation.Maintenance) && result.ActivationCreation is null && result.ActivationTransactionalOperation is null,
            BaseAtomicReceiptResultKind.SubjectRetirement => result.SelectionMutation is null && result.ModuleMutation is null && result.SubjectLifecycleCheckpoint is null && result.SubjectLifecycleMaintenance is null && result.SubjectRetirement is not null && result.ActivationCreation is null && result.ActivationTransactionalOperation is null,
            BaseAtomicReceiptResultKind.ActivationCreation => result.SelectionMutation is null && result.ModuleMutation is null && result.SubjectLifecycleCheckpoint is null && result.SubjectLifecycleMaintenance is null && result.SubjectRetirement is null && result.ActivationCreation is not null && result.ActivationTransactionalOperation is null,
            BaseAtomicReceiptResultKind.ActivationTransactionalOperation => result.SelectionMutation is null && result.ModuleMutation is null && result.SubjectLifecycleCheckpoint is null && result.SubjectLifecycleMaintenance is null && result.SubjectRetirement is null && result.ActivationCreation is null && result.ActivationTransactionalOperation is not null,
            _ => false,
        };
        if (valid && result.SubjectRetirement is { } retirement)
        {
            int payloads = (retirement.Acknowledgement is null ? 0 : 1) + (retirement.Timeout is null ? 0 : 1)
                + (retirement.Override is null ? 0 : 1) + (retirement.Purge is null ? 0 : 1)
                + (retirement.ConsumerRemoval is null ? 0 : 1) + (retirement.Maintenance is null ? 0 : 1);
            valid = payloads == 1 && retirement.Operation switch
            {
                BaseSubjectRetirementReceiptOperation.Acknowledgement => retirement.Acknowledgement is not null,
                BaseSubjectRetirementReceiptOperation.Timeout => retirement.Timeout is not null,
                BaseSubjectRetirementReceiptOperation.Override => retirement.Override is not null,
                BaseSubjectRetirementReceiptOperation.FinalPurge => retirement.Purge is not null,
                BaseSubjectRetirementReceiptOperation.ConsumerRemoval => retirement.ConsumerRemoval is not null,
                BaseSubjectRetirementReceiptOperation.Maintenance => retirement.Maintenance is not null,
                _ => false,
            };
        }
        if (valid && result.ModuleMutation?.SemanticActivation is { } semantic)
            valid = result.ModuleMutation.CreatedActivationIds.IsEmpty && SemanticShapeValid(semantic);
        if (!valid) throw new InvalidOperationException("base.mutation.receipt.invalid");
    }

    private static bool SemanticShapeValid(BaseSemanticActivationReceiptEvidence value)
    {
        if (string.IsNullOrWhiteSpace(value.DefinitionId) || value.DefinitionVersion <= 0 || value.SlotGeneration <= 0
            || value.JournalPosition <= 0 || value.DefinitionChecksum.Length != 32 || value.SlotChecksum.Length != 32
            || value.CommitEvidenceChecksum.Length != 32 || value.Checksum.Length != 32
            || value.RecoveryPublication is { } recovery && !RecoveryShapeValid(recovery, value))
            return false;
        if (!System.Security.Cryptography.CryptographicOperations.FixedTimeEquals(
                BaseSemanticActivationEvidenceContract.ReceiptChecksum(value).AsSpan(), value.Checksum.AsSpan()))
            return false;

        return value.Operation switch
        {
            BaseSemanticActivationOperationKind.Ensure when value.EnsureDisposition is { } disposition && value.RetirementDisposition is null => disposition switch
            {
                BaseSemanticActivationEnsureDisposition.Created or BaseSemanticActivationEnsureDisposition.Existing =>
                    value.State == BaseSemanticActivationSlotState.Live && IsActivationId(value.ActivationId),
                BaseSemanticActivationEnsureDisposition.Retired =>
                    value.State is BaseSemanticActivationSlotState.Retired or BaseSemanticActivationSlotState.CompactedAbsent && value.ActivationId is null,
                _ => false,
            },
            BaseSemanticActivationOperationKind.Retire when value.EnsureDisposition is null && value.RetirementDisposition is { } disposition => disposition switch
            {
                BaseSemanticActivationRetirementDisposition.RetiredNow or BaseSemanticActivationRetirementDisposition.AlreadyRetired =>
                    value.State == BaseSemanticActivationSlotState.Retired && value.ActivationId is null,
                BaseSemanticActivationRetirementDisposition.AlreadyCompacted =>
                    value.State == BaseSemanticActivationSlotState.CompactedAbsent && value.ActivationId is null,
                _ => false,
            },
            _ => false,
        };
    }

    private static bool RecoveryShapeValid(BaseSemanticRecoveryLocalReceiptAuthority value,
        BaseSemanticActivationReceiptEvidence semantic)
    {
        BaseSemanticRecoveryPendingCommitAuthority pending = value.PendingAuthority;
        return semantic.Operation == BaseSemanticActivationOperationKind.Retire
            && semantic.RetirementDisposition == BaseSemanticActivationRetirementDisposition.RetiredNow
            && semantic.State == BaseSemanticActivationSlotState.Retired
            && pending.AuthorityVersion > 0 && !string.IsNullOrWhiteSpace(pending.AuthorityId)
            && pending.AuthorityChecksum.Length == 32 && pending.Checksum.Length == 32
            && value.FinalEntry.State == BaseSemanticActivationSlotState.Retired
            && value.FinalEntry.SlotGeneration == semantic.SlotGeneration
            && value.FinalEntry.Checksum.Length == 32 && value.Checksum.Length == 32
            && BaseSemanticRecoveryAuthorityContract.PendingCommitChecksum(pending).AsSpan().SequenceEqual(pending.Checksum.AsSpan())
            && BaseSemanticRecoveryAuthorityContract.RecoveryEntryChecksum(value.FinalEntry).AsSpan().SequenceEqual(value.FinalEntry.Checksum.AsSpan())
            && BaseSemanticRecoveryAuthorityContract.LocalReceiptAuthorityChecksum(value).AsSpan().SequenceEqual(value.Checksum.AsSpan());
    }

    private static bool IsActivationId(string? value) => value is { Length: 64 }
        && value.All(static character => character is >= '0' and <= '9' or >= 'a' and <= 'f');

    private static BaseSubjectLifecycleMaintenanceResult CloneMaintenance(BaseSubjectLifecycleMaintenanceResult value) => value with
    {
        RollingChecksum = new string(value.RollingChecksum.AsSpan()),
    };
    private static BaseActivationTransactionalReceiptResult? CloneTransactional(BaseActivationTransactionalReceiptResult? value) =>
        value is null ? null : value with
        {
            Generations = value.Generations.Select(static generation => generation with { }).ToImmutableArray(),
            CanonicalResultBytes = value.CanonicalResultBytes.ToArray().ToImmutableArray(),
            ActivationControlChecksum = value.ActivationControlChecksum.ToArray().ToImmutableArray(),
        };
    private static BaseSubjectRetirementReceiptResult CloneRetirement(BaseSubjectRetirementReceiptResult value) => value with
    {
        Acknowledgement = value.Acknowledgement is null ? null : value.Acknowledgement with
        {
            BarrierChecksum = value.Acknowledgement.BarrierChecksum is null ? null : new string(value.Acknowledgement.BarrierChecksum.AsSpan()),
        },
        Timeout = value.Timeout is null ? null : value.Timeout with { BarrierChecksum = new string(value.Timeout.BarrierChecksum.AsSpan()) },
        Override = value.Override is null ? null : value.Override with { BarrierChecksum = new string(value.Override.BarrierChecksum.AsSpan()) },
        Purge = value.Purge is null ? null : value.Purge with { TerminalReceiptChecksum = new string(value.Purge.TerminalReceiptChecksum.AsSpan()) },
        ConsumerRemoval = value.ConsumerRemoval is null ? null : value.ConsumerRemoval with { AcceptedConsumerSetChecksum = new string(value.ConsumerRemoval.AcceptedConsumerSetChecksum.AsSpan()) },
        Maintenance = value.Maintenance is null ? null : value.Maintenance with { RollingChecksum = new string(value.Maintenance.RollingChecksum.AsSpan()) },
    };
}

/// <summary>Provides the source-generated persistence representation of one owned fact.</summary>
public sealed record BaseOwnedMutationFactWire
{
    /// <summary>Gets the canonical codec version.</summary>
    public required int CodecVersion { get; init; }
    /// <summary>Gets copied canonical fact bytes.</summary>
    public required byte[] CanonicalBytes { get; init; }
}

/// <summary>Stores one closed deeply owned atomic receipt result.</summary>
public sealed record BaseAtomicReceiptResult
{
    /// <summary>Gets the result kind.</summary>
    public required BaseAtomicReceiptResultKind Kind { get; init; }
    /// <summary>Gets deeply owned mutation facts.</summary>
    public required ImmutableArray<BaseOwnedMutationFact> Mutations { get; init; }
    /// <summary>Gets the selection result when <see cref="Kind"/> is selection mutation.</summary>
    public BaseSelectionMutationReceiptResult? SelectionMutation { get; init; }
    /// <summary>Gets the module result when <see cref="Kind"/> is module mutation.</summary>
    public BaseModuleMutationReceiptResult? ModuleMutation { get; init; }
    /// <summary>Gets the lifecycle checkpoint result when <see cref="Kind"/> is a checkpoint advancement.</summary>
    public BaseSubjectLifecycleCheckpointResult? SubjectLifecycleCheckpoint { get; init; }
    /// <summary>Gets the lifecycle maintenance result when <see cref="Kind"/> is maintenance.</summary>
    public BaseSubjectLifecycleMaintenanceResult? SubjectLifecycleMaintenance { get; init; }
    /// <summary>Gets the subject-retirement result when <see cref="Kind"/> is retirement.</summary>
    public BaseSubjectRetirementReceiptResult? SubjectRetirement { get; init; }
    /// <summary>Gets the activation result when <see cref="Kind"/> is activation creation.</summary>
    public BaseActivationCreationReceiptResult? ActivationCreation { get; init; }
    /// <summary>Gets the handler-free activation result when <see cref="Kind"/> is transactional activation.</summary>
    public BaseActivationTransactionalReceiptResult? ActivationTransactionalOperation { get; init; }

    internal static BaseAtomicReceiptResult FromFacts(IEnumerable<BaseRecordMutationFact> facts) => new()
    {
        Kind = BaseAtomicReceiptResultKind.RecordMutations,
        Mutations = facts.Select(static fact => BaseOwnedMutationFact.Freeze(fact, 1)).ToImmutableArray(),
        SelectionMutation = null,
        ModuleMutation = null,
        SubjectLifecycleCheckpoint = null,
        SubjectLifecycleMaintenance = null,
        SubjectRetirement = null,
        ActivationCreation = null,
        ActivationTransactionalOperation = null,
    };
    internal BaseRecordMutationFact[] MaterializeFacts() => Mutations.Select(static fact => fact.MaterializeOwned()).ToArray();
}
