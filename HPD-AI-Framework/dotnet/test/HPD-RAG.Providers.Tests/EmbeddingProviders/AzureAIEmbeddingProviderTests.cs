using HPD.RAG.Core.Providers.Embedding;
using HPD.RAG.EmbeddingProviders.AzureAI;
using Xunit;

namespace HPD.RAG.Providers.Tests.EmbeddingProviders;

public sealed class AzureAIEmbeddingProviderTests
{
    static AzureAIEmbeddingProviderTests()
    {
        AzureAIEmbeddingProviderModule.Initialize();
    }

    // T-068 (AzureAI variant)
    [Fact]
    public void ModuleInitializer_RegistersProvider()
    {
        var provider = EmbeddingDiscovery.GetProvider("azureai");
        Assert.NotNull(provider);
    }

    // T-069 (AzureAI variant)
    [Fact]
    public void ProviderKey_IsCorrectString()
    {
        var provider = EmbeddingDiscovery.GetProvider("azureai");
        Assert.NotNull(provider);
        Assert.Equal("azureai", provider.ProviderKey);
    }

    // T-070 (AzureAI variant) — requires ApiKey + Endpoint + ModelName(DeploymentName)
    [Fact]
    public void CreateEmbeddingGenerator_WithValidConfig_DoesNotThrow()
    {
        var provider = EmbeddingDiscovery.GetProvider("azureai");
        Assert.NotNull(provider);

        var config = new EmbeddingConfig
        {
            ProviderKey = "azureai",
            ModelName = "text-embedding-ada-002",
            ApiKey = "fake-azure-api-key-for-testing",
            Endpoint = "https://fake-resource.openai.azure.com/"
        };

        // AzureOpenAIClient is constructed without connecting — no network call at creation time
        var generator = provider.CreateEmbeddingGenerator(config, null);
        Assert.NotNull(generator);
    }

    // T-071 (AzureAI variant) — missing Endpoint
    [Fact]
    public void CreateEmbeddingGenerator_MissingEndpoint_Throws()
    {
        var provider = EmbeddingDiscovery.GetProvider("azureai");
        Assert.NotNull(provider);

        var config = new EmbeddingConfig
        {
            ProviderKey = "azureai",
            ModelName = "text-embedding-ada-002",
            ApiKey = "fake-azure-api-key"
            // No Endpoint
        };

        Assert.Throws<InvalidOperationException>(() =>
            provider.CreateEmbeddingGenerator(config, null));
    }
}
