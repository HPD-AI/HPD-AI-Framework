using HPD.Agent;

namespace HPD.Agent.TUI.Composition;

public sealed record AgentTuiContribution<T>(
    string Key,
    T Value,
    HpdContributionOwner Owner,
    int Order = 0);
