using HPD.Agent;
using HPD.Agent.TUI;
using HPD.Agent.TUI.Application;
using HPD.Agent.TUI.Composition;
using HPD.Agent.TUI.Models;
using HPD.Agent.TUI.Runtime;
using HPD.Agent.TUI.Views;
using HPD.Agent.ToolHarness.Coding.TUI;
using HPD.Agent.ToolHarness.Coding.TUI.Exploration;
using HPD.TUI.Components;
using HPD.TUI.Rendering;

namespace HPD.Agent.ToolHarness.Coding.TUI.Tests;

public sealed class CodingExplorationTuiPerformanceTests
{
    private const int MaxCellOperations = 64;
    private const int MaxDisplayRows = 16;

    [Fact]
    public async Task Exploration_ManyToolCalls_GroupedRowsAreBounded()
    {
        var state = CreateState();

        for (var i = 0; i < 100; i++)
        {
            await StartRead(state, i);
        }

        var rows = ReadRows(state.Shell.Transcript);
        rows.Should().ContainSingle();

        var cell = rows[0].Cell.Should().BeOfType<CodingExplorationCell>().Subject;
        cell.Operations.Count.Should().BeLessThanOrEqualTo(MaxCellOperations);
        cell.Rows.Count.Should().BeLessThanOrEqualTo(MaxDisplayRows);

        var rendered = RenderTranscript(state, height: 40);
        rendered.Should().Contain("+36 more exploration operations");
        rendered.Should().NotContain("File099.cs");
    }

    [Fact]
    public async Task Exploration_RepeatedUpdates_DoNotGrowRenderedRowsUnbounded()
    {
        var state = CreateState();

        for (var i = 0; i < 80; i++)
        {
            await StartGrep(state, i);
            await state.ApplyEventAsync(new ToolCallResultEvent(
                $"call-grep-{i:D3}",
                new ToolResultPayload(Text:
                    $"""<grep path="src" pattern="Pattern{i:D3}" results_read="1" total_results="1" total_matches="1" truncated="false" status="ok" />"""),
                Name: "Grep")
            {
                MessageId = "msg-1"
            });
        }

        var cell = ReadSingleCell<CodingExplorationCell>(state);
        cell.Operations.Count.Should().BeLessThanOrEqualTo(MaxCellOperations);
        cell.Rows.Count.Should().BeLessThanOrEqualTo(MaxDisplayRows);

        var rendered = RenderTranscript(state, height: 48);
        rendered.Split('\n', StringSplitOptions.None).Length.Should().BeLessThan(32);
        rendered.Should().Contain("+16 more exploration operations");
        rendered.Should().NotContain("Pattern079");
    }

    [Fact]
    public async Task Exploration_RendererReplacement_KeepsStateWithoutExtraRows()
    {
        var registry = new HpdAgentTuiBuilder()
            .AddAgentTuiDefaults()
            .AddCodingHarnessTui()
            .ReplaceTranscriptRenderer<CodingExplorationCell>(
                CodingHarnessTuiTranscriptRendererKeys.Exploration,
                _ => new Text("custom exploration row"))
            .Build();
        var state = new AgentTuiSessionState(
            new AgentTuiRuntimeScope("agent", "session", "main"),
            registry);

        for (var i = 0; i < 20; i++)
        {
            await StartRead(state, i);
        }

        ReadRows(state.Shell.Transcript).Should().ContainSingle()
            .Which.Cell.Should().BeOfType<CodingExplorationCell>();
        RenderTranscript(state, registry.TranscriptRenderers).Should().Contain("custom exploration row");
    }

    private static AgentTuiSessionState CreateState()
        => new(
            new AgentTuiRuntimeScope("agent", "session", "main"),
            new HpdAgentTuiBuilder()
                .AddCodingHarnessTui()
                .Build());

    private static async Task StartRead(AgentTuiSessionState state, int index)
    {
        var callId = $"call-read-{index:D3}";
        await state.ApplyEventAsync(new ToolCallStartEvent(callId, "ReadFile", "msg-1"));
        await state.ApplyEventAsync(new ToolCallArgsEvent(callId, $$"""{"path":"src/File{{index:D3}}.cs"}"""));
    }

    private static async Task StartGrep(AgentTuiSessionState state, int index)
    {
        var callId = $"call-grep-{index:D3}";
        await state.ApplyEventAsync(new ToolCallStartEvent(callId, "Grep", "msg-1"));
        await state.ApplyEventAsync(new ToolCallArgsEvent(callId, $$"""{"pattern":"Pattern{{index:D3}}","path":"src"}"""));
    }

    private static TCell ReadSingleCell<TCell>(AgentTuiSessionState state)
        where TCell : TranscriptCell
    {
        var rows = ReadRows(state.Shell.Transcript);
        rows.Should().ContainSingle();
        return rows[0].Cell.Should().BeOfType<TCell>().Subject;
    }

    private static List<TranscriptEntry> ReadRows(TranscriptModel model)
    {
        var rows = model.Snapshot().Entries.ToList();
        return rows;
    }

    private static string RenderTranscript(
        AgentTuiSessionState state,
        AgentTuiTranscriptRendererRegistry? renderers = null,
        int width = 100,
        int height = 24)
        => TuiCapture.RenderToString(
            new TranscriptHistoryView(state.Shell.Transcript, renderers ?? DefaultTranscriptRenderers(), height: height - 2),
            width: width,
            height: height,
            trimTrailingBlankLines: true);

    private static AgentTuiTranscriptRendererRegistry DefaultTranscriptRenderers()
        => new HpdAgentTuiBuilder()
            .AddDefaultTranscriptRenderers()
            .AddCodingHarnessTui()
            .Build()
            .TranscriptRenderers;
}
