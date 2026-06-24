 using System.Runtime.CompilerServices;
using System.Threading.Channels;
using HPD.Events;

namespace HPD.Agent.TUI.Runtime;

public sealed class InMemoryAgentTuiRuntime : IHpdAgentTuiRuntime, IAgentTuiSessionThreadRuntime, IAgentTuiAgentRuntime, IAsyncDisposable
{
    private readonly Agent _agent;
    private readonly AgentTuiRuntimeScope _defaultScope;
    private readonly Channel<AgentEvent> _events = Channel.CreateUnbounded<AgentEvent>();
    private readonly IDisposable _subscription;
    private readonly object _gate = new();
    private AgentTuiThreadRun? _activeRun;

    public InMemoryAgentTuiRuntime(
        Agent agent,
        AgentTuiRuntimeScope? defaultScope = null)
    {
        _agent = agent ?? throw new ArgumentNullException(nameof(agent));
        _defaultScope = defaultScope ?? new AgentTuiRuntimeScope(
            _agent.AgentId,
            "local-session",
            "main");
        _subscription = _agent.SubscribeAny(evt =>
        {
            _events.Writer.TryWrite(evt);
            return ValueTask.CompletedTask;
        });
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
            await store.LoadSessionAsync(scope.SessionId, cancellationToken).ConfigureAwait(false) is not null)
        {
            return new AgentTuiScopeResolution(scope, IsDurable: true);
        }

        return requested is null
            ? new AgentTuiScopeResolution(
                await EnsureDurableScopeAsync(scope, cancellationToken).ConfigureAwait(false),
                IsDurable: true)
            : new AgentTuiScopeResolution(scope, IsDurable: store is null);
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

        var threadIds = await store.ListThreadIdsAsync(sessionId, cancellationToken).ConfigureAwait(false);
        var threads = new List<AgentTuiThreadInfo>(threadIds.Count);
        foreach (var threadId in threadIds)
        {
            var thread = await store.LoadThreadAsync(sessionId, threadId, cancellationToken).ConfigureAwait(false);
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
            ? await store.LoadThreadAsync(sessionId, threadId, cancellationToken).ConfigureAwait(false)
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
        var thread = await store.LoadThreadAsync(sessionId, id, cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException($"Thread '{id}' was not found after creation.");

        ApplyThreadUpdate(thread, new AgentTuiThreadUpdate(
            request.Name,
            request.Description,
            request.Tags,
            request.Metadata));
        await store.AppendThreadMetadataUpdatedAsync(thread, cancellationToken).ConfigureAwait(false);
        return ToThreadInfo(thread, sessionId);
    }

    public async Task<AgentTuiThreadInfo> ForkThreadAsync(
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
        var metadata = ToObjectDictionary(request.Metadata);
        var newThreadId = await _agent.ForkThreadAsync(
                sessionId,
                sourceThreadId,
                id,
                request.FromMessageId,
                metadata,
                cancellationToken)
            .ConfigureAwait(false);
        var store = _agent.Config?.SessionStore
            ?? throw new InvalidOperationException("No session store configured.");
        var thread = await store.LoadThreadAsync(sessionId, newThreadId, cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException($"Thread '{newThreadId}' was not found after fork.");

        ApplyThreadUpdate(thread, new AgentTuiThreadUpdate(
            request.Name,
            request.Description,
            request.Tags,
            request.Metadata));
        await store.AppendThreadMetadataUpdatedAsync(thread, cancellationToken).ConfigureAwait(false);
        return ToThreadInfo(thread, sessionId);
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
        var thread = await store.LoadThreadAsync(sessionId, threadId, cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException($"Thread '{threadId}' was not found.");
        ApplyThreadUpdate(thread, update);
        await store.AppendThreadMetadataUpdatedAsync(thread, cancellationToken).ConfigureAwait(false);
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

        var threadIds = await store.ListThreadIdsAsync(sessionId, cancellationToken).ConfigureAwait(false);
        var threads = new List<Thread>(threadIds.Count);
        foreach (var id in threadIds)
        {
            var thread = await store.LoadThreadAsync(sessionId, id, cancellationToken).ConfigureAwait(false);
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
            thread.SubAgentRunId,
            thread.SubAgentSourceKind,
            thread.ParentToolCallId,
            thread.SessionPolicy,
            thread.ThreadPolicy,
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
            thread.ParentSessionId ?? thread.SessionId,
            thread.ParentThreadId ?? thread.ForkedFrom ?? string.Empty,
            thread.GetDisplayName(),
            thread.Kind,
            thread.Visibility,
            thread.SubAgentName,
            thread.SubAgentRunId,
            thread.SubAgentSourceKind,
            thread.ParentToolCallId,
            thread.SessionPolicy,
            thread.ThreadPolicy,
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

    public async IAsyncEnumerable<AgentEvent> ObserveAsync(
        AgentTuiRuntimeScope scope,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await foreach (var evt in _events.Reader.ReadAllAsync(cancellationToken).ConfigureAwait(false))
        {
            if (IsInScope(evt, scope))
            {
                yield return evt;
            }
        }
    }

    public async Task SubmitInputAsync(
        AgentTuiRuntimeScope scope,
        AgentInputEvent input,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(scope);
        ArgumentNullException.ThrowIfNull(input);

        var runId = input.RuntimeRunId ?? Guid.NewGuid().ToString("N");
        var startedAt = DateTimeOffset.UtcNow;
        var scopedInput = input with
        {
            AgentId = scope.AgentId,
            SessionId = scope.SessionId,
            ThreadId = scope.ThreadId,
            RuntimeRunId = runId
        };

        SetActiveRun(new AgentTuiThreadRun(runId, scope.AgentId, scope.SessionId, scope.ThreadId, "active", startedAt));
        await PublishRuntimeEventAsync(new ThreadRunStartedEvent(runId, scope.AgentId, startedAt)
        {
            SessionId = scope.SessionId,
            ThreadId = scope.ThreadId
        }, cancellationToken).ConfigureAwait(false);

        try
        {
            await _agent.RunAsync(scopedInput, cancellationToken).ConfigureAwait(false);

            await PublishRuntimeEventAsync(new ThreadRunCompletedEvent(runId, scope.AgentId, Cancelled: false)
            {
                SessionId = scope.SessionId,
                ThreadId = scope.ThreadId
            }, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            await PublishRuntimeEventAsync(new ThreadRunCompletedEvent(runId, scope.AgentId, Cancelled: true)
            {
                SessionId = scope.SessionId,
                ThreadId = scope.ThreadId
            }, CancellationToken.None).ConfigureAwait(false);
            throw;
        }
        catch (Exception ex)
        {
            await PublishRuntimeEventAsync(new ThreadRunCompletedEvent(
                runId,
                scope.AgentId,
                Cancelled: false,
                ex.GetType().Name,
                ex.Message)
            {
                SessionId = scope.SessionId,
                ThreadId = scope.ThreadId
            }, CancellationToken.None).ConfigureAwait(false);
            throw;
        }
        finally
        {
            SetActiveRun(null);
        }
    }

    public async Task InterruptAsync(
        AgentTuiRuntimeScope scope,
        string reason,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(scope);

        var interruption = new InterruptionRequestEvent(
            eventFlowId: null,
            Reason: string.IsNullOrWhiteSpace(reason) ? "Interrupted by TUI." : reason,
            Source: InterruptionSource.User)
        {
            AgentId = scope.AgentId,
            SessionId = scope.SessionId,
            ThreadId = scope.ThreadId
        };

        await _agent.RunAsync(interruption, cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task RespondAsync(
        AgentTuiRuntimeScope scope,
        AgentEvent response,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(response);

        if (response is not IResponseEvent responseEvent)
        {
            throw new ArgumentException("Response event must implement IResponseEvent.", nameof(response));
        }

        await _agent.RespondIfPendingAsync(responseEvent, cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<AgentEvent>> GetThreadEventsAsync(
        AgentTuiRuntimeScope scope,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(scope);

        var store = _agent.Config?.SessionStore;
        if (store is null)
        {
            return [];
        }

        var events = new List<AgentEvent>();
        await foreach (var evt in store.ReadThreadEventsAsync(scope.SessionId, scope.ThreadId, ReplayReadOptions.All, cancellationToken)
            .ConfigureAwait(false))
        {
            events.Add(evt);
        }

        return events;
    }

    public Task<AgentTuiThreadRun?> GetActiveRunAsync(
        AgentTuiRuntimeScope scope,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
        {
            return Task.FromResult(_activeRun);
        }
    }

    public ValueTask DisposeAsync()
    {
        _subscription.Dispose();
        _events.Writer.TryComplete();
        return ValueTask.CompletedTask;
    }

    private void SetActiveRun(AgentTuiThreadRun? activeRun)
    {
        lock (_gate)
        {
            _activeRun = activeRun;
        }
    }

    private async Task PublishRuntimeEventAsync(
        AgentEvent evt,
        CancellationToken cancellationToken)
    {
        await _events.Writer.WriteAsync(evt, cancellationToken).ConfigureAwait(false);
    }

    private static bool IsInScope(
        AgentEvent evt,
        AgentTuiRuntimeScope scope)
    {
        var sessionMatches = evt.SessionId is null || string.Equals(evt.SessionId, scope.SessionId, StringComparison.Ordinal);
        var threadMatches = evt.ThreadId is null || string.Equals(evt.ThreadId, scope.ThreadId, StringComparison.Ordinal);
        return sessionMatches && threadMatches;
    }
}
