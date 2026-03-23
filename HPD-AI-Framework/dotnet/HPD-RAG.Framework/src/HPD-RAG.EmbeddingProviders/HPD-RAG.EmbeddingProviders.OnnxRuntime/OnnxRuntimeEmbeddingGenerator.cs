using Microsoft.Extensions.AI;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;

namespace HPD.RAG.EmbeddingProviders.OnnxRuntime;

/// <summary>
/// IEmbeddingGenerator implementation backed by Microsoft.ML.OnnxRuntime.
/// Supports BERT-style models that accept input_ids, attention_mask, and token_type_ids inputs
/// and return last_hidden_state or pooler_output.
/// Embeddings are produced by mean-pooling the token embeddings from last_hidden_state.
/// </summary>
internal sealed class OnnxRuntimeEmbeddingGenerator : IEmbeddingGenerator<string, Embedding<float>>
{
    private readonly InferenceSession _session;
    private readonly OnnxWordPieceTokenizer _tokenizer;
    private readonly string _modelId;
    private EmbeddingGeneratorMetadata? _metadata;

    // Maximum sequence length for BERT-style models
    private const int MaxSequenceLength = 512;

    public OnnxRuntimeEmbeddingGenerator(
        string modelPath,
        string vocabPath,
        string executionProvider,
        string modelId)
    {
        _modelId = modelId;
        _tokenizer = new OnnxWordPieceTokenizer(vocabPath);
        _session = CreateSession(modelPath, executionProvider);
    }

    public EmbeddingGeneratorMetadata Metadata =>
        _metadata ??= new EmbeddingGeneratorMetadata(
            providerName: "onnxruntime",
            defaultModelId: _modelId);

    public async Task<GeneratedEmbeddings<Embedding<float>>> GenerateAsync(
        IEnumerable<string> values,
        EmbeddingGenerationOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        var inputs = values.ToList();
        if (inputs.Count == 0)
            return new GeneratedEmbeddings<Embedding<float>>();

        // Run on thread pool to avoid blocking the calling thread
        return await Task.Run(() => GenerateCore(inputs), cancellationToken);
    }

    private GeneratedEmbeddings<Embedding<float>> GenerateCore(List<string> inputs)
    {
        var embeddings = new List<Embedding<float>>(inputs.Count);

        foreach (var text in inputs)
        {
            var (inputIds, attentionMask, tokenTypeIds) = _tokenizer.Tokenize(text, MaxSequenceLength);
            int seqLen = inputIds.Length;

            var inputIdsTensor = new DenseTensor<long>(inputIds, new[] { 1, seqLen });
            var attentionMaskTensor = new DenseTensor<long>(attentionMask, new[] { 1, seqLen });
            var tokenTypeIdsTensor = new DenseTensor<long>(tokenTypeIds, new[] { 1, seqLen });

            var ortInputs = new List<NamedOnnxValue>
            {
                NamedOnnxValue.CreateFromTensor("input_ids", inputIdsTensor),
                NamedOnnxValue.CreateFromTensor("attention_mask", attentionMaskTensor),
                NamedOnnxValue.CreateFromTensor("token_type_ids", tokenTypeIdsTensor)
            };

            using var results = _session.Run(ortInputs);

            float[] vector = ExtractEmbedding(results, attentionMask, seqLen);
            embeddings.Add(new Embedding<float>(vector));
        }

        return new GeneratedEmbeddings<Embedding<float>>(embeddings);
    }

    private static float[] ExtractEmbedding(
        IDisposableReadOnlyCollection<DisposableNamedOnnxValue> results,
        long[] attentionMask,
        int seqLen)
    {
        // Try pooler_output first (single vector), fall back to mean-pooling last_hidden_state
        DisposableNamedOnnxValue? poolerOutput = null;
        DisposableNamedOnnxValue? lastHiddenState = null;

        foreach (var r in results)
        {
            if (r.Name == "pooler_output") poolerOutput = r;
            else if (r.Name == "last_hidden_state") lastHiddenState = r;
        }

        if (poolerOutput != null)
        {
            var tensor = poolerOutput.AsTensor<float>();
            int hiddenSize = tensor.Dimensions[^1];
            float[] vec = new float[hiddenSize];
            for (int i = 0; i < hiddenSize; i++)
                vec[i] = tensor[0, i];
            return NormalizeL2(vec);
        }

        if (lastHiddenState == null)
            throw new InvalidOperationException(
                "ONNX model output must include 'last_hidden_state' or 'pooler_output'.");

        // Mean-pool last_hidden_state over non-padding tokens
        var lhs = lastHiddenState.AsTensor<float>();
        int dims = lhs.Dimensions[^1];
        float[] meanVec = new float[dims];
        int validTokens = 0;

        for (int t = 0; t < seqLen; t++)
        {
            if (attentionMask[t] == 0) continue;
            validTokens++;
            for (int d = 0; d < dims; d++)
                meanVec[d] += lhs[0, t, d];
        }

        if (validTokens > 0)
        {
            for (int d = 0; d < dims; d++)
                meanVec[d] /= validTokens;
        }

        return NormalizeL2(meanVec);
    }

    private static float[] NormalizeL2(float[] v)
    {
        double norm = 0;
        foreach (float x in v) norm += (double)x * x;
        norm = Math.Sqrt(norm);
        if (norm < 1e-12) return v;
        float scale = (float)(1.0 / norm);
        for (int i = 0; i < v.Length; i++) v[i] *= scale;
        return v;
    }

    private static InferenceSession CreateSession(string modelPath, string executionProvider)
    {
        var sessionOptions = new SessionOptions();

        if (string.Equals(executionProvider, "CUDA", StringComparison.OrdinalIgnoreCase))
        {
            sessionOptions.AppendExecutionProvider_CUDA();
        }
        else if (string.Equals(executionProvider, "DirectML", StringComparison.OrdinalIgnoreCase) ||
                 string.Equals(executionProvider, "DML", StringComparison.OrdinalIgnoreCase))
        {
            sessionOptions.AppendExecutionProvider_DML();
        }
        // CPU is the default fallback

        return new InferenceSession(modelPath, sessionOptions);
    }

    public object? GetService(Type serviceType, object? serviceKey = null) => null;

    public void Dispose()
    {
        _session.Dispose();
    }
}
