using System.Buffers.Binary;
using System.Collections.Immutable;
using System.Globalization;
using System.Text;
using System.Text.Json;

namespace HPD.Base;

internal static class BaseTextOrderingContract
{
    internal static ImmutableArray<BaseTextOrder> Validate(IEnumerable<BaseTextOrder> values, BaseTextIndexDefinition index)
    {
        ArgumentNullException.ThrowIfNull(values);
        ImmutableArray<BaseTextOrder> result = values.ToImmutableArray();
        if (result.Length > index.Limits.MaximumSecondaryOrderFields || result.Select(static value => value.StableFieldId).Distinct(StringComparer.Ordinal).Count() != result.Length) throw Invalid();
        foreach (BaseTextOrder value in result)
            if (string.IsNullOrWhiteSpace(value.StableFieldId) || !Enum.IsDefined(value.Direction) || !Enum.IsDefined(value.NullOrder)
                || !index.FilterFields.Any(field => field.StableFieldId == value.StableFieldId)) throw Invalid();
        return result;
    }

    internal static ImmutableArray<BaseTextOrderingValue> Values(RecordPayload payload, BaseTextIndexDefinition index, ImmutableArray<BaseTextOrder> order)
    {
        var result = ImmutableArray.CreateBuilder<BaseTextOrderingValue>(order.Length);
        foreach (BaseTextOrder item in order)
        {
            BaseTextIndexFilterFieldDefinition field = index.FilterFields.Single(value => value.StableFieldId == item.StableFieldId);
            JsonElement value = default;
            bool present = payload.Fields?.TryGetValue(field.StableFieldId, out value) == true || payload.Fields?.TryGetValue(field.WireName, out value) == true;
            bool nil = present && value.ValueKind == JsonValueKind.Null;
            result.Add(new() { StableFieldId = new(item.StableFieldId.AsSpan()), Missing = !present, Null = nil, CanonicalJsonUtf8 = present && !nil ? ImmutableArray.Create(Encoding.UTF8.GetBytes(value.GetRawText())) : [] });
        }
        return result.MoveToImmutable();
    }

    internal static ImmutableArray<byte> Boundary(BaseTextScore score, ImmutableArray<BaseTextOrderingValue> values, RecordId id)
    {
        using var stream = new MemoryStream(); stream.Write("HPDB-TEXT-ORDER-1\0"u8); U64(stream, ulong.MaxValue - score.Units); U32(stream, values.Length);
        foreach (BaseTextOrderingValue value in values) { String(stream, value.StableFieldId); stream.WriteByte(value.Missing ? (byte)1 : (byte)0); stream.WriteByte(value.Null ? (byte)1 : (byte)0); Bytes(stream, value.CanonicalJsonUtf8.AsSpan()); }
        String(stream, id.Value); return ImmutableArray.Create(stream.ToArray());
    }

    internal static int Compare(BaseTextCandidate left, BaseTextCandidate right, ImmutableArray<BaseTextOrder> order)
    {
        int score = right.Score.Units.CompareTo(left.Score.Units); if (score != 0) return score;
        for (int index = 0; index < order.Length; index++)
        {
            BaseTextOrderingValue a = left.SecondaryOrdering[index], b = right.SecondaryOrdering[index];
            int comparison = CompareValue(a, b, order[index].NullOrder);
            if (comparison != 0) return order[index].Direction == QuerySortDirection.Desc ? -comparison : comparison;
        }
        return string.Compare(left.RecordId.Value, right.RecordId.Value, StringComparison.Ordinal);
    }

    internal static bool ValuesValid(BaseTextCandidate candidate, ImmutableArray<BaseTextOrder> order)
    {
        if (candidate.SecondaryOrdering.Length != order.Length) return false;
        for (int index = 0; index < order.Length; index++)
        {
            BaseTextOrderingValue value = candidate.SecondaryOrdering[index];
            if (value.StableFieldId != order[index].StableFieldId || value.Missing && value.Null || (value.Missing || value.Null) != value.CanonicalJsonUtf8.IsEmpty) return false;
            if (!value.Missing && !value.Null) try { using JsonDocument document = JsonDocument.Parse(value.CanonicalJsonUtf8.ToArray()); if (document.RootElement.ValueKind is JsonValueKind.Array or JsonValueKind.Object or JsonValueKind.Null or JsonValueKind.Undefined) return false; } catch (JsonException) { return false; }
        }
        return true;
    }

    internal static bool ValuesEqual(ImmutableArray<BaseTextOrderingValue> left, ImmutableArray<BaseTextOrderingValue> right)
    {
        if (left.Length != right.Length) return false;
        for (int index = 0; index < left.Length; index++)
            if (left[index].StableFieldId != right[index].StableFieldId || left[index].Missing != right[index].Missing || left[index].Null != right[index].Null
                || !left[index].CanonicalJsonUtf8.AsSpan().SequenceEqual(right[index].CanonicalJsonUtf8.AsSpan())) return false;
        return true;
    }

    private static int CompareValue(BaseTextOrderingValue left, BaseTextOrderingValue right, QueryNullOrder nullOrder)
    {
        bool leftNull = left.Missing || left.Null, rightNull = right.Missing || right.Null;
        if (leftNull || rightNull) { if (leftNull == rightNull) return 0; bool first = nullOrder != QueryNullOrder.Last; return leftNull == first ? -1 : 1; }
        using JsonDocument a = JsonDocument.Parse(left.CanonicalJsonUtf8.ToArray()); using JsonDocument b = JsonDocument.Parse(right.CanonicalJsonUtf8.ToArray());
        if (a.RootElement.ValueKind == JsonValueKind.Number && b.RootElement.ValueKind == JsonValueKind.Number && a.RootElement.TryGetDecimal(out decimal x) && b.RootElement.TryGetDecimal(out decimal y)) return x.CompareTo(y);
        return string.Compare(a.RootElement.ToString(), b.RootElement.ToString(), StringComparison.Ordinal);
    }
    private static ArgumentException Invalid() => new(BaseTextErrorCodes.QueryInvalid);
    private static void U64(Stream stream, ulong value) { Span<byte> bytes = stackalloc byte[8]; BinaryPrimitives.WriteUInt64BigEndian(bytes, value); stream.Write(bytes); }
    private static void U32(Stream stream, int value) { Span<byte> bytes = stackalloc byte[4]; BinaryPrimitives.WriteUInt32BigEndian(bytes, checked((uint)value)); stream.Write(bytes); }
    private static void Bytes(Stream stream, ReadOnlySpan<byte> value) { U32(stream, value.Length); stream.Write(value); }
    private static void String(Stream stream, string value) => Bytes(stream, Encoding.UTF8.GetBytes(value));
}
