using HPD.RAG.Core.Providers.Reranker;
using HPD.RAG.RerankerProviders.OnnxRuntime;
using Xunit;

namespace HPD.RAG.Providers.Tests.RerankerProviders;

public sealed class OnnxRuntimeRerankerProviderTests
{
    static OnnxRuntimeRerankerProviderTests()
    {
        OnnxRuntimeRerankerModule.Initialize();
    }

    // T-073 (OnnxRuntime reranker variant)
    [Fact]
    public void ModuleInitializer_RegistersProvider()
    {
        var provider = RerankerDiscovery.GetProvider("onnxruntime");
        Assert.NotNull(provider);
    }

    // T-074 (OnnxRuntime reranker variant)
    [Fact]
    public void ProviderKey_IsCorrectString()
    {
        var provider = RerankerDiscovery.GetProvider("onnxruntime");
        Assert.NotNull(provider);
        Assert.Equal("onnxruntime", provider.ProviderKey);
    }

    // T-075 (OnnxRuntime reranker variant)
    // OnnxRuntimeReranker loads the ONNX model file at construction time, so we cannot
    // test with a fake path. The constructor will fail with a real file system error.
    [Fact(Skip = "OnnxRuntime reranker validates and loads model files at construction — no fake paths possible")]
    public void CreateReranker_WithValidConfig_DoesNotThrow()
    {
    }
}
