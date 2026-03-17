namespace HPD.RAG.Core.Events;

/// <summary>
/// Base type for all events emitted by MRAG pipeline streaming methods.
/// SA-10 will extend this hierarchy with fully typed event subtypes per pipeline kind.
/// For now the only concrete public subtype is <see cref="MragRawGraphEvent"/> which
/// passes underlying HPD.Graph events through without transformation.
/// </summary>
public abstract record MragEvent
{
    /// <summary>Name of the pipeline that emitted this event.</summary>
    public required string PipelineName { get; init; }
}

/// <summary>
/// Passthrough wrapper that surfaces an underlying HPD.Graph or HPD.Events <c>Event</c>
/// without any MRAG-specific transformation.
/// SA-10 will progressively replace usages of this type with strongly-typed MRAG events
/// (e.g. <c>IngestionStartedEvent</c>, <c>ChunkWrittenEvent</c>, etc.) during M5.
/// </summary>
public sealed record MragRawGraphEvent : MragEvent
{
    /// <summary>The raw HPD.Events or HPD.Graph event emitted by the orchestrator.</summary>
    public required object UnderlyingEvent { get; init; }
}
