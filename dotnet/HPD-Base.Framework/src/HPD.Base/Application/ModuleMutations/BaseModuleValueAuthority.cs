using System.Collections.Immutable;

namespace HPD.Base;

/// <summary>Identifies one closed scalar value admitted by a registered module mutation.</summary>
public enum BaseModuleValueKind
{
    /// <summary>Canonical UTF-8 string.</summary>
    String = 0,
    /// <summary>Canonical bounded binary value.</summary>
    Binary = 1,
    /// <summary>Signed 32-bit integer.</summary>
    Int32 = 2,
    /// <summary>Signed 64-bit integer.</summary>
    Int64 = 3,
    /// <summary>Unsigned 32-bit integer.</summary>
    UInt32 = 4,
    /// <summary>Unsigned 64-bit integer.</summary>
    UInt64 = 5,
    /// <summary>Exact decimal value.</summary>
    Decimal = 6,
    /// <summary>Boolean value.</summary>
    Boolean = 7,
    /// <summary>Canonical lowercase GUID value.</summary>
    Guid = 8,
    /// <summary>Canonical UTC instant.</summary>
    UtcDateTime = 9,
    /// <summary>Source-generated closed enum literal.</summary>
    ClosedEnum = 10,
    /// <summary>Bounded canonical JSON value.</summary>
    CanonicalJson = 11,
    /// <summary>Reserved frozen-array kind; not admitted by L66 module mutations.</summary>
    FrozenArray = 12,
    /// <summary>Typed Base record identity.</summary>
    RecordId = 13,
    /// <summary>Opaque module generation.</summary>
    ModuleGeneration = 14,
    /// <summary>L50-only optimistic revision token.</summary>
    Revision = 15,
    /// <summary>Generated restore-aware exported-subject reference.</summary>
    SubjectReference = 16,
    /// <summary>Opaque exported-subject lifetime incarnation.</summary>
    SubjectIncarnation = 17,
}

/// <summary>Contains one immutable graph-owned module value authority.</summary>
public sealed class BaseModuleValueType
{
    internal BaseModuleValueType(
        BaseModuleValueKind kind,
        BaseFieldPresence presence,
        BaseFieldNullability nullability,
        BaseScalarCodecAuthority? codec,
        BaseScalarConstraintSet? constraints,
        BaseScalarConstraintChecksum? constraintChecksum,
        string? recordTargetCollectionId,
        BaseGeneratedModuleSubjectQualifier? subjectQualifier = null)
    {
        Kind = kind;
        Presence = presence;
        Nullability = nullability;
        _codec = codec is null ? null : BaseModuleValueAuthorityContract.Clone(codec);
        _constraints = constraints is null ? null : BaseModuleValueAuthorityContract.Clone(constraints);
        ConstraintChecksum = constraintChecksum;
        RecordTargetCollectionId = recordTargetCollectionId is null ? null : new string(recordTargetCollectionId.AsSpan());
        SubjectQualifier = subjectQualifier?.Copy();
    }

    /// <summary>Gets the closed module value kind.</summary>
    public BaseModuleValueKind Kind { get; }
    /// <summary>Gets whether the value may be missing.</summary>
    public BaseFieldPresence Presence { get; }
    /// <summary>Gets whether an explicitly present value may be null.</summary>
    public BaseFieldNullability Nullability { get; }
    /// <summary>Gets a defensive copy of the installed scalar codec.</summary>
    public BaseScalarCodecAuthority? Codec => _codec is null ? null : BaseModuleValueAuthorityContract.Clone(_codec);
    /// <summary>Gets a defensive copy of the normalized scalar constraints.</summary>
    public BaseScalarConstraintSet? Constraints => _constraints is null ? null : BaseModuleValueAuthorityContract.Clone(_constraints);
    /// <summary>Gets the sealed scalar-constraint checksum.</summary>
    public BaseScalarConstraintChecksum? ConstraintChecksum { get; }
    /// <summary>Gets the exact target collection for a typed record ID.</summary>
    public string? RecordTargetCollectionId { get; }
    /// <summary>Gets generated subject authority when this is a subject-reference value.</summary>
    internal BaseGeneratedModuleSubjectQualifier? SubjectQualifier { get; }

    private readonly BaseScalarCodecAuthority? _codec;
    private readonly BaseScalarConstraintSet? _constraints;

    internal BaseScalarCodecAuthority? OwnedCodec => _codec;
    internal BaseScalarConstraintSet? OwnedConstraints => _constraints;
}

/// <summary>Identifies one immutable generated module-DTO scalar authority checksum.</summary>
public readonly struct BaseModuleDtoScalarAuthorityChecksum : IEquatable<BaseModuleDtoScalarAuthorityChecksum>
{
    private readonly BaseSchemaAuthorityChecksum _value;
    private BaseModuleDtoScalarAuthorityChecksum(BaseSchemaAuthorityChecksum value) => _value = value;
    internal static BaseModuleDtoScalarAuthorityChecksum Create(ReadOnlySpan<byte> bytes) => new(BaseSchemaAuthorityChecksum.Create(bytes));
    /// <summary>Gets whether this value contains one valid 32-byte checksum.</summary>
    public bool IsValid => _value.IsValid;
    /// <summary>Returns a defensive copy of the checksum bytes.</summary>
    public byte[] ToArray() => _value.ToArray();
    /// <inheritdoc />
    public bool Equals(BaseModuleDtoScalarAuthorityChecksum other) => _value.Equals(other._value);
    /// <inheritdoc />
    public override bool Equals(object? obj) => obj is BaseModuleDtoScalarAuthorityChecksum other && Equals(other);
    /// <inheritdoc />
    public override int GetHashCode() => _value.GetHashCode();
    /// <inheritdoc />
    public override string ToString() => _value.ToString();
    /// <summary>Compares two checksum values.</summary>
    public static bool operator ==(BaseModuleDtoScalarAuthorityChecksum left, BaseModuleDtoScalarAuthorityChecksum right) => left.Equals(right);
    /// <summary>Compares two checksum values.</summary>
    public static bool operator !=(BaseModuleDtoScalarAuthorityChecksum left, BaseModuleDtoScalarAuthorityChecksum right) => !left.Equals(right);
}

/// <summary>Contains one opaque generated DTO property authority.</summary>
public sealed class BaseModuleDtoScalarAuthority
{
    internal BaseModuleDtoScalarAuthority(
        IEnumerable<string> stablePropertyPath,
        BaseModuleValueType valueType,
        BaseModuleDtoScalarAuthorityChecksum authorityChecksum)
    {
        StablePropertyPath = [.. stablePropertyPath.Select(static edge => new string(edge.AsSpan()))];
        ValueType = BaseModuleValueAuthorityContract.Clone(valueType);
        AuthorityChecksum = authorityChecksum;
    }

    /// <summary>Gets the immutable stable property path.</summary>
    public ImmutableArray<string> StablePropertyPath { get; }
    /// <summary>Gets the immutable scalar value authority.</summary>
    public BaseModuleValueType ValueType { get; }
    /// <summary>Gets the canonical path-bound authority checksum.</summary>
    public BaseModuleDtoScalarAuthorityChecksum AuthorityChecksum { get; }
}

internal static class BaseModuleValueAuthorityContract
{
    internal static BaseModuleValueType CanonicalGuidGenerationKey()
    {
        BaseScalarCodecAuthority codec = BaseGeneratedSchemaRegistration.ScalarCodec(BaseScalarKind.String);
        var constraints = new BaseScalarConstraintSet
        {
            MinimumUtf8Bytes = 36,
            MaximumUtf8Bytes = 36,
            StringNormalization = BaseStringNormalizationRequirement.RequireNfc,
        };
        BaseScalarConstraintChecksum checksum = BaseGeneratedSchemaRegistration.ScalarConstraintChecksum(
            "hpd.base.module.proving", "generation-guid-key", BaseFieldPresence.Required,
            BaseFieldNullability.NonNullable, codec, constraints);
        return Create(BaseModuleValueKind.String, BaseFieldPresence.Required,
            BaseFieldNullability.NonNullable, codec, constraints, checksum);
    }

    internal static BaseModuleValueType RecordId<TRecord>()
    {
        BaseScalarCodecAuthority codec = BaseGeneratedSchemaRegistration.ScalarCodec(BaseScalarKind.RecordId);
        var constraints = new BaseScalarConstraintSet
        {
            MinimumUtf8Bytes = 1,
            MaximumUtf8Bytes = 256,
            StringNormalization = BaseStringNormalizationRequirement.RequireNfc,
        };
        BaseScalarConstraintChecksum checksum = BaseGeneratedSchemaRegistration.ScalarConstraintChecksum(
            "hpd.base.module.proving", "record-id", BaseFieldPresence.Required,
            BaseFieldNullability.NonNullable, codec, constraints);
        return Create(BaseModuleValueKind.RecordId, BaseFieldPresence.Required,
            BaseFieldNullability.NonNullable, codec, constraints, checksum,
            BaseGeneratedRecordTypeContract.GetCollectionId<TRecord>());
    }

    internal static BaseModuleValueType Primitive<TValue>(
        BaseFieldPresence presence = BaseFieldPresence.Required,
        BaseFieldNullability? nullability = null)
    {
        Type declared = typeof(TValue);
        Type actual = Nullable.GetUnderlyingType(declared) ?? declared;
        BaseFieldNullability resolvedNullability = nullability
            ?? (Nullable.GetUnderlyingType(declared) is not null ? BaseFieldNullability.Nullable : BaseFieldNullability.NonNullable);
        BaseModuleValueKind kind = actual == typeof(string) ? BaseModuleValueKind.String
            : actual == typeof(bool) ? BaseModuleValueKind.Boolean
            : actual == typeof(int) ? BaseModuleValueKind.Int32
            : actual == typeof(long) ? BaseModuleValueKind.Int64
            : actual == typeof(uint) ? BaseModuleValueKind.UInt32
            : actual == typeof(ulong) ? BaseModuleValueKind.UInt64
            : actual == typeof(decimal) ? BaseModuleValueKind.Decimal
            : actual == typeof(Guid) ? BaseModuleValueKind.Guid
            : actual == typeof(DateTimeOffset) ? BaseModuleValueKind.UtcDateTime
            : actual == typeof(BaseBinary) ? BaseModuleValueKind.Binary
            : actual == typeof(BaseCanonicalJson) ? BaseModuleValueKind.CanonicalJson
            : actual == typeof(BaseModuleGeneration) ? BaseModuleValueKind.ModuleGeneration
            : actual == typeof(RevisionToken) ? BaseModuleValueKind.Revision
            : throw new InvalidOperationException("base.moduleMutation.invalid");
        if (kind == BaseModuleValueKind.Revision)
            return Create(kind, presence, resolvedNullability, null, null, null);
        BaseScalarKind scalarKind = (BaseScalarKind)(int)kind;
        BaseScalarCodecAuthority codec = BaseGeneratedSchemaRegistration.ScalarCodec(scalarKind);
        var constraints = kind == BaseModuleValueKind.ModuleGeneration
            ? new BaseScalarConstraintSet
            {
                MinimumUtf8Bytes = 1,
                MaximumUtf8Bytes = 19,
            }
            : new BaseScalarConstraintSet();
        BaseScalarConstraintChecksum checksum = BaseGeneratedSchemaRegistration.ScalarConstraintChecksum(
            "hpd.base.module.proving", "value", presence, resolvedNullability, codec, constraints);
        return Create(kind, presence, resolvedNullability, codec, constraints, checksum);
    }

    internal static bool StructurallyEquals(BaseModuleValueType? left, BaseModuleValueType? right)
    {
        if (left is null || right is null) return left is null && right is null;
        var leftWriter = new System.Buffers.ArrayBufferWriter<byte>();
        var rightWriter = new System.Buffers.ArrayBufferWriter<byte>();
        BaseSchemaContract.WriteModuleValueType(leftWriter, left);
        BaseSchemaContract.WriteModuleValueType(rightWriter, right);
        return leftWriter.WrittenSpan.SequenceEqual(rightWriter.WrittenSpan);
    }

    internal static bool SameUnderlyingAuthority(BaseModuleValueType? left, BaseModuleValueType? right)
    {
        if (left is null || right is null) return left is null && right is null;
        if (left.Kind != right.Kind || left.Nullability != right.Nullability
            || !string.Equals(left.RecordTargetCollectionId, right.RecordTargetCollectionId, StringComparison.Ordinal))
            return false;
        if (left.SubjectQualifier is not null || right.SubjectQualifier is not null)
        {
            if (left.SubjectQualifier is null || right.SubjectQualifier is null) return false;
            var normalizedLeft = new BaseModuleValueType(left.Kind, BaseFieldPresence.Required,
                left.Nullability, null, null, null, left.RecordTargetCollectionId, left.SubjectQualifier);
            var normalizedRight = new BaseModuleValueType(right.Kind, BaseFieldPresence.Required,
                right.Nullability, null, null, null, right.RecordTargetCollectionId, right.SubjectQualifier);
            return StructurallyEquals(normalizedLeft, normalizedRight);
        }
        if (left.Codec is null || right.Codec is null || left.Constraints is null || right.Constraints is null)
            return left.Codec is null && right.Codec is null
                && left.Constraints is null && right.Constraints is null;
        return BaseSchemaContract.ScalarAuthorityCompatible(
                left.Codec, left.Constraints, right.Codec, right.Constraints)
            && BaseSchemaContract.ScalarAuthorityCompatible(
                right.Codec, right.Constraints, left.Codec, left.Constraints);
    }

    internal static bool ValueCompatible(BaseModuleValueType? source, BaseModuleValueType? destination)
    {
        if (source is null || destination is null) return source is null && destination is null;
        if (source.Kind != destination.Kind || source.Presence != destination.Presence
            || source.Nullability != destination.Nullability
            || source.RecordTargetCollectionId != destination.RecordTargetCollectionId)
            return false;
        if (source.Kind is BaseModuleValueKind.Revision or BaseModuleValueKind.SubjectReference
            or BaseModuleValueKind.SubjectIncarnation)
            return StructurallyEquals(source, destination);
        if (source.Codec is null || destination.Codec is null || source.Constraints is null || destination.Constraints is null)
            return false;
        return BaseSchemaContract.ScalarAuthorityCompatible(
            source.Codec, source.Constraints, destination.Codec, destination.Constraints);
    }

    internal static BaseModuleValueType FromField(FieldDefinition field)
    {
        if (field.SubjectReference is { } subject)
        {
            var qualifier = new BaseGeneratedModuleSubjectQualifier(
                subject.ContractId, subject.ContractVersion, subject.ContractChecksum,
                subject.SubjectIdKind, subject.MaximumSubjectIdUtf8Bytes,
                subject.Requirement, subject.Guarantee);
            return Create(BaseModuleValueKind.SubjectReference, field.Presence, field.Nullability,
                null, null, null, subjectQualifier: qualifier);
        }
        if (field.ScalarKind is not { } scalarKind || scalarKind == BaseScalarKind.FrozenArray
            || field.ScalarCodec is null || field.ScalarConstraints is null || field.ScalarConstraintChecksum is null)
            throw new InvalidOperationException("base.moduleMutation.invalid");
        return Create(
            (BaseModuleValueKind)(int)scalarKind,
            field.Presence,
            field.Nullability,
            field.ScalarCodec,
            field.ScalarConstraints,
            field.ScalarConstraintChecksum,
            scalarKind == BaseScalarKind.RecordId ? field.RecordTargetCollectionId ?? field.Relation?.TargetCollectionId : null);
    }

    internal static BaseModuleValueType Create(
        BaseModuleValueKind kind,
        BaseFieldPresence presence,
        BaseFieldNullability nullability,
        BaseScalarCodecAuthority? codec,
        BaseScalarConstraintSet? constraints,
        BaseScalarConstraintChecksum? constraintChecksum,
        string? recordTargetCollectionId = null,
        BaseGeneratedModuleSubjectQualifier? subjectQualifier = null)
    {
        var value = new BaseModuleValueType(kind, presence, nullability, codec, constraints, constraintChecksum, recordTargetCollectionId, subjectQualifier);
        var writer = new System.Buffers.ArrayBufferWriter<byte>();
        BaseSchemaContract.WriteModuleValueType(writer, value);
        return value;
    }

    internal static BaseModuleDtoScalarAuthority CreateDto(
        IEnumerable<string> stablePropertyPath,
        BaseModuleValueType valueType)
    {
        string[] path = stablePropertyPath.Select(static edge => new string(edge.AsSpan())).ToArray();
        BaseModuleDtoScalarAuthorityChecksum checksum = BaseSchemaContract.SealModuleDtoScalarAuthority(path, valueType);
        return new BaseModuleDtoScalarAuthority(path, valueType, checksum);
    }

    internal static BaseModuleValueType Clone(BaseModuleValueType value) => new(
        value.Kind, value.Presence, value.Nullability, value.OwnedCodec, value.OwnedConstraints,
        value.ConstraintChecksum, value.RecordTargetCollectionId, value.SubjectQualifier);

    internal static BaseScalarCodecAuthority Clone(BaseScalarCodecAuthority value) => value with
    {
        AllowedConstraints = [.. value.AllowedConstraints],
    };

    internal static BaseScalarConstraintSet Clone(BaseScalarConstraintSet value) => value with
    {
        AllowedEnumLiterals = [.. value.AllowedEnumLiterals.Select(static literal => new string(literal.AsSpan()))],
    };
}
