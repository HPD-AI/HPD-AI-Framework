namespace HPD.Agent.ToolHarness.Coding.TUI.Exploration;

internal sealed class CodingExplorationStore
{
    public const string StateKey = "hpd.coding.exploration";

    private readonly Dictionary<string, CodingExplorationGroup> _groups = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _groupIdByCallId = new(StringComparer.Ordinal);

    public IReadOnlyCollection<CodingExplorationGroup> Groups => _groups.Values;

    public IReadOnlyList<CodingExplorationGroup> ActiveGroups
        => _groups.Values
            .Where(static group => group.IsActive)
            .OrderByDescending(static group => group.LastUpdatedAt)
            .ToArray();

    public IReadOnlyList<CodingExplorationGroup> RecentGroups
        => _groups.Values
            .OrderByDescending(static group => group.LastUpdatedAt)
            .Take(5)
            .ToArray();

    public CodingExplorationGroup GetOrCreateGroupForStart(string callId, string toolName, string? messageId)
    {
        var groupId = CreateGroupId(messageId, callId);
        if (!_groups.TryGetValue(groupId, out var group))
        {
            group = new CodingExplorationGroup(groupId, messageId);
            _groups[groupId] = group;
        }

        _groupIdByCallId[callId] = groupId;
        var operation = group.GetOrAdd(callId, toolName);
        operation.Status = CodingExplorationOperationStatus.Running;
        group.Touch();
        return group;
    }

    public bool TryGetOperation(string callId, out CodingExplorationGroup group, out CodingExplorationOperation operation)
    {
        if (_groupIdByCallId.TryGetValue(callId, out var groupId) &&
            _groups.TryGetValue(groupId, out group!))
        {
            operation = group.Operations.FirstOrDefault(item => string.Equals(item.CallId, callId, StringComparison.Ordinal))!;
            return operation is not null;
        }

        group = default!;
        operation = default!;
        return false;
    }

    public CodingExplorationGroup GetOrCreateGroupForResult(
        string callId,
        string toolName,
        string? messageId)
    {
        if (TryGetOperation(callId, out var existingGroup, out var existingOperation))
        {
            existingOperation.ToolName = toolName;
            existingGroup.Touch();
            return existingGroup;
        }

        return GetOrCreateGroupForStart(callId, toolName, messageId);
    }

    private static string CreateGroupId(string? messageId, string callId)
        => string.IsNullOrWhiteSpace(messageId)
            ? $"call:{callId}"
            : $"message:{messageId}";
}
