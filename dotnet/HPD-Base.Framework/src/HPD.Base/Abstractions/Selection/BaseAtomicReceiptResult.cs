using System.Collections.Immutable;
using System.Text.Json;

namespace HPD.Base;

/// <summary>Identifies the closed result stored in one atomic mutation receipt.</summary>
public enum BaseAtomicReceiptResultKind
{
    /// <summary>An ordinary record-mutation result.</summary>
    RecordMutations,
    /// <summary>A transaction-bound selection mutation result.</summary>
    SelectionMutation,
    /// <summary>A registered module-mutation result.</summary>
    ModuleMutation,
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
            },
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
            },
        };
        ValidateShape(result);
        return result;
    }

    private static void ValidateShape(BaseAtomicReceiptResult result)
    {
        bool valid = result.Kind switch
        {
            BaseAtomicReceiptResultKind.RecordMutations => result.SelectionMutation is null && result.ModuleMutation is null,
            BaseAtomicReceiptResultKind.SelectionMutation => result.SelectionMutation is not null && result.ModuleMutation is null,
            BaseAtomicReceiptResultKind.ModuleMutation => result.SelectionMutation is null && result.ModuleMutation is not null,
            _ => false,
        };
        if (!valid) throw new InvalidOperationException("base.mutation.receipt.invalid");
    }
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

    internal static BaseAtomicReceiptResult FromFacts(IEnumerable<BaseRecordMutationFact> facts) => new()
    {
        Kind = BaseAtomicReceiptResultKind.RecordMutations,
        Mutations = facts.Select(static fact => BaseOwnedMutationFact.Freeze(fact, 1)).ToImmutableArray(),
        SelectionMutation = null,
        ModuleMutation = null,
    };
    internal BaseRecordMutationFact[] MaterializeFacts() => Mutations.Select(static fact => fact.MaterializeOwned()).ToArray();
}
