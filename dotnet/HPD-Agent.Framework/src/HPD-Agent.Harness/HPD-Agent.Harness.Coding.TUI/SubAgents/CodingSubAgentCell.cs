using HPD.Agent.TUI.Models;

namespace HPD.Agent.ToolHarness.Coding.TUI.SubAgents;

public sealed record CodingSubAgentCell(
    string CallId,
    string RoleName,
    string? TaskName,
    CodingSubAgentState State,
    SubAgentContextPolicy? ContextPolicy,
    AgentInvocationMode? Mode,
    string? Detail) : TranscriptCell;

public enum CodingSubAgentState
{
    Preparing,
    Running,
    Completed,
    Failed,
    Cancelled
}
