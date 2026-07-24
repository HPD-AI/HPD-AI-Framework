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

/// <summary>Complete authorized request to reserve and activate one semantic debug execution.</summary>
internal sealed record DebugExecutionStartRequest
{
    public required DebugRuntimeBinding Runtime { get; init; }
    public required DebugExecutionPlan ExecutionPlan { get; init; }
    public required DebugPermissionDecision Permission { get; init; }
    public LaunchDebugOperation? SemanticLaunchOperation { get; init; }
    public bool IsRestart { get; init; }
    public required IAgentBackgroundHandleRegistry BackgroundHandles { get; init; }
    public required DebugInitializeFeatures InitializeFeatures { get; init; }
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
    DebugSessionStatus Status,
    int OwnedResourceCount,
    DebugBreakpointCounts Breakpoints);

internal sealed class DebugExecutionStartOrchestrator
{
    private readonly IDebugProtocolSessionStarter _protocolStarter;
    private readonly IDebugExecutionPlanActivator _activator;

    public DebugExecutionStartOrchestrator(
        IDebugProtocolSessionStarter protocolStarter,
        IDebugExecutionPlanActivator activator)
    {
        _protocolStarter = protocolStarter ??
            throw new ArgumentNullException(nameof(protocolStarter));
        _activator = activator ?? throw new ArgumentNullException(nameof(activator));
    }

    public async ValueTask<DebugSessionStartResult> StartAsync(
        DebugExecutionStartRequest request,
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
            (request.ExecutionPlan.EnvironmentId != processExecution.EnvironmentId ||
             request.ExecutionPlan.EnvironmentRevision != processExecution.EnvironmentRevision))
            throw new InvalidOperationException("The debug execution plan does not match the captured runtime Environment.");
        var expectedPermission = request.IsRestart
            ? DebugPermissionClass.Lifecycle
            : request.ExecutionPlan.SemanticStartKind ==
                DebugSemanticStartKind.ExplicitAttach
                ? DebugPermissionClass.Attach
                : DebugPermissionClass.Launch;
        if (request.Permission.PermissionClass != expectedPermission)
            throw new UnauthorizedAccessException(
                "The debug execution permission does not authorize this semantic start.");

        await using var reservation = manager.ReserveTree(
            request.Runtime.SessionId,
            request.Runtime.ThreadId,
            request.ExecutionPlan.EnvironmentId,
            request.ExecutionPlan.EnvironmentRevision);
        var scope = new DebugTreeLookupScope(manager.RuntimeId, request.Runtime.SessionId, request.Runtime.ThreadId);
        var sessionId = Guid.NewGuid().ToString("N");
        var adapterIdentity = PlannedAdapterIdentity(request.ExecutionPlan);
        var scopedPublisher = request.EventPublisher switch
        {
            null => null,
            IDebugEventPublisher publisher => publisher.Bind(request.Runtime.EventScope),
            _ => new DebugScopedEventPublisher(
                request.EventPublisher,
                request.Runtime.EventScope)
        };
        DebugSession? session = null;
        DebugSessionTree? tree = null;
        DebugSessionHandle? handle = null;
        var published = false;
        var managerCommitted = false;
        DebugActivatedExecution? activated = null;
        using var startLifetime = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        try
        {
            await PublishStartEventAsync(scopedPublisher, new DebugExecutionPlannedEvent
            {
                DebugTreeId = reservation.TreeId,
                DebugSessionId = sessionId,
                AdapterId = adapterIdentity.AdapterId,
                SemanticStartKind = request.ExecutionPlan.SemanticStartKind,
                AdapterStartMethod = adapterIdentity.Method,
                ExecutionPlannerId = request.ExecutionPlan.PlannerId
            }).ConfigureAwait(false);
            await PublishStartEventAsync(scopedPublisher, new DebugExecutionActivatingEvent
            {
                DebugTreeId = reservation.TreeId,
                DebugSessionId = sessionId,
                AdapterId = adapterIdentity.AdapterId,
                SemanticStartKind = request.ExecutionPlan.SemanticStartKind,
                AdapterStartMethod = adapterIdentity.Method,
                ExecutionPlannerId = request.ExecutionPlan.PlannerId
            }).ConfigureAwait(false);
            activated = await _activator.ActivateAsync(
                request.ExecutionPlan,
                new DebugExecutionActivationContext
                {
                    Ownership = reservation.Ownership,
                    Runtime = request.Runtime,
                    Permission = request.Permission,
                    IsRestart = request.IsRestart,
                    DebugSessionId = sessionId,
                    EventPublisher = scopedPublisher
                },
                cancellationToken).ConfigureAwait(false);
            var adapterPlan = activated.AdapterPlan;
            tree = new DebugSessionTree
            {
                Ownership = reservation.Ownership,
                RootSessionId = sessionId,
                RuntimeBinding = request.Runtime,
                Authorization = DebugTreeAuthorization.Create(
                    request.Runtime, reservation.Ownership, adapterPlan,
                    activated.SemanticStartKind, request.ExecutionPlan.PlannerId, request.Authorization),
                SemanticRestartOperation = request.SemanticLaunchOperation,
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
                EventPublisher = scopedPublisher
            };
            foreach (var resource in activated.OwnedResources)
                tree.OwnedResources.Enqueue(resource);
            tree.Breakpoints.Seed(request.ExecutionPlan.InitialConfiguration);
            session = await _protocolStarter.StartAsync(
                tree, sessionId, parentSessionId: null, adapterPlan,
                request.RestartData, tree.Breakpoints.Snapshot,
                request.ExecutionPlan.InitialConfiguration.BreakpointPolicy,
                request, startLifetime.Token,
                RegisterCoreHandlers, CreateOutputCoalescer, CreateProgressCoalescer,
                cancellationToken).ConfigureAwait(false);

            handle = new DebugSessionHandle(manager, scope, reservation.TreeId);
            var registration = await request.BackgroundHandles.RegisterHandleAsync(new()
            {
                HandleId = reservation.TreeId,
                Name = $"Debug session {adapterPlan.AdapterId}",
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
                    AdapterId = adapterPlan.AdapterId,
                    EnvironmentId = adapterPlan.EnvironmentId,
                    SemanticStartKind = activated.SemanticStartKind,
                    AdapterStartMethod = adapterPlan.Method,
                    ExecutionPlannerId = request.ExecutionPlan.PlannerId
                }, durable: true, CancellationToken.None).ConfigureAwait(false);
            published = true;
            var snapshot = await handle.GetStatusAsync(CancellationToken.None).ConfigureAwait(false);
            var desired = tree.Breakpoints.Snapshot;
            var adapterBreakpoints = session.AdapterBreakpoints.Snapshot;
            var requestedBreakpoints = desired.Source.Length +
                desired.Function.Length +
                desired.Exception.Length +
                desired.Instruction.Length +
                desired.Data.Length;
            var verifiedBreakpoints = adapterBreakpoints.Count(item => item.Verified);
            return new(
                reservation.TreeId,
                sessionId,
                snapshot,
                session.State.Status,
                activated.OwnedResources.Count,
                new(
                    requestedBreakpoints,
                    adapterBreakpoints.Length,
                    verifiedBreakpoints,
                    Math.Max(0, requestedBreakpoints - verifiedBreakpoints)));
        }
        catch (Exception exception)
        {
            if (!published)
            {
                startLifetime.Cancel();
                handle?.MarkPublicationFailed();
                if (managerCommitted)
                    await manager.DiscardAndDisposeAsync(scope, reservation.TreeId)
                        .ConfigureAwait(false);
                else if (tree is not null) await tree.DisposeAsync().ConfigureAwait(false);
                else if (session is not null) await session.DisposeAsync().ConfigureAwait(false);
                else if (activated is not null)
                    foreach (var resource in activated.OwnedResources.Reverse())
                        try { await resource.DisposeAsync().ConfigureAwait(false); } catch { }
            }
            await PublishStartEventAsync(
                scopedPublisher,
                new DebugExecutionActivationFailedEvent
                {
                    DebugTreeId = reservation.TreeId,
                    DebugSessionId = sessionId,
                    AdapterId = adapterIdentity.AdapterId,
                    ExecutionPlannerId = request.ExecutionPlan.PlannerId,
                    SafeReasonCode = exception is DebugStartPlanningException planning
                        ? planning.Kind
                        : "debug_execution_activation_failed"
                }).ConfigureAwait(false);
            throw;
        }
    }

    private static (string AdapterId, DebugAdapterStartMethod Method)
        PlannedAdapterIdentity(DebugExecutionPlan plan)
        => plan switch
        {
            DirectAdapterDebugExecutionPlan direct =>
                (direct.Adapter.AdapterId, direct.Adapter.Method),
            HostedAttachDebugExecutionPlan hosted =>
                (hosted.Attach.AdapterId, DebugAdapterStartMethod.Attach),
            PreparedAdapterDebugExecutionPlan prepared =>
                (prepared.Launch.AdapterId, DebugAdapterStartMethod.Launch),
            _ => ("unknown", DebugAdapterStartMethod.Launch)
        };

    private static async ValueTask PublishStartEventAsync(
        ITreeDebugEventPublisher? publisher,
        DebugLifecycleEvent @event)
    {
        if (publisher is null)
            return;
        try
        {
            await publisher.PublishAsync(
                @event,
                durable: true,
                CancellationToken.None).ConfigureAwait(false);
        }
        catch
        {
            // Lifecycle telemetry must never replace the start or cleanup result.
        }
    }

    private void RegisterCoreHandlers(
        DebugSession session,
        DebugSessionTree tree,
        DebugConfigurationCoordinator coordinator,
        DebugExecutionStartRequest request)
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
                        AdapterId = session.AdapterPlan.AdapterId,
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
                await publishedManager.RetainAndDisposeAsync(
                    scope,
                    tree.Ownership.DebugTreeId,
                    "Faulted",
                    fault.ReasonCode).ConfigureAwait(false);
                await PublishAsync(tree, new DebugTerminalRecordRetainedEvent
                {
                    DebugTreeId = tree.Ownership.DebugTreeId,
                    DebugSessionId = session.SessionId,
                    AdapterId = session.AdapterPlan.AdapterId,
                    FinalStatus = "Faulted"
                }, durable: true).ConfigureAwait(false);
                await PublishAsync(tree, new DebugTreeFaultedEvent
                {
                    DebugTreeId = tree.Ownership.DebugTreeId,
                    DebugSessionId = session.SessionId,
                    AdapterId = session.AdapterPlan.AdapterId,
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
                AdapterId = session.AdapterPlan.AdapterId, AdapterThreadId = body.ThreadId,
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
                AdapterId = session.AdapterPlan.AdapterId, AdapterThreadId = body.ThreadId,
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
                AdapterId = session.AdapterPlan.AdapterId, Reason = body.Reason,
                AdapterThreadId = body.ThreadId
            }, durable: false);
        }));
        session.HandlerRegistrations.Add(session.Protocol.OnEvent(DebugProtocolDescriptors.ProcessEvent, body =>
        {
            session.Projections.ObserveProcess(body);
            return PublishAsync(tree, new DebugProcessChangedEvent
            {
                DebugTreeId = tree.Ownership.DebugTreeId, DebugSessionId = session.SessionId,
                AdapterId = session.AdapterPlan.AdapterId, Name = body.Name,
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
                AdapterId = session.AdapterPlan.AdapterId, Reason = body.Reason,
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
                AdapterId = session.AdapterPlan.AdapterId, Reason = body.Reason,
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
                AdapterId = session.AdapterPlan.AdapterId,
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
                AdapterId = session.AdapterPlan.AdapterId,
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
                AdapterId = session.AdapterPlan.AdapterId, Enabled = changes.Enabled,
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
            session.AdapterBreakpoints.Reconcile(body.Reason, body.Breakpoint);
            return PublishAsync(tree, new DebugBreakpointChangedEvent
            {
                DebugTreeId = tree.Ownership.DebugTreeId,
                DebugSessionId = session.SessionId,
                AdapterId = session.AdapterPlan.AdapterId,
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
                            AdapterId = session.AdapterPlan.AdapterId
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
                AdapterId = session.AdapterPlan.AdapterId, ExitCode = body.ExitCode
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
                AdapterId = session.AdapterPlan.AdapterId, RestartRequested = body.Restart is not null
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
                        AdapterId = session.AdapterPlan.AdapterId,
                        SafeReasonCode = "ADAPTER_TERMINATED"
                    };
                    if (request.Runtime.SessionManager is DebugSessionManager runtimeManager)
                        await runtimeManager.RetainAndDisposeAsync(
                            scope,
                            tree.Ownership.DebugTreeId,
                            "Terminated",
                            "ADAPTER_TERMINATED")
                            .ConfigureAwait(false);
                    await PublishAsync(tree, new DebugTerminalRecordRetainedEvent
                    {
                        DebugTreeId = tree.Ownership.DebugTreeId,
                        DebugSessionId = session.SessionId,
                        AdapterId = session.AdapterPlan.AdapterId,
                        FinalStatus = "Terminated"
                    }, durable: true).ConfigureAwait(false);
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
            AdapterId = session.AdapterPlan.AdapterId,
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
            session.AdapterPlan.AdapterId, session.SessionId, maximumArtifactBytes, CancellationToken.None)
            .ConfigureAwait(false);
        if (result.Status != DebugArtifactWriteStatus.Stored || result.Address is not { } address) return;
        tree.AddStoredArtifact(new("debug-output", session.SessionId, address.ContentId,
            address.Scope.Value, address.Version, new Dictionary<string, string>
            {
                ["adapter"] = session.AdapterPlan.AdapterId,
                ["debugTreeId"] = tree.Ownership.DebugTreeId,
                ["debugSessionId"] = session.SessionId,
                ["category"] = record.Category.ToString()
            }));
        await PublishAsync(tree, new DebugOutputAvailableEvent
        {
            DebugTreeId = tree.Ownership.DebugTreeId,
            DebugSessionId = session.SessionId,
            AdapterId = session.AdapterPlan.AdapterId,
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
            AdapterId = session.AdapterPlan.AdapterId,
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
                    AdapterId = session.AdapterPlan.AdapterId,
                    ProgressId = notification.State.ProgressId,
                    Title = notification.State.Title,
                    Cancellable = notification.State.Cancellable
                },
                DebugProgressNotificationKind.Updated => new DebugProgressUpdatedEvent
                {
                    DebugTreeId = tree.Ownership.DebugTreeId,
                    DebugSessionId = session.SessionId,
                    AdapterId = session.AdapterPlan.AdapterId,
                    ProgressId = notification.State.ProgressId
                },
                _ => new DebugProgressCompletedEvent
                {
                    DebugTreeId = tree.Ownership.DebugTreeId,
                    DebugSessionId = session.SessionId,
                    AdapterId = session.AdapterPlan.AdapterId,
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
        DebugExecutionStartRequest request,
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
            parent.AdapterPlan,
            arguments.Request,
            arguments.Configuration.Clone(),
            arguments.OutputPresentation,
            tree.Breakpoints.Snapshot,
            cancellationToken).ConfigureAwait(false);
        if (!string.Equals(plan.AdapterPlan.AdapterId, parent.AdapterPlan.AdapterId, StringComparison.Ordinal))
            throw new UnauthorizedAccessException("A child session must resolve the same debug adapter type as its parent.");
        tree.Authorization.ValidateCurrent(tree.RuntimeBinding, plan.AdapterPlan);

        var childId = Guid.NewGuid().ToString("N");
        var child = await _protocolStarter.StartAsync(
            tree, childId, parent.SessionId, plan.AdapterPlan, restartData: null,
            plan.Breakpoints, DebugInitialBreakpointPolicy.AllowPending,
            request, CancellationToken.None,
            RegisterCoreHandlers, CreateOutputCoalescer, CreateProgressCoalescer,
            cancellationToken).ConfigureAwait(false);
        await PublishAsync(tree, new DebugChildSessionStartedEvent
        {
            DebugTreeId = tree.Ownership.DebugTreeId,
            DebugSessionId = child.SessionId,
            AdapterId = child.AdapterPlan.AdapterId,
            ParentDebugSessionId = parent.SessionId,
            AdapterStartMethod = child.AdapterStartMethod,
            OutputPresentation = arguments.OutputPresentation
        }, durable: true).ConfigureAwait(false);
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
