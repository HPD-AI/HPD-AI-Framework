using FluentAssertions;
using HPD.Agent.TUI.Application;
using HPD.Agent.TUI.Composition;
using HPD.Agent.TUI.Models;
using HPD.Agent.TUI.Observability;
using HPD.Agent.TUI.Runtime;
using HPD.Agent.TUI.Views;
using HPD.Events;
using HPD.TUI.Controllers;
using HPD.TUI.Models;
using HPD.TUI.Observability;
using HPD.TUI.Rendering;
using HPD.TUI.Core;

namespace HPD.Agent.TUI.Tests;

public sealed class TranscriptViewPerformanceTests
{
    [Fact]
    public void TranscriptView_LargeLiveSet_RendersVisibleWindowOnly()
    {
        var model = CreateTranscript(1_000);
        var view = CreateView(model, height: 12);

        Render(view);

        view.LastDiagnostics.RenderedRows.Should().Be(12);
        view.LastDiagnostics.RowsCaptured.Should().BeLessThanOrEqualTo(12);
        view.LastDiagnostics.EntriesVisited.Should().BeLessThanOrEqualTo(12);
    }

    [Fact]
    public void TranscriptView_UpsertOneEntry_DoesNotRecaptureAllRows()
    {
        var model = CreateTranscript(1_000);
        var view = CreateView(model, height: 12);
        Render(view);

        model.UpsertLive(Row(999, text: "updated visible row"));
        Render(view);

        view.LastDiagnostics.RowsCaptured.Should().Be(1);
        view.LastDiagnostics.CacheHits.Should().BeGreaterThanOrEqualTo(11);
        view.LastDiagnostics.EntriesVisited.Should().BeLessThanOrEqualTo(12);
    }

    [Fact]
    public void TranscriptView_RenderWithoutChanges_IsCacheHit()
    {
        var model = CreateTranscript(100);
        var view = CreateView(model, height: 12);
        Render(view);

        Render(view);

        view.LastDiagnostics.RowsCaptured.Should().Be(0);
        view.LastDiagnostics.CacheMisses.Should().Be(0);
        view.LastDiagnostics.CacheHits.Should().BeGreaterThanOrEqualTo(12);
        view.LastDiagnostics.RenderedRows.Should().Be(12);
    }

    [Fact]
    public void TranscriptRendererRegistry_Lookup_IsConstantShape()
    {
        var registry = new HpdAgentTuiBuilder()
            .AddDefaultTranscriptRenderers()
            .Build()
            .TranscriptRenderers;
        var entries = new[]
        {
            Row(1),
            new TranscriptEntry(
                Id: "assistant-1",
                EntryKey: "assistant:1",
                Cell: HPD.Agent.TUI.Markdown.MarkdownMessageFactory.CreateAssistant("assistant-1", "assistant"),
                Metadata: new TranscriptEntryMetadata(),
                VerticalSpacing: 0),
            new TranscriptEntry(
                Id: "notice-1",
                EntryKey: "notice:1",
                Cell: new NoticeCell("notice"),
                Metadata: new TranscriptEntryMetadata(),
                VerticalSpacing: 0)
        };
        foreach (var entry in entries)
        {
            registry.Create(entry, 80, Theme.Default, ColorSystem.TrueColor);
        }

        var elapsed = Measure(() =>
        {
            for (var i = 0; i < 25_000; i++)
            {
                registry.Create(entries[i % entries.Length], 80, Theme.Default, ColorSystem.TrueColor);
            }
        });

        elapsed.Should().BeLessThan(
            TimeSpan.FromSeconds(2),
            "transcript renderer lookup should remain dictionary-shaped and not visible in render hot paths");
    }

    [Fact]
    public void ShellLayout_StatusOnlyChange_DoesNotRecaptureTranscript()
    {
        var registry = new HpdAgentTuiBuilder()
            .AddAgentTuiDefaults()
            .Build();
        var state = new AgentTuiSessionState(
            new AgentTuiRuntimeScope("agent", "session", "main"),
            registry);
        for (var i = 0; i < 100; i++)
        {
            state.Shell.Transcript.UpsertLive(Row(i));
        }

        var sink = new RecordingSink();
        AgentTuiPerformanceDiagnostics.SetSink(state.State, sink);
        var prompt = registry.PromptFactory.Create(
            new AgentTuiPromptContext(state.Scope, state.Shell),
            _ => { },
            new AutocompleteController());
        var shell = registry.ShellLayout.Create(new AgentTuiShellLayoutContext(
            state.Shell,
            prompt,
            registry,
            registry.ShellChrome,
            state.State));
        var activity = state.Shell.Activities.Add("status");

        TuiCapture.RenderToString(shell, width: 100, height: 32, trimTrailingBlankLines: true);
        sink.Events.Clear();

        activity.Progress = 0.5;
        TuiCapture.RenderToString(shell, width: 100, height: 32, trimTrailingBlankLines: true);

        var evt = sink.Events.OfType<TranscriptViewRendered>().Should().ContainSingle().Subject;
        evt.RowsCaptured.Should().Be(0);
        evt.CacheMisses.Should().Be(0);
        evt.CacheHits.Should().BeGreaterThan(0);
    }

    private static TranscriptModel CreateTranscript(int count)
    {
        var model = new TranscriptModel();
        for (var i = 0; i < count; i++)
        {
            model.UpsertLive(Row(i));
        }

        return model;
    }

    private static TranscriptView CreateView(TranscriptModel model, int height)
        => new(
            model,
            new HpdAgentTuiBuilder()
                .AddDefaultTranscriptRenderers()
                .Build()
                .TranscriptRenderers,
            height);

    private static void Render(TranscriptView view)
        => TuiCapture.RenderToString(view, width: 80, height: view.Height, trimTrailingBlankLines: true);

    private static TimeSpan Measure(Action action)
    {
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        action();
        stopwatch.Stop();
        return stopwatch.Elapsed;
    }

    private static TranscriptEntry Row(int index, string? text = null)
        => new(
            Id: $"entry-{index:D4}",
            EntryKey: $"entry:{index:D4}",
            Cell: new UserMessageCell(text ?? $"row {index:D4}"),
            Metadata: new TranscriptEntryMetadata(),
            VerticalSpacing: 0);

    private sealed class RecordingSink : IHpdTuiPerformanceEventSink
    {
        public List<Event> Events { get; } = [];

        public void Publish(Event evt)
        {
            Events.Add(evt);
        }
    }
}
