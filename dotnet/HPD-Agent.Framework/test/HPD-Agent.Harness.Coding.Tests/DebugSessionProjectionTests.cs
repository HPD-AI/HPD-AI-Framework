using System.Text.Json;
using HPD.Agent;
using HPD.Agent.ToolHarness.Coding.Debugging;
using HPD.Agent.ToolHarness.Coding.Debugging.Protocol.Generated;

namespace HPD.Agent.ToolHarness.Coding.Tests;

public sealed class DebugSessionProjectionTests
{
    [Fact]
    public void Capability_patch_preserves_absent_values_and_applies_false_and_empty_lists()
    {
        var current = new Capabilities
        {
            SupportsReadMemoryRequest = true,
            SupportsWriteMemoryRequest = true,
            CompletionTriggerCharacters = ["."]
        };
        var merged = DebugCapabilityMerger.Merge(current, new Capabilities
        {
            SupportsReadMemoryRequest = false,
            CompletionTriggerCharacters = []
        });

        merged.SupportsReadMemoryRequest.Should().BeFalse();
        merged.SupportsWriteMemoryRequest.Should().BeTrue();
        merged.CompletionTriggerCharacters.Should().BeEmpty();
    }

    [Fact]
    public void Process_module_and_source_events_reconcile_create_change_and_remove()
    {
        var projections = new DebugSessionProjections();
        projections.ObserveProcess(new ProcessEventBody { Name = "target", SystemProcessId = 42 });
        projections.Process.Should().Be(new DebugProcessSnapshot("target", 42, null, null, null));

        projections.ObserveModule(new ModuleEventBody
        {
            Reason = "new",
            Module = Module("m1", "old")
        });
        projections.ObserveModule(new ModuleEventBody
        {
            Reason = "changed",
            Module = Module("m1", "new")
        });
        projections.Modules.Should().ContainSingle().Which.Name.Should().Be("new");
        projections.ObserveModule(new ModuleEventBody { Reason = "removed", Module = Module("m1", "new") });
        projections.Modules.Should().BeEmpty();

        using var adapterData = JsonDocument.Parse("{\"vendor\":\"opaque\"}");
        var source = new Source { Name = "fixture", SourceReference = 7, AdapterData = adapterData.RootElement };
        projections.ObserveLoadedSource(new LoadedSourceEventBody { Reason = "new", Source = source });
        projections.Sources.Should().ContainSingle().Which.Key.Should().Be("ref:7");
        projections.Sources.Single().AdapterData!.Value.GetProperty("vendor").GetString().Should().Be("opaque");
        projections.ObserveLoadedSource(new LoadedSourceEventBody { Reason = "removed", Source = source });
        projections.Sources.Should().BeEmpty();
    }

    [Fact]
    public void Targeted_invalidation_advances_only_required_generations()
    {
        var projections = new DebugSessionProjections();
        var result = projections.Invalidate(new InvalidatedEventBody
        {
            Areas = [InvalidatedAreas.Variables],
            ThreadId = 7,
            StackFrameId = 11
        });

        result.Variables.Should().Be(1);
        result.Stacks.Should().Be(0);
        result.Threads.Should().Be(0);
        result.All.Should().Be(0);
    }

    [Fact]
    public void Frame_and_thread_invalidation_revoke_only_matching_suspension_tokens()
    {
        var projections = new DebugSessionProjections();
        var frameIdentity = projections.CreateSuspensionToken(1, 10, "frame", 10);
        var frameOne = projections.CreateSuspensionToken(1, 10, "variables");
        var frameTwo = projections.CreateSuspensionToken(1, 20, "variables");
        var otherThread = projections.CreateSuspensionToken(2, 10, "variables");

        projections.Invalidate(new InvalidatedEventBody
        {
            Areas = [InvalidatedAreas.Variables], ThreadId = 1, StackFrameId = 10
        });
        projections.IsSuspensionTokenValid(frameOne).Should().BeFalse();
        projections.IsSuspensionTokenValid(frameIdentity).Should().BeTrue(
            "variable invalidation must not expire the owning stack-frame identity");
        projections.IsSuspensionTokenValid(frameTwo).Should().BeTrue();
        projections.IsSuspensionTokenValid(otherThread).Should().BeTrue();

        projections.InvalidateForContinue(1, allThreadsContinued: false);
        projections.IsSuspensionTokenValid(frameTwo).Should().BeFalse();
        projections.IsSuspensionTokenValid(otherThread).Should().BeTrue();
    }

    [Fact]
    public void Module_source_and_memory_changes_revoke_only_affected_reference_families()
    {
        var projections = new DebugSessionProjections();
        var module = projections.CreateSessionTextToken("module", "m1");
        var source = projections.CreateSourceToken(0, null, new Source { SourceReference = 7 });
        var memory = projections.CreateSessionTextToken("memory", "mem");
        var instruction = projections.CreateSessionTextToken("instruction", "0x1");
        projections.TrackMemoryRange("mem", 0, 4);

        projections.ObserveModule(new ModuleEventBody { Reason = "new", Module = Module("m1", "new") });
        var resolveModule = () => projections.ResolveTextToken(module, "module", out _, out _);
        resolveModule.Should().Throw<DebugSemanticException>();
        projections.ResolveSourceToken(source).SourceReference.Should().Be(7);

        projections.ObserveLoadedSource(new LoadedSourceEventBody
            { Reason = "new", Source = new Source { SourceReference = 7 } });
        var resolveSource = () => projections.ResolveSourceToken(source);
        resolveSource.Should().Throw<DebugSemanticException>();
        projections.ResolveTextToken(memory, "memory", out _, out _).Should().Be("mem");

        projections.ObserveMemory(new MemoryEventBody { MemoryReference = "mem", Offset = 0, Count = 4 });
        projections.ResolveTextToken(memory, "memory", out _, out _).Should().Be("mem");
        var resolveInstruction = () => projections.ResolveTextToken(instruction, "instruction", out _, out _);
        resolveInstruction.Should().Throw<DebugSemanticException>();
    }

    [Fact]
    public void Opaque_references_are_session_isolated_and_suspension_families_expire_together()
    {
        var owner = new DebugSessionProjections();
        var otherSession = new DebugSessionProjections();
        var source = owner.CreateSourceToken(7, 11, new Source { SourceReference = 9 });
        var frame = owner.CreateSuspensionToken(7, 11, "frame", 11);
        var stepTarget = owner.CreateSuspensionToken(7, 11, "stepInTarget", 12);
        var gotoTarget = owner.CreateSuspensionToken(7, 11, "gotoTarget", 13);
        var memory = owner.CreateSuspensionTextToken(7, 11, "memory", "mem");
        var instruction = owner.CreateSuspensionTextToken(7, 11, "instruction", "0x1");

        Action crossSession = () => otherSession.ResolveSourceToken(source);
        crossSession.Should().Throw<DebugSemanticException>().Which.Reason
            .Should().Be(DebugSemanticFailureReason.ReferenceExpired);

        owner.InvalidateForContinue(7, allThreadsContinued: false);
        foreach (var token in new[] { source, frame, stepTarget, gotoTarget, memory, instruction })
            owner.IsSuspensionTokenValid(token).Should().BeFalse();
    }

    [Fact]
    public void Memory_invalidation_is_reference_specific_overlap_safe_and_overflow_safe()
    {
        var projections = new DebugSessionProjections();
        var matching = projections.TrackMemoryRange("a", long.MaxValue - 8, 8);
        var otherReference = projections.TrackMemoryRange("b", long.MaxValue - 8, 8);
        var nonOverlapping = projections.TrackMemoryRange("a", 0, 8);

        projections.ObserveMemory(new MemoryEventBody
        {
            MemoryReference = "a",
            Offset = long.MaxValue - 4,
            Count = long.MaxValue
        }).Should().Be(1);

        projections.ContainsMemoryRange(matching).Should().BeFalse();
        projections.ContainsMemoryRange(otherReference).Should().BeTrue();
        projections.ContainsMemoryRange(nonOverlapping).Should().BeTrue();
        projections.Generations.Memory.Should().Be(1);
    }

    [Fact]
    public void Output_is_categorized_sanitized_bounded_and_hides_telemetry_by_default()
    {
        var output = new DebugOutputBuffer(maximumRetainedBytes: 24, maximumRecordBytes: 16, maximumRecords: 2);
        output.Append("tree", "session", new OutputEventBody { Category = "stdout", Output = "\u001b[31mred\u001b[0m\u0001" }, allowAnsi: false);
        output.Append("tree", "session", new OutputEventBody { Category = "telemetry", Output = "secret" }, allowAnsi: false);
        output.Append("tree", "session", new OutputEventBody { Category = "stderr", Output = "failure" }, allowAnsi: false);

        var visible = output.Snapshot();
        visible.Records.Should().ContainSingle().Which.Category.Should().Be(DebugOutputCategory.StandardError);
        visible.DroppedRecords.Should().Be(1);
        output.Snapshot(includeTelemetry: true).Records.Should().HaveCount(2);

        var sanitized = DebugOutputSanitizer.Sanitize("\u001b[31mred\u001b[0m\u0001", allowAnsi: false);
        sanitized.Should().Be("red");
        DebugOutputSanitizer.Sanitize("\u001b[31mred\u001b[0m\u0001", allowAnsi: true)
            .Should().Be("\u001b[31mred\u001b[0m");
    }

    [Fact]
    public void Output_truncation_respects_utf8_boundaries()
    {
        var output = new DebugOutputBuffer(maximumRetainedBytes: 16, maximumRecordBytes: 5, maximumRecords: 2);
        var record = output.Append("tree", "session", new OutputEventBody { Output = "ééé" }, allowAnsi: false);
        record.DebugTreeId.Should().Be("tree");
        record.DebugSessionId.Should().Be("session");
        record.Text.Should().Be("éé");
        record.Utf8Bytes.Should().Be(4);
        record.Truncated.Should().BeTrue();
    }

    [Fact]
    public void Progress_cancellation_does_not_remove_state_before_progress_end()
    {
        var progress = new DebugProgressProjection();
        progress.Start(new ProgressStartEventBody
        {
            ProgressId = "p1",
            Title = "work",
            Cancellable = true
        });
        progress.MarkCancellationRequested("p1").Should().BeTrue();
        progress.Snapshot.Should().ContainSingle().Which.CancellationRequested.Should().BeTrue();
        progress.Update(new ProgressUpdateEventBody { ProgressId = "p1", Percentage = 101 });
        progress.Snapshot.Single().Percentage.Should().Be(100);
        progress.End(new ProgressEndEventBody { ProgressId = "p1" }).Should().NotBeNull();
        progress.Snapshot.Should().BeEmpty();
    }

    [Fact]
    public void Progress_orphans_expire_without_requiring_an_adapter_update()
    {
        using var progress = new DebugProgressProjection();
        var started = progress.Start(new ProgressStartEventBody { ProgressId = "orphan", Title = "work" });
        progress.ExpireBefore(started.UpdatedAt + TimeSpan.FromTicks(1));
        progress.Snapshot.Should().BeEmpty();
    }

    [Fact]
    public void Opaque_references_are_kind_checked_and_expire_with_the_thread_epoch()
    {
        var projections = new DebugSessionProjections();
        var token = projections.CreateSuspensionToken(7, 11, "variables", 42);
        projections.ResolveSuspensionToken(token, "variables", out var thread, out var frame).Should().Be(42);
        thread.Should().Be(7);
        frame.Should().Be(11);
        Action wrongKind = () => projections.ResolveSuspensionToken(token, "source", out _, out _);
        wrongKind.Should().Throw<InvalidOperationException>();
        projections.InvalidateForContinue(7, allThreadsContinued: false);
        Action expired = () => projections.ResolveSuspensionToken(token, "variables", out _, out _);
        expired.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public async Task Artifact_writer_uses_explicit_scope_and_complete_ownership_tags()
    {
        var store = new InMemoryContentStore();
        var scope = ContentScope.Create("debug:test-tree");
        var writer = new DebugArtifactWriter(store, scope, new Dictionary<string, string>
        {
            ["runtime"] = "runtime",
            ["session"] = "session",
            ["thread"] = "thread",
            ["debug-tree"] = "tree"
        });

        var result = await writer.WriteTextAsync("output", "debug-output", "stderr", "adapter", "protocol-session", 1024);
        result.Status.Should().Be(DebugArtifactWriteStatus.Stored);
        result.Address!.Value.Scope.Should().Be(scope);
        var info = await store.StatAsync(result.Address.Value);
        info!.Tags.Should().Contain("adapter", "adapter").And.Contain("debug-session", "protocol-session")
            .And.Contain("debug-tree", "tree").And.Contain("category", "stderr");
    }

    [Fact]
    public async Task Artifact_writer_returns_bounded_fallback_when_store_is_missing()
    {
        var writer = new DebugArtifactWriter(null, ContentScope.Create("debug:test"),
            new Dictionary<string, string>());
        var result = await writer.WriteTextAsync(new string('x', 5000), "debug-output", null,
            "adapter", "session", 10_000);
        result.Status.Should().Be(DebugArtifactWriteStatus.ContentStoreUnavailable);
        result.Preview.Should().HaveLength(4096);
    }

    [Fact]
    public async Task Artifact_writer_keeps_bounded_fallback_when_store_fails()
    {
        var writer = new DebugArtifactWriter(new FailingContentStore(), ContentScope.Create("debug:test"),
            new Dictionary<string, string>());
        var result = await writer.WriteTextAsync("output", "debug-output", null,
            "adapter", "session", 1024);
        result.Status.Should().Be(DebugArtifactWriteStatus.ContentStoreFailed);
        result.Preview.Should().Be("output");
    }

    [Fact]
    public async Task Output_coalescer_preserves_boundaries_and_never_exceeds_live_byte_limit()
    {
        var batches = new List<DebugOutputBatch>();
        await using var coalescer = new DebugOutputEventCoalescer(batch =>
        {
            lock (batches) batches.Add(batch);
            return ValueTask.CompletedTask;
        });
        var buffer = new DebugOutputBuffer();
        coalescer.TryEnqueue(buffer.Append("tree", "session", new OutputEventBody { Category = "stdout", Output = new string('a', 9000) }, false));
        coalescer.TryEnqueue(buffer.Append("tree", "session", new OutputEventBody { Category = "stdout", Output = new string('b', 9000) }, false));
        coalescer.TryEnqueue(buffer.Append("tree", "session", new OutputEventBody { Category = "stderr", Output = "error" }, false));
        await WaitUntilAsync(() => { lock (batches) return batches.Count == 3; });

        lock (batches)
        {
            batches.Select(x => System.Text.Encoding.UTF8.GetByteCount(x.Text))
                .Should().OnlyContain(bytes => bytes <= DebugOutputEventCoalescer.MaximumLiveEventBytes);
            batches.Select(x => x.Category).Should().Equal(
                DebugOutputCategory.StandardOutput, DebugOutputCategory.StandardOutput, DebugOutputCategory.StandardError);
        }
    }

    [Fact]
    public async Task Progress_coalescer_keeps_start_update_end_order_and_collapses_updates()
    {
        var notifications = new List<DebugProgressNotification>();
        await using var coalescer = new DebugProgressEventCoalescer(notification =>
        {
            lock (notifications) notifications.Add(notification);
            return ValueTask.CompletedTask;
        });
        var started = new DebugProgressSnapshot("p", "work", null, null, null, true,
            DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, false);
        coalescer.TryEnqueue(new(DebugProgressNotificationKind.Started, started));
        for (var percentage = 1; percentage <= 10; percentage++)
            coalescer.TryEnqueue(new(DebugProgressNotificationKind.Updated, started with { Percentage = percentage }));
        coalescer.TryEnqueue(new(DebugProgressNotificationKind.Completed, started with { Percentage = 10 }));
        await WaitUntilAsync(() => { lock (notifications) return notifications.LastOrDefault()?.Kind == DebugProgressNotificationKind.Completed; });

        lock (notifications)
        {
            notifications.Select(x => x.Kind).Should().Equal(
                DebugProgressNotificationKind.Started,
                DebugProgressNotificationKind.Updated,
                DebugProgressNotificationKind.Completed);
            notifications[1].State.Percentage.Should().Be(10);
        }
    }

    private static HPD.Agent.ToolHarness.Coding.Debugging.Protocol.Generated.Module Module(string id, string name)
        => new() { Id = JsonSerializer.SerializeToElement(id), Name = name };

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        var deadline = DateTimeOffset.UtcNow + TimeSpan.FromSeconds(3);
        while (!condition())
        {
            if (DateTimeOffset.UtcNow >= deadline) throw new TimeoutException();
            await Task.Delay(10);
        }
    }

    private sealed class FailingContentStore : IContentStore
    {
        public ValueTask<ContentInfo> WriteAsync(ContentScope scope, Stream data, ContentMetadata metadata,
            ContentWriteOptions options, CancellationToken cancellationToken = default)
            => ValueTask.FromException<ContentInfo>(new IOException("store unavailable"));
        public ValueTask<ContentReadResult?> OpenReadAsync(ContentAddress address, CancellationToken cancellationToken = default)
            => ValueTask.FromResult<ContentReadResult?>(null);
        public ValueTask<Uri?> CreateReadUriAsync(ContentAddress address, TimeSpan expiresIn, CancellationToken cancellationToken = default)
            => ValueTask.FromResult<Uri?>(null);
        public ValueTask<ContentInfo?> StatAsync(ContentAddress address, CancellationToken cancellationToken = default)
            => ValueTask.FromResult<ContentInfo?>(null);
        public ValueTask DeleteAsync(ContentAddress address, CancellationToken cancellationToken = default)
            => ValueTask.CompletedTask;
        public ValueTask<IReadOnlyList<ContentInfo>> QueryAsync(ContentScope scope, ContentQuery? query = null,
            CancellationToken cancellationToken = default)
            => ValueTask.FromResult<IReadOnlyList<ContentInfo>>([]);
    }
}
