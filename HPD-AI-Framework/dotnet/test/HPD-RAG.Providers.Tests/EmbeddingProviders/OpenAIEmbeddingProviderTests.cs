using HPD.RAG.Core.Providers.Embedding;
using HPD.RAG.EmbeddingProviders.OpenAI;
using Xunit;

namespace HPD.RAG.Providers.Tests.EmbeddingProviders;

public sealed class OpenAIEmbeddingProviderTests
{
    static OpenAIEmbeddingProviderTests()
    {
        OpenAIEmbeddingProviderModule.Initialize();
    }

    // T-068
    [Fact]
    public void ModuleInitializer_RegistersProvider()
    {
        var provider = EmbeddingDiscovery.GetProvider("openai");
        Assert.NotNull(provider);
    }

    // T-069
    [Fact]
    public void ProviderKey_IsCorrectString()
    {
        var features = new OpenAIEmbeddingProviderFeatures_Accessor();
        Assert.Equal("openai", features.ProviderKey);
    }

    // T-070
    [Fact]
    public void CreateEmbeddingGenerator_WithValidConfig_DoesNotThrow()
    {
        var provider = EmbeddingDiscovery.GetProvider("openai");
        Assert.NotNull(provider);

        var config = new EmbeddingConfig
        {
            ProviderKey = "openai",
            ModelName = "text-embedding-3-small",
            ApiKey = "sk-fake-api-key-for-testing"
        };

        var generator = provider.CreateEmbeddingGenerator(config, null);
        Assert.NotNull(generator);
    }

    // T-071
    [Fact]
    public void CreateEmbeddingGenerator_MissingApiKey_Throws()
    {
        var provider = EmbeddingDiscovery.GetProvider("openai");
        Assert.NotNull(provider);

        var config = new EmbeddingConfig
        {
            ProviderKey = "openai",
            ModelName = "text-embedding-3-small"
            // No ApiKey
        };

        Assert.Throws<InvalidOperationException>(() =>
            provider.CreateEmbeddingGenerator(config, null));
    }

    // Helper: expose internal type via the public interface
    private sealed class OpenAIEmbeddingProviderFeatures_Accessor : IEmbeddingProviderFeatures
    {
        private readonly IEmbeddingProviderFeatures _inner;

        public OpenAIEmbeddingProviderFeatures_Accessor()
        {
            // Use discovery to get a real instance
            OpenAIEmbeddingProviderModule.Initialize();
            _inner = EmbeddingDiscovery.GetProvider("openai")!;
        }

        public string ProviderKey => _inner.ProviderKey;
        public string DisplayName => _inner.DisplayName;

        public Microsoft.Extensions.AI.IEmbeddingGenerator<string, Microsoft.Extensions.AI.Embedding<float>>
            CreateEmbeddingGenerator(EmbeddingConfig config, IServiceProvider? services = null)
            => _inner.CreateEmbeddingGenerator(config, services);
    }
}
