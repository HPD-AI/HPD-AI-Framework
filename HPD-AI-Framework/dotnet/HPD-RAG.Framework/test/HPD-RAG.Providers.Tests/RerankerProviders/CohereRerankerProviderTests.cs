using HPD.RAG.Core.Providers.Reranker;
using HPD.RAG.RerankerProviders.Cohere;
using Xunit;

namespace HPD.RAG.Providers.Tests.RerankerProviders;

public sealed class CohereRerankerProviderTests
{
    static CohereRerankerProviderTests()
    {
        CohereRerankerModule.Initialize();
    }

    // T-073
    [Fact]
    public void ModuleInitializer_RegistersProvider()
    {
        var provider = RerankerDiscovery.GetProvider("cohere");
        Assert.NotNull(provider);
    }

    // T-074
    [Fact]
    public void ProviderKey_IsCorrectString()
    {
        var provider = RerankerDiscovery.GetProvider("cohere");
        Assert.NotNull(provider);
        Assert.Equal("cohere", provider.ProviderKey);
    }

    // T-075 — CohereRerankerFeatures.CreateReranker only requires a non-null config;
    // it constructs an HttpClient internally and does NOT connect at construction time.
    [Fact]
    public void CreateReranker_WithValidConfig_DoesNotThrow()
    {
        var provider = RerankerDiscovery.GetProvider("cohere");
        Assert.NotNull(provider);

        var config = new RerankerConfig
        {
            ProviderKey = "cohere",
            ApiKey = "fake-cohere-api-key",
            ModelName = "rerank-english-v3.0"
        };

        var reranker = provider.CreateReranker(config, null);
        Assert.NotNull(reranker);
    }
}
