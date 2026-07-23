using HPD.Agent.Middleware;
using HPD.Agent;
using System.Text.Json;
using HPD.Agent.ToolHarness.Coding.Debugging.Protocol;
using HPD.Agent.ToolHarness.Coding.Debugging.Protocol.Generated;
using HPDOS.ToolHarnesses.Middleware;

namespace HPD.Agent.ToolHarness.Coding.Debugging;

internal sealed class DebugAdapterStartException(
    string adapterId,
    string phase,
    DebugAdapterDiagnosticSnapshot diagnostics,
    Exception innerException)
    : Exception($"Debug adapter '{adapterId}' failed during '{phase}'.", innerException)
{
    public string AdapterId { get; } = adapterId;
    public string Phase { get; } = phase;
    public DebugAdapterDiagnosticSnapshot Diagnostics { get; } = diagnostics;
}

internal sealed record DebugSessionStartRequest
{
    public required DebugRuntimeBinding Runtime { get; init; }
    public required DebugAdapterLaunchPlan LaunchPlan { get; init; }
    public required IAgentBackgroundHandleRegistry BackgroundHandles { get; init; }
    public required DebugInitializeFeatures InitializeFeatures { get; init; }
    public DebugInitialConfiguration InitialConfiguration { get; init; } = new();
    public bool IsAttach { get; init; }
    public JsonElement? RestartData { get; init; }
    public IDebugLifecycleEventPublisher? EventPublisher { get; init; }
    public IDebugHostRequestBroker? HostRequestBroker { get; init; }
    public IDebugChildSessionPlanFactory? ChildSessionPlanFactory { get; init; }
    public DebugTreeAuthorizationOptions Authorization { get; init; } = new();
    public IContentStore? ContentStore { get; init; }
    public IDebugProtocolTraceSink? HostTraceSink { get; init; }
}

internal sealed record DebugSessionStartResult(
    string DebugTreeId,
    string DebugSessionId,
    BackgroundHandleSnapshot Handle,
    DebugSessionStatus Status);

internal sealed class DebugSessionStartOrchestrator
{
    private readonly DebugProtocolTransportFactory _transportFactory;
    private readonly DebugInitializePolicy _initializePolicy;

    public DebugSessionStartOrchestrator(
        DebugProtocolTransportFactory transportFactory,
        DebugInitializePolicy? initializePolicy = null)
    {
        _transportFactory = transportFactory;
        _initializePolicy = initializePolicy ?? new();
    }

    public async ValueTask<DebugSessionStartResult> StartAsync(
        DebugSessionStartRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.InitializeFeatures.RunInTerminalHandler && request.HostRequestBroker is null)
            throw new InvalidOperationException("runInTerminal cannot be advertised without a host request broker.");
        if (request.InitializeFeatures.StartDebuggingHandler && request.ChildSessionPlanFactory is null)
            throw new InvalidOperationException("startDebugging cannot be advertised without a child-session plan factory.");
        if (!request.Runtime.State.IsAvailable)
            throw new InvalidOperationException("The captured debug runtime binding is unavailable.");
        if (request.Runtime.SessionManager is not DebugSessionManager manager)
            throw new InvalidOperationException("The runtime debug session manager implementation is unsupported.");
        if (request.Runtime.AgentRuntimeRegistrationId != manager.RuntimeId)
            throw new InvalidOperationException("The captured runtime identity does not match its session manager.");
        if (request.Runtime.ProcessExecution is { } processExecution &&
            (request.LaunchPlan.EnvironmentId != processExecution.EnvironmentId ||
             request.LaunchPlan.EnvironmentRevision != processExecution.EnvironmentRevision))
            throw new InvalidOperationException("The debug launch plan does not match the captured runtime Environment.");

        await using var reservation = manager.ReserveTree(
            request.Runtime.SessionId,
            request.Runtime.ThreadId,
            request.LaunchPlan.EnvironmentId,
            request.LaunchPlan.EnvironmentRevision);
        var scope = new DebugTreeLookupScope(manager.RuntimeId, request.Runtime.SessionId, request.Runtime.ThreadId);
        var sessionId = Guid.NewGuid().ToString("N");
        DebugSession? session = null;
        DebugSessionTree? tree = null;
        DebugSessionHandle? handle = null;
        var published = false;
        var managerCommitted = false;
        using var startLifetime = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        try
        {
            tree = new DebugSessionTree
            {
                Ownership = reservation.Ownership,
                RootSessionId = sessionId,
                RuntimeBinding = request.Runtime,
                Authorization = DebugTreeAuthorization.Create(
                    request.Runtime, reservation.Ownership, request.LaunchPlan, request.IsAttach, request.Authorization),
                RestartTemplate = request,
                Artifacts = new DebugArtifactWriter(
                    request.ContentStore,
                    ContentScope.Create($"debug:{request.Runtime.SessionId}:{reservation.TreeId}"),
                    new Dictionary<string, string>(StringComparer.Ordinal)
                    {
                        ["kind"] = "debug-artifact",
                        ["runtime"] = request.Runtime.AgentRuntimeRegistrationId,
                        ["session"] = request.Runtime.SessionId,
                        ["thread"] = request.Runtime.ThreadId,
                        ["debug-tree"] = reservation.TreeId
                    }),
                EventPublisher = request.EventPublisher switch
                {
                    null => null,
                    IDebugEventPublisher publisher => publisher.Bind(request.Runtime.EventScope),
                    _ => new DebugScopedEventPublisher(request.EventPublisher, request.Runtime.EventScope)
                }
            };
            tree.Breakpoints.Seed(request.InitialConfiguration);
            session = await CreateProtocolSessionAsync(
                tree, sessionId, parentSessionId: null, request.LaunchPlan, request.IsAttach,
                request.RestartData, tree.Breakpoints.Snapshot, request, startLifetime.Token,
                cancellationToken).ConfigureAwait(false);

            handle = new DebugSessionHandle(manager, scope, reservation.TreeId);
            var registration = await request.BackgroundHandles.RegisterHandleAsync(new()
            {
                HandleId = reservation.TreeId,
                Name = $"Debug session {request.LaunchPlan.AdapterId}",
                Kind = BackgroundHandleKind.DebugSession,
                SourceKind = BackgroundTaskSourceKind.ToolCall,
                SourceId = reservation.TreeId,
                SessionId = request.Runtime.SessionId,
                ThreadId = request.Runtime.ThreadId,
                SupportedOperations = BackgroundHandleOperation.Status | BackgroundHandleOperation.Read |
                    BackgroundHandleOperation.Stop | BackgroundHandleOperation.Artifacts | BackgroundHandleOperation.Events
            }, handle, cancellationToken).ConfigureAwait(false);
            handle.AttachRegistration(registration);
            reservation.Commit(tree);
            managerCommitted = true;
            handle.CommitLive();
            if (tree.EventPublisher is not null)
                await tree.EventPublisher.PublishAsync(new DebugTreeStartedEvent
                {
                    DebugTreeId = reservation.TreeId,
                    DebugSessionId = sessionId,
                    AdapterId = request.LaunchPlan.AdapterId,
                    EnvironmentId = request.LaunchPlan.EnvironmentId,
                    IsAttach = request.IsAttach
                }, durable: true, CancellationToken.None).ConfigureAwait(false);
            published = true;
            var snapshot = await handle.GetStatusAsync(CancellationToken.None).ConfigureAwait(false);
            return new(reservation.TreeId, sessionId, snapshot, session.State.Status);
        }
        catch
        {
            if (!published)
            {
                startLifetime.Cancel();
                handle?.MarkPublicationFailed();
                if (managerCommitted)
                    await manager.RemoveAndDisposeAsync(scope, reservation.TreeId).ConfigureAwait(false);
                else if (tree is not null) await tree.DisposeAsync().ConfigureAwait(false);
                else if (session is not null) await session.DisposeAsync().ConfigureAwait(false);
            }
            throw;
        }
    }

    private async ValueTask<DebugSession> CreateProtocolSessionAsync(
        DebugSessionTree tree,
        string sessionId,
        string? parentSessionId,
        DebugAdapterLaunchPlan launchPlan,
        bool isAttach,
        JsonElement? restartData,
        DebugDesiredBreakpointSnapshot breakpoints,
        DebugSessionStartRequest request,
        CancellationToken lifetime,
        CancellationToken cancellationToken)
    {
        IDebugProtocolTransport? transport = null;
        DebugSession? session = null;
        var phase = "transport";
        try
        {
            transport = await _transportFactory.CreateAsync(launchPlan, cancellationToken).ConfigureAwait(false);
            session = new DebugSession
            {
                SessionId = sessionId,
                RootSessionId = tree.RootSessionId,
                ParentSessionId = parentSessionId,
                IsAttach = isAttach,
                Protocol = new DebugProtocolClient(transport, new DebugProtocolClientOptions
                {
                    HostTraceSink = request.HostTraceSink
                }),
                LaunchPlan = launchPlan
            };
            phase = "initialize";
            if (request.EventPublisher is not null)
            {
                session.OutputEvents = CreateOutputCoalescer(session, tree);
                session.ProgressEvents = CreateProgressCoalescer(session, tree);
            }
            transport = null;
            session.State.Transition(DebugSessionStatus.Initializing);
            var coordinator = new DebugConfigurationCoordinator(
                ct => ConfigureAsync(session, breakpoints, ct), lifetime);
            RegisterCoreHandlers(session, tree, coordinator, request);

            session.Capabilities = await session.Protocol.InitializeAsync(
                _initializePolicy.Create(launchPlan.AdapterId, request.InitializeFeatures),
                cancellationToken).ConfigureAwait(false);
            session.State.Transition(DebugSessionStatus.Configuring);
            phase = isAttach ? "attach" : "launch";
            var launchTask = coordinator.RunLaunchAsync(async ct =>
            {
                if (isAttach)
                    await session.Protocol.SendAsync(DebugProtocolDescriptors.AttachRequest,
                        DebugProtocolArgumentComposer.Attach(launchPlan.Arguments, restartData), ct, TimeSpan.FromSeconds(30)).ConfigureAwait(false);
                else
                    await session.Protocol.SendAsync(DebugProtocolDescriptors.LaunchRequest,
                        DebugProtocolArgumentComposer.Launch(launchPlan.Arguments, noDebug: false, restartData), ct, TimeSpan.FromSeconds(30)).ConfigureAwait(false);
            }, cancellationToken);
            await coordinator.AwaitStartBoundaryAsync(cancellationToken).ConfigureAwait(false);
            await launchTask.ConfigureAwait(false);
            phase = "configuration";
            if (session.State.Status == DebugSessionStatus.Configuring)
                session.State.Transition(DebugSessionStatus.Running);

            tree.AddSession(session);
            if (parentSessionId is not null && tree.Sessions.TryGetValue(parentSessionId, out var parent))
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
                if (exception is OperationCanceledException)
                    throw;
                throw new DebugAdapterStartException(
                    launchPlan.AdapterId, phase, diagnostics, exception);
            }
            if (transport is not null)
                await transport.DisposeAsync().ConfigureAwait(false);
            if (exception is OperationCanceledException)
                throw;
            throw new DebugAdapterStartException(
                launchPlan.AdapterId,
                phase,
                new DebugAdapterDiagnosticSnapshot(string.Empty, 0, 0, null),
                exception);
        }
    }

    private void RegisterCoreHandlers(
        DebugSession session,
        DebugSessionTree tree,
        DebugConfigurationCoordinator coordinator,
        DebugSessionStartRequest request)
    {
        session.HandlerRegistrations.Add(session.Protocol.OnFault(fault =>
        {
            try { session.State.Transition(DebugSessionStatus.Faulted); } catch { }
            if (session.SessionId != tree.RootSessionId)
            {
                tree.ObserveTerminated(session.SessionId);
                _ = Task.Run(async () =>
                {
                    tree.Sessions.TryRemove(session.SessionId, out _);
                    await session.DisposeAsync().ConfigureAwait(false);
                    await PublishAsync(tree, new DebugSessionFailedEvent
                    {
                        DebugTreeId = tree.Ownership.DebugTreeId,
                        DebugSessionId = session.SessionId,
                        AdapterId = session.LaunchPlan.AdapterId,
                        SafeReasonCode = fault.ReasonCode
                    }, durable: true).ConfigureAwait(false);
                });
                return;
            }
            var scope = new DebugTreeLookupScope(tree.Ownership.AgentRuntimeRegistrationId,
                tree.Ownership.SessionId, tree.Ownership.ThreadId);
            if (request.Runtime.SessionManager is not DebugSessionManager publishedManager ||
                !publishedManager.ListTrees(scope).Any(x => x.Ownership.DebugTreeId == tree.Ownership.DebugTreeId))
                return;
            tree.TryScheduleTerminal(async () =>
            {
                await PublishAsync(tree, CreateSummary(tree, session, "Faulted"), durable: true)
                    .ConfigureAwait(false);
                await publishedManager.RemoveAndDisposeAsync(scope, tree.Ownership.DebugTreeId).ConfigureAwait(false);
                await PublishAsync(tree, new DebugTreeFaultedEvent
                {
                    DebugTreeId = tree.Ownership.DebugTreeId,
                    DebugSessionId = session.SessionId,
                    AdapterId = session.LaunchPlan.AdapterId,
                    SafeReasonCode = fault.ReasonCode
                }, durable: true).ConfigureAwait(false);
            });
        }));
        session.HandlerRegistrations.Add(session.Protocol.OnEvent(DebugProtocolDescriptors.InitializedEvent, _ =>
        {
            coordinator.ObserveInitialized();
            return ValueTask.CompletedTask;
        }));
        session.HandlerRegistrations.Add(session.Protocol.OnEvent(DebugProtocolDescriptors.StoppedEvent, body =>
        {
            session.State.ObserveStopped(body.ThreadId, body.AllThreadsStopped == true, body.Reason, body.Description ?? body.Text);
            session.Projections.ObserveStopped(body.ThreadId, body.AllThreadsStopped == true);
            tree.Continuations.Revoke(session.SessionId);
            foreach (var stopped in session.State.Threads.Where(x => x.IsStopped &&
                         (body.AllThreadsStopped == true || x.ThreadId == body.ThreadId)))
                session.ScheduleFollowUp(() => FetchTopFramesAsync(session, stopped.ThreadId, stopped.SuspensionEpoch));
            if (tree.Sessions.ContainsKey(session.SessionId)) tree.ObserveStopped(session.SessionId);
            var thread = body.ThreadId is { } id ? session.State.Threads.SingleOrDefault(x => x.ThreadId == id) : null;
            return PublishAsync(tree, new DebugSessionStoppedEvent
            {
                DebugTreeId = tree.Ownership.DebugTreeId, DebugSessionId = session.SessionId,
                AdapterId = session.LaunchPlan.AdapterId, AdapterThreadId = body.ThreadId,
                Reason = body.Reason, Description = body.Description ?? body.Text,
                SuspensionEpoch = thread?.SuspensionEpoch
            }, durable: true);
        }));
        session.HandlerRegistrations.Add(session.Protocol.OnEvent(DebugProtocolDescriptors.ContinuedEvent, body =>
        {
            session.State.ObserveContinued(body.ThreadId, body.AllThreadsContinued == true);
            session.Projections.InvalidateForContinue(body.ThreadId, body.AllThreadsContinued == true);
            tree.Continuations.Revoke(session.SessionId);
            return PublishAsync(tree, new DebugSessionContinuedEvent
            {
                DebugTreeId = tree.Ownership.DebugTreeId, DebugSessionId = session.SessionId,
                AdapterId = session.LaunchPlan.AdapterId, AdapterThreadId = body.ThreadId,
                AllThreadsContinued = body.AllThreadsContinued == true
            }, durable: true);
        }));
        session.HandlerRegistrations.Add(session.Protocol.OnEvent(DebugProtocolDescriptors.ThreadEvent, body =>
        {
            if (body.Reason == "started") session.State.ObserveThread(body.ThreadId);
            else if (body.Reason == "exited")
            {
                session.State.RemoveThread(body.ThreadId);
                session.Projections.ObserveThreadRemoved(body.ThreadId);
            }
            return PublishAsync(tree, new DebugThreadChangedEvent
            {
                DebugTreeId = tree.Ownership.DebugTreeId, DebugSessionId = session.SessionId,
                AdapterId = session.LaunchPlan.AdapterId, Reason = body.Reason,
                AdapterThreadId = body.ThreadId
            }, durable: false);
        }));
        session.HandlerRegistrations.Add(session.Protocol.OnEvent(DebugProtocolDescriptors.ProcessEvent, body =>
        {
            session.Projections.ObserveProcess(body);
            return PublishAsync(tree, new DebugProcessChangedEvent
            {
                DebugTreeId = tree.Ownership.DebugTreeId, DebugSessionId = session.SessionId,
                AdapterId = session.LaunchPlan.AdapterId, Name = body.Name,
                SystemProcessId = body.SystemProcessId, IsLocalProcess = body.IsLocalProcess,
                StartMethod = body.StartMethod
            }, durable: false);
        }));
        session.HandlerRegistrations.Add(session.Protocol.OnEvent(DebugProtocolDescriptors.ModuleEvent, body =>
        {
            session.Projections.ObserveModule(body);
            tree.Continuations.Revoke(session.SessionId, "modules");
            return PublishAsync(tree, new DebugModuleChangedEvent
            {
                DebugTreeId = tree.Ownership.DebugTreeId, DebugSessionId = session.SessionId,
                AdapterId = session.LaunchPlan.AdapterId, Reason = body.Reason,
                OpaqueModuleId = SafeOpaqueToken(body.Module.Id.GetRawText()), Name = body.Module.Name,
                Path = body.Module.Path
            }, durable: false);
        }));
        session.HandlerRegistrations.Add(session.Protocol.OnEvent(DebugProtocolDescriptors.LoadedSourceEvent, body =>
        {
            session.Projections.ObserveLoadedSource(body);
            tree.Continuations.Revoke(session.SessionId, "loadedSources");
            return PublishAsync(tree, new DebugLoadedSourceChangedEvent
            {
                DebugTreeId = tree.Ownership.DebugTreeId, DebugSessionId = session.SessionId,
                AdapterId = session.LaunchPlan.AdapterId, Reason = body.Reason,
                Name = body.Source.Name, Path = body.Source.Path, SourceReference = body.Source.SourceReference
            }, durable: false);
        }));
        session.HandlerRegistrations.Add(session.Protocol.OnEvent(DebugProtocolDescriptors.InvalidatedEvent, body =>
        {
            session.Projections.Invalidate(body);
            tree.Continuations.Revoke(session.SessionId);
            return PublishAsync(tree, new DebugStateInvalidatedEvent
            {
                DebugTreeId = tree.Ownership.DebugTreeId, DebugSessionId = session.SessionId,
                AdapterId = session.LaunchPlan.AdapterId,
                Areas = body.Areas?.Select(x => x.Value).ToArray() ?? ["all"],
                AdapterThreadId = body.ThreadId, StackFrameId = body.StackFrameId
            }, durable: false);
        }));
        session.HandlerRegistrations.Add(session.Protocol.OnEvent(DebugProtocolDescriptors.MemoryEvent, body =>
        {
            var invalidated = session.Projections.ObserveMemory(body);
            tree.Continuations.Revoke(session.SessionId, "disassemble");
            return PublishAsync(tree, new DebugMemoryChangedEvent
            {
                DebugTreeId = tree.Ownership.DebugTreeId, DebugSessionId = session.SessionId,
                AdapterId = session.LaunchPlan.AdapterId,
                MemoryReferenceToken = SafeOpaqueToken(body.MemoryReference), Offset = body.Offset,
                Count = body.Count, InvalidatedRanges = invalidated
            }, durable: false);
        }));
        session.HandlerRegistrations.Add(session.Protocol.OnEvent(DebugProtocolDescriptors.CapabilitiesEvent, body =>
        {
            var before = session.Capabilities ?? new();
            session.Capabilities = DebugCapabilityMerger.Merge(before, body.Capabilities);
            session.Protocol.SetSupportsCancelRequest(session.Capabilities.SupportsCancelRequest == true);
            var changes = DebugCapabilityMerger.DescribeChanges(before, session.Capabilities);
            session.Projections.InvalidateForCapabilityRemoval(changes.Disabled);
            if (changes.Disabled.Count > 0) tree.Continuations.Revoke(session.SessionId);
            return PublishAsync(tree, new DebugCapabilitiesChangedEvent
            {
                DebugTreeId = tree.Ownership.DebugTreeId, DebugSessionId = session.SessionId,
                AdapterId = session.LaunchPlan.AdapterId, Enabled = changes.Enabled,
                Disabled = changes.Disabled
            }, durable: false);
        }));
        session.HandlerRegistrations.Add(session.Protocol.OnEvent(DebugProtocolDescriptors.ProgressStartEvent, body =>
        {
            var state = session.Progress.Start(body);
            session.ProgressEvents?.TryEnqueue(new(DebugProgressNotificationKind.Started, state));
            return ValueTask.CompletedTask;
        }));
        session.HandlerRegistrations.Add(session.Protocol.OnEvent(DebugProtocolDescriptors.ProgressUpdateEvent, body =>
        {
            var state = session.Progress.Update(body);
            if (state is not null) session.ProgressEvents?.TryEnqueue(new(DebugProgressNotificationKind.Updated, state));
            return ValueTask.CompletedTask;
        }));
        session.HandlerRegistrations.Add(session.Protocol.OnEvent(DebugProtocolDescriptors.ProgressEndEvent, body =>
        {
            var state = session.Progress.End(body);
            if (state is not null) session.ProgressEvents?.TryEnqueue(new(DebugProgressNotificationKind.Completed, state));
            return ValueTask.CompletedTask;
        }));
        session.HandlerRegistrations.Add(session.Protocol.OnEvent(DebugProtocolDescriptors.OutputEvent, body =>
        {
            var allowAnsi = session.Capabilities?.SupportsANSIStyling == true && request.InitializeFeatures.AnsiRendering;
            var sanitized = DebugOutputSanitizer.Sanitize(body.Output, allowAnsi);
            var record = session.Output.Append(tree.Ownership.DebugTreeId, session.SessionId, body, allowAnsi,
                body.VariablesReference is > 0 ? session.Projections.CreateSessionToken("variables", body.VariablesReference.Value) : null,
                body.LocationReference is > 0 ? session.Projections.CreateSessionToken("location", body.LocationReference.Value) : null);
            if (record.Category != DebugOutputCategory.Telemetry)
                session.OutputEvents?.TryEnqueue(record);
            if (System.Text.Encoding.UTF8.GetByteCount(sanitized) > DebugOutputBuffer.DefaultMaximumRecordBytes)
                session.ScheduleFollowUp(() => PersistOversizedOutputAsync(
                    tree, session, record, sanitized));
            return ValueTask.CompletedTask;
        }));
        session.HandlerRegistrations.Add(session.Protocol.OnEvent(DebugProtocolDescriptors.BreakpointEvent, body =>
        {
            session.ConfirmedBreakpoints.Reconcile(body.Reason, body.Breakpoint);
            return PublishAsync(tree, new DebugBreakpointChangedEvent
            {
                DebugTreeId = tree.Ownership.DebugTreeId,
                DebugSessionId = session.SessionId,
                AdapterId = session.LaunchPlan.AdapterId,
                Reason = body.Reason,
                BreakpointId = body.Breakpoint.Id,
                Verified = body.Breakpoint.Verified,
                Message = body.Breakpoint.Message,
                SourcePath = body.Breakpoint.Source?.Path,
                Line = body.Breakpoint.Line,
                Column = body.Breakpoint.Column,
                InstructionReference = body.Breakpoint.InstructionReference
            }, durable: true);
        }));
        if (request.HostRequestBroker is not null && request.InitializeFeatures.RunInTerminalHandler)
            session.HandlerRegistrations.Add(session.Protocol.RegisterReverseRequestHandler(
                DebugProtocolDescriptors.RunInTerminalRequest,
                async (body, cancellationToken) =>
                {
                    tree.Authorization.Demand(DebugTreeGrant.TerminalProcesses);
                    var shell = body.ArgsCanBeInterpretedByShell == true;
                    if (shell)
                    {
                        if (!request.InitializeFeatures.ShellArgumentAuthorization)
                            throw new UnauthorizedAccessException("Shell interpretation was not advertised to the adapter.");
                        tree.Authorization.Demand(DebugTreeGrant.ShellInterpretation);
                    }
                    var response = await request.HostRequestBroker.RequestRunInTerminalAsync(
                        tree.RuntimeBinding.EventScope with
                        {
                            DebugTreeId = tree.Ownership.DebugTreeId,
                            DebugSessionId = session.SessionId,
                            AdapterId = session.LaunchPlan.AdapterId
                        },
                        tree.Ownership.DebugTreeId,
                        session.SessionId,
                        body.Kind,
                        body.Title,
                        body.Cwd,
                        body.Args,
                        body.Env ?? new Dictionary<string, string?>(),
                        shell,
                        cancellationToken).ConfigureAwait(false);
                    return new RunInTerminalResponseBody
                    {
                        ProcessId = response.ProcessId,
                        ShellProcessId = response.ShellProcessId
                    };
                }));
        if (request.ChildSessionPlanFactory is not null && request.InitializeFeatures.StartDebuggingHandler)
            session.HandlerRegistrations.Add(session.Protocol.RegisterReverseRequestHandler(
                DebugProtocolDescriptors.StartDebuggingRequest,
                async (body, cancellationToken) =>
                {
                    await StartChildAsync(tree, session, body, request, cancellationToken).ConfigureAwait(false);
                    return new DapNoBody();
                }));
        session.HandlerRegistrations.Add(session.Protocol.OnEvent(DebugProtocolDescriptors.ExitedEvent, body =>
        {
            session.ExitCode = body.ExitCode;
            if (session.State.Status is DebugSessionStatus.Initializing or DebugSessionStatus.Configuring)
                coordinator.ObserveTerminal("DEBUGGEE_EXITED_DURING_START");
            return PublishAsync(tree, new DebugSessionExitedEvent
            {
                DebugTreeId = tree.Ownership.DebugTreeId, DebugSessionId = session.SessionId,
                AdapterId = session.LaunchPlan.AdapterId, ExitCode = body.ExitCode
            }, durable: true);
        }));
        session.HandlerRegistrations.Add(session.Protocol.OnEvent(DebugProtocolDescriptors.TerminatedEvent, async body =>
        {
            session.RestartData = body.Restart?.Clone();
            if (session.State.Status is DebugSessionStatus.Initializing or DebugSessionStatus.Configuring)
                coordinator.ObserveTerminal("ADAPTER_TERMINATED_DURING_START");
            else if (body.Restart is null)
            {
                session.State.Transition(DebugSessionStatus.Terminated);
                if (tree.Sessions.ContainsKey(session.SessionId)) tree.ObserveTerminated(session.SessionId);
            }
            await PublishAsync(tree, new DebugSessionTerminatedEvent
            {
                DebugTreeId = tree.Ownership.DebugTreeId, DebugSessionId = session.SessionId,
                AdapterId = session.LaunchPlan.AdapterId, RestartRequested = body.Restart is not null
            }, durable: true).ConfigureAwait(false);
            if (body.Restart is null && session.SessionId == tree.RootSessionId)
            {
                var scope = new DebugTreeLookupScope(tree.Ownership.AgentRuntimeRegistrationId,
                    tree.Ownership.SessionId, tree.Ownership.ThreadId);
                tree.TryScheduleTerminal(async () =>
                {
                    await PublishAsync(tree, CreateSummary(tree, session, "Terminated"), durable: true)
                        .ConfigureAwait(false);
                    var terminatedEvent = new DebugTreeTerminatedEvent
                    {
                        DebugTreeId = tree.Ownership.DebugTreeId,
                        DebugSessionId = session.SessionId,
                        AdapterId = session.LaunchPlan.AdapterId,
                        SafeReasonCode = "ADAPTER_TERMINATED"
                    };
                    if (request.Runtime.SessionManager is DebugSessionManager runtimeManager)
                        await runtimeManager.RemoveAndDisposeAsync(scope, tree.Ownership.DebugTreeId)
                            .ConfigureAwait(false);
                    await PublishAsync(tree, terminatedEvent, durable: true).ConfigureAwait(false);
                });
            }
        }));
    }

    private static ValueTask PublishAsync(DebugSessionTree tree, DebugLifecycleEvent @event, bool durable)
    {
        if (tree.EventPublisher is null) return ValueTask.CompletedTask;
        return tree.EventPublisher.PublishAsync(@event, durable, CancellationToken.None);
    }

    private static DebugSessionSummaryEvent CreateSummary(
        DebugSessionTree tree, DebugSession session, string finalStatus)
    {
        var output = session.Output.Snapshot(includeTelemetry: true);
        return new DebugSessionSummaryEvent
        {
            DebugTreeId = tree.Ownership.DebugTreeId,
            DebugSessionId = session.SessionId,
            AdapterId = session.LaunchPlan.AdapterId,
            FinalStatus = finalStatus,
            ExitCode = session.ExitCode,
            DurationMilliseconds = Math.Max(0, (long)(DateTimeOffset.UtcNow - session.CreatedAt).TotalMilliseconds),
            ChildSessionCount = session.ChildSessionIds.Count,
            RetainedOutputBytes = output.RetainedBytes,
            DroppedOutputRecords = output.DroppedRecords,
            DroppedOutputBytes = output.DroppedBytes,
            ProjectionFailures = session.Projections.FollowUpFailures
        };
    }

    private static async Task PersistOversizedOutputAsync(
        DebugSessionTree tree,
        DebugSession session,
        DebugOutputRecord record,
        string text)
    {
        const int maximumArtifactBytes = 4 * 1024 * 1024;
        var result = await tree.Artifacts.WriteTextAsync(text, "debug-output", record.Category.ToString(),
            session.LaunchPlan.AdapterId, session.SessionId, maximumArtifactBytes, CancellationToken.None)
            .ConfigureAwait(false);
        if (result.Status != DebugArtifactWriteStatus.Stored || result.Address is not { } address) return;
        tree.AddStoredArtifact(new("debug-output", session.SessionId, address.ContentId,
            address.Scope.Value, address.Version, new Dictionary<string, string>
            {
                ["adapter"] = session.LaunchPlan.AdapterId,
                ["debugTreeId"] = tree.Ownership.DebugTreeId,
                ["debugSessionId"] = session.SessionId,
                ["category"] = record.Category.ToString()
            }));
        await PublishAsync(tree, new DebugOutputAvailableEvent
        {
            DebugTreeId = tree.Ownership.DebugTreeId,
            DebugSessionId = session.SessionId,
            AdapterId = session.LaunchPlan.AdapterId,
            FirstSequence = record.Sequence,
            LastSequence = record.Sequence,
            Category = record.Category.ToString(),
            ContentScope = address.Scope.Value,
            ContentId = address.ContentId,
            ContentVersion = address.Version,
            DroppedRecords = record.DroppedRecordsBefore,
            DroppedBytes = record.DroppedBytesBefore
        }, durable: true).ConfigureAwait(false);
    }

    private static string SafeOpaqueToken(string value)
    {
        var hash = System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(value));
        return Convert.ToHexString(hash.AsSpan(0, 12)).ToLowerInvariant();
    }

    private static DebugOutputEventCoalescer CreateOutputCoalescer(
        DebugSession session,
        DebugSessionTree tree)
        => new(batch => PublishAsync(tree, new DebugOutputAvailableEvent
        {
            DebugTreeId = tree.Ownership.DebugTreeId,
            DebugSessionId = session.SessionId,
            AdapterId = session.LaunchPlan.AdapterId,
            FirstSequence = batch.FirstSequence,
            LastSequence = batch.LastSequence,
            Category = batch.Category.ToString(),
            InlineText = batch.Text,
            DroppedRecords = batch.DroppedPublications,
            DroppedBytes = 0
        }, durable: false));

    private static DebugProgressEventCoalescer CreateProgressCoalescer(
        DebugSession session,
        DebugSessionTree tree)
        => new(notification =>
        {
            DebugProgressEvent @event = notification.Kind switch
            {
                DebugProgressNotificationKind.Started => new DebugProgressStartedEvent
                {
                    DebugTreeId = tree.Ownership.DebugTreeId,
                    DebugSessionId = session.SessionId,
                    AdapterId = session.LaunchPlan.AdapterId,
                    ProgressId = notification.State.ProgressId,
                    Title = notification.State.Title,
                    Cancellable = notification.State.Cancellable
                },
                DebugProgressNotificationKind.Updated => new DebugProgressUpdatedEvent
                {
                    DebugTreeId = tree.Ownership.DebugTreeId,
                    DebugSessionId = session.SessionId,
                    AdapterId = session.LaunchPlan.AdapterId,
                    ProgressId = notification.State.ProgressId
                },
                _ => new DebugProgressCompletedEvent
                {
                    DebugTreeId = tree.Ownership.DebugTreeId,
                    DebugSessionId = session.SessionId,
                    AdapterId = session.LaunchPlan.AdapterId,
                    ProgressId = notification.State.ProgressId
                }
            };
            @event = @event with
            {
                Message = notification.State.Message,
                Percentage = notification.State.Percentage
            };
            return PublishAsync(tree, @event, durable: false);
        });

    private async ValueTask StartChildAsync(
        DebugSessionTree tree,
        DebugSession parent,
        StartDebuggingRequestArguments arguments,
        DebugSessionStartRequest request,
        CancellationToken cancellationToken)
    {
        tree.RuntimeBinding.State.ThrowIfUnavailable();
        tree.Authorization.Demand(DebugTreeGrant.ChildSessions);
        if (arguments.Configuration.ValueKind != JsonValueKind.Object)
            throw new InvalidOperationException("startDebugging configuration must be a JSON object.");
        if (arguments.Request is not ("launch" or "attach"))
            throw new InvalidOperationException("startDebugging request must be 'launch' or 'attach'.");

        var plan = await request.ChildSessionPlanFactory!.CreateAsync(
            tree.RuntimeBinding,
            tree.Authorization,
            parent.LaunchPlan,
            arguments.Request,
            arguments.Configuration.Clone(),
            arguments.OutputPresentation,
            tree.Breakpoints.Snapshot,
            cancellationToken).ConfigureAwait(false);
        if (!string.Equals(plan.LaunchPlan.AdapterId, parent.LaunchPlan.AdapterId, StringComparison.Ordinal))
            throw new UnauthorizedAccessException("A child session must resolve the same debug adapter type as its parent.");
        tree.Authorization.ValidateCurrent(tree.RuntimeBinding, plan.LaunchPlan);

        var childId = Guid.NewGuid().ToString("N");
        var child = await CreateProtocolSessionAsync(
            tree, childId, parent.SessionId, plan.LaunchPlan, plan.IsAttach, restartData: null,
            plan.Breakpoints, request, CancellationToken.None, cancellationToken).ConfigureAwait(false);
        await PublishAsync(tree, new DebugChildSessionStartedEvent
        {
            DebugTreeId = tree.Ownership.DebugTreeId,
            DebugSessionId = child.SessionId,
            AdapterId = child.LaunchPlan.AdapterId,
            ParentDebugSessionId = parent.SessionId,
            IsAttach = child.IsAttach,
            OutputPresentation = arguments.OutputPresentation
        }, durable: true).ConfigureAwait(false);
    }

    private static async Task ConfigureAsync(
        DebugSession session,
        DebugDesiredBreakpointSnapshot breakpoints,
        CancellationToken cancellationToken)
    {
        await DebugBreakpointProtocolApplier.ApplyAllAsync(session, breakpoints, cancellationToken)
            .ConfigureAwait(false);
        if (session.Capabilities?.SupportsConfigurationDoneRequest == true)
            await session.Protocol.SendAsync(DebugProtocolDescriptors.ConfigurationDoneRequest, new ConfigurationDoneArguments(), cancellationToken).ConfigureAwait(false);
    }

    private static async Task FetchTopFramesAsync(DebugSession session, int threadId, long suspensionEpoch)
    {
        var response = await session.Protocol.SendAsync(DebugProtocolDescriptors.StackTraceRequest,
            new StackTraceArguments { ThreadId = threadId, StartFrame = 0, Levels = 20 },
            CancellationToken.None, TimeSpan.FromSeconds(5)).ConfigureAwait(false);
        var current = session.State.Threads.SingleOrDefault(x => x.ThreadId == threadId);
        if (current is { IsStopped: true } && current.SuspensionEpoch == suspensionEpoch)
            session.Projections.CacheStackFrames(threadId, suspensionEpoch, response.StackFrames);
    }
}
