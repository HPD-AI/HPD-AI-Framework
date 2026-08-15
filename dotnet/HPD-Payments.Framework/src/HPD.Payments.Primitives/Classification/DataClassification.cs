namespace HPD.Payments.Primitives.Classification;

/// <summary>Represents the bounded immutable <c>DataClassification</c> vocabulary value; its default form is invalid unless explicitly documented otherwise.</summary>
public enum DataClassification
{
    /// <summary>Invalid default; it never authorizes disclosure.</summary>
    None = 0,
    /// <summary>Information approved for public disclosure.</summary>
    Public,
    /// <summary>Information limited to internal operational audiences.</summary>
    Internal,
    /// <summary>Tenant- or subject-linked information requiring controlled disclosure.</summary>
    Confidential,
    /// <summary>Sensitive payload or evidence requiring stricter access and logging controls.</summary>
    Restricted,
    /// <summary>Secret material that should normally be represented by a reference or proof digest.</summary>
    Secret
}

/// <summary>Represents the bounded immutable <c>RetentionKind</c> vocabulary value; its default form is invalid unless explicitly documented otherwise.</summary>
public enum RetentionKind
{
    /// <summary>Invalid default; no retention decision was supplied.</summary>
    None = 0,
    /// <summary>Lexical or short-lived data not retained across an escape boundary.</summary>
    Ephemeral,
    /// <summary>Data retained for a bounded operational purpose.</summary>
    Operational,
    /// <summary>Data retained as durable history or evidence.</summary>
    Durable,
    /// <summary>Data retained because a legal hold prevents normal disposition.</summary>
    LegalHold,
    /// <summary>Only an owned custody reference is retained; bytes remain externally held.</summary>
    ExternalCustody
}

/// <summary>Represents the bounded immutable <c>ClassificationMark</c> vocabulary value; its default form is invalid unless explicitly documented otherwise.</summary>
public readonly record struct ClassificationMark(DataClassification Classification, RetentionKind Retention)
{
    /// <summary>Gets whether the value satisfies its required-field and bound invariants; a default value is invalid.</summary>
    public bool IsValid => Classification != DataClassification.None && Retention != RetentionKind.None && Enum.IsDefined(Classification) && Enum.IsDefined(Retention);
    /// <summary>Creates a validated value and rejects missing, unknown, or out-of-bound components.</summary>
    public static ClassificationMark Create(DataClassification classification, RetentionKind retention) =>
        classification != DataClassification.None && retention != RetentionKind.None && Enum.IsDefined(classification) && Enum.IsDefined(retention) ? new(classification, retention) : throw new ArgumentOutOfRangeException(nameof(classification));
}

/// <summary>Owns a defensive copy of bounded bytes together with classification and retention metadata.</summary>
/// <remarks>No caller span, array, pool lease, or returned copy aliases retained storage.</remarks>
public sealed class OwnedClassifiedBytes : IEquatable<OwnedClassifiedBytes>
{
    /// <summary>Specifies the maximum admitted size or count enforced by the containing type.</summary>
    public const int MaximumBytes = 1_048_576;
    private readonly byte[] _bytes;
    /// <summary>Gets the validated <c>Mark</c> component; it does not imply ambient context or mutation authority.</summary>
    public ClassificationMark Mark { get; }
    /// <summary>Gets the enforced bound or owned logical length.</summary>
    public int Length => _bytes.Length;

    /// <summary>Copies caller bytes into stable owned storage.</summary>
    /// <param name="source">Borrowed lexical bytes copied before the constructor returns.</param>
    /// <param name="mark">A valid non-default classification and retention mark.</param>
    /// <param name="maximumBytes">The boundary-specific maximum, from zero through <see cref="MaximumBytes"/>.</param>
    /// <exception cref="ArgumentException">The mark or bound is invalid, or the source exceeds the bound.</exception>
    public OwnedClassifiedBytes(ReadOnlySpan<byte> source, ClassificationMark mark, int maximumBytes = MaximumBytes)
    {
        if (!mark.IsValid || maximumBytes is < 0 or > MaximumBytes || source.Length > maximumBytes) throw new ArgumentException("Invalid classification or byte bound.");
        _bytes = source.ToArray();
        Mark = mark;
    }

    /// <summary>Returns a newly allocated copy; callers never receive an alias to retained storage.</summary>
    public byte[] CopyBytes() => (byte[])_bytes.Clone();
    /// <summary>Compares values using the containing type’s stable component and owned-content equality rules.</summary>
    public bool Equals(OwnedClassifiedBytes? other) => other is not null && Mark == other.Mark && _bytes.AsSpan().SequenceEqual(other._bytes);
    /// <summary>Compares values using the containing type’s stable component and owned-content equality rules.</summary>
    public override bool Equals(object? obj) => Equals(obj as OwnedClassifiedBytes);
    /// <summary>Returns a process-local hash consistent with equality; the hash is never a persisted identity.</summary>
    public override int GetHashCode() { var h = new HashCode(); h.Add(Mark); foreach (var b in _bytes) h.Add(b); return h.ToHashCode(); }
}
