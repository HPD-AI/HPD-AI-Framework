namespace HPD.RAG.Ingestion.Internal;

/// <summary>
/// Lightweight whitespace-based token splitter used by all chunker handlers.
/// Uses space-delimited word count as a fast proxy for BPE token count
/// (accurate within ~15% for English prose; sufficient for chunk boundary decisions).
/// </summary>
internal static class TokenSplitter
{
    /// <summary>
    /// Splits <paramref name="text"/> into windows of at most <paramref name="maxTokens"/> tokens
    /// with <paramref name="overlapTokens"/> token overlap between adjacent windows.
    /// Returns a single-element list when the text fits within <paramref name="maxTokens"/>.
    /// </summary>
    internal static List<string> Split(string text, int maxTokens, int overlapTokens)
    {
        if (string.IsNullOrWhiteSpace(text))
            return [];

        // Tokenise as whitespace-delimited words
        var words = text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);

        if (words.Length <= maxTokens)
            return [text];

        var chunks = new List<string>();
        int step = maxTokens - overlapTokens;
        if (step <= 0) step = maxTokens; // Defensive: should not occur after ValidateConfig

        int start = 0;
        while (start < words.Length)
        {
            int end = Math.Min(start + maxTokens, words.Length);
            chunks.Add(string.Join(" ", words, start, end - start));
            if (end == words.Length) break;
            start += step;
        }

        return chunks;
    }

    /// <summary>Returns a fast whitespace-based token-count estimate.</summary>
    internal static int EstimateTokens(string text) =>
        string.IsNullOrWhiteSpace(text)
            ? 0
            : text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Length;
}
