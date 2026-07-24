using HPD.Agent.ToolHarness.Coding.Debugging.Protocol.Generated;
using HPD.Agent.ToolHarness.Coding.Debugging.Protocol;
using HPD.Agent;
using HPDOS.ToolHarnesses.Middleware;

namespace HPD.Agent.ToolHarness.Coding.Debugging;

internal sealed record DebugOperationResult(
    string DebugTreeId,
    string DebugSessionId,
    int ThreadId,
    bool IsStopped,
    bool TimedOutWaitingForStop,
    DebugThreadSnapshot? Thread,
    DebugSessionStatus? EndedStatus = null);

internal enum DebugSteppingGranularity { Statement, Line, Instruction }

internal sealed record DebugSemanticHealth(
    DebugProtocolClientHealth Protocol,
    long ProjectionFollowUpFailures,
    long OutputPublicationsDropped,
    long ProgressNotificationsDropped,
    long RetainedOutputBytes,
    long DroppedOutputRecords,
    long DroppedOutputBytes);

internal sealed record DebugSemanticThread(
    int ThreadId, string? Name, bool IsStopped, bool IsPrimaryStoppedThread, string? StopReason,
    long SuspensionEpoch, long ResumptionGeneration);

internal sealed record DebugSemanticStackFrame(
    string FrameToken, string Name, string? SourcePath, long Line, long Column,
    bool CanRestart, string? SourceToken, string? InstructionReferenceToken);

internal sealed record DebugSemanticStackTrace(
    IReadOnlyList<DebugSemanticStackFrame> Frames, long? TotalFrames, string? ContinuationToken);

internal sealed record DebugSemanticScope(
    string Name, string VariablesToken, int? NamedVariables, int? IndexedVariables,
    bool Expensive, string? SourceToken);

internal sealed record DebugSemanticVariable(
    string Name, string Value, string? Type, string? EvaluateName,
    string? VariablesToken, int? NamedVariables, int? IndexedVariables,
    string? MemoryReferenceToken, string? LocationToken);

internal sealed record DebugSemanticVariables(IReadOnlyList<DebugSemanticVariable> Variables, string? ContinuationToken = null);

internal sealed record DebugSemanticEvaluation(
    string Result, string? Type, string? VariablesToken, int? NamedVariables,
    int? IndexedVariables, string? MemoryReferenceToken, string? LocationToken,
    bool Truncated = false, DebugArtifactWriteStatus? ArtifactStatus = null,
    ContentAddress? ContentAddress = null);

internal sealed record DebugSemanticMutationResult(
    string Value, string? Type, string? VariablesToken, int? NamedVariables,
    int? IndexedVariables, string? MemoryReferenceToken, string? LocationToken,
    bool PriorVariableDerivedTokensInvalidated);

internal sealed record DebugSemanticDataBreakpointInfo(
    string? DiscoveryToken, string Description, IReadOnlyList<string> AccessTypes,
    bool CanPersist);

internal sealed record DebugSemanticExceptionDetails(
    string? Message, string? TypeName, string? FullTypeName, string? EvaluateName,
    string? StackTrace, IReadOnlyList<DebugSemanticExceptionDetails> InnerExceptions);

internal sealed record DebugSemanticExceptionInfo(
    string ExceptionId, string? Description, string BreakMode, DebugSemanticExceptionDetails? Details,
    bool Truncated);

internal sealed record DebugSemanticSourceContent(
    string InlineContent, string? MimeType, long Utf8Bytes, bool Truncated,
    DebugArtifactWriteStatus? ArtifactStatus, ContentAddress? ContentAddress);

internal sealed record DebugSemanticSourceSummary(
    string SourceToken, string? Name, string? Path, string? Origin, string? PresentationHint);

internal sealed record DebugSemanticLoadedSources(
    IReadOnlyList<DebugSemanticSourceSummary> Sources, string? ContinuationToken, bool Truncated);

internal sealed record DebugSemanticModule(
    string ModuleToken, string Name, string? Path, bool? IsOptimized, bool? IsUserCode,
    string? Version, string? SymbolStatus);

internal sealed record DebugSemanticModules(
    IReadOnlyList<DebugSemanticModule> Modules, long? TotalModules, string? ContinuationToken);

internal sealed record DebugSemanticLocation(long Line, long? Column, long? EndLine, long? EndColumn);
internal sealed record DebugSemanticStepTarget(string TargetToken, string Label, DebugSemanticLocation? Location);
internal sealed record DebugSemanticGotoTarget(string TargetToken, string Label, DebugSemanticLocation Location);
internal sealed record DebugSemanticCompletion(
    string Label, string? Text, string? SortText, string? Detail, string? Type,
    long? Start, long? Length, long? SelectionStart, long? SelectionLength);
internal sealed record DebugSemanticCompletions(
    IReadOnlyList<DebugSemanticCompletion> Items, string? ContinuationToken, bool Truncated);
internal sealed record DebugSemanticResolvedLocation(
    DebugSemanticSourceSummary Source, long Line, long? Column, long? EndLine, long? EndColumn);
internal sealed record DebugSemanticMemoryRead(
    string Address, byte[] Bytes, long UnreadableBytes, bool Partial, string MemoryRangeToken);
internal sealed record DebugSemanticMemoryWrite(long Offset, long BytesWritten, bool Partial);
internal sealed record DebugSemanticInstruction(
    string InstructionReferenceToken, string Address, string? InstructionBytes, string Instruction,
    string? Symbol, DebugSemanticSourceSummary? Source, long? Line, long? Column,
    long? EndLine, long? EndColumn, string? PresentationHint);
internal sealed record DebugSemanticDisassembly(
    IReadOnlyList<DebugSemanticInstruction> Instructions, string? ContinuationToken);

internal sealed class DebugSemanticService(DebugSessionManager manager)
{
    public IReadOnlyList<string> ListTrees(DebugTreeLookupScope owner)
        => manager.ListTrees(owner).Select(x => x.Ownership.DebugTreeId).ToArray();

    public IReadOnlyList<DebugTreeSnapshot> ListTreeSnapshots(DebugTreeLookupScope owner)
        => manager.ListTrees(owner).Select(DebugSnapshotProjector.Project).ToArray();

    public DebugTreeSnapshot GetSnapshot(DebugTreeLookupScope owner, string treeId)
    {
        if (manager.TryResolveTerminal(owner, treeId, out var terminal))
            return terminal.Snapshot with { Status = terminal.FinalStatus };
        var tree = manager.ResolveTree(owner, treeId);
        tree.Authorization.Demand(DebugTreeGrant.Inspect);
        return DebugSnapshotProjector.Project(tree);
    }

    public DebugSessionStatus GetStatus(
        DebugTreeLookupScope owner,
        string treeId,
        string? sessionId)
    {
        if (manager.TryResolveTerminal(owner, treeId, out var terminal))
            return string.Equals(terminal.FinalStatus, "Faulted", StringComparison.Ordinal)
                ? DebugSessionStatus.Faulted
                : DebugSessionStatus.Terminated;
        return Session(owner, treeId, sessionId).State.Status;
    }

    public DebugSemanticHealth GetHealth(DebugTreeLookupScope owner, string treeId, string? sessionId)
    {
        var session = Session(owner, treeId, sessionId);
        var output = session.Output.Snapshot(includeTelemetry: true);
        return new(session.Protocol.Health, session.Projections.FollowUpFailures,
            session.OutputEvents?.DroppedPublications ?? 0,
            session.ProgressEvents?.DroppedNotifications ?? 0,
            output.RetainedBytes, output.DroppedRecords, output.DroppedBytes);
    }

    public DebugOutputSnapshot GetOutput(
        DebugTreeLookupScope owner,
        string treeId,
        string? sessionId,
        bool includeTelemetry = false)
        => manager.TryResolveTerminal(owner, treeId, out var terminal)
            ? terminal.Output
            : Session(owner, treeId, sessionId).Output.Snapshot(includeTelemetry);

    public async ValueTask<DebugArtifactWriteResult> PersistOutputAsync(
        DebugTreeLookupScope owner,
        string treeId,
        string? sessionId,
        bool includeTelemetry,
        CancellationToken cancellationToken,
        long? fromSequence = null,
        long? toSequence = null)
    {
        var tree = manager.ResolveTree(owner, treeId);
        var session = tree.SelectSession(sessionId);
        var snapshot = session.Output.Snapshot(fromSequence, toSequence, includeTelemetry);
        var text = string.Concat(snapshot.Records.Select(x => x.Text));
        var result = await tree.Artifacts.WriteTextAsync(text, "debug-output", "mixed",
            session.AdapterPlan.AdapterId, session.SessionId,
            DebugOutputBuffer.DefaultMaximumRetainedBytes, cancellationToken).ConfigureAwait(false);
        if (result.Status == DebugArtifactWriteStatus.Stored && result.Address is { } address && tree.EventPublisher is not null)
        {
            tree.AddStoredArtifact(new("debug-output", session.SessionId, address.ContentId,
                address.Scope.Value, address.Version, new Dictionary<string, string>
                {
                    ["adapter"] = session.AdapterPlan.AdapterId,
                    ["debugTreeId"] = tree.Ownership.DebugTreeId,
                    ["debugSessionId"] = session.SessionId
                }));
            await tree.EventPublisher.PublishAsync(new DebugOutputAvailableEvent
            {
                SessionId = tree.Ownership.SessionId,
                ThreadId = tree.Ownership.ThreadId,
                TraceId = tree.RuntimeBinding.EventScope.TraceId,
                DebugTreeId = tree.Ownership.DebugTreeId,
                DebugSessionId = session.SessionId,
                AdapterId = session.AdapterPlan.AdapterId,
                FirstSequence = snapshot.OldestSequence,
                LastSequence = snapshot.NewestSequence,
                Category = "Mixed",
                ContentScope = address.Scope.Value,
                ContentId = address.ContentId,
                ContentVersion = address.Version,
                DroppedRecords = snapshot.DroppedRecords,
                DroppedBytes = snapshot.DroppedBytes
            }, durable: true, CancellationToken.None).ConfigureAwait(false);
        }
        else if (result.Status == DebugArtifactWriteStatus.Stored && result.Address is { } storedAddress)
            tree.AddStoredArtifact(new("debug-output", session.SessionId, storedAddress.ContentId,
                storedAddress.Scope.Value, storedAddress.Version, new Dictionary<string, string>
                {
                    ["adapter"] = session.AdapterPlan.AdapterId,
                    ["debugTreeId"] = tree.Ownership.DebugTreeId,
                    ["debugSessionId"] = session.SessionId
                }));
        return result;
    }

    public async ValueTask<bool> CancelProgressAsync(
        DebugTreeLookupScope owner,
        string treeId,
        string? sessionId,
        string progressId,
        CancellationToken cancellationToken)
    {
        var session = Session(owner, treeId, sessionId);
        if (!session.Progress.MarkCancellationRequested(progressId)) return false;
        return await session.Protocol.CancelProgressAsync(progressId, cancellationToken).ConfigureAwait(false);
    }

    public async Task DisconnectAsync(DebugTreeLookupScope owner, string treeId, string? sessionId, bool? terminateDebuggee, bool suspendDebuggee, CancellationToken cancellationToken)
    {
        var tree = manager.ResolveTree(owner, treeId);
        var session = tree.SelectSession(sessionId);
        session.State.Transition(DebugSessionStatus.Terminating);
        var supportsTerminate = session.Capabilities?.SupportTerminateDebuggee == true;
        var supportsSuspend = session.Capabilities?.SupportSuspendDebuggee == true;
        await session.Protocol.SendAsync(DebugProtocolDescriptors.DisconnectRequest, new DisconnectArguments
        {
            TerminateDebuggee = supportsTerminate
                ? terminateDebuggee ?? session.AdapterStartMethod != DebugAdapterStartMethod.Attach
                : null,
            SuspendDebuggee = supportsSuspend ? suspendDebuggee : null,
            Restart = false
        }, cancellationToken, TimeSpan.FromSeconds(5)).ConfigureAwait(false);
        session.State.Transition(DebugSessionStatus.Terminated);
        if (session.SessionId == tree.RootSessionId)
        {
            var output = session.Output.Snapshot(includeTelemetry: true);
            var summary = new DebugSessionSummaryEvent
            {
                SessionId = tree.Ownership.SessionId,
                ThreadId = tree.Ownership.ThreadId,
                TraceId = tree.RuntimeBinding.EventScope.TraceId,
                DebugTreeId = tree.Ownership.DebugTreeId,
                DebugSessionId = session.SessionId,
                AdapterId = session.AdapterPlan.AdapterId,
                FinalStatus = "Terminated",
                ExitCode = session.ExitCode,
                DurationMilliseconds = Math.Max(0, (long)(DateTimeOffset.UtcNow - session.CreatedAt).TotalMilliseconds),
                ChildSessionCount = session.ChildSessionIds.Count,
                RetainedOutputBytes = output.RetainedBytes,
                DroppedOutputRecords = output.DroppedRecords,
                DroppedOutputBytes = output.DroppedBytes,
                ProjectionFailures = session.Projections.FollowUpFailures
            };
            var terminatedEvent = new DebugTreeTerminatedEvent
            {
                SessionId = tree.Ownership.SessionId,
                ThreadId = tree.Ownership.ThreadId,
                TraceId = tree.RuntimeBinding.EventScope.TraceId,
                DebugTreeId = tree.Ownership.DebugTreeId,
                DebugSessionId = session.SessionId,
                AdapterId = session.AdapterPlan.AdapterId,
                SafeReasonCode = "DISCONNECTED"
            };
            if (tree.EventPublisher is not null)
                await tree.EventPublisher.PublishAsync(summary, durable: true, CancellationToken.None).ConfigureAwait(false);
            await manager.RemoveAndDisposeAsync(owner, treeId).ConfigureAwait(false);
            if (tree.EventPublisher is not null)
                await tree.EventPublisher.PublishAsync(terminatedEvent, durable: true, CancellationToken.None).ConfigureAwait(false);
        }
        else
        {
            tree.ObserveTerminated(session.SessionId);
            tree.Sessions.TryRemove(session.SessionId, out _);
            await session.DisposeAsync().ConfigureAwait(false);
        }
    }

    public async Task TerminateAsync(DebugTreeLookupScope owner, string treeId, string? sessionId, bool restart, CancellationToken cancellationToken)
    {
        var session = AuthorizedSession(owner, treeId, sessionId, DebugTreeGrant.RoutineExecutionControl);
        if (session.Capabilities?.SupportsTerminateRequest != true)
            throw new DebugSemanticException(DebugSemanticFailureReason.CapabilityUnavailable,
                "The adapter does not support terminate.");
        await session.Protocol.SendAsync(DebugProtocolDescriptors.TerminateRequest,
            new TerminateArguments { Restart = restart }, cancellationToken, TimeSpan.FromSeconds(5)).ConfigureAwait(false);
    }

    public async Task<bool> RestartInPlaceAsync(DebugTreeLookupScope owner, string treeId, string? sessionId, CancellationToken cancellationToken)
    {
        var session = AuthorizedSession(owner, treeId, sessionId, DebugTreeGrant.RoutineExecutionControl);
        if (session.Capabilities?.SupportsRestartRequest != true) return false;
        await session.Protocol.SendAsync(DebugProtocolDescriptors.RestartRequest,
            new RestartArguments(), cancellationToken, TimeSpan.FromSeconds(30)).ConfigureAwait(false);
        return true;
    }

    public async ValueTask<IReadOnlyList<DebugSemanticThread>> ThreadsAsync(
        DebugTreeLookupScope owner, string treeId, string? sessionId, CancellationToken cancellationToken)
    {
        var session = AuthorizedSession(owner, treeId, sessionId, DebugTreeGrant.Inspect);
        var response = await session.Protocol.SendAsync(DebugProtocolDescriptors.ThreadsRequest,
            new DapNoArguments(), cancellationToken).ConfigureAwait(false);
        session.State.ReconcileThreads(response.Threads);
        var primaryStoppedThreadId = session.State.PrimaryStoppedThreadId;
        return session.State.Threads.Take(100).Select(thread => new DebugSemanticThread(
            thread.ThreadId, thread.Name, thread.IsStopped,
            thread.IsStopped && thread.ThreadId == primaryStoppedThreadId, thread.StopReason,
            thread.SuspensionEpoch, thread.ResumptionGeneration)).ToArray();
    }

    public async ValueTask<DebugSemanticStackTrace> StackTraceAsync(
        DebugTreeLookupScope owner, string treeId, string? sessionId, int threadId,
        int pageSize, string? continuationToken, CancellationToken cancellationToken)
    {
        if (pageSize is <= 0 or > 100) throw new DebugSemanticException(
            DebugSemanticFailureReason.InvalidArguments, "Stack-frame page size must be between 1 and 100.");
        var tree = manager.ResolveTree(owner, treeId);
        var session = AuthorizedSession(owner, treeId, sessionId, DebugTreeGrant.Inspect);
        var epoch = RequireStoppedThread(session, threadId).SuspensionEpoch;
        var context = ContinuationContext(tree, session, "stackTrace", $"thread={threadId};pageSize={pageSize}", epoch);
        var start = continuationToken is null ? 0 : tree.Continuations.Resolve(continuationToken, context).AdapterOffset;
        var response = await session.Protocol.SendAsync(DebugProtocolDescriptors.StackTraceRequest,
            new StackTraceArguments { ThreadId = threadId, StartFrame = start, Levels = pageSize }, cancellationToken).ConfigureAwait(false);
        session.Projections.CacheStackFrames(threadId, epoch, response.StackFrames);
        var frames = response.StackFrames.Take(pageSize).Select(frame => new DebugSemanticStackFrame(
            session.Projections.CreateSuspensionToken(threadId, frame.Id, "frame", frame.Id),
            Bound(frame.Name, 1024)!, Bound(frame.Source?.Path, 4096), frame.Line, frame.Column,
            frame.CanRestart == true,
            frame.Source is { } source && (source.SourceReference is > 0 || !string.IsNullOrWhiteSpace(source.Path))
                ? session.Projections.CreateSourceToken(threadId, frame.Id, source) : null,
            string.IsNullOrWhiteSpace(frame.InstructionPointerReference) ? null
                : session.Projections.CreateSuspensionTextToken(threadId, frame.Id, "instruction", frame.InstructionPointerReference))).ToArray();
        var nextOffset = checked(start + frames.Length);
        var hasMore = frames.Length == pageSize && (response.TotalFrames is null || nextOffset < response.TotalFrames);
        var next = hasMore ? tree.Continuations.Create(context, new(nextOffset)) : null;
        return new(frames, response.TotalFrames, next);
    }

    public async ValueTask<IReadOnlyList<DebugSemanticScope>> ScopesAsync(
        DebugTreeLookupScope owner, string treeId, string? sessionId, string frameToken,
        CancellationToken cancellationToken)
    {
        var session = AuthorizedSession(owner, treeId, sessionId, DebugTreeGrant.Inspect);
        var frameId = session.Projections.ResolveSuspensionToken(frameToken, "frame", out var threadId, out _);
        var response = await session.Protocol.SendAsync(DebugProtocolDescriptors.ScopesRequest,
            new ScopesArguments { FrameId = frameId }, cancellationToken).ConfigureAwait(false);
        var scopes = response.Scopes.Take(64).Select(scope => new DebugSemanticScope(
            Bound(scope.Name, 1024)!, session.Projections.CreateSuspensionToken(
                threadId, frameId, "variables", scope.VariablesReference),
            scope.NamedVariables, scope.IndexedVariables, scope.Expensive,
            scope.Source is { } source && (source.SourceReference is > 0 || !string.IsNullOrWhiteSpace(source.Path))
                ? session.Projections.CreateSourceToken(threadId, frameId, source) : null)).ToArray();
        session.Projections.CacheScopes(frameToken, scopes);
        return scopes;
    }

    public async ValueTask<DebugSemanticVariables> VariablesAsync(
        DebugTreeLookupScope owner, string treeId, string? sessionId, string variablesToken,
        string? filter, int pageSize, string? continuationToken, CancellationToken cancellationToken)
    {
        if (pageSize is <= 0 or > 200 || filter is not (null or "indexed" or "named"))
            throw new DebugSemanticException(DebugSemanticFailureReason.InvalidArguments,
                "Variable paging arguments are invalid.");
        var tree = manager.ResolveTree(owner, treeId);
        var session = AuthorizedSession(owner, treeId, sessionId, DebugTreeGrant.Inspect);
        var reference = session.Projections.ResolveSuspensionToken(variablesToken, "variables", out var threadId, out var frameId);
        var generation = threadId > 0 ? RequireStoppedThread(session, threadId).SuspensionEpoch : session.Projections.Generations.Variables;
        var context = ContinuationContext(tree, session, "variables",
            $"reference={variablesToken};filter={filter ?? "all"};pageSize={pageSize}", generation);
        var start = continuationToken is null ? 0 : tree.Continuations.Resolve(continuationToken, context).AdapterOffset;
        var response = await session.Protocol.SendAsync(DebugProtocolDescriptors.VariablesRequest,
            new VariablesArguments { VariablesReference = reference, Filter = filter, Start = start, Count = pageSize }, cancellationToken).ConfigureAwait(false);
        var items = response.Variables.Take(pageSize).Select(variable => new DebugSemanticVariable(
            Bound(variable.Name, 1024)!, Bound(variable.Value, 16 * 1024)!, Bound(variable.@Type, 1024),
            Bound(variable.EvaluateName, 4096), variable.VariablesReference > 0
                ? threadId > 0
                    ? session.Projections.CreateSuspensionToken(threadId, frameId, "variables", variable.VariablesReference)
                    : session.Projections.CreateSessionToken("variables", variable.VariablesReference)
                : null,
            variable.NamedVariables, variable.IndexedVariables,
            string.IsNullOrWhiteSpace(variable.MemoryReference) ? null
                : threadId > 0
                    ? session.Projections.CreateSuspensionTextToken(threadId, frameId, "memory", variable.MemoryReference)
                    : session.Projections.CreateSessionTextToken("memory", variable.MemoryReference),
            variable.ValueLocationReference is > 0
                ? threadId > 0
                    ? session.Projections.CreateSuspensionToken(threadId, frameId, "location", variable.ValueLocationReference.Value)
                    : session.Projections.CreateSessionToken("location", variable.ValueLocationReference.Value)
                : null)).ToArray();
        var nextOffset = checked(start + items.Length);
        var next = items.Length == pageSize ? tree.Continuations.Create(context, new(nextOffset)) : null;
        var variables = new DebugSemanticVariables(items, next);
        session.Projections.CacheVariables(variablesToken, variables);
        return variables;
    }

    public async ValueTask<DebugSemanticEvaluation> EvaluateAsync(
        DebugTreeLookupScope owner, string treeId, string? sessionId, string expression,
        string? frameToken, string? context, CancellationToken cancellationToken)
        => await EvaluateAsync(owner, treeId, sessionId, expression, frameToken, context,
            hexadecimal: false, privilegedAuthorization: null, cancellationToken).ConfigureAwait(false);

    public async ValueTask<DebugSemanticEvaluation> EvaluateAsync(
        DebugTreeLookupScope owner, string treeId, string? sessionId, string expression,
        string? frameToken, string? context, DebugPrivilegedOperationAuthorization? privilegedAuthorization,
        CancellationToken cancellationToken)
        => await EvaluateAsync(owner, treeId, sessionId, expression, frameToken, context,
            hexadecimal: false, privilegedAuthorization, cancellationToken).ConfigureAwait(false);

    public async ValueTask<DebugSemanticEvaluation> EvaluateAsync(
        DebugTreeLookupScope owner, string treeId, string? sessionId, string expression,
        string? frameToken, string? context, bool hexadecimal,
        DebugPrivilegedOperationAuthorization? privilegedAuthorization,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(expression);
        if (System.Text.Encoding.UTF8.GetByteCount(expression) > 16 * 1024)
            throw new DebugSemanticException(DebugSemanticFailureReason.InvalidArguments, "Evaluation expression exceeds 16 KiB.");
        var acceptedContext = context is null or "watch" or "repl" or "hover" or "clipboard" or "variables";
        if (!acceptedContext)
            throw new DebugSemanticException(DebugSemanticFailureReason.InvalidArguments, "The evaluation context is unsupported.");
        var tree = manager.ResolveTree(owner, treeId);
        var session = AuthorizedSession(owner, treeId, sessionId, DebugTreeGrant.Evaluate);
        if (context == "repl")
            (privilegedAuthorization ?? throw new DebugSemanticException(DebugSemanticFailureReason.PermissionDenied,
                "REPL evaluation requires privileged authorization."))
                .Validate(tree, session, DebugPrivilegedOperation.PrivilegedEvaluate);
        int? frameId = null;
        var threadId = RequireAnyStoppedThread(session).ThreadId;
        if (frameToken is not null)
            frameId = session.Projections.ResolveSuspensionToken(frameToken, "frame", out threadId, out _);
        var response = await session.Protocol.SendAsync(DebugProtocolDescriptors.EvaluateRequest,
            new EvaluateArguments { Expression = expression, FrameId = frameId, Context = context,
                Format = hexadecimal ? new ValueFormat { Hex = true } : null }, cancellationToken).ConfigureAwait(false);
        const int inlineBytes = 64 * 1024;
        var resultBytes = System.Text.Encoding.UTF8.GetByteCount(response.Result);
        var inline = BoundUtf8(response.Result, inlineBytes);
        DebugArtifactWriteResult? artifact = null;
        if (resultBytes > inlineBytes)
            artifact = await tree.Artifacts.WriteTextAsync(response.Result, "debug-evaluation", "evaluation",
                session.AdapterPlan.AdapterId, session.SessionId, 4 * 1024 * 1024, cancellationToken).ConfigureAwait(false);
        return new(inline, Bound(response.@Type, 1024),
            response.VariablesReference > 0 ? session.Projections.CreateSuspensionToken(
                threadId, frameId, "variables", response.VariablesReference) : null,
            response.NamedVariables, response.IndexedVariables,
            string.IsNullOrWhiteSpace(response.MemoryReference) ? null
                : session.Projections.CreateSuspensionTextToken(threadId, frameId, "memory", response.MemoryReference),
            response.ValueLocationReference is > 0
                ? session.Projections.CreateSuspensionToken(threadId, frameId, "location", response.ValueLocationReference.Value) : null,
            resultBytes > inlineBytes, artifact?.Status, artifact?.Address);
    }

    public async ValueTask<DebugSemanticMutationResult> SetVariableAsync(
        DebugTreeLookupScope owner, string treeId, string? sessionId, string variablesToken,
        string name, string value, DebugPrivilegedOperationAuthorization authorization,
        CancellationToken cancellationToken)
        => await SetVariableAsync(owner, treeId, sessionId, variablesToken, name, value,
            hexadecimal: false, authorization, cancellationToken).ConfigureAwait(false);

    public async ValueTask<DebugSemanticMutationResult> SetVariableAsync(
        DebugTreeLookupScope owner, string treeId, string? sessionId, string variablesToken,
        string name, string value, bool hexadecimal, DebugPrivilegedOperationAuthorization authorization,
        CancellationToken cancellationToken)
    {
        ValidateMutationText(name, 1024, nameof(name));
        ValidateMutationText(value, 64 * 1024, nameof(value));
        RequireCapability(owner, treeId, sessionId, x => x.SupportsSetVariable == true, "set variable");
        var tree = manager.ResolveTree(owner, treeId);
        var session = AuthorizedSession(owner, treeId, sessionId, DebugTreeGrant.MutateVariables);
        authorization.Validate(tree, session, DebugPrivilegedOperation.SetVariable);
        var reference = session.Projections.ResolveSuspensionToken(variablesToken, "variables", out var threadId, out var frameId);
        var response = await session.Protocol.SendAsync(DebugProtocolDescriptors.SetVariableRequest,
            new SetVariableArguments { VariablesReference = reference, Name = name, Value = value,
                Format = hexadecimal ? new ValueFormat { Hex = true } : null }, cancellationToken).ConfigureAwait(false);
        session.Projections.Invalidate(new InvalidatedEventBody { Areas = [InvalidatedAreas.Variables], ThreadId = threadId, StackFrameId = frameId });
        return MutationResult(session, threadId, frameId, response.Value, response.Type,
            response.VariablesReference, response.NamedVariables, response.IndexedVariables,
            response.MemoryReference, response.ValueLocationReference);
    }

    public async ValueTask<DebugSemanticMutationResult> SetExpressionAsync(
        DebugTreeLookupScope owner, string treeId, string? sessionId, string expression,
        string value, string? frameToken, DebugPrivilegedOperationAuthorization authorization,
        CancellationToken cancellationToken)
        => await SetExpressionAsync(owner, treeId, sessionId, expression, value, frameToken,
            hexadecimal: false, authorization, cancellationToken).ConfigureAwait(false);

    public async ValueTask<DebugSemanticMutationResult> SetExpressionAsync(
        DebugTreeLookupScope owner, string treeId, string? sessionId, string expression,
        string value, string? frameToken, bool hexadecimal,
        DebugPrivilegedOperationAuthorization authorization, CancellationToken cancellationToken)
    {
        ValidateMutationText(expression, 16 * 1024, nameof(expression));
        ValidateMutationText(value, 64 * 1024, nameof(value));
        RequireCapability(owner, treeId, sessionId, x => x.SupportsSetExpression == true, "set expression");
        var tree = manager.ResolveTree(owner, treeId);
        var session = AuthorizedSession(owner, treeId, sessionId, DebugTreeGrant.MutateVariables);
        authorization.Validate(tree, session, DebugPrivilegedOperation.SetExpression);
        int? frameId = null;
        var threadId = RequireAnyStoppedThread(session).ThreadId;
        if (frameToken is not null)
            frameId = session.Projections.ResolveSuspensionToken(frameToken, "frame", out threadId, out _);
        var response = await session.Protocol.SendAsync(DebugProtocolDescriptors.SetExpressionRequest,
            new SetExpressionArguments { Expression = expression, Value = value, FrameId = frameId,
                Format = hexadecimal ? new ValueFormat { Hex = true } : null }, cancellationToken).ConfigureAwait(false);
        session.Projections.Invalidate(new InvalidatedEventBody { Areas = [InvalidatedAreas.Variables], ThreadId = threadId, StackFrameId = frameId });
        return MutationResult(session, threadId, frameId, response.Value, response.Type,
            response.VariablesReference, response.NamedVariables, response.IndexedVariables,
            response.MemoryReference, response.ValueLocationReference);
    }

    public async ValueTask<DebugSemanticSourceContent> SourceAsync(
        DebugTreeLookupScope owner, string treeId, string? sessionId, string sourceToken,
        CancellationToken cancellationToken)
    {
        var tree = manager.ResolveTree(owner, treeId);
        var session = AuthorizedSession(owner, treeId, sessionId, DebugTreeGrant.Inspect);
        var source = session.Projections.ResolveSourceToken(sourceToken);
        var reference = source.SourceReference.GetValueOrDefault();
        var response = await session.Protocol.SendAsync(DebugProtocolDescriptors.SourceRequest,
            new SourceArguments { SourceReference = reference, Source = source }, cancellationToken).ConfigureAwait(false);
        session.Projections.CacheSource(sourceToken, response);
        const int inlineBytes = 64 * 1024;
        const int maximumArtifactBytes = 4 * 1024 * 1024;
        var byteCount = System.Text.Encoding.UTF8.GetByteCount(response.Content);
        if (byteCount <= inlineBytes)
            return new(response.Content, Bound(response.MimeType, 256), byteCount, false, null, null);
        var preview = BoundUtf8(response.Content, inlineBytes);
        var artifact = await tree.Artifacts.WriteTextAsync(response.Content, "debug-source",
            Bound(response.MimeType, 256), session.AdapterPlan.AdapterId, session.SessionId,
            maximumArtifactBytes, cancellationToken).ConfigureAwait(false);
        return new(preview, Bound(response.MimeType, 256), byteCount, true, artifact.Status, artifact.Address);
    }

    public async ValueTask<DebugSemanticDataBreakpointInfo> DataBreakpointInfoAsync(
        DebugTreeLookupScope owner,
        string treeId,
        string? sessionId,
        string name,
        string? variablesToken,
        string? frameToken,
        long? bytes,
        bool? asAddress,
        string? mode,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        RequireCapability(owner, treeId, sessionId,
            capability => capability.SupportsDataBreakpoints == true,
            "data breakpoint discovery");
        var session = AuthorizedSession(owner, treeId, sessionId, DebugTreeGrant.Inspect);
        int? variablesReference = null;
        int? frameId = null;
        int threadId;
        if (variablesToken is not null)
        {
            variablesReference = session.Projections.ResolveSuspensionToken(
                variablesToken, "variables", out threadId, out var variablesFrame);
            frameId = variablesFrame;
            if (threadId == 0)
                threadId = session.State.Threads.FirstOrDefault(x => x.IsStopped)?.ThreadId
                    ?? throw new InvalidOperationException("Data-breakpoint discovery requires a stopped thread.");
        }
        else if (frameToken is not null)
            frameId = session.Projections.ResolveSuspensionToken(frameToken, "frame", out threadId, out _);
        else
            threadId = session.State.Threads.FirstOrDefault(x => x.IsStopped)?.ThreadId
                ?? throw new InvalidOperationException("Data-breakpoint discovery requires a stopped thread.");

        var response = await session.Protocol.SendAsync(DebugProtocolDescriptors.DataBreakpointInfoRequest,
            new DataBreakpointInfoArguments
            {
                VariablesReference = variablesReference,
                Name = name,
                FrameId = frameId,
                Bytes = bytes,
                AsAddress = asAddress,
                Mode = mode
            }, cancellationToken).ConfigureAwait(false);
        return new(response.DataId is null ? null : session.Projections.CreateDataBreakpointToken(
                threadId, frameId, response.DataId),
            Bound(response.Description, 4096)!,
            response.AccessTypes?.Take(16).Select(x => Bound(x.Value, 128)!).ToArray() ?? [],
            response.CanPersist == true);
    }

    public async ValueTask<DebugSemanticLoadedSources> LoadedSourcesAsync(
        DebugTreeLookupScope owner, string treeId, string? sessionId, int pageSize,
        string? continuationToken, CancellationToken cancellationToken)
    {
        if (pageSize is <= 0 or > 200)
            throw new DebugSemanticException(DebugSemanticFailureReason.InvalidArguments, "Loaded-source page size must be between 1 and 200.");
        RequireCapability(owner, treeId, sessionId, x => x.SupportsLoadedSourcesRequest == true, "loaded sources");
        var tree = manager.ResolveTree(owner, treeId);
        var session = AuthorizedSession(owner, treeId, sessionId, DebugTreeGrant.Inspect);
        var generation = session.Projections.Generations.Sources;
        var context = ContinuationContext(tree, session, "loadedSources", $"pageSize={pageSize}", generation);
        IReadOnlyList<DebugSemanticSourceSummary> all;
        var offset = 0;
        if (continuationToken is not null)
        {
            var state = tree.Continuations.Resolve(continuationToken, context);
            offset = checked((int)state.AdapterOffset);
            all = state.State as IReadOnlyList<DebugSemanticSourceSummary>
                ?? throw new DebugSemanticException(DebugSemanticFailureReason.ReferenceExpired, "The loaded-source continuation state expired.");
        }
        else
        {
            var response = await session.Protocol.SendAsync(DebugProtocolDescriptors.LoadedSourcesRequest,
                new LoadedSourcesArguments(), cancellationToken).ConfigureAwait(false);
            all = response.Sources.Take(4096).Select(source => new DebugSemanticSourceSummary(
                session.Projections.CreateSourceToken(0, null, source), Bound(source.Name, 1024),
                Bound(source.Path, 4096), Bound(source.Origin, 256), Bound(source.PresentationHint, 128))).ToArray();
        }
        var page = all.Skip(offset).Take(pageSize).ToArray();
        var nextOffset = checked(offset + page.Length);
        var next = nextOffset < all.Count
            ? tree.Continuations.Create(context, new(nextOffset, all)) : null;
        return new(page, next, next is not null);
    }

    public ValueTask<DebugSemanticLoadedSources> LoadedSourcesAsync(
        DebugTreeLookupScope owner, string treeId, string? sessionId, CancellationToken cancellationToken)
        => LoadedSourcesAsync(owner, treeId, sessionId, 200, null, cancellationToken);

    public async ValueTask<DebugSemanticExceptionInfo> ExceptionInfoAsync(DebugTreeLookupScope owner, string treeId, string? sessionId, int threadId, CancellationToken cancellationToken)
    {
        RequireCapability(owner, treeId, sessionId, x => x.SupportsExceptionInfoRequest == true, "exception information");
        var session = AuthorizedSession(owner, treeId, sessionId, DebugTreeGrant.Inspect);
        _ = RequireStoppedThread(session, threadId);
        var response = await session.Protocol.SendAsync(DebugProtocolDescriptors.ExceptionInfoRequest,
            new ExceptionInfoArguments { ThreadId = threadId }, cancellationToken).ConfigureAwait(false);
        var budget = new ExceptionBudget(64 * 1024, 64);
        var details = MapExceptionDetails(response.Details, 0, budget);
        return new(Bound(response.ExceptionId, 1024)!, Bound(response.Description, 4096),
            Bound(response.BreakMode.Value, 128)!, details, budget.Truncated);
    }

    public async ValueTask<DebugSemanticModules> ModulesAsync(
        DebugTreeLookupScope owner, string treeId, string? sessionId, int pageSize,
        string? continuationToken, CancellationToken cancellationToken)
    {
        if (pageSize is <= 0 or > 200)
            throw new DebugSemanticException(DebugSemanticFailureReason.InvalidArguments, "Module page size must be between 1 and 200.");
        RequireCapability(owner, treeId, sessionId, x => x.SupportsModulesRequest == true, "modules");
        var tree = manager.ResolveTree(owner, treeId);
        var session = AuthorizedSession(owner, treeId, sessionId, DebugTreeGrant.Inspect);
        var generation = session.Projections.Generations.Modules;
        var context = ContinuationContext(tree, session, "modules", $"pageSize={pageSize}", generation);
        var start = continuationToken is null ? 0 : checked((int)tree.Continuations.Resolve(continuationToken, context).AdapterOffset);
        var response = await session.Protocol.SendAsync(DebugProtocolDescriptors.ModulesRequest,
            new ModulesArguments { StartModule = start, ModuleCount = pageSize }, cancellationToken).ConfigureAwait(false);
        var modules = response.Modules.Take(pageSize).Select(module => new DebugSemanticModule(
            session.Projections.CreateSessionTextToken("module", module.Id.GetRawText()),
            Bound(module.Name, 1024)!, Bound(module.Path, 4096), module.IsOptimized, module.IsUserCode,
            Bound(module.Version, 256), Bound(module.SymbolStatus, 512))).ToArray();
        var nextOffset = checked(start + modules.Length);
        var hasMore = modules.Length == pageSize && (response.TotalModules is null || nextOffset < response.TotalModules);
        var next = hasMore ? tree.Continuations.Create(context, new(nextOffset)) : null;
        return new(modules, response.TotalModules, next);
    }

    public async ValueTask<IReadOnlyList<DebugSemanticLocation>> BreakpointLocationsAsync(
        DebugTreeLookupScope owner, string treeId, string? sessionId, string sourceToken,
        long line, long? column, long? endLine, long? endColumn, CancellationToken cancellationToken)
    {
        ValidateLocationRange(line, column, endLine, endColumn);
        RequireCapability(owner, treeId, sessionId, x => x.SupportsBreakpointLocationsRequest == true, "breakpoint locations");
        var session = AuthorizedSession(owner, treeId, sessionId, DebugTreeGrant.Inspect);
        var source = session.Projections.ResolveSourceToken(sourceToken);
        var response = await session.Protocol.SendAsync(DebugProtocolDescriptors.BreakpointLocationsRequest,
            new BreakpointLocationsArguments { Source = source, Line = line, Column = column, EndLine = endLine, EndColumn = endColumn },
            cancellationToken).ConfigureAwait(false);
        return response.Breakpoints.Take(200).Select(location => new DebugSemanticLocation(
            location.Line, location.Column, location.EndLine, location.EndColumn)).ToArray();
    }

    public async ValueTask<IReadOnlyList<DebugSemanticStepTarget>> StepInTargetsAsync(
        DebugTreeLookupScope owner, string treeId, string? sessionId, string frameToken,
        CancellationToken cancellationToken)
    {
        RequireCapability(owner, treeId, sessionId, x => x.SupportsStepInTargetsRequest == true, "step-in targets");
        var session = AuthorizedSession(owner, treeId, sessionId, DebugTreeGrant.Inspect);
        var frameId = session.Projections.ResolveSuspensionToken(frameToken, "frame", out var threadId, out _);
        _ = RequireStoppedThread(session, threadId);
        var response = await session.Protocol.SendAsync(DebugProtocolDescriptors.StepInTargetsRequest,
            new StepInTargetsArguments { FrameId = frameId }, cancellationToken).ConfigureAwait(false);
        return response.Targets.Take(200).Select(target => new DebugSemanticStepTarget(
            session.Projections.CreateSuspensionToken(threadId, frameId, "stepInTarget", target.Id),
            Bound(target.Label, 1024)!, target.Line is null ? null : new(
                target.Line.Value, target.Column, target.EndLine, target.EndColumn))).ToArray();
    }

    public async ValueTask<IReadOnlyList<DebugSemanticGotoTarget>> GotoTargetsAsync(
        DebugTreeLookupScope owner, string treeId, string? sessionId, int threadId,
        string sourceToken, long line, long? column, CancellationToken cancellationToken)
    {
        ValidateLocationRange(line, column, null, null);
        RequireCapability(owner, treeId, sessionId, x => x.SupportsGotoTargetsRequest == true, "goto targets");
        var session = AuthorizedSession(owner, treeId, sessionId, DebugTreeGrant.RoutineExecutionControl);
        var stopped = RequireStoppedThread(session, threadId);
        var source = session.Projections.ResolveSourceToken(sourceToken);
        var response = await session.Protocol.SendAsync(DebugProtocolDescriptors.GotoTargetsRequest,
            new GotoTargetsArguments { Source = source, Line = line, Column = column }, cancellationToken).ConfigureAwait(false);
        return response.Targets.Take(200).Select(target => new DebugSemanticGotoTarget(
            session.Projections.CreateSuspensionToken(threadId, null, "gotoTarget", target.Id),
            Bound(target.Label, 1024)!, new(target.Line, target.Column, target.EndLine, target.EndColumn))).ToArray();
    }

    public async ValueTask<DebugSemanticCompletions> CompletionsAsync(
        DebugTreeLookupScope owner, string treeId, string? sessionId, string text,
        long column, long? line, string? frameToken, int pageSize, string? continuationToken,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(text);
        if (System.Text.Encoding.UTF8.GetByteCount(text) > 16 * 1024 || column < 0 || line < 0 || pageSize is <= 0 or > 200)
            throw new DebugSemanticException(DebugSemanticFailureReason.InvalidArguments, "Completion input is invalid or exceeds 16 KiB.");
        RequireCapability(owner, treeId, sessionId, x => x.SupportsCompletionsRequest == true, "completions");
        var tree = manager.ResolveTree(owner, treeId);
        var session = AuthorizedSession(owner, treeId, sessionId, DebugTreeGrant.Inspect);
        int? frameId = null;
        long generation = session.Projections.Generations.All;
        if (frameToken is not null)
        {
            frameId = session.Projections.ResolveSuspensionToken(frameToken, "frame", out var threadId, out _);
            generation = RequireStoppedThread(session, threadId).SuspensionEpoch;
        }
        var context = ContinuationContext(tree, session, "completions",
            $"frame={frameToken ?? "none"};column={column};line={line};pageSize={pageSize};text={StableQueryHash(text)}", generation);
        IReadOnlyList<DebugSemanticCompletion> all;
        var offset = 0;
        if (continuationToken is not null)
        {
            var state = tree.Continuations.Resolve(continuationToken, context);
            offset = checked((int)state.AdapterOffset);
            all = state.State as IReadOnlyList<DebugSemanticCompletion>
                ?? throw new DebugSemanticException(DebugSemanticFailureReason.ReferenceExpired, "The completion continuation expired.");
        }
        else
        {
            var response = await session.Protocol.SendAsync(DebugProtocolDescriptors.CompletionsRequest,
                new CompletionsArguments { Text = text, Column = column, Line = line, FrameId = frameId }, cancellationToken).ConfigureAwait(false);
            all = response.Targets.Take(4096).Select(item => new DebugSemanticCompletion(
                Bound(item.Label, 1024)!, Bound(item.Text, 4096), Bound(item.SortText, 1024),
                Bound(item.Detail, 4096), Bound(item.Type?.Value, 128), item.Start, item.Length,
                item.SelectionStart, item.SelectionLength)).ToArray();
        }
        var items = all.Skip(offset).Take(pageSize).ToArray();
        var nextOffset = checked(offset + items.Length);
        var next = nextOffset < all.Count ? tree.Continuations.Create(context, new(nextOffset, all)) : null;
        return new(items, next, next is not null);
    }

    public async ValueTask<DebugSemanticResolvedLocation> ResolveLocationAsync(
        DebugTreeLookupScope owner, string treeId, string? sessionId, string locationToken,
        CancellationToken cancellationToken)
    {
        var session = AuthorizedSession(owner, treeId, sessionId, DebugTreeGrant.Inspect);
        var reference = session.Projections.ResolveSuspensionToken(locationToken, "location", out var threadId, out var frameId);
        var response = await session.Protocol.SendAsync(DebugProtocolDescriptors.LocationsRequest,
            new LocationsArguments { LocationReference = reference }, cancellationToken).ConfigureAwait(false);
        var sourceToken = session.Projections.CreateSourceToken(threadId, frameId, response.Source);
        return new(new(sourceToken, Bound(response.Source.Name, 1024), Bound(response.Source.Path, 4096),
            Bound(response.Source.Origin, 256), Bound(response.Source.PresentationHint, 128)),
            response.Line, response.Column, response.EndLine, response.EndColumn);
    }

    public async ValueTask<DebugSemanticMemoryRead> ReadMemoryAsync(
        DebugTreeLookupScope owner, string treeId, string? sessionId, string memoryReferenceToken,
        long offset, int count, CancellationToken cancellationToken)
    {
        if (count is <= 0 or > 64 * 1024) throw new DebugSemanticException(
            DebugSemanticFailureReason.InvalidArguments, "Memory read count must be between 1 and 65536 bytes.");
        ValidateOffsetCount(offset, count, "memory read");
        RequireCapability(owner, treeId, sessionId, x => x.SupportsReadMemoryRequest == true, "read memory");
        var session = AuthorizedSession(owner, treeId, sessionId, DebugTreeGrant.Inspect);
        var memoryReference = session.Projections.ResolveTextToken(memoryReferenceToken, "memory", out _, out _);
        var response = await session.Protocol.SendAsync(DebugProtocolDescriptors.ReadMemoryRequest,
            new ReadMemoryArguments { MemoryReference = memoryReference, Offset = offset, Count = count }, cancellationToken).ConfigureAwait(false);
        var unreadable = response.UnreadableBytes.GetValueOrDefault();
        if (unreadable < 0 || unreadable > count)
            throw new DebugSemanticException(DebugSemanticFailureReason.AdapterRequestFailed, "The adapter returned invalid unreadable-byte metadata.");
        byte[] bytes;
        try { bytes = response.Data is null ? [] : Convert.FromBase64String(response.Data); }
        catch (FormatException exception)
        {
            throw new DebugSemanticException(DebugSemanticFailureReason.AdapterRequestFailed,
                "The adapter returned malformed memory data.", exception);
        }
        if (bytes.Length > count || bytes.Length + unreadable > count)
            throw new DebugSemanticException(DebugSemanticFailureReason.AdapterRequestFailed,
                "The adapter returned more memory data than requested.");
        var rangeId = session.Projections.TrackMemoryRange(memoryReference, offset, Math.Max(1, bytes.Length));
        var rangeToken = session.Projections.CreateSessionTextToken("memoryRange", rangeId.ToString(System.Globalization.CultureInfo.InvariantCulture));
        return new(Bound(response.Address, 1024)!, bytes, unreadable, bytes.Length + unreadable < count || unreadable > 0, rangeToken);
    }

    public async ValueTask<DebugSemanticMemoryWrite> WriteMemoryAsync(
        DebugTreeLookupScope owner, string treeId, string? sessionId, string memoryReferenceToken,
        long offset, ReadOnlyMemory<byte> bytes, bool allowPartial,
        DebugPrivilegedOperationAuthorization authorization, CancellationToken cancellationToken)
    {
        if (bytes.Length is <= 0 or > 4 * 1024) throw new DebugSemanticException(
            DebugSemanticFailureReason.InvalidArguments, "Memory writes must contain between 1 and 4096 bytes.");
        ValidateOffsetCount(offset, bytes.Length, "memory write");
        RequireCapability(owner, treeId, sessionId, x => x.SupportsWriteMemoryRequest == true, "write memory");
        var tree = manager.ResolveTree(owner, treeId);
        var session = AuthorizedSession(owner, treeId, sessionId, DebugTreeGrant.WriteMemory);
        authorization.Validate(tree, session, DebugPrivilegedOperation.WriteMemory);
        var memoryReference = session.Projections.ResolveTextToken(memoryReferenceToken, "memory", out _, out _);
        var response = await session.Protocol.SendAsync(DebugProtocolDescriptors.WriteMemoryRequest,
            new WriteMemoryArguments
            {
                MemoryReference = memoryReference, Offset = offset, AllowPartial = allowPartial,
                Data = Convert.ToBase64String(bytes.Span)
            }, cancellationToken).ConfigureAwait(false);
        var written = response?.BytesWritten ?? bytes.Length;
        var returnedOffset = response?.Offset ?? 0;
        if (written < 0 || written > bytes.Length || returnedOffset < 0 || (!allowPartial && written != bytes.Length))
            throw new DebugSemanticException(DebugSemanticFailureReason.AdapterRequestFailed,
                "The adapter returned invalid memory-write counts.");
        session.Projections.ObserveMemory(new MemoryEventBody
        {
            MemoryReference = memoryReference,
            Offset = checked(offset + returnedOffset),
            Count = Math.Max(1, written)
        });
        return new(returnedOffset, written, written != bytes.Length);
    }

    public async ValueTask<DebugSemanticDisassembly> DisassembleAsync(
        DebugTreeLookupScope owner, string treeId, string? sessionId, string referenceToken,
        long offset, long instructionOffset, int instructionCount, bool resolveSymbols,
        string? continuationToken, CancellationToken cancellationToken)
    {
        if (instructionCount is <= 0 or > 256) throw new DebugSemanticException(
            DebugSemanticFailureReason.InvalidArguments, "Disassembly pages must contain between 1 and 256 instructions.");
        RequireCapability(owner, treeId, sessionId, x => x.SupportsDisassembleRequest == true, "disassembly");
        var tree = manager.ResolveTree(owner, treeId);
        var session = AuthorizedSession(owner, treeId, sessionId, DebugTreeGrant.Inspect);
        string reference;
        int referenceThreadId;
        int? referenceFrameId;
        try
        {
            reference = session.Projections.ResolveTextToken(referenceToken, "memory",
                out referenceThreadId, out referenceFrameId);
        }
        catch (InvalidOperationException)
        {
            reference = session.Projections.ResolveTextToken(referenceToken, "instruction",
                out referenceThreadId, out referenceFrameId);
        }
        var generation = session.Projections.Generations.Memory;
        var identity = $"reference={referenceToken};offset={offset};count={instructionCount};symbols={resolveSymbols}";
        var context = ContinuationContext(tree, session, "disassemble", identity, generation);
        var currentInstructionOffset = continuationToken is null ? instructionOffset
            : tree.Continuations.Resolve(continuationToken, context).AdapterOffset;
        var response = await session.Protocol.SendAsync(DebugProtocolDescriptors.DisassembleRequest,
            new DisassembleArguments
            {
                MemoryReference = reference, Offset = offset, InstructionOffset = currentInstructionOffset,
                InstructionCount = instructionCount, ResolveSymbols = resolveSymbols
            }, cancellationToken).ConfigureAwait(false);
        var wireInstructions = response?.Instructions ?? [];
        var instructions = wireInstructions.Take(instructionCount).Select(item =>
        {
            DebugSemanticSourceSummary? source = null;
            if (item.Location is { } location)
            {
                var sourceToken = session.Projections.CreateSourceToken(referenceThreadId, referenceFrameId, location);
                source = new(sourceToken, Bound(location.Name, 1024), Bound(location.Path, 4096),
                    Bound(location.Origin, 256), Bound(location.PresentationHint, 128));
            }
            return new DebugSemanticInstruction(
                referenceThreadId > 0
                    ? session.Projections.CreateSuspensionTextToken(referenceThreadId, referenceFrameId,
                        "instruction", item.Address)
                    : session.Projections.CreateSessionTextToken("instruction", item.Address),
                Bound(item.Address, 1024)!, Bound(item.InstructionBytes, 4096), Bound(item.Instruction, 4096)!,
                Bound(item.Symbol, 1024), source, item.Line, item.Column, item.EndLine, item.EndColumn,
                Bound(item.PresentationHint, 128));
        }).ToArray();
        var next = instructions.Length == instructionCount
            ? tree.Continuations.Create(context, new(checked(currentInstructionOffset + instructions.Length))) : null;
        return new(instructions, next);
    }

    public async ValueTask TerminateThreadsAsync(
        DebugTreeLookupScope owner, string treeId, string? sessionId,
        IReadOnlyList<int> threadIds, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(threadIds);
        var ids = threadIds.Distinct().Take(256).ToArray();
        if (ids.Length == 0 || threadIds.Count > 256)
            throw new DebugSemanticException(DebugSemanticFailureReason.InvalidArguments, "Terminate-threads requires 1 to 256 thread IDs.");
        RequireCapability(owner, treeId, sessionId, x => x.SupportsTerminateThreadsRequest == true, "terminate threads");
        var session = AuthorizedSession(owner, treeId, sessionId, DebugTreeGrant.RoutineExecutionControl);
        if (ids.Any(id => !session.State.Threads.Any(thread => thread.ThreadId == id)))
            throw new DebugSemanticException(DebugSemanticFailureReason.ReferenceOwnerMismatch,
                "One or more debugger threads do not belong to the selected protocol session.");
        await session.Protocol.SendAsync(DebugProtocolDescriptors.TerminateThreadsRequest,
            new TerminateThreadsArguments { ThreadIds = ids.ToList() }, cancellationToken).ConfigureAwait(false);
    }

    public Task<DebugOperationResult> ContinueAsync(DebugTreeLookupScope owner, string treeId, string? sessionId, int threadId, bool singleThread, TimeSpan waitTimeout, CancellationToken cancellationToken)
    {
        var option = ExecutionSingleThread(Session(owner, treeId, sessionId), singleThread);
        return ResumeAndWaitAsync(owner, treeId, sessionId, threadId, allThreadsResume: !singleThread, waitTimeout,
            (session, ct) => session.Protocol.SendAsync(DebugProtocolDescriptors.ContinueRequest,
                new ContinueArguments { ThreadId = threadId, SingleThread = option }, ct).AsTask(), cancellationToken);
    }

    public Task<DebugOperationResult> NextAsync(DebugTreeLookupScope owner, string treeId, string? sessionId, int threadId, bool singleThread, TimeSpan waitTimeout, CancellationToken cancellationToken)
        => NextAsync(owner, treeId, sessionId, threadId, singleThread, null, waitTimeout, cancellationToken);

    public Task<DebugOperationResult> NextAsync(DebugTreeLookupScope owner, string treeId, string? sessionId, int threadId,
        bool singleThread, DebugSteppingGranularity? granularity, TimeSpan waitTimeout, CancellationToken cancellationToken)
    {
        var session = Session(owner, treeId, sessionId);
        var singleThreadOption = ExecutionSingleThread(session, singleThread);
        var granularityOption = ExecutionGranularity(session, granularity);
        return ResumeAndWaitAsync(owner, treeId, sessionId, threadId, allThreadsResume: !singleThread, waitTimeout,
            (current, ct) => current.Protocol.SendAsync(DebugProtocolDescriptors.NextRequest,
                new NextArguments { ThreadId = threadId, SingleThread = singleThreadOption,
                    Granularity = granularityOption }, ct).AsTask(), cancellationToken);
    }

    public Task<DebugOperationResult> StepInAsync(DebugTreeLookupScope owner, string treeId, string? sessionId, int threadId, bool singleThread, TimeSpan waitTimeout, CancellationToken cancellationToken)
        => StepInAsync(owner, treeId, sessionId, threadId, singleThread, null, waitTimeout, cancellationToken);

    public Task<DebugOperationResult> StepInAsync(DebugTreeLookupScope owner, string treeId, string? sessionId, int threadId,
        bool singleThread, string? targetToken, TimeSpan waitTimeout, CancellationToken cancellationToken)
        => StepInAsync(owner, treeId, sessionId, threadId, singleThread, targetToken, null, waitTimeout, cancellationToken);

    public Task<DebugOperationResult> StepInAsync(DebugTreeLookupScope owner, string treeId, string? sessionId, int threadId,
        bool singleThread, string? targetToken, DebugSteppingGranularity? granularity,
        TimeSpan waitTimeout, CancellationToken cancellationToken)
    {
        var session = AuthorizedSession(owner, treeId, sessionId, DebugTreeGrant.RoutineExecutionControl);
        var singleThreadOption = ExecutionSingleThread(session, singleThread);
        var granularityOption = ExecutionGranularity(session, granularity);
        int? targetId = null;
        if (targetToken is not null)
        {
            targetId = session.Projections.ResolveSuspensionToken(targetToken, "stepInTarget", out var targetThread, out _);
            if (targetThread != threadId)
                throw new DebugSemanticException(DebugSemanticFailureReason.ReferenceOwnerMismatch,
                    "The step-in target belongs to another debugger thread.");
        }
        return ResumeAndWaitAsync(owner, treeId, sessionId, threadId, allThreadsResume: !singleThread, waitTimeout,
            (current, ct) => current.Protocol.SendAsync(DebugProtocolDescriptors.StepInRequest,
                new StepInArguments { ThreadId = threadId, SingleThread = singleThreadOption,
                    TargetId = targetId, Granularity = granularityOption }, ct).AsTask(), cancellationToken);
    }

    public Task<DebugOperationResult> StepOutAsync(DebugTreeLookupScope owner, string treeId, string? sessionId, int threadId, bool singleThread, TimeSpan waitTimeout, CancellationToken cancellationToken)
        => StepOutAsync(owner, treeId, sessionId, threadId, singleThread, null, waitTimeout, cancellationToken);

    public Task<DebugOperationResult> StepOutAsync(DebugTreeLookupScope owner, string treeId, string? sessionId, int threadId,
        bool singleThread, DebugSteppingGranularity? granularity, TimeSpan waitTimeout, CancellationToken cancellationToken)
    {
        var session = Session(owner, treeId, sessionId);
        var singleThreadOption = ExecutionSingleThread(session, singleThread);
        var granularityOption = ExecutionGranularity(session, granularity);
        return ResumeAndWaitAsync(owner, treeId, sessionId, threadId, allThreadsResume: !singleThread, waitTimeout,
            (current, ct) => current.Protocol.SendAsync(DebugProtocolDescriptors.StepOutRequest,
                new StepOutArguments { ThreadId = threadId, SingleThread = singleThreadOption,
                    Granularity = granularityOption }, ct).AsTask(), cancellationToken);
    }

    public Task<DebugOperationResult> StepBackAsync(DebugTreeLookupScope owner, string treeId, string? sessionId, int threadId, bool singleThread, TimeSpan waitTimeout, CancellationToken cancellationToken)
        => StepBackAsync(owner, treeId, sessionId, threadId, singleThread, null, waitTimeout, cancellationToken);

    public Task<DebugOperationResult> StepBackAsync(DebugTreeLookupScope owner, string treeId, string? sessionId, int threadId,
        bool singleThread, DebugSteppingGranularity? granularity, TimeSpan waitTimeout, CancellationToken cancellationToken)
    {
        RequireCapability(owner, treeId, sessionId, x => x.SupportsStepBack == true, "step back");
        var current = Session(owner, treeId, sessionId);
        var singleThreadOption = ExecutionSingleThread(current, singleThread);
        var granularityOption = ExecutionGranularity(current, granularity);
        return ResumeAndWaitAsync(owner, treeId, sessionId, threadId, allThreadsResume: !singleThread, waitTimeout,
            (session, ct) => session.Protocol.SendAsync(DebugProtocolDescriptors.StepBackRequest,
                new StepBackArguments { ThreadId = threadId, SingleThread = singleThreadOption,
                    Granularity = granularityOption }, ct).AsTask(), cancellationToken);
    }

    public Task<DebugOperationResult> ReverseContinueAsync(DebugTreeLookupScope owner, string treeId, string? sessionId, int threadId, bool singleThread, TimeSpan waitTimeout, CancellationToken cancellationToken)
    {
        RequireCapability(owner, treeId, sessionId, x => x.SupportsStepBack == true, "reverse continue");
        var option = ExecutionSingleThread(Session(owner, treeId, sessionId), singleThread);
        return ResumeAndWaitAsync(owner, treeId, sessionId, threadId, allThreadsResume: !singleThread, waitTimeout,
            (session, ct) => session.Protocol.SendAsync(DebugProtocolDescriptors.ReverseContinueRequest,
                new ReverseContinueArguments { ThreadId = threadId, SingleThread = option }, ct).AsTask(), cancellationToken);
    }

    public Task<DebugOperationResult> GotoAsync(DebugTreeLookupScope owner, string treeId, string? sessionId, int threadId, string targetToken, TimeSpan waitTimeout, CancellationToken cancellationToken)
    {
        RequireCapability(owner, treeId, sessionId, x => x.SupportsGotoTargetsRequest == true, "goto");
        var session = AuthorizedSession(owner, treeId, sessionId, DebugTreeGrant.RoutineExecutionControl);
        var targetId = session.Projections.ResolveSuspensionToken(targetToken, "gotoTarget", out var targetThread, out _);
        if (targetThread != threadId)
            throw new DebugSemanticException(DebugSemanticFailureReason.ReferenceOwnerMismatch,
                "The goto target belongs to another debugger thread.");
        return ResumeAndWaitAsync(owner, treeId, sessionId, threadId, allThreadsResume: true, waitTimeout,
            (current, ct) => current.Protocol.SendAsync(DebugProtocolDescriptors.GotoRequest,
                new GotoArguments { ThreadId = threadId, TargetId = targetId }, ct).AsTask(), cancellationToken);
    }

    public Task<DebugOperationResult> RestartFrameAsync(DebugTreeLookupScope owner, string treeId, string? sessionId, string frameToken, TimeSpan waitTimeout, CancellationToken cancellationToken)
    {
        RequireCapability(owner, treeId, sessionId, x => x.SupportsRestartFrame == true, "restart frame");
        var session = AuthorizedSession(owner, treeId, sessionId, DebugTreeGrant.RoutineExecutionControl);
        var frameId = session.Projections.ResolveSuspensionToken(frameToken, "frame", out var threadId, out _);
        var stopped = RequireStoppedThread(session, threadId);
        if (session.Projections.FindStackFrame(threadId, stopped.SuspensionEpoch, frameId)?.CanRestart == false)
            throw new DebugSemanticException(DebugSemanticFailureReason.InvalidSessionState,
                "The selected stack frame cannot be restarted.");
        return ResumeAndWaitAsync(owner, treeId, sessionId, threadId, allThreadsResume: false, waitTimeout,
            (current, ct) => current.Protocol.SendAsync(DebugProtocolDescriptors.RestartFrameRequest,
                new RestartFrameArguments { FrameId = frameId }, ct).AsTask(), cancellationToken);
    }

    public async Task<DebugOperationResult> PauseAsync(DebugTreeLookupScope owner, string treeId, string? sessionId, int threadId, TimeSpan waitTimeout, CancellationToken cancellationToken)
    {
        var tree = manager.ResolveTree(owner, treeId);
        tree.RuntimeBinding.State.ThrowIfUnavailable();
        try { tree.Authorization.Demand(DebugTreeGrant.RoutineExecutionControl); }
        catch (UnauthorizedAccessException exception)
        {
            throw new DebugSemanticException(DebugSemanticFailureReason.PermissionDenied,
                "The debug operation requires routine execution-control authorization.", exception);
        }
        var session = tree.SelectSession(sessionId);
        var current = session.State.Threads.SingleOrDefault(x => x.ThreadId == threadId)
            ?? throw new KeyNotFoundException($"Debug thread '{threadId}' is unknown.");
        using var waiter = session.State.RegisterStopWaiter(threadId, current.ResumptionGeneration);
        await session.Protocol.SendAsync(DebugProtocolDescriptors.PauseRequest, new PauseArguments { ThreadId = threadId }, cancellationToken).ConfigureAwait(false);
        return await AwaitStopAsync(treeId, session, threadId, waiter, waitTimeout, cancellationToken).ConfigureAwait(false);
    }

    private async Task<DebugOperationResult> ResumeAndWaitAsync(
        DebugTreeLookupScope owner, string treeId, string? sessionId, int threadId,
        bool allThreadsResume, TimeSpan waitTimeout,
        Func<DebugSession, CancellationToken, Task> send, CancellationToken cancellationToken)
    {
        var tree = manager.ResolveTree(owner, treeId);
        tree.RuntimeBinding.State.ThrowIfUnavailable();
        try { tree.Authorization.Demand(DebugTreeGrant.RoutineExecutionControl); }
        catch (UnauthorizedAccessException exception)
        {
            throw new DebugSemanticException(DebugSemanticFailureReason.PermissionDenied,
                "The debug operation requires routine execution-control authorization.", exception);
        }
        var session = tree.SelectSession(sessionId);
        var transition = session.State.BeginResume(threadId, allThreadsResume);
        session.Projections.InvalidateForContinue(threadId, allThreadsResume);
        var generation = session.State.Threads.Single(x => x.ThreadId == threadId).ResumptionGeneration;
        using var waiter = session.State.RegisterStopWaiter(threadId, generation);
        try
        {
            await send(session, cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            session.State.TryRollbackResume(transition);
            throw;
        }
        return await AwaitStopAsync(treeId, session, threadId, waiter, waitTimeout, cancellationToken).ConfigureAwait(false);
    }

    private static async Task<DebugOperationResult> AwaitStopAsync(
        string treeId, DebugSession session, int threadId, DebugOutcomeWaitRegistration waiter,
        TimeSpan timeout, CancellationToken cancellationToken)
    {
        try
        {
            var stopped = await waiter.Task.WaitAsync(timeout, cancellationToken).ConfigureAwait(false);
            return new(treeId, session.SessionId, threadId, true, false, stopped);
        }
        catch (TimeoutException)
        {
            return new(treeId, session.SessionId, threadId, false, true,
                session.State.Threads.SingleOrDefault(x => x.ThreadId == threadId));
        }
        catch (DebugSessionEndedException ended)
        {
            return new(treeId, session.SessionId, threadId, false, false, null, ended.Status);
        }
    }

    private DebugSession Session(DebugTreeLookupScope owner, string treeId, string? sessionId)
        => manager.ResolveTree(owner, treeId).SelectSession(sessionId);

    private DebugSession AuthorizedSession(
        DebugTreeLookupScope owner, string treeId, string? sessionId, DebugTreeGrant grant)
    {
        var tree = manager.ResolveTree(owner, treeId);
        tree.RuntimeBinding.State.ThrowIfUnavailable();
        try { tree.Authorization.Demand(grant); }
        catch (UnauthorizedAccessException exception)
        {
            throw new DebugSemanticException(DebugSemanticFailureReason.PermissionDenied,
                $"The debug operation requires the '{grant}' grant.", exception);
        }
        var session = tree.SelectSession(sessionId);
        tree.Authorization.ValidateCurrent(tree.RuntimeBinding, session.AdapterPlan);
        return session;
    }

    private static DebugThreadSnapshot RequireStoppedThread(DebugSession session, int threadId)
        => session.State.Threads.SingleOrDefault(x => x.ThreadId == threadId && x.IsStopped)
            ?? throw new DebugSemanticException(DebugSemanticFailureReason.InvalidSessionState,
                $"Debug thread '{threadId}' is not stopped.");

    private static DebugThreadSnapshot RequireAnyStoppedThread(DebugSession session)
        => session.State.Threads.FirstOrDefault(x => x.IsStopped)
            ?? throw new DebugSemanticException(DebugSemanticFailureReason.InvalidSessionState,
                "The debug operation requires a stopped thread.");

    private static void ValidateMutationText(string value, int maximumBytes, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value) || System.Text.Encoding.UTF8.GetByteCount(value) > maximumBytes)
            throw new DebugSemanticException(DebugSemanticFailureReason.InvalidArguments,
                $"Debugger mutation input '{parameterName}' is empty or exceeds its byte limit.");
    }

    private static bool? ExecutionSingleThread(DebugSession session, bool requested)
    {
        if (!requested) return null;
        if (session.Capabilities?.SupportsSingleThreadExecutionRequests != true)
            throw new DebugSemanticException(DebugSemanticFailureReason.CapabilityUnavailable,
                "The adapter does not support single-thread execution requests.");
        return true;
    }

    private static SteppingGranularity? ExecutionGranularity(
        DebugSession session, DebugSteppingGranularity? requested)
    {
        if (requested is null) return null;
        if (session.Capabilities?.SupportsSteppingGranularity != true)
            throw new DebugSemanticException(DebugSemanticFailureReason.CapabilityUnavailable,
                "The adapter does not support stepping granularity.");
        return requested.Value switch
        {
            DebugSteppingGranularity.Statement => SteppingGranularity.Statement,
            DebugSteppingGranularity.Line => SteppingGranularity.Line,
            DebugSteppingGranularity.Instruction => SteppingGranularity.Instruction,
            _ => throw new DebugSemanticException(DebugSemanticFailureReason.InvalidArguments,
                "The requested stepping granularity is invalid.")
        };
    }

    private static DebugSemanticMutationResult MutationResult(
        DebugSession session, int threadId, int? frameId, string value, string? type,
        int? variablesReference, int? namedVariables, int? indexedVariables,
        string? memoryReference, int? valueLocationReference)
        => new(BoundUtf8(value, 64 * 1024), Bound(type, 1024),
            variablesReference is > 0
                ? session.Projections.CreateSuspensionToken(threadId, frameId, "variables", variablesReference.Value) : null,
            namedVariables, indexedVariables,
            string.IsNullOrWhiteSpace(memoryReference) ? null
                : threadId > 0
                    ? session.Projections.CreateSuspensionTextToken(threadId, frameId, "memory", memoryReference)
                    : session.Projections.CreateSessionTextToken("memory", memoryReference),
            valueLocationReference is > 0
                ? session.Projections.CreateSuspensionToken(threadId, frameId, "location", valueLocationReference.Value) : null,
            PriorVariableDerivedTokensInvalidated: true);

    private static string? Bound(string? value, int maximum)
        => value is null ? null : value[..Math.Min(value.Length, maximum)];

    private static string BoundUtf8(string value, int maximumBytes)
    {
        if (System.Text.Encoding.UTF8.GetByteCount(value) <= maximumBytes) return value;
        var low = 0;
        var high = value.Length;
        while (low < high)
        {
            var middle = low + ((high - low + 1) / 2);
            if (middle < value.Length && char.IsHighSurrogate(value[middle - 1]) && char.IsLowSurrogate(value[middle])) middle--;
            if (System.Text.Encoding.UTF8.GetByteCount(value.AsSpan(0, middle)) <= maximumBytes) low = middle;
            else high = middle - 1;
        }
        if (low > 0 && low < value.Length && char.IsHighSurrogate(value[low - 1]) && char.IsLowSurrogate(value[low])) low--;
        return value[..low];
    }

    private static DebugContinuationTokenContext ContinuationContext(
        DebugSessionTree tree, DebugSession session, string queryKind, string queryIdentity, long generation)
        => new(tree.Ownership.AgentRuntimeRegistrationId, tree.Ownership.DebugTreeId,
            session.SessionId, queryKind, queryIdentity, generation);

    private static string StableQueryHash(string value)
        => Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(
            System.Text.Encoding.UTF8.GetBytes(value)));

    private static void ValidateLocationRange(long line, long? column, long? endLine, long? endColumn)
    {
        if (line <= 0 || column is <= 0 || endLine is <= 0 || endColumn is <= 0 ||
            endLine is { } lastLine && lastLine < line ||
            endLine == line && column is { } firstColumn && endColumn is { } lastColumn && lastColumn < firstColumn)
            throw new DebugSemanticException(DebugSemanticFailureReason.InvalidArguments, "The debugger source range is invalid.");
    }

    private static void ValidateOffsetCount(long offset, long count, string operation)
    {
        try { _ = checked(offset + count); }
        catch (OverflowException exception)
        {
            throw new DebugSemanticException(DebugSemanticFailureReason.InvalidArguments,
                $"The {operation} range overflows.", exception);
        }
    }

    private static DebugSemanticExceptionDetails? MapExceptionDetails(
        ExceptionDetails? details, int depth, ExceptionBudget budget)
    {
        if (details is null) return null;
        if (depth >= 8 || !budget.TryTakeNode()) { budget.Truncated = true; return null; }
        string? Take(string? value, int maximum)
        {
            if (value is null) return null;
            var bounded = BoundUtf8(value, Math.Min(maximum, budget.RemainingBytes));
            budget.Take(System.Text.Encoding.UTF8.GetByteCount(bounded));
            if (bounded.Length != value.Length) budget.Truncated = true;
            return bounded;
        }
        var inner = new List<DebugSemanticExceptionDetails>();
        foreach (var child in details.InnerException ?? [])
        {
            if (budget.RemainingNodes <= 0 || budget.RemainingBytes <= 0) { budget.Truncated = true; break; }
            var mapped = MapExceptionDetails(child, depth + 1, budget);
            if (mapped is not null) inner.Add(mapped);
        }
        return new(Take(details.Message, 4096), Take(details.TypeName, 1024),
            Take(details.FullTypeName, 2048), Take(details.EvaluateName, 4096),
            Take(details.StackTrace, 16 * 1024), inner);
    }

    private void RequireCapability(DebugTreeLookupScope owner, string treeId, string? sessionId, Func<Capabilities, bool> predicate, string operation)
    {
        var capabilities = Session(owner, treeId, sessionId).Capabilities;
        if (capabilities is null || !predicate(capabilities))
            throw new DebugSemanticException(DebugSemanticFailureReason.CapabilityUnavailable,
                $"The adapter does not support {operation}.");
    }

    private sealed class ExceptionBudget(int bytes, int nodes)
    {
        public int RemainingBytes { get; private set; } = bytes;
        public int RemainingNodes { get; private set; } = nodes;
        public bool Truncated { get; set; }
        public bool TryTakeNode()
        {
            if (RemainingNodes <= 0) return false;
            RemainingNodes--;
            return true;
        }
        public void Take(int bytesUsed) => RemainingBytes = Math.Max(0, RemainingBytes - bytesUsed);
    }
}
