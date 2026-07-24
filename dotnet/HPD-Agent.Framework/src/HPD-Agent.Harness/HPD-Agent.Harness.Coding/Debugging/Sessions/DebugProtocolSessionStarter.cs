using System.Text.Json;
using HPD.Agent.ToolHarness.Coding.Debugging.Protocol;
using HPD.Agent.ToolHarness.Coding.Debugging.Protocol.Generated;

namespace HPD.Agent.ToolHarness.Coding.Debugging;

/// <summary>
/// Starts one DAP protocol session from an activated execution. The boundary keeps
/// reservation and resource-ownership behavior independently verifiable.
/// </summary>
internal interface IDebugProtocolSessionStarter
{
    ValueTask<DebugSession> StartAsync(
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
        CancellationToken cancellationToken);
}

/// <summary>
/// Starts one DAP protocol session from an already-activated adapter start plan.
/// </summary>
internal sealed class DebugProtocolSessionStarter(
    DebugProtocolTransportFactory transportFactory,
    DebugInitializePolicy initializePolicy) : IDebugProtocolSessionStarter
{
    public async ValueTask<DebugSession> StartAsync(
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
        IDebugProtocolTransport? transport = null;
        DebugSession? session = null;
        var phase = "transport";
        try
        {
            transport = await transportFactory.CreateAsync(adapterPlan, cancellationToken)
                .ConfigureAwait(false);
            session = new DebugSession
            {
                SessionId = sessionId,
                RootSessionId = tree.RootSessionId,
                ParentSessionId = parentSessionId,
                AdapterStartMethod = adapterPlan.Method,
                Protocol = new DebugProtocolClient(transport, new DebugProtocolClientOptions
                {
                    HostTraceSink = request.HostTraceSink
                }),
                AdapterPlan = adapterPlan
            };
            phase = "initialize";
            if (request.EventPublisher is not null)
            {
                session.OutputEvents = createOutputEvents(session, tree);
                session.ProgressEvents = createProgressEvents(session, tree);
            }
            transport = null;
            session.State.Transition(DebugSessionStatus.Initializing);
            var coordinator = new DebugConfigurationCoordinator(
                ct => ConfigureAsync(session, breakpoints, breakpointPolicy, ct), lifetime);
            registerHandlers(session, tree, coordinator, request);

            session.Capabilities = await session.Protocol.InitializeAsync(
                initializePolicy.Create(adapterPlan.AdapterId, request.InitializeFeatures),
                cancellationToken).ConfigureAwait(false);
            session.State.Transition(DebugSessionStatus.Configuring);
            phase = adapterPlan.Method == DebugAdapterStartMethod.Attach
                ? "attach"
                : "launch";
            var startTask = coordinator.RunLaunchAsync(async ct =>
            {
                if (adapterPlan.Method == DebugAdapterStartMethod.Attach)
                    await session.Protocol.SendAsync(
                        DebugProtocolDescriptors.AttachRequest,
                        DebugProtocolArgumentComposer.Attach(adapterPlan.Arguments, restartData),
                        ct,
                        TimeSpan.FromSeconds(30)).ConfigureAwait(false);
                else
                    await session.Protocol.SendAsync(
                        DebugProtocolDescriptors.LaunchRequest,
                        DebugProtocolArgumentComposer.Launch(
                            adapterPlan.Arguments, noDebug: false, restartData),
                        ct,
                        TimeSpan.FromSeconds(30)).ConfigureAwait(false);
            }, cancellationToken);
            await coordinator.AwaitStartBoundaryAsync(cancellationToken).ConfigureAwait(false);
            await startTask.ConfigureAwait(false);
            phase = "configuration";
            if (session.State.Status == DebugSessionStatus.Configuring)
                session.State.Transition(DebugSessionStatus.Running);

            tree.AddSession(session);
            if (parentSessionId is not null &&
                tree.Sessions.TryGetValue(parentSessionId, out var parent))
            {
                parent.ChildSessionIds.TryAdd(sessionId, 0);
                tree.ActivateSession(sessionId);
            }
            return session;
        }
        catch (Exception exception)
        {
            if (session is not null)
            {
                var diagnostics = session.Protocol.AdapterDiagnostics;
                await session.DisposeAsync().ConfigureAwait(false);
                if (exception is OperationCanceledException or
                    DebugStartPlanningException or
                    DebugExceptionBreakpointValidationException)
                    throw;
                throw new DebugAdapterStartException(
                    adapterPlan.AdapterId, phase, diagnostics, exception);
            }
            if (transport is not null)
                await transport.DisposeAsync().ConfigureAwait(false);
            if (exception is OperationCanceledException)
                throw;
            throw new DebugAdapterStartException(
                adapterPlan.AdapterId,
                phase,
                new DebugAdapterDiagnosticSnapshot(string.Empty, 0, 0, null),
                exception);
        }
    }

    private static async Task ConfigureAsync(
        DebugSession session,
        DebugDesiredBreakpointSnapshot breakpoints,
        DebugInitialBreakpointPolicy breakpointPolicy,
        CancellationToken cancellationToken)
    {
        await DebugBreakpointProtocolApplier.ApplyAllAsync(
            session, breakpoints, cancellationToken).ConfigureAwait(false);
        EnsureBreakpointPolicy(
            breakpointPolicy,
            session.AdapterBreakpoints.Snapshot);
        if (session.Capabilities?.SupportsConfigurationDoneRequest == true)
            await session.Protocol.SendAsync(
                DebugProtocolDescriptors.ConfigurationDoneRequest,
                new ConfigurationDoneArguments(),
                cancellationToken).ConfigureAwait(false);
    }

    internal static void EnsureBreakpointPolicy(
        DebugInitialBreakpointPolicy breakpointPolicy,
        IReadOnlyList<DebugAdapterBreakpointState> states)
    {
        ArgumentNullException.ThrowIfNull(states);
        if (breakpointPolicy == DebugInitialBreakpointPolicy.RequireImmediatelyVerified &&
            states.Any(state => !state.Verified))
            throw new DebugStartPlanningException(
                "debug_initial_breakpoint_unverified",
                "At least one initial breakpoint was acknowledged but not immediately verified.");
    }
}
