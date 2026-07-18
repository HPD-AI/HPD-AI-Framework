namespace HPD.Agent;

internal static class ThreadDescriptorProjection
{
    public static ThreadDescriptor Apply(
        ThreadKey key,
        ThreadDescriptor? current,
        ISet<string> messageIds,
        AgentEvent evt,
        long generation,
        long head)
    {
        var createdAt = current?.CreatedAt ?? evt.Timestamp;
        var owner = current?.Owner ?? new ThreadAgentBinding(string.Empty);
        var name = current?.Name;
        var description = current?.Description;
        IReadOnlyList<string> tags = current?.Tags ?? [];
        var kind = current?.Kind ?? ThreadKind.MainAgent;
        var visibility = current?.Visibility ?? ThreadVisibility.Visible;
        var fork = current?.Fork;
        var runtimeChild = current?.RuntimeChild;
        IReadOnlyDictionary<string, object> metadata = current?.Metadata
            ?? new Dictionary<string, object>(StringComparer.Ordinal);

        switch (evt)
        {
            case ThreadCreatedEvent created:
                owner = new ThreadAgentBinding(created.OwnerAgentId);
                createdAt = new DateTimeOffset(created.CreatedAt, TimeSpan.Zero);
                name = created.Name;
                description = created.Description;
                tags = created.Tags?.ToArray() ?? [];
                kind = created.ThreadKind;
                visibility = created.Visibility;
                metadata = CopyMetadata(created.ThreadMetadata);
                fork = CreateFork(created.ForkedFrom, created.ForkedAtMessageId, created.ForkedAtMessageIndex);
                runtimeChild = CreateRuntimeChild(created.ParentSessionId, created.ParentThreadId, created.SubAgentName,
                    created.SubAgentTaskName, created.InvocationId, created.SubAgentSourceKind, created.ParentToolCallId,
                    created.SessionPolicy, created.ThreadPolicy);
                break;

            case ThreadUpdatedEvent updated:
                owner = new ThreadAgentBinding(updated.OwnerAgentId);
                name = updated.Name;
                description = updated.Description;
                tags = updated.Tags?.ToArray() ?? [];
                kind = updated.ThreadKind;
                visibility = updated.Visibility;
                metadata = CopyMetadata(updated.ThreadMetadata);
                fork = CreateFork(updated.ForkedFrom, updated.ForkedAtMessageId, updated.ForkedAtMessageIndex);
                runtimeChild = CreateRuntimeChild(updated.ParentSessionId, updated.ParentThreadId, updated.SubAgentName,
                    updated.SubAgentTaskName, updated.InvocationId, updated.SubAgentSourceKind, updated.ParentToolCallId,
                    updated.SessionPolicy, updated.ThreadPolicy);
                break;

            case ThreadRunStartedEvent when runtimeChild is not null:
                runtimeChild = runtimeChild with { Status = ThreadRunStatus.Active };
                break;

            case ThreadRunCompletedEvent completed when runtimeChild is not null:
                runtimeChild = runtimeChild with
                {
                    Status = completed.ErrorType is not null
                        ? ThreadRunStatus.Failed
                        : completed.Cancelled
                            ? ThreadRunStatus.Cancelled
                            : ThreadRunStatus.Completed
                };
                break;
        }

        TrackMessage(messageIds, evt);
        return new ThreadDescriptor(
            key,
            owner,
            name,
            description,
            tags,
            kind,
            visibility,
            createdAt,
            evt.Timestamp,
            generation,
            head,
            messageIds.Count,
            fork,
            runtimeChild,
            metadata);
    }

    private static IReadOnlyDictionary<string, object> CopyMetadata(Dictionary<string, object>? metadata)
        => metadata is null
            ? new Dictionary<string, object>(StringComparer.Ordinal)
            : new Dictionary<string, object>(metadata, StringComparer.Ordinal);

    private static void TrackMessage(ISet<string> messageIds, AgentEvent evt)
    {
        switch (evt)
        {
            case ContentAddedEvent content when !string.IsNullOrWhiteSpace(content.MessageId):
                messageIds.Add(content.MessageId);
                break;
            case TextMessageStartEvent text when !string.IsNullOrWhiteSpace(text.MessageId):
                messageIds.Add(text.MessageId);
                break;
            case ReasoningMessageStartEvent reasoning when !string.IsNullOrWhiteSpace(reasoning.MessageId):
                messageIds.Add(reasoning.MessageId);
                break;
            case ToolCallStartEvent tool when !string.IsNullOrWhiteSpace(tool.MessageId):
                messageIds.Add(tool.MessageId);
                break;
            case ToolCallResultEvent result when !string.IsNullOrWhiteSpace(result.MessageId):
                messageIds.Add(result.MessageId);
                break;
            case ThreadHistoryCompactionCheckpointEvent compaction:
                break;
        }
    }

    private static ThreadForkDescriptor? CreateFork(string? source, string? messageId, int? messageIndex)
        => string.IsNullOrWhiteSpace(source) ? null : new ThreadForkDescriptor(source, messageId, messageIndex);

    private static ThreadRuntimeChildDescriptor? CreateRuntimeChild(
        string? parentSessionId,
        string? parentThreadId,
        string? subAgentName,
        string? subAgentTaskName,
        string? invocationId,
        string? subAgentSourceKind,
        string? parentToolCallId,
        string? sessionPolicy,
        string? threadPolicy)
        => parentSessionId is null && parentThreadId is null && subAgentName is null
            ? null
            : new ThreadRuntimeChildDescriptor(parentSessionId, parentThreadId, subAgentName, subAgentTaskName, invocationId,
                subAgentSourceKind, parentToolCallId, sessionPolicy, threadPolicy, Status: null);
}
