using System.Buffers;
using System.Buffers.Binary;
using System.Collections.Immutable;
using System.Security.Cryptography;

namespace HPD.Base;

internal static class BaseSchemaContract
{
    internal static bool ScalarAuthorityCompatible(
        BaseScalarCodecAuthority leftCodec, BaseScalarConstraintSet leftConstraints,
        BaseScalarCodecAuthority rightCodec, BaseScalarConstraintSet rightConstraints)
    {
        var left = new ArrayBufferWriter<byte>();
        var right = new ArrayBufferWriter<byte>();
        WriteCodec(left, leftCodec); WriteConstraints(left, leftConstraints);
        WriteCodec(right, rightCodec); WriteConstraints(right, rightConstraints);
        return left.WrittenSpan.SequenceEqual(right.WrittenSpan);
    }
    private static readonly byte[] ConstraintPurpose = "hpd.base.scalar-constraint.v1\0"u8.ToArray();
    private static readonly byte[] PredicatePurpose = "hpd.base.index-predicate.v1\0"u8.ToArray();
    private static readonly byte[] IndexPurpose = "hpd.base.logical-index.v1\0"u8.ToArray();

    internal static BaseScalarCodecAuthority Codec(BaseScalarKind kind, string? qualifier = null)
    {
        if (!Enum.IsDefined(kind)) throw Invalid();
        string id = qualifier is null ? $"hpd.base.scalar.{Wire(kind)}.v1" : $"hpd.base.scalar.{Wire(kind)}.{qualifier}.v1";
        return Codec(kind, id, true);
    }

    private static BaseScalarCodecAuthority Codec(BaseScalarKind kind, string id, bool _)
    {
        ImmutableArray<BaseScalarConstraintKind> constraints = kind switch
        {
            BaseScalarKind.String => [BaseScalarConstraintKind.Utf8Bytes, BaseScalarConstraintKind.StringNormalization],
            BaseScalarKind.RecordId => [BaseScalarConstraintKind.Utf8Bytes, BaseScalarConstraintKind.StringNormalization],
            BaseScalarKind.ModuleGeneration => [BaseScalarConstraintKind.Utf8Bytes],
            BaseScalarKind.Binary => [BaseScalarConstraintKind.BinaryBytes],
            BaseScalarKind.Int32 => [BaseScalarConstraintKind.Int32Range],
            BaseScalarKind.Int64 => [BaseScalarConstraintKind.Int64Range],
            BaseScalarKind.UInt32 => [BaseScalarConstraintKind.UInt32Range],
            BaseScalarKind.UInt64 => [BaseScalarConstraintKind.UInt64Range],
            BaseScalarKind.Decimal => [BaseScalarConstraintKind.DecimalRange],
            BaseScalarKind.ClosedEnum => [BaseScalarConstraintKind.EnumLiterals],
            BaseScalarKind.CanonicalJson => [BaseScalarConstraintKind.CanonicalJson],
            BaseScalarKind.FrozenArray => [BaseScalarConstraintKind.CollectionItems],
            _ => [],
        };
        bool orderable = kind is not BaseScalarKind.CanonicalJson and not BaseScalarKind.FrozenArray and not BaseScalarKind.ModuleGeneration;
        byte[] equality = AuthorityChecksum("hpd.base.scalar-equality.v1\0"u8, id, 1, kind);
        byte[]? ordering = orderable ? AuthorityChecksum("hpd.base.scalar-ordering.v1\0"u8, id, 1, kind) : null;
        var authority = new ArrayBufferWriter<byte>(); authority.Write("hpd.base.scalar-codec.v1\0"u8);
        Write(authority, id); WriteUInt64(authority, 1); WriteUInt16(authority, (ushort)kind); WriteUInt32(authority, checked((uint)constraints.Length));
        foreach (BaseScalarConstraintKind constraint in constraints) WriteUInt16(authority, (ushort)constraint);
        WriteUInt64(authority, 1); authority.Write(equality); Write(authority, orderable); if (orderable) { WriteUInt64(authority, 1); authority.Write(ordering!); }
        byte[] codec = SHA256.HashData(authority.WrittenSpan);
        return new BaseScalarCodecAuthority
        {
            Id = BaseScalarCodecId.Create(id), Version = 1, Kind = kind, AllowedConstraints = constraints,
            CodecChecksum = BaseSchemaAuthorityChecksum.Create(codec), EqualityVersion = 1,
            EqualityChecksum = BaseSchemaAuthorityChecksum.Create(equality), OrderingVersion = orderable ? 1 : null,
            OrderingChecksum = orderable ? BaseSchemaAuthorityChecksum.Create(ordering!) : null,
        };
    }

    internal static BaseScalarConstraintChecksum SealConstraints(
        string collectionId, string fieldId, BaseFieldPresence presence, BaseFieldNullability nullability,
        BaseScalarCodecAuthority codec, BaseScalarConstraintSet constraints)
    {
        ValidateConstraints(codec, constraints);
        var writer = new ArrayBufferWriter<byte>(); writer.Write(ConstraintPurpose);
        if (!Enum.IsDefined(presence) || !Enum.IsDefined(nullability)) throw Invalid();
        Write(writer, collectionId); Write(writer, fieldId); WriteUInt16(writer, (ushort)presence); WriteUInt16(writer, (ushort)nullability);
        WriteCodec(writer, codec); WriteConstraints(writer, constraints);
        return BaseScalarConstraintChecksum.Create(SHA256.HashData(writer.WrittenSpan));
    }

    internal static BaseModuleDtoScalarAuthorityChecksum SealModuleDtoScalarAuthority(
        IReadOnlyList<string> stablePropertyPath,
        BaseModuleValueType valueType)
    {
        ArgumentNullException.ThrowIfNull(stablePropertyPath);
        ArgumentNullException.ThrowIfNull(valueType);
        if (stablePropertyPath.Count is < 1 or > 16 || stablePropertyPath.Any(static edge => string.IsNullOrWhiteSpace(edge)))
            throw Invalid();
        var writer = new ArrayBufferWriter<byte>();
        writer.Write("hpd.base.module.dto-scalar-authority.v1\0"u8);
        WriteUInt32(writer, checked((uint)stablePropertyPath.Count));
        foreach (string edge in stablePropertyPath) { BaseApplicationId.Validate(edge, nameof(stablePropertyPath)); Write(writer, edge); }
        WriteModuleValueType(writer, valueType);
        return BaseModuleDtoScalarAuthorityChecksum.Create(SHA256.HashData(writer.WrittenSpan));
    }

    internal static void WriteModuleValueType(ArrayBufferWriter<byte> writer, BaseModuleValueType value)
    {
        ArgumentNullException.ThrowIfNull(value);
        if (!Enum.IsDefined(value.Kind) || !Enum.IsDefined(value.Presence) || !Enum.IsDefined(value.Nullability)
            || value.Kind == BaseModuleValueKind.FrozenArray)
            throw Invalid();
        WriteUInt64(writer, checked((ulong)value.Kind));
        WriteUInt64(writer, checked((ulong)value.Presence));
        WriteUInt64(writer, checked((ulong)value.Nullability));
        if (value.Kind == BaseModuleValueKind.Revision)
        {
            if (value.Presence != BaseFieldPresence.Required || value.Nullability != BaseFieldNullability.NonNullable
                || value.OwnedCodec is not null || value.OwnedConstraints is not null
                || value.ConstraintChecksum is not null || value.RecordTargetCollectionId is not null)
                throw Invalid();
            Write(writer, false); Write(writer, false); Write(writer, false); Write(writer, false);
            return;
        }

        BaseScalarKind scalarKind = (BaseScalarKind)(int)value.Kind;
        BaseScalarCodecAuthority codec = value.OwnedCodec ?? throw Invalid();
        BaseScalarConstraintSet constraints = value.OwnedConstraints ?? throw Invalid();
        BaseScalarConstraintChecksum checksum = value.ConstraintChecksum ?? throw Invalid();
        if (codec.Kind != scalarKind || !ValidCodec(codec)) throw Invalid();
        ValidateConstraints(codec, constraints);
        if (!checksum.IsValid) throw Invalid();
        bool recordId = value.Kind == BaseModuleValueKind.RecordId;
        if (recordId != (value.RecordTargetCollectionId is not null)) throw Invalid();
        if (value.RecordTargetCollectionId is { } target) BaseApplicationId.Validate(target, nameof(value));
        Write(writer, true); WriteCodec(writer, codec);
        Write(writer, true); WriteConstraints(writer, constraints);
        Write(writer, true); writer.Write(checksum.ToArray());
        Write(writer, recordId); if (recordId) Write(writer, value.RecordTargetCollectionId!);
    }

    internal static string EnumQualifier(IEnumerable<string> literals)
    {
        var writer = new ArrayBufferWriter<byte>(); writer.Write("hpd.base.enum-codec.v1\0"u8);
        foreach (string literal in literals.Order(StringComparer.Ordinal)) Write(writer, literal);
        return "enum-" + Convert.ToHexStringLower(SHA256.HashData(writer.WrittenSpan))[..32];
    }

    internal static BaseIndexPredicateRegistry SealPredicate(BaseIndexPredicateId root, IEnumerable<BaseIndexPredicateNode> values)
    {
        BaseIndexPredicateNode[] nodes = values.OrderBy(static value => value.Id).Select(Clone).ToArray();
        ValidatePredicate(root, nodes);
        var writer = new ArrayBufferWriter<byte>(); writer.Write(PredicatePurpose); Write(writer, root.ToString()); WriteUInt32(writer, checked((uint)nodes.Length));
        foreach (BaseIndexPredicateNode node in nodes) WritePredicateNode(writer, node);
        return new BaseIndexPredicateRegistry { Root = root, Nodes = [.. nodes], Checksum = BaseSchemaAuthorityChecksum.Create(SHA256.HashData(writer.WrittenSpan)) };
    }

    internal static BaseLogicalIndexDefinition SealIndex(BaseLogicalIndexDefinition value, IReadOnlyList<FieldDefinition> fields)
    {
        ArgumentNullException.ThrowIfNull(fields);
        if (!value.Id.IsValid || value.Version < 1 || string.IsNullOrWhiteSpace(value.CollectionId) || value.Parts.IsDefaultOrEmpty)
            throw Invalid();
        if (value.Parts.Any(static part => part.FieldOrdinal < 0 || !Enum.IsDefined(part.Direction) || !Enum.IsDefined(part.Collation) || !Enum.IsDefined(part.NullOrder))
            || value.Parts.Select(static part => part.FieldOrdinal).Distinct().Count() != value.Parts.Length)
            throw Invalid();
        BaseIndexPredicateRegistry predicate = SealPredicate(value.MembershipPredicate.Root, value.MembershipPredicate.Nodes);
        ValidateIndexFields(value, predicate, fields);
        var writer = new ArrayBufferWriter<byte>(); writer.Write(IndexPurpose); Write(writer, value.Id.ToString()); WriteUInt64(writer, checked((ulong)value.Version));
        Write(writer, value.CollectionId); Write(writer, value.Unique); Write(writer, value.StoreRequired); WriteUInt32(writer, checked((uint)value.Parts.Length));
        foreach (BaseLogicalIndexPart part in value.Parts)
        {
            WriteUInt64(writer, checked((ulong)part.FieldOrdinal)); WriteUInt16(writer, (ushort)part.Direction); WriteUInt16(writer, (ushort)part.Collation); WriteUInt16(writer, (ushort)part.NullOrder);
            WriteCodec(writer, fields[part.FieldOrdinal].ScalarCodec ?? throw Invalid());
        }
        writer.Write(predicate.Checksum.ToArray());
        return value with { Parts = [.. value.Parts.Select(Clone)], MembershipPredicate = predicate, Checksum = BaseLogicalIndexChecksum.Create(SHA256.HashData(writer.WrittenSpan)) };
    }

    internal static BaseLogicalIndexDefinition Clone(BaseLogicalIndexDefinition value) => value with
    {
        Parts = [.. value.Parts.Select(Clone)],
        MembershipPredicate = value.MembershipPredicate with { Nodes = [.. value.MembershipPredicate.Nodes.Select(Clone)] },
    };

    private static BaseLogicalIndexPart Clone(BaseLogicalIndexPart value) => value with { };
    private static BaseIndexPredicateNode Clone(BaseIndexPredicateNode value) => value with
    {
        Children = [.. value.Children], Literal = value.Literal is null ? null : value.Literal with { CanonicalBytes = [.. value.Literal.CanonicalBytes] },
    };

    private static void ValidateConstraints(BaseScalarCodecAuthority codec, BaseScalarConstraintSet value)
    {
        if (!codec.Id.IsValid || codec.Version < 1 || !codec.CodecChecksum.IsValid || !codec.EqualityChecksum.IsValid || !Enum.IsDefined(codec.Kind) || codec.AllowedConstraints.IsDefault || codec.AllowedConstraints.Any(static value => !Enum.IsDefined(value))) throw Invalid();
        BaseScalarCodecAuthority expected = Codec(codec.Kind, codec.Id.ToString(), true);
        if (codec.Id != expected.Id || codec.Version != expected.Version || codec.Kind != expected.Kind || !codec.AllowedConstraints.SequenceEqual(expected.AllowedConstraints) || codec.CodecChecksum != expected.CodecChecksum || codec.EqualityVersion != expected.EqualityVersion || codec.EqualityChecksum != expected.EqualityChecksum || codec.OrderingVersion != expected.OrderingVersion || codec.OrderingChecksum != expected.OrderingChecksum) throw Invalid();
        NonNegative(value.MinimumUtf8Bytes); NonNegative(value.MaximumUtf8Bytes); Range(value.MinimumUtf8Bytes, value.MaximumUtf8Bytes);
        Range(value.MinimumInt32, value.MaximumInt32); Range(value.MinimumInt64, value.MaximumInt64);
        Range(value.MinimumUInt32, value.MaximumUInt32); Range(value.MinimumUInt64, value.MaximumUInt64);
        if (value.MinimumDecimal is { } minDecimal && value.MaximumDecimal is { } maxDecimal && Compare(minDecimal, maxDecimal) > 0) throw Invalid();
        NonNegative(value.MaximumBinaryBytes); NonNegative(value.MaximumCanonicalJsonBytes); NonNegative(value.MaximumJsonDepth);
        NonNegative(value.MaximumJsonArrayItems); NonNegative(value.MaximumJsonObjectProperties); NonNegative(value.MaximumJsonTotalNodes);
        NonNegative(value.MaximumJsonTotalStringUtf8Bytes); NonNegative(value.MaximumJsonTotalNameUtf8Bytes);
        NonNegative(value.MinimumCollectionItems); NonNegative(value.MaximumCollectionItems); Range(value.MinimumCollectionItems, value.MaximumCollectionItems);
        if (value.StringNormalization is { } normalization && !Enum.IsDefined(normalization) || value.JsonShape is { } shape && !Enum.IsDefined(shape)) throw Invalid();
        if (!value.AllowedEnumLiterals.IsDefaultOrEmpty && (!value.AllowedEnumLiterals.SequenceEqual(value.AllowedEnumLiterals.Order(StringComparer.Ordinal)) || value.AllowedEnumLiterals.Distinct(StringComparer.Ordinal).Count() != value.AllowedEnumLiterals.Length)) throw Invalid();
        HashSet<BaseScalarConstraintKind> used = Used(value);
        if (used.Any(kind => !codec.AllowedConstraints.Contains(kind))) throw Invalid();
        if (codec.Kind == BaseScalarKind.FrozenArray && value.MaximumCollectionItems is not > 0) throw Invalid();
        if (codec.Kind == BaseScalarKind.CanonicalJson &&
            (value.MaximumCanonicalJsonBytes is not > 0 || value.MaximumJsonDepth is not > 0 ||
             value.MaximumJsonArrayItems is not > 0 || value.MaximumJsonObjectProperties is not > 0 ||
             value.MaximumJsonTotalNodes is not > 0 || value.MaximumJsonTotalStringUtf8Bytes is not > 0 ||
             value.MaximumJsonTotalNameUtf8Bytes is not > 0)) throw Invalid();
        if (codec.Kind == BaseScalarKind.ClosedEnum && value.AllowedEnumLiterals.IsDefaultOrEmpty) throw Invalid();
        if (codec.Kind == BaseScalarKind.RecordId &&
            (value.MinimumUtf8Bytes != 1 || value.MaximumUtf8Bytes != 256 ||
             value.StringNormalization != BaseStringNormalizationRequirement.RequireNfc)) throw Invalid();
        if (codec.Kind == BaseScalarKind.ModuleGeneration &&
            (value.MinimumUtf8Bytes != 1 || value.MaximumUtf8Bytes != 19 || value.StringNormalization is not null)) throw Invalid();
    }

    private static HashSet<BaseScalarConstraintKind> Used(BaseScalarConstraintSet value)
    {
        var result = new HashSet<BaseScalarConstraintKind>();
        if (value.MinimumUtf8Bytes is not null || value.MaximumUtf8Bytes is not null) result.Add(BaseScalarConstraintKind.Utf8Bytes);
        if (value.StringNormalization is not null) result.Add(BaseScalarConstraintKind.StringNormalization);
        if (value.MinimumInt32 is not null || value.MaximumInt32 is not null) result.Add(BaseScalarConstraintKind.Int32Range);
        if (value.MinimumInt64 is not null || value.MaximumInt64 is not null) result.Add(BaseScalarConstraintKind.Int64Range);
        if (value.MinimumUInt32 is not null || value.MaximumUInt32 is not null) result.Add(BaseScalarConstraintKind.UInt32Range);
        if (value.MinimumUInt64 is not null || value.MaximumUInt64 is not null) result.Add(BaseScalarConstraintKind.UInt64Range);
        if (value.MinimumDecimal is not null || value.MaximumDecimal is not null) result.Add(BaseScalarConstraintKind.DecimalRange);
        if (!value.AllowedEnumLiterals.IsDefaultOrEmpty) result.Add(BaseScalarConstraintKind.EnumLiterals);
        if (value.MaximumBinaryBytes is not null) result.Add(BaseScalarConstraintKind.BinaryBytes);
        if (value.MaximumCanonicalJsonBytes is not null || value.JsonShape is not null || value.MaximumJsonDepth is not null || value.MaximumJsonArrayItems is not null || value.MaximumJsonObjectProperties is not null || value.MaximumJsonTotalNodes is not null || value.MaximumJsonTotalStringUtf8Bytes is not null || value.MaximumJsonTotalNameUtf8Bytes is not null) result.Add(BaseScalarConstraintKind.CanonicalJson);
        if (value.MinimumCollectionItems is not null || value.MaximumCollectionItems is not null) result.Add(BaseScalarConstraintKind.CollectionItems);
        return result;
    }

    private static void ValidatePredicate(BaseIndexPredicateId root, BaseIndexPredicateNode[] nodes)
    {
        if (!root.IsValid || nodes.Length == 0 || nodes.Select(static node => node.Id).Distinct().Count() != nodes.Length) throw Invalid();
        Dictionary<BaseIndexPredicateId, BaseIndexPredicateNode> byId = nodes.ToDictionary(static node => node.Id);
        if (!byId.ContainsKey(root)) throw Invalid();
        var parents = nodes.ToDictionary(static node => node.Id, static _ => 0);
        foreach (BaseIndexPredicateNode node in nodes)
        {
            bool field = node.FieldOrdinal is >= 0; bool literal = node.Literal is not null; int children = node.Children.Length;
            bool valid = node.Kind switch
            {
                BaseIndexPredicateNodeKind.True or BaseIndexPredicateNodeKind.False => !field && !literal && children == 0,
                BaseIndexPredicateNodeKind.IsDefined or BaseIndexPredicateNodeKind.IsMissing or BaseIndexPredicateNodeKind.IsNull or BaseIndexPredicateNodeKind.IsNotNull => field && !literal && children == 0,
                BaseIndexPredicateNodeKind.Equal => field && literal && children == 0 && !node.Literal!.CanonicalBytes.IsDefault,
                BaseIndexPredicateNodeKind.And or BaseIndexPredicateNodeKind.Or => !field && !literal && children >= 2,
                BaseIndexPredicateNodeKind.Not => !field && !literal && children == 1,
                _ => false,
            };
            if (!valid || !node.Children.SequenceEqual(node.Children.Order())) throw Invalid();
            foreach (BaseIndexPredicateId child in node.Children) { if (!byId.ContainsKey(child)) throw Invalid(); parents[child]++; }
        }
        if (parents[root] != 0 || parents.Where(pair => pair.Key != root).Any(static pair => pair.Value != 1)) throw Invalid();
        var reached = new HashSet<BaseIndexPredicateId>(); Visit(root, byId, reached, new HashSet<BaseIndexPredicateId>());
        if (reached.Count != nodes.Length) throw Invalid();
    }

    private static void ValidateIndexFields(BaseLogicalIndexDefinition index, BaseIndexPredicateRegistry predicate, IReadOnlyList<FieldDefinition> fields)
    {
        foreach (BaseLogicalIndexPart part in index.Parts)
        {
            if (part.FieldOrdinal < 0 || part.FieldOrdinal >= fields.Count || fields[part.FieldOrdinal].ScalarCodec is not { } codec || !ValidCodec(codec)) throw Invalid();
            if (codec.Kind == BaseScalarKind.FrozenArray) throw Invalid();
            if (codec.OrderingVersion is null || codec.OrderingChecksum is null)
            {
                if (!index.Unique) throw Invalid();
            }
        }
        foreach (BaseIndexPredicateNode node in predicate.Nodes)
        {
            if (node.FieldOrdinal is not { } ordinal) continue;
            if (ordinal < 0 || ordinal >= fields.Count || fields[ordinal].ScalarCodec is not { } fieldCodec || !ValidCodec(fieldCodec)) throw Invalid();
            if (node.Literal is not { } literal) continue;
            if (literal.Kind != fieldCodec.Kind || literal.Codec.Kind != literal.Kind || !SameCodec(literal.Codec, fieldCodec) || !ValidateLiteral(literal.Kind, literal.CanonicalBytes.AsSpan())) throw Invalid();
        }
    }

    internal static byte[] EncodeLiteral(BaseScalarKind kind, object value)
    {
        ArgumentNullException.ThrowIfNull(value);
        byte[] result = (kind, value) switch
        {
            (BaseScalarKind.String, string item) => StrictUtf8(item),
            (BaseScalarKind.RecordId, RecordId item) when item.IsValid => StrictUtf8(item.Value),
            (BaseScalarKind.ModuleGeneration, BaseModuleGeneration item) => StrictUtf8(item.ToCanonicalString()),
            (BaseScalarKind.Binary, BaseBinary item) => item.ToArray(),
            (BaseScalarKind.Int32, int item) => Signed32(item),
            (BaseScalarKind.Int64, long item) => Signed64(item),
            (BaseScalarKind.UInt32, uint item) => Unsigned32(item),
            (BaseScalarKind.UInt64, ulong item) => Unsigned64(item),
            (BaseScalarKind.Boolean, bool item) => [item ? (byte)1 : (byte)0],
            (BaseScalarKind.Guid, Guid item) => GuidBytes(item),
            (BaseScalarKind.UtcDateTime, DateTimeOffset item) when item.Offset == TimeSpan.Zero => Signed64(item.UtcTicks),
            (BaseScalarKind.Decimal, decimal item) => DecimalBytes(item),
            (BaseScalarKind.ClosedEnum, Enum item) => StrictUtf8(item.ToString()),
            _ => throw Invalid(),
        };
        return ValidateLiteral(kind, result) ? result : throw Invalid();
    }

    private static bool ValidateLiteral(BaseScalarKind kind, ReadOnlySpan<byte> bytes)
    {
        try
        {
            return kind switch
            {
                BaseScalarKind.String => ValidUtf8(bytes),
                BaseScalarKind.RecordId => ValidRecordId(bytes),
                BaseScalarKind.ModuleGeneration => ValidModuleGeneration(bytes),
                BaseScalarKind.Binary => true,
                BaseScalarKind.Int32 or BaseScalarKind.UInt32 => bytes.Length == 4,
                BaseScalarKind.Int64 or BaseScalarKind.UInt64 => bytes.Length == 8,
                BaseScalarKind.ClosedEnum => ValidUtf8(bytes) && bytes.Length > 0,
                BaseScalarKind.Boolean => bytes.Length == 1 && bytes[0] <= 1,
                BaseScalarKind.Guid => bytes.Length == 16,
                BaseScalarKind.UtcDateTime => bytes.Length == 8,
                BaseScalarKind.Decimal => bytes.Length == 17 && bytes[16] <= 28 && new BaseDecimalValue(BinaryPrimitives.ReadInt128BigEndian(bytes[..16]), bytes[16]) is var value && value.Scale == bytes[16],
                _ => false,
            };
        }
        catch { return false; }
    }

    private static byte[] GuidBytes(Guid value) { byte[] bytes = new byte[16]; value.TryWriteBytes(bytes, bigEndian: true, out _); return bytes; }

    private static bool ValidRecordId(ReadOnlySpan<byte> bytes)
    {
        try
        {
            string value = new System.Text.UTF8Encoding(false, true).GetString(bytes);
            return RecordId.TryParse(value, out _)
                && bytes.SequenceEqual(System.Text.Encoding.UTF8.GetBytes(value));
        }
        catch { return false; }
    }

    private static bool ValidModuleGeneration(ReadOnlySpan<byte> bytes)
    {
        try
        {
            string value = new System.Text.UTF8Encoding(false, true).GetString(bytes);
            _ = BaseModuleGeneration.ParseCanonical(value);
            return bytes.SequenceEqual(System.Text.Encoding.UTF8.GetBytes(value));
        }
        catch { return false; }
    }

    private static bool ValidCodec(BaseScalarCodecAuthority codec)
    {
        if (!codec.Id.IsValid || !codec.Id.ToString().StartsWith($"hpd.base.scalar.{Wire(codec.Kind)}.", StringComparison.Ordinal) || !codec.Id.ToString().EndsWith(".v1", StringComparison.Ordinal)) return false;
        BaseScalarCodecAuthority expected = Codec(codec.Kind, codec.Id.ToString(), true); return SameCodec(codec, expected);
    }
    private static bool SameCodec(BaseScalarCodecAuthority left, BaseScalarCodecAuthority right) => left.Id == right.Id && left.Version == right.Version && left.Kind == right.Kind && left.AllowedConstraints.SequenceEqual(right.AllowedConstraints) && left.CodecChecksum == right.CodecChecksum && left.EqualityVersion == right.EqualityVersion && left.EqualityChecksum == right.EqualityChecksum && left.OrderingVersion == right.OrderingVersion && left.OrderingChecksum == right.OrderingChecksum;
    private static byte[] StrictUtf8(string value) => BaseStrictUtf8.Encode(value);
    private static bool ValidUtf8(ReadOnlySpan<byte> bytes)
    {
        for (int index = 0; index < bytes.Length;)
        {
            byte first = bytes[index++]; if (first <= 0x7f) continue;
            int remaining; int scalar;
            if (first is >= 0xc2 and <= 0xdf) { remaining = 1; scalar = first & 0x1f; }
            else if (first is >= 0xe0 and <= 0xef) { remaining = 2; scalar = first & 0x0f; }
            else if (first is >= 0xf0 and <= 0xf4) { remaining = 3; scalar = first & 0x07; }
            else return false;
            if (index + remaining > bytes.Length) return false;
            for (int part = 0; part < remaining; part++) { byte next = bytes[index++]; if ((next & 0xc0) != 0x80) return false; scalar = scalar << 6 | next & 0x3f; }
            if (remaining == 2 && (scalar < 0x800 || scalar is >= 0xd800 and <= 0xdfff) || remaining == 3 && (scalar < 0x10000 || scalar > 0x10ffff)) return false;
        }
        return true;
    }
    private static byte[] Signed32(int value) { byte[] bytes = new byte[4]; BinaryPrimitives.WriteInt32BigEndian(bytes, value); return bytes; }
    private static byte[] Signed64(long value) { byte[] bytes = new byte[8]; BinaryPrimitives.WriteInt64BigEndian(bytes, value); return bytes; }
    private static byte[] Unsigned32(uint value) { byte[] bytes = new byte[4]; BinaryPrimitives.WriteUInt32BigEndian(bytes, value); return bytes; }
    private static byte[] Unsigned64(ulong value) { byte[] bytes = new byte[8]; BinaryPrimitives.WriteUInt64BigEndian(bytes, value); return bytes; }
    private static byte[] DecimalBytes(decimal value) { int[] bits = decimal.GetBits(value); int scale = (bits[3] >> 16) & 0x7f; UInt128 magnitude = (uint)bits[0]; magnitude += (UInt128)(uint)bits[1] << 32; magnitude += (UInt128)(uint)bits[2] << 64; Int128 coefficient = checked((Int128)magnitude); if ((bits[3] & int.MinValue) != 0) coefficient = -coefficient; var canonical = new BaseDecimalValue(coefficient, checked((byte)scale)); byte[] bytes = new byte[17]; BinaryPrimitives.WriteInt128BigEndian(bytes, canonical.Coefficient); bytes[16] = canonical.Scale; return bytes; }

    private static void Visit(BaseIndexPredicateId id, Dictionary<BaseIndexPredicateId, BaseIndexPredicateNode> nodes, HashSet<BaseIndexPredicateId> reached, HashSet<BaseIndexPredicateId> active)
    { if (!active.Add(id)) throw Invalid(); if (!reached.Add(id)) throw Invalid(); foreach (BaseIndexPredicateId child in nodes[id].Children) Visit(child, nodes, reached, active); active.Remove(id); }

    private static void WritePredicateNode(ArrayBufferWriter<byte> writer, BaseIndexPredicateNode node)
    {
        Write(writer, node.Id.ToString()); WriteUInt16(writer, (ushort)node.Kind); WriteOptional(writer, node.FieldOrdinal);
        Write(writer, node.Literal is not null); if (node.Literal is { } literal) { WriteUInt16(writer, (ushort)literal.Kind); WriteCodec(writer, literal.Codec); Write(writer, literal.CanonicalBytes.AsSpan()); }
        WriteUInt32(writer, checked((uint)node.Children.Length)); foreach (BaseIndexPredicateId child in node.Children) Write(writer, child.ToString());
    }

    private static void WriteConstraints(ArrayBufferWriter<byte> writer, BaseScalarConstraintSet value)
    {
        WriteOptional(writer, value.MinimumUtf8Bytes); WriteOptional(writer, value.MaximumUtf8Bytes); WriteOptional(writer, value.StringNormalization is null ? null : (int)value.StringNormalization.Value);
        if (value.StringNormalization is not null) writer.Write(Convert.FromHexString(BaseUnicode17NfcData.ReceiptChecksum));
        WriteOptional(writer, value.MinimumInt32); WriteOptional(writer, value.MaximumInt32); WriteOptional(writer, value.MinimumInt64); WriteOptional(writer, value.MaximumInt64);
        WriteOptional(writer, value.MinimumUInt32); WriteOptional(writer, value.MaximumUInt32); WriteOptional(writer, value.MinimumUInt64); WriteOptional(writer, value.MaximumUInt64);
        WriteDecimal(writer, value.MinimumDecimal); WriteDecimal(writer, value.MaximumDecimal); WriteUInt32(writer, checked((uint)value.AllowedEnumLiterals.Length)); foreach (string item in value.AllowedEnumLiterals) Write(writer, item);
        WriteOptional(writer, value.MaximumBinaryBytes); WriteOptional(writer, value.MaximumCanonicalJsonBytes); WriteOptional(writer, value.JsonShape is null ? null : (int)value.JsonShape.Value);
        WriteOptional(writer, value.MaximumJsonDepth); WriteOptional(writer, value.MaximumJsonArrayItems); WriteOptional(writer, value.MaximumJsonObjectProperties); WriteOptional(writer, value.MaximumJsonTotalNodes);
        WriteOptional(writer, value.MaximumJsonTotalStringUtf8Bytes); WriteOptional(writer, value.MaximumJsonTotalNameUtf8Bytes); WriteOptional(writer, value.MinimumCollectionItems); WriteOptional(writer, value.MaximumCollectionItems);
    }

    private static void WriteCodec(ArrayBufferWriter<byte> writer, BaseScalarCodecAuthority codec)
    {
        Write(writer, codec.Id.ToString()); WriteUInt64(writer, checked((ulong)codec.Version)); WriteUInt16(writer, (ushort)codec.Kind);
        WriteUInt32(writer, checked((uint)codec.AllowedConstraints.Length)); foreach (BaseScalarConstraintKind constraint in codec.AllowedConstraints) WriteUInt16(writer, (ushort)constraint);
        writer.Write(codec.CodecChecksum.ToArray()); WriteUInt64(writer, checked((ulong)codec.EqualityVersion)); writer.Write(codec.EqualityChecksum.ToArray());
        Write(writer, codec.OrderingVersion is not null); if (codec.OrderingVersion is { } orderingVersion) { WriteUInt64(writer, checked((ulong)orderingVersion)); writer.Write(codec.OrderingChecksum?.ToArray() ?? throw Invalid()); }
    }
    private static void WriteDecimal(ArrayBufferWriter<byte> writer, BaseDecimalValue? value) { Write(writer, value is not null); if (value is { } item) { Span<byte> bytes = stackalloc byte[16]; BinaryPrimitives.WriteInt128BigEndian(bytes, item.Coefficient); writer.Write(bytes); writer.Write([item.Scale]); } }
    private static void WriteOptional(ArrayBufferWriter<byte> writer, int? value) { Write(writer, value is not null); if (value is not null) Write(writer, value.Value); }
    private static void WriteOptional(ArrayBufferWriter<byte> writer, long? value) { Write(writer, value is not null); if (value is not null) Write(writer, value.Value); }
    private static void WriteOptional(ArrayBufferWriter<byte> writer, uint? value) { Write(writer, value is not null); if (value is not null) { Span<byte> bytes = writer.GetSpan(4); BinaryPrimitives.WriteUInt32BigEndian(bytes, value.Value); writer.Advance(4); } }
    private static void WriteOptional(ArrayBufferWriter<byte> writer, ulong? value) { Write(writer, value is not null); if (value is not null) { Span<byte> bytes = writer.GetSpan(8); BinaryPrimitives.WriteUInt64BigEndian(bytes, value.Value); writer.Advance(8); } }
    private static void Write(ArrayBufferWriter<byte> writer, string value) { byte[] bytes = BaseStrictUtf8.Encode(value); WriteUInt32(writer, checked((uint)bytes.Length)); writer.Write(bytes); }
    private static void Write(ArrayBufferWriter<byte> writer, ReadOnlySpan<byte> value) { WriteUInt32(writer, checked((uint)value.Length)); writer.Write(value); }
    private static void Write(ArrayBufferWriter<byte> writer, bool value) => writer.Write([value ? (byte)1 : (byte)0]);
    private static void Write(ArrayBufferWriter<byte> writer, int value) { Span<byte> bytes = writer.GetSpan(4); BinaryPrimitives.WriteInt32BigEndian(bytes, value); writer.Advance(4); }
    private static void Write(ArrayBufferWriter<byte> writer, long value) { Span<byte> bytes = writer.GetSpan(8); BinaryPrimitives.WriteInt64BigEndian(bytes, value); writer.Advance(8); }
    private static int Compare(BaseDecimalValue left, BaseDecimalValue right)
    {
        int leftSign = left.Coefficient.CompareTo(0), rightSign = right.Coefficient.CompareTo(0);
        if (leftSign != rightSign) return leftSign.CompareTo(rightSign);
        if (leftSign == 0) return 0;
        string leftDigits = left.Coefficient.ToString(System.Globalization.CultureInfo.InvariantCulture).TrimStart('-');
        string rightDigits = right.Coefficient.ToString(System.Globalization.CultureInfo.InvariantCulture).TrimStart('-');
        int leftExponent = leftDigits.Length - left.Scale, rightExponent = rightDigits.Length - right.Scale;
        int magnitude = leftExponent.CompareTo(rightExponent);
        if (magnitude == 0)
        {
            int length = Math.Max(leftDigits.Length, rightDigits.Length);
            for (int index = 0; index < length && magnitude == 0; index++) magnitude = (index < leftDigits.Length ? leftDigits[index] : '0').CompareTo(index < rightDigits.Length ? rightDigits[index] : '0');
        }
        return leftSign > 0 ? magnitude : -magnitude;
    }
    private static byte[] Ascii(string value) { byte[] result = new byte[value.Length]; for (int index = 0; index < value.Length; index++) result[index] = value[index] <= 0x7f ? (byte)value[index] : throw Invalid(); return result; }
    private static string Ascii(ReadOnlySpan<byte> value) { char[] result = new char[value.Length]; for (int index = 0; index < value.Length; index++) result[index] = value[index] <= 0x7f ? (char)value[index] : throw Invalid(); return new string(result); }
    private static byte[] AuthorityChecksum(ReadOnlySpan<byte> purpose, string id, long version, BaseScalarKind kind) { var writer = new ArrayBufferWriter<byte>(); writer.Write(purpose); Write(writer, id); WriteUInt64(writer, checked((ulong)version)); WriteUInt16(writer, (ushort)kind); return SHA256.HashData(writer.WrittenSpan); }
    private static void WriteUInt16(ArrayBufferWriter<byte> writer, ushort value) { Span<byte> bytes = writer.GetSpan(2); BinaryPrimitives.WriteUInt16BigEndian(bytes, value); writer.Advance(2); }
    private static void WriteUInt32(ArrayBufferWriter<byte> writer, uint value) { Span<byte> bytes = writer.GetSpan(4); BinaryPrimitives.WriteUInt32BigEndian(bytes, value); writer.Advance(4); }
    private static void WriteUInt64(ArrayBufferWriter<byte> writer, ulong value) { Span<byte> bytes = writer.GetSpan(8); BinaryPrimitives.WriteUInt64BigEndian(bytes, value); writer.Advance(8); }
    private static void NonNegative(int? value) { if (value < 0) throw Invalid(); }
    private static void Range<T>(T? minimum, T? maximum) where T : struct, IComparable<T> { if (minimum is { } left && maximum is { } right && left.CompareTo(right) > 0) throw Invalid(); }
    private static string Wire(BaseScalarKind kind) => kind.ToString().ToLowerInvariant();
    private static InvalidOperationException Invalid() => new(BaseSchemaErrorCodes.ContractInvalid);
}
