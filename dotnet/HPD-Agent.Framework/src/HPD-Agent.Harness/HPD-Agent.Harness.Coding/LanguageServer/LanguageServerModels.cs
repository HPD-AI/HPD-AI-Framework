using HPD.Agent;

namespace HPDOS.ToolHarnesses.Middleware;

public interface ILanguageServerService : IAsyncDisposable
{
    ValueTask<IReadOnlyList<LanguageServerStatus>> GetStatusAsync(
        CancellationToken cancellationToken = default);

    ValueTask<bool> HasServerForFileAsync(
        string path,
        CancellationToken cancellationToken = default);

    ValueTask<LanguageServerDocumentResolution> ResolveDocumentAsync(
        string path,
        CancellationToken cancellationToken = default);

    ValueTask<LanguageServerOpenResult> OpenDocumentAsync(
        LanguageServerDocumentOpenRequest request,
        CancellationToken cancellationToken = default);

    ValueTask<LanguageServerChangeResult> ChangeDocumentAsync(
        LanguageServerDocumentChangeRequest request,
        CancellationToken cancellationToken = default);

    ValueTask SaveDocumentAsync(
        LanguageServerDocumentSaveRequest request,
        CancellationToken cancellationToken = default);

    ValueTask CloseDocumentAsync(
        LanguageServerDocumentCloseRequest request,
        CancellationToken cancellationToken = default);

    ValueTask NotifyWatchedFileChangedAsync(
        LanguageServerWatchedFileChangeRequest request,
        CancellationToken cancellationToken = default);

    ValueTask<IReadOnlyList<LanguageServerDiagnosticSet>> GetDiagnosticsAsync(
        LanguageServerDiagnosticRequest request,
        CancellationToken cancellationToken = default);
}

public interface ILanguageServerRegistryProvider
{
    IEnumerable<LanguageServerDefinition> GetAll();
}

public interface ILanguageServerProvider
{
    ValueTask<string?> ResolveRootAsync(
        LanguageServerRootContext context,
        CancellationToken cancellationToken = default);

    ValueTask<LanguageServerLaunchDescriptor?> ResolveLaunchAsync(
        LanguageServerLaunchContext context,
        CancellationToken cancellationToken = default);

    ValueTask<LanguageServerInitialization> CreateInitializationAsync(
        LanguageServerInitializationContext context,
        CancellationToken cancellationToken = default);
}

public interface ILanguageServerToolResolver
{
    ValueTask<string?> FindExecutableAsync(
        string name,
        string root,
        CancellationToken cancellationToken = default);

    ValueTask<string?> FindNodeModuleAsync(
        string modulePath,
        string root,
        CancellationToken cancellationToken = default);

    ValueTask<string?> FindLocalBinAsync(
        string name,
        string root,
        CancellationToken cancellationToken = default);
}

public sealed record LanguageServerOptions
{
    public bool Enabled { get; init; } = true;

    public IReadOnlyList<LanguageServerDefinition> Servers { get; init; } = [];

    /// <summary>Server IDs that are explicitly enabled even when their declaration is disabled by default.</summary>
    public IReadOnlySet<string> EnabledServers { get; init; }
        = new HashSet<string>(StringComparer.Ordinal);

    /// <summary>Server IDs that are disabled for this runtime.</summary>
    public IReadOnlySet<string> DisabledServers { get; init; }
        = new HashSet<string>(StringComparer.Ordinal);

    public IReadOnlySet<string> EnabledExperimentalServers { get; init; }
        = new HashSet<string>(StringComparer.Ordinal);

    public LanguageServerFeedbackOptions Feedback { get; init; } = new();

    public IReadOnlyList<string> WorkspaceFolders { get; init; } = [];

    public IReadOnlyDictionary<string, object?> WorkspaceConfiguration { get; init; }
        = new Dictionary<string, object?>(StringComparer.Ordinal);

    public int ConfigVersion { get; init; }
}

public sealed record LanguageServerFeedbackOptions
{
    public bool Enabled { get; init; } = true;
    public bool ShowErrors { get; init; } = true;
    public bool ShowWarnings { get; init; }
    public bool ShowInformation { get; init; }
    public int MaxErrorsPerFile { get; init; } = 5;
    public int MaxWarningsPerFile { get; init; } = 3;
    public int MaxFeedbackCharacters { get; init; } = 8000;
    public bool IncludeHoverInfo { get; init; } = true;
    public bool IncludeCodeActions { get; init; } = true;
    public bool QuickFixesOnly { get; init; } = true;
    public TimeSpan SemanticContextTimeout { get; init; } = TimeSpan.FromSeconds(3);
}

public sealed record LanguageServerDefinition
{
    public required string Id { get; init; }
    public required IReadOnlyList<string> Extensions { get; init; }
    public IReadOnlyDictionary<string, string> LanguageIds { get; init; }
        = new Dictionary<string, string>(StringComparer.Ordinal);
    public required ILanguageServerProvider Provider { get; init; }
    public bool EnabledByDefault { get; init; } = true;
    public bool Experimental { get; init; }
}

public sealed record LanguageServerLaunchDescriptor
{
    public required string FileName { get; init; }
    public IReadOnlyList<string> Arguments { get; init; } = [];
    public IReadOnlyDictionary<string, string> Environment { get; init; }
        = new Dictionary<string, string>(StringComparer.Ordinal);
    public string? WorkingDirectory { get; init; }
    public IReadOnlyDictionary<string, object?> InitializationOptions { get; init; }
        = new Dictionary<string, object?>(StringComparer.Ordinal);
}

public sealed record LanguageServerInitialization
{
    public IReadOnlyDictionary<string, object?> InitializationOptions { get; init; }
        = new Dictionary<string, object?>(StringComparer.Ordinal);

    public IReadOnlyDictionary<string, object?> WorkspaceConfiguration { get; init; }
        = new Dictionary<string, object?>(StringComparer.Ordinal);

    public IReadOnlyDictionary<string, object?> ClientCapabilities { get; init; }
        = new Dictionary<string, object?>(StringComparer.Ordinal);
}

public sealed record LanguageServerRootContext
{
    public required string Path { get; init; }
    public required string WorkspaceRoot { get; init; }
    public required LanguageServerDefinition Definition { get; init; }
    public required LanguageServerOptions Options { get; init; }
}

public sealed record LanguageServerLaunchContext
{
    public required string Root { get; init; }
    public required string WorkspaceRoot { get; init; }
    public required LanguageServerDefinition Definition { get; init; }
    public required LanguageServerOptions Options { get; init; }
    public required ILanguageServerToolResolver ToolResolver { get; init; }
}

public sealed record LanguageServerInitializationContext
{
    public required string Root { get; init; }
    public required string WorkspaceRoot { get; init; }
    public required LanguageServerDefinition Definition { get; init; }
    public required LanguageServerOptions Options { get; init; }
    public required ILanguageServerToolResolver ToolResolver { get; init; }
}

public sealed record LanguageServerStatus
{
    public required string ServerId { get; init; }
    public required string Root { get; init; }
    public required LanguageServerStatusKind Status { get; init; }
    public string? Message { get; init; }
}

public sealed record LanguageServerDocumentResolution
{
    public required string Path { get; init; }
    public required string Uri { get; init; }
    public IReadOnlyList<LanguageServerResolvedServer> Servers { get; init; } = [];
    public bool HasServers => Servers.Count > 0;
    public string? PrimaryLanguageId => Servers.Count == 0 ? null : Servers[0].LanguageId;
}

public sealed record LanguageServerResolvedServer
{
    public required string ServerId { get; init; }
    public required string Root { get; init; }
    public required string LanguageId { get; init; }
    public int ConfigVersion { get; init; }
}

public enum LanguageServerStatusKind
{
    Starting,
    Running,
    Unavailable,
    Stopped
}

public sealed record LanguageServerDocumentOpenRequest
{
    public required string Path { get; init; }
    public required string Uri { get; init; }
    public required string LanguageId { get; init; }
    public required string Text { get; init; }
    public int Version { get; init; }
    public string PositionEncoding { get; init; } = "utf-16";
}

public sealed record LanguageServerOpenResult
{
    public required string Path { get; init; }
    public required string Uri { get; init; }
    public required string LanguageId { get; init; }
    public int Version { get; init; }
    public bool Opened { get; init; }
    public string PositionEncoding { get; init; } = "utf-16";
    public IReadOnlyList<LanguageServerDiagnosticSet> Diagnostics { get; init; } = [];
    public string? Message { get; init; }
}

public sealed record LanguageServerDocumentChangeRequest
{
    public required string Path { get; init; }
    public required string Uri { get; init; }
    public required string Text { get; init; }
    public int Version { get; init; }
}

public sealed record LanguageServerChangeResult
{
    public required string Path { get; init; }
    public int Version { get; init; }
    public IReadOnlyList<LanguageServerDiagnosticSet> Diagnostics { get; init; } = [];
}

public sealed record LanguageServerDocumentSaveRequest
{
    public required string Path { get; init; }
    public required string Uri { get; init; }
    public string? Text { get; init; }
}

public sealed record LanguageServerDocumentCloseRequest
{
    public required string Path { get; init; }
    public required string Uri { get; init; }
}

public sealed record LanguageServerWatchedFileChangeRequest
{
    public required string Path { get; init; }
    public required string Uri { get; init; }
    public required LanguageServerWatchedFileChangeKind Kind { get; init; }
}

public enum LanguageServerWatchedFileChangeKind
{
    Created = 1,
    Changed = 2,
    Deleted = 3
}

public sealed record LanguageServerDiagnosticRequest
{
    public required string Path { get; init; }
    public required string Uri { get; init; }
    public required LanguageServerDiagnosticMode Mode { get; init; }
    public required int DocumentVersion { get; init; }
    public required DateTimeOffset StartedAt { get; init; }
    public TimeSpan Timeout { get; init; } = TimeSpan.FromSeconds(5);
    public TimeSpan RequestTimeout { get; init; } = TimeSpan.FromSeconds(3);
    public TimeSpan Debounce { get; init; } = TimeSpan.FromMilliseconds(150);
}

public enum LanguageServerDiagnosticMode
{
    None,
    Document,
    Workspace
}

[MiddlewareState(Persistent = true, Scope = StateScope.Session)]
public sealed record LanguageServerState
{
    public IReadOnlyDictionary<string, LanguageServerDocumentSnapshot> DocumentsByPath { get; init; }
        = new Dictionary<string, LanguageServerDocumentSnapshot>(StringComparer.Ordinal);

    public IReadOnlyDictionary<string, LanguageServerDiagnosticSet> DiagnosticsByPath { get; init; }
        = new Dictionary<string, LanguageServerDiagnosticSet>(StringComparer.Ordinal);

    public IReadOnlyList<LanguageServerPendingFeedback> PendingFeedback { get; init; } = [];

    public IReadOnlyList<LanguageServerPendingOperation> PendingOperations { get; init; } = [];

    public IReadOnlyDictionary<string, LanguageServerUnavailableServer> UnavailableServers { get; init; }
        = new Dictionary<string, LanguageServerUnavailableServer>(StringComparer.Ordinal);
}

public sealed record LanguageServerDocumentSnapshot
{
    public required string Path { get; init; }
    public required string Uri { get; init; }
    public required string LanguageId { get; init; }
    public int Version { get; init; }
    public string PositionEncoding { get; init; } = "utf-16";
    public string? ContentHash { get; init; }
    public DateTimeOffset? LastWriteTimeUtc { get; init; }
    public bool Opened { get; init; }
    public bool DirtySinceLastDiagnostics { get; init; }
    public DateTimeOffset LastObservedAt { get; init; }
}

public sealed record LanguageServerDiagnosticSet
{
    public required string Path { get; init; }
    public required string ServerId { get; init; }
    public required LanguageServerDiagnosticSource Source { get; init; }
    public int? Version { get; init; }
    public string? ResultId { get; init; }
    public IReadOnlyList<LanguageServerDiagnostic> Diagnostics { get; init; } = [];
    public DateTimeOffset ReceivedAt { get; init; }
    public bool Partial { get; init; }
}

public enum LanguageServerDiagnosticSource
{
    Publish,
    DocumentPull,
    WorkspacePull
}

public sealed record LanguageServerDiagnostic
{
    public required LanguageServerDiagnosticSeverity Severity { get; init; }
    public int Line { get; init; }
    public int Character { get; init; }
    public string? Code { get; init; }
    public required string Message { get; init; }
}

public enum LanguageServerDiagnosticSeverity
{
    Error = 1,
    Warning = 2,
    Information = 3,
    Hint = 4
}

public sealed record LanguageServerPendingFeedback
{
    public required string Id { get; init; }
    public required LanguageServerDiagnosticSet DiagnosticSet { get; init; }
    public bool Injected { get; init; }
    public DateTimeOffset CreatedAt { get; init; }
}

public sealed record LanguageServerPendingOperation
{
    public required string CallId { get; init; }
    public required string ToolName { get; init; }
    public string? Path { get; init; }
    public DateTimeOffset ObservedAt { get; init; }
}

public sealed record LanguageServerUnavailableServer
{
    public required string ServerId { get; init; }
    public required string Root { get; init; }
    public int ConfigVersion { get; init; }
    public string? Reason { get; init; }
    public DateTimeOffset LastAttemptedAt { get; init; }
}

public sealed record LanguageServerClientCapabilities
{
    public bool OpenClose { get; init; }
    public LanguageServerTextDocumentSyncKind Change { get; init; } = LanguageServerTextDocumentSyncKind.None;
    public bool Save { get; init; }
    public bool IncludeTextOnSave { get; init; }
    public string PositionEncoding { get; init; } = "utf-16";
    public bool DocumentDiagnostics { get; init; }
    public bool WorkspaceDiagnostics { get; init; }
}

public enum LanguageServerTextDocumentSyncKind
{
    None = 0,
    Full = 1,
    Incremental = 2
}
