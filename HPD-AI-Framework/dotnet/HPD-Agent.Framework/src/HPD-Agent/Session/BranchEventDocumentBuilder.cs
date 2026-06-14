namespace HPD.Agent;

public static class BranchEventDocumentBuilder
{
    public static BranchEventDocument FromInitialBranch(string sessionId, Branch branch)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);
        ArgumentNullException.ThrowIfNull(branch);

        var createdAt = new DateTimeOffset(branch.CreatedAt, TimeSpan.Zero);
        var events = new List<AgentEvent>();
        if (branch.ForkedFrom is null)
        {
            events.Add(BranchEventFactory.BranchCreated(branch));
            if (!HasDefaultRootTreeState(branch))
                events.Add(BranchEventFactory.BranchTreeUpdated(branch));
        }
        else
        {
            events.Add(BranchEventFactory.BranchForked(branch));
            events.Add(BranchEventFactory.BranchMetadataUpdated(branch));
            events.Add(BranchEventFactory.BranchTreeUpdated(branch));
        }

        foreach (var message in branch.Messages)
            events.AddRange(BranchMessageEventConverter.ToBranchEvents(branch.SessionId, branch.Id, message));

        if (branch.MiddlewareState.Count > 0)
        {
            events.Add(BranchEventFactory.BranchMiddlewareStateCommitted(
                branch.SessionId,
                branch.Id,
                branch.MiddlewareState));
        }

        return Create(
            sessionId,
            branch.Id,
            events,
            createdAt,
            new DateTimeOffset(branch.LastActivity, TimeSpan.Zero));
    }

    private static bool HasDefaultRootTreeState(Branch branch) =>
        branch.SiblingIndex == 0 &&
        branch.TotalSiblings == 1 &&
        branch.IsOriginal &&
        branch.OriginalBranchId is null &&
        branch.PreviousSiblingId is null &&
        branch.NextSiblingId is null &&
        branch.ChildBranches.Count == 0;

    public static BranchEventDocument FromBranchSnapshot(string sessionId, Branch branch)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);
        ArgumentNullException.ThrowIfNull(branch);

        var createdAt = new DateTimeOffset(branch.CreatedAt, TimeSpan.Zero);
        var events = new List<AgentEvent>
        {
            branch.ForkedFrom is null
                ? BranchEventFactory.BranchCreated(branch)
                : BranchEventFactory.BranchForked(branch),
            BranchEventFactory.BranchMetadataUpdated(branch),
            BranchEventFactory.BranchTreeUpdated(branch)
        };

        foreach (var message in branch.Messages)
        {
            events.AddRange(BranchMessageEventConverter.ToBranchEvents(branch.SessionId, branch.Id, message));
        }

        if (branch.MiddlewareState.Count > 0)
        {
            events.Add(BranchEventFactory.BranchMiddlewareStateCommitted(
                branch.SessionId,
                branch.Id,
                branch.MiddlewareState));
        }

        return Create(sessionId, branch.Id, events, createdAt, new DateTimeOffset(branch.LastActivity, TimeSpan.Zero));
    }

    public static BranchEventDocument Create(
        string sessionId,
        string branchId,
        IEnumerable<AgentEvent> events,
        DateTimeOffset? createdAt = null,
        DateTimeOffset? updatedAt = null)
    {
        var sequenced = new List<AgentEvent>();
        var sequence = 1L;
        foreach (var evt in events)
        {
            evt.SequenceNumber = sequence++;
            sequenced.Add(evt);
        }

        var now = DateTimeOffset.UtcNow;
        var document = new BranchEventDocument
        {
            SessionId = sessionId,
            BranchId = branchId,
            CreatedAt = createdAt ?? sequenced.FirstOrDefault()?.Timestamp ?? now,
            UpdatedAt = updatedAt ?? sequenced.LastOrDefault()?.Timestamp ?? now,
            NextSequenceNumber = sequence,
            Events = sequenced
        };

        BranchEventValidation.RequireDocumentScope(document, sessionId, branchId);
        return document;
    }
}
