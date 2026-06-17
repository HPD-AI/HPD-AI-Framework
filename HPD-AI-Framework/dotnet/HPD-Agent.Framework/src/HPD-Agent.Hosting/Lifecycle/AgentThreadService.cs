using HPD.Agent.Hosting.Data;
using HPD.Agent.Hosting.Extensions;
using Microsoft.Extensions.AI;

namespace HPD.Agent.Hosting.Lifecycle;

public sealed class AgentThreadService : IAgentThreadService
{
    private readonly SessionManager _sessionManager;
    private readonly AgentManager _agentManager;

    public AgentThreadService(SessionManager sessionManager, AgentManager agentManager)
    {
        _sessionManager = sessionManager ?? throw new ArgumentNullException(nameof(sessionManager));
        _agentManager = agentManager ?? throw new ArgumentNullException(nameof(agentManager));
    }

    public async Task<AgentServiceResult<IReadOnlyList<ThreadDto>>> ListThreadsAsync(
        string sessionId,
        CancellationToken cancellationToken = default)
    {
        if (await _sessionManager.Store.LoadSessionAsync(sessionId, cancellationToken) == null)
            return AgentServiceResult<IReadOnlyList<ThreadDto>>.NotFound;

        var threadIds = await _sessionManager.Store.ListThreadIdsAsync(sessionId, cancellationToken);
        var dtos = new List<ThreadDto>();
        foreach (var threadId in threadIds)
        {
            var thread = await _sessionManager.Store.LoadThreadAsync(sessionId, threadId, cancellationToken);
            if (thread != null)
                dtos.Add(thread.ToDto(sessionId));
        }

        return AgentServiceResult<IReadOnlyList<ThreadDto>>.Success(dtos);
    }

    public async Task<AgentServiceResult<ThreadDto>> GetThreadAsync(
        string sessionId,
        string threadId,
        CancellationToken cancellationToken = default)
    {
        var thread = await _sessionManager.Store.LoadThreadAsync(sessionId, threadId, cancellationToken);
        return thread == null
            ? AgentServiceResult<ThreadDto>.NotFound
            : AgentServiceResult<ThreadDto>.Success(thread.ToDto(sessionId));
    }

    public async Task<AgentServiceResult<ThreadDto>> CreateThreadAsync(
        string agentId,
        string sessionId,
        CreateThreadRequest request,
        CancellationToken cancellationToken = default)
    {
        if (await _sessionManager.Store.LoadSessionAsync(sessionId, cancellationToken) == null)
            return AgentServiceResult<ThreadDto>.NotFound;

        var threadId = string.IsNullOrWhiteSpace(request.ThreadId)
            ? Guid.NewGuid().ToString()
            : request.ThreadId;

        if (await _sessionManager.Store.LoadThreadAsync(sessionId, threadId, cancellationToken) != null)
            return AgentServiceResult<ThreadDto>.Conflict;

        var session = await _sessionManager.Store.LoadSessionAsync(sessionId, cancellationToken)
            ?? throw new InvalidOperationException($"Session '{sessionId}' not found after existence check.");
        session.Store = _sessionManager.Store;

        var thread = session.CreateThread(threadId);
        thread.Name = request.Name ?? threadId;
        thread.Description = request.Description;
        thread.Tags = request.Tags;
        MergeThreadMetadata(thread, request.Metadata);
        await _sessionManager.Store.SaveInitialThreadAsync(sessionId, thread, cancellationToken);

        thread = await _sessionManager.Store.LoadThreadAsync(sessionId, threadId, cancellationToken)
            ?? throw new InvalidOperationException($"Thread '{threadId}' not found after creation.");

        return AgentServiceResult<ThreadDto>.Success(thread.ToDto(sessionId));
    }

    public Task<AgentServiceResult<ThreadDto>> ForkThreadAsync(
        string agentId,
        string sessionId,
        string threadId,
        ForkThreadRequest request,
        CancellationToken cancellationToken = default)
    {
        return _sessionManager.WithSessionLockAsync(
            sessionId,
            () => ForkThreadCoreAsync(agentId, sessionId, threadId, request, cancellationToken),
            cancellationToken);
    }

    public async Task<AgentServiceResult<ThreadDto>> UpdateThreadAsync(
        string sessionId,
        string threadId,
        UpdateThreadRequest request,
        CancellationToken cancellationToken = default)
    {
        var thread = await _sessionManager.Store.LoadThreadAsync(sessionId, threadId, cancellationToken);
        if (thread == null)
            return AgentServiceResult<ThreadDto>.NotFound;

        return await _sessionManager.WithSessionLockAsync(
            sessionId,
            async () =>
            {
                if (request.Name != null) thread.Name = request.Name;
                if (request.Description != null) thread.Description = request.Description;
                if (request.Tags != null) thread.Tags = request.Tags;
                MergeThreadMetadata(thread, request.Metadata);
                thread.LastActivity = DateTime.UtcNow;

                await _sessionManager.Store.AppendThreadMetadataUpdatedAsync(thread, cancellationToken);
                return AgentServiceResult<ThreadDto>.Success(thread.ToDto(sessionId));
            },
            cancellationToken);
    }

    public async Task<AgentServiceResult> DeleteThreadAsync(
        string sessionId,
        string threadId,
        bool recursive = false,
        CancellationToken cancellationToken = default)
    {
        if (threadId == "main")
            return AgentServiceResult.Validation("ProtectedThread", "Cannot delete the 'main' thread.");

        var thread = await _sessionManager.Store.LoadThreadAsync(sessionId, threadId, cancellationToken);
        if (thread == null)
            return AgentServiceResult.NotFound;

        if (thread.ChildThreads.Count > 0)
        {
            if (!recursive)
            {
                return AgentServiceResult.Validation(
                    "HasChildren",
                    $"Cannot delete thread with {thread.ChildThreads.Count} child threads. " +
                    $"Use ?recursive=true to delete the entire subtree, or delete children first: " +
                    $"{string.Join(", ", thread.ChildThreads)}");
            }

            if (!_sessionManager.AllowRecursiveThreadDelete)
            {
                return AgentServiceResult.Validation(
                    "RecursiveDeleteDisabled",
                    "Recursive thread deletion is not enabled on this server. " +
                    "Set AllowRecursiveThreadDelete = true in HPDAgentConfig to enable it.");
            }
        }

        if (!_sessionManager.TryAcquireThreadOperationLock(sessionId, threadId))
            return AgentServiceResult.Conflict;

        try
        {
            return await _sessionManager.WithSessionLockAsync(
                sessionId,
                () => DeleteThreadCoreAsync(sessionId, threadId, thread, recursive, cancellationToken),
                cancellationToken);
        }
        finally
        {
            _sessionManager.ReleaseThreadOperationLock(sessionId, threadId);
            _sessionManager.RemoveThreadOperationLock(sessionId, threadId);
        }
    }

    public async Task<AgentServiceResult<IReadOnlyList<AgentEvent>>> GetEventsAsync(
        string sessionId,
        string threadId,
        CancellationToken cancellationToken = default)
    {
        var document = await _sessionManager.Store.LoadThreadDocumentAsync(sessionId, threadId, cancellationToken);
        if (document == null && await _sessionManager.Store.LoadThreadAsync(sessionId, threadId, cancellationToken) == null)
            return AgentServiceResult<IReadOnlyList<AgentEvent>>.NotFound;

        return AgentServiceResult<IReadOnlyList<AgentEvent>>.Success(
            document?.Events.OrderBy(e => e.SequenceNumber).ToList() ?? []);
    }

    public async Task<AgentServiceResult<IReadOnlyList<ThreadDto>>> GetSiblingsAsync(
        string sessionId,
        string threadId,
        CancellationToken cancellationToken = default)
    {
        var targetThread = await _sessionManager.Store.LoadThreadAsync(sessionId, threadId, cancellationToken);
        if (targetThread == null)
            return AgentServiceResult<IReadOnlyList<ThreadDto>>.NotFound;

        var threadIds = await _sessionManager.Store.ListThreadIdsAsync(sessionId, cancellationToken);
        var siblingDtos = new List<ThreadDto>();

        foreach (var id in threadIds)
        {
            var thread = await _sessionManager.Store.LoadThreadAsync(sessionId, id, cancellationToken);
            if (thread == null)
                continue;

            var isSameGroup = thread.ForkedFrom == targetThread.ForkedFrom &&
                thread.ForkedAtMessageId == targetThread.ForkedAtMessageId;
            var isSource = targetThread.ForkedFrom != null && id == targetThread.ForkedFrom;

            if (isSameGroup || isSource)
                siblingDtos.Add(thread.ToDto(sessionId));
        }

        return AgentServiceResult<IReadOnlyList<ThreadDto>>.Success(
            siblingDtos.OrderBy(s => s.SiblingIndex).ToList());
    }

    private async Task<AgentServiceResult<ThreadDto>> ForkThreadCoreAsync(
        string agentId,
        string sessionId,
        string threadId,
        ForkThreadRequest request,
        CancellationToken cancellationToken)
    {
        if (await _sessionManager.Store.LoadSessionAsync(sessionId, cancellationToken) == null)
            return AgentServiceResult<ThreadDto>.NotFound;

        if (await _sessionManager.Store.LoadThreadAsync(sessionId, threadId, cancellationToken) == null)
            return AgentServiceResult<ThreadDto>.NotFound;

        var newThreadId = string.IsNullOrWhiteSpace(request.NewThreadId)
            ? Guid.NewGuid().ToString()
            : request.NewThreadId;

        var agent = await _agentManager.GetOrBuildAgentAsync(agentId, cancellationToken);
        try
        {
            await agent.ForkThreadAsync(
                sessionId,
                threadId,
                newThreadId,
                request.FromMessageId,
                request.Metadata,
                cancellationToken);
        }
        catch (MessageNotPresentOnThreadException ex)
        {
            return AgentServiceResult<ThreadDto>.Validation(
                "ForkMessageNotPresent",
                ex.Message);
        }

        var newThread = await _sessionManager.Store.LoadThreadAsync(sessionId, newThreadId, cancellationToken)
            ?? throw new InvalidOperationException($"Thread '{newThreadId}' not found after fork.");

        ApplyThreadMetadata(newThread, request.Name, request.Description, request.Tags, metadata: null);
        await _sessionManager.Store.AppendThreadMetadataUpdatedAsync(newThread, cancellationToken);

        return AgentServiceResult<ThreadDto>.Success(newThread.ToDto(sessionId));
    }

    private async Task<AgentServiceResult> DeleteThreadCoreAsync(
        string sessionId,
        string threadId,
        Thread thread,
        bool recursive,
        CancellationToken cancellationToken)
    {
        if (recursive)
        {
            foreach (var childId in thread.ChildThreads.ToList())
                await DeleteSubtreeAsync(sessionId, childId, cancellationToken);
        }

        await ReindexSiblingsAfterDeleteAsync(sessionId, threadId, thread, cancellationToken);

        var session = await _sessionManager.Store.LoadSessionAsync(sessionId, cancellationToken);
        if (session != null)
        {
            session.LastActivity = DateTime.UtcNow;
            await _sessionManager.Store.SaveSessionAsync(session, cancellationToken);
        }

        await _sessionManager.Store.DeleteThreadAsync(sessionId, threadId, cancellationToken);
        return AgentServiceResult.Success;
    }

    private async Task DeleteSubtreeAsync(
        string sessionId,
        string threadId,
        CancellationToken cancellationToken)
    {
        var thread = await _sessionManager.Store.LoadThreadAsync(sessionId, threadId, cancellationToken);
        if (thread == null)
            return;

        foreach (var childId in thread.ChildThreads.ToList())
            await DeleteSubtreeAsync(sessionId, childId, cancellationToken);

        await ReindexSiblingsAfterDeleteAsync(sessionId, threadId, thread, cancellationToken);
        await _sessionManager.Store.DeleteThreadAsync(sessionId, threadId, cancellationToken);
    }

    private async Task ReindexSiblingsAfterDeleteAsync(
        string sessionId,
        string threadId,
        Thread thread,
        CancellationToken cancellationToken)
    {
        if (thread.ForkedFrom != null)
        {
            var parent = await _sessionManager.Store.LoadThreadAsync(sessionId, thread.ForkedFrom, cancellationToken);
            if (parent != null && parent.ChildThreads.Contains(threadId))
            {
                parent.ChildThreads.Remove(threadId);
                parent.LastActivity = DateTime.UtcNow;
                await _sessionManager.Store.AppendThreadTreeUpdatedAsync(parent, cancellationToken);
            }
        }

        var allThreadIds = await _sessionManager.Store.ListThreadIdsAsync(sessionId, cancellationToken);
        var remainingSiblings = new List<Thread>();

        foreach (var id in allThreadIds)
        {
            if (id == threadId)
                continue;

            var sibling = await _sessionManager.Store.LoadThreadAsync(sessionId, id, cancellationToken);
            if (sibling == null)
                continue;

            var isSameGroup = sibling.ForkedFrom == thread.ForkedFrom &&
                sibling.ForkedAtMessageId == thread.ForkedAtMessageId;
            var isSource = thread.ForkedFrom != null && id == thread.ForkedFrom;

            if (isSameGroup || isSource)
                remainingSiblings.Add(sibling);
        }

        remainingSiblings = remainingSiblings.OrderBy(b => b.SiblingIndex).ToList();
        for (var i = 0; i < remainingSiblings.Count; i++)
        {
            var sibling = remainingSiblings[i];
            sibling.SiblingIndex = i;
            sibling.TotalSiblings = remainingSiblings.Count;
            sibling.PreviousSiblingId = i > 0 ? remainingSiblings[i - 1].Id : null;
            sibling.NextSiblingId = i < remainingSiblings.Count - 1 ? remainingSiblings[i + 1].Id : null;
            sibling.LastActivity = DateTime.UtcNow;
            await _sessionManager.Store.AppendThreadTreeUpdatedAsync(sibling, cancellationToken);
        }
    }

    private static void ApplyThreadMetadata(
        Thread thread,
        string? name,
        string? description,
        List<string>? tags,
        Dictionary<string, object>? metadata)
    {
        if (!string.IsNullOrEmpty(name))
            thread.Name = name;
        if (!string.IsNullOrEmpty(description))
            thread.Description = description;
        if (tags != null && tags.Count > 0)
            thread.Tags = tags;
        if (metadata != null)
        {
            var runtimeMetadata = metadata
                .Where(kvp => kvp.Value != null)
                .ToDictionary(kvp => kvp.Key, kvp => kvp.Value!, StringComparer.Ordinal);
            thread.ApplyRuntimeMetadata(runtimeMetadata);
            thread.Metadata.Clear();
            foreach (var (key, value) in runtimeMetadata)
                thread.Metadata[key] = value;
        }
    }

    private static void MergeThreadMetadata(
        Thread thread,
        Dictionary<string, object?>? patch)
    {
        if (patch == null)
            return;

        var extensionPatch = patch
            .Where(kvp => kvp.Value != null)
            .ToDictionary(kvp => kvp.Key, kvp => kvp.Value!, StringComparer.Ordinal);
        thread.ApplyRuntimeMetadata(extensionPatch);

        foreach (var (key, value) in patch)
        {
            if (value == null)
                thread.Metadata.Remove(key);
        }

        foreach (var (key, value) in extensionPatch)
        {
            thread.Metadata[key] = value;
        }
    }
}
