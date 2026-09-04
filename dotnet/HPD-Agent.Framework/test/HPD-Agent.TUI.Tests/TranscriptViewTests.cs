using FluentAssertions;
using HPD.Agent.TUI.Composition;
using HPD.Agent.TUI.Models;
using HPD.Agent.TUI.Views;
using HPD.TUI.Components;
using HPD.TUI.Core;
using HPD.TUI.Rendering;
using HPD.Agent.TUI.Markdown;
using HPD.TUI.Markdown;

namespace HPD.Agent.TUI.Tests;

public sealed class TranscriptViewTests
{
    [Fact]
    public void TranscriptPagingPersistsAcrossRecaptureAndClipboardTracksVisiblePage()
    {
        var source = string.Concat(Enumerable.Range(0, 8_200).Select(static index => $"item-{index:D5}\n\n"));
        var session = new MarkdownStreamSession(new(MarkdownStreamKind.Assistant, "paged"));
        session.Append(source);
        var document = session.Complete().Document;
        var options = new MarkdownLayoutOptions(40, MarkdownTheme.FromTheme(Theme.Default));
        _ = session.Projection.Prepare(document, options, new MarkdownLayoutEngine());
        var model = new TranscriptModel();
        model.AddFinal(new TranscriptEntry("paged-entry", "assistant:paged",
            new AssistantMessageCell("assistant", document, session.Projection), new()));
        var view = CreateView(model, height: 5);

        var first = TuiCapture.RenderToString(view, 40, 5, trimTrailingBlankLines: true);
        view.HandleInput(new KeyEvent(KeyCode.PageDown)).Should().BeTrue("handled input requests repaint");
        var second = TuiCapture.RenderToString(view, 40, 5, trimTrailingBlankLines: true);
        var recaptured = TuiCapture.RenderToString(view, 40, 5, trimTrailingBlankLines: true);

        second.Should().Be(recaptured);
        second.Should().Contain("item-08199");
        first.Should().NotContain("item-08199");
        view.TryGetSemanticClipboardText("paged-entry", new(0, 0, 100, 80), out var copied).Should().BeTrue();
        copied.Should().Contain("item-08192").And.Contain("item-08199");
        view.HandleInput(new KeyEvent(KeyCode.PageUp)).Should().BeTrue();
        var restored = TuiCapture.RenderToString(view, 40, 5, trimTrailingBlankLines: true);
        restored.Should().Be(first);
    }

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
    public void Render_AssistantMessageCell_DoesNotRepeatAgentNameHeading()
    {
        var model = new TranscriptModel();
        model.AddFinal(new TranscriptEntry(
            Id: "assistant-1",
            EntryKey: "assistant:1",
            Cell: HPD.Agent.TUI.Markdown.MarkdownMessageFactory.CreateAssistant("test-assistant", "hello", 80, Theme.Default, "assistant"),
            Metadata: new TranscriptEntryMetadata(AgentName: "assistant")));

        var view = CreateView(model, height: 4);

        var rendered = TuiCapture.RenderToString(view, width: 80, height: 6, trimTrailingBlankLines: true);

        rendered.Should().Contain("hello");
        rendered.Should().NotContain("assistant\n");
        view.TryGetSemanticClipboardText("assistant-1", new(0, 0, 0, 80), out var copied).Should().BeTrue();
        copied.Should().Be("hello");
    }

    [Fact]
    public void Render_RunStatusCell_ShowsFailureStateAndDuration()
    {
        var model = new TranscriptModel();
        model.UpsertLive(new TranscriptEntry(
            Id: "run-run-123456789",
            EntryKey: "run:run-123456789",
            Cell: new RunStatusCell(
                "run-123456789",
                TranscriptRunState.Failed,
                Duration: TimeSpan.FromSeconds(2.4)),
            Metadata: new TranscriptEntryMetadata()));

        var view = CreateView(model, height: 4);

        var rendered = TuiCapture.RenderToString(view, width: 80, height: 6, trimTrailingBlankLines: true);

        rendered.Should().Contain("failed");
        rendered.Should().NotContain("run-1234");
        rendered.Should().Contain("2.4s");
    }

    [Fact]
    public void Render_RunStatusCell_UsesRegisteredRenderer()
    {
        var registry = new HpdAgentTuiBuilder()
            .AddDefaultTranscriptRenderers()
            .ReplaceTranscriptRenderer<RunStatusCell>(
                AgentTuiTranscriptRendererKeys.RunStatus,
                context => new Text($"run {context.Cell.ThreadExecutionId}"))
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

    [Fact]
    public void HandleInput_PageUpAndPageDown_NavigateLoadedHistory()
    {
        var model = new TranscriptModel();
        for (var i = 0; i < 8; i++)
        {
            model.AddFinal(Row($"row-{i}", $"row:{i}", $"row {i}"));
        }

        var view = CreateView(model, height: 3);

        Render(view).Should().Contain("row 7").And.NotContain("row 1");

        view.HandleInput(new KeyEvent(KeyCode.PageUp)).Should().BeTrue();
        Render(view).Should().Contain("row 6").And.NotContain("row 7");

        view.HandleInput(new KeyEvent(KeyCode.PageDown)).Should().BeTrue();
        Render(view).Should().Contain("row 7").And.NotContain("row 6");
    }

    [Fact]
    public void Scrollback_TerminalMode_PreparesFinalPrefixAndRemovesItFromLiveProjection()
    {
        var model = new TranscriptModel
        {
            HistoryPresentation = TranscriptHistoryPresentation.TerminalScrollback
        };
        for (var i = 0; i < 8; i++)
            model.AddFinal(Row($"row-{i}", $"row:{i}", $"row {i}"));

        var view = CreateView(model, height: 3);
        var context = new RenderContext(80, 3, Theme.Default);
        var batch = view.PrepareScrollback(in context, 64);
        var rendered = TuiCapture.RenderToString(view, width: 80, height: 3, trimTrailingBlankLines: true);

        batch.Should().NotBeNull();
        batch!.Rows.SelectMany(static row => row.Cells).Select(static cell => cell.Grapheme)
            .Should().Contain("r");
        rendered.Should().NotContain("row 0").And.NotContain("row 7");
        view.CommitScrollback(batch);
        model.CommittedCount.Should().Be(8);
        view.HandleInput(new KeyEvent(KeyCode.PageUp)).Should().BeFalse();
    }

    [Fact]
    public void Render_Viewport_DoesNotEmitHistoryBeyondViewportHeight()
    {
        var model = new TranscriptModel
        {
            HistoryPresentation = TranscriptHistoryPresentation.Viewport
        };
        for (var i = 0; i < 8; i++)
            model.AddFinal(Row($"row-{i}", $"row:{i}", $"row {i}"));

        var view = CreateView(model, height: 3);
        var rendered = TuiCapture.RenderToString(
            view, width: 80, height: 20, trimTrailingBlankLines: true);

        rendered.Should().Contain("row 7").And.NotContain("row 0");
    }

    [Fact]
    public void HandleInput_RepeatedPaging_ClampsToHistoryBoundaries()
    {
        var model = new TranscriptModel();
        for (var i = 0; i < 8; i++)
        {
            model.AddFinal(Row($"row-{i}", $"row:{i}", $"row {i}"));
        }

        var view = CreateView(model, height: 3);

        for (var i = 0; i < 20; i++)
        {
            view.HandleInput(new KeyEvent(KeyCode.PageUp)).Should().BeTrue();
        }

        Render(view).Should().Contain("row 0").And.NotContain("row 7");
        view.ScrollOffset.Should().BeGreaterThan(0);

        for (var i = 0; i < 20; i++)
        {
            view.HandleInput(new KeyEvent(KeyCode.PageDown)).Should().BeTrue();
        }

        Render(view).Should().Contain("row 7").And.NotContain("row 0");
        view.ScrollOffset.Should().Be(0);
    }

    [Fact]
    public void HandleInput_PlainHome_RemainsAvailableToPrompt()
    {
        var view = CreateView(new TranscriptModel(), height: 3);

        view.HandleInput(new KeyEvent(KeyCode.Home)).Should().BeFalse();
    }

    [Fact]
    public void Render_SingleEntryLongerThanLegacyCaptureLimit_IsFullyScrollable()
    {
        const int lineCount = 16_390;
        var model = new TranscriptModel();
        model.AddFinal(Row("long", "long:1", string.Join('\n', Enumerable.Range(0, lineCount).Select(i => $"line {i}"))));
        var view = CreateView(model, height: 4);

        Render(view).Should().Contain($"line {lineCount - 1}");
        for (var i = 0; i < lineCount / view.Height + 1; i++)
        {
            view.HandleInput(new KeyEvent(KeyCode.PageUp));
        }

        Render(view).Should().Contain("line 0");
    }

    private static TranscriptView CreateView(TranscriptModel model, int height)
        => new(
            model,
            new HpdAgentTuiBuilder()
                .AddDefaultTranscriptRenderers()
                .Build()
                .TranscriptRenderers,
            height);

    private static string Render(TranscriptView view)
        => TuiCapture.RenderToString(
            view,
            width: 80,
            height: view.Height,
            trimTrailingBlankLines: true);

    private static TranscriptEntry Row(string id, string? entryKey, string text)
        => new(
            id,
            entryKey,
            id.Contains("user", StringComparison.Ordinal)
                ? new UserMessageCell(HPD.TUI.Content.TextBlock.Create(text))
                : HPD.Agent.TUI.Markdown.MarkdownMessageFactory.CreateAssistant(id, text, 80, Theme.Default, "assistant"),
            new TranscriptEntryMetadata());

    private sealed record UnknownTranscriptCell : TranscriptCell;
}
