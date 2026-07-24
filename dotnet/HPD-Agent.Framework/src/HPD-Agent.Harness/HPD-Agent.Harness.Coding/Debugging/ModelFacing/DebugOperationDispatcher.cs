using System.Globalization;
using HPD.Agent.Middleware;
using HPD.Agent.ToolHarness.Coding.Debugging.Protocol;
using HPDOS.ToolHarnesses.Middleware;

namespace HPD.Agent.ToolHarness.Coding.Debugging;

public sealed class DebugOperationDispatcher
{
    private readonly DebugRuntimeServiceFactory _runtimeServices;
    private readonly DebugResultFormatter _formatter;
    private readonly DebugExecutionPlanningService? _starts;
    private readonly DebugPermissionAuthorizationService _authorization;

    internal DebugOperationDispatcher(
        DebugRuntimeServiceFactory runtimeServices,
        DebugResultFormatter formatter,
        DebugPermissionAuthorizationService authorization,
        DebugExecutionPlanningService? starts = null)
    {
        _runtimeServices = runtimeServices;
        _formatter = formatter;
        _authorization = authorization;
        _starts = starts;
    }

    public async Task<string> ExecuteAsync(
        DebugOperation operation,
        FunctionExecutionContext context,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(operation);
        ArgumentNullException.ThrowIfNull(context);
        var action = Action(operation);
        try
        {
            var permission = _authorization.DemandApproved(context, action);
            var result = await ExecuteCoreAsync(operation, permission, context, cancellationToken).ConfigureAwait(false);
            if (!context.ResultMetadata.TryGet<DebugOperationMetadata>(
                    CodingToolMetadataKeys.DebugOperation,
                    out _))
                context.ResultMetadata.Set(
                    CodingToolMetadataKeys.DebugOperation,
                    new DebugOperationMetadata(
                        action,
                        TreeId(operation),
                        SessionId(operation),
                        true));
            return result;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return Failure(context, action, operation, "request_cancelled", "The debugger operation was cancelled.");
        }
        catch (DebugSemanticException exception)
        {
            var message = exception.Reason == DebugSemanticFailureReason.CapabilityUnavailable
                ? $"The selected adapter does not support the '{action}' action."
                : SafeMessage(exception.Reason);
            return Failure(context, action, operation, ErrorKind(exception.Reason), message);
        }
        catch (DebugStartPlanningException exception)
        {
            return Failure(context, action, operation, exception.Kind, exception.Message);
        }
        catch (DebugExceptionBreakpointValidationException exception)
        {
            context.ResultMetadata.Set(
                CodingToolMetadataKeys.DebugExceptionFilters,
                exception.AvailableFilters);
            return Failure(
                context,
                action,
                operation,
                "invalid_exception_filter",
                exception.Message,
                Attr(("availableFilterIds", Join(exception.AvailableFilters.Select(item => item.FilterId)))),
                exception.AvailableFilters.Select(item =>
                    $"{item.FilterId}: {item.Label}; default={item.IsDefault}; supportsCondition={item.SupportsCondition}"));
        }
        catch (DebugAdapterRequestException exception)
        {
            var failure = ClassifyProtocolFailure(action, exception);
            return Failure(context, action, operation, failure.Kind, failure.Message);
        }
        catch (DebugProtocolException exception)
        {
            var failure = ClassifyProtocolFailure(action, exception);
            return Failure(context, action, operation, failure.Kind, failure.Message);
        }
        catch (DebugSessionOwnershipException)
        {
            return Failure(context, action, operation, "session_ownership_mismatch",
                "The debug tree does not belong to the current runtime, session, or thread.");
        }
        catch (KeyNotFoundException)
        {
            return Failure(context, action, operation, "session_not_found", "The requested debug tree or session was not found.");
        }
        catch (UnauthorizedAccessException)
        {
            return Failure(context, action, operation, "permission_denied", "The debugger operation is not authorized.");
        }
        catch (ArgumentException)
        {
            return Failure(context, action, operation, "invalid_request", "The debugger operation contains invalid arguments.");
        }
        catch (InvalidOperationException)
        {
            return Failure(context, action, operation, "invalid_session_state",
                "The debugger operation is unavailable in the current state.");
        }
        catch (Exception)
        {
            return Failure(context, action, operation, "internal_failure", "The debugger operation failed.");
        }
    }

    private async Task<string> ExecuteCoreAsync(
        DebugOperation operation,
        DebugPermissionDecision permission,
        FunctionExecutionContext context,
        CancellationToken cancellationToken)
    {
        if (operation is LaunchDebugOperation launch)
            return await RequireStarts().LaunchAsync(
                launch, permission, context, cancellationToken).ConfigureAwait(false);
        if (operation is AttachDebugOperation attach)
            return await RequireStarts().AttachAsync(
                attach, permission, context, cancellationToken).ConfigureAwait(false);

        var runtime = DebugRuntimeBinding.Capture(context, requireProcessExecution: false);
        var services = _runtimeServices.Create(runtime);
        var owner = new DebugTreeLookupScope(runtime.AgentRuntimeRegistrationId, runtime.SessionId, runtime.ThreadId);

        return operation switch
        {
            ListDebugSessionsOperation => ListSessions(services, owner, context),
            GetDebugStatusOperation request => Status(services, owner, request, context),
            GetDebugHealthOperation request => Health(services, owner, request, context),
            SnapshotDebugOperation request => Snapshot(services, owner, request, context),
            InspectDebugStopOperation request => await InspectStopAsync(services, owner, request, context, cancellationToken).ConfigureAwait(false),
            DisconnectDebugOperation request => await DisconnectAsync(services, owner, request, cancellationToken).ConfigureAwait(false),
            TerminateDebugOperation request => await TerminateAsync(
                services, owner, request, context, cancellationToken).ConfigureAwait(false),
            RestartDebugOperation request => await RestartAsync(
                services,
                owner,
                request,
                permission,
                context,
                cancellationToken).ConfigureAwait(false),
            SetSourceBreakpointsOperation request => await SetSourceBreakpointsAsync(services, owner, request, context, cancellationToken).ConfigureAwait(false),
            SetFunctionBreakpointsOperation request => await SetFunctionBreakpointsAsync(services, owner, request, context, cancellationToken).ConfigureAwait(false),
            SetExceptionBreakpointsOperation request => await SetExceptionBreakpointsAsync(services, owner, request, context, cancellationToken).ConfigureAwait(false),
            SetInstructionBreakpointsOperation request => await SetInstructionBreakpointsAsync(services, owner, request, context, cancellationToken).ConfigureAwait(false),
            DiscoverDataBreakpointOperation request => await DiscoverDataBreakpointAsync(services, owner, request, cancellationToken).ConfigureAwait(false),
            SetDataBreakpointsOperation request => await SetDataBreakpointsAsync(services, owner, request, context, cancellationToken).ConfigureAwait(false),
            GetDebugBreakpointsOperation request => Breakpoints(services, owner, request, context),
            GetBreakpointLocationsOperation request => await BreakpointLocationsAsync(services, owner, request, cancellationToken).ConfigureAwait(false),
            ContinueDebugOperation request => await ContinueAsync(services, owner, request, cancellationToken).ConfigureAwait(false),
            PauseDebugOperation request => await PauseAsync(services, owner, request, cancellationToken).ConfigureAwait(false),
            StepOverDebugOperation request => await StepOverAsync(services, owner, request, cancellationToken).ConfigureAwait(false),
            StepInDebugOperation request => await StepInAsync(services, owner, request, cancellationToken).ConfigureAwait(false),
            StepOutDebugOperation request => await StepOutAsync(services, owner, request, cancellationToken).ConfigureAwait(false),
            StepBackDebugOperation request => await StepBackAsync(services, owner, request, cancellationToken).ConfigureAwait(false),
            ReverseContinueDebugOperation request => await ReverseContinueAsync(services, owner, request, cancellationToken).ConfigureAwait(false),
            RestartFrameDebugOperation request => await RestartFrameAsync(services, owner, request, cancellationToken).ConfigureAwait(false),
            GotoDebugOperation request => await GotoAsync(services, owner, request, cancellationToken).ConfigureAwait(false),
            TerminateThreadsDebugOperation request => await TerminateThreadsAsync(services, owner, request, cancellationToken).ConfigureAwait(false),
            GetThreadsOperation request => await ThreadsAsync(services, owner, request, cancellationToken).ConfigureAwait(false),
            GetStackTraceOperation request => await StackAsync(services, owner, request, context, cancellationToken).ConfigureAwait(false),
            GetScopesOperation request => await ScopesAsync(services, owner, request, cancellationToken).ConfigureAwait(false),
            GetVariablesOperation request => await VariablesAsync(services, owner, request, cancellationToken).ConfigureAwait(false),
            EvaluateDebugOperation request => await EvaluateAsync(services, owner, request, permission, cancellationToken).ConfigureAwait(false),
            GetExceptionInfoOperation request => await ExceptionInfoAsync(services, owner, request, cancellationToken).ConfigureAwait(false),
            GetModulesOperation request => await ModulesAsync(services, owner, request, cancellationToken).ConfigureAwait(false),
            GetLoadedSourcesOperation request => await LoadedSourcesAsync(services, owner, request, cancellationToken).ConfigureAwait(false),
            GetSourceOperation request => await SourceAsync(services, owner, request, cancellationToken).ConfigureAwait(false),
            GetStepInTargetsOperation request => await StepInTargetsAsync(services, owner, request, cancellationToken).ConfigureAwait(false),
            GetGotoTargetsOperation request => await GotoTargetsAsync(services, owner, request, cancellationToken).ConfigureAwait(false),
            GetCompletionsOperation request => await CompletionsAsync(services, owner, request, cancellationToken).ConfigureAwait(false),
            ResolveDebugLocationOperation request => await ResolveLocationAsync(services, owner, request, cancellationToken).ConfigureAwait(false),
            SetDebugVariableOperation request => await SetVariableAsync(services, owner, request, permission, cancellationToken).ConfigureAwait(false),
            SetDebugExpressionOperation request => await SetExpressionAsync(services, owner, request, permission, cancellationToken).ConfigureAwait(false),
            ReadDebugMemoryOperation request => await ReadMemoryAsync(services, owner, request, cancellationToken).ConfigureAwait(false),
            WriteDebugMemoryOperation request => await WriteMemoryAsync(services, owner, request, permission, cancellationToken).ConfigureAwait(false),
            DisassembleDebugOperation request => await DisassembleAsync(services, owner, request, cancellationToken).ConfigureAwait(false),
            GetDebugOutputOperation request => Output(services, owner, request, context),
            PersistDebugOutputOperation request => await PersistOutputAsync(services, owner, request, context, cancellationToken).ConfigureAwait(false),
            CancelDebugProgressOperation request => await CancelProgressAsync(services, owner, request, cancellationToken).ConfigureAwait(false),
            _ => throw new ArgumentOutOfRangeException(nameof(operation))
        };
    }

    private string ListSessions(DebugRuntimeServices services, DebugTreeLookupScope owner, FunctionExecutionContext context)
    {
        var snapshots = services.Semantics.ListTreeSnapshots(owner);
        context.ResultMetadata.Set(CodingToolMetadataKeys.DebugSessionSnapshot, snapshots);
        return _formatter.Success("listSessions", Attr(("count", snapshots.Count)),
            snapshots.Select(item => $"{item.DebugTreeId} {item.Status} sessions={item.SessionCount}"));
    }

    private string Status(
        DebugRuntimeServices services,
        DebugTreeLookupScope owner,
        GetDebugStatusOperation request,
        FunctionExecutionContext context)
    {
        if (services.Manager.TryResolveTerminal(owner, request.DebugTreeId, out var terminal))
        {
            context.ResultMetadata.Set(
                CodingToolMetadataKeys.DebugTerminalRecord,
                DebugTerminalRecordMetadataProjection.Project(terminal));
            return _formatter.Success("getStatus", Attr(
                ("debugTreeId", request.DebugTreeId),
                ("debugSessionId", request.DebugSessionId),
                ("status", terminal.FinalStatus),
                ("retained", true),
                ("exitCode", terminal.ExitCode)));
        }
        return _formatter.Success("getStatus", Attr(
            ("debugTreeId", request.DebugTreeId),
            ("debugSessionId", request.DebugSessionId),
            ("status", services.Semantics.GetStatus(
                owner, request.DebugTreeId, request.DebugSessionId))));
    }

    private string Health(
        DebugRuntimeServices services,
        DebugTreeLookupScope owner,
        GetDebugHealthOperation request,
        FunctionExecutionContext context)
    {
        if (services.Manager.TryResolveTerminal(owner, request.DebugTreeId, out var terminal))
        {
            context.ResultMetadata.Set(
                CodingToolMetadataKeys.DebugTerminalRecord,
                DebugTerminalRecordMetadataProjection.Project(terminal));
            return _formatter.Success("getHealth", Attr(
                ("debugTreeId", request.DebugTreeId),
                ("retained", true),
                ("retainedOutputBytes", terminal.Output.RetainedBytes),
                ("droppedOutputRecords", terminal.Output.DroppedRecords),
                ("projectionFailures", terminal.Snapshot.ProjectionFailures)));
        }
        var health = services.Semantics.GetHealth(owner, request.DebugTreeId, request.DebugSessionId);
        return _formatter.Success("getHealth", Attr(
            ("debugTreeId", request.DebugTreeId),
            ("retainedOutputBytes", health.RetainedOutputBytes),
            ("droppedOutputRecords", health.DroppedOutputRecords),
            ("projectionFailures", health.ProjectionFollowUpFailures)));
    }

    private string Snapshot(
        DebugRuntimeServices services,
        DebugTreeLookupScope owner,
        SnapshotDebugOperation request,
        FunctionExecutionContext context)
    {
        if (request.MaximumOutputBytes is < 0 or > 16 * 1024)
            throw new ArgumentOutOfRangeException(nameof(request.MaximumOutputBytes));
        var snapshot = services.Semantics.GetSnapshot(owner, request.DebugTreeId);
        var breakpoints = services.Breakpoints.GetSnapshot(owner, request.DebugTreeId, request.DebugSessionId);
        var output = services.Semantics.GetOutput(owner, request.DebugTreeId, request.DebugSessionId);
        if (services.Manager.TryResolveTerminal(owner, request.DebugTreeId, out var terminal))
        {
            context.ResultMetadata.Set(
                CodingToolMetadataKeys.DebugSessionSnapshot, snapshot);
            context.ResultMetadata.Set(
                CodingToolMetadataKeys.DebugBreakpoints, breakpoints);
            context.ResultMetadata.Set(
                CodingToolMetadataKeys.DebugBreakpointState, breakpoints.Counts);
            context.ResultMetadata.Set(
                CodingToolMetadataKeys.DebugTerminalRecord,
                DebugTerminalRecordMetadataProjection.Project(terminal));
            return _formatter.Success("snapshot", Attr(
                ("debugTreeId", snapshot.DebugTreeId),
                ("debugSessionId", snapshot.ActiveDebugSessionId),
                ("status", terminal.FinalStatus),
                ("retained", true),
                ("exitCode", terminal.ExitCode),
                ("sessionCount", snapshot.SessionCount),
                ("childSessionCount", snapshot.ChildSessionCount),
                ("requestedBreakpoints", breakpoints.Counts.Requested),
                ("acknowledgedBreakpoints", breakpoints.Counts.Acknowledged),
                ("verifiedBreakpoints", breakpoints.Counts.Verified),
                ("breakpointVerification", "resolved_not_hit"),
                ("pendingBreakpoints", breakpoints.Counts.Pending),
                ("retainedOutputBytes", output.RetainedBytes)),
                BoundOutput(output, request.MaximumOutputBytes));
        }
        var tree = services.Manager.ResolveTree(owner, request.DebugTreeId);
        var session = tree.SelectSession(request.DebugSessionId);
        context.ResultMetadata.Set(CodingToolMetadataKeys.DebugSessionSnapshot, snapshot);
        context.ResultMetadata.Set(CodingToolMetadataKeys.DebugBreakpoints, breakpoints);
        context.ResultMetadata.Set(
            CodingToolMetadataKeys.DebugBreakpointState, breakpoints.Counts);
        var capabilities = session.Capabilities is null
            ? null
            : DebugCapabilityProjection.Project(session.Capabilities);
        if (capabilities is not null)
            context.ResultMetadata.Set(CodingToolMetadataKeys.DebugCapabilities, capabilities);
        var attributes = Attr(
            ("debugTreeId", snapshot.DebugTreeId),
            ("debugSessionId", snapshot.ActiveDebugSessionId),
            ("status", snapshot.Status),
            ("primaryStoppedThreadId", snapshot.Sessions
                .FirstOrDefault(item => item.DebugSessionId == snapshot.ActiveDebugSessionId)?
                .PrimaryStoppedThreadId),
            ("sessionCount", snapshot.SessionCount),
            ("childSessionCount", snapshot.ChildSessionCount),
            ("requestedBreakpoints", breakpoints.Counts.Requested),
            ("acknowledgedBreakpoints", breakpoints.Counts.Acknowledged),
            ("verifiedBreakpoints", breakpoints.Counts.Verified),
            ("breakpointVerification", "resolved_not_hit"),
            ("pendingBreakpoints", breakpoints.Counts.Pending),
            ("retainedOutputBytes", output.RetainedBytes),
            ("supportedOptionalActions", Join(capabilities?.SupportedOptionalActions)),
            ("unsupportedOptionalActions", Join(capabilities?.UnsupportedOptionalActions)),
            ("executionOptions", Join(capabilities?.ExecutionOptions)),
            ("exceptionFilterIds", Join(capabilities?.ExceptionFilters.Select(item => item.FilterId))));
        return _formatter.Success("snapshot", attributes,
            BoundOutput(output, request.MaximumOutputBytes));
    }

    private async Task<string> InspectStopAsync(
        DebugRuntimeServices services,
        DebugTreeLookupScope owner,
        InspectDebugStopOperation request,
        FunctionExecutionContext context,
        CancellationToken cancellationToken)
    {
        if (request.MaximumFrames is <= 0 or > 100 ||
            request.MaximumVariablesPerScope is <= 0 or > 200 ||
            request.MaximumOutputBytes is < 0 or > 16 * 1024)
            throw new ArgumentOutOfRangeException(nameof(request));
        var threads = await services.Semantics.ThreadsAsync(owner, request.DebugTreeId, request.DebugSessionId, cancellationToken).ConfigureAwait(false);
        var session = services.Manager.ResolveTree(owner, request.DebugTreeId)
            .SelectSession(request.DebugSessionId);
        var selected = request.ThreadId is { } requested
            ? threads.SingleOrDefault(item => item.ThreadId == requested && item.IsStopped)
            : session.State.PrimaryStoppedThreadId is { } primary
                ? threads.SingleOrDefault(item => item.ThreadId == primary && item.IsStopped)
                : threads.FirstOrDefault(item => item.IsStopped);
        if (selected is null)
            throw new DebugSemanticException(DebugSemanticFailureReason.InvalidSessionState, "No stopped thread is available.");
        var stack = await services.Semantics.StackTraceAsync(
            owner, request.DebugTreeId, request.DebugSessionId, selected.ThreadId,
            request.MaximumFrames, null, cancellationToken).ConfigureAwait(false);
        var scopes = new List<DebugSemanticScope>();
        var variables = new List<DebugSemanticVariables>();
        var details = new List<string>(stack.Frames.Select(frame =>
            $"{frame.Name} {frame.SourcePath ?? "<unknown>"}:{frame.Line}:{frame.Column} frameToken={frame.FrameToken}"));
        if (request.IncludeScopes && stack.Frames.FirstOrDefault() is { } topFrame)
        {
            var topScopes = await services.Semantics.ScopesAsync(
                owner, request.DebugTreeId, request.DebugSessionId, topFrame.FrameToken,
                cancellationToken).ConfigureAwait(false);
            scopes.AddRange(topScopes);
            foreach (var scope in topScopes)
            {
                details.Add($"scope {scope.Name} variablesToken={scope.VariablesToken}");
                if (!request.IncludeVariables) continue;
                var page = await services.Semantics.VariablesAsync(
                    owner, request.DebugTreeId, request.DebugSessionId, scope.VariablesToken,
                    filter: null, request.MaximumVariablesPerScope, continuationToken: null,
                    cancellationToken).ConfigureAwait(false);
                variables.Add(page);
                details.AddRange(page.Variables.Select(variable =>
                    $"{scope.Name}.{variable.Name}={variable.Value} type={variable.Type} variablesToken={variable.VariablesToken}"));
            }
        }
        var output = services.Semantics.GetOutput(owner, request.DebugTreeId, request.DebugSessionId);
        details.AddRange(BoundOutput(output, request.MaximumOutputBytes));
        var inspection = new DebugStopInspectionMetadata(selected, stack, scopes, variables, output);
        var capabilities = session.Capabilities is null
            ? null
            : DebugCapabilityProjection.Project(session.Capabilities);
        context.ResultMetadata.Set(CodingToolMetadataKeys.DebugStopSnapshot, inspection);
        context.ResultMetadata.Set(CodingToolMetadataKeys.DebugStackFrames, stack);
        if (capabilities is not null)
            context.ResultMetadata.Set(CodingToolMetadataKeys.DebugCapabilities, capabilities);
        return _formatter.Success("inspectStop", Attr(
            ("debugTreeId", request.DebugTreeId),
            ("threadId", selected.ThreadId),
            ("reason", selected.StopReason),
            ("frameCount", stack.Frames.Count),
            ("scopeCount", scopes.Count),
            ("variableCount", variables.Sum(page => page.Variables.Count)),
            ("continuationToken", stack.ContinuationToken),
            ("supportedOptionalActions", Join(capabilities?.SupportedOptionalActions)),
            ("unsupportedOptionalActions", Join(capabilities?.UnsupportedOptionalActions)),
            ("executionOptions", Join(capabilities?.ExecutionOptions)),
            ("exceptionFilterIds", Join(capabilities?.ExceptionFilters.Select(item => item.FilterId)))), details);
    }

    private async Task<string> DisconnectAsync(DebugRuntimeServices services, DebugTreeLookupScope owner, DisconnectDebugOperation request, CancellationToken cancellationToken)
    {
        await services.Semantics.DisconnectAsync(
            owner, request.DebugTreeId, request.DebugSessionId,
            request.Mode == DebugDisconnectMode.Detach ? false : request.Mode == DebugDisconnectMode.TerminateDebuggee,
            request.Mode == DebugDisconnectMode.SuspendDebuggee,
            cancellationToken).ConfigureAwait(false);
        return _formatter.Success("disconnect", Attr(("debugTreeId", request.DebugTreeId), ("mode", request.Mode)));
    }

    private async Task<string> TerminateAsync(
        DebugRuntimeServices services,
        DebugTreeLookupScope owner,
        TerminateDebugOperation request,
        FunctionExecutionContext context,
        CancellationToken cancellationToken)
    {
        if (services.Manager.TryResolveTerminal(
                owner,
                request.DebugTreeId,
                out var terminal))
        {
            context.ResultMetadata.Set(
                CodingToolMetadataKeys.DebugTerminalRecord,
                DebugTerminalRecordMetadataProjection.Project(terminal));
            return _formatter.Success("terminate", Attr(
                ("debugTreeId", request.DebugTreeId),
                ("target", request.Target),
                ("alreadyTerminated", true),
                ("terminalRecordRetained", true)));
        }
        var scope = request.Target switch
        {
            DebugTerminationTarget.Tree => DebugTerminationScope.Tree,
            DebugTerminationTarget.Session => DebugTerminationScope.Session,
            DebugTerminationTarget.Debuggee => DebugTerminationScope.Debuggee,
            _ => throw new ArgumentOutOfRangeException(nameof(request.Target))
        };
        var result = await services.Lifecycle.TerminateAsync(
            owner, request.DebugTreeId, request.DebugSessionId, scope,
            terminateDebuggee: true, cancellationToken).ConfigureAwait(false);
        return _formatter.Success("terminate", Attr(
            ("debugTreeId", request.DebugTreeId), ("target", request.Target),
            ("graceful", result.Graceful), ("treeDisposed", result.TreeDisposed)));
    }

    private async Task<string> RestartAsync(
        DebugRuntimeServices services,
        DebugTreeLookupScope owner,
        RestartDebugOperation request,
        DebugPermissionDecision permission,
        FunctionExecutionContext context,
        CancellationToken cancellationToken)
    {
        var tree = services.Manager.ResolveTree(owner, request.DebugTreeId);
        var session = tree.SelectSession(request.DebugSessionId);
        var semantic = tree.SemanticRestartOperation
            ?? throw new InvalidOperationException(
                "The debug tree has no semantic restart input.");
        var desired = tree.Breakpoints.Snapshot;
        var replacementOperation = semantic with
        {
            InitialConfiguration = new DebugInitialConfigurationInput(
                desired.Source.Select(item => new DebugSourceBreakpointInput(
                    item.Path,
                    checked((int)item.Line),
                    item.Column is null
                        ? null
                        : checked((int)item.Column.Value),
                    item.Condition,
                    item.HitCondition,
                    item.LogMessage)).ToArray(),
                desired.Function.Select(item => new DebugFunctionBreakpointInput(
                    item.Name,
                    item.Condition,
                    item.HitCondition)).ToArray(),
                desired.Exception.Select(item => new DebugExceptionBreakpointInput(
                    item.FilterId,
                    item.Condition)).ToArray(),
                semantic.InitialConfiguration?.BreakpointPolicy ??
                    DebugInitialBreakpointPolicy.AllowPending)
        };
        await services.Semantics.DisconnectAsync(
            owner,
            request.DebugTreeId,
            session.SessionId,
            terminateDebuggee:
                session.AdapterStartMethod == DebugAdapterStartMethod.Launch,
            suspendDebuggee: false,
            cancellationToken).ConfigureAwait(false);
        return await RequireStarts().RestartAsync(
            replacementOperation,
            permission,
            context,
            cancellationToken).ConfigureAwait(false);
    }

    private async Task<string> SetSourceBreakpointsAsync(DebugRuntimeServices services, DebugTreeLookupScope owner, SetSourceBreakpointsOperation request, FunctionExecutionContext context, CancellationToken cancellationToken)
    {
        await services.Breakpoints.SetSourceAsync(owner, request.DebugTreeId, request.DebugSessionId,
            request.Breakpoints.Select(item => new DebugSourceBreakpoint(item.Path, item.Line, item.Column, item.Condition, item.HitCondition, item.LogMessage)).ToArray(),
            cancellationToken).ConfigureAwait(false);
        return BreakpointMutationResult(services, owner, request.DebugTreeId, request.DebugSessionId, "setSourceBreakpoints", context);
    }

    private async Task<string> SetFunctionBreakpointsAsync(DebugRuntimeServices services, DebugTreeLookupScope owner, SetFunctionBreakpointsOperation request, FunctionExecutionContext context, CancellationToken cancellationToken)
    {
        await services.Breakpoints.SetFunctionAsync(owner, request.DebugTreeId, request.DebugSessionId,
            request.Breakpoints.Select(item => new DebugFunctionBreakpoint(item.Name, item.Condition, item.HitCondition)).ToArray(),
            cancellationToken).ConfigureAwait(false);
        return BreakpointMutationResult(services, owner, request.DebugTreeId, request.DebugSessionId, "setFunctionBreakpoints", context);
    }

    private async Task<string> SetExceptionBreakpointsAsync(DebugRuntimeServices services, DebugTreeLookupScope owner, SetExceptionBreakpointsOperation request, FunctionExecutionContext context, CancellationToken cancellationToken)
    {
        await services.Breakpoints.SetExceptionAsync(owner, request.DebugTreeId, request.DebugSessionId,
            request.Breakpoints.Select(item => new DebugExceptionFilter(item.FilterId, item.Condition)).ToArray(),
            cancellationToken).ConfigureAwait(false);
        return BreakpointMutationResult(services, owner, request.DebugTreeId, request.DebugSessionId, "setExceptionBreakpoints", context);
    }

    private async Task<string> SetInstructionBreakpointsAsync(DebugRuntimeServices services, DebugTreeLookupScope owner, SetInstructionBreakpointsOperation request, FunctionExecutionContext context, CancellationToken cancellationToken)
    {
        await services.Breakpoints.SetInstructionTokensAsync(owner, request.DebugTreeId, request.DebugSessionId,
            request.Breakpoints.Select(item => (item.InstructionReferenceToken, item.Offset, item.Condition, item.HitCondition)).ToArray(),
            cancellationToken).ConfigureAwait(false);
        return BreakpointMutationResult(services, owner, request.DebugTreeId, request.DebugSessionId, "setInstructionBreakpoints", context);
    }

    private async Task<string> DiscoverDataBreakpointAsync(DebugRuntimeServices services, DebugTreeLookupScope owner, DiscoverDataBreakpointOperation request, CancellationToken cancellationToken)
    {
        if (request.VariablesToken is not null && request.FrameToken is not null)
            throw new ArgumentException("Only one data-breakpoint discovery owner may be supplied.");
        var result = await services.Semantics.DataBreakpointInfoAsync(
            owner, request.DebugTreeId, request.DebugSessionId, request.Name,
            request.VariablesToken, request.FrameToken, request.Bytes, request.AsAddress,
            request.Mode, cancellationToken).ConfigureAwait(false);
        return _formatter.Success("discoverDataBreakpoint", Attr(
            ("debugTreeId", request.DebugTreeId), ("dataBreakpointToken", result.DiscoveryToken),
            ("description", result.Description), ("canPersist", result.CanPersist)),
            result.AccessTypes);
    }

    private async Task<string> SetDataBreakpointsAsync(DebugRuntimeServices services, DebugTreeLookupScope owner, SetDataBreakpointsOperation request, FunctionExecutionContext context, CancellationToken cancellationToken)
    {
        await services.Breakpoints.SetDataTokensAsync(owner, request.DebugTreeId, request.DebugSessionId,
            request.Breakpoints.Select(item => (
                item.DataBreakpointToken,
                item.AccessType switch
                {
                    DebugDataBreakpointAccessType.Read => "read",
                    DebugDataBreakpointAccessType.Write => "write",
                    DebugDataBreakpointAccessType.ReadWrite => "readWrite",
                    null => null,
                    _ => throw new ArgumentOutOfRangeException()
                },
                item.Condition,
                item.HitCondition)).ToArray(),
            cancellationToken).ConfigureAwait(false);
        return BreakpointMutationResult(services, owner, request.DebugTreeId, request.DebugSessionId, "setDataBreakpoints", context);
    }

    private string Breakpoints(DebugRuntimeServices services, DebugTreeLookupScope owner, GetDebugBreakpointsOperation request, FunctionExecutionContext context)
    {
        var snapshot = services.Breakpoints.GetSnapshot(owner, request.DebugTreeId, request.DebugSessionId);
        context.ResultMetadata.Set(CodingToolMetadataKeys.DebugBreakpoints, snapshot);
        var attributes = new List<KeyValuePair<string, object?>>
        {
            KeyValuePair.Create<string, object?>("debugTreeId", request.DebugTreeId),
            KeyValuePair.Create<string, object?>("retained", !snapshot.DetailsRetained),
            KeyValuePair.Create<string, object?>("detailsRetained", snapshot.DetailsRetained),
            KeyValuePair.Create<string, object?>("requestedCount", snapshot.Counts.Requested),
            KeyValuePair.Create<string, object?>("acknowledgedCount", snapshot.Counts.Acknowledged),
            KeyValuePair.Create<string, object?>("verifiedCount", snapshot.Counts.Verified),
            KeyValuePair.Create<string, object?>("verificationMeaning", "resolved_not_hit"),
            KeyValuePair.Create<string, object?>("pendingCount", snapshot.Counts.Pending)
        };
        if (snapshot.DetailsRetained)
        {
            attributes.AddRange(Attr(
                ("debugSessionId", snapshot.DebugSessionId),
                ("sourceCount", snapshot.Desired.Source.Length),
                ("functionCount", snapshot.Desired.Function.Length),
                ("exceptionCount", snapshot.Desired.Exception.Length),
                ("instructionCount", snapshot.Desired.Instruction.Length),
                ("dataCount", snapshot.Desired.Data.Length)));
        }
        return _formatter.Success("getBreakpoints", attributes);
    }

    private string BreakpointMutationResult(DebugRuntimeServices services, DebugTreeLookupScope owner, string treeId, string? sessionId, string action, FunctionExecutionContext context)
    {
        var snapshot = services.Breakpoints.GetSnapshot(owner, treeId, sessionId);
        context.ResultMetadata.Set(CodingToolMetadataKeys.DebugBreakpoints, snapshot);
        return _formatter.Success(action, Attr(
            ("debugTreeId", treeId), ("debugSessionId", snapshot.DebugSessionId),
            ("requestedCount", snapshot.Counts.Requested),
            ("acknowledgedCount", snapshot.Counts.Acknowledged),
            ("verifiedCount", snapshot.Counts.Verified),
            ("verificationMeaning", "resolved_not_hit"),
            ("pendingCount", snapshot.Counts.Pending)));
    }

    private async Task<string> BreakpointLocationsAsync(DebugRuntimeServices services, DebugTreeLookupScope owner, GetBreakpointLocationsOperation request, CancellationToken cancellationToken)
    {
        var items = await services.Semantics.BreakpointLocationsAsync(
            owner, request.DebugTreeId, request.DebugSessionId, request.SourceToken,
            request.StartLine, request.StartColumn, request.EndLine, request.EndColumn,
            cancellationToken).ConfigureAwait(false);
        return _formatter.Success("getBreakpointLocations", Attr(("count", items.Count)),
            items.Select(item => $"{item.Line}:{item.Column}"));
    }

    private async Task<string> ContinueAsync(DebugRuntimeServices services, DebugTreeLookupScope owner, ContinueDebugOperation request, CancellationToken cancellationToken)
        => OperationResult("continue", await services.Semantics.ContinueAsync(
            owner, request.DebugTreeId, request.DebugSessionId,
            ResolvePrimaryStoppedThreadId(services, owner, request.DebugTreeId, request.DebugSessionId, request.ThreadId),
            request.SingleThread, Timeout(request.WaitTimeoutMilliseconds), cancellationToken).ConfigureAwait(false));

    private async Task<string> PauseAsync(DebugRuntimeServices services, DebugTreeLookupScope owner, PauseDebugOperation request, CancellationToken cancellationToken)
        => OperationResult("pause", await services.Semantics.PauseAsync(owner, request.DebugTreeId, request.DebugSessionId, request.ThreadId, Timeout(request.WaitTimeoutMilliseconds), cancellationToken).ConfigureAwait(false));

    private async Task<string> StepOverAsync(DebugRuntimeServices services, DebugTreeLookupScope owner, StepOverDebugOperation request, CancellationToken cancellationToken)
        => OperationResult("stepOver", await services.Semantics.NextAsync(
            owner, request.DebugTreeId, request.DebugSessionId,
            ResolvePrimaryStoppedThreadId(services, owner, request.DebugTreeId, request.DebugSessionId, request.ThreadId),
            request.SingleThread, Granularity(request.Granularity), Timeout(request.WaitTimeoutMilliseconds), cancellationToken).ConfigureAwait(false));

    private async Task<string> StepInAsync(DebugRuntimeServices services, DebugTreeLookupScope owner, StepInDebugOperation request, CancellationToken cancellationToken)
        => OperationResult("stepIn", await services.Semantics.StepInAsync(
            owner, request.DebugTreeId, request.DebugSessionId,
            ResolvePrimaryStoppedThreadId(services, owner, request.DebugTreeId, request.DebugSessionId, request.ThreadId),
            request.SingleThread, request.TargetToken, Granularity(request.Granularity), Timeout(request.WaitTimeoutMilliseconds), cancellationToken).ConfigureAwait(false));

    private async Task<string> StepOutAsync(DebugRuntimeServices services, DebugTreeLookupScope owner, StepOutDebugOperation request, CancellationToken cancellationToken)
        => OperationResult("stepOut", await services.Semantics.StepOutAsync(
            owner, request.DebugTreeId, request.DebugSessionId,
            ResolvePrimaryStoppedThreadId(services, owner, request.DebugTreeId, request.DebugSessionId, request.ThreadId),
            request.SingleThread, Granularity(request.Granularity), Timeout(request.WaitTimeoutMilliseconds), cancellationToken).ConfigureAwait(false));

    private async Task<string> StepBackAsync(DebugRuntimeServices services, DebugTreeLookupScope owner, StepBackDebugOperation request, CancellationToken cancellationToken)
        => OperationResult("stepBack", await services.Semantics.StepBackAsync(
            owner, request.DebugTreeId, request.DebugSessionId,
            ResolvePrimaryStoppedThreadId(services, owner, request.DebugTreeId, request.DebugSessionId, request.ThreadId),
            request.SingleThread, Granularity(request.Granularity), Timeout(request.WaitTimeoutMilliseconds), cancellationToken).ConfigureAwait(false));

    private async Task<string> ReverseContinueAsync(DebugRuntimeServices services, DebugTreeLookupScope owner, ReverseContinueDebugOperation request, CancellationToken cancellationToken)
        => OperationResult("reverseContinue", await services.Semantics.ReverseContinueAsync(
            owner, request.DebugTreeId, request.DebugSessionId,
            ResolvePrimaryStoppedThreadId(services, owner, request.DebugTreeId, request.DebugSessionId, request.ThreadId),
            request.SingleThread, Timeout(request.WaitTimeoutMilliseconds), cancellationToken).ConfigureAwait(false));

    private async Task<string> RestartFrameAsync(DebugRuntimeServices services, DebugTreeLookupScope owner, RestartFrameDebugOperation request, CancellationToken cancellationToken)
        => OperationResult("restartFrame", await services.Semantics.RestartFrameAsync(owner, request.DebugTreeId, request.DebugSessionId, request.FrameToken, Timeout(request.WaitTimeoutMilliseconds), cancellationToken).ConfigureAwait(false));

    private async Task<string> GotoAsync(DebugRuntimeServices services, DebugTreeLookupScope owner, GotoDebugOperation request, CancellationToken cancellationToken)
        => OperationResult("goto", await services.Semantics.GotoAsync(owner, request.DebugTreeId, request.DebugSessionId, request.ThreadId, request.TargetToken, Timeout(request.WaitTimeoutMilliseconds), cancellationToken).ConfigureAwait(false));

    private async Task<string> TerminateThreadsAsync(DebugRuntimeServices services, DebugTreeLookupScope owner, TerminateThreadsDebugOperation request, CancellationToken cancellationToken)
    {
        await services.Semantics.TerminateThreadsAsync(owner, request.DebugTreeId, request.DebugSessionId, request.ThreadIds, cancellationToken).ConfigureAwait(false);
        return _formatter.Success("terminateThreads", Attr(("count", request.ThreadIds.Count)));
    }

    private async Task<string> ThreadsAsync(DebugRuntimeServices services, DebugTreeLookupScope owner, GetThreadsOperation request, CancellationToken cancellationToken)
    {
        var items = await services.Semantics.ThreadsAsync(owner, request.DebugTreeId, request.DebugSessionId, cancellationToken).ConfigureAwait(false);
        return ProjectThreads(items);
    }

    internal string ProjectThreads(IReadOnlyList<DebugSemanticThread> items)
    {
        ArgumentNullException.ThrowIfNull(items);
        return _formatter.Success("getThreads", Attr(
                ("count", items.Count),
                ("primaryStoppedThreadId", items.SingleOrDefault(item => item.IsPrimaryStoppedThread)?.ThreadId)),
            items.Select(item =>
                $"{item.ThreadId} {item.Name} stopped={item.IsStopped} primary={item.IsPrimaryStoppedThread} reason={item.StopReason}"));
    }

    private async Task<string> StackAsync(DebugRuntimeServices services, DebugTreeLookupScope owner, GetStackTraceOperation request, FunctionExecutionContext context, CancellationToken cancellationToken)
    {
        var threadId = ResolvePrimaryStoppedThreadId(
            services, owner, request.DebugTreeId, request.DebugSessionId, request.ThreadId);
        var result = await services.Semantics.StackTraceAsync(owner, request.DebugTreeId, request.DebugSessionId, threadId, request.Levels, request.ContinuationToken, cancellationToken).ConfigureAwait(false);
        context.ResultMetadata.Set(CodingToolMetadataKeys.DebugStackFrames, result);
        return _formatter.Success("getStackTrace", Attr(
            ("count", result.Frames.Count), ("totalFrames", result.TotalFrames),
            ("continuationToken", result.ContinuationToken)),
            result.Frames.Select(item => $"{item.Name} {item.SourcePath}:{item.Line}:{item.Column} frameToken={item.FrameToken}"));
    }

    private static int ResolvePrimaryStoppedThreadId(
        DebugRuntimeServices services,
        DebugTreeLookupScope owner,
        string treeId,
        string? sessionId,
        int? requestedThreadId)
    {
        if (requestedThreadId is > 0)
            return requestedThreadId.Value;
        if (requestedThreadId is <= 0)
            throw new DebugSemanticException(
                DebugSemanticFailureReason.InvalidArguments,
                "A debugger thread ID must be positive when supplied.");
        return services.Manager.ResolveTree(owner, treeId)
            .SelectSession(sessionId)
            .State.PrimaryStoppedThreadId
            ?? throw new DebugSemanticException(
                DebugSemanticFailureReason.InvalidSessionState,
                "The debugger operation requires an adapter-designated stopped thread.");
    }

    private async Task<string> ScopesAsync(DebugRuntimeServices services, DebugTreeLookupScope owner, GetScopesOperation request, CancellationToken cancellationToken)
    {
        var result = await services.Semantics.ScopesAsync(owner, request.DebugTreeId, request.DebugSessionId, request.FrameToken, cancellationToken).ConfigureAwait(false);
        return _formatter.Success("getScopes", Attr(("count", result.Count)),
            result.Select(item => $"{item.Name} variablesToken={item.VariablesToken} expensive={item.Expensive}"));
    }

    private async Task<string> VariablesAsync(DebugRuntimeServices services, DebugTreeLookupScope owner, GetVariablesOperation request, CancellationToken cancellationToken)
    {
        var filter = request.Filter switch
        {
            DebugVariableFilter.Indexed => "indexed",
            DebugVariableFilter.Named => "named",
            null => null,
            _ => throw new ArgumentOutOfRangeException()
        };
        var result = await services.Semantics.VariablesAsync(owner, request.DebugTreeId, request.DebugSessionId, request.VariablesToken, filter, request.Count, request.ContinuationToken, cancellationToken).ConfigureAwait(false);
        return _formatter.Success("getVariables", Attr(("count", result.Variables.Count), ("continuationToken", result.ContinuationToken)),
            result.Variables.Select(item => $"{item.Name}={item.Value} type={item.Type} variablesToken={item.VariablesToken} memoryToken={item.MemoryReferenceToken}"));
    }

    private async Task<string> EvaluateAsync(DebugRuntimeServices services, DebugTreeLookupScope owner, EvaluateDebugOperation request, DebugPermissionDecision permission, CancellationToken cancellationToken)
    {
        var authorization = request.Context == DebugEvaluationContext.Repl
            ? _authorization.CreatePrivileged(permission, services, owner, request.DebugTreeId, request.DebugSessionId, DebugPrivilegedOperation.PrivilegedEvaluate)
            : null;
        var result = await services.Semantics.EvaluateAsync(owner, request.DebugTreeId, request.DebugSessionId, request.Expression, request.FrameToken, EvaluationContext(request.Context), authorization, cancellationToken).ConfigureAwait(false);
        return _formatter.Success("evaluate", Attr(
            ("result", result.Result), ("type", result.Type), ("variablesToken", result.VariablesToken),
            ("memoryToken", result.MemoryReferenceToken), ("truncated", result.Truncated)));
    }

    private async Task<string> ExceptionInfoAsync(DebugRuntimeServices services, DebugTreeLookupScope owner, GetExceptionInfoOperation request, CancellationToken cancellationToken)
    {
        var result = await services.Semantics.ExceptionInfoAsync(owner, request.DebugTreeId, request.DebugSessionId, request.ThreadId, cancellationToken).ConfigureAwait(false);
        return _formatter.Success("getExceptionInfo", Attr(
            ("exceptionId", result.ExceptionId), ("description", result.Description),
            ("breakMode", result.BreakMode), ("truncated", result.Truncated)));
    }

    private async Task<string> ModulesAsync(DebugRuntimeServices services, DebugTreeLookupScope owner, GetModulesOperation request, CancellationToken cancellationToken)
    {
        var result = await services.Semantics.ModulesAsync(owner, request.DebugTreeId, request.DebugSessionId, request.Count, request.ContinuationToken, cancellationToken).ConfigureAwait(false);
        return _formatter.Success("getModules", Attr(
            ("count", result.Modules.Count), ("totalModules", result.TotalModules),
            ("continuationToken", result.ContinuationToken)),
            result.Modules.Select(item => $"{item.Name} {item.Path} moduleToken={item.ModuleToken}"));
    }

    private async Task<string> LoadedSourcesAsync(DebugRuntimeServices services, DebugTreeLookupScope owner, GetLoadedSourcesOperation request, CancellationToken cancellationToken)
    {
        var result = await services.Semantics.LoadedSourcesAsync(owner, request.DebugTreeId, request.DebugSessionId, request.Count, request.ContinuationToken, cancellationToken).ConfigureAwait(false);
        return _formatter.Success("getLoadedSources", Attr(
            ("count", result.Sources.Count), ("continuationToken", result.ContinuationToken),
            ("truncated", result.Truncated)),
            result.Sources.Select(item => $"{item.Name} {item.Path} sourceToken={item.SourceToken}"));
    }

    private async Task<string> SourceAsync(DebugRuntimeServices services, DebugTreeLookupScope owner, GetSourceOperation request, CancellationToken cancellationToken)
    {
        var result = await services.Semantics.SourceAsync(owner, request.DebugTreeId, request.DebugSessionId, request.SourceToken, cancellationToken).ConfigureAwait(false);
        return _formatter.Success("getSource", Attr(
            ("mimeType", result.MimeType), ("utf8Bytes", result.Utf8Bytes),
            ("truncated", result.Truncated)), [result.InlineContent]);
    }

    private async Task<string> StepInTargetsAsync(DebugRuntimeServices services, DebugTreeLookupScope owner, GetStepInTargetsOperation request, CancellationToken cancellationToken)
    {
        var result = await services.Semantics.StepInTargetsAsync(owner, request.DebugTreeId, request.DebugSessionId, request.FrameToken, cancellationToken).ConfigureAwait(false);
        return _formatter.Success("getStepInTargets", Attr(("count", result.Count)),
            result.Select(item => $"{item.Label} targetToken={item.TargetToken}"));
    }

    private async Task<string> GotoTargetsAsync(DebugRuntimeServices services, DebugTreeLookupScope owner, GetGotoTargetsOperation request, CancellationToken cancellationToken)
    {
        var result = await services.Semantics.GotoTargetsAsync(owner, request.DebugTreeId, request.DebugSessionId, request.ThreadId, request.SourceToken, request.Line, request.Column, cancellationToken).ConfigureAwait(false);
        return _formatter.Success("getGotoTargets", Attr(("count", result.Count)),
            result.Select(item => $"{item.Label} {item.Location.Line}:{item.Location.Column} targetToken={item.TargetToken}"));
    }

    private async Task<string> CompletionsAsync(DebugRuntimeServices services, DebugTreeLookupScope owner, GetCompletionsOperation request, CancellationToken cancellationToken)
    {
        var result = await services.Semantics.CompletionsAsync(owner, request.DebugTreeId, request.DebugSessionId, request.Text, request.Column, request.Line, request.FrameToken, request.Count, request.ContinuationToken, cancellationToken).ConfigureAwait(false);
        return _formatter.Success("getCompletions", Attr(
            ("count", result.Items.Count), ("continuationToken", result.ContinuationToken),
            ("truncated", result.Truncated)),
            result.Items.Select(item => $"{item.Label} {item.Detail}"));
    }

    private async Task<string> ResolveLocationAsync(DebugRuntimeServices services, DebugTreeLookupScope owner, ResolveDebugLocationOperation request, CancellationToken cancellationToken)
    {
        var result = await services.Semantics.ResolveLocationAsync(owner, request.DebugTreeId, request.DebugSessionId, request.LocationToken, cancellationToken).ConfigureAwait(false);
        return _formatter.Success("resolveLocation", Attr(
            ("sourceToken", result.Source.SourceToken), ("path", result.Source.Path),
            ("line", result.Line), ("column", result.Column)));
    }

    private async Task<string> SetVariableAsync(DebugRuntimeServices services, DebugTreeLookupScope owner, SetDebugVariableOperation request, DebugPermissionDecision permission, CancellationToken cancellationToken)
    {
        var authorization = _authorization.CreatePrivileged(permission, services, owner, request.DebugTreeId, request.DebugSessionId, DebugPrivilegedOperation.SetVariable);
        var result = await services.Semantics.SetVariableAsync(owner, request.DebugTreeId, request.DebugSessionId, request.VariablesToken, request.Name, request.Value, authorization, cancellationToken).ConfigureAwait(false);
        return _formatter.Success("setVariable", Attr(("value", result.Value), ("type", result.Type), ("variablesToken", result.VariablesToken)));
    }

    private async Task<string> SetExpressionAsync(DebugRuntimeServices services, DebugTreeLookupScope owner, SetDebugExpressionOperation request, DebugPermissionDecision permission, CancellationToken cancellationToken)
    {
        var authorization = _authorization.CreatePrivileged(permission, services, owner, request.DebugTreeId, request.DebugSessionId, DebugPrivilegedOperation.SetExpression);
        var result = await services.Semantics.SetExpressionAsync(owner, request.DebugTreeId, request.DebugSessionId, request.Expression, request.Value, request.FrameToken, authorization, cancellationToken).ConfigureAwait(false);
        return _formatter.Success("setExpression", Attr(("value", result.Value), ("type", result.Type), ("variablesToken", result.VariablesToken)));
    }

    private async Task<string> ReadMemoryAsync(DebugRuntimeServices services, DebugTreeLookupScope owner, ReadDebugMemoryOperation request, CancellationToken cancellationToken)
    {
        var result = await services.Semantics.ReadMemoryAsync(owner, request.DebugTreeId, request.DebugSessionId, request.MemoryToken, request.Offset, request.Count, cancellationToken).ConfigureAwait(false);
        return _formatter.Success("readMemory", Attr(
            ("address", result.Address), ("base64Data", Convert.ToBase64String(result.Bytes)),
            ("unreadableBytes", result.UnreadableBytes), ("partial", result.Partial),
            ("memoryRangeToken", result.MemoryRangeToken)));
    }

    private async Task<string> WriteMemoryAsync(DebugRuntimeServices services, DebugTreeLookupScope owner, WriteDebugMemoryOperation request, DebugPermissionDecision permission, CancellationToken cancellationToken)
    {
        byte[] bytes;
        try { bytes = Convert.FromBase64String(request.Base64Data); }
        catch (FormatException) { throw new ArgumentException("Memory data is not valid base64."); }
        var authorization = _authorization.CreatePrivileged(permission, services, owner, request.DebugTreeId, request.DebugSessionId, DebugPrivilegedOperation.WriteMemory);
        var result = await services.Semantics.WriteMemoryAsync(owner, request.DebugTreeId, request.DebugSessionId, request.MemoryToken, request.Offset, bytes, request.AllowPartial, authorization, cancellationToken).ConfigureAwait(false);
        return _formatter.Success("writeMemory", Attr(
            ("offset", result.Offset), ("bytesWritten", result.BytesWritten), ("partial", result.Partial)));
    }

    private async Task<string> DisassembleAsync(DebugRuntimeServices services, DebugTreeLookupScope owner, DisassembleDebugOperation request, CancellationToken cancellationToken)
    {
        var result = await services.Semantics.DisassembleAsync(owner, request.DebugTreeId, request.DebugSessionId, request.MemoryToken, request.ByteOffset, request.InstructionOffset, request.InstructionCount, request.ResolveSymbols, request.ContinuationToken, cancellationToken).ConfigureAwait(false);
        return _formatter.Success("disassemble", Attr(
            ("count", result.Instructions.Count), ("continuationToken", result.ContinuationToken)),
            result.Instructions.Select(item => $"{item.Address} {item.Instruction} instructionToken={item.InstructionReferenceToken}"));
    }

    private string Output(
        DebugRuntimeServices services,
        DebugTreeLookupScope owner,
        GetDebugOutputOperation request,
        FunctionExecutionContext context)
    {
        if (request.MaximumRecords is <= 0 or > 1000 || request.MaximumBytes is <= 0 or > 64 * 1024)
            throw new ArgumentOutOfRangeException(nameof(request));
        var snapshot = services.Semantics.GetOutput(owner, request.DebugTreeId, request.DebugSessionId);
        if (services.Manager.TryResolveTerminal(owner, request.DebugTreeId, out var terminal))
            context.ResultMetadata.Set(
                CodingToolMetadataKeys.DebugTerminalRecord,
                DebugTerminalRecordMetadataProjection.Project(terminal));
        var categories = request.Categories?.Select(OutputCategory).ToHashSet();
        var bytes = 0;
        var records = snapshot.Records
            .Where(item => request.AfterSequence is null || item.Sequence > request.AfterSequence)
            .Where(item => categories is null || categories.Contains(item.Category))
            .Take(request.MaximumRecords)
            .TakeWhile(item =>
            {
                if (bytes + item.Utf8Bytes > request.MaximumBytes) return false;
                bytes += item.Utf8Bytes;
                return true;
            })
            .ToArray();
        return _formatter.Success("getOutput", Attr(
            ("count", records.Length), ("oldestSequence", snapshot.OldestSequence),
            ("newestSequence", snapshot.NewestSequence), ("droppedRecords", snapshot.DroppedRecords),
            ("droppedBytes", snapshot.DroppedBytes)),
            records.Select(item => $"{item.Sequence} {item.Category}: {item.Text}"));
    }

    private async Task<string> PersistOutputAsync(DebugRuntimeServices services, DebugTreeLookupScope owner, PersistDebugOutputOperation request, FunctionExecutionContext context, CancellationToken cancellationToken)
    {
        var result = await services.Semantics.PersistOutputAsync(owner, request.DebugTreeId, request.DebugSessionId, includeTelemetry: false, cancellationToken, request.FromSequence, request.ToSequence).ConfigureAwait(false);
        context.ResultMetadata.Set(CodingToolMetadataKeys.DebugOutputReference, result);
        return _formatter.Success("persistOutput", Attr(("status", result.Status), ("contentId", result.Address?.ContentId)));
    }

    private async Task<string> CancelProgressAsync(DebugRuntimeServices services, DebugTreeLookupScope owner, CancelDebugProgressOperation request, CancellationToken cancellationToken)
    {
        var accepted = await services.Semantics.CancelProgressAsync(owner, request.DebugTreeId, request.DebugSessionId, request.ProgressId, cancellationToken).ConfigureAwait(false);
        return _formatter.Success("cancelProgress", Attr(("accepted", accepted)));
    }

    private string OperationResult(string action, DebugOperationResult result)
    {
        var invalidatesTokens = action is "continue" or "stepOver" or "stepIn" or "stepOut" or
            "stepBack" or "reverseContinue" or "restartFrame" or "goto";
        return _formatter.Success(action, Attr(
            ("debugTreeId", result.DebugTreeId), ("debugSessionId", result.DebugSessionId),
            ("threadId", result.ThreadId), ("status", result.EndedStatus?.ToString() ??
                (result.IsStopped ? "stopped" : "running")),
            ("timedOutWaitingForStop", result.TimedOutWaitingForStop),
            ("stopReason", result.Thread?.StopReason),
            ("priorSuspensionTokensInvalidated", invalidatesTokens),
            ("nextAction", invalidatesTokens && result.IsStopped ? "inspectStop" :
                invalidatesTokens ? "snapshot" : null)),
            invalidatesTokens
                ? ["Prior suspension-bound tokens are expired; inspect the current stop before using frame, scope, variable, or location tokens."]
                : null);
    }

    private DebugExecutionPlanningService RequireStarts()
        => _starts ?? throw new InvalidOperationException("Debug start planning is not configured.");

    private string Failure(
        FunctionExecutionContext context,
        string action,
        DebugOperation operation,
        string kind,
        string message,
        IEnumerable<KeyValuePair<string, object?>>? attributes = null,
        IEnumerable<string>? items = null)
    {
        context.ResultMetadata.Set(
            CodingToolMetadataKeys.DebugOperation,
            new DebugOperationMetadata(action, TreeId(operation), SessionId(operation), false, kind));
        return _formatter.Failure(action, kind, message, attributes, items);
    }

    private static TimeSpan Timeout(int milliseconds)
    {
        if (milliseconds is <= 0 or > 30_000)
            throw new ArgumentOutOfRangeException(nameof(milliseconds));
        return TimeSpan.FromMilliseconds(milliseconds);
    }

    private static DebugSteppingGranularity? Granularity(DebugStepGranularity? value) => value switch
    {
        null => null,
        DebugStepGranularity.Statement => DebugSteppingGranularity.Statement,
        DebugStepGranularity.Line => DebugSteppingGranularity.Line,
        DebugStepGranularity.Instruction => DebugSteppingGranularity.Instruction,
        _ => throw new ArgumentOutOfRangeException(nameof(value))
    };

    private static string EvaluationContext(DebugEvaluationContext value) => value switch
    {
        DebugEvaluationContext.Repl => "repl",
        DebugEvaluationContext.Watch => "watch",
        DebugEvaluationContext.Hover => "hover",
        DebugEvaluationContext.Clipboard => "clipboard",
        DebugEvaluationContext.Variables => "variables",
        _ => throw new ArgumentOutOfRangeException(nameof(value))
    };

    private static DebugOutputCategory OutputCategory(DebugOutputFilter value) => value switch
    {
        DebugOutputFilter.Console => DebugOutputCategory.Console,
        DebugOutputFilter.Stdout => DebugOutputCategory.StandardOutput,
        DebugOutputFilter.Stderr => DebugOutputCategory.StandardError,
        DebugOutputFilter.Important => DebugOutputCategory.Important,
        _ => throw new ArgumentOutOfRangeException(nameof(value))
    };

    private static IReadOnlyList<KeyValuePair<string, object?>> Attr(params (string Key, object? Value)[] values)
        => values.Select(value => KeyValuePair.Create(value.Key, value.Value)).ToArray();

    private static string? Join(IEnumerable<string>? values)
    {
        if (values is null)
            return null;
        var materialized = values.Take(64).ToArray();
        return materialized.Length == 0 ? "none" : string.Join(',', materialized);
    }

    private static IReadOnlyList<string> BoundOutput(DebugOutputSnapshot snapshot, int maximumBytes)
    {
        if (maximumBytes <= 0) return [];
        var selected = new List<string>();
        var bytes = 0;
        foreach (var record in snapshot.Records.Reverse())
        {
            if (bytes + record.Utf8Bytes > maximumBytes) break;
            selected.Add($"{record.Sequence} {record.Category}: {record.Text}");
            bytes += record.Utf8Bytes;
        }
        selected.Reverse();
        return selected;
    }

    private static string? TreeId(DebugOperation operation) => operation switch
    {
        DebugTreeOperation tree => tree.DebugTreeId,
        _ => null
    };

    private static string? SessionId(DebugOperation operation) => operation switch
    {
        DebugTreeOperation tree => tree.DebugSessionId,
        _ => null
    };

    internal static string Action(DebugOperation operation) => operation switch
    {
        LaunchDebugOperation => "launch",
        AttachDebugOperation => "attach",
        ListDebugSessionsOperation => "listSessions",
        GetDebugStatusOperation => "getStatus",
        GetDebugHealthOperation => "getHealth",
        SnapshotDebugOperation => "snapshot",
        InspectDebugStopOperation => "inspectStop",
        DisconnectDebugOperation => "disconnect",
        TerminateDebugOperation => "terminate",
        RestartDebugOperation => "restart",
        SetSourceBreakpointsOperation => "setSourceBreakpoints",
        SetFunctionBreakpointsOperation => "setFunctionBreakpoints",
        SetExceptionBreakpointsOperation => "setExceptionBreakpoints",
        SetInstructionBreakpointsOperation => "setInstructionBreakpoints",
        DiscoverDataBreakpointOperation => "discoverDataBreakpoint",
        SetDataBreakpointsOperation => "setDataBreakpoints",
        GetDebugBreakpointsOperation => "getBreakpoints",
        GetBreakpointLocationsOperation => "getBreakpointLocations",
        ContinueDebugOperation => "continue",
        PauseDebugOperation => "pause",
        StepOverDebugOperation => "stepOver",
        StepInDebugOperation => "stepIn",
        StepOutDebugOperation => "stepOut",
        StepBackDebugOperation => "stepBack",
        ReverseContinueDebugOperation => "reverseContinue",
        RestartFrameDebugOperation => "restartFrame",
        GotoDebugOperation => "goto",
        TerminateThreadsDebugOperation => "terminateThreads",
        GetThreadsOperation => "getThreads",
        GetStackTraceOperation => "getStackTrace",
        GetScopesOperation => "getScopes",
        GetVariablesOperation => "getVariables",
        EvaluateDebugOperation => "evaluate",
        GetExceptionInfoOperation => "getExceptionInfo",
        GetModulesOperation => "getModules",
        GetLoadedSourcesOperation => "getLoadedSources",
        GetSourceOperation => "getSource",
        GetStepInTargetsOperation => "getStepInTargets",
        GetGotoTargetsOperation => "getGotoTargets",
        GetCompletionsOperation => "getCompletions",
        ResolveDebugLocationOperation => "resolveLocation",
        SetDebugVariableOperation => "setVariable",
        SetDebugExpressionOperation => "setExpression",
        ReadDebugMemoryOperation => "readMemory",
        WriteDebugMemoryOperation => "writeMemory",
        DisassembleDebugOperation => "disassemble",
        GetDebugOutputOperation => "getOutput",
        PersistDebugOutputOperation => "persistOutput",
        CancelDebugProgressOperation => "cancelProgress",
        _ => throw new ArgumentOutOfRangeException(nameof(operation))
    };

    private static string ErrorKind(DebugSemanticFailureReason reason) => reason switch
    {
        DebugSemanticFailureReason.CapabilityUnavailable => "capability_unavailable",
        DebugSemanticFailureReason.InvalidSessionState => "invalid_session_state",
        DebugSemanticFailureReason.ReferenceExpired => "reference_expired",
        DebugSemanticFailureReason.ReferenceOwnerMismatch => "reference_owner_mismatch",
        DebugSemanticFailureReason.InvalidArguments => "invalid_request",
        DebugSemanticFailureReason.PermissionDenied => "permission_denied",
        DebugSemanticFailureReason.AdapterRequestFailed => "adapter_request_failed",
        DebugSemanticFailureReason.RequestTimedOut => "request_timed_out",
        DebugSemanticFailureReason.RequestCancelled => "request_cancelled",
        DebugSemanticFailureReason.OutputTooLarge => "output_too_large",
        DebugSemanticFailureReason.ContentStoreUnavailable => "content_store_unavailable",
        _ => "internal_failure"
    };

    private static string SafeMessage(DebugSemanticFailureReason reason) => reason switch
    {
        DebugSemanticFailureReason.CapabilityUnavailable => "The selected adapter does not support this debugger operation.",
        DebugSemanticFailureReason.InvalidSessionState => "The debugger operation is unavailable in the current session state.",
        DebugSemanticFailureReason.ReferenceExpired => "The debugger reference expired; inspect the current state and use a new token.",
        DebugSemanticFailureReason.ReferenceOwnerMismatch => "The debugger reference belongs to another owner, session, or query.",
        DebugSemanticFailureReason.InvalidArguments => "The debugger operation contains invalid arguments.",
        DebugSemanticFailureReason.PermissionDenied => "The debugger operation is not authorized.",
        DebugSemanticFailureReason.AdapterRequestFailed => "The debug adapter rejected the operation.",
        DebugSemanticFailureReason.RequestTimedOut => "The debugger operation timed out.",
        DebugSemanticFailureReason.RequestCancelled => "The debugger operation was cancelled.",
        DebugSemanticFailureReason.OutputTooLarge => "The debugger result exceeds its configured limit.",
        DebugSemanticFailureReason.ContentStoreUnavailable => "Debugger artifact storage is unavailable.",
        _ => "The debugger operation failed."
    };

    internal static (string Kind, string Message) ClassifyProtocolFailure(
        string action,
        Exception exception)
        => exception switch
        {
            DebugAdapterRequestException when action is "setVariable" or "setExpression" =>
                ("mutation_rejected",
                    $"The adapter advertises '{action}' support but rejected the selected target or value. Inspect the current stop and choose a writable location or a compatible value."),
            DebugAdapterRequestException =>
                ("adapter_request_failed", "The debug adapter rejected the operation."),
            DebugProtocolException =>
                ("adapter_protocol_failed", "The debug adapter protocol operation failed."),
            _ => throw new ArgumentOutOfRangeException(nameof(exception))
        };
}
