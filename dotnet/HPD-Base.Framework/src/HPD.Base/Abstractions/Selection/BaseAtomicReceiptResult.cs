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
    internal static BaseAtomicReceiptWire From(BaseAtomicReceiptResult result) => new()
    {
        Kind = result.Kind,
        Mutations = result.Mutations.Select(static fact => new BaseOwnedMutationFactWire
        {
            CodecVersion = fact.CodecVersion,
            CanonicalBytes = fact.CopyCanonicalBytes(),
        }).ToArray(),
        SelectionMutation = result.SelectionMutation,
    };
    internal BaseAtomicReceiptResult Materialize() => new()
    {
        Kind = Kind,
        Mutations = Mutations.Select(static fact => BaseOwnedMutationFact.FromCanonicalBytes(fact.CanonicalBytes, fact.CodecVersion)).ToImmutableArray(),
        SelectionMutation = SelectionMutation,
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

    internal static BaseAtomicReceiptResult FromFacts(IEnumerable<BaseRecordMutationFact> facts) => new()
    {
        Kind = BaseAtomicReceiptResultKind.RecordMutations,
        Mutations = facts.Select(static fact => BaseOwnedMutationFact.Freeze(fact, 1)).ToImmutableArray(),
    };
    internal BaseRecordMutationFact[] MaterializeFacts() => Mutations.Select(static fact => fact.MaterializeOwned()).ToArray();
}
