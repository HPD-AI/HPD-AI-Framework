using System.Globalization;
using System.Text;

namespace HPD.RAG.EmbeddingProviders.OnnxRuntime;

/// <summary>
/// Minimal WordPiece tokenizer for BERT-style models.
/// Loads a standard vocab.txt (one token per line, line index = token id).
/// Produces input_ids, attention_mask, and token_type_ids for ONNX inference.
/// Special token IDs use the standard BERT defaults: [CLS]=101, [SEP]=102, [UNK]=100, [PAD]=0.
/// </summary>
internal sealed class OnnxWordPieceTokenizer
{
    private readonly Dictionary<string, int> _vocab;
    private readonly int _clsId;
    private readonly int _sepId;
    private readonly int _unkId;

    public OnnxWordPieceTokenizer(string vocabPath)
    {
        _vocab = LoadVocab(vocabPath);
        _clsId = _vocab.GetValueOrDefault("[CLS]", 101);
        _sepId = _vocab.GetValueOrDefault("[SEP]", 102);
        _unkId = _vocab.GetValueOrDefault("[UNK]", 100);
    }

    /// <summary>
    /// Tokenizes text and returns arrays padded/truncated to maxLength for BERT inference.
    /// </summary>
    public (long[] InputIds, long[] AttentionMask, long[] TokenTypeIds) Tokenize(
        string text, int maxLength)
    {
        var tokens = TokenizeToWordPieces(text);

        // Truncate to fit [CLS] + tokens + [SEP] within maxLength
        int maxTokens = maxLength - 2;
        if (tokens.Count > maxTokens)
            tokens = tokens.Take(maxTokens).ToList();

        int totalLen = tokens.Count + 2; // +2 for [CLS] and [SEP]

        long[] inputIds = new long[maxLength];
        long[] attentionMask = new long[maxLength];
        long[] tokenTypeIds = new long[maxLength]; // all zeros for single-segment BERT

        inputIds[0] = _clsId;
        attentionMask[0] = 1;

        for (int i = 0; i < tokens.Count; i++)
        {
            inputIds[i + 1] = tokens[i];
            attentionMask[i + 1] = 1;
        }

        inputIds[totalLen - 1] = _sepId;
        attentionMask[totalLen - 1] = 1;

        // Remaining positions stay at zero (PAD id=0, attention_mask=0)

        return (inputIds, attentionMask, tokenTypeIds);
    }

    private List<int> TokenizeToWordPieces(string text)
    {
        var result = new List<int>();
        foreach (var word in SplitWords(text.ToLower(CultureInfo.InvariantCulture)))
        {
            foreach (var piece in WordPiece(word))
                result.Add(piece);
        }
        return result;
    }

    private IEnumerable<int> WordPiece(string word)
    {
        if (_vocab.TryGetValue(word, out int wordId))
        {
            yield return wordId;
            yield break;
        }

        int start = 0;
        bool failed = false;
        var pieces = new List<int>();

        while (start < word.Length)
        {
            int end = word.Length;
            int foundId = -1;

            while (start < end)
            {
                string sub = (start == 0 ? "" : "##") + word[start..end];
                if (_vocab.TryGetValue(sub, out int id))
                {
                    foundId = id;
                    break;
                }
                end--;
            }

            if (foundId == -1) { failed = true; break; }
            pieces.Add(foundId);
            start = end;
        }

        if (failed)
            yield return _unkId;
        else
            foreach (var p in pieces) yield return p;
    }

    private static IEnumerable<string> SplitWords(string text)
    {
        var sb = new StringBuilder();
        foreach (char c in text)
        {
            if (char.IsWhiteSpace(c))
            {
                if (sb.Length > 0) { yield return sb.ToString(); sb.Clear(); }
            }
            else if (char.IsPunctuation(c) || char.IsSymbol(c))
            {
                if (sb.Length > 0) { yield return sb.ToString(); sb.Clear(); }
                yield return c.ToString();
            }
            else
            {
                sb.Append(c);
            }
        }
        if (sb.Length > 0) yield return sb.ToString();
    }

    private static Dictionary<string, int> LoadVocab(string vocabPath)
    {
        var vocab = new Dictionary<string, int>(StringComparer.Ordinal);
        int index = 0;
        foreach (var line in File.ReadLines(vocabPath))
        {
            var token = line.Trim();
            if (!string.IsNullOrEmpty(token))
                vocab[token] = index;
            index++;
        }
        return vocab;
    }
}
