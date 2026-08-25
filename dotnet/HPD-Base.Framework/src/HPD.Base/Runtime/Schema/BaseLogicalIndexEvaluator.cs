using System.Buffers;
using System.Buffers.Binary;
using System.Globalization;
using System.Text.Json;

namespace HPD.Base;

internal static class BaseLogicalIndexEvaluator
{
    internal static bool Includes(CollectionDefinition collection, BaseLogicalIndexDefinition index, RecordPayload payload)
    {
        FieldDefinition[] fields = (collection.Fields ?? []).OrderBy(static field => field.Id, StringComparer.Ordinal).ToArray();
        Dictionary<BaseIndexPredicateId, BaseIndexPredicateNode> nodes = index.MembershipPredicate.Nodes.ToDictionary(static node => node.Id);
        return Evaluate(nodes[index.MembershipPredicate.Root], nodes, fields, payload.Fields ?? []);
    }

    internal static byte[] Key(CollectionDefinition collection, BaseLogicalIndexDefinition index, RecordPayload payload)
    {
        FieldDefinition[] fields = (collection.Fields ?? []).OrderBy(static field => field.Id, StringComparer.Ordinal).ToArray();
        var writer = new ArrayBufferWriter<byte>(); writer.Write("hpd.base.logical-index-key.v1\0"u8); WriteUInt32(writer, checked((uint)index.Parts.Length));
        foreach (BaseLogicalIndexPart part in index.Parts)
        {
            FieldDefinition field = fields[part.FieldOrdinal];
            BaseScalarKind kind = field.ScalarKind ?? throw new InvalidOperationException(BaseSchemaErrorCodes.ContractInvalid);
            BaseScalarCodecAuthority codec = field.ScalarCodec ?? throw new InvalidOperationException(BaseSchemaErrorCodes.ContractInvalid);
            var framed = new ArrayBufferWriter<byte>();
            JsonElement value = default; bool present = payload.Fields?.TryGetValue(field.WireName, out value) == true;
            framed.Write([!present ? (byte)0 : value.ValueKind == JsonValueKind.Null ? (byte)1 : (byte)2]);
            WriteUInt16(framed, (ushort)kind); WriteString(framed, codec.Id.ToString()); WriteUInt64(framed, checked((ulong)codec.Version)); framed.Write(codec.CodecChecksum.ToArray());
            if (present && value.ValueKind != JsonValueKind.Null) { byte[] bytes = BaseScalarCanonical.Encode(kind, value); WriteUInt32(framed, checked((uint)bytes.Length)); framed.Write(bytes); }
            WriteUInt32(writer, checked((uint)framed.WrittenCount)); writer.Write(framed.WrittenSpan);
        }
        return writer.WrittenSpan.ToArray();
    }

    internal static int Compare(CollectionDefinition collection, BaseLogicalIndexDefinition index, RecordPayload left, RecordId leftId, RecordPayload right, RecordId rightId)
    {
        FieldDefinition[] fields = (collection.Fields ?? []).OrderBy(static field => field.Id, StringComparer.Ordinal).ToArray();
        foreach (BaseLogicalIndexPart part in index.Parts)
        {
            FieldDefinition field = fields[part.FieldOrdinal];
            (int Rank, JsonElement Value) leftPart = OrderingPart(field, part.NullOrder, left);
            (int Rank, JsonElement Value) rightPart = OrderingPart(field, part.NullOrder, right);
            int comparison = leftPart.Rank.CompareTo(rightPart.Rank);
            if (comparison == 0 && leftPart.Rank == ValueRank(part.NullOrder))
                comparison = CompareValues(field, leftPart.Value, rightPart.Value);
            if (comparison != 0)
                return part.Direction == BaseIndexSortDirection.Ascending ? comparison : -comparison;
        }
        return StringComparer.Ordinal.Compare(leftId.ToString(), rightId.ToString());
    }

    private static bool Evaluate(BaseIndexPredicateNode node, Dictionary<BaseIndexPredicateId, BaseIndexPredicateNode> nodes, FieldDefinition[] fields, Dictionary<string, JsonElement> values)
    {
        JsonElement value = default;
        bool present = node.FieldOrdinal is { } ordinal && values.TryGetValue(fields[ordinal].WireName, out value);
        return node.Kind switch
        {
            BaseIndexPredicateNodeKind.True => true,
            BaseIndexPredicateNodeKind.False => false,
            BaseIndexPredicateNodeKind.IsDefined => present,
            BaseIndexPredicateNodeKind.IsMissing => !present,
            BaseIndexPredicateNodeKind.IsNull => present && value.ValueKind == JsonValueKind.Null,
            BaseIndexPredicateNodeKind.IsNotNull => present && value.ValueKind != JsonValueKind.Null,
            BaseIndexPredicateNodeKind.Equal => present && value.ValueKind != JsonValueKind.Null && BaseScalarCanonical.Encode(node.Literal!.Kind, value).AsSpan().SequenceEqual(node.Literal.CanonicalBytes.AsSpan()),
            BaseIndexPredicateNodeKind.And => node.Children.All(child => Evaluate(nodes[child], nodes, fields, values)),
            BaseIndexPredicateNodeKind.Or => node.Children.Any(child => Evaluate(nodes[child], nodes, fields, values)),
            BaseIndexPredicateNodeKind.Not => !Evaluate(nodes[node.Children[0]], nodes, fields, values),
            _ => throw new InvalidOperationException(BaseSchemaErrorCodes.ContractInvalid),
        };
    }

    private static (int Rank, JsonElement Value) OrderingPart(FieldDefinition field, BaseIndexNullOrder nullOrder, RecordPayload payload)
    {
        JsonElement value = default;
        bool present = payload.Fields?.TryGetValue(field.WireName, out value) == true;
        int state = !present ? 0 : value.ValueKind == JsonValueKind.Null ? 1 : 2;
        int rank = nullOrder == BaseIndexNullOrder.MissingThenNullThenValue ? state : 2 - state;
        return (rank, value);
    }

    private static int ValueRank(BaseIndexNullOrder nullOrder) => nullOrder == BaseIndexNullOrder.MissingThenNullThenValue ? 2 : 0;

    private static int CompareValues(FieldDefinition field, JsonElement left, JsonElement right)
    {
        BaseScalarKind kind = field.ScalarKind ?? throw new InvalidOperationException(BaseSchemaErrorCodes.ContractInvalid);
        return kind switch
        {
            BaseScalarKind.String or BaseScalarKind.Binary or BaseScalarKind.Guid => BaseScalarCanonical.Encode(kind, left).AsSpan().SequenceCompareTo(BaseScalarCanonical.Encode(kind, right)),
            BaseScalarKind.Int32 => left.GetInt32().CompareTo(right.GetInt32()),
            BaseScalarKind.Int64 => left.GetInt64().CompareTo(right.GetInt64()),
            BaseScalarKind.UInt32 => left.GetUInt32().CompareTo(right.GetUInt32()),
            BaseScalarKind.UInt64 => left.GetUInt64().CompareTo(right.GetUInt64()),
            BaseScalarKind.Decimal => CompareDecimal(left, right),
            BaseScalarKind.Boolean => left.GetBoolean().CompareTo(right.GetBoolean()),
            BaseScalarKind.UtcDateTime => DateTimeOffset.ParseExact(left.GetString()!, "yyyy-MM-dd'T'HH:mm:ss.fffffff'Z'", CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal).UtcTicks.CompareTo(DateTimeOffset.ParseExact(right.GetString()!, "yyyy-MM-dd'T'HH:mm:ss.fffffff'Z'", CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal).UtcTicks),
            BaseScalarKind.ClosedEnum => field.ScalarConstraints!.AllowedEnumLiterals.IndexOf(left.GetString()!).CompareTo(field.ScalarConstraints.AllowedEnumLiterals.IndexOf(right.GetString()!)),
            _ => throw new InvalidOperationException(BaseSchemaErrorCodes.ContractInvalid),
        };
    }

    private static int CompareDecimal(JsonElement left, JsonElement right)
    {
        if (!BaseScalarCanonical.TryParseDecimal(left.GetRawText(), out BaseDecimalValue leftValue) || !BaseScalarCanonical.TryParseDecimal(right.GetRawText(), out BaseDecimalValue rightValue))
            throw new InvalidOperationException(BaseSchemaErrorCodes.ContractInvalid);
        return BaseScalarCanonical.Compare(leftValue, rightValue);
    }

    private static void WriteString(ArrayBufferWriter<byte> writer, string value) { byte[] bytes = BaseStrictUtf8.Encode(value); WriteUInt32(writer, checked((uint)bytes.Length)); writer.Write(bytes); }
    private static void WriteUInt16(ArrayBufferWriter<byte> writer, ushort value) { Span<byte> bytes = writer.GetSpan(2); BinaryPrimitives.WriteUInt16BigEndian(bytes, value); writer.Advance(2); }
    private static void WriteUInt32(ArrayBufferWriter<byte> writer, uint value) { Span<byte> bytes = writer.GetSpan(4); BinaryPrimitives.WriteUInt32BigEndian(bytes, value); writer.Advance(4); }
    private static void WriteUInt64(ArrayBufferWriter<byte> writer, ulong value) { Span<byte> bytes = writer.GetSpan(8); BinaryPrimitives.WriteUInt64BigEndian(bytes, value); writer.Advance(8); }
}
