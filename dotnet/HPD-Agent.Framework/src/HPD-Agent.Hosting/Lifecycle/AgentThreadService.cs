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

        var threads = await _sessionManager.Store.CollectThreadDescriptorsAsync(
            sessionId,
            cancellationToken: cancellationToken);
        var childCounts = CountDirectForks(threads);
        var dtos = threads
            .Select(thread => thread.ToDto(childCounts.GetValueOrDefault(thread.Key.ThreadId)))
            .ToList();

        return AgentServiceResult<IReadOnlyList<ThreadDto>>.Success(dtos);
    }

    public async Task<AgentServiceResult<ThreadGraphDto>> GetThreadGraphAsync(
        string sessionId,
        CancellationToken cancellationToken = default)
    {
        if (await _sessionManager.Store.LoadSessionAsync(sessionId, cancellationToken) == null)
            return AgentServiceResult<ThreadGraphDto>.NotFound;

        var threads = await _sessionManager.Store.CollectThreadDescriptorsAsync(
            sessionId,
            cancellationToken: cancellationToken);
        var childCounts = CountDirectForks(threads);
        var dtos = threads
            .Select(thread => thread.ToDto(childCounts.GetValueOrDefault(thread.Key.ThreadId)))
            .ToList();
        var forkGroups = BuildDescriptorForkGroups(threads);
        var runtimeChildren = BuildDescriptorRuntimeChildren(threads);

        return AgentServiceResult<ThreadGraphDto>.Success(new ThreadGraphDto(dtos, forkGroups, runtimeChildren));
    }

    public async Task<AgentServiceResult<ThreadDto>> GetThreadAsync(
        string sessionId,
        string threadId,
        CancellationToken cancellationToken = default)
    {
        var thread = await _sessionManager.Store.GetThreadAsync(
            new ThreadKey(sessionId, threadId), cancellationToken);
        if (thread is null)
            return AgentServiceResult<ThreadDto>.NotFound;

        var descriptors = await _sessionManager.Store.CollectThreadDescriptorsAsync(
            sessionId,
            cancellationToken: cancellationToken);
        var totalForks = descriptors.Count(candidate =>
            StringComparer.Ordinal.Equals(candidate.Fork?.SourceThreadId, threadId));
        return AgentServiceResult<ThreadDto>.Success(thread.ToDto(totalForks));
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

        if (await _sessionManager.Store.GetThreadAsync(
                new ThreadKey(sessionId, threadId), cancellationToken).ConfigureAwait(false) != null)
            return AgentServiceResult<ThreadDto>.Conflict;

        var session = await _sessionManager.Store.LoadSessionAsync(sessionId, cancellationToken)
            ?? throw new InvalidOperationException($"Session '{sessionId}' not found after existence check.");
        session.Store = _sessionManager.Store;

        var thread = session.CreateThread(agentId, threadId);
        thread.Name = request.Name ?? threadId;
        thread.Description = request.Description;
        thread.Tags = request.Tags;
        MergeThreadMetadata(thread, request.Metadata);
        await _sessionManager.Store.SaveInitialThreadAsync(sessionId, thread, cancellationToken);

        var descriptor = await _sessionManager.Store.GetThreadAsync(
                new ThreadKey(sessionId, threadId), cancellationToken)
            .ConfigureAwait(false)
            ?? throw new InvalidOperationException($"Thread '{threadId}' not found after creation.");

        return AgentServiceResult<ThreadDto>.Success(descriptor.ToDto());
    }

    public Task<AgentServiceResult<ThreadForkResultDto>> ForkThreadAsync(
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
        var thread = await _sessionManager.Store.ProjectThreadAsync(sessionId, threadId, ThreadProjectionPurpose.ThreadHistory, cancellationToken);
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

        var thread = await _sessionManager.Store.ProjectThreadAsync(sessionId, threadId, ThreadProjectionPurpose.ThreadHistory, cancellationToken);
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

    private async Task<AgentServiceResult<ThreadForkResultDto>> ForkThreadCoreAsync(
        string agentId,
        string sessionId,
        string threadId,
        ForkThreadRequest request,
        CancellationToken cancellationToken)
    {
        if (await _sessionManager.Store.LoadSessionAsync(sessionId, cancellationToken) == null)
            return AgentServiceResult<ThreadForkResultDto>.NotFound;

        if (await _sessionManager.Store.GetThreadAsync(
                new ThreadKey(sessionId, threadId), cancellationToken).ConfigureAwait(false) == null)
            return AgentServiceResult<ThreadForkResultDto>.NotFound;

        var newThreadId = string.IsNullOrWhiteSpace(request.NewThreadId)
            ? Guid.NewGuid().ToString()
            : request.NewThreadId;

        var agent = await _agentManager.GetOrBuildAgentAsync(agentId, cancellationToken);
        ThreadForkResult forkResult;
        try
        {
            forkResult = await agent.ForkThreadAsync(
                sessionId,
                threadId,
                newThreadId,
                request.FromMessageId,
                new ThreadForkOptions
                {
                    Metadata = request.Metadata,
                    Compaction = request.Compaction ?? new InheritThreadForkCompaction(),
                    SubAgents = request.SubAgents
                },
                cancellationToken);
        }
        catch (MessageNotPresentOnThreadException ex)
        {
            return AgentServiceResult<ThreadForkResultDto>.Validation(
                "ForkMessageNotPresent",
                ex.Message);
        }

        var newThread = await _sessionManager.Store.ProjectThreadAsync(sessionId, newThreadId, ThreadProjectionPurpose.ThreadHistory, cancellationToken)
            ?? throw new InvalidOperationException($"Thread '{newThreadId}' not found after fork.");

        ApplyThreadMetadata(newThread, request.Name, request.Description, request.Tags, metadata: null);
        await _sessionManager.Store.AppendThreadUpdatedAsync(newThread, cancellationToken);

        return AgentServiceResult<ThreadForkResultDto>.Success(new ThreadForkResultDto(
            forkResult.OperationId,
            newThread.ToDto(sessionId),
            forkResult.SourceBoundary.Generation,
            forkResult.SourceBoundary.SequenceNumber,
            forkResult.SubAgentPolicy,
            forkResult.Status,
            forkResult.Children));
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
        var thread = await _sessionManager.Store.ProjectThreadAsync(sessionId, threadId, ThreadProjectionPurpose.ThreadHistory, cancellationToken);
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
            var parent = await _sessionManager.Store.ProjectThreadAsync(sessionId, thread.ForkedFrom, ThreadProjectionPurpose.ThreadHistory, cancellationToken);
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

    private static Dictionary<string, int> CountDirectForks(IReadOnlyList<ThreadDescriptor> threads)
        => threads
            .Where(thread => thread.Fork is not null)
            .GroupBy(thread => thread.Fork!.SourceThreadId, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.Count(), StringComparer.Ordinal);

    private static IReadOnlyList<ThreadForkGroupDto> BuildDescriptorForkGroups(
        IReadOnlyList<ThreadDescriptor> threads)
    {
        var visible = threads
            .Where(IsVisibleBranchThread)
            .ToDictionary(thread => thread.Key.ThreadId, StringComparer.Ordinal);

        return threads
            .Where(IsVisibleBranchThread)
            .Where(thread => thread.Fork is not null &&
                visible.ContainsKey(thread.Fork.SourceThreadId))
            .GroupBy(
                thread => (SourceThreadId: ResolveForkGroupSource(thread, visible), thread.Fork!.MessageId),
                new ForkDescriptorKeyComparer())
            .OrderBy(group => group.Key.SourceThreadId, StringComparer.Ordinal)
            .ThenBy(group => group.Min(thread => thread.Fork?.MessageIndex ?? -1))
            .ThenBy(group => group.Key.MessageId ?? string.Empty, StringComparer.Ordinal)
            .Select(group =>
            {
                var source = visible[group.Key.SourceThreadId];
                var forks = group
                    .OrderBy(thread => thread.CreatedAt)
                    .ThenBy(thread => thread.Key.ThreadId, StringComparer.Ordinal)
                    .ToArray();
                var forkIndex = forks.Select(thread => thread.Fork?.MessageIndex)
                    .FirstOrDefault(index => index is not null);
                var choiceIndex = forkIndex is null ? 0 : forkIndex.Value + 1;
                var members = new List<ThreadForkGroupMemberDto>
                {
                    ToForkMember(source, 0, isSource: true, choiceIndex)
                };
                for (var index = 0; index < forks.Length; index++)
                {
                    members.Add(ToForkMember(forks[index], index + 1, isSource: false, choiceIndex));
                }

                return new ThreadForkGroupDto(
                    $"{group.Key.SourceThreadId}@{group.Key.MessageId ?? "root"}",
                    group.Key.SourceThreadId,
                    group.Key.MessageId,
                    forkIndex,
                    choiceIndex,
                    members);
            })
            .ToArray();
    }

    private static string ResolveForkGroupSource(
        ThreadDescriptor thread,
        IReadOnlyDictionary<string, ThreadDescriptor> visible)
    {
        var messageId = thread.Fork!.MessageId;
        var sourceId = thread.Fork.SourceThreadId;

        while (visible.TryGetValue(sourceId, out var source) &&
               source.Fork is not null &&
               StringComparer.Ordinal.Equals(source.Fork.MessageId, messageId))
        {
            sourceId = source.Fork.SourceThreadId;
        }

        return sourceId;
    }

    private static ThreadForkGroupMemberDto ToForkMember(
        ThreadDescriptor thread,
        int index,
        bool isSource,
        int choiceIndex)
        => new(
            thread.Key.ThreadId,
            thread.Name ?? thread.Key.ThreadId,
            index,
            isSource,
            thread.Metadata.TryGetValue("inputMessageId", out var inputMessageId)
                ? inputMessageId?.ToString()
                : null,
            choiceIndex,
            thread.MessageCount,
            thread.CreatedAt.UtcDateTime,
            thread.UpdatedAt.UtcDateTime);

    private static IReadOnlyList<ThreadRuntimeChildDto> BuildDescriptorRuntimeChildren(
        IReadOnlyList<ThreadDescriptor> threads)
        => threads
            .Where(thread => thread.RuntimeChild is not null ||
                thread.Kind != ThreadKind.MainAgent ||
                thread.Visibility == ThreadVisibility.Hidden)
            .OrderBy(thread => thread.RuntimeChild?.ParentThreadId, StringComparer.Ordinal)
            .ThenBy(thread => thread.CreatedAt)
            .ThenBy(thread => thread.Key.ThreadId, StringComparer.Ordinal)
            .Select(thread => new ThreadRuntimeChildDto(
                thread.Key.ThreadId,
                thread.Key.SessionId,
                thread.DefaultAgent.AgentId,
                thread.RuntimeChild?.ParentSessionId ?? thread.Key.SessionId,
                thread.RuntimeChild?.ParentThreadId ?? thread.Fork?.SourceThreadId ?? string.Empty,
                thread.Name ?? thread.Key.ThreadId,
                thread.Kind,
                thread.Visibility,
                thread.RuntimeChild?.SubAgentName,
                thread.RuntimeChild?.InvocationId,
                thread.RuntimeChild?.SubAgentSourceKind,
                thread.RuntimeChild?.ParentToolCallId,
                thread.RuntimeChild?.ContextPolicy,
                thread.RuntimeChild?.Status,
                thread.MessageCount,
                thread.CreatedAt.UtcDateTime,
                thread.UpdatedAt.UtcDateTime))
            .ToArray();

    private static bool IsVisibleBranchThread(ThreadDescriptor thread)
        => thread.Kind == ThreadKind.MainAgent &&
           thread.Visibility == ThreadVisibility.Visible;

    private sealed class ForkDescriptorKeyComparer
        : IEqualityComparer<(string SourceThreadId, string? MessageId)>
    {
        public bool Equals(
            (string SourceThreadId, string? MessageId) x,
            (string SourceThreadId, string? MessageId) y)
            => string.Equals(x.SourceThreadId, y.SourceThreadId, StringComparison.Ordinal) &&
               string.Equals(x.MessageId, y.MessageId, StringComparison.Ordinal);

        public int GetHashCode((string SourceThreadId, string? MessageId) value)
            => HashCode.Combine(
                StringComparer.Ordinal.GetHashCode(value.SourceThreadId),
                value.MessageId is null ? 0 : StringComparer.Ordinal.GetHashCode(value.MessageId));
    }
}
