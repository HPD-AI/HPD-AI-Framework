using System.Text.Json;
using HPD.RAG.Core.Providers.Embedding;
using HPD.RAG.Core.Providers.VectorStore;
using Xunit;

namespace HPD.RAG.Core.Tests;

/// <summary>T-045 through T-046: Config GetTypedConfig tests.</summary>
public class ConfigTests
{
    private sealed class MyConfig
    {
        public string? Setting { get; set; }
    }

    // T-045
    [Fact]
    public void VectorStoreConfig_GetTypedConfig_UsesRegisteredDeserializer()
    {
        var key = "test-cfg-spy-" + Guid.NewGuid();
        var deserializerInvoked = false;

        // Register a spy deserializer
        VectorStoreDiscovery.RegisterVectorStoreConfigType(
            key,
            json =>
            {
                deserializerInvoked = true;
                return JsonSerializer.Deserialize<MyConfig>(json);
            },
            cfg => JsonSerializer.Serialize(cfg));

        // Register the factory so the key exists
        VectorStoreDiscovery.RegisterVectorStoreFactory(() => new StubVectorStoreFeaturesCfg(key));

        var config = new VectorStoreConfig
        {
            ProviderKey = key,
            ProviderOptionsJson = "{\"Setting\":\"hello\"}"
        };

        var result = config.GetTypedConfig<MyConfig>();

        Assert.True(deserializerInvoked, "Registered deserializer lambda should have been invoked.");
        Assert.NotNull(result);
        Assert.Equal("hello", result.Setting);
    }

    // T-046
    [Fact]
    public void EmbeddingConfig_GetTypedConfig_ReturnsNull_WhenNotRegistered()
    {
        // A provider key with no registered config type — deserialization should return null gracefully
        var key = "test-emb-no-cfg-" + Guid.NewGuid();

        var config = new EmbeddingConfig
        {
            ProviderKey = key,
            ModelName = "model",
            ProviderOptionsJson = "{\"setting\":\"val\"}"
        };

        var result = config.GetTypedConfig<MyConfig>();

        Assert.Null(result);
    }

    // ─── Internal helper stubs ────────────────────────────────────────────────

    private sealed class StubVectorStoreFeaturesCfg : HPD.RAG.Core.Providers.VectorStore.IVectorStoreFeatures
    {
        public StubVectorStoreFeaturesCfg(string key) => ProviderKey = key;
        public string ProviderKey { get; }
        public string DisplayName => "Stub";
        public Microsoft.Extensions.VectorData.VectorStore CreateVectorStore(VectorStoreConfig config, IServiceProvider? services = null)
            => throw new NotImplementedException();
        public HPD.RAG.Core.Filters.IMragFilterTranslator CreateFilterTranslator()
            => throw new NotImplementedException();
    }
}
