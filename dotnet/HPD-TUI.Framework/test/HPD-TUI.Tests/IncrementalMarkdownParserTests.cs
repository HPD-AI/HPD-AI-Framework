using HPD.TUI.Markdown;

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
}
