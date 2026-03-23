using HPD.RAG.Core.Filters;
using HPD.RAG.Core.Providers.VectorStore;
using HPD.RAG.VectorStores.AzureAISearch;
using HPD.RAG.VectorStores.Tests.Shared;

namespace HPD.RAG.VectorStores.Tests.Backends;

public sealed class AzureAISearchVectorStoreTests : VectorStoreTestBase
{
    static AzureAISearchVectorStoreTests() => AzureAISearchVectorStoreModule.Initialize();

    // T-055
    [Fact]
    public void ModuleInitializer_RegistersProvider()
    {
        Assert.NotNull(VectorStoreDiscovery.GetProvider("azure-ai-search"));
    }

    // T-056
    [Fact]
    public void Initialize_Idempotent()
    {
        AzureAISearchVectorStoreModule.Initialize();
        AzureAISearchVectorStoreModule.Initialize();

        var count = VectorStoreDiscovery.GetRegisteredProviders().Count(k => k == "azure-ai-search");
        Assert.Equal(1, count);
    }

    // T-057
    [Fact]
    public void ProviderKey_IsCorrectString()
    {
        var features = VectorStoreDiscovery.GetProvider("azure-ai-search")!;
        Assert.Equal("azure-ai-search", features.ProviderKey);
    }

    // T-058: SearchIndexClient constructor accepts fake URI and API key without connecting.
    [Fact]
    public void CreateVectorStore_HappyPath()
    {
        var features = VectorStoreDiscovery.GetProvider("azure-ai-search")!;
        var config = new VectorStoreConfig
        {
            ProviderKey = "azure-ai-search",
            Endpoint = "https://fake-search.search.windows.net",
            ApiKey = "fake-api-key"
        };
        var store = features.CreateVectorStore(config);
        Assert.NotNull(store);
    }

    // T-059
    [Fact]
    public void CreateVectorStore_MissingEndpoint_Throws()
    {
        var features = VectorStoreDiscovery.GetProvider("azure-ai-search")!;
        var config = new VectorStoreConfig { ProviderKey = "azure-ai-search" };
        Assert.ThrowsAny<Exception>(() => features.CreateVectorStore(config));
    }

    // T-060
    [Fact]
    public void CreateFilterTranslator_ReturnsNonNull()
    {
        Assert.NotNull(VectorStoreDiscovery.GetProvider("azure-ai-search")!.CreateFilterTranslator());
    }

    // T-061: Azure AI Search returns an OData filter string with "eq"
    [Fact]
    public void Translate_EqFilter()
    {
        var translator = VectorStoreDiscovery.GetProvider("azure-ai-search")!.CreateFilterTranslator();
        var result = translator.Translate(EqFilter("category", "Technical"));
        Assert.NotNull(result);
        var odata = Assert.IsType<string>(result);
        Assert.Contains("eq", odata);
    }

    // T-062
    [Fact]
    public void Translate_TagPrefix()
    {
        var translator = VectorStoreDiscovery.GetProvider("azure-ai-search")!.CreateFilterTranslator();
        var result = translator.Translate(TagFilter("userId", "u123"));
        Assert.NotNull(result);
        var odata = Assert.IsType<string>(result);
        // Tags resolve to "tags/userId" in OData path syntax
        Assert.Contains("tags", odata);
    }

    // T-063
    [Fact]
    public void Translate_And()
    {
        var translator = VectorStoreDiscovery.GetProvider("azure-ai-search")!.CreateFilterTranslator();
        var result = translator.Translate(AndFilter(EqFilter("a", "1"), EqFilter("b", "2")));
        Assert.NotNull(result);
        var odata = Assert.IsType<string>(result);
        Assert.Contains("and", odata, StringComparison.OrdinalIgnoreCase);
    }

    // T-064
    [Fact]
    public void Translate_Or()
    {
        var translator = VectorStoreDiscovery.GetProvider("azure-ai-search")!.CreateFilterTranslator();
        var result = translator.Translate(OrFilter(EqFilter("a", "1"), EqFilter("b", "2")));
        Assert.NotNull(result);
        var odata = Assert.IsType<string>(result);
        Assert.Contains("or", odata, StringComparison.OrdinalIgnoreCase);
    }

    // T-065
    [Fact]
    public void Translate_Not()
    {
        var translator = VectorStoreDiscovery.GetProvider("azure-ai-search")!.CreateFilterTranslator();
        var result = translator.Translate(NotFilter(EqFilter("a", "b")));
        Assert.NotNull(result);
        var odata = Assert.IsType<string>(result);
        Assert.Contains("not", odata, StringComparison.OrdinalIgnoreCase);
    }

    // T-066
    [Theory]
    [InlineData("gt")]
    [InlineData("gte")]
    [InlineData("lt")]
    [InlineData("lte")]
    [InlineData("neq")]
    [InlineData("contains")]
    [InlineData("startswith")]
    public void Translate_AllComparisonOps(string op)
    {
        var translator = VectorStoreDiscovery.GetProvider("azure-ai-search")!.CreateFilterTranslator();
        var result = translator.Translate(FilterForOp(op));
        Assert.NotNull(result);
    }

    // T-067
    [Fact]
    public void Translate_Null_ReturnsNull()
    {
        var translator = VectorStoreDiscovery.GetProvider("azure-ai-search")!.CreateFilterTranslator();
        Assert.Null(translator.Translate(null!));
    }
}
