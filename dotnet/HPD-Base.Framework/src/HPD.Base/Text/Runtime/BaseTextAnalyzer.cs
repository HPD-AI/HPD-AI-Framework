using System.Collections.Immutable;
using System.Text;

namespace HPD.Base;

/// <summary>Executes the portable BASE lexical analyzer.</summary>
public static class BaseTextAnalyzer
{
    /// <summary>Normalizes and tokenizes one source field under the v1 contract.</summary>
    public static ImmutableArray<string> Analyze(string? source)
    {
        if (string.IsNullOrEmpty(source)) return [];
        ValidateUtf16(source);

        string normalized = NormalizeNfkc(source);
        var folded = new StringBuilder(normalized.Length);
        foreach (Rune rune in normalized.EnumerateRunes()) folded.Append(BaseTextUnicode17.Fold(rune.Value));
        normalized = NormalizeNfkc(folded.ToString());

        var tokens = ImmutableArray.CreateBuilder<string>();
        var current = new StringBuilder();
        Rune? previous = null;
        Rune[] runes = normalized.EnumerateRunes().ToArray();
        long normalizedBytes = 0;
        for (int index = 0; index < runes.Length; index++)
        {
            Rune rune = runes[index];
            byte category = BaseTextUnicode17.TokenCategory(rune.Value);
            bool mark = category == 2;
            bool letterOrDigit = category == 1;
            bool connector = category == 3;
            bool nextLetterOrDigit = index + 1 < runes.Length && BaseTextUnicode17.TokenCategory(runes[index + 1].Value) == 1;
            bool accepted = letterOrDigit
                || (mark && current.Length > 0 && previous is not null)
                || (connector && current.Length > 0 && previous is not null
                    && BaseTextUnicode17.TokenCategory(previous.Value.Value) == 1 && nextLetterOrDigit);

            if (!accepted)
            {
                Emit(current, tokens, ref normalizedBytes);
                previous = null;
                continue;
            }

            current.Append(rune.ToString());
            previous = rune;
        }
        Emit(current, tokens, ref normalizedBytes);
        return tokens.ToImmutable();
    }

    private static string NormalizeNfkc(string source)
    {
        var decomposed = new List<int>(source.Length);
        foreach (Rune rune in source.EnumerateRunes()) Decompose(rune.Value, decomposed);
        for (int index = 1; index < decomposed.Count; index++)
        {
            byte current = BaseTextUnicode17.CombiningClass(decomposed[index]);
            if (current == 0) continue;
            int insert = index;
            while (insert > 0)
            {
                byte prior = BaseTextUnicode17.CombiningClass(decomposed[insert - 1]);
                if (prior == 0 || prior <= current) break;
                (decomposed[insert - 1], decomposed[insert]) = (decomposed[insert], decomposed[insert - 1]);
                insert--;
            }
        }
        Compose(decomposed);
        var result = new StringBuilder(decomposed.Count);
        foreach (int scalar in decomposed) result.Append(char.ConvertFromUtf32(scalar));
        return result.ToString();
    }

    private static void Decompose(int scalar, List<int> output)
    {
        const int sBase = 0xAC00, lBase = 0x1100, vBase = 0x1161, tBase = 0x11A7, nCount = 588, tCount = 28;
        int sIndex = scalar - sBase;
        if ((uint)sIndex < 11172) { output.Add(lBase + sIndex / nCount); output.Add(vBase + (sIndex % nCount) / tCount); if (sIndex % tCount != 0) output.Add(tBase + sIndex % tCount); return; }
        string? mapping = BaseTextUnicode17.Decomposition(scalar);
        if (mapping is null) { output.Add(scalar); return; }
        foreach (Rune rune in mapping.EnumerateRunes()) Decompose(rune.Value, output);
    }

    private static void Compose(List<int> values)
    {
        if (values.Count == 0) return;
        int starterIndex = 0, starter = values[0]; byte previousClass = 0;
        for (int index = 1; index < values.Count; index++)
        {
            int current = values[index]; byte currentClass = BaseTextUnicode17.CombiningClass(current);
            int composite = HangulCompose(starter, current);
            if (composite == 0) composite = BaseTextUnicode17.Compose(starter, current);
            if (composite != 0 && (previousClass < currentClass || previousClass == 0)) { values[starterIndex] = starter = composite; values.RemoveAt(index--); continue; }
            if (currentClass == 0) { starterIndex = index; starter = current; }
            previousClass = currentClass;
        }
    }

    private static int HangulCompose(int first, int second)
    {
        const int sBase = 0xAC00, lBase = 0x1100, vBase = 0x1161, tBase = 0x11A7, lCount = 19, vCount = 21, tCount = 28, nCount = vCount * tCount, sCount = lCount * nCount;
        int lIndex = first - lBase;
        if ((uint)lIndex < lCount) { int vIndex = second - vBase; if ((uint)vIndex < vCount) return sBase + (lIndex * vCount + vIndex) * tCount; }
        int sIndex = first - sBase;
        if ((uint)sIndex < sCount && sIndex % tCount == 0) { int tIndex = second - tBase; if ((uint)tIndex is > 0 and < tCount) return first + tIndex; }
        return 0;
    }

    private static void Emit(StringBuilder current, ImmutableArray<string>.Builder tokens, ref long normalizedBytes)
    {
        if (current.Length == 0) return;
        string token = current.ToString();
        int bytes = Encoding.UTF8.GetByteCount(token);
        if (bytes > BaseTextAnalyzers.MaximumTokenBytes)
            throw new ArgumentException("A normalized text token exceeds 64 UTF-8 bytes.", BaseTextErrorCodes.BudgetExceeded);
        normalizedBytes = checked(normalizedBytes + bytes);
        if (tokens.Count >= BaseTextAnalyzers.MaximumTokensPerField || normalizedBytes > BaseTextAnalyzers.MaximumNormalizedBytesPerField)
            throw new ArgumentException("A normalized text field exceeds its token or byte limit.", BaseTextErrorCodes.BudgetExceeded);
        tokens.Add(token);
        current.Clear();
    }

    private static void ValidateUtf16(string source)
    {
        for (int index = 0; index < source.Length; index++)
        {
            char value = source[index];
            if (char.IsHighSurrogate(value))
            {
                if (++index >= source.Length || !char.IsLowSurrogate(source[index]))
                    throw new ArgumentException("Text contains an unpaired UTF-16 surrogate.", nameof(source));
            }
            else if (char.IsLowSurrogate(value))
            {
                throw new ArgumentException("Text contains an unpaired UTF-16 surrogate.", nameof(source));
            }
        }
    }
}
