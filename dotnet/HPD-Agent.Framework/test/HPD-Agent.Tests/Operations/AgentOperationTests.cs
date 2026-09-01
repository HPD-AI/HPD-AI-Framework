using HPD.Agent.Middleware;
using Microsoft.Extensions.AI;

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

        Assert.Empty(registry.Snapshot());
        var snapshot = sink.Events.OfType<AgentOperationTransitionedEvent>().Last().Operation;
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

        Assert.Empty(registry.Snapshot());
        var snapshot = sink.Events.OfType<AgentOperationTransitionedEvent>().Last().Operation;
        Assert.Equal(AgentOperationProviderStatus.Accepted, snapshot.ProviderStatus);
        Assert.Equal(AgentOperationObservationStatus.Detached, snapshot.ObservationStatus);
        Assert.Equal(0, controller.CancellationRequests);
        Assert.Equal(1, controller.DisposeCount);
    }

    [Fact]
    public async Task Shutdown_RequestCancellationPolicyCancelsThenDetachesRemoteOperation()
    {
        var sink = new TestEventSink();
        var registry = new AgentOperationRegistry(sink);
        var controller = new RecordingController();
        await registry.RegisterAsync(CreateSnapshot(), controller);

        await registry.ShutdownAsync(FastShutdown() with
        {
            RemoteOperations = AgentRemoteOperationShutdownPolicy.RequestCancellation
        });

        Assert.Empty(registry.Snapshot());
        var snapshot = sink.Events.OfType<AgentOperationTransitionedEvent>().Last().Operation;
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
        using var dispatcher = new AgentOperationNotificationDispatcher(
            events,
            null,
            notification => new PreparedAgentWorkAdmission(
                notification with { ThreadExecutionId = Guid.NewGuid().ToString("N") },
                input.Writer),
            null);
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
        await WaitUntilAsync(() => Volatile.Read(ref suppressed) == 1, TimeSpan.FromSeconds(2));
        Assert.Equal(1, suppressed);
    }

    [Fact]
    public void NotificationConversionCreatesHiddenSystemRuntimeContextAndPreservesRouting()
    {
        var runConfig = new AgentRunConfig();
        var source = new AgentOperationNotificationInputEvent(
        [
            new AgentOperationNotification
            {
                NotificationId = "notification-1",
                OperationId = "operation-1",
                Name = "build<&\"",
                ProviderStatus = "completed",
                Summary = "done < safely",
                SourceThreadExecutionId = "source-execution"
            }
        ])
        {
            ClientInputId = "client-input",
            AgentId = "agent",
            SessionId = "session",
            ThreadId = "thread",
            ThreadExecutionId = "notification-execution",
            RunConfig = runConfig
        };

        var converted = AgentOperationNotificationDispatcher.ToNotificationTurnInput(source);
        var message = Assert.Single(converted.Messages);

        Assert.Equal(ChatRole.System, message.Role);
        Assert.Equal(AgentMessageSource.BackgroundNotification, message.GetSource());
        Assert.Equal(AgentMessageVisibility.Hidden, message.GetVisibility());
        Assert.Equal(AgentMessagePersistence.ModelContextOnly, message.GetPersistence());
        Assert.Contains("name=\"build&lt;&amp;&quot;\"", message.Text);
        Assert.Contains("<summary>done &lt; safely</summary>", message.Text);
        Assert.Equal(source.ClientInputId, converted.ClientInputId);
        Assert.Equal(source.AgentId, converted.AgentId);
        Assert.Equal(source.SessionId, converted.SessionId);
        Assert.Equal(source.ThreadId, converted.ThreadId);
        Assert.Equal(source.ThreadExecutionId, converted.ThreadExecutionId);
        Assert.Same(runConfig, converted.RunConfig);
    }

    [Fact]
    public void NotificationFormattingPreservesBatchOrder()
    {
        static AgentOperationNotification Notification(string id) => new()
        {
            NotificationId = $"notification-{id}",
            OperationId = $"operation-{id}",
            Name = $"name-{id}",
            ProviderStatus = "running"
        };

        var formatted = AgentOperationNotificationDispatcher.FormatNotifications(
            [Notification("first"), Notification("second")]);

        Assert.True(formatted.IndexOf("operation-first", StringComparison.Ordinal) <
                    formatted.IndexOf("operation-second", StringComparison.Ordinal));
    }

    [Fact]
    public async Task NotificationAdmissionSeparatesSourceAndNotificationExecutionIdentities()
    {
        using var events = new HPD.Events.Core.EventCoordinator();
        var input = System.Threading.Channels.Channel.CreateUnbounded<AgentInputEvent>();
        AgentOperationNotificationQueuedEvent? queued = null;
        using var queuedSubscription = events.Subscribe<AgentOperationNotificationQueuedEvent>(
            evt => { queued = evt; return ValueTask.CompletedTask; });
        using var dispatcher = new AgentOperationNotificationDispatcher(
            events,
            null,
            notification => new PreparedAgentWorkAdmission(
                notification with { ThreadExecutionId = "notification-execution" },
                input.Writer),
            null);
        var operation = TerminalNotificationSnapshot("operation", "policy");

        await events.EmitAsync(new AgentOperationTransitionedEvent
        {
            OperationId = operation.OperationId,
            PreviousVersion = 0,
            Operation = operation,
            ThreadExecutionId = "source-execution"
        });

        var admitted = Assert.IsType<AgentOperationNotificationInputEvent>(await input.Reader.ReadAsync());
        Assert.Equal("notification-execution", admitted.ThreadExecutionId);
        Assert.Equal("source-execution", Assert.Single(admitted.Notifications).SourceThreadExecutionId);
        Assert.NotNull(queued);
        Assert.Equal("notification-execution", queued!.ThreadExecutionId);
        Assert.Equal("source-execution", queued.Notification.SourceThreadExecutionId);
    }

    [Theory]
    [InlineData(AgentOperationProviderStatus.Completed)]
    [InlineData(AgentOperationProviderStatus.Failed)]
    [InlineData(AgentOperationProviderStatus.Cancelled)]
    public async Task TerminalTransition_ReleasesTransferredExecutionOwnerExactlyOnce(
        AgentOperationProviderStatus terminalStatus)
    {
        var owner = new RecordingAsyncDisposable();
        await using var operation = new AgentOperation(CreateSnapshot(), new TestEventSink(), executionOwner: owner);
        await operation.TransitionAsync(
            new AgentOperationTransition { ProviderStatus = AgentOperationProviderStatus.Running }, 0, default);

        var transition = terminalStatus switch
        {
            AgentOperationProviderStatus.Completed => new AgentOperationTransition
            {
                ProviderStatus = terminalStatus,
                Completion = new AgentOperationCompletion("done")
            },
            AgentOperationProviderStatus.Failed => new AgentOperationTransition
            {
                ProviderStatus = terminalStatus,
                Failure = new AgentOperationFailure("failed", "failed")
            },
            _ => new AgentOperationTransition { ProviderStatus = terminalStatus }
        };
        await operation.TransitionAsync(transition, 1, default);
        await operation.DisposeAsync();

        Assert.Equal(1, owner.DisposeCount);
    }

    [Fact]
    public async Task TerminalOwnerReleaseFailure_DoesNotEscapeDurableTransitionOrRepeatTerminalCommit()
    {
        var sink = new TestEventSink();
        var owner = new RecordingAsyncDisposable(fail: true);
        await using var operation = new AgentOperation(CreateSnapshot(), sink, executionOwner: owner);
        await operation.TransitionAsync(
            new AgentOperationTransition { ProviderStatus = AgentOperationProviderStatus.Running }, 0, default);

        var result = await operation.TransitionAsync(new AgentOperationTransition
        {
            ProviderStatus = AgentOperationProviderStatus.Completed,
            Completion = new AgentOperationCompletion("committed")
        }, 1, default);

        Assert.Equal(AgentOperationProviderStatus.Completed, result.Snapshot.ProviderStatus);
        Assert.Equal(1, owner.DisposeCount);
        Assert.Equal(2, sink.Events.OfType<AgentOperationTransitionedEvent>().Count());
    }

    [Fact]
    public async Task TerminalOwner_IsDetachedUnderTransitionLock_AndDisposedAfterLockRelease()
    {
        AgentOperation? operation = null;
        var owner = new RecordingAsyncDisposable(async () =>
        {
            var conflict = await Assert.ThrowsAsync<AgentOperationVersionConflictException>(() =>
                operation!.TransitionAsync(new AgentOperationTransition(), -1, default).AsTask());
            Assert.Equal(2, conflict.ActualVersion);
        });
        operation = new AgentOperation(CreateSnapshot(), new TestEventSink(), executionOwner: owner);
        await operation.TransitionAsync(
            new AgentOperationTransition { ProviderStatus = AgentOperationProviderStatus.Running }, 0, default);

        await operation.TransitionAsync(new AgentOperationTransition
        {
            ProviderStatus = AgentOperationProviderStatus.Completed,
            Completion = new AgentOperationCompletion("done")
        }, 1, default).AsTask().WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(1, owner.DisposeCount);
        await operation.DisposeAsync();
    }

    [Fact]
    public async Task FailedRegistration_ReleasesTransferredExecutionOwner()
    {
        var owner = new RecordingAsyncDisposable();
        await using var registry = new AgentOperationRegistry(new ThrowingEventSink());

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            registry.RegisterAsync(CreateSnapshot(), executionOwner: owner).AsTask());

        Assert.Equal(1, owner.DisposeCount);
        Assert.Empty(registry.Snapshot());
    }

    [Fact]
    public async Task CompactionRemoval_ReleasesTransferredExecutionOwnerExactlyOnce()
    {
        var owner = new RecordingAsyncDisposable();
        var retention = new AgentOperationRetentionPolicy
        {
            TerminalRetention = TimeSpan.Zero,
            ProviderDeduplicationRetention = TimeSpan.FromMinutes(1)
        };
        await using var registry = new AgentOperationRegistry(new TestEventSink(), retention);
        var operation = await registry.RegisterAsync(CreateSnapshot(), executionOwner: owner);
        await operation.TransitionAsync(new AgentOperationTransition
        {
            ProviderStatus = AgentOperationProviderStatus.Failed,
            Failure = new AgentOperationFailure("failed", "failed")
        }, 0, default);

        await registry.CompactAsync(DateTimeOffset.UtcNow.AddSeconds(1), default);

        Assert.Equal(1, owner.DisposeCount);
        Assert.Empty(registry.Snapshot());
    }

    [Fact]
    public async Task RegistryShutdown_ReleasesEveryTransferredOwnerDespiteCleanupFailure()
    {
        var first = new RecordingAsyncDisposable(fail: true);
        var second = new RecordingAsyncDisposable();
        var registry = new AgentOperationRegistry(new TestEventSink());
        await registry.RegisterAsync(CreateSnapshot() with { OperationId = "first" }, executionOwner: first);
        await registry.RegisterAsync(CreateSnapshot() with { OperationId = "second" }, executionOwner: second);

        await registry.ShutdownAsync(FastShutdown());

        Assert.Equal(1, first.DisposeCount);
        Assert.Equal(1, second.DisposeCount);
        Assert.Empty(registry.Snapshot());
    }

    [Theory]
    [InlineData(AgentOperationProviderStatus.Completed)]
    [InlineData(AgentOperationProviderStatus.Failed)]
    [InlineData(AgentOperationProviderStatus.Cancelled)]
    public async Task FunctionExecutionContext_StartOperationAsync_RetainsThenReleasesExecutionScope(
        AgentOperationProviderStatus expectedStatus)
    {
        var disposed = 0;
        var execution = ToolHarnessExecutionScope.Create(null);
        var harness = new ToolHarnessFactory(
            "OperationHarness",
            typeof(object),
            static () => new object(),
            static (_, _, _) => [],
            static () => [],
            static () => [],
            "tests:operation",
            Middleware:
            [
                new ToolHarnessMiddlewareDescriptor
                {
                    MiddlewareType = typeof(OperationLifetimeProbe),
                    Factory = _ => ToolHarnessMiddlewareActivation.ExecutionOwned(
                        new OperationLifetimeProbe(() => Interlocked.Increment(ref disposed)))
                }
            ]);
        await using (await execution.Registry.AcquireAsync(
            harness,
            new ToolHarnessActivationContext(
                harness.ActivationIdentity, "execution", null, new AgentRunConfig()))) { }
        var capabilities = new RuntimeCapabilityRegistry();
        await using var registry = new AgentOperationRegistry(new TestEventSink());
        capabilities.Set(registry);
        var state = AgentLoopState.InitialSafe([], "run", "conversation", "agent");
        var session = new global::HPD.Agent.Session("session");
        var thread = new Thread("session", "agent") { Id = "thread" };
        var agentContext = new AgentContext(
            "agent", "conversation", state, new HPD.Events.Core.EventCoordinator(),
            session, thread, default,
            runtimeCapabilities: capabilities,
            toolHarnessExecutionScope: execution,
            threadExecutionId: "execution");
        var function = AIFunctionFactory.Create(() => "ok", "operation_tool");
        var before = agentContext.AsBeforeFunction(
            function, "call", new Dictionary<string, object?>(), new AgentRunConfig());
        var functionContext = new FunctionExecutionContext(before, new FunctionRequest
        {
            Function = function,
            CallId = "call",
            Arguments = new Dictionary<string, object?>(),
            State = state,
            EventCoordinator = agentContext.EventCoordinator
        });
        var workEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseWork = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var receipt = await functionContext.StartOperationAsync(
            "operation",
            null,
            new AgentOperationNotificationPolicy(),
            async (_, cancellationToken) =>
            {
                workEntered.TrySetResult();
                await releaseWork.Task.WaitAsync(cancellationToken);
                if (expectedStatus == AgentOperationProviderStatus.Failed)
                    throw new InvalidOperationException("failed");
                return new AgentOperationCompletion("done");
            });

        await workEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await execution.ReleaseForegroundAsync(ToolHarnessDeactivationReason.Completed);
        Assert.Equal(0, disposed);
        if (expectedStatus == AgentOperationProviderStatus.Cancelled)
            await functionContext.CancelOperationAsync(receipt.OperationId);
        else
            releaseWork.TrySetResult();
        await WaitUntilAsync(
            () => registry.Snapshot().Single(snapshot => snapshot.OperationId == receipt.OperationId)
                .ProviderStatus == expectedStatus,
            TimeSpan.FromSeconds(5));
        await execution.Completion.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(expectedStatus, registry.Snapshot().Single().ProviderStatus);
        Assert.Equal(1, disposed);
    }

    [Fact]
    public async Task HarnessOriginatedSchedulingFailure_ReleasesTransferredLeaseExactlyOnce()
    {
        var (execution, disposed) = await ActivatedExecutionAsync();
        await using var registry = new AgentOperationRegistry(new ThrowingEventSink());

        await Assert.ThrowsAsync<InvalidOperationException>(() => AgentLocalOperationScheduler.StartAsync(
            registry,
            AgentOperationSourceKind.LocalTool,
            "scheduling-failure",
            new AgentExecutionAddress("agent", "session", "thread"),
            "execution",
            null,
            null,
            new AgentOperationNotificationPolicy(),
            static (_, _) => ValueTask.FromResult(new AgentOperationCompletion("unreachable")),
            execution).AsTask());
        await execution.ReleaseForegroundAsync(ToolHarnessDeactivationReason.Failed);
        await execution.Completion;

        Assert.Equal(1, disposed());
        Assert.Empty(registry.Snapshot());
    }

    [Fact]
    public async Task HarnessOriginatedRegistryShutdown_ReleasesTransferredLeaseExactlyOnce()
    {
        var (execution, disposed) = await ActivatedExecutionAsync();
        var registry = new AgentOperationRegistry(new TestEventSink());
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await AgentLocalOperationScheduler.StartAsync(
            registry,
            AgentOperationSourceKind.LocalTool,
            "shutdown",
            new AgentExecutionAddress("agent", "session", "thread"),
            "execution",
            null,
            null,
            new AgentOperationNotificationPolicy(),
            async (_, cancellationToken) =>
            {
                entered.TrySetResult();
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
                return new AgentOperationCompletion("unreachable");
            },
            execution);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await execution.ReleaseForegroundAsync(ToolHarnessDeactivationReason.Cancelled);

        await registry.ShutdownAsync(FastShutdown());
        await execution.Completion.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(1, disposed());
        Assert.Empty(registry.Snapshot());
    }

    [Fact]
    public async Task HarnessOriginatedCompactionRemoval_ReleasesTransferredLeaseExactlyOnce()
    {
        var (execution, disposed) = await ActivatedExecutionAsync();
        var registry = new AgentOperationRegistry(
            new TestEventSink(),
            new AgentOperationRetentionPolicy
            {
                TerminalRetention = TimeSpan.Zero,
                ProviderDeduplicationRetention = TimeSpan.FromMinutes(1)
            });
        var receipt = await AgentLocalOperationScheduler.StartAsync(
            registry,
            AgentOperationSourceKind.LocalTool,
            "compact",
            new AgentExecutionAddress("agent", "session", "thread"),
            "execution",
            null,
            null,
            new AgentOperationNotificationPolicy(),
            static (_, _) => ValueTask.FromResult(new AgentOperationCompletion("done")),
            execution);
        await execution.ReleaseForegroundAsync(ToolHarnessDeactivationReason.Completed);
        await WaitUntilAsync(
            () => registry.Snapshot().Single().ProviderStatus == AgentOperationProviderStatus.Completed,
            TimeSpan.FromSeconds(5));

        await registry.CompactAsync(DateTimeOffset.UtcNow.AddSeconds(1), default);
        await execution.Completion.WaitAsync(TimeSpan.FromSeconds(5));
        await registry.DisposeAsync();

        Assert.Equal(1, disposed());
        Assert.Empty(registry.Snapshot());
        Assert.Contains(registry.Tombstones(), tombstone => tombstone.OperationId == receipt.OperationId);
    }

    [Theory]
    [InlineData(AgentOperationSourceKind.McpTask)]
    [InlineData(AgentOperationSourceKind.ProviderOperation)]
    [InlineData(AgentOperationSourceKind.SubAgent)]
    [InlineData(AgentOperationSourceKind.Workflow)]
    [InlineData(AgentOperationSourceKind.MultiAgent)]
    public async Task UnrelatedAndHydratableOperations_RegisterAndShutdownWithoutExecutionLease(
        AgentOperationSourceKind sourceKind)
    {
        var snapshot = CreateSnapshot() with
        {
            OperationId = sourceKind.ToString(),
            SourceKind = sourceKind,
            Recovery = sourceKind is AgentOperationSourceKind.McpTask or AgentOperationSourceKind.ProviderOperation
                ? new AgentOperationRecoveryReference("provider", "durable")
                : null
        };
        var registry = new AgentOperationRegistry(new TestEventSink());

        await registry.RegisterAsync(snapshot);
        await registry.ShutdownAsync(FastShutdown());

        Assert.Empty(registry.Snapshot());
    }

    private static async Task<(ToolHarnessExecutionScope Execution, Func<int> Disposed)>
        ActivatedExecutionAsync()
    {
        var disposed = 0;
        var execution = ToolHarnessExecutionScope.Create(null);
        var harness = new ToolHarnessFactory(
            "OperationHarness",
            typeof(object),
            static () => new object(),
            static (_, _, _) => [],
            static () => [],
            static () => [],
            "tests:operation-entrypoint",
            Middleware:
            [
                new ToolHarnessMiddlewareDescriptor
                {
                    MiddlewareType = typeof(OperationLifetimeProbe),
                    Factory = _ => ToolHarnessMiddlewareActivation.ExecutionOwned(
                        new OperationLifetimeProbe(() => Interlocked.Increment(ref disposed)))
                }
            ]);
        await using (await execution.Registry.AcquireAsync(
            harness,
            new ToolHarnessActivationContext(
                harness.ActivationIdentity, "execution", null, new AgentRunConfig()))) { }
        return (execution, () => Volatile.Read(ref disposed));
    }

    private static AgentShutdownOptions FastShutdown() => new()
    {
        GracefulDrainTimeout = TimeSpan.FromMilliseconds(1),
        CancellationDrainTimeout = TimeSpan.FromMilliseconds(1)
    };

    private sealed class OperationLifetimeProbe(Action onDispose)
        : IToolHarnessMiddleware, IAsyncDisposable
    {
        public ValueTask DisposeAsync()
        {
            onDispose();
            return ValueTask.CompletedTask;
        }
    }

    private static async Task WaitUntilAsync(Func<bool> predicate, TimeSpan timeout)
    {
        var deadline = DateTimeOffset.UtcNow + timeout;
        while (!predicate() && DateTimeOffset.UtcNow < deadline)
            await Task.Delay(10);
    }

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

    private sealed class RecordingAsyncDisposable(
        Func<Task>? onDispose = null,
        bool fail = false) : IAsyncDisposable
    {
        public int DisposeCount { get; private set; }

        public async ValueTask DisposeAsync()
        {
            DisposeCount++;
            if (onDispose is not null)
                await onDispose();
            if (fail)
                throw new InvalidOperationException("owner cleanup failed");
        }
    }

    private sealed class ThrowingEventSink : IAgentOperationEventSink
    {
        public ValueTask AppendAsync(AgentEvent operationEvent, CancellationToken cancellationToken) =>
            ValueTask.FromException(new InvalidOperationException("journal failed"));
    }
}
