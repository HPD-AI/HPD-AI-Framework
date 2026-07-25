using HPD.Agent.Serialization;
using HPD.Agent.ToolHarness.Coding.Debugging;
using HPDOS.ToolHarnesses.Middleware;

namespace HPD.Agent.ToolHarness.Coding.Tests;

public sealed class DebugPresentationEventTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(), $"hpd-debug-presentation-{Guid.NewGuid():N}");

    public DebugPresentationEventTests() => Directory.CreateDirectory(_root);

    [Fact]
    public async Task Preview_is_bounded_to_selected_source_context()
    {
        var path = Path.Combine(_root, "Program.cs");
        await File.WriteAllLinesAsync(path, Enumerable.Range(1, 30).Select(line => $"line {line}"));
        var provider = new DebugSourcePreviewProvider([], new DebugSourcePreviewOptions());

        var preview = await provider.CaptureAsync(
            new DebugSourcePreviewRequest(Workspace(), path, [10]),
            CancellationToken.None);

        preview.DisplayPath.Should().Be("Program.cs");
        preview.Hunks.Should().ContainSingle().Which.Should().BeEquivalentTo(
            new DebugSourcePreviewHunk(
                7,
                ["line 7", "line 8", "line 9", "line 10", "line 11", "line 12", "line 13"]));
        preview.ContentHash.Should().HaveLength(64);
        preview.UnavailableReason.Should().BeNull();
    }

    [Fact]
    public async Task Preview_rejects_paths_outside_every_workspace_root()
    {
        var provider = new DebugSourcePreviewProvider([], new DebugSourcePreviewOptions());

        var preview = await provider.CaptureAsync(
            new DebugSourcePreviewRequest(
                Workspace(),
                Path.Combine(Path.GetTempPath(), $"outside-{Guid.NewGuid():N}.cs"),
                [1]),
            CancellationToken.None);

        preview.Hunks.Should().BeEmpty();
        preview.UnavailableReason.Should().Be("outside_workspace");
        preview.DisplayPath.Should().NotContain(_root);
    }

    [Fact]
    public async Task Live_text_source_wins_over_disk()
    {
        var path = Path.Combine(_root, "Live.cs");
        await File.WriteAllTextAsync(path, "disk");
        var provider = new DebugSourcePreviewProvider(
            [new StubTextSource(path, "editor")],
            new DebugSourcePreviewOptions());

        var preview = await provider.CaptureAsync(
            new DebugSourcePreviewRequest(Workspace(), path, [1]),
            CancellationToken.None);

        preview.Hunks.Single().Lines.Should().ContainSingle().Which.Should().Be("editor");
        preview.SourceVersion.Should().Be("live-2");
    }

    [Fact]
    public async Task Ambiguous_relative_path_is_qualified_with_owning_root()
    {
        var secondRoot = Path.Combine(Path.GetTempPath(), $"hpd-debug-presentation-{Guid.NewGuid():N}");
        Directory.CreateDirectory(secondRoot);
        try
        {
            var firstPath = Path.Combine(_root, "Shared.cs");
            await File.WriteAllTextAsync(firstPath, "first");
            await File.WriteAllTextAsync(Path.Combine(secondRoot, "Shared.cs"), "second");
            var workspace = new AgentWorkspace(
                "first",
                _root,
                [
                    new AgentWorkspaceRoot("first", _root),
                    new AgentWorkspaceRoot("second", secondRoot)
                ]);
            var provider = new DebugSourcePreviewProvider([], new DebugSourcePreviewOptions());

            var preview = await provider.CaptureAsync(
                new DebugSourcePreviewRequest(workspace, firstPath, [1]),
                CancellationToken.None);

            preview.DisplayPath.Should().Be("@first/Shared.cs");
        }
        finally
        {
            Directory.Delete(secondRoot, recursive: true);
        }
    }

    [Fact]
    public async Task Owned_adapter_source_can_be_previewed_without_filesystem_access()
    {
        var provider = new DebugSourcePreviewProvider([], new DebugSourcePreviewOptions());

        var preview = await provider.CaptureAsync(
            new DebugSourcePreviewRequest(
                Workspace(),
                "/adapter/generated/virtual.cs",
                [2],
                "csharp",
                "line one\nline two\nline three",
                "adapter-1"),
            CancellationToken.None);

        preview.DisplayPath.Should().Be("virtual.cs");
        preview.Hunks.Single().Lines.Should().Contain("line two");
        preview.SourceVersion.Should().Be("adapter-1");
        preview.UnavailableReason.Should().BeNull();
    }

    [Fact]
    public async Task Selection_event_has_semantic_delta_and_never_exposes_opaque_data_identity()
    {
        var provider = new DebugSourcePreviewProvider([], new DebugSourcePreviewOptions());
        var factory = new DebugBreakpointSelectionEventFactory(provider);
        var before = new DebugDesiredBreakpointSnapshot();
        var desired = new DebugDataBreakpoint("raw-adapter-data-id", "write", "ready");
        var after = new DebugDesiredBreakpointSnapshot { Data = [desired] };
        var binding = new DebugBreakpointBindingState
        {
            Kind = DebugBreakpointKind.Data,
            ClientBreakpointId = BreakpointIdentity.Data(desired),
            AdapterId = 4,
            RequestedName = desired.DataId,
            Acknowledged = true,
            Verified = true
        };

        var @event = await factory.CreateAsync(
            new DebugBreakpointMutationResult(
                DebugBreakpointKind.Data,
                before,
                after,
                [binding],
                new DebugBreakpointCounts(1, 1, 1, 0),
                "session"),
            Workspace(),
            "call",
            "setDataBreakpoints",
            "tree",
            "adapter",
            CancellationToken.None);

        @event.Changes.Should().ContainSingle().Which.Kind.Should()
            .Be(DebugBreakpointSelectionDeltaKind.Added);
        @event.After.Should().ContainSingle().Which.SafeDisplayName.Should()
            .Be("Data breakpoint");
        @event.After.Single().ToString().Should().NotContain("raw-adapter-data-id");
    }

    [Fact]
    public void New_debug_presentation_events_round_trip_through_registered_serializer()
    {
        CodingHarnessEventSerialization.RegisterEvents();
        var original = new DebugPrimaryStopAvailableEvent
        {
            DebugTreeId = "tree",
            DebugSessionId = "debug-session",
            AdapterId = "adapter",
            AdapterThreadId = 3,
            SuspensionEpoch = 8,
            Reason = "breakpoint",
            FrameName = "Calculate",
            DisplayPath = "Program.cs",
            Line = 12,
            Column = 5,
            InspectionSucceeded = true,
            HitBreakpointIdentityUnknown = false
        };

        var json = AgentEventSerializer.ToJson(original);
        var roundTrip = AgentEventSerializer.FromJson(json)
            .Should().BeOfType<DebugPrimaryStopAvailableEvent>().Subject;

        roundTrip.SuspensionEpoch.Should().Be(8);
        roundTrip.FrameName.Should().Be("Calculate");
    }

    [Fact]
    public void Activity_and_breakpoint_hit_evidence_round_trip_through_registered_serializer()
    {
        CodingHarnessEventSerialization.RegisterEvents();
        var execution = AgentEventSerializer.FromJson(AgentEventSerializer.ToJson(
                new DebugExecutionCommandAppliedEvent
                {
                    DebugTreeId = "tree",
                    DebugSessionId = "debug-session",
                    AdapterId = "adapter",
                    ToolCallId = "step-call",
                    Command = DebugExecutionCommand.StepOver,
                    AdapterThreadId = 7
                }))
            .Should().BeOfType<DebugExecutionCommandAppliedEvent>().Subject;
        execution.ToolCallId.Should().Be("step-call");
        execution.Command.Should().Be(DebugExecutionCommand.StepOver);
        execution.AdapterThreadId.Should().Be(7);
        var stopped = AgentEventSerializer.FromJson(AgentEventSerializer.ToJson(
                new DebugPrimaryStopAvailableEvent
                {
                    DebugTreeId = "tree",
                    DebugSessionId = "debug-session",
                    AdapterId = "adapter",
                    ToolCallId = "launch-call",
                    AdapterThreadId = 7,
                    Reason = "breakpoint",
                    SuspensionEpoch = 4,
                    InspectionSucceeded = true,
                    HitBreakpointClientIds = ["client-breakpoint"],
                    HitBreakpointIdentityUnknown = false
                }))
            .Should().BeOfType<DebugPrimaryStopAvailableEvent>().Subject;
        stopped.ToolCallId.Should().Be("launch-call");
        stopped.HitBreakpointClientIds.Should().Equal("client-breakpoint");
    }

    private AgentWorkspace Workspace()
        => new("root", _root, [new AgentWorkspaceRoot("root", _root)]);

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }

    private sealed class StubTextSource(string path, string text) : IReadFileTextSource
    {
        public ValueTask<ReadFileTextSourceResult?> TryReadTextAsync(
            string fullPath,
            CancellationToken cancellationToken)
            => ValueTask.FromResult<ReadFileTextSourceResult?>(
                string.Equals(path, fullPath, StringComparison.Ordinal)
                    ? new ReadFileTextSourceResult
                    {
                        FullPath = path,
                        Reader = new StringReader(text),
                        LastWriteTimeUtc = DateTimeOffset.UtcNow,
                        Length = text.Length,
                        Version = "live-2",
                        IsUnsavedEditorContent = true
                    }
                    : null);
    }
}
