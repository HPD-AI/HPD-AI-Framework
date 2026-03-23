using System.Diagnostics.CodeAnalysis;
using HPD.RAG.Core.Providers.Reranker;

namespace HPD.RAG.RerankerProviders.OnnxRuntime;

/// <summary>
/// Provider descriptor for the local ONNX cross-encoder reranker.
/// Registered automatically via <see cref="OnnxRuntimeRerankerModule"/>.
/// </summary>
public sealed class OnnxRuntimeRerankerFeatures : IRerankerFeatures
{
    public string ProviderKey => "onnxruntime";
    public string DisplayName => "ONNX Runtime Cross-Encoder Reranker (local)";

    [RequiresUnreferencedCode("OnnxRuntimeReranker uses reflection-based tokenizer loading.")]
    public IReranker CreateReranker(RerankerConfig config, IServiceProvider? services = null)
    {
        ArgumentNullException.ThrowIfNull(config);
        return new OnnxRuntimeReranker(config);
    }
}
