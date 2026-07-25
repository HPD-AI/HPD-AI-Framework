using HPD.Agent.ToolHarness.Coding.Debugging;
using HPD.Agent.ToolHarness.Coding.Debugging.Protocol.Generated;

namespace HPD.Agent.ToolHarness.Coding.Tests;

public sealed class DebugBreakpointStoreTests
{
    [Fact]
    public void Exception_filters_are_validated_against_negotiated_capabilities()
    {
        var capabilities = new Capabilities
        {
            ExceptionBreakpointFilters =
            [
                new ExceptionBreakpointsFilter
                {
                    Filter = "all",
                    Label = "All exceptions",
                    Default = true,
                    SupportsCondition = false
                },
                new ExceptionBreakpointsFilter
                {
                    Filter = "user-unhandled",
                    Label = "User unhandled",
                    SupportsCondition = true
                }
            ]
        };

        var metadata = DebugExceptionBreakpointValidator.Validate(
            capabilities,
            [new("user-unhandled", "exception.Message != null")]);

        metadata.Select(item => item.FilterId).Should()
            .Equal("all", "user-unhandled");
        metadata[0].IsDefault.Should().BeTrue();
        metadata[1].SupportsCondition.Should().BeTrue();
    }

    [Theory]
    [InlineData("unknown", null)]
    [InlineData("all", "condition")]
    public void Invalid_exception_filters_return_bounded_recovery_metadata(
        string filterId,
        string? condition)
    {
        var capabilities = new Capabilities
        {
            ExceptionBreakpointFilters =
            [
                new ExceptionBreakpointsFilter
                {
                    Filter = "all",
                    Label = "All exceptions",
                    SupportsCondition = false
                }
            ]
        };

        var action = () => DebugExceptionBreakpointValidator.Validate(
            capabilities,
            [new(filterId, condition)]);

        var failure = action.Should()
            .Throw<DebugExceptionBreakpointValidationException>().Which;
        failure.AvailableFilters.Should().ContainSingle()
            .Which.FilterId.Should().Be("all");
    }

    [Fact]
    public void Duplicate_exception_filters_are_rejected_ordinally()
    {
        var capabilities = new Capabilities
        {
            ExceptionBreakpointFilters =
            [
                new ExceptionBreakpointsFilter
                {
                    Filter = "all",
                    Label = "All exceptions"
                }
            ]
        };

        var action = () => DebugExceptionBreakpointValidator.Validate(
            capabilities,
            [new("all"), new("all")]);

        action.Should().Throw<DebugExceptionBreakpointValidationException>()
            .WithMessage("*Duplicate*");
    }

    [Fact]
    public void Nonempty_exception_filters_require_advertised_capabilities()
    {
        DebugExceptionBreakpointValidator.Validate(new Capabilities(), [])
            .Should().BeEmpty();

        var action = () => DebugExceptionBreakpointValidator.Validate(
            new Capabilities(),
            [new("all")]);

        action.Should().Throw<DebugExceptionBreakpointValidationException>();
    }

    [Fact]
    public async Task Invalid_exception_replacement_preserves_desired_state()
    {
        await using var store = new DebugBreakpointStore();
        store.Seed(new DebugInitialConfiguration
        {
            ExceptionFilters = [new("all")]
        });
        var capabilities = new Capabilities
        {
            ExceptionBreakpointFilters =
            [
                new ExceptionBreakpointsFilter
                {
                    Filter = "all",
                    Label = "All exceptions"
                }
            ]
        };

        var replace = async () => await store.ReplaceExceptionAsync(
            [new("unknown")],
            (_, replacement, _) =>
            {
                DebugExceptionBreakpointValidator.Validate(
                    capabilities,
                    replacement);
                return ValueTask.CompletedTask;
            });

        await replace.Should()
            .ThrowAsync<DebugExceptionBreakpointValidationException>();
        store.Snapshot.Exception.Should().ContainSingle()
            .Which.FilterId.Should().Be("all");
    }

    [Fact]
    public void Duplicate_advertised_filters_are_bounded_without_crashing()
    {
        var capabilities = new Capabilities
        {
            ExceptionBreakpointFilters =
            [
                new ExceptionBreakpointsFilter
                {
                    Filter = "all",
                    Label = "All\u0001 exceptions"
                },
                new ExceptionBreakpointsFilter
                {
                    Filter = "all",
                    Label = "Duplicate"
                }
            ]
        };

        var metadata = DebugExceptionBreakpointValidator.Validate(
            capabilities,
            [new("all")]);

        metadata.Should().ContainSingle();
        metadata[0].Label.Should().Be("All exceptions");
    }

    [Fact]
    public void Successful_exception_configuration_is_acknowledged_when_response_omits_breakpoints()
    {
        var store = new DebugAdapterBreakpointStateStore();

        store.ReplaceException([new("all")], []);

        var binding = store.Snapshot.Should().ContainSingle().Subject;
        binding.Kind.Should().Be(DebugBreakpointKind.Exception);
        binding.RequestedName.Should().Be("all");
        binding.Acknowledged.Should().BeTrue();
        binding.Verified.Should().BeTrue();
    }

    [Fact]
    public void Output_snapshot_ranges_are_exact_and_reject_dropped_prefixes()
    {
        var buffer = new DebugOutputBuffer(maximumRetainedBytes: 8, maximumRecordBytes: 4, maximumRecords: 2);
        buffer.Append("tree", "session", new OutputEventBody { Output = "one" }, allowAnsi: false);
        buffer.Append("tree", "session", new OutputEventBody { Output = "two" }, allowAnsi: false);
        buffer.Append("tree", "session", new OutputEventBody { Output = "tri" }, allowAnsi: false);

        var range = buffer.Snapshot(fromSequence: 2, toSequence: 2);
        range.Records.Should().ContainSingle().Which.Text.Should().Be("two");
        range.OldestSequence.Should().Be(2);
        range.NewestSequence.Should().Be(2);

        var stale = () => buffer.Snapshot(fromSequence: 1, toSequence: 2);
        stale.Should().Throw<InvalidOperationException>().WithMessage("*oldest retained sequence is 2*");
    }

    [Fact]
    public async Task Concurrent_replacements_are_serialized_and_commit_in_adapter_order()
    {
        await using var store = new DebugBreakpointStore();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var applied = new List<string>();

        var first = store.ReplaceFunctionAsync(
            [new("first")],
            async (_, items, _) =>
            {
                entered.SetResult();
                await release.Task;
                applied.Add(items[0].Name);
            }).AsTask();
        await entered.Task;
        var second = store.ReplaceFunctionAsync(
            [new("second")],
            (_, items, _) =>
            {
                applied.Add(items[0].Name);
                return ValueTask.CompletedTask;
            }).AsTask();

        second.IsCompleted.Should().BeFalse();
        release.SetResult();
        await Task.WhenAll(first, second);

        applied.Should().Equal("first", "second");
        store.Snapshot.Function.Should().ContainSingle().Which.Name.Should().Be("second");
    }

    [Fact]
    public async Task Concurrent_read_modify_write_additions_do_not_lose_updates()
    {
        await using var store = new DebugBreakpointStore();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var first = store.MutateFunctionAsync(
            current => [.. current, new("first")],
            async (_, _, _) => { entered.SetResult(); await release.Task; }).AsTask();
        await entered.Task;
        var second = store.MutateFunctionAsync(
            current => [.. current, new("second")],
            static (_, _, _) => ValueTask.CompletedTask).AsTask();

        release.SetResult();
        await Task.WhenAll(first, second);

        store.Snapshot.Function.Select(x => x.Name).Should().Equal("first", "second");
    }

    [Fact]
    public async Task Empty_replacement_is_sent_and_failed_replacement_does_not_change_desired_state()
    {
        await using var store = new DebugBreakpointStore();
        store.Seed(new() { SourceBreakpoints = [new("/workspace/a.cs", 10)] });
        var observedCount = -1;

        await store.ReplaceSourceAsync([], (_, items, _) =>
        {
            observedCount = items.Count;
            return ValueTask.CompletedTask;
        });
        observedCount.Should().Be(0);
        store.Snapshot.Source.Should().BeEmpty();

        var replace = async () => await store.ReplaceSourceAsync(
            [new("/workspace/b.cs", 20)],
            (_, _, _) => ValueTask.FromException(new InvalidOperationException("adapter rejected")));
        await replace.Should().ThrowAsync<InvalidOperationException>();
        store.Snapshot.Source.Should().BeEmpty();
    }

    [Fact]
    public void Confirmed_state_reconciles_by_adapter_id_and_remains_store_local()
    {
        var root = new DebugAdapterBreakpointStateStore();
        var child = new DebugAdapterBreakpointStateStore();
        root.ReplaceSource(
            "/workspace/a.cs",
            [new DebugSourceBreakpoint("/workspace/a.cs", 10)],
            [new Breakpoint { Id = 7, Verified = false, Source = new Source { Path = "/workspace/a.cs" }, Line = 10 }]);

        var changed = root.Reconcile("changed", new Breakpoint
        {
            Id = 7,
            Verified = true,
            Source = new Source { Path = "/workspace/a.cs" },
            Line = 12,
            Message = "moved"
        });

        root.Snapshot.Should().ContainSingle().Which.Should().Match<DebugBreakpointBindingState>(x =>
            x.AdapterId == 7 &&
            x.Acknowledged &&
            x.Verified &&
            x.RequestedLine == 10 &&
            x.ResolvedLine == 12 &&
            x.Message == "moved");
        child.Snapshot.Should().BeEmpty();
        changed.ClientBreakpointId.Should().Be(root.Snapshot.Single().ClientBreakpointId);
        changed.ResolvedLine.Should().Be(12);

        var removed = root.Reconcile("removed", new Breakpoint { Id = 7, Verified = false });
        removed.ClientBreakpointId.Should().Be(changed.ClientBreakpointId);
        removed.Change.Should().Be(DebugBreakpointChangeKind.Removed);
        root.Snapshot.Should().BeEmpty();
    }

    [Fact]
    public void Requested_source_location_is_preserved_when_adapter_relocates_breakpoint()
    {
        var store = new DebugAdapterBreakpointStateStore();

        store.ReplaceSource(
            "/workspace/a.cs",
            [new DebugSourceBreakpoint("/workspace/a.cs", 10, 3, "ready", "2", "value={value}")],
            [new Breakpoint
            {
                Id = 9,
                Verified = true,
                Source = new Source { Path = "/workspace/generated/a.cs" },
                Line = 12,
                Column = 1
            }]);

        store.Snapshot.Should().ContainSingle().Which.Should().Match<DebugBreakpointBindingState>(item =>
            item.ClientBreakpointId.Length == 24 &&
            item.RequestedPath == "/workspace/a.cs" &&
            item.RequestedLine == 10 &&
            item.RequestedColumn == 3 &&
            item.ResolvedPath == "/workspace/generated/a.cs" &&
            item.ResolvedLine == 12 &&
            item.ResolvedColumn == 1 &&
            item.Acknowledged &&
            item.Verified);
    }

    [Fact]
    public void Missing_adapter_responses_leave_requested_breakpoints_explicitly_pending()
    {
        var store = new DebugAdapterBreakpointStateStore();

        store.ReplaceFunction(
            [new DebugFunctionBreakpoint("One"), new DebugFunctionBreakpoint("Two")],
            [new Breakpoint { Id = 1, Verified = true }]);

        store.Snapshot.Should().HaveCount(2);
        store.Snapshot[0].Should().Match<DebugBreakpointBindingState>(item =>
            item.RequestedName == "One" && item.Acknowledged && item.Verified);
        store.Snapshot[1].Should().Match<DebugBreakpointBindingState>(item =>
            item.RequestedName == "Two" && !item.Acknowledged && !item.Verified &&
            item.AdapterId == null);
    }

    [Fact]
    public void Extra_adapter_responses_are_retained_only_as_bounded_diagnostics()
    {
        var store = new DebugAdapterBreakpointStateStore();

        store.ReplaceFunction(
            [new DebugFunctionBreakpoint("One")],
            [
                new Breakpoint { Id = 1, Verified = true },
                new Breakpoint { Id = 2, Verified = false, Message = new string('x', 400) }
            ]);

        store.Snapshot.Should().ContainSingle();
        store.UnmatchedResponses.Should().ContainSingle().Which.Should()
            .Match<DebugUnmatchedAdapterBreakpointDiagnostic>(item =>
                item.Kind == DebugBreakpointKind.Function &&
                item.AdapterId == 2 &&
                !item.Verified &&
                item.SafeMessage!.Length == 256);
    }

    [Fact]
    public void Client_identity_is_stable_and_changes_with_semantic_intent()
    {
        var first = new DebugSourceBreakpoint("/workspace/a.cs", 10, Condition: "ready");
        var same = new DebugSourceBreakpoint("/workspace/a.cs", 10, Condition: "ready");
        var changed = new DebugSourceBreakpoint("/workspace/a.cs", 10, Condition: "other");

        BreakpointIdentity.Source(first).Should().Be(BreakpointIdentity.Source(same));
        BreakpointIdentity.Source(first).Should().NotBe(BreakpointIdentity.Source(changed));
    }

    [Fact]
    public void Adapter_hit_ids_are_correlated_to_stable_client_identities_once_per_epoch()
    {
        var store = new DebugAdapterBreakpointStateStore();
        store.ReplaceSource(
            "/workspace/a.cs",
            [new DebugSourceBreakpoint("/workspace/a.cs", 10)],
            [new Breakpoint { Id = 7, Verified = true, Line = 10 }]);

        var first = store.ObserveHits([7], suspensionEpoch: 3, stoppedForBreakpoint: true);
        var duplicate = store.ObserveHits([7], suspensionEpoch: 3, stoppedForBreakpoint: true);
        store.ObserveHits([7], suspensionEpoch: 4, stoppedForBreakpoint: true);

        first.ClientBreakpointIds.Should().ContainSingle()
            .Which.Should().Be(store.Snapshot.Single().ClientBreakpointId);
        duplicate.ClientBreakpointIds.Should().ContainSingle();
        store.RuntimeEvidence.Should().ContainSingle().Which.Should()
            .Match<DebugBreakpointRuntimeEvidence>(item =>
                item.HitCount == 2 && item.LastHitSuspensionEpoch == 4);
        store.HitCounts.Should().Be(new DebugBreakpointHitCounts(1, 0));
    }

    [Fact]
    public void Missing_adapter_hit_ids_remain_pending_until_primary_inspection_completes()
    {
        var store = new DebugAdapterBreakpointStateStore();
        store.ReplaceFunction(
            [new DebugFunctionBreakpoint("Run")],
            [new Breakpoint { Id = 4, Verified = true }]);

        var observation = store.ObserveHits(
            adapterBreakpointIds: null,
            suspensionEpoch: 8,
            stoppedForBreakpoint: true);
        var completed = store.CompleteUnknownStop(suspensionEpoch: 8);

        observation.IdentityUnknown.Should().BeFalse();
        completed.IdentityUnknown.Should().BeTrue();
        store.RuntimeEvidence.Should().BeEmpty();
        store.HitCounts.Should().Be(new DebugBreakpointHitCounts(0, 1));
    }

    [Fact]
    public async Task Child_composition_copies_only_portable_intentions_and_rediscovers_persistent_data()
    {
        var desired = new DebugDesiredBreakpointSnapshot
        {
            Source = [new("/workspace/a.cs", 10)],
            Function = [new("Run")],
            Exception = [new("all")],
            Instruction = [new("portable", Portable: true), new("bound", Portable: false)],
            Data =
            [
                new("root-data", CanPersist: true, Recipe: new("field"), OriginSessionId: "root", SuspensionEpoch: 3),
                new("frame-data", CanPersist: false, Recipe: new("local"), OriginSessionId: "root", SuspensionEpoch: 3)
            ]
        };

        var child = await DebugChildBreakpointComposer.ComposeAsync(
            desired,
            instructionReferencesArePortable: true,
            (recipe, _) => ValueTask.FromResult<DebugDataBreakpoint?>(new("child-" + recipe.Name)),
            CancellationToken.None);

        child.Source.Should().Equal(desired.Source);
        child.Function.Should().Equal(desired.Function);
        child.Exception.Should().Equal(desired.Exception);
        child.Instruction.Should().ContainSingle().Which.InstructionReference.Should().Be("portable");
        child.Data.Should().ContainSingle().Which.Should().Match<DebugDataBreakpoint>(x =>
            x.DataId == "child-field" && x.OriginSessionId == null && x.SuspensionEpoch == null && x.CanPersist);
    }

    [Fact]
    public async Task Persistent_data_recipe_without_rediscovery_provider_fails_explicitly()
    {
        var resolver = new PortableDebugChildBreakpointResolver();
        var desired = new DebugDesiredBreakpointSnapshot
        {
            Data = [new("root-data", CanPersist: true, Recipe: new("field"), OriginSessionId: "root")]
        };

        var compose = async () => await resolver.ComposeAsync(desired, null!, null!, CancellationToken.None);
        await compose.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*no child-session rediscovery provider*");
    }

    [Fact]
    public void Source_stop_correlates_unique_resolved_location_after_relocation()
    {
        var store = new DebugAdapterBreakpointStateStore();
        store.ReplaceSource(
            "/workspace/Program.cs",
            [new DebugSourceBreakpoint("/workspace/Program.cs", 32)],
            [new Breakpoint
            {
                Id = 7,
                Verified = true,
                Source = new Source { Path = "/workspace/Program.cs" },
                Line = 34
            }]);

        var observation = store.ObserveSourceStop(
            "/workspace/Program.cs", 34, column: null, suspensionEpoch: 5);

        observation.IdentityUnknown.Should().BeFalse();
        observation.ClientBreakpointIds.Should().ContainSingle()
            .Which.Should().Be(store.Snapshot.Single().ClientBreakpointId);
        store.HitCounts.Should().Be(new DebugBreakpointHitCounts(1, 0));
        store.BreakpointStopCount.Should().Be(1);
    }

    [Fact]
    public void Source_stop_column_does_not_reject_unique_line_breakpoint_without_column_constraint()
    {
        var store = new DebugAdapterBreakpointStateStore();
        store.ReplaceSource(
            "/workspace/Program.cs",
            [new DebugSourceBreakpoint("/workspace/Program.cs", 21)],
            [new Breakpoint
            {
                Id = 7,
                Verified = true,
                Source = new Source { Path = "/workspace/Program.cs" },
                Line = 21
            }]);

        var observation = store.ObserveSourceStop(
            "/workspace/Program.cs", 21, column: 9, suspensionEpoch: 5);

        observation.IdentityUnknown.Should().BeFalse();
        observation.ClientBreakpointIds.Should().ContainSingle()
            .Which.Should().Be(store.Snapshot.Single().ClientBreakpointId);
        store.HitCounts.Should().Be(new DebugBreakpointHitCounts(1, 0));
    }

    [Fact]
    public void Ambiguous_source_stop_remains_unidentified()
    {
        var store = new DebugAdapterBreakpointStateStore();
        store.ReplaceSource(
            "/workspace/Program.cs",
            [
                new DebugSourceBreakpoint("/workspace/Program.cs", 32, 1),
                new DebugSourceBreakpoint("/workspace/Program.cs", 32, 5)
            ],
            [
                new Breakpoint { Id = 7, Verified = true, Source = new Source { Path = "/workspace/Program.cs" }, Line = 34 },
                new Breakpoint { Id = 8, Verified = true, Source = new Source { Path = "/workspace/Program.cs" }, Line = 34 }
            ]);

        var observation = store.ObserveSourceStop(
            "/workspace/Program.cs", 34, column: null, suspensionEpoch: 6);

        observation.IdentityUnknown.Should().BeTrue();
        store.HitCounts.Should().Be(new DebugBreakpointHitCounts(0, 1));
    }
}
