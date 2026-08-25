using System.Buffers.Binary;
using System.Globalization;
using System.Text.Json;

namespace HPD.Base;

internal static class BaseScalarCanonical
{
    internal static byte[] Encode(BaseScalarKind kind, JsonElement value) => kind switch
    {
        BaseScalarKind.String when value.ValueKind == JsonValueKind.String => BaseStrictUtf8.Encode(value.GetString()!),
        BaseScalarKind.Binary when value.ValueKind == JsonValueKind.String => BaseBinary.FromBase64(value.GetString()!).ToArray(),
        BaseScalarKind.Int32 when value.TryGetInt32(out int item) => Number(4, span => BinaryPrimitives.WriteInt32BigEndian(span, item)),
        BaseScalarKind.Int64 when value.TryGetInt64(out long item) => Number(8, span => BinaryPrimitives.WriteInt64BigEndian(span, item)),
        BaseScalarKind.UInt32 when value.TryGetUInt32(out uint item) => Number(4, span => BinaryPrimitives.WriteUInt32BigEndian(span, item)),
        BaseScalarKind.UInt64 when value.TryGetUInt64(out ulong item) => Number(8, span => BinaryPrimitives.WriteUInt64BigEndian(span, item)),
        BaseScalarKind.Decimal when value.ValueKind == JsonValueKind.Number && TryParseDecimal(value.GetRawText(), out BaseDecimalValue item) => DecimalBytes(item),
        BaseScalarKind.Boolean when value.ValueKind is JsonValueKind.True or JsonValueKind.False => [value.GetBoolean() ? (byte)1 : (byte)0],
        BaseScalarKind.Guid when value.ValueKind == JsonValueKind.String && Guid.TryParseExact(value.GetString(), "D", out Guid item) && value.GetString() == item.ToString("D") => BaseSchemaContract.EncodeLiteral(BaseScalarKind.Guid, item),
        BaseScalarKind.UtcDateTime when value.ValueKind == JsonValueKind.String && DateTimeOffset.TryParseExact(value.GetString(), "yyyy-MM-dd'T'HH:mm:ss.fffffff'Z'", CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out DateTimeOffset item) && item.Offset == TimeSpan.Zero => BaseSchemaContract.EncodeLiteral(BaseScalarKind.UtcDateTime, item),
        BaseScalarKind.ClosedEnum when value.ValueKind == JsonValueKind.String => BaseStrictUtf8.Encode(value.GetString()!),
        BaseScalarKind.CanonicalJson => BaseStrictUtf8.Encode(value.GetRawText()),
        BaseScalarKind.FrozenArray => throw new InvalidOperationException(BaseSchemaErrorCodes.ContractInvalid),
        _ => throw new InvalidOperationException(BaseSchemaErrorCodes.ContractInvalid),
    };

    internal static bool TryParseDecimal(string text, out BaseDecimalValue value)
    {
        value = default;
        if (text.Length == 0 || text.Contains('e', StringComparison.OrdinalIgnoreCase) || text[0] == '+') return false;
        bool negative = text[0] == '-'; int start = negative ? 1 : 0; int dot = text.IndexOf('.', start);
        ReadOnlySpan<char> whole = dot < 0 ? text.AsSpan(start) : text.AsSpan(start, dot - start);
        ReadOnlySpan<char> fraction = dot < 0 ? [] : text.AsSpan(dot + 1);
        if (whole.Length == 0 || fraction.Length > 28 || fraction.Length == 0 && dot >= 0 || whole.Length > 1 && whole[0] == '0' || !Digits(whole) || !Digits(fraction)) return false;
        string digits = string.Concat(whole, fraction);
        if (!UInt128.TryParse(digits, NumberStyles.None, CultureInfo.InvariantCulture, out UInt128 magnitude)) return false;
        UInt128 negativeLimit = (UInt128)Int128.MaxValue + 1;
        if (negative ? magnitude > negativeLimit : magnitude > (UInt128)Int128.MaxValue) return false;
        Int128 coefficient = negative
            ? magnitude == negativeLimit ? Int128.MinValue : -(Int128)magnitude
            : (Int128)magnitude;
        value = new BaseDecimalValue(coefficient, checked((byte)fraction.Length));
        return (coefficient != 0 || !negative) && string.Equals(DecimalText(value), text, StringComparison.Ordinal);
    }

    internal static int Compare(BaseDecimalValue left, BaseDecimalValue right)
    {
        int leftSign = left.Coefficient.CompareTo(0), rightSign = right.Coefficient.CompareTo(0);
        if (leftSign != rightSign) return leftSign.CompareTo(rightSign);
        if (leftSign == 0) return 0;
        string leftDigits = left.Coefficient.ToString(CultureInfo.InvariantCulture).TrimStart('-');
        string rightDigits = right.Coefficient.ToString(CultureInfo.InvariantCulture).TrimStart('-');
        int comparison = (leftDigits.Length - left.Scale).CompareTo(rightDigits.Length - right.Scale);
        if (comparison == 0)
        {
            int length = Math.Max(leftDigits.Length, rightDigits.Length);
            for (int index = 0; index < length && comparison == 0; index++)
                comparison = (index < leftDigits.Length ? leftDigits[index] : '0').CompareTo(index < rightDigits.Length ? rightDigits[index] : '0');
        }
        return leftSign > 0 ? comparison : -comparison;
    }

    internal static byte[] DecimalBytes(BaseDecimalValue value)
    {
        byte[] bytes = new byte[17]; BinaryPrimitives.WriteInt128BigEndian(bytes, value.Coefficient); bytes[16] = value.Scale; return bytes;
    }

    internal static string DecimalText(BaseDecimalValue value)
    {
        if (value.Coefficient == 0) return "0";
        bool negative = value.Coefficient < 0;
        string digits = value.Coefficient.ToString(CultureInfo.InvariantCulture).TrimStart('-');
        string magnitude = value.Scale == 0 ? digits
            : value.Scale < digits.Length ? digits.Insert(digits.Length - value.Scale, ".")
            : "0." + new string('0', value.Scale - digits.Length) + digits;
        return negative ? "-" + magnitude : magnitude;
    }

    private static bool Digits(ReadOnlySpan<char> value) { foreach (char item in value) if (item is < '0' or > '9') return false; return true; }
    private delegate void SpanWriter(Span<byte> value);
    private static byte[] Number(int length, SpanWriter write) { byte[] result = new byte[length]; write(result); return result; }
}
