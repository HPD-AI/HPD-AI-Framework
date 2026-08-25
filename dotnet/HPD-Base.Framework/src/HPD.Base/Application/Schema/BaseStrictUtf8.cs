namespace HPD.Base;

internal static class BaseStrictUtf8
{
    internal static int GetByteCount(string value)
    {
        ArgumentNullException.ThrowIfNull(value); int count = 0;
        for (int index = 0; index < value.Length; index++)
        {
            int scalar = value[index];
            if (char.IsHighSurrogate(value[index]))
            {
                if (++index >= value.Length || !char.IsLowSurrogate(value[index])) throw new FormatException(BaseSchemaErrorCodes.ScalarConstraintViolated);
                scalar = char.ConvertToUtf32(value[index - 1], value[index]);
            }
            else if (char.IsLowSurrogate(value[index])) throw new FormatException(BaseSchemaErrorCodes.ScalarConstraintViolated);
            count = checked(count + (scalar <= 0x7f ? 1 : scalar <= 0x7ff ? 2 : scalar <= 0xffff ? 3 : 4));
        }
        return count;
    }

    internal static byte[] Encode(string value)
    {
        byte[] result = new byte[GetByteCount(value)]; int offset = 0;
        for (int index = 0; index < value.Length; index++)
        {
            int scalar = value[index]; if (char.IsHighSurrogate(value[index])) scalar = char.ConvertToUtf32(value[index], value[++index]);
            if (scalar <= 0x7f) result[offset++] = (byte)scalar;
            else if (scalar <= 0x7ff) { result[offset++] = (byte)(0xc0 | scalar >> 6); result[offset++] = (byte)(0x80 | scalar & 0x3f); }
            else if (scalar <= 0xffff) { result[offset++] = (byte)(0xe0 | scalar >> 12); result[offset++] = (byte)(0x80 | scalar >> 6 & 0x3f); result[offset++] = (byte)(0x80 | scalar & 0x3f); }
            else { result[offset++] = (byte)(0xf0 | scalar >> 18); result[offset++] = (byte)(0x80 | scalar >> 12 & 0x3f); result[offset++] = (byte)(0x80 | scalar >> 6 & 0x3f); result[offset++] = (byte)(0x80 | scalar & 0x3f); }
        }
        return result;
    }
}
