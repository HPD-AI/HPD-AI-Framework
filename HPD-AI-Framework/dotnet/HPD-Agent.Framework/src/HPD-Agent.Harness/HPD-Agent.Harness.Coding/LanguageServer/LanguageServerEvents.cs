using HPD.Agent;
using HPD.Events;

namespace HPDOS.ToolHarnesses.Middleware;

public abstract record LanguageServerEvent : AgentEvent
{
    public override EventKind Kind { get; init; } = EventKind.Diagnostic;
    public override bool ShouldPersistToBranch() => true;
    public required string Path { get; init; }
    public required string Uri { get; init; }
}

public sealed record LanguageServerDocumentOpenedEvent : LanguageServerEvent
{
    public required string LanguageId { get; init; }
    public int Version { get; init; }
}

public sealed record LanguageServerDocumentChangedEvent : LanguageServerEvent
{
    public required string LanguageId { get; init; }
    public int Version { get; init; }
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
    public int DiagnosticSetCount { get; init; }
}
