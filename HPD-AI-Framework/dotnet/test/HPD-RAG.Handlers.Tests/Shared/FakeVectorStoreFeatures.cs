using HPD.RAG.Core.Filters;
using HPD.RAG.Core.Providers.VectorStore;
using Microsoft.Extensions.VectorData;

namespace HPD.RAG.Handlers.Tests.Shared;

/// <summary>
/// Test-only IVectorStoreFeatures implementation.
/// Returns a passthrough filter translator whose Translate() result cannot be cast to
/// VectorSearchFilter — VectorSearchHandler silently leaves vsf = null, so searches
/// run unfiltered against InMemoryVectorStore. This is correct for unit-test purposes:
/// we verify the handler executes end-to-end without throwing, not that the backend
/// applies the predicate (that is exercised in integration tests).
/// </summary>
internal sealed class FakeVectorStoreFeatures : IVectorStoreFeatures
{
    public string ProviderKey => "fake";
    public string DisplayName => "Fake (Test)";

    public VectorStore CreateVectorStore(VectorStoreConfig config, IServiceProvider? services = null)
        => throw new NotSupportedException("FakeVectorStoreFeatures.CreateVectorStore is not used in unit tests.");

    public IMragFilterTranslator CreateFilterTranslator() => new FakeFilterTranslator();

    private sealed class FakeFilterTranslator : IMragFilterTranslator
    {
        /// <summary>
        /// Returns an opaque object that is NOT a VectorSearchFilter.
        /// VectorSearchHandler casts with "as VectorSearchFilter" — the null result
        /// means the search runs without an OldFilter, which is correct for InMemory.
        /// </summary>
        public object? Translate(MragFilterNode? node) => node is null ? null : new object();
    }
}
