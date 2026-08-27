namespace HPD.Agent.Tests.Operations;

public sealed class AgentOperationTests
{
    [Fact]
    public async Task ProviderAndObservationTransitionsRemainIndependent()
    {
        await using var operation = Create();

        var running = await operation.TransitionAsync(
            new AgentOperationTransition { ProviderStatus = AgentOperationProviderStatus.Running },
            0,
            default);
        var detached = await operation.TransitionAsync(
            new AgentOperationTransition { ObservationStatus = AgentOperationObservationStatus.Detached },
            1,
            default);

        Assert.Equal(AgentOperationProviderStatus.Running, detached.Snapshot.ProviderStatus);
        Assert.Equal(AgentOperationObservationStatus.Detached, detached.Snapshot.ObservationStatus);
        Assert.Null(detached.Snapshot.FinishedAt);
        Assert.True(running.Applied);
    }

    [Fact]
    public async Task VersionConflictDoesNotMutateOperation()
    {
        await using var operation = Create();

        var exception = await Assert.ThrowsAsync<AgentOperationVersionConflictException>(() =>
            operation.TransitionAsync(
                new AgentOperationTransition { ProviderStatus = AgentOperationProviderStatus.Running },
                7,
                default).AsTask());

        Assert.Equal(7, exception.ExpectedVersion);
        Assert.Equal(0, exception.ActualVersion);
        Assert.Equal(AgentOperationProviderStatus.Accepted, operation.Snapshot.ProviderStatus);
    }

    [Fact]
    public async Task DuplicateProviderStateKeyIsIdempotent()
    {
        await using var operation = Create();
        var first = await operation.TransitionAsync(
            new AgentOperationTransition
            {
                ProviderStatus = AgentOperationProviderStatus.Running,
                ProviderDeduplicationKey = "provider-version-2"
            },
            0,
            default);
        var duplicate = await operation.TransitionAsync(
            new AgentOperationTransition
            {
                ProviderStatus = AgentOperationProviderStatus.Running,
                ProviderDeduplicationKey = "provider-version-2"
            },
            1,
            default);

        Assert.True(first.Applied);
        Assert.False(duplicate.Applied);
        Assert.Equal(1, duplicate.Snapshot.Version);
    }

    [Fact]
    public async Task TerminalTransitionRequiresMatchingPayloadAndClearsRecovery()
    {
        await using var operation = Create();
        await operation.TransitionAsync(
            new AgentOperationTransition { ProviderStatus = AgentOperationProviderStatus.Running },
            0,
            default);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            operation.TransitionAsync(
                new AgentOperationTransition { ProviderStatus = AgentOperationProviderStatus.Completed },
                1,
                default).AsTask());

        var completed = await operation.TransitionAsync(
            new AgentOperationTransition
            {
                ProviderStatus = AgentOperationProviderStatus.Completed,
                Completion = new AgentOperationCompletion("done")
            },
            1,
            default);

        Assert.NotNull(completed.Snapshot.FinishedAt);
        Assert.Null(completed.Snapshot.Recovery);
        Assert.Equal("done", completed.Snapshot.Completion!.Summary);
    }

    [Fact]
    public async Task RehydrateDetachesNonTerminalOperationAndRestoresProviderDeduplication()
    {
        var sink = new TestEventSink();
        await using var registry = new AgentOperationRegistry(sink);
        var initial = CreateSnapshot();
        var running = initial with
        {
            ProviderStatus = AgentOperationProviderStatus.Running,
            StartedAt = initial.RegisteredAt.AddSeconds(1),
            UpdatedAt = initial.RegisteredAt.AddSeconds(1),
            Version = 1
        };

        await registry.RehydrateAsync([
            new AgentOperationRegisteredEvent { Operation = initial },
            new AgentOperationTransitionedEvent
            {
                OperationId = initial.OperationId,
                PreviousVersion = 0,
                Operation = running,
                ProviderDeduplicationKey = "remote-v1"
            }
        ]);

        var operation = Assert.Single(registry.Snapshot());
        Assert.Equal(AgentOperationProviderStatus.Running, operation.ProviderStatus);
        Assert.Equal(AgentOperationObservationStatus.Detached, operation.ObservationStatus);
        Assert.True(registry.TryGet(initial.OperationId, out var aggregate));
        var duplicate = await aggregate!.TransitionAsync(
            new AgentOperationTransition
            {
                ProviderStatus = AgentOperationProviderStatus.Running,
                ProviderDeduplicationKey = "remote-v1"
            },
            1,
            default);
        Assert.False(duplicate.Applied);
    }

    [Fact]
    public async Task CompactionJournalsTombstoneThenEvictionAndRejectsLateControl()
    {
        var sink = new TestEventSink();
        var retention = new AgentOperationRetentionPolicy
        {
            TerminalRetention = TimeSpan.FromMinutes(1),
            ProviderDeduplicationRetention = TimeSpan.FromMinutes(2),
            MaximumTerminalOperationsPerThread = 1
        };
        await using var registry = new AgentOperationRegistry(sink, retention);
        var initial = CreateSnapshot();
        var operation = await registry.RegisterAsync(initial);
        await operation.TransitionAsync(
            new AgentOperationTransition
            {
                ProviderStatus = AgentOperationProviderStatus.Running,
                ProviderDeduplicationKey = "remote-running",
                Timestamp = initial.RegisteredAt.AddSeconds(1)
            },
            0,
            default);
        await operation.TransitionAsync(
            new AgentOperationTransition
            {
                ProviderStatus = AgentOperationProviderStatus.Completed,
                Completion = new AgentOperationCompletion("done"),
                ProviderDeduplicationKey = "remote-final",
                Timestamp = initial.RegisteredAt.AddMinutes(1)
            },
            1,
            default);

        await registry.CompactAsync(initial.RegisteredAt.AddMinutes(3), default);
        var tombstone = Assert.Single(registry.Tombstones());
        Assert.Contains("remote-final", tombstone.ProviderDeduplicationKeys);
        Assert.Contains(sink.Events, static evt => evt is AgentOperationTombstonedEvent);
        await Assert.ThrowsAsync<KeyNotFoundException>(() =>
            registry.RequestCancellationAsync(initial.OperationId, default).AsTask());

        await registry.CompactAsync(initial.RegisteredAt.AddMinutes(4), default);
        Assert.Empty(registry.Tombstones());
        Assert.Contains(sink.Events, static evt => evt is AgentOperationTombstoneEvictedEvent);
    }

    [Fact]
    public async Task JournalPrefixForkAndRebasePreserveIdentityWithoutRecreatingTerminalOperation()
    {
        var sourceSink = new TestEventSink();
        var retention = new AgentOperationRetentionPolicy
        {
            TerminalRetention = TimeSpan.Zero,
            ProviderDeduplicationRetention = TimeSpan.FromMinutes(1)
        };
        await using var source = new AgentOperationRegistry(sourceSink, retention);
        var initial = CreateSnapshot();
        var operation = await source.RegisterAsync(initial);
        await operation.TransitionAsync(new AgentOperationTransition
        {
            ProviderStatus = AgentOperationProviderStatus.Running,
            ProviderDeduplicationKey = "provider-running",
            Timestamp = initial.RegisteredAt.AddMilliseconds(500)
        }, 0, default);
        await operation.TransitionAsync(new AgentOperationTransition
        {
            ProviderStatus = AgentOperationProviderStatus.Completed,
            Completion = new AgentOperationCompletion("done"),
            ProviderDeduplicationKey = "provider-final",
            Timestamp = initial.RegisteredAt.AddSeconds(1)
        }, 1, default);
        var forkPrefix = sourceSink.Events.ToArray();
        await source.CompactAsync(initial.RegisteredAt.AddSeconds(2), default);

        await using var fork = new AgentOperationRegistry(new TestEventSink(), retention);
        await fork.RehydrateAsync(forkPrefix);
        Assert.Equal(initial.OperationId, Assert.Single(fork.Snapshot()).OperationId);

        await fork.RehydrateAsync(sourceSink.Events);
        Assert.Empty(fork.Snapshot());
        Assert.Equal(initial.OperationId, Assert.Single(fork.Tombstones()).OperationId);

        await fork.RehydrateAsync(sourceSink.Events);
        Assert.Empty(fork.Snapshot());
        Assert.Single(fork.Tombstones());
    }

    [Fact]
    public async Task EvictedTombstoneRejectsLateTransitionAndNeverRecreatesOperation()
    {
        var initial = CreateSnapshot();
        var tombstone = new AgentOperationTombstone
        {
            OperationId = initial.OperationId,
            Address = initial.Address,
            ProviderDeduplicationKeys = ["provider-final"],
            ProviderStatus = AgentOperationProviderStatus.Completed,
            FinishedAt = initial.RegisteredAt.AddSeconds(1),
            FinalVersion = 1
        };
        var late = initial with
        {
            ProviderStatus = AgentOperationProviderStatus.Running,
            UpdatedAt = initial.RegisteredAt.AddMinutes(3),
            Version = 2
        };
        await using var registry = new AgentOperationRegistry(new TestEventSink());

        await registry.RehydrateAsync([
            new AgentOperationRegisteredEvent { Operation = initial },
            new AgentOperationTombstonedEvent { Tombstone = tombstone },
            new AgentOperationTombstoneEvictedEvent
            {
                OperationId = initial.OperationId,
                EvictedAt = initial.RegisteredAt.AddMinutes(2)
            },
            new AgentOperationTransitionedEvent
            {
                OperationId = initial.OperationId,
                PreviousVersion = 1,
                Operation = late,
                ProviderDeduplicationKey = "late-provider-update"
            }
        ]);

        Assert.Empty(registry.Snapshot());
        Assert.Empty(registry.Tombstones());
        await Assert.ThrowsAsync<KeyNotFoundException>(() =>
            registry.RequestCancellationAsync(initial.OperationId, default).AsTask());
    }

    [Fact]
    public async Task Shutdown_ForcesNonCooperativeLocalOperationToTerminalFailure()
    {
        var sink = new TestEventSink();
        var registry = new AgentOperationRegistry(sink);
        var controller = new RecordingController();
        var initial = CreateSnapshot() with
        {
            SourceKind = AgentOperationSourceKind.LocalTool,
            ProviderOperationId = null,
            Recovery = null
        };
        await registry.RegisterAsync(initial, controller);

        await registry.ShutdownAsync(FastShutdown());

        var snapshot = Assert.Single(registry.Snapshot());
        Assert.Equal(AgentOperationProviderStatus.Failed, snapshot.ProviderStatus);
        Assert.Equal(AgentOperationObservationStatus.Stopped, snapshot.ObservationStatus);
        Assert.Equal("shutdown_deadline_exceeded", snapshot.Failure?.Code);
        Assert.Equal(1, controller.CancellationRequests);
        Assert.Equal(1, controller.DisposeCount);
    }

    [Fact]
    public async Task Shutdown_DetachesRemoteOperationWithoutCancellingProviderByDefault()
    {
        var sink = new TestEventSink();
        var registry = new AgentOperationRegistry(sink);
        var controller = new RecordingController();
        await registry.RegisterAsync(CreateSnapshot(), controller);

        await registry.ShutdownAsync(FastShutdown());

        var snapshot = Assert.Single(registry.Snapshot());
        Assert.Equal(AgentOperationProviderStatus.Accepted, snapshot.ProviderStatus);
        Assert.Equal(AgentOperationObservationStatus.Detached, snapshot.ObservationStatus);
        Assert.Equal(0, controller.CancellationRequests);
        Assert.Equal(1, controller.DisposeCount);
    }

    [Fact]
    public async Task Shutdown_RequestCancellationPolicyCancelsThenDetachesRemoteOperation()
    {
        var registry = new AgentOperationRegistry(new TestEventSink());
        var controller = new RecordingController();
        await registry.RegisterAsync(CreateSnapshot(), controller);

        await registry.ShutdownAsync(FastShutdown() with
        {
            RemoteOperations = AgentRemoteOperationShutdownPolicy.RequestCancellation
        });

        var snapshot = Assert.Single(registry.Snapshot());
        Assert.Equal(AgentOperationProviderStatus.Accepted, snapshot.ProviderStatus);
        Assert.Equal(AgentOperationObservationStatus.Detached, snapshot.ObservationStatus);
        Assert.Equal(1, controller.CancellationRequests);
        Assert.Equal(1, controller.DisposeCount);
    }

    [Fact]
    public async Task NotificationPolicyDeduplicatesTerminalDeliveryByConfiguredPolicyKey()
    {
        using var events = new HPD.Events.Core.EventCoordinator();
        var input = System.Threading.Channels.Channel.CreateUnbounded<AgentInputEvent>();
        var suppressed = 0;
        using var suppressionSubscription = events.Subscribe<AgentOperationNotificationSuppressedEvent>(
            _ => { Interlocked.Increment(ref suppressed); return ValueTask.CompletedTask; });
        using var dispatcher = new AgentOperationNotificationDispatcher(events, null, input.Writer, null);
        var first = TerminalNotificationSnapshot("op-1", "same-policy-key");
        var second = TerminalNotificationSnapshot("op-2", "same-policy-key");

        await events.EmitAsync(new AgentOperationTransitionedEvent
        {
            OperationId = first.OperationId,
            PreviousVersion = 0,
            Operation = first
        });
        await events.EmitAsync(new AgentOperationTransitionedEvent
        {
            OperationId = second.OperationId,
            PreviousVersion = 0,
            Operation = second
        });

        var delivered = await input.Reader.ReadAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(2));
        Assert.Equal("op-1", Assert.IsType<AgentOperationNotificationInputEvent>(delivered)
            .Notifications.Single().OperationId);
        Assert.False(input.Reader.TryRead(out _));
        Assert.Equal(1, suppressed);
    }

    private static AgentShutdownOptions FastShutdown() => new()
    {
        GracefulDrainTimeout = TimeSpan.FromMilliseconds(1),
        CancellationDrainTimeout = TimeSpan.FromMilliseconds(1)
    };

    private static AgentOperation Create()
    {
        return new AgentOperation(CreateSnapshot(), new TestEventSink());
    }

    private static AgentOperationSnapshot CreateSnapshot()
    {
        var now = DateTimeOffset.UtcNow;
        return new AgentOperationSnapshot
        {
            OperationId = "op-1",
            ProviderOperationId = "remote-1",
            SourceKind = AgentOperationSourceKind.McpTask,
            Name = "remote tool",
            Address = new AgentExecutionAddress("agent", "session", "thread"),
            ProviderStatus = AgentOperationProviderStatus.Accepted,
            ObservationStatus = AgentOperationObservationStatus.Attached,
            Control = new AgentOperationControl(
                "handle-1",
                AgentOperationKind.Task,
                AgentOperationCapabilities.Cancel | AgentOperationCapabilities.Update),
            Notification = new AgentOperationNotificationPolicy(),
            RegisteredAt = now,
            UpdatedAt = now,
            Recovery = new AgentOperationRecoveryReference("mcp-task-v1", "protected-reference"),
            Version = 0
        };
    }

    private static AgentOperationSnapshot TerminalNotificationSnapshot(string operationId, string key)
    {
        var initial = CreateSnapshot();
        return initial with
        {
            OperationId = operationId,
            ProviderStatus = AgentOperationProviderStatus.Completed,
            Notification = new AgentOperationNotificationPolicy
            {
                IncludeTerminal = true,
                DeduplicationKey = key
            },
            Completion = new AgentOperationCompletion("done"),
            FinishedAt = initial.UpdatedAt,
            Version = 1
        };
    }

    private sealed class TestEventSink : IAgentOperationEventSink
    {
        public List<AgentEvent> Events { get; } = [];

        public ValueTask AppendAsync(AgentEvent operationEvent, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Events.Add(operationEvent);
            return ValueTask.CompletedTask;
        }
    }

    private sealed class RecordingController : IAgentOperationController
    {
        public int CancellationRequests { get; private set; }
        public int DisposeCount { get; private set; }

        public ValueTask RequestCancellationAsync(CancellationToken cancellationToken)
        {
            CancellationRequests++;
            return ValueTask.CompletedTask;
        }

        public ValueTask SupplyInputAsync(AgentOperationInput input, CancellationToken cancellationToken) =>
            ValueTask.CompletedTask;

        public ValueTask DisposeAsync()
        {
            DisposeCount++;
            return ValueTask.CompletedTask;
        }
    }
}
