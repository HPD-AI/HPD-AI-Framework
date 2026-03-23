using System.Diagnostics.CodeAnalysis;
using HPD.RAG.Core.DTOs;
using HPD.RAG.Core.Providers.Reranker;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using Microsoft.ML.Tokenizers;

namespace HPD.RAG.RerankerProviders.OnnxRuntime;

/// <summary>
/// Reranker that runs a cross-encoder model locally via Microsoft.ML.OnnxRuntime.
/// The model must accept inputs: input_ids, attention_mask, token_type_ids (BERT-style).
/// The model must produce a single logit score per pair (shape: [batch, 1] or [batch]).
/// A sigmoid is applied to produce a [0, 1] relevance score.
/// </summary>
[RequiresUnreferencedCode("OnnxRuntime uses reflection-based tokenizer loading.")]
public sealed class OnnxRuntimeReranker : IReranker, IDisposable
{
    private readonly InferenceSession _session;
    private readonly BertTokenizer _tokenizer;
    private readonly int _maxSequenceLength;
    private bool _disposed;

    public OnnxRuntimeReranker(RerankerConfig config)
    {
        ArgumentNullException.ThrowIfNull(config);

        var typedConfig = config.GetTypedConfig<OnnxRuntimeRerankerConfig>();
        if (typedConfig is null || string.IsNullOrWhiteSpace(typedConfig.ModelPath))
            throw new InvalidOperationException(
                "OnnxRuntimeReranker requires OnnxRuntimeRerankerConfig with a non-empty ModelPath " +
                "in RerankerConfig.ProviderOptionsJson.");

        if (!File.Exists(typedConfig.ModelPath))
            throw new FileNotFoundException(
                $"OnnxRuntime model file not found: {typedConfig.ModelPath}", typedConfig.ModelPath);

        _maxSequenceLength = typedConfig.MaxSequenceLength > 0 ? typedConfig.MaxSequenceLength : 512;

        // Resolve tokenizer path: explicit path, or look for vocab.txt / tokenizer.json
        // alongside the model file.
        var tokenizerPath = ResolveTokenizerPath(typedConfig);

        _session = new InferenceSession(typedConfig.ModelPath);
        _tokenizer = BertTokenizer.Create(tokenizerPath);
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<MragSearchResultDto>> RerankAsync(
        string query,
        IReadOnlyList<MragSearchResultDto> results,
        int topN,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        ArgumentNullException.ThrowIfNull(results);

        cancellationToken.ThrowIfCancellationRequested();

        if (results.Count == 0 || topN <= 0)
            return Task.FromResult<IReadOnlyList<MragSearchResultDto>>(Array.Empty<MragSearchResultDto>());

        var scored = new (MragSearchResultDto Result, double Score)[results.Count];

        for (int i = 0; i < results.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var score = ScorePair(query, results[i].Content);
            scored[i] = (results[i], score);
        }

        // Sort descending by score
        Array.Sort(scored, static (a, b) => b.Score.CompareTo(a.Score));

        var effectiveTopN = Math.Min(topN, scored.Length);
        var reranked = new List<MragSearchResultDto>(effectiveTopN);
        for (int i = 0; i < effectiveTopN; i++)
            reranked.Add(scored[i].Result with { Score = scored[i].Score });

        return Task.FromResult<IReadOnlyList<MragSearchResultDto>>(reranked.AsReadOnly());
    }

    private double ScorePair(string query, string passage)
    {
        // Tokenize as a BERT sentence pair: [CLS] query [SEP] passage [SEP]
        // Query segment: encode with special tokens → [CLS] q1 q2 ... [SEP]
        var queryIds = _tokenizer.EncodeToIds(query, addSpecialTokens: true);
        // Passage segment: encode without special tokens, then append [SEP] manually
        var passageIds = _tokenizer.EncodeToIds(passage, addSpecialTokens: false);

        // Build combined id list: query + passage + SEP
        var combinedCount = queryIds.Count + passageIds.Count + 1;
        var tokenCount = Math.Min(combinedCount, _maxSequenceLength);

        var inputIds = new long[tokenCount];
        var attentionMask = new long[tokenCount];
        var tokenTypeIds = new long[tokenCount];

        // Fill query segment (TypeId = 0)
        int qi = Math.Min(queryIds.Count, tokenCount);
        for (int i = 0; i < qi; i++)
        {
            inputIds[i] = queryIds[i];
            attentionMask[i] = 1L;
            tokenTypeIds[i] = 0L;
        }
        // Fill passage segment (TypeId = 1) and trailing SEP
        for (int i = qi; i < tokenCount; i++)
        {
            int pi = i - qi;
            inputIds[i] = pi < passageIds.Count ? passageIds[pi] : _tokenizer.SeparatorTokenId;
            attentionMask[i] = 1L;
            tokenTypeIds[i] = 1L;
        }

        int[] shape = [1, tokenCount];

        var inputs = new List<NamedOnnxValue>
        {
            NamedOnnxValue.CreateFromTensor("input_ids",
                new DenseTensor<long>(inputIds, shape)),
            NamedOnnxValue.CreateFromTensor("attention_mask",
                new DenseTensor<long>(attentionMask, shape)),
            NamedOnnxValue.CreateFromTensor("token_type_ids",
                new DenseTensor<long>(tokenTypeIds, shape))
        };

        using var outputs = _session.Run(inputs);

        // Expect first output to be logits with shape [1] or [1, 1]
        var logitsTensor = outputs[0].AsTensor<float>();
        var rawScore = logitsTensor.GetValue(0);

        // Apply sigmoid to convert logit → relevance score in [0, 1]
        return Sigmoid(rawScore);
    }

    private static double Sigmoid(float x) => 1.0 / (1.0 + Math.Exp(-x));

    private static string ResolveTokenizerPath(OnnxRuntimeRerankerConfig config)
    {
        if (!string.IsNullOrWhiteSpace(config.TokenizerPath))
        {
            if (!File.Exists(config.TokenizerPath))
                throw new FileNotFoundException(
                    $"Tokenizer file not found: {config.TokenizerPath}", config.TokenizerPath);
            return config.TokenizerPath;
        }

        var modelDir = Path.GetDirectoryName(config.ModelPath) ?? ".";

        // Prefer tokenizer.json (fast tokenizer), fall back to vocab.txt (WordPiece)
        var tokenizerJson = Path.Combine(modelDir, "tokenizer.json");
        if (File.Exists(tokenizerJson))
            return tokenizerJson;

        var vocabTxt = Path.Combine(modelDir, "vocab.txt");
        if (File.Exists(vocabTxt))
            return vocabTxt;

        throw new FileNotFoundException(
            $"No tokenizer file found next to model at '{config.ModelPath}'. " +
            "Expected tokenizer.json or vocab.txt, or set OnnxRuntimeRerankerConfig.TokenizerPath explicitly.");
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _session.Dispose();
    }
}
