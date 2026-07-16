using HPD.Agent;
using HPD.Events;

namespace HPDOS.ToolHarnesses.Middleware;

public abstract record LanguageServerEvent : AgentEvent
{
    public override EventKind Kind { get; init; } = EventKind.Diagnostic;
    public required string Path { get; init; }
    public required string Uri { get; init; }
}

public sealed record LanguageServerDocumentOpenedEvent : LanguageServerEvent
{
    public required string LanguageId { get; init; }
    public int DocumentVersion { get; init; }
}

public sealed record LanguageServerDocumentChangedEvent : LanguageServerEvent
{
    public required string LanguageId { get; init; }
    public int DocumentVersion { get; init; }
}

public sealed record LanguageServerDocumentClosedEvent : LanguageServerEvent;

public sealed record LanguageServerDocumentSavedEvent : LanguageServerEvent;

public sealed record LanguageServerWatchedFileChangedEvent : LanguageServerEvent
{
    public required LanguageServerWatchedFileChangeKind ChangeKind { get; init; }
}

public sealed record LanguageServerDiagnosticsReceivedEvent : LanguageServerEvent
{
    public int ErrorCount { get; init; }
    public int WarningCount { get; init; }
    public int InformationCount { get; init; }
    public int HintCount { get; init; }
    public int DiagnosticSetCount { get; init; }
    public IReadOnlyList<LanguageServerDiagnosticSummary> Diagnostics { get; init; } = [];
    public bool DiagnosticsTruncated { get; init; }
}

public sealed record LanguageServerDiagnosticSummary
{
    public required string Path { get; init; }
    public required string ServerId { get; init; }
    public required LanguageServerDiagnosticSource Source { get; init; }
    public required LanguageServerDiagnosticSeverity Severity { get; init; }
    public int Line { get; init; }
    public int Character { get; init; }
    public string? Code { get; init; }
    public required string Message { get; init; }
}
