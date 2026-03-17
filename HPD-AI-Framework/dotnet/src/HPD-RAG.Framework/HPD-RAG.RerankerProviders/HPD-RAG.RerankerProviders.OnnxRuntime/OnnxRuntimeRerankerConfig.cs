namespace HPD.RAG.RerankerProviders.OnnxRuntime;

/// <summary>
/// OnnxRuntime-specific reranker configuration.
/// </summary>
public sealed class OnnxRuntimeRerankerConfig
{
    /// <summary>
    /// Required. Absolute or relative path to the cross-encoder .onnx model file.
    /// Example: "/models/cross-encoder-ms-marco-MiniLM-L-6-v2.onnx"
    /// </summary>
    public required string ModelPath { get; set; }

    /// <summary>
    /// Optional path to the tokenizer vocabulary file (vocab.txt or tokenizer.json).
    /// When null the implementation will look for vocab.txt / tokenizer.json
    /// in the same directory as <see cref="ModelPath"/>.
    /// </summary>
    public string? TokenizerPath { get; set; }

    /// <summary>
    /// Maximum sequence length (query + passage combined). Defaults to 512.
    /// Sequences longer than this are truncated.
    /// </summary>
    public int MaxSequenceLength { get; set; } = 512;
}
