using HPD.Agent.TUI;
using HPD.Agent.TUI.Application;
using HPD.Agent.TUI.Composition;
using HPD.Agent.TUI.Runtime;
using HPD.Agent.TUI.Views;
using HPD.Agent.ToolHarness.Coding.Debugging;
using HPD.Agent.ToolHarness.Coding.TUI;
using HPD.Agent.ToolHarness.Coding.TUI.Debugging;
using HPD.TUI.Rendering;
using HPD.TUI.Views;
using HPDOS.ToolHarnesses.Middleware;

namespace HPD.Agent.ToolHarness.Coding.TUI.Tests;

public sealed class DebugTuiTests
{
    [Fact]
    public void AddCodingHarnessTui_registers_semantic_debugger_presentation()
    {
        var registry = new HpdAgentTuiBuilder()
            .AddCodingHarnessTui()
            .Build();

        registry.EventHandlers.Select(handler => handler.Key).Should()
            .Contain([
                "hpd.coding.debug.reducer",
                "hpd.coding.debug.breakpoint-selection",
                "hpd.coding.debug.stopped",
                "hpd.coding.debug.stop-summary",
                "hpd.coding.debug.continued"
            ]);
        registry.TranscriptRenderers.TryFindRenderer<DebugBreakpointCell>(
            CodingHarnessTuiTranscriptRendererKeys.DebugBreakpoint,
            out _).Should().BeTrue();
        registry.FooterItems.Select(item => item.Key).Should().Contain("hpd.coding.debug");
        registry.Pages.Should().Contain(page => page.Id == DebugStatusPage.PageId);
        registry.Commands.Should().Contain(command => command.Name == "debug");
    }

    [Fact]
    public void Source_breakpoint_cell_renders_verified_marker_condition_and_delta()
    {
        var item = new DebugBreakpointSelectionEventItem
        {
            ClientBreakpointId = "client",
            Kind = DebugBreakpointKind.Source,
            DisplayPath = "Program.cs",
            RequestedLine = 2,
            ResolvedLine = 2,
            Condition = "price < 0",
            Acknowledged = true,
            Verified = true
        };
        var cell = new DebugBreakpointCell(
            "entry",
            "• Breakpoints +1 · 1/1 verified",
            DebugBreakpointKind.Source,
            [],
            [item],
            [new("client", DebugBreakpointSelectionDeltaKind.Added)],
            new(1, 1, 1, 0),
            [new DebugSourcePreview
            {
                DisplayPath = "Program.cs",
                Language = "csharp",
                Hunks = [new(1, ["var total = 0;", "total += price;", "return total;"])],
                Truncated = false
            }],
            false);

        var rendered = TuiCapture.RenderToString(
            new DebugBreakpointCellView(cell, CodingHarnessTuiTheme.Default),
            80,
            20,
            trimTrailingBlankLines: true);

        rendered.Should().Contain("2 ◆ total += price;");
        rendered.Should().Contain("added · when price < 0");
    }

    [Fact]
    public void Non_source_breakpoint_cell_does_not_display_adapter_identity()
    {
        var item = new DebugBreakpointSelectionEventItem
        {
            ClientBreakpointId = "digest",
            Kind = DebugBreakpointKind.Data,
            SafeDisplayName = "Data breakpoint",
            Acknowledged = false,
            Verified = false
        };
        var cell = new DebugBreakpointCell(
            "entry",
            "• Breakpoints +1 · 0/1 verified",
            DebugBreakpointKind.Data,
            [],
            [item],
            [new("digest", DebugBreakpointSelectionDeltaKind.Added)],
            new(1, 0, 0, 1),
            [],
            false);

        var rendered = TuiCapture.RenderToString(
            new DebugBreakpointCellView(cell, CodingHarnessTuiTheme.Default),
            80,
            20,
            trimTrailingBlankLines: true);

        rendered.Should().Contain("Data breakpoint");
        rendered.Should().Contain("not acknowledged");
        rendered.Should().NotContain("digest");
        rendered.Should().NotMatchRegex(@"^\s*1\s", "non-source breakpoints must not look like source lines");
    }

    [Fact]
    public void Reducer_identity_is_idempotent()
    {
        var state = new DebugTuiState();
        var @event = new DebugSessionStoppedEvent
        {
            DebugTreeId = "tree",
            DebugSessionId = "debug-session",
            AdapterId = "adapter",
            AdapterThreadId = 7,
            Reason = "breakpoint",
            SuspensionEpoch = 1
        };

        state.BeginReduce(@event).Should().BeTrue();
        state.BeginReduce(@event).Should().BeFalse();
    }

    [Fact]
    public async Task Stop_summary_updates_only_the_matching_suspension_epoch()
    {
        var registry = new HpdAgentTuiBuilder()
            .AddAgentTuiDefaults()
            .AddCodingHarnessTui()
            .Build();
        var state = new AgentTuiSessionState(
            new AgentTuiRuntimeScope("agent", "session", "main"),
            registry);
        await state.ApplyEventAsync(new DebugSessionStoppedEvent
        {
            DebugTreeId = "tree",
            DebugSessionId = "debug-session",
            AdapterId = "adapter",
            AdapterThreadId = 7,
            Reason = "breakpoint",
            SuspensionEpoch = 3
        });
        await state.ApplyEventAsync(Summary(epoch: 2, frame: "stale"));
        Render(state, registry).Should().Contain("Collecting top frame").And.NotContain("stale");

        await state.ApplyEventAsync(Summary(epoch: 3, frame: "CalculateTotal"));
        Render(state, registry).Should().Contain("CalculateTotal");

        await state.ApplyEventAsync(new DebugSessionContinuedEvent
        {
            DebugTreeId = "tree",
            DebugSessionId = "debug-session",
            AdapterId = "adapter",
            AdapterThreadId = 7
        });
        Render(state, registry).Should().Contain("CalculateTotal");
    }

    [Fact]
    public async Task Historical_events_rehydrate_debug_footer_and_terminal_eviction_clears_it()
    {
        var registry = new HpdAgentTuiBuilder()
            .AddAgentTuiDefaults()
            .AddCodingHarnessTui()
            .Build();
        var state = new AgentTuiSessionState(
            new AgentTuiRuntimeScope("agent", "session", "main"),
            registry);
        await state.ApplyEventAsync(new DebugTreeStartedEvent
        {
            DebugTreeId = "tree",
            DebugSessionId = "debug-session",
            AdapterId = "netcoredbg",
            EnvironmentId = "local",
            SemanticStartKind = DebugSemanticStartKind.DirectLaunch,
            AdapterStartMethod = DebugAdapterStartMethod.Launch,
            ExecutionPlannerId = "dotnet-application"
        }, deliveryMode: AgentTuiEventDeliveryMode.Historical);
        await state.ApplyEventAsync(new DebugSessionStoppedEvent
        {
            DebugTreeId = "tree",
            DebugSessionId = "debug-session",
            AdapterId = "netcoredbg",
            AdapterThreadId = 1,
            Reason = "entry",
            SuspensionEpoch = 1
        }, deliveryMode: AgentTuiEventDeliveryMode.Historical);

        RenderShell(registry, state).Should().Contain("debug stopped");

        await state.ApplyEventAsync(new DebugTerminalRecordEvictedEvent
        {
            DebugTreeId = "tree",
            DebugSessionId = "debug-session",
            AdapterId = "netcoredbg",
            SafeReasonCode = "capacity"
        });
        RenderShell(registry, state).Should().NotContain("debug stopped");
    }

    private static DebugStopSummaryAvailableEvent Summary(long epoch, string frame)
        => new()
        {
            DebugTreeId = "tree",
            DebugSessionId = "debug-session",
            AdapterId = "adapter",
            AdapterThreadId = 7,
            SuspensionEpoch = epoch,
            Reason = "breakpoint",
            FrameName = frame,
            DisplayPath = "Program.cs",
            Line = 18,
            Column = 5,
            InspectionSucceeded = true
        };

    private static string Render(AgentTuiSessionState state, HpdAgentTuiRegistry registry)
        => TuiCapture.RenderToString(
            new TranscriptView(state.Shell.Transcript, registry.TranscriptRenderers, height: 18),
            100,
            20,
            trimTrailingBlankLines: true);

    private static string RenderShell(HpdAgentTuiRegistry registry, AgentTuiSessionState state)
        => TuiCapture.RenderToString(
            registry.ShellLayout.Create(new AgentTuiShellLayoutContext(
                state.Shell,
                PromptView.Create("Ask HPD..."),
                registry,
                registry.ShellChrome,
                state.State)),
            100,
            24,
            trimTrailingBlankLines: true);
}
