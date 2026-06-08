namespace HPD.Agent.TUI.Models;

public sealed record TranscriptEntryMetadata(
    string? AgentId = null,
    string? AgentName = null,
    string? ParentAgentId = null,
    IReadOnlyList<string>? AgentChain = null,
    int AgentDepth = 0)
{
    public IReadOnlyList<string> AgentChainValue => AgentChain ?? [];

    public static TranscriptEntryMetadata FromEvent(AgentEvent evt)
    {
        ArgumentNullException.ThrowIfNull(evt);

        return new TranscriptEntryMetadata(
            evt.Metadata?.AgentId,
            evt.Metadata?.AgentName,
            evt.Metadata?.ParentAgentId,
            evt.Metadata?.AgentChain,
            evt.Metadata?.Depth ?? 0);
    }
}
