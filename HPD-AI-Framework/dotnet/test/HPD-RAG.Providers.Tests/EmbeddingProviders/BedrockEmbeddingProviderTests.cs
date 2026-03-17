using HPD.RAG.Core.Providers.Embedding;
using HPD.RAG.EmbeddingProviders.Bedrock;
using Xunit;

namespace HPD.RAG.Providers.Tests.EmbeddingProviders;

public sealed class BedrockEmbeddingProviderTests
{
    static BedrockEmbeddingProviderTests()
    {
        BedrockEmbeddingProviderModule.Initialize();
    }

    // T-068 (Bedrock variant)
    [Fact]
    public void ModuleInitializer_RegistersProvider()
    {
        var provider = EmbeddingDiscovery.GetProvider("bedrock");
        Assert.NotNull(provider);
    }

    // T-069 (Bedrock variant)
    [Fact]
    public void ProviderKey_IsCorrectString()
    {
        var provider = EmbeddingDiscovery.GetProvider("bedrock");
        Assert.NotNull(provider);
        Assert.Equal("bedrock", provider.ProviderKey);
    }

    // T-070 (Bedrock variant) — requires ModelName + Region (via typed config)
    [Fact]
    public void CreateEmbeddingGenerator_WithValidConfig_DoesNotThrow()
    {
        var provider = EmbeddingDiscovery.GetProvider("bedrock");
        Assert.NotNull(provider);

        // Provide region via ProviderOptionsJson; credentials omitted — AWS default chain used
        var config = new EmbeddingConfig
        {
            ProviderKey = "bedrock",
            ModelName = "amazon.titan-embed-text-v1",
            ProviderOptionsJson = """{"region":"us-east-1","accessKeyId":"AKIAFAKEACCESSKEY","secretAccessKey":"fakeSecretKeyForTesting1234567890"}"""
        };

        // AmazonBedrockRuntimeClient is constructed without making a network call
        var generator = provider.CreateEmbeddingGenerator(config, null);
        Assert.NotNull(generator);
    }

    // T-071 (Bedrock variant) — missing ModelName and missing Region (no env var set)
    [Fact]
    public void CreateEmbeddingGenerator_MissingRegion_Throws()
    {
        var provider = EmbeddingDiscovery.GetProvider("bedrock");
        Assert.NotNull(provider);

        // Temporarily clear environment variables to ensure no region is resolved
        var savedRegion = Environment.GetEnvironmentVariable("AWS_REGION");
        var savedDefaultRegion = Environment.GetEnvironmentVariable("AWS_DEFAULT_REGION");
        try
        {
            Environment.SetEnvironmentVariable("AWS_REGION", null);
            Environment.SetEnvironmentVariable("AWS_DEFAULT_REGION", null);

            var config = new EmbeddingConfig
            {
                ProviderKey = "bedrock",
                ModelName = "amazon.titan-embed-text-v1"
                // No region in ProviderOptionsJson, no env vars
            };

            Assert.Throws<InvalidOperationException>(() =>
                provider.CreateEmbeddingGenerator(config, null));
        }
        finally
        {
            Environment.SetEnvironmentVariable("AWS_REGION", savedRegion);
            Environment.SetEnvironmentVariable("AWS_DEFAULT_REGION", savedDefaultRegion);
        }
    }
}
