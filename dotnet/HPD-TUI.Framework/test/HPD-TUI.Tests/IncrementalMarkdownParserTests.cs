using HPD.TUI.Markdown;
using HPD.TUI.Observability;

namespace HPD.TUI.Tests;

public sealed class IncrementalMarkdownParserTests
{
    [Fact]
    public void Append_ReusesStablePrefix_AndMatchesFullParseBlocks()
    {
        var options = new MarkdownParseOptions { Pipeline = MarkdownPipelineFactory.CreateDefault() };
        var incremental = new ConservativeIncrementalMarkdownParser();
        var state = incremental.ParseInitial("first\n\nsecond\n\nthird".AsMemory(), options);

        state = incremental.Append(state, " continues\n".AsMemory(), terminal: false);
        var full = new MarkdownDocumentParser().Parse("first\n\nsecond\n\nthird continues\n", options);

        Assert.Equal(full.Source, state.Document.Source);
        Assert.Equal(full.Blocks.Select(static block => (block.SourceStart, block.SourceEndExclusive, block.Kind)),
            state.Document.Blocks.Select(static block => (block.SourceStart, block.SourceEndExclusive, block.Kind)));
        Assert.True(state.StablePrefixNodes > 0);
        Assert.True(state.ReparsedCharacters < state.Document.Source.Length * 2L);
    }

    [Fact]
    public void Append_ReferenceDefinition_UsesConservativeFullFallback()
    {
        var parser = new ConservativeIncrementalMarkdownParser();
        var state = parser.ParseInitial("[link][id]\n\nbody\n".AsMemory(),
            new MarkdownParseOptions { Pipeline = MarkdownPipelineFactory.CreateDefault() });

        state = parser.Append(state, "\n[id]: https://example.test\n".AsMemory(), terminal: true);

        Assert.Equal(1, state.FallbackCount);
        Assert.True(state.Document.Features.HasFlag(MarkdownDocumentFeatures.ReferenceDefinitions));
    }

    [Fact]
    public void StableBoundaryAdvancement_ParsesOnlyBoundedSuffixes()
    {
        var inner = new MarkdownDocumentParser();
        var recording = new RecordingParser(inner);
        var parser = new ConservativeIncrementalMarkdownParser(recording);
        var options = new MarkdownParseOptions { Pipeline = MarkdownPipelineFactory.CreateDefault() };
        var state = parser.ParseInitial(ReadOnlyMemory<char>.Empty, options);

        for (var index = 0; index < 512; index++)
            state = parser.Append(state, $"paragraph {index}\n\n".AsMemory(), terminal: false);

        Assert.Equal(0, state.FallbackCount);
        Assert.True(state.StablePrefixNodes >= 510);
        Assert.True(recording.MaximumSourceLength < 64,
            $"A streaming parse unexpectedly consumed {recording.MaximumSourceLength} characters.");
        Assert.True(state.ReparsedCharacters < state.Document.SourceLength * 4L);
    }

    [Fact]
    public void PersistentSource_TailSliceAllocationDoesNotScaleWithAppendCount()
    {
        var source = MarkdownSourceText.Empty;
        for (var index = 0; index < 16_384; index++) source = source.Append("x".AsMemory());

        _ = source.Slice(source.Length - 32, 32);
        var before = GC.GetAllocatedBytesForCurrentThread();
        var tail = source.Slice(source.Length - 32, 32);
        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        Assert.Equal(new string('x', 32), tail);
        Assert.True(allocated < 512, $"Tail slicing allocated {allocated} bytes.");
    }

    [Fact]
    public void PersistentSource_DoesNotRetainWholeMaterializations()
    {
        var source = MarkdownSourceText.Empty.Append(new string('x', 4096).AsMemory());
        _ = source.Materialize();
        var before = GC.GetAllocatedBytesForCurrentThread();
        _ = source.Materialize();
        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        Assert.True(allocated >= source.Length * sizeof(char));
    }

    [Fact]
    public void InitialParse_PreservesCallerSourceIdentity_WithoutCachingAppendedMaterialization()
    {
        var original = new string("cold source".AsSpan());
        var parser = new ConservativeIncrementalMarkdownParser();
        var options = new MarkdownParseOptions { Pipeline = MarkdownPipelineFactory.CreateDefault() };
        var initial = parser.ParseInitial(original.AsMemory(), options);

        Assert.Same(original, initial.Document.Source);

        var appended = parser.Append(initial, "\n\ntail".AsMemory(), terminal: false);
        var first = appended.Document.Source;
        var second = appended.Document.Source;
        Assert.Equal(first, second);
        Assert.NotSame(first, second);
    }

    [Fact]
    public void CommonCounters_RecordActualIncrementalParserWork()
    {
        var counters = new TuiPerformanceCounters();
        var parser = new ConservativeIncrementalMarkdownParser(new MarkdownDocumentParser(), counters);
        var options = new MarkdownParseOptions { Pipeline = MarkdownPipelineFactory.CreateDefault() };
        var state = parser.ParseInitial("first\n\nsecond\n\nthird".AsMemory(), options);
        var initial = counters.Snapshot();

        state = parser.Append(state, " continues\n".AsMemory(), terminal: false);
        var snapshot = counters.Snapshot();

        Assert.Equal(initial.MarkdownCharactersReparsed +
            state.ReparsedCharacters - "first\n\nsecond\n\nthird".Length,
            snapshot.MarkdownCharactersReparsed);
        Assert.Equal(state.StablePrefixNodes, snapshot.MarkdownStablePrefixNodesReused);
    }

    private sealed class RecordingParser(IMarkdownDocumentParser inner) : IMarkdownDocumentParser
    {
        public int MaximumSourceLength { get; private set; }

        public MarkdownDocumentSnapshot Parse(string source, MarkdownParseOptions options)
        {
            MaximumSourceLength = Math.Max(MaximumSourceLength, source.Length);
            return inner.Parse(source, options);
        }
    }
}
