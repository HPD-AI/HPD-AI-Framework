using HPD.RAG.Core.Providers.Reranker;
using HPD.RAG.RerankerProviders.HuggingFace;
using Xunit;

namespace HPD.RAG.Providers.Tests.RerankerProviders;

public sealed class HuggingFaceRerankerProviderTests
{
    static HuggingFaceRerankerProviderTests()
    {
        HuggingFaceRerankerModule.Initialize();
    }

    // T-073 (HuggingFace reranker variant)
    [Fact]
    public void ModuleInitializer_RegistersProvider()
    {
        var provider = RerankerDiscovery.GetProvider("huggingface");
        Assert.NotNull(provider);
    }

    // T-074 (HuggingFace reranker variant)
    [Fact]
    public void ProviderKey_IsCorrectString()
    {
        var provider = RerankerDiscovery.GetProvider("huggingface");
        Assert.NotNull(provider);
        Assert.Equal("huggingface", provider.ProviderKey);
    }

    // T-075 (HuggingFace reranker variant) — HuggingFaceReranker only wraps an HttpClient at construction
    [Fact]
    public void CreateReranker_WithValidConfig_DoesNotThrow()
    {
        var provider = RerankerDiscovery.GetProvider("huggingface");
        Assert.NotNull(provider);

        var config = new RerankerConfig
        {
            ProviderKey = "huggingface",
            ApiKey = "hf_fake_token_for_testing",
            Endpoint = "https://api-inference.huggingface.co",
            ModelName = "cross-encoder/ms-marco-MiniLM-L-6-v2"
        };

        var reranker = provider.CreateReranker(config, null);
        Assert.NotNull(reranker);
    }
}
