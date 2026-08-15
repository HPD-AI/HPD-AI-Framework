using System.Buffers;
using System.Buffers.Binary;
using System.Globalization;
using System.Text;
using System.Text.Json;

namespace HPD.Base;

internal static class BaseSelectionOrderTuple
{
    internal static byte[] Encode(RecordEnvelope record, QuerySort[] sort)
    {
        var writer = new ArrayBufferWriter<byte>();
        foreach (QuerySort item in sort)
        {
            Write(writer, [(byte)item.Direction, (byte)item.Nulls]);
            if (string.Equals(item.Field, "id", StringComparison.Ordinal)) { Scalar(writer, 1, Encoding.UTF8.GetBytes(record.Id.Value)); continue; }
            JsonElement value = default;
            bool present = record.Payload.Fields?.TryGetValue(item.Field, out value) == true;
            if (!present) { Scalar(writer, 0, []); continue; }
            switch (value.ValueKind)
            {
                case JsonValueKind.Null: Scalar(writer, 1, []); break;
                case JsonValueKind.False: Scalar(writer, 2, [0]); break;
                case JsonValueKind.True: Scalar(writer, 2, [1]); break;
                case JsonValueKind.Number: Scalar(writer, 3, Encoding.UTF8.GetBytes(value.GetRawText())); break;
                case JsonValueKind.String: Scalar(writer, 4, Encoding.UTF8.GetBytes(value.GetString()!)); break;
                default: throw new InvalidOperationException("A selection sort key is not scalar.");
            }
        }
        return writer.WrittenSpan.ToArray();
    }

    private static void Scalar(ArrayBufferWriter<byte> writer, byte tag, ReadOnlySpan<byte> value)
    {
        Write(writer, [tag]); Span<byte> length = stackalloc byte[4]; BinaryPrimitives.WriteInt32BigEndian(length, value.Length); Write(writer, length); Write(writer, value);
    }

    private static void Write(ArrayBufferWriter<byte> writer, ReadOnlySpan<byte> value) { value.CopyTo(writer.GetSpan(value.Length)); writer.Advance(value.Length); }
}
