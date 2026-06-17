 using System.Runtime.CompilerServices;
using System.Threading.Channels;
using HPD.Events;

namespace HPD.Agent.TUI.Runtime;

public sealed class InMemoryAgentTuiRuntime : IHpdAgentTuiRuntime, IAgentTuiSessionBranchRuntime, IAgentTuiAgentRuntime, IAsyncDisposable
{
    private readonly Agent _agent;
    private readonly AgentTuiRuntimeScope _defaultScope;
    private readonly Channel<AgentEvent> _events = Channel.CreateUnbounded<AgentEvent>();
    private readonly IDisposable _subscription;
    private readonly object _gate = new();
    private AgentTuiBranchRun? _activeRun;

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

    public async Task<AgentTuiRuntimeScope> EnsureScopeAsync(
        AgentTuiRuntimeScope? requested,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var scope = requested ?? _defaultScope;
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

    public async Task<IReadOnlyList<AgentTuiBranchInfo>> ListBranchesAsync(
        string sessionId,
        CancellationToken cancellationToken = default)
    {
        var store = _agent.Config?.SessionStore;
        if (store is null)
        {
            return [];
        }

        var branchIds = await store.ListBranchIdsAsync(sessionId, cancellationToken).ConfigureAwait(false);
        var branches = new List<AgentTuiBranchInfo>(branchIds.Count);
        foreach (var branchId in branchIds)
        {
            var branch = await store.LoadBranchAsync(sessionId, branchId, cancellationToken).ConfigureAwait(false);
            if (branch is not null)
            {
                branches.Add(ToBranchInfo(branch, sessionId));
            }
        }

        return branches
            .OrderBy(static branch => branch.IsOriginal ? 0 : 1)
            .ThenByDescending(static branch => branch.LastActivity)
            .ToArray();
    }

    public async Task<AgentTuiBranchInfo?> GetBranchAsync(
        string sessionId,
        string branchId,
        CancellationToken cancellationToken = default)
    {
        var branch = _agent.Config?.SessionStore is { } store
            ? await store.LoadBranchAsync(sessionId, branchId, cancellationToken).ConfigureAwait(false)
            : null;
        return branch is null ? null : ToBranchInfo(branch, sessionId);
    }

    public async Task<AgentTuiBranchInfo> CreateBranchAsync(
        string agentId,
        string sessionId,
        string? branchId = null,
        string? name = null,
        CancellationToken cancellationToken = default)
        => await CreateBranchAsync(
                agentId,
                sessionId,
                new AgentTuiCreateBranchRequest(branchId, name),
                cancellationToken)
            .ConfigureAwait(false);

    public async Task<AgentTuiBranchInfo> CreateBranchAsync(
        string agentId,
        string sessionId,
        AgentTuiCreateBranchRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var id = await _agent.CreateBranchAsync(sessionId, request.BranchId, request.Name, cancellationToken)
            .ConfigureAwait(false);
        var store = _agent.Config?.SessionStore
            ?? throw new InvalidOperationException("No session store configured.");
        var branch = await store.LoadBranchAsync(sessionId, id, cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException($"Branch '{id}' was not found after creation.");

        ApplyBranchUpdate(branch, new AgentTuiBranchUpdate(
            request.Name,
            request.Description,
            request.Tags,
            request.Metadata));
        await store.AppendBranchMetadataUpdatedAsync(branch, cancellationToken).ConfigureAwait(false);
        return ToBranchInfo(branch, sessionId);
    }

    public async Task<AgentTuiBranchInfo> ForkBranchAsync(
        string agentId,
        string sessionId,
        string sourceBranchId,
        AgentTuiForkBranchRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var id = string.IsNullOrWhiteSpace(request.NewBranchId)
            ? Guid.NewGuid().ToString("N")[..12]
            : request.NewBranchId;
        var metadata = ToObjectDictionary(request.Metadata);
        var newBranchId = await _agent.ForkBranchAsync(
                sessionId,
                sourceBranchId,
                id,
                request.FromMessageId,
                metadata,
                cancellationToken)
            .ConfigureAwait(false);
        var store = _agent.Config?.SessionStore
            ?? throw new InvalidOperationException("No session store configured.");
        var branch = await store.LoadBranchAsync(sessionId, newBranchId, cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException($"Branch '{newBranchId}' was not found after fork.");

        ApplyBranchUpdate(branch, new AgentTuiBranchUpdate(
            request.Name,
            request.Description,
            request.Tags,
            request.Metadata));
        await store.AppendBranchMetadataUpdatedAsync(branch, cancellationToken).ConfigureAwait(false);
        return ToBranchInfo(branch, sessionId);
    }

    public async Task<AgentTuiBranchInfo> UpdateBranchAsync(
        string sessionId,
        string branchId,
        AgentTuiBranchUpdate update,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(update);

        var store = _agent.Config?.SessionStore
            ?? throw new InvalidOperationException("No session store configured.");
        var branch = await store.LoadBranchAsync(sessionId, branchId, cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException($"Branch '{branchId}' was not found.");
        ApplyBranchUpdate(branch, update);
        await store.AppendBranchMetadataUpdatedAsync(branch, cancellationToken).ConfigureAwait(false);
        return ToBranchInfo(branch, sessionId);
    }

    public async Task<IReadOnlyList<AgentTuiBranchInfo>> GetSiblingBranchesAsync(
        string sessionId,
        string branchId,
        CancellationToken cancellationToken = default)
    {
        var store = _agent.Config?.SessionStore
            ?? throw new InvalidOperationException("No session store configured.");
        var target = await store.LoadBranchAsync(sessionId, branchId, cancellationToken).ConfigureAwait(false);
        if (target is null)
        {
            return [];
        }

        var branchIds = await store.ListBranchIdsAsync(sessionId, cancellationToken).ConfigureAwait(false);
        var siblings = new List<AgentTuiBranchInfo>();
        foreach (var id in branchIds)
        {
            var branch = await store.LoadBranchAsync(sessionId, id, cancellationToken).ConfigureAwait(false);
            if (branch is not null && IsSiblingOf(branch, target))
            {
                siblings.Add(ToBranchInfo(branch, sessionId));
            }
        }

        return siblings
            .OrderBy(static branch => branch.SiblingIndex)
            .ThenBy(static branch => branch.CreatedAt)
            .ToArray();
    }

    public async Task DeleteBranchAsync(
        string sessionId,
        string branchId,
        bool recursive = false,
        CancellationToken cancellationToken = default)
    {
        if (recursive)
        {
            throw new NotSupportedException("Recursive branch deletion is not supported by the in-memory TUI runtime.");
        }

        await _agent.DeleteBranchAsync(sessionId, branchId, cancellationToken).ConfigureAwait(false);
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

    private static AgentTuiBranchInfo ToBranchInfo(Branch branch, string sessionId)
        => new(
            branch.Id,
            sessionId,
            branch.GetDisplayName(),
            branch.Description,
            branch.CreatedAt,
            branch.LastActivity,
            branch.MessageCount,
            branch.IsOriginal,
            branch.ForkedFrom,
            branch.ForkedAtMessageId,
            branch.ForkedAtMessageIndex,
            branch.TotalForks,
            branch.Tags?.ToArray(),
            branch.Ancestors?.ToDictionary(
                static pair => pair.Key,
                static pair => pair.Value,
                StringComparer.Ordinal),
            branch.SiblingIndex,
            branch.TotalSiblings,
            branch.OriginalBranchId,
            branch.PreviousSiblingId,
            branch.NextSiblingId,
            branch.Metadata.ToDictionary(
                static pair => pair.Key,
                static pair => (object?)pair.Value,
                StringComparer.Ordinal));

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

    private static void ApplyBranchUpdate(
        Branch branch,
        AgentTuiBranchUpdate update)
    {
        if (update.Name is not null)
        {
            branch.Name = update.Name;
        }

        if (update.Description is not null)
        {
            branch.Description = update.Description;
        }

        if (update.Tags is not null)
        {
            branch.Tags = update.Tags.ToList();
        }

        ApplyMetadata(branch.Metadata, update.Metadata);
        branch.LastActivity = DateTime.UtcNow;
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

    private static bool IsSiblingOf(Branch branch, Branch target)
    {
        if (branch.Id == target.Id)
        {
            return true;
        }

        return string.Equals(branch.ForkedFrom, target.ForkedFrom, StringComparison.Ordinal) &&
               string.Equals(branch.ForkedAtMessageId, target.ForkedAtMessageId, StringComparison.Ordinal);
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
            BranchId = scope.BranchId,
            RuntimeRunId = runId
        };

        SetActiveRun(new AgentTuiBranchRun(runId, scope.AgentId, scope.SessionId, scope.BranchId, "running", startedAt));
        await PublishRuntimeEventAsync(new BranchRunStartedEvent(runId, scope.AgentId, startedAt)
        {
            SessionId = scope.SessionId,
            BranchId = scope.BranchId
        }, cancellationToken).ConfigureAwait(false);

        try
        {
            await _agent.RunAsync(scopedInput, cancellationToken).ConfigureAwait(false);

            await PublishRuntimeEventAsync(new BranchRunCompletedEvent(runId, scope.AgentId, Cancelled: false)
            {
                SessionId = scope.SessionId,
                BranchId = scope.BranchId
            }, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            await PublishRuntimeEventAsync(new BranchRunCompletedEvent(runId, scope.AgentId, Cancelled: true)
            {
                SessionId = scope.SessionId,
                BranchId = scope.BranchId
            }, CancellationToken.None).ConfigureAwait(false);
            throw;
        }
        catch (Exception ex)
        {
            await PublishRuntimeEventAsync(new BranchRunCompletedEvent(
                runId,
                scope.AgentId,
                Cancelled: false,
                ex.GetType().Name,
                ex.Message)
            {
                SessionId = scope.SessionId,
                BranchId = scope.BranchId
            }, CancellationToken.None).ConfigureAwait(false);
            throw;
        }
        finally
        {
            SetActiveRun(null);
        }
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

    public async Task<IReadOnlyList<AgentEvent>> GetBranchEventsAsync(
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
        await foreach (var evt in store.ReadBranchEventsAsync(scope.SessionId, scope.BranchId, ReplayReadOptions.All, cancellationToken)
            .ConfigureAwait(false))
        {
            events.Add(evt);
        }

        return events;
    }

    public Task<AgentTuiBranchRun?> GetActiveRunAsync(
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

    private void SetActiveRun(AgentTuiBranchRun? activeRun)
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
        var branchMatches = evt.BranchId is null || string.Equals(evt.BranchId, scope.BranchId, StringComparison.Ordinal);
        return sessionMatches && branchMatches;
    }
}
