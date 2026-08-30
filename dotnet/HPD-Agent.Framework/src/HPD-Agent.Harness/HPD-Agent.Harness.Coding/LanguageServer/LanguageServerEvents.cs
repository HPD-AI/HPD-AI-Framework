using HPD.Agent;
using HPD.Events;
using HPD.Agent.Serialization;

namespace HPDOS.ToolHarnesses.Middleware;

public abstract record LanguageServerEvent : AgentEvent
{
    public override EventKind Kind { get; init; } = EventKind.Diagnostic;
    public required string Path { get; init; }
    public required string Uri { get; init; }
}

/// <summary>Authoritative snapshot of language-server processes observed by the current runtime.</summary>
[EventType("LANGUAGE_SERVER_STATUS_SNAPSHOT", Durability = AgentEventDurability.Durable)]
public sealed record LanguageServerStatusSnapshotEvent : AgentEvent
{
    public override EventKind Kind { get; init; } = EventKind.Diagnostic;
    public IReadOnlyList<LanguageServerStatusSnapshot> Servers { get; init; } = [];
}

/// <summary>Serializable status of one activated language server.</summary>
public sealed record LanguageServerStatusSnapshot
{
    public required string ServerId { get; init; }
    public required string Root { get; init; }
    public required LanguageServerStatusKind Status { get; init; }
    public string? Message { get; init; }
}

[EventType("LANGUAGE_SERVER_DOCUMENT_OPENED", Durability = AgentEventDurability.Durable)]
public sealed record LanguageServerDocumentOpenedEvent : LanguageServerEvent
{
    public required string LanguageId { get; init; }
    public int DocumentVersion { get; init; }
}

[EventType("LANGUAGE_SERVER_DOCUMENT_CHANGED", Durability = AgentEventDurability.Durable)]
public sealed record LanguageServerDocumentChangedEvent : LanguageServerEvent
{
    public required string LanguageId { get; init; }
    public int DocumentVersion { get; init; }
}

[EventType("LANGUAGE_SERVER_DOCUMENT_CLOSED", Durability = AgentEventDurability.Durable)]
public sealed record LanguageServerDocumentClosedEvent : LanguageServerEvent;

[EventType("LANGUAGE_SERVER_DOCUMENT_SAVED", Durability = AgentEventDurability.Durable)]
public sealed record LanguageServerDocumentSavedEvent : LanguageServerEvent;

[EventType("LANGUAGE_SERVER_WATCHED_FILE_CHANGED", Durability = AgentEventDurability.Durable)]
public sealed record LanguageServerWatchedFileChangedEvent : LanguageServerEvent
{
    public required LanguageServerWatchedFileChangeKind ChangeKind { get; init; }
}

[EventType("LANGUAGE_SERVER_DIAGNOSTICS_RECEIVED", Durability = AgentEventDurability.Durable)]
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
