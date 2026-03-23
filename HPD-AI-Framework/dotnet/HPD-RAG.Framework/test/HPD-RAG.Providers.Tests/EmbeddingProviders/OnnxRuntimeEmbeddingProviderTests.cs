using HPD.RAG.Core.Providers.Embedding;
using HPD.RAG.EmbeddingProviders.OnnxRuntime;
using Xunit;

namespace HPD.RAG.Providers.Tests.EmbeddingProviders;

public sealed class OnnxRuntimeEmbeddingProviderTests
{
    static OnnxRuntimeEmbeddingProviderTests()
    {
        OnnxRuntimeEmbeddingProviderModule.Initialize();
    }

    // T-068 (OnnxRuntime variant)
    [Fact]
    public void ModuleInitializer_RegistersProvider()
    {
        var provider = EmbeddingDiscovery.GetProvider("onnxruntime");
        Assert.NotNull(provider);
    }

    // T-069 (OnnxRuntime variant)
    [Fact]
    public void ProviderKey_IsCorrectString()
    {
        var provider = EmbeddingDiscovery.GetProvider("onnxruntime");
        Assert.NotNull(provider);
        Assert.Equal("onnxruntime", provider.ProviderKey);
    }

    // T-070 (OnnxRuntime variant) — requires ModelPath + VocabPath that exist on disk
    // OnnxRuntimeEmbeddingProviderFeatures validates that the files exist before constructing,
    // so we skip this test since we cannot provide real model files in CI.
    [Fact(Skip = "OnnxRuntime provider validates that ModelPath and VocabPath exist on disk at construction time — no fake paths possible")]
    public void CreateEmbeddingGenerator_WithValidConfig_DoesNotThrow()
    {
    }

    // T-071 (OnnxRuntime variant) — missing ModelPath in ProviderOptionsJson
    [Fact]
    public void CreateEmbeddingGenerator_MissingModelPath_Throws()
    {
        var provider = EmbeddingDiscovery.GetProvider("onnxruntime");
        Assert.NotNull(provider);

        // No ProviderOptionsJson → GetTypedConfig returns null → modelPath is null
        var config = new EmbeddingConfig
        {
            ProviderKey = "onnxruntime",
            ModelName = "onnx-model"
            // No ProviderOptionsJson
        };

        Assert.Throws<InvalidOperationException>(() =>
            provider.CreateEmbeddingGenerator(config, null));
    }

    // T-072 — OnnxRuntimeEmbeddingConfig roundtrip via GetTypedConfig
    [Fact]
    public void GetTypedConfig_Roundtrip_ModelPath()
    {
        const string modelPath = "/fake/path/model.onnx";
        const string vocabPath = "/fake/path/vocab.txt";

        var config = new EmbeddingConfig
        {
            ProviderKey = "onnxruntime",
            ModelName = "onnx-model",
            ProviderOptionsJson = $$$"""{"modelPath":"{{{modelPath}}}","vocabPath":"{{{vocabPath}}}"}"""
        };

        var typedConfig = config.GetTypedConfig<OnnxRuntimeEmbeddingConfig>();

        Assert.NotNull(typedConfig);
        Assert.Equal(modelPath, typedConfig.ModelPath);
    }
}
