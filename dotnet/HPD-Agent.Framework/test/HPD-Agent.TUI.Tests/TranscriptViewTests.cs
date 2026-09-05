using FluentAssertions;
using HPD.Agent.TUI.Composition;
using HPD.Agent.TUI.Models;
using HPD.Agent.TUI.Views;
using HPD.TUI.Components;
using HPD.TUI.Core;
using HPD.TUI.Rendering;
using HPD.TUI.Terminal;
using HPD.Agent.TUI.Markdown;
using HPD.TUI.Markdown;
using HPD.TUI.Observability;

namespace HPD.Agent.TUI.Tests;

public sealed class TranscriptViewTests
{
    [Fact]
    public void ViewportMutations_TrackHeightAsLayoutAndPagingAsPaint()
    {
        var model = new TranscriptModel();
        model.AddFinal(Row("revision-user", "revision", string.Join('\n', Enumerable.Repeat("row", 20))));
        var view = CreateView(model, height: 4);
        var layout = view.LayoutRevision;
        var paint = view.PaintRevision;

        view.SetHeight(5);

        view.Height.Should().Be(5);
        view.LayoutRevision.Should().NotBe(layout);
        view.PaintRevision.Should().NotBe(paint);
        layout = view.LayoutRevision;
        paint = view.PaintRevision;

        view.HandleInput(new KeyEvent(KeyCode.PageUp)).Should().BeTrue();

        view.LayoutRevision.Should().Be(layout);
        view.PaintRevision.Should().NotBe(paint);
    }

    [Fact]
    public void Render_ReusesPreparedEntryThroughTranscriptLayoutCache()
    {
        var model = new TranscriptModel();
        model.AddFinal(Row("cached-user", "cached", "retained row"));
        var view = CreateView(model, height: 4);

        _ = TuiCapture.RenderToString(view, 40, 4);
        var first = view.LastDiagnostics;
        _ = TuiCapture.RenderToString(view, 40, 4);
        var second = view.LastDiagnostics;

        first.CacheMisses.Should().Be(1);
        second.CacheHits.Should().Be(1);
        second.CacheMisses.Should().Be(0);
    }

    [Fact]
    public void Render_CachePressureRetainsEveryRasterUntilProjectionCompletes()
    {
        const int width = 40;
        using var sizingGrid = new TerminalGrid(width, 1);
        var model = new TranscriptModel
        {
            HistoryPresentation = TranscriptHistoryPresentation.TerminalScrollback
        };
        model.AddFinal(Row("first-user", "first", "first"));
        model.AddFinal(Row("second-user", "second", "second"));
        model.AddFinal(Row("third-user", "third", "third"));
        var counters = new TuiPerformanceCounters();
        var view = new TranscriptView(
            model,
            new HpdAgentTuiBuilder().AddDefaultTranscriptRenderers().Build().TranscriptRenderers,
            height: 12,
            cacheByteBudget: sizingGrid.EstimatedByteSize * 2,
            performanceCounters: counters);

        _ = TuiCapture.RenderToString(view, width, 12);
        var firstPass = counters.Snapshot();
        firstPass.SurfaceCacheEvictions.Should().BeGreaterThan(0,
            $"the cache retained {firstPass.SurfaceCacheBytes} bytes under a {sizingGrid.EstimatedByteSize * 2}-byte budget");
        var context = new RenderContext(width, 12, Theme.Default);
        var pendingScrollback = view.PrepareScrollback(in context, 64);
        pendingScrollback.Should().NotBeNull();
        view.RollbackScrollback(pendingScrollback!);
        var renderUnderPressure = () => TuiCapture.RenderToString(
            view, width, 12, trimTrailingBlankLines: true);

        renderUnderPressure.Should().NotThrow()
            .Which.Should().Contain("first").And.Contain("second").And.Contain("third");
    }

    [Fact]
    public void CommonCounters_TrackActualRetainedSurfaceBytesAndEviction()
    {
        var model = new TranscriptModel();
        model.AddFinal(Row("counter-user", "counter", "retained row"));
        var counters = new TuiPerformanceCounters();
        var view = new TranscriptView(model,
            new HpdAgentTuiBuilder().AddDefaultTranscriptRenderers().Build().TranscriptRenderers,
            height: 4, performanceCounters: counters);

        _ = TuiCapture.RenderToString(view, 40, 4);

        counters.Snapshot().SurfaceCacheBytes.Should().BeGreaterThan(0);
        view.DisposeCache();
        var disposed = counters.Snapshot();
        disposed.SurfaceCacheBytes.Should().Be(0);
        disposed.SurfaceCacheEvictions.Should().Be(1);
    }

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
        model.UpsertLive(Row("live-assistant", "assistant:live", "still streaming"), CommittedHistoryMutationPolicy.Reject);

        var view = CreateView(model, height: 6);

        var rendered = TuiCapture.RenderToString(view, width: 80, height: 8, trimTrailingBlankLines: true);

        rendered.Should().Contain("still streaming");
        rendered.Should().Contain("already committed");
    }

    [Fact]
    public void Render_UsesLatestLiveEntryVersion()
    {
        var model = new TranscriptModel();
        model.UpsertLive(Row("assistant-1", "assistant:1", "first draft"), CommittedHistoryMutationPolicy.Reject);
        model.UpsertLive(Row("assistant-2", "assistant:1", "second draft"), CommittedHistoryMutationPolicy.Reject);

        var view = CreateView(model, height: 6);

        var rendered = TuiCapture.RenderToString(view, width: 80, height: 8, trimTrailingBlankLines: true);

        rendered.Should().Contain("second draft");
        rendered.Should().NotContain("first draft");
    }

    [Fact]
    public void Render_FinalizedLiveEntryStaysInTailViewport()
    {
        var model = new TranscriptModel();
        model.UpsertLive(Row("assistant-1", "assistant:1", "streaming"), CommittedHistoryMutationPolicy.Reject);
        model.FinalizeLive("assistant:1", Row("assistant-1", "assistant:1", "done"), CommittedHistoryMutationPolicy.Reject);

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
            Metadata: new TranscriptEntryMetadata()), CommittedHistoryMutationPolicy.Reject);

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
            Metadata: new TranscriptEntryMetadata()), CommittedHistoryMutationPolicy.Reject);

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
            Metadata: new TranscriptEntryMetadata()), CommittedHistoryMutationPolicy.Reject);

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
            Metadata: new TranscriptEntryMetadata()), CommittedHistoryMutationPolicy.Reject);

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
    public void Scrollback_ResizeReleasesPendingBatchAndReflowsTailInNewEpoch()
    {
        var model = new TranscriptModel { HistoryPresentation = TranscriptHistoryPresentation.TerminalScrollback };
        model.AddFinal(Row("user-row", "row:key", "abcdefgh"));
        var view = CreateView(model, height: 3);
        var wide = new RenderContext(8, 3, Theme.Default);
        var stale = view.PrepareScrollback(in wide, 64)!;

        var narrow = new RenderContext(4, 3, Theme.Default);
        view.ResetPresentation(9, in narrow);
        var reflowed = view.PrepareScrollback(in narrow, 64)!;

        reflowed.PresentationEpoch.Should().Be(9);
        reflowed.FirstSequence.Should().Be(0);
        reflowed.Rows.Should().HaveCountGreaterThan(stale.Rows.Count);
        reflowed.Rows.SelectMany(row => row.Cells).Select(cell => cell.Grapheme)
            .Should().Equal("›", " ", "a", "b", " ", " ", "c", "d",
                " ", " ", "e", "f", " ", " ", "g", "h");
        var commitStale = () => view.CommitScrollback(stale);
        commitStale.Should().Throw<InvalidOperationException>();
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
