using System.Text;
using System.Text.Json;
using HPD.Agent;
using HPD.Agent.ToolHarness.Coding.Debugging;
using HPD.Agent.ToolHarness.Coding.Debugging.Protocol;

namespace HPD.Agent.ToolHarness.Coding.Tests;

public sealed class DebugSessionStateTests
{
    [Fact]
    public void All_threads_stopped_preserves_the_adapter_designated_primary_thread()
    {
        var state = new DebugSessionState();
        state.Transition(DebugSessionStatus.Initializing);
        state.Transition(DebugSessionStatus.Configuring);
        state.Transition(DebugSessionStatus.Running);
        state.ObserveThread(10);
        state.ObserveThread(20);

        state.ObserveStopped(20, allThreadsStopped: true, "breakpoint", null);

        state.PrimaryStoppedThreadId.Should().Be(20);
        state.Threads.Should().OnlyContain(thread => thread.IsStopped);
        state.Threads.Single(thread => thread.ThreadId == 20).StopReason.Should().Be("breakpoint");
        state.Threads.Single(thread => thread.ThreadId == 10).StopReason.Should().BeNull(
            "an all-threads suspension does not make every thread the focal breakpoint thread");
        state.ObserveContinued(20, allThreadsContinued: true);
        state.PrimaryStoppedThreadId.Should().BeNull();
    }

    [Fact]
    public void Resume_transaction_updates_all_threads_and_rolls_back_an_adapter_rejection()
    {
        var state = RunningState(10, 20);
        state.ObserveStopped(20, allThreadsStopped: true, "breakpoint", "hit");

        var transition = state.BeginResume(20, allThreadsContinued: true);

        state.Status.Should().Be(DebugSessionStatus.Running);
        state.Threads.Should().OnlyContain(thread =>
            !thread.IsStopped && thread.ResumptionGeneration == 1);
        state.TryRollbackResume(transition).Should().BeTrue();
        state.Status.Should().Be(DebugSessionStatus.Stopped);
        state.PrimaryStoppedThreadId.Should().Be(20);
        state.Threads.Single(thread => thread.ThreadId == 20).StopReason.Should().Be("breakpoint");
        state.Threads.Single(thread => thread.ThreadId == 10).StopReason.Should().BeNull();
    }

    [Fact]
    public void Resume_transaction_does_not_overwrite_a_newer_adapter_stop()
    {
        var state = RunningState(10, 20);
        state.ObserveStopped(20, allThreadsStopped: true, "breakpoint", null);
        var transition = state.BeginResume(20, allThreadsContinued: true);

        state.ObserveStopped(10, allThreadsStopped: true, "step", null);

        state.TryRollbackResume(transition).Should().BeFalse();
        state.PrimaryStoppedThreadId.Should().Be(10);
        state.Threads.Single(thread => thread.ThreadId == 10).StopReason.Should().Be("step");
    }

    [Fact]
    public void Matching_continued_event_confirms_an_optimistic_resume_without_double_counting()
    {
        var state = RunningState(10, 20);
        state.ObserveStopped(20, allThreadsStopped: true, "breakpoint", null);
        state.BeginResume(20, allThreadsContinued: true);

        state.ObserveContinued(20, allThreadsContinued: true);

        state.Threads.Should().OnlyContain(thread =>
            !thread.IsStopped && thread.ResumptionGeneration == 1);
    }

    [Fact]
    public void Adapter_continued_scope_supersedes_an_optimistic_resume_scope()
    {
        var state = RunningState(10, 20);
        state.ObserveStopped(20, allThreadsStopped: true, "breakpoint", null);
        state.BeginResume(20, allThreadsContinued: true);

        state.ObserveContinued(20, allThreadsContinued: false);

        state.Status.Should().Be(DebugSessionStatus.PartiallyStopped);
        state.Threads.Single(thread => thread.ThreadId == 20).IsStopped.Should().BeFalse();
        state.Threads.Single(thread => thread.ThreadId == 10).IsStopped.Should().BeTrue();
    }

    [Fact]
    public async Task Semantic_step_rejection_rolls_back_the_projected_resume()
    {
        await using var transport = new InMemoryDebugProtocolTransport();
        await using var manager = new DebugSessionManager(
            new DebugTerminalRecordStore(new DebugTerminalRecordStoreOptions()));
        await using var reservation = manager.ReserveTree(
            "owner", "thread", "env", 1, "tree");
        var tree = Tree(reservation.Ownership, "root", manager, Plan());
        var session = Session("root", "root", transport);
        session.State.Transition(DebugSessionStatus.Initializing);
        session.State.Transition(DebugSessionStatus.Configuring);
        session.State.ObserveThread(10);
        session.State.ObserveThread(20);
        session.State.ObserveStopped(20, allThreadsStopped: true, "breakpoint", null);
        tree.AddSession(session);
        reservation.Commit(tree);
        var semantics = new DebugSemanticService(manager);

        var step = semantics.NextAsync(
            Scope(manager, "owner", "thread"),
            "tree",
            null,
            20,
            singleThread: false,
            TimeSpan.FromSeconds(2),
            CancellationToken.None);
        var request = await ReadWrittenMessageAsync(transport);
        await transport.FeedProtocolAsync(DebugProtocolFramer.Encode(
            Encoding.UTF8.GetBytes($$"""
                {"seq":2,"type":"response","request_seq":{{request.GetProperty("seq").GetInt32()}},"success":false,"command":"next","message":"cannot step"}
                """)));

        var action = async () => await step;
        await action.Should().ThrowAsync<DebugAdapterRequestException>();
        session.State.Status.Should().Be(DebugSessionStatus.Stopped);
        session.State.PrimaryStoppedThreadId.Should().Be(20);
        session.State.Threads.Should().OnlyContain(thread => thread.IsStopped);
    }

    [Fact]
    public async Task Shared_snapshot_projects_bounded_tree_session_and_output_state()
    {
        await using var transport = new InMemoryDebugProtocolTransport();
        await using var manager = new DebugSessionManager(new DebugTerminalRecordStore(new DebugTerminalRecordStoreOptions()));
        var ownership = new DebugTreeOwnership(manager.RuntimeId, "owner", "thread", "tree", "env", 1);
        await using var tree = Tree(ownership, "root", manager, Plan());
        var root = Session("root", "root", transport);
        root.State.Transition(DebugSessionStatus.Initializing);
        root.State.Transition(DebugSessionStatus.Configuring);
        root.State.ObserveThread(7);
        root.State.ObserveStopped(7, allThreadsStopped: true, "breakpoint", "hit");
        root.Output.Append("tree", "root", new() { Output = "hello\n", Category = "stdout" }, allowAnsi: false);
        tree.AddSession(root);

        var snapshot = DebugSnapshotProjector.Project(tree);

        snapshot.DebugTreeId.Should().Be("tree");
        snapshot.Status.Should().Be("Stopped");
        snapshot.RetainedOutputBytes.Should().Be(6);
        snapshot.Sessions.Should().ContainSingle().Which.Should().Match<DebugSessionSnapshot>(
            x => x.DebugSessionId == "root" && x.ThreadCount == 1 &&
                 x.StoppedThreadCount == 1 && x.PrimaryStoppedThreadId == 7 &&
                 x.StopReason == "breakpoint");
    }

    [Fact]
    public void Partial_stop_and_continue_update_only_the_identified_thread_and_epoch()
    {
        var state = RunningState(1, 2);

        state.ObserveStopped(1, allThreadsStopped: false, "breakpoint", "hit");

        state.Status.Should().Be(DebugSessionStatus.PartiallyStopped);
        state.Threads.Single(x => x.ThreadId == 1).Should().Match<DebugThreadSnapshot>(x => x.IsStopped && x.SuspensionEpoch == 1);
        state.Threads.Single(x => x.ThreadId == 2).IsStopped.Should().BeFalse();

        state.ObserveContinued(1, allThreadsContinued: false);

        state.Status.Should().Be(DebugSessionStatus.Running);
        state.Threads.Single(x => x.ThreadId == 1).ResumptionGeneration.Should().Be(1);
        state.Threads.Single(x => x.ThreadId == 2).ResumptionGeneration.Should().Be(0);
    }

    [Fact]
    public void All_thread_flags_affect_every_known_thread()
    {
        var state = RunningState(1, 2);

        state.ObserveStopped(1, allThreadsStopped: true, "pause", null);
        state.Status.Should().Be(DebugSessionStatus.Stopped);
        state.Threads.Should().OnlyContain(x => x.IsStopped && x.SuspensionEpoch == 1);

        state.ObserveContinued(1, allThreadsContinued: true);
        state.Status.Should().Be(DebugSessionStatus.Running);
        state.Threads.Should().OnlyContain(x => !x.IsStopped && x.ResumptionGeneration == 1);
    }

    [Fact]
    public async Task Stop_waiter_is_thread_and_resumption_generation_correlated()
    {
        var state = RunningState(1, 2);
        state.ObserveContinued(1, false);
        using var waiter = state.RegisterStopWaiter(1, minimumResumptionGeneration: 1);

        state.ObserveStopped(2, false, "breakpoint", null);
        waiter.Task.IsCompleted.Should().BeFalse();
        state.ObserveStopped(1, false, "step", null);

        (await waiter.Task).ThreadId.Should().Be(1);
    }

    [Fact]
    public async Task Terminal_state_settles_outcome_waiters()
    {
        var state = RunningState(1);
        using var waiter = state.RegisterStopWaiter(1, 1);

        state.Transition(DebugSessionStatus.Faulted);

        var wait = async () => await waiter.Task;
        await wait.Should().ThrowAsync<DebugSessionEndedException>();
    }

    [Fact]
    public async Task Manager_reservations_are_non_addressable_and_rollback_cleanly()
    {
        await using var manager = new DebugSessionManager(new DebugTerminalRecordStore(new DebugTerminalRecordStoreOptions()));
        var reservation = manager.ReserveTree("owner-session", "owner-thread", "env", 1, "tree");
        var scope = Scope(manager, "owner-session", "owner-thread");

        var reservedLookup = () => manager.ResolveTree(scope, "tree");
        reservedLookup.Should().Throw<KeyNotFoundException>();
        await reservation.DisposeAsync();
        var rolledBackLookup = () => manager.ResolveTree(scope, "tree");
        rolledBackLookup.Should().Throw<KeyNotFoundException>();
    }

    [Fact]
    public async Task Manager_commit_enforces_complete_ownership_and_runtime_disposal()
    {
        await using var manager = new DebugSessionManager(new DebugTerminalRecordStore(new DebugTerminalRecordStoreOptions()));
        await using var reservation = manager.ReserveTree("owner-session", "owner-thread", "env", 1, "tree");
        var tree = Tree(reservation.Ownership, "root", manager, Plan());
        reservation.Commit(tree);

        manager.ResolveTree(Scope(manager, "owner-session", "owner-thread"), "tree").Should().BeSameAs(tree);
        var wrongOwner = () => manager.ResolveTree(Scope(manager, "other-session", "owner-thread"), "tree");
        wrongOwner.Should().Throw<DebugSessionOwnershipException>().Which.ReasonCode.Should().Be("SESSION_OWNERSHIP_MISMATCH");

        await manager.DisposeAsync();
        manager.IsAvailable.Should().BeFalse();
    }

    [Fact]
    public async Task Active_session_selection_prefers_the_latest_stopped_member_then_live_root()
    {
        await using var rootTransport = new InMemoryDebugProtocolTransport();
        await using var childTransport = new InMemoryDebugProtocolTransport();
        await using var manager = new DebugSessionManager(new DebugTerminalRecordStore(new DebugTerminalRecordStoreOptions()));
        var ownership = new DebugTreeOwnership(manager.RuntimeId, "owner", "thread", "tree", "env", 1);
        var plan = Plan();
        await using var tree = Tree(ownership, "root", manager, plan);
        var root = Session("root", "root", rootTransport);
        var child = Session("child", "root", childTransport, "root");
        tree.AddSession(root);
        tree.AddSession(child);
        child.State.Transition(DebugSessionStatus.Initializing);
        child.State.Transition(DebugSessionStatus.Configuring);
        child.State.Transition(DebugSessionStatus.Running);
        tree.ActivateSession("child");
        tree.SelectSession().SessionId.Should().Be("child",
            "a configured child is targetable before its first stopped event");
        child.State.ObserveThread(1);
        child.State.ObserveStopped(1, false, "entry", null);

        tree.ObserveStopped("child");
        tree.SelectSession().SessionId.Should().Be("child");
        child.State.Transition(DebugSessionStatus.Terminated);
        tree.ObserveTerminated("child");
        tree.SelectSession().SessionId.Should().Be("root");
    }

    [Fact]
    public async Task Explicit_tree_waiter_observes_an_owned_child_without_completing_session_waiters()
    {
        await using var rootTransport = new InMemoryDebugProtocolTransport();
        await using var childTransport = new InMemoryDebugProtocolTransport();
        await using var manager = new DebugSessionManager(new DebugTerminalRecordStore(new DebugTerminalRecordStoreOptions()));
        var ownership = new DebugTreeOwnership(manager.RuntimeId, "owner", "thread", "tree", "env", 1);
        await using var tree = Tree(ownership, "root", manager, Plan());
        var root = Session("root", "root", rootTransport);
        var child = Session("child", "root", childTransport, "root");
        tree.AddSession(root);
        tree.AddSession(child);
        root.State.Transition(DebugSessionStatus.Initializing);
        root.State.Transition(DebugSessionStatus.Configuring);
        root.State.ObserveThread(1);
        root.State.Transition(DebugSessionStatus.Running);
        child.State.Transition(DebugSessionStatus.Initializing);
        child.State.Transition(DebugSessionStatus.Configuring);
        child.State.ObserveThread(2);
        child.State.Transition(DebugSessionStatus.Running);
        using var rootOnly = root.State.RegisterStopWaiter(1, 0);
        using var anyOwned = tree.RegisterTreeStopWaiter();

        child.State.ObserveStopped(2, false, "breakpoint", null);
        tree.ObserveStopped("child");

        (await anyOwned.Task).DebugSessionId.Should().Be("child");
        rootOnly.Task.IsCompleted.Should().BeFalse();
    }

    [Fact]
    public async Task Tree_authorization_rejects_endpoint_revision_or_identity_changes()
    {
        await using var manager = new DebugSessionManager(new DebugTerminalRecordStore(new DebugTerminalRecordStoreOptions()));
        var ownership = new DebugTreeOwnership(manager.RuntimeId, "owner", "thread", "tree", "env", 1);
        var plan = Plan();
        await using var tree = Tree(ownership, "root", manager, plan);
        var changed = plan with
        {
            Transport = plan.Transport with { EndpointId = "another-endpoint" }
        };

        var validate = () => tree.Authorization.ValidateCurrent(tree.RuntimeBinding, changed);
        validate.Should().Throw<UnauthorizedAccessException>();
    }

    private static DebugSessionState RunningState(params int[] threadIds)
    {
        var state = new DebugSessionState();
        state.Transition(DebugSessionStatus.Initializing);
        state.Transition(DebugSessionStatus.Configuring);
        foreach (var id in threadIds) state.ObserveThread(id);
        state.Transition(DebugSessionStatus.Running);
        return state;
    }

    private static DebugTreeLookupScope Scope(DebugSessionManager manager, string session, string thread)
        => new(manager.RuntimeId, session, thread);

    private static async Task<JsonElement> ReadWrittenMessageAsync(
        InMemoryDebugProtocolTransport transport)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        await foreach (var bytes in transport.ReadWrittenAsync()
                           .WithCancellation(timeout.Token))
        {
            var frame = new DebugProtocolFramer().Append(bytes).Single();
            using var document = JsonDocument.Parse(frame);
            return document.RootElement.Clone();
        }
        throw new InvalidOperationException(
            "The debug transport completed before writing a request.");
    }

    private static DebugSession Session(
        string id,
        string rootId,
        InMemoryDebugProtocolTransport transport,
        string? parentId = null) => new()
    {
        SessionId = id,
        RootSessionId = rootId,
        ParentSessionId = parentId,
        AdapterStartMethod = DebugAdapterStartMethod.Launch,
        Protocol = new DebugProtocolClient(transport, new() { RequireInitializeFirst = false }),
        AdapterPlan = Plan()
    };

    private static DebugSessionTree Tree(
        DebugTreeOwnership ownership,
        string rootSessionId,
        DebugSessionManager manager,
        DebugAdapterStartPlan plan)
    {
        var runtime = new DebugRuntimeBinding
        {
            AgentRuntimeRegistrationId = manager.RuntimeId,
            SessionId = ownership.SessionId,
            ThreadId = ownership.ThreadId,
            SessionManager = manager,
            EventScope = new(null, ownership.SessionId, ownership.ThreadId, ownership.DebugTreeId),
            State = new()
        };
        return new DebugSessionTree
        {
            Ownership = ownership,
            RootSessionId = rootSessionId,
            RuntimeBinding = runtime,
            Authorization = DebugTreeAuthorization.Create(
                runtime,
                ownership,
                plan,
                DebugSemanticStartKind.DirectLaunch,
                "test",
                new()),
            Artifacts = new DebugArtifactWriter(null, ContentScope.Create("debug:test"),
                new Dictionary<string, string>()),
            EventPublisher = null
        };
    }

    private static DebugAdapterStartPlan Plan()
    {
        using var json = JsonDocument.Parse("{}");
        return new()
        {
            Method = DebugAdapterStartMethod.Launch,
            AdapterId = "fixture",
            EnvironmentId = "env",
            EnvironmentRevision = 1,
            PolicyRevision = 1,
            EndpointCatalogRevision = 1,
            PackageProvenance = new() { PackageId = "fixture", PackageVersion = "1", AssemblyName = "fixture" },
            TrustDecision = new() { TrustLevel = DebugAdapterTrustLevel.Trusted, PolicyRevision = "1", ReasonCode = "TEST" },
            CanonicalWorkingDirectory = "/workspace",
            AuthorizationScope = "debug.adapter.launch",
            FilteredEnvironment = new Dictionary<string, string?>(),
            Transport = new() { Kind = DebugAdapterTransportKind.ApprovedTcpConnect, Command = "" },
            Arguments = json.RootElement.Clone()
        };
    }
}
