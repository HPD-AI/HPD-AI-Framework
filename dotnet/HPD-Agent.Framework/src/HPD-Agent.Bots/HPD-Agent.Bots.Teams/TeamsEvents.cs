namespace HPD.Agent.Bots.Teams;

public sealed record TeamsCardActionEvent(
    string ActionId,
    IReadOnlyDictionary<string, string> Values,
    string ThreadId);

public sealed record TeamsModalSubmitEvent(
    string ActionId,
    IReadOnlyDictionary<string, string> Values,
    string ThreadId);

public sealed record TeamsReactionEvent(
    string ThreadId,
    string? MessageId,
    string? Reaction,
    bool Added);

public sealed record TeamsMembersChangedEvent(
    string ThreadId,
    IReadOnlyList<string> AddedMembers,
    IReadOnlyList<string> RemovedMembers);
