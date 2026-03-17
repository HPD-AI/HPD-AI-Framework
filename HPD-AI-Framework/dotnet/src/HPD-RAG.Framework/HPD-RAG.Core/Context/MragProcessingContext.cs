namespace HPD.RAG.Core.Context;

/// <summary>
/// Thin wrapper over MragPipelineContext exposed to Tier 1 (IMragProcessor) and
/// Tier 1.5 (IMragRouter) custom nodes. Provides pipeline metadata without
/// exposing HPD.Graph internals.
/// </summary>
public sealed class MragProcessingContext
{
    private readonly MragPipelineContext _inner;

    internal MragProcessingContext(MragPipelineContext inner)
    {
        _inner = inner;
    }

    public string PipelineName => _inner.PipelineName;
    public string? CollectionName => _inner.CollectionName;
    public IReadOnlyDictionary<string, string>? RunTags => _inner.RunTags;
    public string? CorpusVersion => _inner.CorpusVersion;
    public string ExecutionId => _inner.ExecutionId;
}
