using System.Collections.Immutable;
using System.Security.Cryptography;

#pragma warning disable CS1591 // XML documentation is completed before the contract checkpoint closes.

namespace HPD.Base;

/// <summary>Controls whether a field must be present in its canonical record.</summary>
public enum BaseFieldPresence { Required = 0, Optional = 1 }

/// <summary>Controls whether an explicitly present field may contain JSON null.</summary>
public enum BaseFieldNullability { NonNullable = 0, Nullable = 1 }

/// <summary>Identifies one closed scalar representation.</summary>
public enum BaseScalarKind
{
    String = 0, Binary = 1, Int32 = 2, Int64 = 3, UInt32 = 4, UInt64 = 5,
    Decimal = 6, Boolean = 7, Guid = 8, UtcDateTime = 9, ClosedEnum = 10,
    CanonicalJson = 11, FrozenArray = 12, RecordId = 13, ModuleGeneration = 14,
}

/// <summary>Identifies one closed scalar constraint family.</summary>
public enum BaseScalarConstraintKind
{
    Utf8Bytes = 0, StringNormalization = 1, Int32Range = 2, Int64Range = 3,
    UInt32Range = 4, UInt64Range = 5, DecimalRange = 6, EnumLiterals = 7,
    BinaryBytes = 8, CanonicalJson = 9, CollectionItems = 10,
}

/// <summary>Defines validation-only string normalization requirements.</summary>
public enum BaseStringNormalizationRequirement { RequireNfc = 0 }

/// <summary>Defines an admitted canonical JSON root shape.</summary>
public enum BaseJsonShape { Object = 0, Array = 1, ObjectOrArray = 2 }

/// <summary>Contains one reduced exact decimal value.</summary>
public readonly record struct BaseDecimalValue
{
    /// <summary>Initializes a reduced exact decimal value.</summary>
    public BaseDecimalValue(Int128 coefficient, byte scale)
    {
        if (scale > 28) throw new ArgumentOutOfRangeException(nameof(scale));
        while (coefficient != 0 && scale > 0 && coefficient % 10 == 0) { coefficient /= 10; scale--; }
        Coefficient = coefficient;
        Scale = coefficient == 0 ? (byte)0 : scale;
    }
    /// <summary>Gets the signed coefficient.</summary>
    public Int128 Coefficient { get; }
    /// <summary>Gets the decimal scale.</summary>
    public byte Scale { get; }
}

/// <summary>Contains the complete closed constraints for one scalar field.</summary>
public sealed record BaseScalarConstraintSet
{
    public int? MinimumUtf8Bytes { get; init; }
    public int? MaximumUtf8Bytes { get; init; }
    public BaseStringNormalizationRequirement? StringNormalization { get; init; }
    public int? MinimumInt32 { get; init; }
    public int? MaximumInt32 { get; init; }
    public long? MinimumInt64 { get; init; }
    public long? MaximumInt64 { get; init; }
    public uint? MinimumUInt32 { get; init; }
    public uint? MaximumUInt32 { get; init; }
    public ulong? MinimumUInt64 { get; init; }
    public ulong? MaximumUInt64 { get; init; }
    public BaseDecimalValue? MinimumDecimal { get; init; }
    public BaseDecimalValue? MaximumDecimal { get; init; }
    public ImmutableArray<string> AllowedEnumLiterals { get; init; } = [];
    public int? MaximumBinaryBytes { get; init; }
    public int? MaximumCanonicalJsonBytes { get; init; }
    public BaseJsonShape? JsonShape { get; init; }
    public int? MaximumJsonDepth { get; init; }
    public int? MaximumJsonArrayItems { get; init; }
    public int? MaximumJsonObjectProperties { get; init; }
    public int? MaximumJsonTotalNodes { get; init; }
    public int? MaximumJsonTotalStringUtf8Bytes { get; init; }
    public int? MaximumJsonTotalNameUtf8Bytes { get; init; }
    public int? MinimumCollectionItems { get; init; }
    public int? MaximumCollectionItems { get; init; }
}

/// <summary>Identifies one installed scalar codec.</summary>
public readonly struct BaseScalarCodecId : IEquatable<BaseScalarCodecId>, IComparable<BaseScalarCodecId>
{
    private readonly string? _value;
    private BaseScalarCodecId(string value) => _value = value;
    public static BaseScalarCodecId Create(string value) { BaseApplicationId.Validate(value, nameof(value)); return new(new string(value.AsSpan())); }
    public static bool TryParse(string? value, out BaseScalarCodecId result) { try { result = Create(value!); return true; } catch { result = default; return false; } }
    public bool IsValid => _value is not null;
    public override string ToString() => _value ?? throw new InvalidOperationException(BaseSchemaErrorCodes.ContractInvalid);
    public bool Equals(BaseScalarCodecId other) => string.Equals(_value, other._value, StringComparison.Ordinal) && IsValid;
    public override bool Equals(object? obj) => obj is BaseScalarCodecId other && Equals(other);
    public override int GetHashCode() => StringComparer.Ordinal.GetHashCode(_value ?? string.Empty);
    public int CompareTo(BaseScalarCodecId other) => StringComparer.Ordinal.Compare(_value, other._value);
    public static bool operator ==(BaseScalarCodecId left, BaseScalarCodecId right) => left.Equals(right);
    public static bool operator !=(BaseScalarCodecId left, BaseScalarCodecId right) => !left.Equals(right);
}

/// <summary>Contains one immutable 32-byte schema authority checksum.</summary>
public readonly struct BaseSchemaAuthorityChecksum : IEquatable<BaseSchemaAuthorityChecksum>
{
    private readonly byte[]? _bytes;
    private BaseSchemaAuthorityChecksum(byte[] bytes) => _bytes = bytes;
    public static BaseSchemaAuthorityChecksum Create(ReadOnlySpan<byte> bytes) => bytes.Length == 32 ? new(bytes.ToArray()) : throw new ArgumentException("A schema checksum must contain exactly 32 bytes.", nameof(bytes));
    public static BaseSchemaAuthorityChecksum ParseHex(string value) => Create(Convert.FromHexString(value));
    public bool IsValid => _bytes is { Length: 32 };
    public byte[] ToArray() => _bytes?.ToArray() ?? throw new InvalidOperationException(BaseSchemaErrorCodes.ContractInvalid);
    public void CopyTo(Span<byte> destination) { if (!IsValid || destination.Length < 32) throw new ArgumentException("The checksum destination is invalid.", nameof(destination)); _bytes!.CopyTo(destination); }
    public bool Equals(BaseSchemaAuthorityChecksum other) => IsValid && other.IsValid && CryptographicOperations.FixedTimeEquals(_bytes!, other._bytes!);
    public override bool Equals(object? obj) => obj is BaseSchemaAuthorityChecksum other && Equals(other);
    public override int GetHashCode() => !IsValid ? 0 : System.Buffers.Binary.BinaryPrimitives.ReadInt32BigEndian(_bytes!);
    public override string ToString() => IsValid ? Convert.ToHexStringLower(_bytes!) : throw new InvalidOperationException(BaseSchemaErrorCodes.ContractInvalid);
    public static bool operator ==(BaseSchemaAuthorityChecksum left, BaseSchemaAuthorityChecksum right) => left.Equals(right);
    public static bool operator !=(BaseSchemaAuthorityChecksum left, BaseSchemaAuthorityChecksum right) => !left.Equals(right);
}

/// <summary>Contains one immutable scalar-constraint checksum.</summary>
public readonly struct BaseScalarConstraintChecksum : IEquatable<BaseScalarConstraintChecksum>
{
    private readonly BaseSchemaAuthorityChecksum _value;
    private BaseScalarConstraintChecksum(BaseSchemaAuthorityChecksum value) => _value = value;
    public static BaseScalarConstraintChecksum Create(ReadOnlySpan<byte> bytes) => new(BaseSchemaAuthorityChecksum.Create(bytes));
    public bool IsValid => _value.IsValid;
    public byte[] ToArray() => _value.ToArray();
    public void CopyTo(Span<byte> destination) => _value.CopyTo(destination);
    public bool Equals(BaseScalarConstraintChecksum other) => _value.Equals(other._value);
    public override bool Equals(object? obj) => obj is BaseScalarConstraintChecksum other && Equals(other);
    public override int GetHashCode() => _value.GetHashCode();
    public override string ToString() => _value.ToString();
    public static bool operator ==(BaseScalarConstraintChecksum left, BaseScalarConstraintChecksum right) => left.Equals(right);
    public static bool operator !=(BaseScalarConstraintChecksum left, BaseScalarConstraintChecksum right) => !left.Equals(right);
}

/// <summary>Defines one graph-owned scalar codec authority.</summary>
public sealed record BaseScalarCodecAuthority
{
    public required BaseScalarCodecId Id { get; init; }
    public required long Version { get; init; }
    public required BaseScalarKind Kind { get; init; }
    public required ImmutableArray<BaseScalarConstraintKind> AllowedConstraints { get; init; }
    public required BaseSchemaAuthorityChecksum CodecChecksum { get; init; }
    public required long EqualityVersion { get; init; }
    public required BaseSchemaAuthorityChecksum EqualityChecksum { get; init; }
    public long? OrderingVersion { get; init; }
    public BaseSchemaAuthorityChecksum? OrderingChecksum { get; init; }
}
