using System.Collections.Immutable;

#pragma warning disable CS1591 // XML documentation is completed before the contract checkpoint closes.

namespace HPD.Base;

/// <summary>Identifies one graph-owned logical index.</summary>
public readonly struct BaseLogicalIndexId : IEquatable<BaseLogicalIndexId>, IComparable<BaseLogicalIndexId>
{
    private readonly string? _value;
    private BaseLogicalIndexId(string value) => _value = value;
    public static BaseLogicalIndexId Create(string value) { BaseApplicationId.Validate(value, nameof(value)); return new(new string(value.AsSpan())); }
    public static bool TryParse(string? value, out BaseLogicalIndexId result) { try { result = Create(value!); return true; } catch { result = default; return false; } }
    public bool IsValid => _value is not null;
    public override string ToString() => _value ?? throw new InvalidOperationException(BaseSchemaErrorCodes.ContractInvalid);
    public bool Equals(BaseLogicalIndexId other) => IsValid && string.Equals(_value, other._value, StringComparison.Ordinal);
    public override bool Equals(object? obj) => obj is BaseLogicalIndexId other && Equals(other);
    public override int GetHashCode() => StringComparer.Ordinal.GetHashCode(_value ?? string.Empty);
    public int CompareTo(BaseLogicalIndexId other) => StringComparer.Ordinal.Compare(_value, other._value);
    public static bool operator ==(BaseLogicalIndexId left, BaseLogicalIndexId right) => left.Equals(right);
    public static bool operator !=(BaseLogicalIndexId left, BaseLogicalIndexId right) => !left.Equals(right);
}

/// <summary>Identifies one node in an index-owned predicate tree.</summary>
public readonly struct BaseIndexPredicateId : IEquatable<BaseIndexPredicateId>, IComparable<BaseIndexPredicateId>
{
    private readonly string? _value;
    private BaseIndexPredicateId(string value) => _value = value;
    public static BaseIndexPredicateId Create(string value) { BaseApplicationId.Validate(value, nameof(value)); return new(new string(value.AsSpan())); }
    public static bool TryParse(string? value, out BaseIndexPredicateId result) { try { result = Create(value!); return true; } catch { result = default; return false; } }
    public bool IsValid => _value is not null;
    public override string ToString() => _value ?? throw new InvalidOperationException(BaseSchemaErrorCodes.ContractInvalid);
    public bool Equals(BaseIndexPredicateId other) => IsValid && string.Equals(_value, other._value, StringComparison.Ordinal);
    public override bool Equals(object? obj) => obj is BaseIndexPredicateId other && Equals(other);
    public override int GetHashCode() => StringComparer.Ordinal.GetHashCode(_value ?? string.Empty);
    public int CompareTo(BaseIndexPredicateId other) => StringComparer.Ordinal.Compare(_value, other._value);
    public static bool operator ==(BaseIndexPredicateId left, BaseIndexPredicateId right) => left.Equals(right);
    public static bool operator !=(BaseIndexPredicateId left, BaseIndexPredicateId right) => !left.Equals(right);
}

/// <summary>Contains one immutable logical-index checksum.</summary>
public readonly struct BaseLogicalIndexChecksum : IEquatable<BaseLogicalIndexChecksum>
{
    private readonly BaseSchemaAuthorityChecksum _value;
    private BaseLogicalIndexChecksum(BaseSchemaAuthorityChecksum value) => _value = value;
    public static BaseLogicalIndexChecksum Create(ReadOnlySpan<byte> bytes) => new(BaseSchemaAuthorityChecksum.Create(bytes));
    public bool IsValid => _value.IsValid;
    public byte[] ToArray() => _value.ToArray();
    public void CopyTo(Span<byte> destination) => _value.CopyTo(destination);
    public bool Equals(BaseLogicalIndexChecksum other) => _value.Equals(other._value);
    public override bool Equals(object? obj) => obj is BaseLogicalIndexChecksum other && Equals(other);
    public override int GetHashCode() => _value.GetHashCode();
    public override string ToString() => _value.ToString();
    public static bool operator ==(BaseLogicalIndexChecksum left, BaseLogicalIndexChecksum right) => left.Equals(right);
    public static bool operator !=(BaseLogicalIndexChecksum left, BaseLogicalIndexChecksum right) => !left.Equals(right);
}

public enum BaseIndexSortDirection { Ascending = 0, Descending = 1 }
public enum BaseIndexCollation { OrdinalBinary = 0 }
public enum BaseIndexNullOrder { MissingThenNullThenValue = 0, ValueThenNullThenMissing = 1 }
public enum BaseIndexPredicateNodeKind { True = 0, False = 1, IsDefined = 2, IsMissing = 3, IsNull = 4, IsNotNull = 5, Equal = 6, And = 7, Or = 8, Not = 9 }

/// <summary>Contains one canonical non-null scalar literal.</summary>
public sealed record BaseCanonicalScalarLiteral
{
    public required BaseScalarKind Kind { get; init; }
    public required BaseScalarCodecAuthority Codec { get; init; }
    public required ImmutableArray<byte> CanonicalBytes { get; init; }
}

/// <summary>Contains one node in an index-owned predicate tree.</summary>
public sealed record BaseIndexPredicateNode
{
    public required BaseIndexPredicateId Id { get; init; }
    public required BaseIndexPredicateNodeKind Kind { get; init; }
    public int? FieldOrdinal { get; init; }
    public BaseCanonicalScalarLiteral? Literal { get; init; }
    public ImmutableArray<BaseIndexPredicateId> Children { get; init; } = [];
}

/// <summary>Contains one complete index-owned predicate registry.</summary>
public sealed record BaseIndexPredicateRegistry
{
    public required BaseIndexPredicateId Root { get; init; }
    public required ImmutableArray<BaseIndexPredicateNode> Nodes { get; init; }
    public required BaseSchemaAuthorityChecksum Checksum { get; init; }
}

/// <summary>Contains one ordered logical index part.</summary>
public sealed record BaseLogicalIndexPart
{
    public required int FieldOrdinal { get; init; }
    public required BaseIndexSortDirection Direction { get; init; }
    public required BaseIndexCollation Collation { get; init; }
    public required BaseIndexNullOrder NullOrder { get; init; }
}

/// <summary>Contains one exact logical index definition.</summary>
public sealed record BaseLogicalIndexDefinition
{
    public required BaseLogicalIndexId Id { get; init; }
    public required long Version { get; init; }
    public required string CollectionId { get; init; }
    public required ImmutableArray<BaseLogicalIndexPart> Parts { get; init; }
    public required bool Unique { get; init; }
    public required bool StoreRequired { get; init; }
    public required BaseIndexPredicateRegistry MembershipPredicate { get; init; }
    public required BaseLogicalIndexChecksum Checksum { get; init; }
}
