using HPD.RAG.Core.Events;
using Xunit;

namespace HPD.RAG.IntegrationTests.Tests;

/// <summary>
/// Group 4: Event type tests — verify construction, required properties, and the record type hierarchy.
/// </summary>
public sealed class MragEventTypesTests
{
    // T-038
    [Fact]
    public void IngestionStartedEvent_HasPipelineName()
    {
        var evt = new IngestionStartedEvent
        {
            PipelineName = "test-pipeline",
            DocumentCount = 5,
            CollectionName = "docs"
        };

        Assert.Equal("test-pipeline", evt.PipelineName);
        Assert.Equal(5, evt.DocumentCount);
        Assert.Equal("docs", evt.CollectionName);
    }

    // T-039
    [Fact]
    public void IngestionCompletedEvent_HasRequiredProperties()
    {
        var evt = new IngestionCompletedEvent
        {
            PipelineName = "test-pipeline",
            DocumentCount = 3,
            WrittenChunks = 12,
            Duration = TimeSpan.FromSeconds(4.2)
        };

        Assert.Equal("test-pipeline", evt.PipelineName);
        Assert.Equal(3, evt.DocumentCount);
        Assert.Equal(12, evt.WrittenChunks);
        Assert.Equal(TimeSpan.FromSeconds(4.2), evt.Duration);
    }

    // T-040
    [Fact]
    public void DocumentReadEvent_HasDocumentIdAndElementCount()
    {
        var evt = new DocumentReadEvent
        {
            PipelineName = "test-pipeline",
            DocumentId = "/docs/readme.md",
            ElementCount = 7
        };

        Assert.Equal("/docs/readme.md", evt.DocumentId);
        Assert.Equal(7, evt.ElementCount);
    }

    // T-041
    [Fact]
    public void DocumentSkippedEvent_KindIsDiagnostic()
    {
        var evt = new DocumentSkippedEvent
        {
            PipelineName = "test-pipeline",
            DocumentId = "/docs/readme.md",
            Reason = "unchanged"
        };

        Assert.Equal(HPD.Events.EventKind.Diagnostic, evt.Kind);
        Assert.Equal("unchanged", evt.Reason);
    }

    // T-042
    [Fact]
    public void DocumentFailedEvent_HasException()
    {
        var exception = new InvalidOperationException("handler exploded");
        var evt = new DocumentFailedEvent
        {
            PipelineName = "test-pipeline",
            DocumentId = "/docs/readme.md",
            NodeId = "read-node",
            Exception = exception
        };

        Assert.Same(exception, evt.Exception);
        Assert.Equal("read-node", evt.NodeId);
    }

    // T-043
    [Fact]
    public void RetrievalStartedEvent_HasQuery()
    {
        var evt = new RetrievalStartedEvent
        {
            PipelineName = "retrieval-v1",
            Query = "what is the capital of France?"
        };

        Assert.Equal("what is the capital of France?", evt.Query);
    }

    // T-044
    [Fact]
    public void EvalPartitionCompletedEvent_HasScores()
    {
        var scores = new Dictionary<string, double>
        {
            ["Relevance"] = 0.85,
            ["Groundedness"] = 0.92
        };

        var evt = new EvalPartitionCompletedEvent
        {
            PipelineName = "eval-v1",
            ScenarioName = "scenario-1",
            IterationName = "iteration-0",
            Scores = scores
        };

        Assert.Equal(0.85, evt.Scores["Relevance"]);
        Assert.Equal(0.92, evt.Scores["Groundedness"]);
    }

    // T-045
    [Fact]
    public void MragRawGraphEvent_HasUnderlyingEvent()
    {
        var raw = new object();
        var evt = new MragRawGraphEvent
        {
            PipelineName = "test-pipeline",
            UnderlyingEvent = raw
        };

        Assert.Same(raw, evt.UnderlyingEvent);
    }

    // T-046
    [Fact]
    public void AllMragEventSubtypes_AreRecords()
    {
        // Collect all concrete subtypes of MragEvent via reflection
        var mragEventType = typeof(MragEvent);
        var assembly = mragEventType.Assembly;

        var subtypes = assembly.GetTypes()
            .Where(t => t != mragEventType && mragEventType.IsAssignableFrom(t) && !t.IsAbstract)
            .ToList();

        Assert.NotEmpty(subtypes);

        foreach (var subtype in subtypes)
        {
            // Records implement IEquatable<T> — this is the canonical way to detect a record type
            // in .NET reflection (the compiler generates it).
            var isRecord = subtype.GetMethods()
                .Any(m => m.Name == "<Clone>$" || m.Name == "op_Inequality");
            // Alternative: check for the synthesized EqualityContract property
            var hasEqualityContract = subtype.GetProperty("EqualityContract",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance) != null;

            Assert.True(isRecord || hasEqualityContract,
                $"Type {subtype.FullName} is expected to be a record but does not appear to be one.");
        }
    }
}
