using HPD.RAG.Core.Providers.Embedding;
using HPD.RAG.EmbeddingProviders.Ollama;
using Xunit;

namespace HPD.RAG.Providers.Tests.EmbeddingProviders;

public sealed class OllamaEmbeddingProviderTests
{
    static OllamaEmbeddingProviderTests()
    {
        OllamaEmbeddingProviderModule.Initialize();
    }

    // T-068 (Ollama variant)
    [Fact]
    public void ModuleInitializer_RegistersProvider()
    {
        var provider = EmbeddingDiscovery.GetProvider("ollama");
        Assert.NotNull(provider);
    }

    // T-069 (Ollama variant)
    [Fact]
    public void ProviderKey_IsCorrectString()
    {
        var provider = EmbeddingDiscovery.GetProvider("ollama");
        Assert.NotNull(provider);
        Assert.Equal("ollama", provider.ProviderKey);
    }

    // T-070 (Ollama variant) — Ollama only requires ModelName; Endpoint is optional (defaults to localhost)
    [Fact]
    public void CreateEmbeddingGenerator_WithValidConfig_DoesNotThrow()
    {
        var provider = EmbeddingDiscovery.GetProvider("ollama");
        Assert.NotNull(provider);

        var config = new EmbeddingConfig
        {
            ProviderKey = "ollama",
            ModelName = "nomic-embed-text"
            // No ApiKey needed; Endpoint defaults to http://localhost:11434
        };

        // OllamaApiClient is constructed without connecting — no network call at creation time
        var generator = provider.CreateEmbeddingGenerator(config, null);
        Assert.NotNull(generator);
    }

    // T-071 (Ollama variant) — ModelName is required
    [Fact]
    public void CreateEmbeddingGenerator_MissingModelName_Throws()
    {
        var provider = EmbeddingDiscovery.GetProvider("ollama");
        Assert.NotNull(provider);

        // EmbeddingConfig.ModelName is required at compile time, so pass empty string
        var config = new EmbeddingConfig
        {
            ProviderKey = "ollama",
            ModelName = ""
        };

        Assert.Throws<InvalidOperationException>(() =>
            provider.CreateEmbeddingGenerator(config, null));
    }
}
