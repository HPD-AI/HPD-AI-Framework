using HPD.RAG.Core.Providers.Reranker;
using HPD.RAG.RerankerProviders.Jina;
using Xunit;

namespace HPD.RAG.Providers.Tests.RerankerProviders;

public sealed class JinaRerankerProviderTests
{
    static JinaRerankerProviderTests()
    {
        JinaRerankerModule.Initialize();
    }

    // T-073 (Jina variant)
    [Fact]
    public void ModuleInitializer_RegistersProvider()
    {
        var provider = RerankerDiscovery.GetProvider("jina");
        Assert.NotNull(provider);
    }

    // T-074 (Jina variant)
    [Fact]
    public void ProviderKey_IsCorrectString()
    {
        var provider = RerankerDiscovery.GetProvider("jina");
        Assert.NotNull(provider);
        Assert.Equal("jina", provider.ProviderKey);
    }

    // T-075 (Jina variant) — JinaReranker is constructed with an HttpClient; no network call at construction
    [Fact]
    public void CreateReranker_WithValidConfig_DoesNotThrow()
    {
        var provider = RerankerDiscovery.GetProvider("jina");
        Assert.NotNull(provider);

        var config = new RerankerConfig
        {
            ProviderKey = "jina",
            ApiKey = "fake-jina-api-key",
            ModelName = "jina-reranker-v2-base-multilingual"
        };

        var reranker = provider.CreateReranker(config, null);
        Assert.NotNull(reranker);
    }
}
