using HPD.RAG.Core.Providers.Embedding;
using HPD.RAG.EmbeddingProviders.HuggingFace;
using Xunit;

namespace HPD.RAG.Providers.Tests.EmbeddingProviders;

public sealed class HuggingFaceEmbeddingProviderTests
{
    static HuggingFaceEmbeddingProviderTests()
    {
        HuggingFaceEmbeddingProviderModule.Initialize();
    }

    // T-068 (HuggingFace variant)
    [Fact]
    public void ModuleInitializer_RegistersProvider()
    {
        var provider = EmbeddingDiscovery.GetProvider("huggingface");
        Assert.NotNull(provider);
    }

    // T-069 (HuggingFace variant)
    [Fact]
    public void ProviderKey_IsCorrectString()
    {
        var provider = EmbeddingDiscovery.GetProvider("huggingface");
        Assert.NotNull(provider);
        Assert.Equal("huggingface", provider.ProviderKey);
    }

    // T-070 (HuggingFace variant) — requires ModelName + ApiKey
    [Fact]
    public void CreateEmbeddingGenerator_WithValidConfig_DoesNotThrow()
    {
        var provider = EmbeddingDiscovery.GetProvider("huggingface");
        Assert.NotNull(provider);

        var config = new EmbeddingConfig
        {
            ProviderKey = "huggingface",
            ModelName = "sentence-transformers/all-MiniLM-L6-v2",
            ApiKey = "hf_fake_token_for_testing"
        };

        // HuggingFaceEmbeddingGenerator is constructed without making a network call
        var generator = provider.CreateEmbeddingGenerator(config, null);
        Assert.NotNull(generator);
    }

    // T-071 (HuggingFace variant) — missing both ModelName and ApiKey
    [Fact]
    public void CreateEmbeddingGenerator_MissingRequiredFields_Throws()
    {
        var provider = EmbeddingDiscovery.GetProvider("huggingface");
        Assert.NotNull(provider);

        var config = new EmbeddingConfig
        {
            ProviderKey = "huggingface",
            ModelName = "sentence-transformers/all-MiniLM-L6-v2"
            // No ApiKey
        };

        Assert.Throws<InvalidOperationException>(() =>
            provider.CreateEmbeddingGenerator(config, null));
    }
}
