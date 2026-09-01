namespace HPD.Base;

/// <summary>Applies the graph-pinned Unicode 17.0.0 NFC authority without host Unicode tables.</summary>
internal static class BaseUnicode17Nfc
{
    private const int SBase = 0xAC00, LBase = 0x1100, VBase = 0x1161, TBase = 0x11A7;
    private const int LCount = 19, VCount = 21, TCount = 28, NCount = VCount * TCount, SCount = LCount * NCount;

    internal static bool IsNormalized(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        List<int> original = Scalars(value);
        var decomposed = new List<int>(original.Count);
        foreach (int scalar in original) Decompose(scalar, decomposed);
        List<int> normalized = Compose(decomposed);
        return original.SequenceEqual(normalized);
    }

    private static List<int> Scalars(string value)
    {
        var result = new List<int>(value.Length);
        for (int index = 0; index < value.Length; index++)
        {
            char current = value[index];
            if (!char.IsSurrogate(current)) { result.Add(current); continue; }
            if (!char.IsHighSurrogate(current) || ++index >= value.Length || !char.IsLowSurrogate(value[index]))
                throw new FormatException(BaseSchemaErrorCodes.ContractInvalid);
            result.Add(char.ConvertToUtf32(current, value[index]));
        }
        return result;
    }

    private static void Decompose(int scalar, List<int> output)
    {
        int hangul = scalar - SBase;
        if ((uint)hangul < SCount)
        {
            int leading = LBase + hangul / NCount;
            int vowel = VBase + hangul % NCount / TCount;
            int trailing = TBase + hangul % TCount;
            AppendOrdered(output, leading); AppendOrdered(output, vowel); if (trailing != TBase) AppendOrdered(output, trailing);
            return;
        }
        int entry = FindCodepoint(BaseUnicode17NfcData.DecompositionIndex, 3, scalar);
        if (entry >= 0)
        {
            int offset = BaseUnicode17NfcData.DecompositionIndex[entry + 1], length = BaseUnicode17NfcData.DecompositionIndex[entry + 2];
            for (int index = 0; index < length; index++) Decompose(BaseUnicode17NfcData.DecompositionData[offset + index], output);
            return;
        }
        AppendOrdered(output, scalar);
    }

    private static void AppendOrdered(List<int> output, int scalar)
    {
        int combining = CombiningClass(scalar), position = output.Count;
        output.Add(scalar);
        if (combining == 0) return;
        while (position > 0)
        {
            int preceding = CombiningClass(output[position - 1]);
            if (preceding == 0 || preceding <= combining) break;
            output[position] = output[position - 1]; position--;
        }
        output[position] = scalar;
    }

    private static List<int> Compose(List<int> input)
    {
        if (input.Count == 0) return [];
        var output = new List<int>(input.Count) { input[0] };
        int starterPosition = 0, starter = input[0], lastClass = CombiningClass(input[0]);
        for (int index = 1; index < input.Count; index++)
        {
            int current = input[index], currentClass = CombiningClass(current), composite = ComposePair(starter, current);
            if (composite >= 0 && (lastClass < currentClass || lastClass == 0))
            {
                output[starterPosition] = composite; starter = composite;
                continue;
            }
            if (currentClass == 0) { starterPosition = output.Count; starter = current; }
            lastClass = currentClass; output.Add(current);
        }
        return output;
    }

    private static int ComposePair(int starter, int current)
    {
        int leading = starter - LBase;
        if ((uint)leading < LCount && (uint)(current - VBase) < VCount)
            return SBase + (leading * VCount + current - VBase) * TCount;
        int syllable = starter - SBase;
        if ((uint)syllable < SCount && syllable % TCount == 0 && current > TBase && current < TBase + TCount)
            return starter + current - TBase;
        int[] values = BaseUnicode17NfcData.Compositions; int low = 0, high = values.Length / 3 - 1;
        while (low <= high)
        {
            int middle = low + (high - low) / 2, offset = middle * 3;
            int comparison = values[offset].CompareTo(starter); if (comparison == 0) comparison = values[offset + 1].CompareTo(current);
            if (comparison == 0) return values[offset + 2];
            if (comparison < 0) low = middle + 1; else high = middle - 1;
        }
        return -1;
    }

    private static int CombiningClass(int scalar)
    {
        int entry = FindCodepoint(BaseUnicode17NfcData.CombiningClasses, 2, scalar);
        return entry < 0 ? 0 : BaseUnicode17NfcData.CombiningClasses[entry + 1];
    }

    private static int FindCodepoint(int[] values, int width, int scalar)
    {
        int low = 0, high = values.Length / width - 1;
        while (low <= high)
        {
            int middle = low + (high - low) / 2, offset = middle * width;
            int comparison = values[offset].CompareTo(scalar);
            if (comparison == 0) return offset;
            if (comparison < 0) low = middle + 1; else high = middle - 1;
        }
        return -1;
    }
}
