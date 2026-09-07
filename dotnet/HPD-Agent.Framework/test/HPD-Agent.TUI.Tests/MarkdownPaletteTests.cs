using HPD.Agent.TUI.Markdown;
using HPD.Agent.TUI.Models;
using HPD.Agent.TUI.Views;
using HPD.TUI.Core;
using HPD.TUI.Markdown;
using HPD.TUI.Rendering;

namespace HPD.Agent.TUI.Tests;

public sealed class MarkdownPaletteTests
{
    private static readonly Style Red = new(new Color(240, 40, 50), Color.Default);
    private static readonly Style Green = new(new Color(20, 220, 90), Color.Default);

    [Fact]
    public void PreparedAndBlockCachesIncludeTheActualPalette()
    {
        var session = new MarkdownStreamSession(new(MarkdownStreamKind.Assistant, "palette-cache"));
        session.Append("# heading\n\nparagraph\n\nnext");
        var document = session.Refresh().Document;
        var first = MarkdownTheme.FromTheme(Theme.Default) with { Heading1 = Red };
        var second = first with { Heading1 = Green };
        var engine = new MarkdownLayoutEngine();
        var red = session.Projection.Prepare(document, new(80, first), engine);
        var green = session.Projection.Prepare(document, new(80, second), engine);
        Assert.NotEqual(red.Key, green.Key);
        Assert.Contains(red.Rows.SelectMany(row => row.Line.Runs), run => run.Text == "heading" && run.Style == Red);
        Assert.Contains(green.Rows.SelectMany(row => row.Line.Runs), run => run.Text == "heading" && run.Style == Green);
        Assert.Same(red, session.Projection.RequirePrepared(document.Revision, red.Key));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ConfiguredPaletteSurvivesStreamingFinalizationAndScrollback(bool reasoning)
    {
        var response = MarkdownTheme.FromTheme(Theme.Default) with { Heading1 = Red, Body = Red };
        var thoughts = response with { Heading1 = Green, Body = Green };
        var registry = new HpdAgentTuiBuilder().AddAgentTuiDefaults()
            .UseMarkdownTheme(response).UseReasoningMarkdownTheme(thoughts)
            .UseTranscriptHistoryPresentation(TranscriptHistoryPresentation.TerminalScrollback).Build();
        var palette = registry.TranscriptRenderers.Services.ResolveMarkdownTheme(Theme.Default, reasoning);
        Assert.Same(reasoning ? thoughts : response, palette);
        var session = new MarkdownStreamSession(new(reasoning ? MarkdownStreamKind.Reasoning : MarkdownStreamKind.Assistant, "stream"));
        session.Append("# stable\n\nmutable");
        var document = session.Refresh().Document;
        var options = new MarkdownLayoutOptions(reasoning ? 78 : 80, palette);
        session.Projection.Prepare(document, options, new MarkdownLayoutEngine());
        TranscriptCell Cell(MarkdownMessageDocument value) => reasoning
            ? new ReasoningMessageCell(value, session.Projection)
            : new AssistantMessageCell(null, value, session.Projection);
        var entry = new TranscriptEntry("stream", "stream", Cell(document), new TranscriptEntryMetadata(), VerticalSpacing: 0);
        var model = new TranscriptModel { HistoryPresentation = TranscriptHistoryPresentation.TerminalScrollback };
        model.UpsertLive(entry, CommittedHistoryMutationPolicy.Reject);
        var view = new TranscriptView(model, registry.TranscriptRenderers, 8);
        var context = new RenderContext(80, 8, Theme.Default);
        var first = view.PrepareScrollback(in context, 64)!;
        var heading = Assert.Single(first.Rows.Where(row => string.Concat(row.Cells.Select(cell => cell.Grapheme)).Contains("stable")));
        Assert.All(heading.Cells.Where(cell => cell.Grapheme != " "), cell => Assert.Equal(palette.Heading1.Foreground, cell.Style.Foreground));
        view.CommitScrollback(first);
        session.Append(" finished");
        document = session.Complete().Document;
        session.Projection.Prepare(document, options, new MarkdownLayoutEngine());
        model.FinalizeLive("stream", entry with { Cell = Cell(document) }, CommittedHistoryMutationPolicy.Reject);
        var last = view.PrepareScrollback(in context, 64)!;
        var body = Assert.Single(last.Rows.Where(row => string.Concat(row.Cells.Select(cell => cell.Grapheme)).Contains("mutable finished")));
        Assert.All(body.Cells.Where(cell => cell.Grapheme != " "), cell => Assert.Equal(palette.Body.Foreground, cell.Style.Foreground));
        view.CommitScrollback(last);
        Assert.Equal(1, model.CommittedCount);
    }
}
