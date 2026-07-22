using HPD.Agent;
using HPD.Agent.TUI;
using HPD.Agent.TUI.Application;
using HPD.Agent.TUI.Models;
using HPD.Agent.TUI.Runtime;
using HPD.Agent.ToolHarness.Coding.TUI.SubAgents;

namespace HPD.Agent.ToolHarness.Coding.TUI.Tests;

public sealed class SubAgentTuiTests
{
    [Fact]
    public void AddCodingHarnessTui_RegistersSubAgentProjection()
    {
        var registry = new HpdAgentTuiBuilder().AddCodingHarnessTui().Build();

        registry.EventHandlers.Select(static handler => handler.Key)
            .Should().Contain("hpd.coding.subagent.lifecycle");
        registry.TranscriptRenderers.TryFindRenderer<CodingSubAgentCell>(
            CodingHarnessTuiTranscriptRendererKeys.SubAgent,
            out _).Should().BeTrue();
    }

    [Fact]
    public async Task InvocationLifecycle_UpdatesAndFinalizesOneSemanticCell()
    {
        var state = CreateState();
        await state.ApplyEventAsync(new ToolCallStartEvent("call-1", "reviewer", "message-1"));
        await state.ApplyEventAsync(new ToolCallArgsEvent(
            "call-1",
            """{"taskName":"Review Helium","input":"Read and analyze the Helium project."}"""));
        await state.ApplyEventAsync(new SubAgentInvocationStartedEvent(
            "invocation-1", "call-1", "reviewer-agent", "session-1", "child-1",
            "reviewer", "Review Helium", SubAgentContextPolicy.Fresh, AgentInvocationMode.Synchronous));

        var running = Assert.IsType<CodingSubAgentCell>(Assert.Single(Rows(state)).Cell);
        running.State.Should().Be(CodingSubAgentState.Running);
        running.ContextPolicy.Should().Be(SubAgentContextPolicy.Fresh);
        running.Detail.Should().Be("Read and analyze the Helium project.");

        await state.ApplyEventAsync(new SubAgentInvocationCompletedEvent(
            "invocation-1", "Helium uses a layered mathematical architecture."));

        var completed = Assert.IsType<CodingSubAgentCell>(Assert.Single(Rows(state)).Cell);
        completed.State.Should().Be(CodingSubAgentState.Completed);
        completed.Detail.Should().Be("Helium uses a layered mathematical architecture.");
        Rows(state).Should().ContainSingle();
    }

    [Fact]
    public async Task TerminalEventWithoutStart_RehydratesAsFinalCell()
    {
        var state = CreateState();

        await state.ApplyEventAsync(new SubAgentInvocationFailedEvent(
            "invocation-1", "InvalidOperationException", "No assistant response was produced."));

        var cell = Assert.IsType<CodingSubAgentCell>(Assert.Single(Rows(state)).Cell);
        cell.State.Should().Be(CodingSubAgentState.Failed);
        cell.Detail.Should().Be("No assistant response was produced.");
    }

    [Fact]
    public async Task LongPromptAndSummary_AreBounded()
    {
        var state = CreateState();
        await state.ApplyEventAsync(new ToolCallStartEvent("call-1", "worker", "message-1"));
        await state.ApplyEventAsync(new ToolCallArgsEvent(
            "call-1", $$"""{"taskName":"Work","input":"{{new string('p', 400)}}"}"""));

        Assert.IsType<CodingSubAgentCell>(Assert.Single(Rows(state)).Cell)
            .Detail!.Length.Should().Be(160);

        await state.ApplyEventAsync(new SubAgentInvocationStartedEvent(
            "invocation-1", "call-1", "worker-agent", "session-1", "child-1",
            "worker", "Work", SubAgentContextPolicy.Fork, AgentInvocationMode.Background));
        await state.ApplyEventAsync(new SubAgentInvocationCompletedEvent(
            "invocation-1", new string('s', 500)));

        var completed = Assert.IsType<CodingSubAgentCell>(Assert.Single(Rows(state)).Cell);
        completed.Detail!.Length.Should().Be(240);
        completed.Mode.Should().Be(AgentInvocationMode.Background);
    }

    private static AgentTuiSessionState CreateState()
        => new(
            new AgentTuiRuntimeScope("agent", "session", "main"),
            new HpdAgentTuiBuilder().AddCodingHarnessTui().Build());

    private static List<TranscriptEntry> Rows(AgentTuiSessionState state)
        => state.Shell.Transcript.Snapshot().Entries.ToList();
}
