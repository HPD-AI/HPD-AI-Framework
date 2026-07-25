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
using System.Reflection;
using System.Text.Json.Serialization;

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
                "hpd.coding.debug.tool-calls",
                "hpd.coding.debug.lifecycle-presentation",
                "hpd.coding.debug.execution",
                "hpd.coding.debug.mutation",
                "hpd.coding.debug.breakpoint-selection",
                "hpd.coding.debug.stopped",
                "hpd.coding.debug.primary-stop",
                "hpd.coding.debug.continued"
            ]);
        registry.TranscriptRenderers.TryFindRenderer<DebugBreakpointCell>(
            CodingHarnessTuiTranscriptRendererKeys.DebugBreakpoint,
            out _).Should().BeTrue();
        registry.TranscriptRenderers.TryFindRenderer<DebugActivityCell>(
            CodingHarnessTuiTranscriptRendererKeys.DebugActivity,
            out _).Should().BeTrue();
        registry.FooterItems.Select(item => item.Key).Should().NotContain("hpd.coding.debug");
        registry.Pages.Should().Contain(page => page.Id == DebugStatusPage.PageId);
        registry.Commands.Should().Contain(command => command.Name == "debug");
    }

    [Fact]
    public async Task Launch_call_becomes_one_semantic_lifecycle_entry()
    {
        var registry = new HpdAgentTuiBuilder()
            .AddAgentTuiDefaults()
            .AddCodingHarnessTui()
            .Build();
        var state = new AgentTuiSessionState(
            new AgentTuiRuntimeScope("agent", "session", "main"),
            registry);
        await state.ApplyEventAsync(new ToolCallStartEvent("launch-1", "Debug", "message"));
        await state.ApplyEventAsync(new ToolCallArgsEvent(
            "launch-1",
            """{"request":{"action":"launch","target":{"targetKind":"test","path":"Tests.csproj"}}}"""));
        await state.ApplyEventAsync(new DebugExecutionPlannedEvent
        {
            DebugTreeId = "tree",
            DebugSessionId = "debug-session",
            AdapterId = "netcoredbg",
            ToolCallId = "launch-1",
            SemanticStartKind = DebugSemanticStartKind.HostedLaunchAttach,
            AdapterStartMethod = DebugAdapterStartMethod.Attach,
            ExecutionPlannerId = "dotnet-test"
        });
        await state.ApplyEventAsync(new DebugTreeStartedEvent
        {
            DebugTreeId = "tree",
            DebugSessionId = "debug-session",
            AdapterId = "netcoredbg",
            ToolCallId = "launch-1",
            EnvironmentId = "local",
            SemanticStartKind = DebugSemanticStartKind.HostedLaunchAttach,
            AdapterStartMethod = DebugAdapterStartMethod.Attach,
            ExecutionPlannerId = "dotnet-test"
        });

        var rendered = Render(state, registry);
        rendered.Should().Contain("Debugging started");
        rendered.Should().Contain("netcoredbg");
        rendered.Should().NotContain("Running…");
    }

    [Fact]
    public async Task Late_semantic_execution_claim_replaces_final_fallback()
    {
        var registry = new HpdAgentTuiBuilder()
            .AddAgentTuiDefaults()
            .AddCodingHarnessTui()
            .Build();
        var state = new AgentTuiSessionState(
            new AgentTuiRuntimeScope("agent", "session", "main"),
            registry);
        await state.ApplyEventAsync(new ToolCallStartEvent("step-1", "Debug", "message"));
        await state.ApplyEventAsync(new ToolCallArgsEvent(
            "step-1",
            """{"request":{"action":"stepOver","debugTreeId":"tree","threadId":7}}"""));
        await state.ApplyEventAsync(new ToolCallResultEvent(
            "step-1",
            new ToolResultPayload(Text:
                """<debug tool="Debug" action="stepOver" success="true" status="Stopped" />"""),
            Name: "Debug"));
        Render(state, registry).Should().Contain("Debug · step over");

        await state.ApplyEventAsync(new DebugExecutionCommandAppliedEvent
        {
            DebugTreeId = "tree",
            DebugSessionId = "debug-session",
            AdapterId = "netcoredbg",
            ToolCallId = "step-1",
            Command = DebugExecutionCommand.StepOver,
            AdapterThreadId = 7
        });

        var rendered = Render(state, registry);
        rendered.Should().Contain("Stepped over");
        rendered.Should().Contain("Thread 7");
        rendered.Split("Stepped over").Should().HaveCount(2);
        rendered.Should().NotContain("Debug · step over");
    }

    [Fact]
    public async Task State_mutation_is_explicit_and_bounded()
    {
        var registry = new HpdAgentTuiBuilder()
            .AddAgentTuiDefaults()
            .AddCodingHarnessTui()
            .Build();
        var state = new AgentTuiSessionState(
            new AgentTuiRuntimeScope("agent", "session", "main"),
            registry);
        await state.ApplyEventAsync(new DebugStateMutationAppliedEvent
        {
            DebugTreeId = "tree",
            DebugSessionId = "debug-session",
            AdapterId = "netcoredbg",
            ToolCallId = "mutation-1",
            MutationKind = DebugStateMutationKind.Variable,
            SafeTargetName = "total",
            SafeNewValue = "20"
        });

        Render(state, registry).Should()
            .Contain("Changed variable")
            .And.Contain("total = 20");
    }

    [Fact]
    public async Task Lifecycle_cell_surfaces_missing_stopping_strategy_warning()
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
            ToolCallId = "launch-1",
            EnvironmentId = "local",
            SemanticStartKind = DebugSemanticStartKind.DirectLaunch,
            AdapterStartMethod = DebugAdapterStartMethod.Launch,
            ExecutionPlannerId = "dotnet"
        });
        await state.ApplyEventAsync(new ToolCallResultEvent(
            "launch-1",
            new ToolResultPayload(Text:
                """<debug tool="Debug" action="launch" success="true" warning="no_initial_stop_strategy" />"""),
            Name: "Debug"));

        Render(state, registry).Should().Contain(
            "No stopping strategy was configured");
    }

    [Fact]
    public void Every_public_debug_action_has_exactly_one_presentation_policy()
    {
        var actions = typeof(DebugOperation)
            .GetCustomAttributes<JsonDerivedTypeAttribute>()
            .Select(attribute => attribute.TypeDiscriminator)
            .OfType<string>()
            .Order(StringComparer.Ordinal)
            .ToArray();
        var policies = DebugActionPresentationPolicy.All
            .Select(policy => policy.Action)
            .Order(StringComparer.Ordinal)
            .ToArray();

        actions.Should().HaveCount(49);
        policies.Should().Equal(actions);
        policies.Should().OnlyHaveUniqueItems();
    }

    [Fact]
    public async Task Unclaimed_debug_result_gets_a_bounded_fallback_cell()
    {
        var registry = new HpdAgentTuiBuilder()
            .AddAgentTuiDefaults()
            .AddCodingHarnessTui()
            .Build();
        var state = new AgentTuiSessionState(
            new AgentTuiRuntimeScope("agent", "session", "main"),
            registry);
        await state.ApplyEventAsync(new ToolCallStartEvent("debug-1", "Debug", "message"));
        await state.ApplyEventAsync(new ToolCallArgsEvent(
            "debug-1",
            """{"request":{"action":"getModules","debugTreeId":"tree"}}"""));
        await state.ApplyEventAsync(new ToolCallResultEvent(
            "debug-1",
            new ToolResultPayload(Text:
                """<error tool="Debug" action="getModules" success="false" kind="capability_unavailable">The adapter does not support modules.</error>"""),
            Name: "Debug"));

        var rendered = Render(state, registry);
        rendered.Should().Contain("Debug failed · get modules");
        rendered.Should().Contain("The adapter does not support modules.");
        rendered.Should().NotContain("debugTreeId");
    }

    [Fact]
    public async Task Empty_debug_result_is_visible_instead_of_disappearing()
    {
        var registry = new HpdAgentTuiBuilder()
            .AddAgentTuiDefaults()
            .AddCodingHarnessTui()
            .Build();
        var state = new AgentTuiSessionState(
            new AgentTuiRuntimeScope("agent", "session", "main"),
            registry);
        await state.ApplyEventAsync(new ToolCallStartEvent("debug-1", "Debug", "message"));
        await state.ApplyEventAsync(new ToolCallArgsEvent(
            "debug-1",
            """{"request":{"action":"launch"}}"""));
        await state.ApplyEventAsync(new ToolCallResultEvent(
            "debug-1",
            new ToolResultPayload(Text: ""),
            Name: "Debug"));

        Render(state, registry).Should()
            .Contain("Debug failed · launch")
            .And.Contain("empty result");
    }

    [Fact]
    public async Task Tool_call_end_waits_for_the_following_result()
    {
        var registry = new HpdAgentTuiBuilder()
            .AddAgentTuiDefaults()
            .AddCodingHarnessTui()
            .Build();
        var state = new AgentTuiSessionState(
            new AgentTuiRuntimeScope("agent", "session", "main"),
            registry);
        await state.ApplyEventAsync(new ToolCallStartEvent(
            "snapshot-1",
            "Debug",
            "message"));
        await state.ApplyEventAsync(new ToolCallArgsEvent(
            "snapshot-1",
            """{"request":{"action":"snapshot","debugTreeId":"tree"}}"""));
        await state.ApplyEventAsync(new ToolCallEndEvent(
            "snapshot-1",
            "message",
            "Debug",
            """{"request":{"action":"snapshot","debugTreeId":"tree"}}"""));

        Render(state, registry).Should()
            .Contain("Running…")
            .And.NotContain("ended without a result")
            .And.NotContain("Debug failed");

        await state.ApplyEventAsync(new ToolCallResultEvent(
            "snapshot-1",
            new ToolResultPayload(Text:
                """<debug tool="Debug" action="snapshot" success="true" status="Stopped" />"""),
            Name: "Debug"));

        Render(state, registry).Should()
            .NotContain("ended without a result")
            .And.NotContain("Debug failed");
    }

    [Fact]
    public async Task Successful_terminal_summary_is_not_rewritten_by_late_teardown_faults()
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
            ToolCallId = "launch-1",
            EnvironmentId = "local",
            SemanticStartKind = DebugSemanticStartKind.HostedLaunchAttach,
            AdapterStartMethod = DebugAdapterStartMethod.Attach,
            ExecutionPlannerId = "dotnet-test"
        });
        await state.ApplyEventAsync(new DebugTreeCompletedEvent
        {
            DebugTreeId = "tree",
            DebugSessionId = "debug-session",
            AdapterId = "netcoredbg",
            ToolCallId = "launch-1",
            FinalStatus = "Terminated",
            ExitCode = 0,
            DurationMilliseconds = 100,
            SessionCount = 1,
            ChildSessionCount = 0,
            Breakpoints = new(0, 0, 0, 0),
            BreakpointStopCount = 0,
            RetainedOutputBytes = 0,
            DroppedOutputRecords = 0,
            DroppedOutputBytes = 0,
            ProjectionFailures = 0
        });
        await state.ApplyEventAsync(new DebugTreeFaultedEvent
        {
            DebugTreeId = "tree",
            DebugSessionId = "debug-session",
            AdapterId = "netcoredbg",
            ToolCallId = "launch-1",
            SafeReasonCode = "TRANSPORT_EOF"
        });

        Render(state, registry).Should()
            .Contain("Debug session completed")
            .And.NotContain("Debugging failed")
            .And.NotContain("Transport eof");
    }

    [Fact]
    public async Task Generated_union_validation_error_is_rendered_without_raw_json()
    {
        var registry = new HpdAgentTuiBuilder()
            .AddAgentTuiDefaults()
            .AddCodingHarnessTui()
            .Build();
        var state = new AgentTuiSessionState(
            new AgentTuiRuntimeScope("agent", "session", "main"),
            registry);
        await state.ApplyEventAsync(new ToolCallStartEvent("debug-1", "Debug", "message"));
        await state.ApplyEventAsync(new ToolCallArgsEvent(
            "debug-1",
            """{"request":{"action":"launch","project":"Tests.csproj"}}"""));
        await state.ApplyEventAsync(new ToolCallResultEvent(
            "debug-1",
            new ToolResultPayload(Json: System.Text.Json.JsonSerializer.SerializeToElement(
                new
                {
                    error_type = "validation_error",
                    errors = new[]
                    {
                        new
                        {
                            property = "request.target",
                            error_message = "Required property 'target' is missing."
                        }
                    }
                })),
            Name: "Debug"));

        var rendered = Render(state, registry);
        rendered.Should().Contain("Debug failed · launch");
        rendered.Should().Contain(
            "request.target: Required property 'target' is missing.");
        rendered.Should().NotContain("\"error_type\"");
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
            "• Breakpoints +1 · 1/1 resolved",
            DebugBreakpointKind.Source,
            [],
            [item],
            [new("client", DebugBreakpointSelectionDeltaKind.Added)],
            new(1, 1, 1, 0),
            new HashSet<string>(),
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
            "• Breakpoints +1 · 0/1 resolved",
            DebugBreakpointKind.Data,
            [],
            [item],
            [new("digest", DebugBreakpointSelectionDeltaKind.Added)],
            new(1, 0, 0, 1),
            new HashSet<string>(),
            [],
            false);

        var rendered = TuiCapture.RenderToString(
            new DebugBreakpointCellView(cell, CodingHarnessTuiTheme.Default),
            80,
            20,
            trimTrailingBlankLines: true);

        rendered.Should().Contain("Data breakpoint");
        rendered.Should().Contain("Data: Data breakpoint");
        rendered.Should().Contain("not acknowledged");
        rendered.Should().NotContain("digest");
        rendered.Should().NotMatchRegex(@"^\s*1\s", "non-source breakpoints must not look like source lines");
    }

    [Fact]
    public async Task Breakpoint_card_reconciles_relocation_and_primary_hit_by_stable_identity()
    {
        var registry = new HpdAgentTuiBuilder()
            .AddAgentTuiDefaults()
            .AddCodingHarnessTui()
            .Build();
        var state = new AgentTuiSessionState(
            new AgentTuiRuntimeScope("agent", "session", "main"),
            registry);
        var pending = new DebugBreakpointSelectionEventItem
        {
            ClientBreakpointId = "client-1",
            Kind = DebugBreakpointKind.Source,
            DisplayPath = "Program.cs",
            RequestedLine = 32,
            Acknowledged = false,
            Verified = false
        };
        await state.ApplyEventAsync(new DebugBreakpointSelectionAppliedEvent
        {
            DebugTreeId = "tree",
            DebugSessionId = "debug-session",
            AdapterId = "adapter",
            ToolCallId = "breakpoints-1",
            Action = "setSourceBreakpoints",
            BreakpointKind = DebugBreakpointKind.Source,
            Before = [],
            After = [pending],
            Changes = [new("client-1", DebugBreakpointSelectionDeltaKind.Added)],
            Counts = new(1, 0, 0, 1),
            DetailsTruncated = false
        });
        await state.ApplyEventAsync(new DebugBreakpointChangedEvent
        {
            DebugTreeId = "tree",
            DebugSessionId = "debug-session",
            AdapterId = "adapter",
            ClientBreakpointId = "client-1",
            BreakpointKind = DebugBreakpointKind.Source,
            Change = DebugBreakpointChangeKind.Changed,
            Acknowledged = true,
            Verified = true,
            DisplayPath = "Program.cs",
            ResolvedLine = 34
        });
        await state.ApplyEventAsync(new DebugSessionStoppedEvent
        {
            DebugTreeId = "tree",
            DebugSessionId = "debug-session",
            AdapterId = "adapter",
            AdapterThreadId = 7,
            Reason = "breakpoint",
            SuspensionEpoch = 2
        });
        await state.ApplyEventAsync(new DebugPrimaryStopAvailableEvent
        {
            DebugTreeId = "tree",
            DebugSessionId = "debug-session",
            AdapterId = "adapter",
            AdapterThreadId = 7,
            SuspensionEpoch = 2,
            Reason = "breakpoint",
            FrameName = "Run",
            DisplayPath = "Program.cs",
            Line = 34,
            InspectionSucceeded = true,
            HitBreakpointClientIds = ["client-1"],
            HitBreakpointIdentityUnknown = false
        });

        var rendered = Render(state, registry);
        rendered.Should().Contain("Breakpoints · 1/1 resolved · 1 hit");
        rendered.Should().Contain("● Source · resolved");
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
    public void Unidentified_stop_is_counted_once_across_breakpoint_families()
    {
        var state = new DebugTuiState();
        state.Apply(Selection(DebugBreakpointKind.Source, "source-call", "source"));
        state.Apply(Selection(DebugBreakpointKind.Exception, "exception-call", "exception"));

        var changed = state.ObserveHits(new DebugPrimaryStopAvailableEvent
        {
            DebugTreeId = "tree",
            DebugSessionId = "debug-session",
            AdapterId = "adapter",
            AdapterThreadId = 7,
            SuspensionEpoch = 1,
            Reason = "breakpoint",
            SourcePreview = new DebugSourcePreview
            {
                DisplayPath = "Program.cs",
                Hunks = [],
                Truncated = false
            },
            InspectionSucceeded = true,
            HitBreakpointIdentityUnknown = true
        });

        changed.Should().ContainSingle()
            .Which.Kind.Should().Be(DebugBreakpointKind.Source);
        state.BreakpointSelections.Values.Sum(selection =>
            selection.UnknownHitCount).Should().Be(1);
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
    public async Task Historical_events_rehydrate_debug_state_and_terminal_eviction_clears_it()
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

        state.State.TryGet<DebugTuiState>(DebugTuiState.StateKey, out var debug).Should().BeTrue();
        debug!.Trees.Should().ContainKey("tree");
        debug.Trees["tree"].Status.Should().Be("Stopped");

        await state.ApplyEventAsync(new DebugTerminalRecordEvictedEvent
        {
            DebugTreeId = "tree",
            DebugSessionId = "debug-session",
            AdapterId = "netcoredbg",
            SafeReasonCode = "capacity"
        });
        debug.Trees.Should().NotContainKey("tree");
    }

    private static DebugPrimaryStopAvailableEvent Summary(long epoch, string frame)
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
            InspectionSucceeded = true,
            HitBreakpointIdentityUnknown = false
        };

    private static DebugBreakpointSelectionAppliedEvent Selection(
        DebugBreakpointKind kind,
        string callId,
        string clientId)
        => new()
        {
            DebugTreeId = "tree",
            DebugSessionId = "debug-session",
            AdapterId = "adapter",
            ToolCallId = callId,
            Action = "launch",
            BreakpointKind = kind,
            Before = [],
            After =
            [
                new DebugBreakpointSelectionEventItem
                {
                    ClientBreakpointId = clientId,
                    Kind = kind,
                    SafeDisplayName = kind.ToString(),
                    Acknowledged = true,
                    Verified = true
                }
            ],
            Changes = [new(clientId, DebugBreakpointSelectionDeltaKind.Added)],
            Counts = new(1, 1, 1, 0),
            DetailsTruncated = false
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
