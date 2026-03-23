using Microsoft.Extensions.AI;
using HPD.RAG.Core.Providers.Embedding;

namespace HPD.RAG.EmbeddingProviders.OnnxRuntime;

/// <summary>
/// ONNX Runtime embedding provider for HPD.RAG.
/// Runs BERT-style embedding models locally via Microsoft.ML.OnnxRuntime.
///
/// Typed config: OnnxRuntimeEmbeddingConfig is required (ModelPath, VocabPath).
/// ModelPath and VocabPath must exist on disk; a clear FileNotFoundException is raised if not.
/// ExecutionProvider defaults to "CPU".
/// </summary>
internal sealed class OnnxRuntimeEmbeddingProviderFeatures : IEmbeddingProviderFeatures
{
    public string ProviderKey => "onnxruntime";
    public string DisplayName => "ONNX Runtime (Local)";

    public IEmbeddingGenerator<string, Embedding<float>> CreateEmbeddingGenerator(
        EmbeddingConfig config, IServiceProvider? services = null)
    {
        var typedConfig = config.GetTypedConfig<OnnxRuntimeEmbeddingConfig>();

        string? modelPath = typedConfig?.ModelPath;
        if (string.IsNullOrWhiteSpace(modelPath))
            throw new InvalidOperationException(
                "ModelPath is required for the OnnxRuntime embedding provider. " +
                "Set it via OnnxRuntimeEmbeddingConfig.ModelPath in ProviderOptionsJson.");

        if (!File.Exists(modelPath))
            throw new FileNotFoundException(
                $"ONNX model file not found at path: '{modelPath}'. " +
                "Verify ModelPath points to a valid .onnx file.", modelPath);

        string? vocabPath = typedConfig?.VocabPath;
        if (string.IsNullOrWhiteSpace(vocabPath))
            throw new InvalidOperationException(
                "VocabPath is required for the OnnxRuntime embedding provider. " +
                "Set it via OnnxRuntimeEmbeddingConfig.VocabPath in ProviderOptionsJson.");

        if (!File.Exists(vocabPath))
            throw new FileNotFoundException(
                $"Vocabulary file not found at path: '{vocabPath}'. " +
                "Verify VocabPath points to a valid vocabulary file.", vocabPath);

        string executionProvider = typedConfig?.ExecutionProvider ?? "CPU";

        return new OnnxRuntimeEmbeddingGenerator(modelPath, vocabPath, executionProvider, config.ModelName ?? "onnx");
    }
}
