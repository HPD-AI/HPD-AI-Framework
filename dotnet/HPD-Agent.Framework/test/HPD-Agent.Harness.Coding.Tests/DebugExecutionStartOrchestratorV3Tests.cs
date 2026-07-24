using System.Text.Json;
using FluentAssertions;
using HPD.Agent;
using HPD.Agent.Middleware;
using HPD.Agent.ToolHarness.Coding.Debugging;
using HPD.Agent.ToolHarness.Coding.Debugging.Protocol;
using HPD.Environment.Contracts;
using HPDOS.ToolHarnesses.Middleware;

namespace HPD.Agent.ToolHarness.Coding.Tests;

public sealed class DebugExecutionStartOrchestratorV3Tests
{
    [Fact]
    public async Task Reservation_exists_before_activation_and_protocol_failure_rolls_back_once()
    {
        await using var fixture = new Fixture();
        fixture.Starter.Failure = new InvalidOperationException("protocol failed");

        var action = () => fixture.Orchestrator.StartAsync(
            fixture.Request(),
            CancellationToken.None).AsTask();

        await action.Should().ThrowAsync<InvalidOperationException>();
        fixture.Activator.ReservationObserved.Should().BeTrue();
        fixture.Resource.DisposeCount.Should().Be(1);
        fixture.Manager.ListTrees(fixture.Scope).Should().BeEmpty();
    }

    [Fact]
    public async Task Background_registration_failure_disposes_session_and_owned_resource()
    {
        await using var fixture = new Fixture();
        fixture.Backgrounds.FailRegistration = true;

        var action = () => fixture.Orchestrator.StartAsync(
            fixture.Request(),
            CancellationToken.None).AsTask();

        await action.Should().ThrowAsync<InvalidOperationException>();
        fixture.Resource.DisposeCount.Should().Be(1);
        fixture.Starter.Transport!.IsAlive.Should().BeFalse();
        fixture.Manager.ListTrees(fixture.Scope).Should().BeEmpty();
    }

    [Fact]
    public async Task Successful_publication_transfers_resource_ownership_exactly_once()
    {
        await using var fixture = new Fixture();

        var result = await fixture.Orchestrator.StartAsync(
            fixture.Request(),
            CancellationToken.None);

        result.OwnedResourceCount.Should().Be(1);
        fixture.Resource.DisposeCount.Should().Be(0);
        fixture.Manager.ListTrees(fixture.Scope).Should().ContainSingle();

        await fixture.Manager.RemoveAndDisposeAsync(fixture.Scope, result.DebugTreeId);
        fixture.Resource.DisposeCount.Should().Be(1);
        await fixture.Manager.RemoveAndDisposeAsync(fixture.Scope, result.DebugTreeId);
        fixture.Resource.DisposeCount.Should().Be(1);
    }

    [Fact]
    public async Task Wrong_permission_is_rejected_before_reservation_or_activation()
    {
        await using var fixture = new Fixture();
        var request = fixture.Request() with
        {
            Permission = new("call", "attach", DebugPermissionClass.Attach)
        };

        var action = () => fixture.Orchestrator.StartAsync(
            request,
            CancellationToken.None).AsTask();

        await action.Should().ThrowAsync<UnauthorizedAccessException>();
        fixture.Activator.CallCount.Should().Be(0);
        fixture.Manager.ListTrees(fixture.Scope).Should().BeEmpty();
    }

    [Fact]
    public async Task Terminal_count_eviction_publishes_a_durable_classified_event()
    {
        await using var fixture = new Fixture(new()
        {
            MaximumRecords = 1,
            MaximumAggregateBytes = 1024 * 1024,
            Retention = TimeSpan.FromMinutes(15)
        });

        var first = await fixture.Orchestrator.StartAsync(
            fixture.Request(),
            CancellationToken.None);
        await fixture.Manager.RemoveAndDisposeAsync(
            fixture.Scope,
            first.DebugTreeId);
        var second = await fixture.Orchestrator.StartAsync(
            fixture.Request(),
            CancellationToken.None);
        await fixture.Manager.RemoveAndDisposeAsync(
            fixture.Scope,
            second.DebugTreeId);

        fixture.Events.Events
            .OfType<DebugTerminalRecordEvictedEvent>()
            .Should().ContainSingle()
            .Which.SafeReasonCode.Should().Be("COUNT_BOUND");
    }

    [Fact]
    public async Task Semantic_restart_uses_lifecycle_permission_and_fresh_resources()
    {
        await using var fixture = new Fixture();
        var original = await fixture.Orchestrator.StartAsync(
            fixture.Request(),
            CancellationToken.None);
        await fixture.Manager.RemoveAndDisposeAsync(
            fixture.Scope,
            original.DebugTreeId);

        var replacement = await fixture.Orchestrator.StartAsync(
            fixture.Request() with
            {
                IsRestart = true,
                Permission = new(
                    "restart-call",
                    "restart",
                    DebugPermissionClass.Lifecycle)
            },
            CancellationToken.None);

        replacement.DebugTreeId.Should().NotBe(original.DebugTreeId);
        fixture.Activator.RestartFlags.Should().Equal(false, true);
        fixture.Activator.Resources.Should().HaveCount(2);
        fixture.Activator.Resources[0].Should()
            .NotBeSameAs(fixture.Activator.Resources[1]);
        fixture.Activator.Resources[0].DisposeCount.Should().Be(1);
        fixture.Activator.Resources[1].DisposeCount.Should().Be(0);

        await fixture.Manager.RemoveAndDisposeAsync(
            fixture.Scope,
            replacement.DebugTreeId);
        fixture.Activator.Resources[1].DisposeCount.Should().Be(1);
    }

    private sealed class Fixture : IAsyncDisposable
    {
        public Fixture(DebugTerminalRecordStoreOptions? terminalOptions = null)
        {
            Manager = new DebugSessionManager(
                new DebugTerminalRecordStore(
                    terminalOptions ?? new DebugTerminalRecordStoreOptions()));
            Runtime = new()
            {
                AgentRuntimeRegistrationId = Manager.RuntimeId,
                SessionId = "session",
                ThreadId = "thread",
                SessionManager = Manager,
                EventScope = new(null, "session", "thread"),
                State = new()
            };
            Scope = new(Manager.RuntimeId, "session", "thread");
            Adapter = CreateAdapterPlan();
            Plan = new DirectAdapterDebugExecutionPlan
            {
                PlannerId = "fixture",
                SemanticStartKind = DebugSemanticStartKind.DirectLaunch,
                EnvironmentId = "environment",
                EnvironmentRevision = 1,
                CanonicalWorkingDirectory = "/workspace",
                InitialConfiguration = new(),
                Adapter = Adapter
            };
            Activator = new FakeActivator(Manager, Adapter);
            Starter = new FakeProtocolStarter();
            Backgrounds = new FakeBackgroundRegistry();
            Events = new FakeLifecyclePublisher();
            Orchestrator = new(Starter, Activator);
        }

        public DebugSessionManager Manager { get; }
        public DebugRuntimeBinding Runtime { get; }
        public DebugTreeLookupScope Scope { get; }
        public FakeOwnedResource Resource => Activator.Resources[0];
        public DebugAdapterStartPlan Adapter { get; }
        public DirectAdapterDebugExecutionPlan Plan { get; }
        public FakeActivator Activator { get; }
        public FakeProtocolStarter Starter { get; }
        public FakeBackgroundRegistry Backgrounds { get; }
        public FakeLifecyclePublisher Events { get; }
        public DebugExecutionStartOrchestrator Orchestrator { get; }

        public DebugExecutionStartRequest Request() => new()
        {
            Runtime = Runtime,
            ExecutionPlan = Plan,
            Permission = new("call", "launch", DebugPermissionClass.Launch),
            BackgroundHandles = Backgrounds,
            InitializeFeatures = new(),
            EventPublisher = Events
        };

        public async ValueTask DisposeAsync() => await Manager.DisposeAsync();

        private static DebugAdapterStartPlan CreateAdapterPlan()
        {
            using var arguments = JsonDocument.Parse("{}");
            return new()
            {
                Method = DebugAdapterStartMethod.Launch,
                AdapterId = "fixture",
                EnvironmentId = "environment",
                EnvironmentRevision = 1,
                PolicyRevision = 1,
                EndpointCatalogRevision = 1,
                PackageProvenance = new()
                {
                    PackageId = "fixture",
                    PackageVersion = "1",
                    AssemblyName = "fixture"
                },
                TrustDecision = new()
                {
                    TrustLevel = DebugAdapterTrustLevel.Trusted,
                    PolicyRevision = "fixture",
                    ReasonCode = "TEST"
                },
                CanonicalWorkingDirectory = "/workspace",
                AuthorizationScope = "debug.adapter.launch",
                FilteredEnvironment = new Dictionary<string, string?>(),
                Transport = new()
                {
                    Kind = DebugAdapterTransportKind.HostCallback,
                    Command = string.Empty
                },
                Arguments = arguments.RootElement.Clone()
            };
        }
    }

    private sealed class FakeActivator(
        DebugSessionManager manager,
        DebugAdapterStartPlan adapter) : IDebugExecutionPlanActivator
    {
        public int CallCount { get; private set; }
        public bool ReservationObserved { get; private set; }
        public List<bool> RestartFlags { get; } = [];
        public List<FakeOwnedResource> Resources { get; } = [];

        public ValueTask<DebugActivatedExecution> ActivateAsync(
            DebugExecutionPlan plan,
            DebugExecutionActivationContext context,
            CancellationToken cancellationToken)
        {
            CallCount++;
            RestartFlags.Add(context.IsRestart);
            try
            {
                var duplicate = manager.ReserveTree(
                    context.Ownership.SessionId,
                    context.Ownership.ThreadId,
                    context.Ownership.EnvironmentId,
                    context.Ownership.EnvironmentRevision,
                    context.Ownership.DebugTreeId);
                duplicate.DisposeAsync().GetAwaiter().GetResult();
            }
            catch (InvalidOperationException)
            {
                ReservationObserved = true;
            }
            var resource = new FakeOwnedResource();
            Resources.Add(resource);
            return ValueTask.FromResult(new DebugActivatedExecution
            {
                AdapterPlan = adapter,
                SemanticStartKind = DebugSemanticStartKind.DirectLaunch,
                AdapterStartMethod = DebugAdapterStartMethod.Launch,
                OwnedResources = [resource]
            });
        }
    }

    private sealed class FakeProtocolStarter : IDebugProtocolSessionStarter
    {
        public Exception? Failure { get; set; }
        public InMemoryDebugProtocolTransport? Transport { get; private set; }

        public ValueTask<DebugSession> StartAsync(
            DebugSessionTree tree,
            string sessionId,
            string? parentSessionId,
            DebugAdapterStartPlan adapterPlan,
            JsonElement? restartData,
            DebugDesiredBreakpointSnapshot breakpoints,
            DebugInitialBreakpointPolicy breakpointPolicy,
            DebugExecutionStartRequest request,
            CancellationToken lifetime,
            Action<DebugSession, DebugSessionTree, DebugConfigurationCoordinator,
                DebugExecutionStartRequest> registerHandlers,
            Func<DebugSession, DebugSessionTree, DebugOutputEventCoalescer> createOutputEvents,
            Func<DebugSession, DebugSessionTree, DebugProgressEventCoalescer> createProgressEvents,
            CancellationToken cancellationToken)
        {
            if (Failure is not null)
                throw Failure;
            Transport = new();
            var session = new DebugSession
            {
                SessionId = sessionId,
                RootSessionId = sessionId,
                ParentSessionId = parentSessionId,
                AdapterStartMethod = adapterPlan.Method,
                AdapterPlan = adapterPlan,
                Protocol = new DebugProtocolClient(Transport)
            };
            session.State.Transition(DebugSessionStatus.Initializing);
            session.State.Transition(DebugSessionStatus.Configuring);
            session.State.Transition(DebugSessionStatus.Running);
            tree.AddSession(session);
            return ValueTask.FromResult(session);
        }
    }

    private sealed class FakeOwnedResource : IDebugOwnedResource
    {
        public string Kind => "fixture";
        public string SafeIdentity => "fixture";
        public int DisposeCount { get; private set; }
        public int StopCount { get; private set; }

        public ValueTask StopAsync(
            string reason,
            CancellationToken cancellationToken)
        {
            StopCount++;
            return ValueTask.CompletedTask;
        }

        public ValueTask DisposeAsync()
        {
            DisposeCount++;
            return ValueTask.CompletedTask;
        }
    }

    private sealed class FakeBackgroundRegistry : IAgentBackgroundHandleRegistry
    {
        public bool FailRegistration { get; set; }
        private readonly Dictionary<string, RegisteredBackgroundHandle> _handles = [];

        public ValueTask<BackgroundHandleRegistration> RegisterHandleAsync(
            BackgroundHandleDescriptor descriptor,
            IBackgroundHandle handle,
            CancellationToken cancellationToken = default)
        {
            if (FailRegistration)
                throw new InvalidOperationException("registration failed");
            var id = descriptor.HandleId!;
            _handles[id] = new(id, descriptor, handle, DateTimeOffset.UtcNow);
            return ValueTask.FromResult(new BackgroundHandleRegistration(
                id,
                descriptor.Name,
                descriptor.Kind,
                descriptor.SourceKind));
        }

        public bool TryGetHandle(
            string handleId,
            BackgroundHandleScope scope,
            out RegisteredBackgroundHandle handle)
            => _handles.TryGetValue(handleId, out handle!);

        public IReadOnlyList<RegisteredBackgroundHandle> ListHandles(
            BackgroundHandleQuery query)
            => _handles.Values.ToArray();
    }

    private sealed class FakeLifecyclePublisher : IDebugLifecycleEventPublisher
    {
        public List<AgentEvent> Events { get; } = [];

        public ValueTask PublishAsync(
            AgentEvent @event,
            bool durable,
            CancellationToken cancellationToken = default)
        {
            durable.Should().BeTrue();
            lock (Events)
                Events.Add(@event);
            return ValueTask.CompletedTask;
        }
    }
}
