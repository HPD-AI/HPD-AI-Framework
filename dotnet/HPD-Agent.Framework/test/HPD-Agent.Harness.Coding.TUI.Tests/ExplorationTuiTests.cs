using HPD.Agent;
using HPD.Agent.TUI;
using HPD.Agent.TUI.Application;
using HPD.Agent.TUI.Composition;
using HPD.Agent.TUI.Models;
using HPD.Agent.TUI.Runtime;
using HPD.Agent.TUI.Views;
using HPD.Agent.ToolHarness.Coding.TUI;
using HPD.Agent.ToolHarness.Coding.TUI.Exploration;
using HPD.Agent.ToolHarness.Coding.TUI.Harness;
using HPD.TUI.Components;
using HPD.TUI.Core;
using HPD.TUI.Rendering;
using HPD.TUI.Views;

namespace HPD.Agent.ToolHarness.Coding.TUI.Tests;

public sealed class ExplorationTuiTests
{
    [Fact]
    public void AddCodingHarnessTui_RegistersExplorationHandlersAndStatus()
    {
        var registry = new HpdAgentTuiBuilder()
            .AddCodingHarnessTui()
            .Build();

        registry.EventHandlers.Select(static handler => handler.Key).Should().Contain([
            "hpd.coding.harness.tool-call",
            "hpd.coding.exploration.tool-start",
            "hpd.coding.exploration.tool-args",
            "hpd.coding.exploration.tool-result",
            "hpd.coding.exploration.tool-end"
        ]);
        registry.StatusItems.Select(static item => item.Key).Should().Contain("hpd.coding.exploration");
        registry.TranscriptRenderers.TryFindRenderer<CodingExplorationCell>(
            CodingHarnessTuiTranscriptRendererKeys.Exploration,
            out _).Should().BeTrue();
        registry.TranscriptRenderers.TryFindRenderer<CodingHarnessToolCell>(
            CodingHarnessTuiTranscriptRendererKeys.Harness,
            out _).Should().BeTrue();
    }

    [Fact]
    public async Task CollapsedCodingHarnessToolCall_RendersAsCodingToolsTranscriptEntry()
    {
        var state = CreateState();
        const string callId = "call-coding-harness";

        await state.ApplyEventAsync(new ToolCallStartEvent(
            callId,
            "CodingToolHarness",
            "msg-1",
            ToolHarnessName: "CodingToolHarness"));

        ReadRows(state.Shell.Transcript).Should().ContainSingle()
            .Which.Cell.Should().BeOfType<CodingHarnessToolCell>()
            .Which.IsActive.Should().BeTrue();

        await state.ApplyEventAsync(new ToolCallResultEvent(
            callId,
            new ToolResultPayload(Text: "Contains tools for coding operations: file operations, code search, shell execution, and code analysis. expanded."),
            ToolHarnessName: "CodingToolHarness",
            Name: "CodingToolHarness")
        {
            MessageId = "msg-1"
        });

        var rows = ReadRows(state.Shell.Transcript);
        rows.Should().ContainSingle();
        rows[0].Cell.Should().BeOfType<CodingHarnessToolCell>()
            .Which.IsActive.Should().BeFalse();

        var rendered = RenderTranscript(state);
        rendered.Should().Contain("• Coding tools");
        rendered.Should().Contain("Ready: file operations, code search, shell execution, and code analysis");
        rendered.Should().NotContain("Contains tools for coding operations");
    }

    [Fact]
    public async Task CollapsedCodingHarnessToolCall_IsRenderedOnlyOnce()
    {
        var state = CreateState();
        const string firstCallId = "call-coding-harness-1";
        const string secondCallId = "call-coding-harness-2";

        await state.ApplyEventAsync(new ToolCallStartEvent(
            firstCallId,
            "CodingToolHarness",
            "msg-1",
            ToolHarnessName: "CodingToolHarness"));
        await state.ApplyEventAsync(new ToolCallResultEvent(
            firstCallId,
            new ToolResultPayload(Text: "expanded"),
            ToolHarnessName: "CodingToolHarness",
            Name: "CodingToolHarness")
        {
            MessageId = "msg-1"
        });
        await state.ApplyEventAsync(new ToolCallStartEvent(
            secondCallId,
            "CodingToolHarness",
            "msg-1",
            ToolHarnessName: "CodingToolHarness"));
        await state.ApplyEventAsync(new ToolCallResultEvent(
            secondCallId,
            new ToolResultPayload(Text: "expanded again"),
            ToolHarnessName: "CodingToolHarness",
            Name: "CodingToolHarness")
        {
            MessageId = "msg-1"
        });

        var rows = ReadRows(state.Shell.Transcript);
        rows.Should().ContainSingle();
        rows[0].EntryKey.Should().Be("coding.harness");
        rows[0].Cell.Should().BeOfType<CodingHarnessToolCell>();
        RenderTranscript(state).Should().Contain("• Coding tools");
    }

    [Fact]
    public async Task ReadFileBurst_CoalescesIntoOneExploredTranscriptEntry()
    {
        var state = CreateState();

        await CompleteTool(
            state,
            callId: "call-read-1",
            name: "ReadFile",
            messageId: "msg-1",
            argsJson: """{"path":"src/Agent.cs"}""",
            resultXml: """<file path="src/Agent.cs" start_line="1" lines_read="100" total_lines="900" truncated="false" coverage="partial" />""");
        await CompleteTool(
            state,
            callId: "call-read-2",
            name: "ReadFile",
            messageId: "msg-1",
            argsJson: """{"path":"src/AgentEvents.cs"}""",
            resultXml: """<file path="src/AgentEvents.cs" start_line="1" lines_read="50" total_lines="200" truncated="false" coverage="partial" />""");

        var rows = ReadRows(state.Shell.Transcript);
        rows.Should().ContainSingle();
        rows[0].Cell.Should().BeOfType<CodingExplorationCell>()
            .Which.IsActive.Should().BeFalse();

        var rendered = RenderTranscript(state);
        rendered.Should().Contain("• Explored");
        rendered.Should().Contain("Read Agent.cs, AgentEvents.cs");
        rendered.Should().NotContain("<file");
    }

    [Fact]
    public async Task ReplacedExplorationRenderer_KeepsExplorationHandlersAndState()
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

        await state.ApplyEventAsync(new ToolCallStartEvent("call-read-1", "ReadFile", "msg-1"));

        ReadRows(state.Shell.Transcript).Should().ContainSingle()
            .Which.Cell.Should().BeOfType<CodingExplorationCell>();
        RenderTranscript(state, registry.TranscriptRenderers).Should().Contain("custom exploration row");
        RenderShell(registry, state).Should().Contain("exploring");
    }

    [Fact]
    public async Task CustomCodingTheme_StylesExplorationTranscript()
    {
        var codingTheme = new CodingHarnessTuiTheme
        {
            Label = new Style(new Color(201, 82, 221), Color.Default, TextAttributes.Bold),
            Prefix = new Style(new Color(90, 100, 120), Color.Default),
            Text = new Style(new Color(220, 225, 230), Color.Default)
        };
        var registry = new HpdAgentTuiBuilder()
            .AddAgentTuiDefaults()
            .AddCodingHarnessTui(codingTheme)
            .Build();
        var state = new AgentTuiSessionState(
            new AgentTuiRuntimeScope("agent", "session", "main"),
            registry);

        await CompleteTool(
            state,
            callId: "call-read-1",
            name: "ReadFile",
            messageId: "msg-1",
            argsJson: """{"path":"src/Agent.cs"}""",
            resultXml: """<file path="src/Agent.cs" start_line="1" lines_read="100" total_lines="900" truncated="false" coverage="partial" />""");

        var rendered = RenderTranscriptAnsi(state, registry.TranscriptRenderers);

        rendered.Should().Contain("38;2;201;82;221");
        rendered.Should().Contain("38;2;90;100;120");
        rendered.Should().Contain("38;2;220;225;230");
    }

    [Fact]
    public async Task SearchFollowedByReads_RendersSearchThenCoalescedReadRow()
    {
        var state = CreateState();

        await CompleteTool(
            state,
            callId: "call-grep-1",
            name: "Grep",
            messageId: "msg-1",
            argsJson: """{"pattern":"AgentEvent","path":"src"}""",
            resultXml: """<grep path="src" pattern="AgentEvent" results_read="8" total_results="8" total_matches="12" truncated="false" status="ok" />""");
        await CompleteTool(
            state,
            callId: "call-read-1",
            name: "ReadFile",
            messageId: "msg-1",
            argsJson: """{"path":"src/AgentEvents.cs"}""",
            resultXml: """<file path="src/AgentEvents.cs" start_line="1" lines_read="80" total_lines="300" truncated="false" coverage="partial" />""");
        await CompleteTool(
            state,
            callId: "call-read-2",
            name: "ReadFile",
            messageId: "msg-1",
            argsJson: """{"path":"src/Agent.cs"}""",
            resultXml: """<file path="src/Agent.cs" start_line="1" lines_read="80" total_lines="300" truncated="false" coverage="partial" />""");

        var rendered = RenderTranscript(state);
        rendered.Should().Contain("Search \"AgentEvent\" in src 12 matches");
        rendered.Should().Contain("Read AgentEvents.cs, Agent.cs");
    }

    [Fact]
    public async Task NonExplorationTool_IsIgnored()
    {
        var state = CreateState();

        await state.ApplyEventAsync(new ToolCallStartEvent("call-command-1", "ExecuteCommand", "msg-1"));
        await state.ApplyEventAsync(new ToolCallArgsEvent("call-command-1", """{"command":"pwd"}"""));
        await state.ApplyEventAsync(new ToolCallResultEvent("call-command-1", new ToolResultPayload(Text: "ok"), Name: "ExecuteCommand")
        {
            MessageId = "msg-1"
        });

        ReadRows(state.Shell.Transcript).Should().BeEmpty();
    }

    [Fact]
    public async Task StatusItem_ReadsExplorationState()
    {
        var registry = new HpdAgentTuiBuilder()
            .AddAgentTuiDefaults()
            .AddCodingHarnessTui()
            .Build();
        var state = new AgentTuiSessionState(
            new AgentTuiRuntimeScope("agent", "session", "main"),
            registry);

        await state.ApplyEventAsync(new ToolCallStartEvent("call-read-1", "ReadFile", "msg-1"));

        var rendered = RenderShell(registry, state);
        rendered.Should().Contain("exploring");
    }

    private static AgentTuiSessionState CreateState()
        => new(
            new AgentTuiRuntimeScope("agent", "session", "main"),
            new HpdAgentTuiBuilder()
                .AddCodingHarnessTui()
                .Build());

    private static async Task CompleteTool(
        AgentTuiSessionState state,
        string callId,
        string name,
        string messageId,
        string argsJson,
        string resultXml)
    {
        await state.ApplyEventAsync(new ToolCallStartEvent(callId, name, messageId));
        await state.ApplyEventAsync(new ToolCallArgsEvent(callId, argsJson));
        await state.ApplyEventAsync(new ToolCallResultEvent(callId, new ToolResultPayload(Text: resultXml), Name: name)
        {
            MessageId = messageId
        });
        await state.ApplyEventAsync(new ToolCallEndEvent(callId, messageId, name, argsJson));
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
        int height = 12)
        => TuiCapture.RenderToString(
            new TranscriptView(state.Shell.Transcript, renderers ?? DefaultTranscriptRenderers(), height: 10),
            width: width,
            height: height,
            trimTrailingBlankLines: true);

    private static string RenderTranscriptAnsi(
        AgentTuiSessionState state,
        AgentTuiTranscriptRendererRegistry renderers,
        int width = 100,
        int height = 12)
        => TuiCapture.RenderToAnsi(
            new TranscriptView(state.Shell.Transcript, renderers, height: 10),
            width: width,
            height: height);

    private static AgentTuiTranscriptRendererRegistry DefaultTranscriptRenderers()
        => new HpdAgentTuiBuilder()
            .AddDefaultTranscriptRenderers()
            .AddCodingHarnessTui()
            .Build()
            .TranscriptRenderers;

    private static string RenderShell(HpdAgentTuiRegistry registry, AgentTuiSessionState state)
        => TuiCapture.RenderToString(
            registry.ShellLayout.Create(new AgentTuiShellLayoutContext(
                state.Shell,
                PromptView.Create("Ask HPD..."),
                registry,
                registry.ShellChrome,
                state.State)),
            width: 100,
            height: 24,
            trimTrailingBlankLines: true);
}
