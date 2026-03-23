using Microsoft.Extensions.AI;

namespace HPD.RAG.IntegrationTests.Fakes;

internal sealed class FakeEmbeddingGenerator : IEmbeddingGenerator<string, Embedding<float>>
{
    private readonly int _dimensions;

    public FakeEmbeddingGenerator(int dimensions = 384)
    {
        _dimensions = dimensions;
    }

    public Task<GeneratedEmbeddings<Embedding<float>>> GenerateAsync(
        IEnumerable<string> values,
        EmbeddingGenerationOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        var embeddings = values
            .Select(_ => new Embedding<float>(
                Enumerable.Range(0, _dimensions).Select(i => (float)i / _dimensions).ToArray()))
            .ToList();
        return Task.FromResult(new GeneratedEmbeddings<Embedding<float>>(embeddings));
    }

    public EmbeddingGeneratorMetadata Metadata => new("fake", null, null, _dimensions);

    public void Dispose() { }

    public object? GetService(Type serviceType, object? serviceKey = null) => null;

    public TService? GetService<TService>(object? serviceKey = null) where TService : class => null;
}
