using FluentAssertions;
using HPD.Agent.TUI.Composition;
using HPD.Agent.TUI.Models;
using HPD.Agent.TUI.Views;
using HPD.TUI.Components;
using HPD.TUI.Core;
using HPD.TUI.Rendering;

namespace HPD.Agent.TUI.Tests;

public sealed class TranscriptViewTests
{
    [Fact]
    public void Render_ShowsTranscriptTailEntries()
    {
        var model = new TranscriptModel();
        model.AddFinal(Row("final-user", "user", "already committed"));
        model.UpsertLive(Row("live-assistant", "assistant:live", "still streaming"));

        var view = CreateView(model, height: 6);

        var rendered = TuiCapture.RenderToString(view, width: 80, height: 8, trimTrailingBlankLines: true);

        rendered.Should().Contain("still streaming");
        rendered.Should().Contain("already committed");
    }

    [Fact]
    public void Render_UsesLatestLiveEntryVersion()
    {
        var model = new TranscriptModel();
        model.UpsertLive(Row("assistant-1", "assistant:1", "first draft"));
        model.UpsertLive(Row("assistant-2", "assistant:1", "second draft"));

        var view = CreateView(model, height: 6);

        var rendered = TuiCapture.RenderToString(view, width: 80, height: 8, trimTrailingBlankLines: true);

        rendered.Should().Contain("second draft");
        rendered.Should().NotContain("first draft");
    }

    [Fact]
    public void Render_FinalizedLiveEntryStaysInTailViewport()
    {
        var model = new TranscriptModel();
        model.UpsertLive(Row("assistant-1", "assistant:1", "streaming"));
        model.FinalizeLive("assistant:1", Row("assistant-1", "assistant:1", "done"));

        var view = CreateView(model, height: 6);

        var rendered = TuiCapture.RenderToString(view, width: 80, height: 8, trimTrailingBlankLines: true);

        rendered.Should().NotContain("streaming");
        rendered.Should().Contain("done");
    }

    [Fact]
    public void Render_RunStatusCell_ShowsStateAndDuration()
    {
        var model = new TranscriptModel();
        model.UpsertLive(new TranscriptEntry(
            Id: "run-run-123456789",
            EntryKey: "run:run-123456789",
            Cell: new RunStatusCell(
                "run-123456789",
                TranscriptRunState.Cancelled,
                Duration: TimeSpan.FromSeconds(2.4)),
            Metadata: new TranscriptEntryMetadata()));

        var view = CreateView(model, height: 4);

        var rendered = TuiCapture.RenderToString(view, width: 80, height: 6, trimTrailingBlankLines: true);

        rendered.Should().Contain("cancelled");
        rendered.Should().NotContain("run-1234");
        rendered.Should().Contain("2.4s");
    }

    [Fact]
    public void Render_RunStatusCell_UsesRegisteredRenderer()
    {
        var registry = TuiTestBuilder.Create()
            .AddDefaultTranscriptRenderers()
            .ReplaceTranscriptRenderer<RunStatusCell>(
                AgentTuiTranscriptRendererKeys.RunStatus,
                context => new Text($"run {context.Cell.RuntimeRunId}"))
            .Build();
        var model = new TranscriptModel();
        model.UpsertLive(new TranscriptEntry(
            Id: "run-run-123456789",
            EntryKey: "run:run-123456789",
            Cell: new RunStatusCell("run-123456789", TranscriptRunState.Completed),
            Metadata: new TranscriptEntryMetadata()));

        var view = new TranscriptView(model, registry.TranscriptRenderers, height: 4);

        var rendered = TuiCapture.RenderToString(view, width: 80, height: 6, trimTrailingBlankLines: true);

        rendered.Should().Contain("run run-123456789");
    }

    [Fact]
    public void Render_RunStatusCell_CompletedUsesMutedStyle()
    {
        var model = new TranscriptModel();
        model.UpsertLive(new TranscriptEntry(
            Id: "run-run-123456789",
            EntryKey: "run:run-123456789",
            Cell: new RunStatusCell("run-123456789", TranscriptRunState.Completed),
            Metadata: new TranscriptEntryMetadata()));

        var view = CreateView(model, height: 4);

        using var grid = TuiCapture.RenderToGrid(view, width: 80, height: 6);

        grid.GetCell(0, 0).Style.Foreground.Should().Be(Color.Gray);
    }

    [Fact]
    public void Render_UnknownTranscriptCell_UsesGracefulFallback()
    {
        var model = new TranscriptModel();
        model.UpsertLive(new TranscriptEntry(
            Id: "unknown",
            EntryKey: "unknown:1",
            Cell: new UnknownTranscriptCell(),
            Metadata: new TranscriptEntryMetadata()));

        var view = CreateView(model, height: 4);

        var rendered = TuiCapture.RenderToString(view, width: 80, height: 6, trimTrailingBlankLines: true);

        rendered.Should().Contain(nameof(UnknownTranscriptCell));
    }

    private static TranscriptView CreateView(TranscriptModel model, int height)
        => new(
            model,
            TuiTestBuilder.Create()
                .AddDefaultTranscriptRenderers()
                .Build()
                .TranscriptRenderers,
            height);

    private static TranscriptEntry Row(string id, string? entryKey, string text)
        => new(
            id,
            entryKey,
            id.Contains("user", StringComparison.Ordinal)
                ? new UserMessageCell(new Markdown(text))
                : new AssistantMessageCell("assistant", new Markdown(text)),
            new TranscriptEntryMetadata());

    private sealed record UnknownTranscriptCell : TranscriptCell;
}
