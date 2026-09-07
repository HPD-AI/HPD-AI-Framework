using HPD.TUI.Core;
using HPD.Agent.TUI.Markdown;

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
    MarkdownMessageDocument Document,
    MarkdownMessageProjection Projection) : TranscriptCell;

public sealed record ReasoningMessageCell(
    MarkdownMessageDocument Document,
    MarkdownMessageProjection Projection) : TranscriptCell;

public sealed record NoticeCell(
    string Title,
    IComponent? Body = null,
    TranscriptSeverity Severity = TranscriptSeverity.Info) : TranscriptCell;

public sealed record RunStatusCell(
    string ThreadExecutionId,
    TranscriptRunState State,
    string? Detail = null,
    TimeSpan? Duration = null,
    string? Hint = null) : TranscriptCell;

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
