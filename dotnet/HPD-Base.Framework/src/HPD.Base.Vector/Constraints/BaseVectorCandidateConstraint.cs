using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text;

namespace HPD.Base;

/// <summary>Names the closed portable value kinds supported by vector candidate constraints.</summary>
public enum BaseVectorFilterValueKind
{
    /// <summary>An explicitly stored null.</summary>
    Null,
    /// <summary>An ordinal UTF-8 string.</summary>
    String,
    /// <summary>A Boolean.</summary>
    Boolean,
    /// <summary>A signed 64-bit integer.</summary>
    Integer,
    /// <summary>A stable ordinal identifier.</summary>
    Id,
}

/// <summary>Identifies one generated vector-filter field and its exact portable value kind.</summary>
public readonly record struct BaseVectorFilterField
{
    /// <summary>Initializes a validated vector-filter field.</summary>
    public BaseVectorFilterField(string stableFieldId, BaseVectorFilterValueKind valueKind)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(stableFieldId);
        if (Encoding.UTF8.GetByteCount(stableFieldId) > 128)
            throw new ArgumentOutOfRangeException(nameof(stableFieldId));
        StableFieldId = new string(stableFieldId.AsSpan());
        ValueKind = valueKind;
    }
    /// <summary>Gets the stable field identifier.</summary>
    public string StableFieldId { get; }
    /// <summary>Gets the portable value kind.</summary>
    public BaseVectorFilterValueKind ValueKind { get; }
}

/// <summary>Contains one immutable, closed vector-filter value.</summary>
public sealed class BaseVectorFilterValue : IEquatable<BaseVectorFilterValue>
{
    private BaseVectorFilterValue(BaseVectorFilterValueKind kind, string? text, bool? boolean, long? integer)
    { Kind = kind; Text = text is null ? null : new string(text.AsSpan()); Boolean = boolean; Integer = integer; }
    /// <summary>Gets the value kind.</summary>
    public BaseVectorFilterValueKind Kind { get; }
    /// <summary>Gets the string or ID value.</summary>
    public string? Text { get; }
    /// <summary>Gets the Boolean value.</summary>
    public bool? Boolean { get; }
    /// <summary>Gets the signed integer value.</summary>
    public long? Integer { get; }
    /// <summary>Creates the explicit-null value.</summary>
    public static BaseVectorFilterValue Null() => new(BaseVectorFilterValueKind.Null, null, null, null);
    /// <summary>Creates an ordinal string value.</summary>
    public static BaseVectorFilterValue FromString(string value) => new(BaseVectorFilterValueKind.String, Bounded(value), null, null);
    /// <summary>Creates a Boolean value.</summary>
    public static BaseVectorFilterValue FromBoolean(bool value) => new(BaseVectorFilterValueKind.Boolean, null, value, null);
    /// <summary>Creates a signed 64-bit integer value.</summary>
    public static BaseVectorFilterValue FromInteger(long value) => new(BaseVectorFilterValueKind.Integer, null, null, value);
    /// <summary>Creates a stable ordinal identifier value.</summary>
    public static BaseVectorFilterValue FromId(string value) => new(BaseVectorFilterValueKind.Id, Bounded(value), null, null);
    /// <inheritdoc />
    public bool Equals(BaseVectorFilterValue? other) => other is not null && Kind == other.Kind && Text == other.Text && Boolean == other.Boolean && Integer == other.Integer;
    /// <inheritdoc />
    public override bool Equals(object? obj) => obj is BaseVectorFilterValue other && Equals(other);
    /// <inheritdoc />
    public override int GetHashCode() => HashCode.Combine(Kind, Text, Boolean, Integer);
    private static string Bounded(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        if (Encoding.UTF8.GetByteCount(value) > 512) throw new ArgumentOutOfRangeException(nameof(value));
        return new string(value.AsSpan());
    }
}

/// <summary>Defines the normalized, immutable constraint applied before vector ranking.</summary>
public abstract record BaseVectorCandidateConstraint
{
    private BaseVectorCandidateConstraint() { }
    /// <summary>Matches every candidate.</summary>
    public sealed record True : BaseVectorCandidateConstraint;
    /// <summary>Matches no candidates.</summary>
    public sealed record False : BaseVectorCandidateConstraint;
    /// <summary>Requires every child constraint.</summary>
    public sealed record And : BaseVectorCandidateConstraint
    {
        /// <summary>Initializes an AND node with owned children.</summary>
        public And(IEnumerable<BaseVectorCandidateConstraint> children) => Children = Copy(children);
        /// <summary>Gets the immutable children.</summary>
        public ImmutableArray<BaseVectorCandidateConstraint> Children { get; }
    }
    /// <summary>Requires at least one child constraint.</summary>
    public sealed record Or : BaseVectorCandidateConstraint
    {
        /// <summary>Initializes an OR node with owned children.</summary>
        public Or(IEnumerable<BaseVectorCandidateConstraint> children) => Children = Copy(children);
        /// <summary>Gets the immutable children.</summary>
        public ImmutableArray<BaseVectorCandidateConstraint> Children { get; }
    }
    /// <summary>Matches one exact typed value.</summary>
    public sealed record Equal : BaseVectorCandidateConstraint
    {
        /// <summary>Initializes a typed equality node.</summary>
        public Equal(BaseVectorFilterField field, BaseVectorFilterValue value)
        { ArgumentNullException.ThrowIfNull(value); if (field.ValueKind != value.Kind) throw new ArgumentException("The value must match the field kind.", nameof(value)); Field = field; Value = value; }
        /// <summary>Gets the compared field.</summary>
        public BaseVectorFilterField Field { get; }
        /// <summary>Gets the compared value.</summary>
        public BaseVectorFilterValue Value { get; }
    }
    /// <summary>Matches membership in one bounded typed set.</summary>
    public sealed record In : BaseVectorCandidateConstraint
    {
        /// <summary>Initializes an IN node with owned values.</summary>
        public In(BaseVectorFilterField field, IEnumerable<BaseVectorFilterValue> values)
        { Field = field; Values = values?.ToImmutableArray() ?? throw new ArgumentNullException(nameof(values)); if (Values.Length is < 1 or > 64) throw new ArgumentOutOfRangeException(nameof(values)); if (Values.Any(v => v is null || v.Kind != field.ValueKind)) throw new ArgumentException("Every value must match the field kind.", nameof(values)); }
        /// <summary>Gets the field.</summary>
        public BaseVectorFilterField Field { get; }
        /// <summary>Gets the immutable values.</summary>
        public ImmutableArray<BaseVectorFilterValue> Values { get; }
    }
    private static ImmutableArray<BaseVectorCandidateConstraint> Copy(IEnumerable<BaseVectorCandidateConstraint> children)
    { var result = children?.ToImmutableArray() ?? throw new ArgumentNullException(nameof(children)); if (result.Length is < 1 or > 16 || result.Any(static child => child is null)) throw new ArgumentOutOfRangeException(nameof(children)); return result; }
}

/// <summary>Contains the immutable SHA-256 identity of one normalized vector constraint.</summary>
public readonly struct BaseVectorConstraintDigest : IEquatable<BaseVectorConstraintDigest>
{
    private readonly byte[]? _bytes;
    private BaseVectorConstraintDigest(ReadOnlySpan<byte> bytes) => _bytes = bytes.ToArray();
    /// <summary>Creates a digest by copying exactly 32 bytes.</summary>
    public static BaseVectorConstraintDigest Create(ReadOnlySpan<byte> bytes) => bytes.Length == 32 ? new(bytes) : throw new ArgumentException("A vector constraint digest must contain 32 bytes.", nameof(bytes));
    /// <summary>Copies the digest into caller-owned storage.</summary>
    public byte[] ToArray() => (_bytes ?? throw new InvalidOperationException("The default vector constraint digest is invalid.")).ToArray();
    /// <inheritdoc />
    public bool Equals(BaseVectorConstraintDigest other) => _bytes is not null && other._bytes is not null && CryptographicOperations.FixedTimeEquals(_bytes, other._bytes);
    /// <inheritdoc />
    public override bool Equals(object? obj) => obj is BaseVectorConstraintDigest other && Equals(other);
    /// <inheritdoc />
    public override int GetHashCode() => _bytes is null ? 0 : BitConverter.ToInt32(_bytes, 0);
    /// <inheritdoc />
    public override string ToString() => "BaseVectorConstraintDigest[redacted]";
}
