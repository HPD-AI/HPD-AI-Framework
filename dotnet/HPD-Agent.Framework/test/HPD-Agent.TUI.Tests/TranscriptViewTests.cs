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
    public void Render_ScrollsByRenderedHeightSoLatestRowsAreVisible()
    {
        var model = new TranscriptModel();
        model.Append(Row("old-user", "user", "first message"));
        model.Append(Row("old-assistant", "assistant", "line one\nline two\nline three"));
        model.Append(Row("middle-user", "user", "second message"));
        model.Append(Row("middle-assistant", "assistant", "line one\nline two\nline three"));
        model.Append(Row("latest-user", "user", "third message"));
        model.Append(Row("latest-assistant", "assistant", "latest response"));

        var view = CreateView(model, height: 6);

        var rendered = TuiCapture.RenderToString(view, width: 80, height: 8, trimTrailingBlankLines: true);

        rendered.Should().Contain("third message");
        rendered.Should().Contain("latest response");
        rendered.Should().Contain("assistant");
        rendered.Should().NotContain("first message");
    }

    [Fact]
    public void Render_WhenScrolledUp_ShowsOlderRows()
    {
        var model = new TranscriptModel();
        model.Append(Row("old-user", "user", "first message"));
        model.Append(Row("old-assistant", "assistant", "first response"));
        model.Append(Row("latest-user", "user", "latest message"));
        model.Append(Row("latest-assistant", "assistant", "latest response"));
        model.ScrollUp(6);

        var view = CreateView(model, height: 6);

        var rendered = TuiCapture.RenderToString(view, width: 80, height: 8, trimTrailingBlankLines: true);

        rendered.Should().Contain("first message");
        rendered.Should().Contain("first response");
        rendered.Should().NotContain("latest response");
    }

    [Fact]
    public void Render_WhenLatestEntryIsLong_CanScrollUpToPreviousMessages()
    {
        var model = new TranscriptModel();
        model.Append(Row("old-user", "user", "first message"));
        model.Append(Row("old-assistant", "assistant", "first response"));
        model.Append(Row("latest-user", "user", "latest request"));
        model.Append(Row(
            "latest-assistant",
            "assistant",
            string.Join('\n', Enumerable.Range(1, 40).Select(static i => $"latest response line {i}"))));

        model.ScrollUp(42);

        var view = CreateView(model, height: 8);

        var rendered = TuiCapture.RenderToString(view, width: 80, height: 10, trimTrailingBlankLines: true);

        rendered.Should().Contain("first message");
        rendered.Should().Contain("first response");
    }

    [Fact]
    public void Render_AfterScrollToBottom_ShowsLatestRows()
    {
        var model = new TranscriptModel();
        model.Append(Row("old-user", "user", "first message"));
        model.Append(Row("old-assistant", "assistant", "first response"));
        model.Append(Row("latest-user", "user", "latest message"));
        model.Append(Row("latest-assistant", "assistant", "latest response"));
        model.ScrollUp(3);
        model.ScrollToBottom();

        var view = CreateView(model, height: 6);

        var rendered = TuiCapture.RenderToString(view, width: 80, height: 8, trimTrailingBlankLines: true);

        rendered.Should().Contain("latest message");
        rendered.Should().Contain("latest response");
        rendered.Should().NotContain("first message");
    }

    [Fact]
    public void Render_RunStatusCell_ShowsStateAndDuration()
    {
        var model = new TranscriptModel();
        model.Append(new TranscriptEntry(
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
        var registry = new HpdAgentTuiBuilder()
            .AddDefaultTranscriptRenderers()
            .ReplaceTranscriptRenderer<RunStatusCell>(
                AgentTuiTranscriptRendererKeys.RunStatus,
                context => new Text($"run {context.Cell.RuntimeRunId}"))
            .Build();
        var model = new TranscriptModel();
        model.Append(new TranscriptEntry(
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
        model.Append(new TranscriptEntry(
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
        model.Append(new TranscriptEntry(
            Id: "unknown",
            EntryKey: null,
            Cell: new UnknownTranscriptCell(),
            Metadata: new TranscriptEntryMetadata()));

        var view = CreateView(model, height: 4);

        var rendered = TuiCapture.RenderToString(view, width: 80, height: 6, trimTrailingBlankLines: true);

        rendered.Should().Contain(nameof(UnknownTranscriptCell));
    }

    private static TranscriptView CreateView(TranscriptModel model, int height)
        => new(
            model,
            new HpdAgentTuiBuilder()
                .AddDefaultTranscriptRenderers()
                .Build()
                .TranscriptRenderers,
            height);

    private static TranscriptEntry Row(string id, string label, string text)
        => new(
            id,
            EntryKey: null,
            label == "user"
                ? new UserMessageCell(new Markdown(text))
                : new AssistantMessageCell(label, new Markdown(text)),
            new TranscriptEntryMetadata());

    private sealed record UnknownTranscriptCell : TranscriptCell;
}
