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

        var threads = await LoadSessionThreadsAsync(sessionId, cancellationToken);
        var dtos = threads.Select(thread => thread.ToDto(sessionId)).ToList();

        return AgentServiceResult<IReadOnlyList<ThreadDto>>.Success(dtos);
    }

    public async Task<AgentServiceResult<ThreadGraphDto>> GetThreadGraphAsync(
        string sessionId,
        CancellationToken cancellationToken = default)
    {
        if (await _sessionManager.Store.LoadSessionAsync(sessionId, cancellationToken) == null)
            return AgentServiceResult<ThreadGraphDto>.NotFound;

        var threads = await LoadSessionThreadsAsync(sessionId, cancellationToken);
        var dtos = threads.Select(thread => thread.ToDto(sessionId)).ToList();
        var forkGroups = BuildForkGroups(threads);
        var runtimeChildren = BuildRuntimeChildren(threads);

        return AgentServiceResult<ThreadGraphDto>.Success(new ThreadGraphDto(dtos, forkGroups, runtimeChildren));
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

                await _sessionManager.Store.AppendThreadUpdatedAsync(thread, cancellationToken);
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
        await _sessionManager.Store.AppendThreadUpdatedAsync(newThread, cancellationToken);

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

        await RemoveDeletedThreadFromDirectParentAsync(sessionId, threadId, thread, cancellationToken);

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

        await RemoveDeletedThreadFromDirectParentAsync(sessionId, threadId, thread, cancellationToken);
        await _sessionManager.Store.DeleteThreadAsync(sessionId, threadId, cancellationToken);
    }

    private async Task RemoveDeletedThreadFromDirectParentAsync(
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
                await _sessionManager.Store.AppendThreadUpdatedAsync(parent, cancellationToken);
            }
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

    private async Task<List<Thread>> LoadSessionThreadsAsync(
        string sessionId,
        CancellationToken cancellationToken)
    {
        var threadIds = await _sessionManager.Store.ListThreadIdsAsync(sessionId, cancellationToken);
        var threads = new List<Thread>();

        foreach (var threadId in threadIds)
        {
            var thread = await _sessionManager.Store.LoadThreadAsync(sessionId, threadId, cancellationToken);
            if (thread != null)
                threads.Add(thread);
        }

        return threads;
    }

    private static IReadOnlyList<ThreadForkGroupDto> BuildForkGroups(IReadOnlyList<Thread> threads)
    {
        return ThreadForkGraph.BuildVisibleForkGroups(threads)
            .Select(ToForkGroupDto)
            .ToList();
    }

    private static IReadOnlyList<ThreadRuntimeChildDto> BuildRuntimeChildren(IReadOnlyList<Thread> threads)
    {
        return threads
            .Where(IsRuntimeChildThread)
            .OrderBy(thread => thread.ParentThreadId, StringComparer.Ordinal)
            .ThenBy(thread => thread.CreatedAt)
            .ThenBy(thread => thread.Id, StringComparer.Ordinal)
            .Select(ToRuntimeChild)
            .ToList();
    }

    private static bool IsVisibleBranchThread(Thread thread) =>
        thread.Kind == ThreadKind.MainAgent &&
        thread.Visibility == ThreadVisibility.Visible;

    private static bool IsRuntimeChildThread(Thread thread) =>
        !string.IsNullOrWhiteSpace(thread.ParentThreadId) ||
        thread.Kind != ThreadKind.MainAgent ||
        thread.Visibility == ThreadVisibility.Hidden;

    private static ThreadRuntimeChildDto ToRuntimeChild(Thread thread)
    {
        var parentSessionId = thread.ParentSessionId ?? thread.SessionId;
        var parentThreadId = thread.ParentThreadId ?? thread.ForkedFrom ?? string.Empty;
        return new ThreadRuntimeChildDto(
            thread.Id,
            parentSessionId,
            parentThreadId,
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
    }

    private static ThreadForkGroupDto ToForkGroupDto(ThreadForkGroup group) =>
        new(
            group.Id,
            group.SourceThreadId,
            group.ForkedAtMessageId,
            group.ForkedAtMessageIndex,
            group.ChoiceMessageIndex,
            group.Members.Select(ToForkGroupMember).ToList());

    private static ThreadForkGroupMemberDto ToForkGroupMember(ThreadForkGroupMember member) =>
        new(
            member.Thread.Id,
            member.Thread.GetDisplayName(),
            member.Index,
            member.IsSource,
            member.ChoiceMessageId,
            member.ChoiceMessageIndex,
            member.Thread.MessageCount,
            member.Thread.CreatedAt,
            member.Thread.LastActivity);
}
