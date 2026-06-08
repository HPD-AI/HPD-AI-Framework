using HPD.TUI.Core;

namespace HPD.Agent.TUI.Models;

public abstract record TranscriptCell;

public sealed record UserMessageCell(IComponent Body) : TranscriptCell
{
    public UserMessageCell(string text)
        : this(new HPD.TUI.Components.Text(text))
    {
    }
}

public sealed record AssistantMessageCell(
    string? Name,
    IComponent Body,
    bool IsStreaming = false) : TranscriptCell;

public sealed record ReasoningMessageCell(
    IComponent Body,
    bool IsStreaming = false) : TranscriptCell;

public sealed record NoticeCell(
    string Title,
    IComponent? Body = null,
    TranscriptSeverity Severity = TranscriptSeverity.Info) : TranscriptCell;

public sealed record ToolCallCell(
    string Name,
    TranscriptRunState State,
    IComponent? Summary = null,
    IComponent? Detail = null,
    string? StateDetail = null) : TranscriptCell;

public sealed record CustomComponentCell(
    string Label,
    IComponent Component,
    int Indent = 0) : TranscriptCell;
