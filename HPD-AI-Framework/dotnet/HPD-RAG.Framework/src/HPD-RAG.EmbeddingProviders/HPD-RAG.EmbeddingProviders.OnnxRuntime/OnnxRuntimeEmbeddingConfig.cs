using System.Text.Json.Serialization;

namespace HPD.RAG.EmbeddingProviders.OnnxRuntime;

/// <summary>
/// ONNX Runtime-specific embedding configuration.
///
/// JSON Example (ProviderOptionsJson):
/// <code>
/// {
///   "modelPath": "/path/to/model.onnx",
///   "vocabPath": "/path/to/vocab.txt",
///   "executionProvider": "CPU"
/// }
/// </code>
/// </summary>
public sealed class OnnxRuntimeEmbeddingConfig
{
    /// <summary>
    /// Absolute path to the ONNX model file (.onnx). Required.
    /// </summary>
    [JsonPropertyName("modelPath")]
    public string? ModelPath { get; set; }

    /// <summary>
    /// Absolute path to the tokenizer vocabulary file (e.g. vocab.txt). Required.
    /// </summary>
    [JsonPropertyName("vocabPath")]
    public string? VocabPath { get; set; }

    /// <summary>
    /// Execution provider to use. Defaults to "CPU".
    /// Other values: "CUDA", "DirectML", "TensorRT", etc.
    /// </summary>
    [JsonPropertyName("executionProvider")]
    public string ExecutionProvider { get; set; } = "CPU";
}
