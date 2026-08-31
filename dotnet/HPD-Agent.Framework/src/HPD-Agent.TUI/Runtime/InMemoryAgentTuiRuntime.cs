 using System.Runtime.CompilerServices;
using System.Threading.Channels;
using HPD.Events;

namespace HPD.Agent.TUI.Runtime;

public sealed class InMemoryAgentTuiRuntime : IHpdAgentTuiRuntime, IAgentTuiSessionThreadRuntime, IAgentTuiAgentRuntime, IAsyncDisposable
{
    private readonly Agent _agent;
    private readonly AgentTuiRuntimeScope _defaultScope;
    private readonly object _gate = new();
    private AgentTuiThreadExecution? _activeExecution;
    private string? _reservedExecutionId;

    public InMemoryAgentTuiRuntime(
        Agent agent,
        AgentTuiRuntimeScope? defaultScope = null)
    {
        _agent = agent ?? throw new ArgumentNullException(nameof(agent));
        _defaultScope = defaultScope ?? new AgentTuiRuntimeScope(
            _agent.AgentId,
            "local-session",
            "main");
    }

    public bool CanSwitchAgents => false;

    public async Task<AgentTuiScopeResolution> ResolveInitialScopeAsync(
        AgentTuiRuntimeScope? requested,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var scope = requested ?? _defaultScope;
        var store = _agent.Config?.SessionStore;
        if (store is not null &&
            await store.LoadSessionAsync(scope.SessionId, cancellationToken).ConfigureAwait(false) is not null &&
            await store.GetThreadAsync(new ThreadKey(scope.SessionId, scope.ThreadId), cancellationToken).ConfigureAwait(false) is not null)
        {
            return new AgentTuiScopeResolution(scope, IsDurable: true);
        }

        return new AgentTuiScopeResolution(scope, IsDurable: store is null);
    }

    public async Task<AgentTuiRuntimeScope> EnsureDurableScopeAsync(
        AgentTuiRuntimeScope scope,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var store = _agent.Config?.SessionStore;
        if (store is not null &&
            await store.LoadSessionAsync(scope.SessionId, cancellationToken).ConfigureAwait(false) is null)
        {
            await _agent.CreateSessionAsync(scope.SessionId, cancellationToken: cancellationToken)
                .ConfigureAwait(false);
        }
        if (store is not null &&
            await store.GetThreadAsync(new ThreadKey(scope.SessionId, scope.ThreadId), cancellationToken).ConfigureAwait(false) is null)
        {
            await _agent.CreateThreadAsync(
                    scope.SessionId,
                    scope.ThreadId,
                    cancellationToken: cancellationToken)
                .ConfigureAwait(false);
        }

        return scope;
    }

    public async Task<IReadOnlyList<AgentTuiAgentInfo>> ListAgentsAsync(
        CancellationToken cancellationToken = default)
    {
        var store = _agent.Config?.AgentStore;
        if (store is null)
        {
            return [];
        }

        var ids = await store.ListIdsAsync(cancellationToken).ConfigureAwait(false);
        var agents = new List<AgentTuiAgentInfo>(ids.Count);
        foreach (var id in ids)
        {
            var stored = await store.LoadAsync(id, cancellationToken).ConfigureAwait(false);
            if (stored is not null)
            {
                agents.Add(ToAgentInfo(stored));
            }
        }

        return agents
            .OrderBy(static agent => agent.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(static agent => agent.Id, StringComparer.Ordinal)
            .ToArray();
    }

    public async Task<AgentTuiAgentInfo?> GetAgentAsync(
        string agentId,
        CancellationToken cancellationToken = default)
    {
        var stored = _agent.Config?.AgentStore is { } store
            ? await store.LoadAsync(agentId, cancellationToken).ConfigureAwait(false)
            : null;
        return stored is null ? null : ToAgentInfo(stored);
    }

    public async Task<AgentTuiAgentInfo> CreateAgentAsync(
        AgentTuiCreateAgentRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var store = _agent.Config?.AgentStore
            ?? throw new InvalidOperationException("No agent store configured.");
        var id = string.IsNullOrWhiteSpace(request.Config.AgentId)
            ? Guid.NewGuid().ToString("N")
            : request.Config.AgentId;
        if (await store.LoadAsync(id, cancellationToken).ConfigureAwait(false) is not null)
        {
            throw new InvalidOperationException($"Agent '{id}' already exists.");
        }

        var now = DateTime.UtcNow;
        var stored = new StoredAgent
        {
            Id = id,
            Name = request.Name,
            Config = request.Config,
            CreatedAt = now,
            UpdatedAt = now,
            Metadata = ToObjectDictionary(request.Metadata)
        };
        await store.SaveAsync(stored, cancellationToken).ConfigureAwait(false);
        return ToAgentInfo(stored);
    }

    public async Task<AgentTuiAgentInfo> UpdateAgentAsync(
        string agentId,
        AgentTuiUpdateAgentRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var store = _agent.Config?.AgentStore
            ?? throw new InvalidOperationException("No agent store configured.");
        var stored = await store.LoadAsync(agentId, cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException($"Agent '{agentId}' was not found.");
        stored.Config = request.Config;
        stored.UpdatedAt = DateTime.UtcNow;
        await store.SaveAsync(stored, cancellationToken).ConfigureAwait(false);
        return ToAgentInfo(stored);
    }

    public async Task DeleteAgentAsync(
        string agentId,
        CancellationToken cancellationToken = default)
    {
        var store = _agent.Config?.AgentStore
            ?? throw new InvalidOperationException("No agent store configured.");
        await store.DeleteAsync(agentId, cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<AgentTuiSessionInfo>> ListSessionsAsync(
        CancellationToken cancellationToken = default)
    {
        var store = _agent.Config?.SessionStore;
        if (store is null)
        {
            return [];
        }

        var ids = await store.ListSessionIdsAsync(cancellationToken).ConfigureAwait(false);
        var sessions = new List<AgentTuiSessionInfo>(ids.Count);
        foreach (var id in ids)
        {
            var session = await store.LoadSessionAsync(id, cancellationToken).ConfigureAwait(false);
            if (session is null)
            {
                continue;
            }

            sessions.Add(ToSessionInfo(session));
        }

        return sessions
            .OrderByDescending(static session => session.LastActivity)
            .ToArray();
    }

    public async Task<IReadOnlyList<AgentTuiSessionInfo>> SearchSessionsAsync(
        AgentTuiSessionSearch? search = null,
        CancellationToken cancellationToken = default)
    {
        search ??= new AgentTuiSessionSearch();
        var sessions = await ListSessionsAsync(cancellationToken).ConfigureAwait(false);
        var filtered = search.Metadata is { Count: > 0 }
            ? sessions.Where(session => MatchesMetadata(session.Metadata, search.Metadata))
            : sessions;

        return filtered
            .Skip(Math.Max(0, search.Offset))
            .Take(Math.Max(0, search.Limit))
            .ToArray();
    }

    public async Task<AgentTuiSessionInfo?> GetSessionAsync(
        string sessionId,
        CancellationToken cancellationToken = default)
    {
        var session = _agent.Config?.SessionStore is { } store
            ? await store.LoadSessionAsync(sessionId, cancellationToken).ConfigureAwait(false)
            : null;
        return session is null ? null : ToSessionInfo(session);
    }

    public async Task<AgentTuiSessionInfo> CreateSessionAsync(
        string? sessionId = null,
        string? title = null,
        CancellationToken cancellationToken = default)
    {
        var metadata = string.IsNullOrWhiteSpace(title)
            ? null
            : new Dictionary<string, object> { ["title"] = title };
        var id = await _agent.CreateSessionAsync(sessionId, metadata, cancellationToken)
            .ConfigureAwait(false);
        return await GetSessionAsync(id, cancellationToken).ConfigureAwait(false)
            ?? new AgentTuiSessionInfo(id, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, title);
    }

    public async Task RenameSessionAsync(
        string sessionId,
        string title,
        CancellationToken cancellationToken = default)
    {
        await UpdateSessionAsync(
                sessionId,
                new AgentTuiSessionUpdate(new Dictionary<string, object?> { ["title"] = title }),
                cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<AgentTuiSessionInfo> UpdateSessionAsync(
        string sessionId,
        AgentTuiSessionUpdate update,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(update);

        var store = _agent.Config?.SessionStore
            ?? throw new InvalidOperationException("No session store configured.");
        var session = await store.LoadSessionAsync(sessionId, cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException($"Session '{sessionId}' was not found.");
        ApplyMetadata(session.Metadata, update.Metadata);
        session.LastActivity = DateTime.UtcNow;
        await store.SaveSessionAsync(session, cancellationToken).ConfigureAwait(false);
        return ToSessionInfo(session);
    }

    public async Task DeleteSessionAsync(
        string sessionId,
        CancellationToken cancellationToken = default)
    {
        await _agent.DeleteSessionAsync(sessionId, cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<AgentTuiThreadInfo>> ListThreadsAsync(
        string sessionId,
        CancellationToken cancellationToken = default)
    {
        var store = _agent.Config?.SessionStore;
        if (store is null)
        {
            return [];
        }

        var descriptors = await store.CollectThreadDescriptorsAsync(
            sessionId,
            cancellationToken: cancellationToken).ConfigureAwait(false);
        var threads = new List<AgentTuiThreadInfo>(descriptors.Count);
        foreach (var descriptor in descriptors)
        {
            var thread = await store.ProjectThreadAsync(
                sessionId,
                descriptor.Key.ThreadId,
                ThreadProjectionPurpose.ThreadHistory,
                cancellationToken).ConfigureAwait(false);
            if (thread is not null)
            {
                threads.Add(ToThreadInfo(thread, sessionId));
            }
        }

        return threads
            .OrderByDescending(static thread => thread.LastActivity)
            .ThenBy(static thread => thread.Id, StringComparer.Ordinal)
            .ToArray();
    }

    public async Task<AgentTuiThreadInfo?> GetThreadAsync(
        string sessionId,
        string threadId,
        CancellationToken cancellationToken = default)
    {
        var thread = _agent.Config?.SessionStore is { } store
            ? await store.ProjectThreadAsync(sessionId, threadId, ThreadProjectionPurpose.ThreadHistory, cancellationToken).ConfigureAwait(false)
            : null;
        return thread is null ? null : ToThreadInfo(thread, sessionId);
    }

    public async Task<AgentTuiThreadInfo> CreateThreadAsync(
        string agentId,
        string sessionId,
        string? threadId = null,
        string? name = null,
        CancellationToken cancellationToken = default)
        => await CreateThreadAsync(
                agentId,
                sessionId,
                new AgentTuiCreateThreadRequest(threadId, name),
                cancellationToken)
            .ConfigureAwait(false);

    public async Task<AgentTuiThreadInfo> CreateThreadAsync(
        string agentId,
        string sessionId,
        AgentTuiCreateThreadRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var id = await _agent.CreateThreadAsync(sessionId, request.ThreadId, request.Name, cancellationToken)
            .ConfigureAwait(false);
        var store = _agent.Config?.SessionStore
            ?? throw new InvalidOperationException("No session store configured.");
        var thread = await store.ProjectThreadAsync(sessionId, id, ThreadProjectionPurpose.ThreadHistory, cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException($"Thread '{id}' was not found after creation.");

        ApplyThreadUpdate(thread, new AgentTuiThreadUpdate(
            request.Name,
            request.Description,
            request.Tags,
            request.Metadata));
        await store.AppendThreadUpdatedAsync(thread, cancellationToken).ConfigureAwait(false);
        return ToThreadInfo(thread, sessionId);
    }

    public async Task<AgentTuiThreadForkInfo> ForkThreadAsync(
        string agentId,
        string sessionId,
        string sourceThreadId,
        AgentTuiForkThreadRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var id = string.IsNullOrWhiteSpace(request.NewThreadId)
            ? Guid.NewGuid().ToString("N")[..12]
            : request.NewThreadId;
        var result = await _agent.ForkThreadAsync(
                sessionId,
                sourceThreadId,
                id,
                request.FromMessageId,
                new ThreadForkOptions
                {
                    Metadata = ToObjectDictionary(request.Metadata),
                    SubAgents = request.SubAgents
                },
                cancellationToken)
            .ConfigureAwait(false);
        var store = _agent.Config?.SessionStore
            ?? throw new InvalidOperationException("No session store configured.");
        var thread = await store.ProjectThreadAsync(sessionId, result.Target.ThreadId, ThreadProjectionPurpose.ThreadHistory, cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException($"Thread '{result.Target.ThreadId}' was not found after fork.");

        ApplyThreadUpdate(thread, new AgentTuiThreadUpdate(
            request.Name,
            request.Description,
            request.Tags,
            request.Metadata));
        await store.AppendThreadUpdatedAsync(thread, cancellationToken).ConfigureAwait(false);
        return new AgentTuiThreadForkInfo(
            result.OperationId,
            ToThreadInfo(thread, sessionId),
            result.SourceBoundary.Generation,
            result.SourceBoundary.SequenceNumber,
            result.SubAgentPolicy,
            result.Status,
            result.Children);
    }

    public async Task<AgentTuiThreadInfo> UpdateThreadAsync(
        string sessionId,
        string threadId,
        AgentTuiThreadUpdate update,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(update);

        var store = _agent.Config?.SessionStore
            ?? throw new InvalidOperationException("No session store configured.");
        var thread = await store.ProjectThreadAsync(sessionId, threadId, ThreadProjectionPurpose.ThreadHistory, cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException($"Thread '{threadId}' was not found.");
        ApplyThreadUpdate(thread, update);
        await store.AppendThreadUpdatedAsync(thread, cancellationToken).ConfigureAwait(false);
        return ToThreadInfo(thread, sessionId);
    }

    public async Task<AgentTuiThreadGraph> GetThreadGraphAsync(
        string sessionId,
        CancellationToken cancellationToken = default)
    {
        var store = _agent.Config?.SessionStore;
        if (store is null)
        {
            return new AgentTuiThreadGraph([], [], []);
        }

        var descriptors = await store.CollectThreadDescriptorsAsync(
            sessionId,
            cancellationToken: cancellationToken).ConfigureAwait(false);
        var threads = new List<Thread>(descriptors.Count);
        foreach (var descriptor in descriptors)
        {
            var thread = await store.ProjectThreadAsync(
                sessionId,
                descriptor.Key.ThreadId,
                ThreadProjectionPurpose.ThreadHistory,
                cancellationToken).ConfigureAwait(false);
            if (thread is not null)
            {
                threads.Add(thread);
            }
        }

        return new AgentTuiThreadGraph(
            threads
                .Select(thread => ToThreadInfo(thread, sessionId))
                .OrderByDescending(static thread => thread.LastActivity)
                .ThenBy(static thread => thread.Id, StringComparer.Ordinal)
                .ToArray(),
            BuildForkGroups(threads),
            BuildRuntimeChildren(threads));
    }

    public async Task DeleteThreadAsync(
        string sessionId,
        string threadId,
        bool recursive = false,
        CancellationToken cancellationToken = default)
    {
        if (recursive)
        {
            throw new NotSupportedException("Recursive thread deletion is not supported by the in-memory TUI runtime.");
        }

        await _agent.DeleteThreadAsync(sessionId, threadId, cancellationToken).ConfigureAwait(false);
    }

    private static AgentTuiSessionInfo ToSessionInfo(Session session)
        => new(
            session.Id,
            session.CreatedAt,
            session.LastActivity,
            GetMetadataString(session.Metadata, "title"),
            session.Metadata.ToDictionary(
                static pair => pair.Key,
                static pair => (object?)pair.Value,
                StringComparer.Ordinal));

    private static AgentTuiAgentInfo ToAgentInfo(StoredAgent agent)
        => new(
            agent.Id,
            agent.Name,
            agent.CreatedAt,
            agent.UpdatedAt,
            agent.Metadata?.ToDictionary(
                static pair => pair.Key,
                static pair => (object?)pair.Value,
                StringComparer.Ordinal),
            agent.Config);

    private static AgentTuiThreadInfo ToThreadInfo(Thread thread, string sessionId)
        => new(
            thread.Id,
            sessionId,
            thread.DefaultAgentId,
            thread.GetDisplayName(),
            thread.Description,
            thread.CreatedAt,
            thread.LastActivity,
            thread.MessageCount,
            thread.ForkedFrom,
            thread.ForkedAtMessageId,
            thread.ForkedAtMessageIndex,
            thread.TotalForks,
            thread.Tags?.ToArray(),
            thread.Ancestors?.ToDictionary(
                static pair => pair.Key,
                static pair => pair.Value,
                StringComparer.Ordinal),
            thread.Kind,
            thread.Visibility,
            thread.ParentSessionId,
            thread.ParentThreadId,
            thread.SubAgentName,
            thread.InvocationId,
            thread.SubAgentSourceKind,
            thread.ParentToolCallId,
            thread.ContextPolicy,
            thread.Metadata.ToDictionary(
                static pair => pair.Key,
                static pair => (object?)pair.Value,
                StringComparer.Ordinal));

    private static IReadOnlyList<AgentTuiThreadForkGroup> BuildForkGroups(IReadOnlyList<Thread> threads)
    {
        return ThreadForkGraph.BuildVisibleForkGroups(threads)
            .Select(ToForkGroup)
            .ToArray();
    }

    private static IReadOnlyList<AgentTuiThreadRuntimeChild> BuildRuntimeChildren(IReadOnlyList<Thread> threads)
    {
        return threads
            .Where(IsRuntimeChildThread)
            .OrderBy(static thread => thread.ParentThreadId, StringComparer.Ordinal)
            .ThenBy(static thread => thread.CreatedAt)
            .ThenBy(static thread => thread.Id, StringComparer.Ordinal)
            .Select(ToRuntimeChild)
            .ToArray();
    }

    private static bool IsVisibleBranchThread(Thread thread) =>
        thread.Kind == ThreadKind.MainAgent &&
        thread.Visibility == ThreadVisibility.Visible;

    private static bool IsRuntimeChildThread(Thread thread) =>
        !string.IsNullOrWhiteSpace(thread.ParentThreadId) ||
        thread.Kind != ThreadKind.MainAgent ||
        thread.Visibility == ThreadVisibility.Hidden;

    private static AgentTuiThreadRuntimeChild ToRuntimeChild(Thread thread)
        => new(
            thread.Id,
            thread.SessionId,
            thread.DefaultAgentId,
            thread.ParentSessionId ?? thread.SessionId,
            thread.ParentThreadId ?? thread.ForkedFrom ?? string.Empty,
            thread.GetDisplayName(),
            thread.Kind,
            thread.Visibility,
            thread.SubAgentName,
            thread.InvocationId,
            thread.SubAgentSourceKind,
            thread.ParentToolCallId,
            thread.ContextPolicy,
            thread.SubAgentStatus,
            thread.MessageCount,
            thread.CreatedAt,
            thread.LastActivity);

    private static AgentTuiThreadForkGroup ToForkGroup(ThreadForkGroup group)
        => new(
            group.Id,
            group.SourceThreadId,
            group.ForkedAtMessageId,
            group.ForkedAtMessageIndex,
            group.ChoiceMessageIndex,
            group.Members.Select(ToForkGroupMember).ToArray());

    private static AgentTuiThreadForkGroupMember ToForkGroupMember(
        ThreadForkGroupMember member)
        => new(
            member.Thread.Id,
            member.Thread.GetDisplayName(),
            member.Index,
            member.IsSource,
            member.ChoiceMessageId,
            member.ChoiceMessageIndex,
            member.Thread.MessageCount,
            member.Thread.CreatedAt,
            member.Thread.LastActivity);

    private static string? GetMetadataString(
        IReadOnlyDictionary<string, object> metadata,
        string key)
        => metadata.TryGetValue(key, out var value) ? value?.ToString() : null;

    private static Dictionary<string, object>? ToObjectDictionary(
        IReadOnlyDictionary<string, object?>? values)
        => values is null
            ? null
            : values
                .Where(static pair => pair.Value is not null)
                .ToDictionary(
                    static pair => pair.Key,
                    static pair => pair.Value!,
                    StringComparer.Ordinal);

    private static void ApplyMetadata(
        Dictionary<string, object> target,
        IReadOnlyDictionary<string, object?>? update)
    {
        if (update is null)
        {
            return;
        }

        foreach (var pair in update)
        {
            if (pair.Value is null)
            {
                target.Remove(pair.Key);
            }
            else
            {
                target[pair.Key] = pair.Value;
            }
        }
    }

    private static void ApplyThreadUpdate(
        Thread thread,
        AgentTuiThreadUpdate update)
    {
        if (update.Name is not null)
        {
            thread.Name = update.Name;
        }

        if (update.Description is not null)
        {
            thread.Description = update.Description;
        }

        if (update.Tags is not null)
        {
            thread.Tags = update.Tags.ToList();
        }

        ApplyMetadata(thread.Metadata, update.Metadata);
        thread.LastActivity = DateTime.UtcNow;
    }

    private static bool MatchesMetadata(
        IReadOnlyDictionary<string, object?>? source,
        IReadOnlyDictionary<string, object?> required)
    {
        if (source is null)
        {
            return false;
        }

        foreach (var pair in required)
        {
            if (!source.TryGetValue(pair.Key, out var value) ||
                !Equals(value?.ToString(), pair.Value?.ToString()))
            {
                return false;
            }
        }

        return true;
    }

    public async IAsyncEnumerable<AgentTuiEventBatch> ObserveAsync(
        AgentTuiRuntimeScope scope,
        ThreadJournalCursor after,
        ThreadJournalCursor initialObservedCursor,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var cursor = after;
        var catchUpMode = after.SequenceNumber == 0
            ? AgentTuiEventDeliveryMode.Historical
            : AgentTuiEventDeliveryMode.CatchUp;
        var store = _agent.Config?.SessionStore;
        if (store is null)
        {
            var signals = Channel.CreateUnbounded<AgentEvent>();
            using var subscription = _agent.SubscribeAny(evt =>
            {
                signals.Writer.TryWrite(evt);
                return ValueTask.CompletedTask;
            });
            await foreach (var evt in signals.Reader.ReadAllAsync(cancellationToken).ConfigureAwait(false))
            {
                if (IsInScope(evt, scope))
                {
                    yield return CreateDeliveryBatch([evt], AgentTuiEventDeliveryMode.Live, initialObservedCursor, 0);
                }
            }

            yield break;
        }

        var liveSignals = Channel.CreateUnbounded<AgentEvent>();
        using var liveSubscription = _agent.SubscribeAny(evt =>
        {
            liveSignals.Writer.TryWrite(evt);
            return ValueTask.CompletedTask;
        });

        var thread = new ThreadKey(scope.SessionId, scope.ThreadId);
        var head = await store.GetThreadEventHeadAsync(thread, cancellationToken).ConfigureAwait(false);
        if (head is null)
            yield break;

        await foreach (var batch in store.ReadThreadEventsAsync(
            thread,
            new ThreadEventReadRequest(cursor, head.ThreadSequenceNumber),
            cancellationToken).ConfigureAwait(false))
        {
            var catchUp = batch.Events
                .Where(evt => evt.ThreadSequenceNumber <= initialObservedCursor.SequenceNumber)
                .ToArray();
            if (catchUp.Length > 0)
            {
                yield return CreateDeliveryBatch(catchUp, catchUpMode, initialObservedCursor, batch.Generation);
                cursor = new ThreadJournalCursor(batch.Generation, catchUp[^1].ThreadSequenceNumber);
            }

            var live = batch.Events
                .Where(evt => evt.ThreadSequenceNumber > initialObservedCursor.SequenceNumber)
                .ToArray();
            if (live.Length > 0)
            {
                yield return CreateDeliveryBatch(live, AgentTuiEventDeliveryMode.Live, initialObservedCursor, batch.Generation);
                cursor = new ThreadJournalCursor(batch.Generation, live[^1].ThreadSequenceNumber);
            }
        }

        await foreach (var evt in liveSignals.Reader.ReadAllAsync(cancellationToken).ConfigureAwait(false))
        {
            if (!IsInScope(evt, scope))
                continue;
            var selectedThread = StringComparer.Ordinal.Equals(evt.SessionId, scope.SessionId) &&
                StringComparer.Ordinal.Equals(evt.ThreadId, scope.ThreadId);
            if (evt.ThreadSequenceNumber > 0 && selectedThread)
            {
                var liveHead = await store.GetThreadEventHeadAsync(thread, cancellationToken).ConfigureAwait(false);
                if (liveHead is null)
                    yield break;
                if (liveHead.Generation != head.Generation)
                {
                    throw new ThreadJournalReplacedException(
                        thread,
                        new ThreadJournalCursor(head.Generation, cursor.SequenceNumber),
                        ThreadJournalCursor.Start(liveHead.Generation));
                }
            }
            if (evt.ThreadSequenceNumber > 0 && selectedThread &&
                evt.ThreadSequenceNumber <= head.ThreadSequenceNumber)
                continue;

            var first = evt.ThreadSequenceNumber > 0 && selectedThread
                ? new ThreadJournalCursor(head.Generation, evt.ThreadSequenceNumber)
                : cursor;
            yield return new AgentTuiEventBatch(
                [evt],
                AgentTuiEventDeliveryMode.Live,
                initialObservedCursor,
                first,
                first);
            if (evt.ThreadSequenceNumber > 0 && selectedThread)
                cursor = first;
        }
    }

    public async Task<AgentTuiSubmitResult> SubmitInputAsync(
        AgentTuiRuntimeScope scope,
        AgentInputEvent input,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(scope);
        ArgumentNullException.ThrowIfNull(input);

        var registration = AgentInputDispatcher.GetBuiltInRegistration(input.GetType());
        if (registration.RoutingClass == AgentInputRoutingClass.ActiveControl)
        {
            var scopedControl = input with
            {
                AgentId = scope.AgentId,
                SessionId = scope.SessionId,
                ThreadId = scope.ThreadId
            };
            var result = await _agent.RunAsync(scopedControl, cancellationToken).ConfigureAwait(false);
            var (disposition, controlExecutionId) = result switch
            {
                AgentInputResult.Control control => (control.Disposition, control.ThreadExecutionId),
                AgentInputResult.Steered steered => (AgentInputDisposition.Accepted, steered.ThreadExecutionId),
                AgentInputResult.Completed completed => (AgentInputDisposition.Completed, completed.ThreadExecutionId),
                _ => throw new InvalidOperationException($"Unsupported input result '{result.GetType().Name}'.")
            };
            AgentTuiThreadExecution? current;
            lock (_gate)
                current = _activeExecution;
            return new AgentTuiSubmitResult(disposition, controlExecutionId, current);
        }

        var executionId = input.ThreadExecutionId ?? Guid.NewGuid().ToString("N");
        var startedAt = DateTimeOffset.UtcNow;
        var scopedInput = input with
        {
            AgentId = scope.AgentId,
            SessionId = scope.SessionId,
            ThreadId = scope.ThreadId,
            ThreadExecutionId = executionId
        };

        if (!TryReserveExecution(executionId))
        {
            throw new InvalidOperationException(
                $"Thread '{scope.ThreadId}' in session '{scope.SessionId}' already has an active execution.");
        }

        var startCommitted = false;
        try
        {
            await _agent.StartAsync(input.RunConfig, CancellationToken.None).ConfigureAwait(false);
            await PublishRuntimeEventAsync(new ThreadExecutionStartedEvent(executionId, scope.AgentId, startedAt)
            {
                SessionId = scope.SessionId,
                ThreadId = scope.ThreadId
            }, cancellationToken).ConfigureAwait(false);
            startCommitted = true;

            var activeExecution = new AgentTuiThreadExecution(
                executionId,
                scope.AgentId,
                scope.SessionId,
                scope.ThreadId,
                "active",
                startedAt);
            if (!ActivateReservedExecution(activeExecution))
                throw new InvalidOperationException($"Thread execution '{executionId}' lost its reserved ownership before activation.");

            var submission = await _agent.SubmitRuntimeInputAsync(scopedInput, CancellationToken.None).ConfigureAwait(false);
            _ = CompleteSubmittedInputAsync(scope, submission, executionId);
            return new AgentTuiSubmitResult(AgentInputDisposition.Queued, executionId, activeExecution);
        }
        catch (Exception ex)
        {
            if (startCommitted)
            {
                await PublishRuntimeEventAsync(new ThreadExecutionFinishedEvent(
                    executionId,
                    scope.AgentId,
                    ex is OperationCanceledException
                        ? ThreadExecutionOutcome.Cancelled
                        : ThreadExecutionOutcome.Failed,
                    DateTimeOffset.UtcNow,
                    ex is OperationCanceledException
                        ? null
                        : new ThreadExecutionError(ex.GetType().Name, ex.Message))
                {
                    SessionId = scope.SessionId,
                    ThreadId = scope.ThreadId
                }, CancellationToken.None).ConfigureAwait(false);
            }

            ReleaseExecution(executionId);
            throw;
        }
    }

    public Task<AgentTuiSubmitResult> CancelExecutionAsync(
        AgentTuiRuntimeScope scope,
        string threadExecutionId,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var control = _agent.CancelRuntimeExecution(threadExecutionId);
        AgentTuiThreadExecution? active;
        lock (_gate)
            active = _activeExecution;
        return Task.FromResult(new AgentTuiSubmitResult(
            control.Disposition,
            control.ThreadExecutionId,
            active));
    }

    private async Task CompleteSubmittedInputAsync(
        AgentTuiRuntimeScope scope,
        RuntimeInputReceipt submission,
        string executionId)
    {
        try
        {
            var outcome = await submission.Completion.ConfigureAwait(false);

            await PublishRuntimeEventAsync(new ThreadExecutionFinishedEvent(
                executionId,
                scope.AgentId,
                outcome.Error is not null
                    ? ThreadExecutionOutcome.Failed
                    : outcome.Cancelled
                        ? ThreadExecutionOutcome.Cancelled
                        : ThreadExecutionOutcome.Succeeded,
                DateTimeOffset.UtcNow,
                outcome.Error is null
                    ? null
                    : new ThreadExecutionError(outcome.Error.GetType().Name, outcome.Error.Message))
            {
                SessionId = scope.SessionId,
                ThreadId = scope.ThreadId
            }, CancellationToken.None).ConfigureAwait(false);

            ReleaseExecution(executionId);
            submission.Dispose();
        }
        catch
        {
            submission.Dispose();
            // A missing terminal commit must leave ownership visible instead of presenting
            // an unjournaled completion as authoritative state.
        }
    }

    public async Task<ThreadContextUsage> EstimateContextUsageAsync(
        AgentTuiRuntimeScope scope,
        AgentRunConfig? runConfig = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(scope);

        var store = _agent.Config?.SessionStore;
        var thread = store is null
            ? null
            : await store.ProjectThreadAsync(
                    scope.SessionId,
                    scope.ThreadId,
                    ThreadProjectionPurpose.ModelContext,
                    cancellationToken)
                .ConfigureAwait(false);
        if (thread is null)
        {
            return new ThreadContextUsage
            {
                SessionId = scope.SessionId,
                ThreadId = scope.ThreadId,
                Source = "thread-not-found"
            };
        }

        var estimator = new ThreadContextUsageEstimator();
        return await estimator.EstimateAsync(thread, runConfig ?? new AgentRunConfig(), cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<AgentRespondResult> AnswerRequestAsync(
        AgentTuiRuntimeScope scope,
        AgentEvent response,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(response);

        if (response is not IAgentResponseEvent responseEvent)
        {
            throw new ArgumentException("Response event must implement IAgentResponseEvent.", nameof(response));
        }

        return await _agent.TryAnswerRequestAsync(responseEvent, cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<AgentTuiThreadState> GetThreadStateAsync(
        AgentTuiRuntimeScope scope,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(scope);

        var store = _agent.Config?.SessionStore;
        if (store is null)
        {
            return new AgentTuiThreadState(default, GetActiveExecution(), GetLivePendingRequests());
        }

        var head = await store.GetThreadEventHeadAsync(
            new ThreadKey(scope.SessionId, scope.ThreadId), cancellationToken).ConfigureAwait(false);
        if (head is null)
        {
            throw new InvalidOperationException(
                $"Thread '{scope.SessionId}/{scope.ThreadId}' does not have a durable journal.");
        }

        var activeExecution = GetActiveExecution();
        var journal = await store.CollectThreadEventsAsync(
            new ThreadKey(scope.SessionId, scope.ThreadId), cancellationToken).ConfigureAwait(false) ?? [];
        return new AgentTuiThreadState(
            head.Cursor,
            activeExecution,
            AgentRequestProjector.ProjectPending(journal, activeExecution?.ThreadExecutionId));
    }

    private IReadOnlyList<AgentEvent> GetLivePendingRequests()
        => _agent.EventCoordinator.GetPendingRequests()
            .Select(item => item.Request)
            .OfType<AgentEvent>()
            .ToArray();

    public ValueTask DisposeAsync()
    {
        return ValueTask.CompletedTask;
    }

    private AgentTuiThreadExecution? GetActiveExecution()
    {
        lock (_gate)
        {
            if (_activeExecution is not { } active)
                return null;
            var operations = _agent.ListOperations()
                .Where(operation =>
                    operation.Address.SessionId == active.SessionId &&
                    operation.Address.ThreadId == active.ThreadId &&
                    (operation.OriginatingThreadExecutionId is null ||
                     operation.OriginatingThreadExecutionId == active.ThreadExecutionId))
                .Select(ToTuiOperation)
                .ToArray();
            return active with { Operations = operations };
        }
    }

    private static AgentTuiOperation ToTuiOperation(AgentOperationSnapshot operation) => new(
        operation.OperationId,
        operation.ProviderOperationId,
        operation.Name,
        operation.SourceKind.ToString().ToLowerInvariant(),
        operation.ProviderStatus.ToString().ToLowerInvariant(),
        operation.ObservationStatus.ToString().ToLowerInvariant(),
        operation.Control.Kind.ToString().ToLowerInvariant(),
        operation.Control.Capabilities.ToString().ToLowerInvariant(),
        operation.Control.HandleId,
        operation.Version,
        operation.RegisteredAt,
        operation.StartedAt,
        operation.UpdatedAt,
        operation.FinishedAt,
        operation.Completion?.Summary,
        operation.Completion?.ArtifactReferences,
        operation.Failure?.Code,
        operation.Failure?.Message,
        operation.Metadata);

    private bool TryReserveExecution(string executionId)
    {
        lock (_gate)
        {
            if (_activeExecution is not null || _reservedExecutionId is not null)
            {
                return false;
            }

            _reservedExecutionId = executionId;
            return true;
        }
    }

    private bool ActivateReservedExecution(AgentTuiThreadExecution activeExecution)
    {
        lock (_gate)
        {
            if (!string.Equals(_reservedExecutionId, activeExecution.ThreadExecutionId, StringComparison.Ordinal) || _activeExecution is not null)
                return false;

            _reservedExecutionId = null;
            _activeExecution = activeExecution;
            return true;
        }
    }

    private void ReleaseExecution(string executionId)
    {
        lock (_gate)
        {
            if (string.Equals(_reservedExecutionId, executionId, StringComparison.Ordinal))
                _reservedExecutionId = null;
            if (string.Equals(_activeExecution?.ThreadExecutionId, executionId, StringComparison.Ordinal))
                _activeExecution = null;
        }
    }

    private async Task<AgentEvent> PublishRuntimeEventAsync(
        AgentEvent evt,
        CancellationToken cancellationToken)
    {
        var store = _agent.Config?.SessionStore;
        if (store is null || string.IsNullOrWhiteSpace(evt.SessionId) || string.IsNullOrWhiteSpace(evt.ThreadId))
        {
            await _agent.EventCoordinator.EmitAsync(evt, cancellationToken).ConfigureAwait(false);
            return evt;
        }

        return await new AgentEventPublisher(store, _agent.EventCoordinator).CommitAndPublishAsync(
            new ThreadKey(evt.SessionId, evt.ThreadId),
            evt,
            cancellationToken).ConfigureAwait(false);
    }

    private static bool IsInScope(
        AgentEvent evt,
        AgentTuiRuntimeScope scope)
    {
        var sessionMatches = evt.SessionId is null || string.Equals(evt.SessionId, scope.SessionId, StringComparison.Ordinal);
        var threadMatches = evt.ThreadId is null || string.Equals(evt.ThreadId, scope.ThreadId, StringComparison.Ordinal);
        return sessionMatches && threadMatches;
    }

    private static AgentTuiEventBatch CreateDeliveryBatch(
        IReadOnlyList<AgentEvent> events,
        AgentTuiEventDeliveryMode deliveryMode,
        ThreadJournalCursor initialObservedCursor,
        long generation) =>
        new(
            events,
            deliveryMode,
            initialObservedCursor,
            new ThreadJournalCursor(generation, events[0].ThreadSequenceNumber),
            new ThreadJournalCursor(generation, events[^1].ThreadSequenceNumber));
}
