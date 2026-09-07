using System.Text.Json;
using FluentAssertions;
using HPD.Agent;
using HPD.Agent.Middleware;
using HPD.Agent.ToolHarness.Coding.Debugging;
using HPD.Agent.ToolHarness.Coding.Debugging.Protocol;
using HPD.Environment.Contracts;
using HPD.Events;
using HPD.Events.Core;
using HPDOS.ToolHarnesses.Middleware;
using Microsoft.Extensions.AI;

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
        fixture.OperationSink.FailRegistration = true;

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

    [Fact]
    public async Task Expected_transport_eof_after_disconnect_intent_does_not_fault_tree()
    {
        await using var fixture = new Fixture();
        var result = await fixture.Orchestrator.StartAsync(
            fixture.Request(),
            CancellationToken.None);
        var tree = fixture.Manager.ResolveTree(
            fixture.Scope,
            result.DebugTreeId);
        tree.SelectSession(result.DebugSessionId).BeginDisconnect();

        fixture.Starter.Transport!.Complete();
        await Task.Delay(100);

        fixture.Events.Events.Should()
            .NotContain(@event => @event is DebugTreeFaultedEvent);
        fixture.Manager.ListTrees(fixture.Scope).Should().ContainSingle();
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
            OperationSink = new FakeOperationSink();
            Operations = new AgentOperationRegistry(OperationSink);
            Events = new FakeLifecyclePublisher();
            Orchestrator = new(
                Starter,
                Activator,
                new DebugSourcePreviewProvider([], new DebugSourcePreviewOptions()));
        }

        public DebugSessionManager Manager { get; }
        public DebugRuntimeBinding Runtime { get; }
        public DebugTreeLookupScope Scope { get; }
        public FakeOwnedResource Resource => Activator.Resources[0];
        public DebugAdapterStartPlan Adapter { get; }
        public DirectAdapterDebugExecutionPlan Plan { get; }
        public FakeActivator Activator { get; }
        public FakeProtocolStarter Starter { get; }
        public FakeOperationSink OperationSink { get; }
        public AgentOperationRegistry Operations { get; }
        public FakeLifecyclePublisher Events { get; }
        public DebugExecutionStartOrchestrator Orchestrator { get; }

        public DebugExecutionStartRequest Request() => new()
        {
            Runtime = Runtime,
            Workspace = new(
                "root",
                Path.GetTempPath(),
                [new AgentWorkspaceRoot("root", Path.GetTempPath())]),
            ExecutionPlan = Plan,
            Permission = new("call", "launch", DebugPermissionClass.Launch),
            ExecutionContext = CreateExecutionContext(),
            InitializeFeatures = new(),
            EventPublisher = Events
        };

        public async ValueTask DisposeAsync()
        {
            await Manager.DisposeAsync();
            await Operations.DisposeAsync();
        }

        private FunctionExecutionContext CreateExecutionContext()
        {
            var coordinator = new EventCoordinator();
            var state = AgentLoopState.InitialSafe([], "run-1", "conversation-1", "agent");
            var session = new Session("session");
            var thread = new Thread("session", "agent") { Id = "thread" };
            var agentContext = new AgentContext(
                "agent", "conversation-1", state, coordinator, session, thread, CancellationToken.None);
            agentContext.RuntimeCapabilities.Set(Operations);
            var function = AIFunctionFactory.Create(() => "ok", new AIFunctionFactoryOptions { Name = "Debug" });
            var before = agentContext.AsBeforeFunction(
                function, "call", new Dictionary<string, object?>(), new AgentRunConfig(), null, null, null);
            return new FunctionExecutionContext(before, new FunctionRequest
            {
                Function = function,
                CallId = "call",
                Arguments = new Dictionary<string, object?>(),
                State = state,
                EventCoordinator = coordinator
            });
        }

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
            registerHandlers(
                session,
                tree,
                new DebugConfigurationCoordinator(
                    _ => Task.CompletedTask,
                    lifetime),
                request);
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

    private sealed class FakeOperationSink : IAgentOperationEventSink
    {
        public bool FailRegistration { get; set; }
        public ValueTask AppendAsync(AgentEvent operationEvent, CancellationToken cancellationToken)
        {
            if (FailRegistration)
                throw new InvalidOperationException("registration failed");
            return ValueTask.CompletedTask;
        }
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
