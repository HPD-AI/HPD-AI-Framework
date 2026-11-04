#nullable enable

namespace HPD.Environment.Contracts;

using System;

/// <summary>Identifies a logical workspace root for the lifetime of a sandbox session.</summary>
public readonly record struct WorkspaceRootId(string Value);

/// <summary>
/// A model-facing workspace address. The relative path is POSIX-style and is
/// always interpreted below its logical root.
/// </summary>
public readonly record struct WorkspacePath(
    WorkspaceRootId RootId,
    string RelativePath)
{
    public override string ToString() =>
        RelativePath.Length == 0
            ? $"@{RootId.Value}"
            : $"@{RootId.Value}/{RelativePath}";
}

/// <summary>
/// A backend-specific path used only by trusted HPD tooling such as language
/// servers and debug adapters.
/// </summary>
public readonly record struct WorkspaceExecutionPath(string Value);

/// <summary>
/// Identifies the content instance shared by process execution, content
/// projection, and workspace file access.
/// </summary>
public readonly record struct WorkspaceContentIdentity(
    string Value,
    ResourceRef<Workspace>? Resource = null)
{
    public ResourceGeneration? Generation => Resource?.Generation;
}

/// <summary>Describes path behavior that differs between execution backends.</summary>
public readonly record struct WorkspacePathSemantics(
    bool CaseSensitive,
    SymlinkEvaluationPolicy Symlinks);

/// <summary>Classifies an entry exposed by a workspace file store.</summary>
public enum WorkspaceEntryKind
{
    File,
    Directory,
    Symlink,
    Other,
}

/// <summary>
/// Content identity returned by reads and required for stale-write validation.
/// </summary>
public sealed record WorkspaceContentVersion(
    long? BackendRevision,
    Digest? ContentDigest,
    DateTimeOffset? ModifiedAt,
    ByteSize Size);

/// <summary>Controls create and optimistic-concurrency requirements for a mutation.</summary>
public sealed record WorkspaceWriteCondition
{
    /// <summary>Requires the target file not to exist before the mutation.</summary>
    public static WorkspaceWriteCondition RequireCreate { get; } = new()
    {
        CreateOnly = true,
    };

    /// <summary>Version observed before the mutation; omission disables the check.</summary>
    public WorkspaceContentVersion? ExpectedVersion { get; init; }

    /// <summary>Rejects the mutation when the target already exists.</summary>
    public bool CreateOnly { get; init; }

    /// <summary>Allows the mutation to create a missing target.</summary>
    public bool AllowCreate { get; init; } = true;
}

/// <summary>Bounds text reads by line and byte count.</summary>
public sealed record WorkspaceTextReadOptions
{
    /// <summary>One-based first line to return.</summary>
    public long StartLine { get; init; } = 1;

    /// <summary>Maximum lines to return, or null when bounded only by bytes.</summary>
    public int? MaxLines { get; init; }

    /// <summary>Maximum UTF-8 bytes returned by the operation.</summary>
    public int MaxBytes { get; init; } = 1_048_576;
}

/// <summary>Bounds a binary read to a byte range.</summary>
public sealed record WorkspaceByteReadOptions
{
    /// <summary>Zero-based byte offset at which to start reading.</summary>
    public long Offset { get; init; }

    /// <summary>Maximum bytes returned by the operation.</summary>
    public int MaxBytes { get; init; } = 1_048_576;
}

/// <summary>Bounds workspace directory enumeration.</summary>
public sealed record WorkspaceListOptions
{
    /// <summary>Includes descendants when true.</summary>
    public bool Recursive { get; init; }

    /// <summary>
    /// Optional backend-evaluated glob filter. Patterns are relative to the
    /// listed directory and use POSIX separators.
    /// </summary>
    public string? GlobPattern { get; init; }

    /// <summary>Maximum descendant depth, where direct children are depth one.</summary>
    public int? MaxDepth { get; init; }

    /// <summary>Includes hidden files and directories when true.</summary>
    public bool IncludeHidden { get; init; }

    /// <summary>Honors ignore files supported by the backend.</summary>
    public bool RespectIgnoreFiles { get; init; } = true;

    /// <summary>Maximum entries returned by the operation.</summary>
    public int MaxEntries { get; init; } = 10_000;

    /// <summary>Maximum backend enumeration duration.</summary>
    public TimeSpan Timeout { get; init; } = TimeSpan.FromSeconds(10);
}

/// <summary>Terminal state of one backend workspace listing.</summary>
public enum WorkspaceListStatus
{
    Completed,
    Truncated,
    TimedOut,
    Cancelled,
    Failed,
}

/// <summary>
/// Bounded directory entries plus completion state. Entries remain available
/// when a backend reports a cap, timeout, or failure.
/// </summary>
public sealed record WorkspaceListResultSet
{
    /// <summary>Entries accepted by the backend in enumeration order.</summary>
    public IReadOnlyList<WorkspaceDirectoryEntry> Entries { get; init; } = [];

    /// <summary>Terminal backend state for the listing.</summary>
    public WorkspaceListStatus Status { get; init; } =
        WorkspaceListStatus.Completed;

    /// <summary>Number of paths omitted because of backend ignore rules.</summary>
    public int IgnoredCount { get; init; }

    /// <summary>Optional diagnostic or truncation detail.</summary>
    public string? Message { get; init; }
}

/// <summary>
/// Defines a root-scoped text search. Implementations must not escape the
/// supplied logical root.
/// </summary>
public sealed record WorkspaceSearchRequest
{
    /// <summary>Logical root or subdirectory searched by the operation.</summary>
    public required WorkspacePath Root { get; init; }

    /// <summary>Literal or regular-expression text to find.</summary>
    public required string Query { get; init; }

    /// <summary>Selects match lines, matching files, or per-file counts.</summary>
    public WorkspaceSearchOutputMode OutputMode { get; init; } =
        WorkspaceSearchOutputMode.Matches;

    /// <summary>Optional relative glob restricting files in the search.</summary>
    public string? GlobPattern { get; init; }

    /// <summary>Additional relative globs restricting searched files.</summary>
    public IReadOnlyList<string> IncludeGlobs { get; init; } = [];

    /// <summary>Relative globs excluding files from the search.</summary>
    public IReadOnlyList<string> ExcludeGlobs { get; init; } = [];

    /// <summary>Controls backend matching case behavior.</summary>
    public WorkspaceSearchCaseMode CaseMode { get; init; } =
        WorkspaceSearchCaseMode.Smart;

    /// <summary>Interprets <see cref="Query"/> as literal text.</summary>
    public bool FixedStrings { get; init; }

    /// <summary>Requires the match to occupy a whole word.</summary>
    public bool WordRegexp { get; init; }

    /// <summary>
    /// Allows regular expressions to span lines. A multiline match reports
    /// its starting line and retains the complete matched text.
    /// </summary>
    public bool Multiline { get; init; }

    /// <summary>Includes hidden files when true.</summary>
    public bool IncludeHidden { get; init; }

    /// <summary>Honors ignore files supported by the backend.</summary>
    public bool RespectIgnoreFiles { get; init; } = true;

    /// <summary>Maximum recursive directory depth.</summary>
    public int? MaxDepth { get; init; }

    /// <summary>Maximum matches returned per file.</summary>
    public int? MaxMatchesPerFile { get; init; }

    /// <summary>Context lines returned before each content match.</summary>
    public int BeforeContext { get; init; }

    /// <summary>Context lines returned after each content match.</summary>
    public int AfterContext { get; init; }

    /// <summary>Maximum text columns retained per match line.</summary>
    public int? MaxColumns { get; init; }

    /// <summary>Maximum source file size searched.</summary>
    public long? MaxFileSizeBytes { get; init; }

    /// <summary>Maximum backend search duration.</summary>
    public TimeSpan Timeout { get; init; } = TimeSpan.FromSeconds(20);

    /// <summary>Maximum matches returned by the operation.</summary>
    public int MaxResults { get; init; } = 1_000;
}

/// <summary>Output projection produced by a workspace text search.</summary>
public enum WorkspaceSearchOutputMode
{
    Matches,
    FilesWithMatches,
    Count,
}

/// <summary>Case behavior for portable workspace text search.</summary>
public enum WorkspaceSearchCaseMode
{
    Smart,
    Sensitive,
    Insensitive,
}

/// <summary>Terminal state of one backend workspace text search.</summary>
public enum WorkspaceSearchStatus
{
    Completed,
    NoMatches,
    Truncated,
    TimedOut,
    Cancelled,
    Failed,
}

/// <summary>
/// Bounded search results plus a backend completion state. Partial results
/// remain available when a backend reports truncation, timeout, or failure.
/// </summary>
public sealed record WorkspaceSearchResultSet
{
    /// <summary>Matches retained by the backend in result order.</summary>
    public IReadOnlyList<WorkspaceSearchResult> Results { get; init; } = [];

    /// <summary>Files retained for a files-with-matches search.</summary>
    public IReadOnlyList<WorkspacePath> Files { get; init; } = [];

    /// <summary>Per-file counts retained for a count search.</summary>
    public IReadOnlyList<WorkspaceSearchCount> Counts { get; init; } = [];

    /// <summary>Terminal backend state for the search.</summary>
    public WorkspaceSearchStatus Status { get; init; } =
        WorkspaceSearchStatus.Completed;

    /// <summary>Number of files omitted because of backend ignore rules.</summary>
    public int IgnoredFileCount { get; init; }

    /// <summary>Optional diagnostic or truncation detail.</summary>
    public string? Message { get; init; }
}

/// <summary>Number of accepted matches in one workspace file.</summary>
public sealed record WorkspaceSearchCount
{
    /// <summary>Logical path of the counted file.</summary>
    public required WorkspacePath Path { get; init; }

    /// <summary>Number of non-overlapping matches in the file.</summary>
    public required long Count { get; init; }
}

/// <summary>Metadata for a workspace entry.</summary>
public sealed record WorkspaceFileInfo
{
    public required WorkspacePath Path { get; init; }
    public required WorkspaceEntryKind Kind { get; init; }
    public required WorkspaceContentVersion Version { get; init; }
}

/// <summary>A bounded text read with its content version.</summary>
public sealed record WorkspaceTextSnapshot
{
    public required WorkspacePath Path { get; init; }
    public required string Content { get; init; }
    public required WorkspaceContentVersion Version { get; init; }
    public long StartLine { get; init; } = 1;
    public long EndLine { get; init; }
    public long TotalLines { get; init; }
    public bool Truncated { get; init; }
}

/// <summary>A bounded binary read with its content version.</summary>
public sealed record WorkspaceBinarySnapshot
{
    public required WorkspacePath Path { get; init; }
    public required ReadOnlyMemory<byte> Content { get; init; }
    public required WorkspaceContentVersion Version { get; init; }
    public long Offset { get; init; }
    public bool Truncated { get; init; }
}

/// <summary>An entry returned by directory listing.</summary>
public sealed record WorkspaceDirectoryEntry
{
    public required WorkspacePath Path { get; init; }
    public required WorkspaceEntryKind Kind { get; init; }
    public required WorkspaceContentVersion Version { get; init; }
}

/// <summary>A single text-search match using a logical workspace path.</summary>
public sealed record WorkspaceSearchResult
{
    /// <summary>Logical path containing the match.</summary>
    public required WorkspacePath Path { get; init; }

    /// <summary>One-based starting line of the match.</summary>
    public required long Line { get; init; }

    /// <summary>One-based character column of the first match on the line.</summary>
    public required long Column { get; init; }

    /// <summary>Matched text, which can span lines in multiline mode.</summary>
    public required string LineText { get; init; }

    /// <summary>Requested context lines associated with this match.</summary>
    public IReadOnlyList<WorkspaceSearchContextLine> ContextLines { get; init; } =
        [];
}

/// <summary>One line of context attached to a text-search match.</summary>
public sealed record WorkspaceSearchContextLine
{
    /// <summary>One-based line number in the source file.</summary>
    public required long Line { get; init; }

    /// <summary>Line text without its line terminator.</summary>
    public required string Text { get; init; }
}

/// <summary>Outcome of a workspace mutation.</summary>
public enum WorkspaceMutationOutcome
{
    Succeeded,
    NotFound,
    Stale,
    AlreadyExists,
    AccessDenied,
    Unsupported,
    Failed,
}

/// <summary>
/// Mutation result including the new version or the version that caused a
/// stale-write conflict.
/// </summary>
public sealed record WorkspaceMutationResult
{
    public required WorkspaceMutationOutcome Outcome { get; init; }
    public required WorkspacePath Path { get; init; }
    public WorkspaceContentVersion? Version { get; init; }
    public WorkspaceContentVersion? ObservedVersion { get; init; }
    public string? Message { get; init; }
}

/// <summary>
/// Session-scoped file access for model-facing tools. Implementations operate
/// on the same logical content observed by the selected execution backend.
/// </summary>
public interface IWorkspaceFileStore
{
    /// <summary>Gets entry metadata, or null when the path does not exist.</summary>
    ValueTask<WorkspaceFileInfo?> GetInfoAsync(
        WorkspacePath path,
        CancellationToken cancellationToken = default);

    /// <summary>Reads a bounded text range plus its content version.</summary>
    ValueTask<WorkspaceTextSnapshot?> ReadTextAsync(
        WorkspacePath path,
        WorkspaceTextReadOptions options,
        CancellationToken cancellationToken = default);

    /// <summary>Reads a bounded binary range plus its content version.</summary>
    ValueTask<WorkspaceBinarySnapshot?> ReadBytesAsync(
        WorkspacePath path,
        WorkspaceByteReadOptions options,
        CancellationToken cancellationToken = default);

    /// <summary>Writes UTF-8 text using the supplied concurrency condition.</summary>
    ValueTask<WorkspaceMutationResult> WriteTextAsync(
        WorkspacePath path,
        string content,
        WorkspaceWriteCondition condition,
        CancellationToken cancellationToken = default);

    /// <summary>Writes bytes using the supplied concurrency condition.</summary>
    ValueTask<WorkspaceMutationResult> WriteBytesAsync(
        WorkspacePath path,
        ReadOnlyMemory<byte> content,
        WorkspaceWriteCondition condition,
        CancellationToken cancellationToken = default);

    /// <summary>Deletes an entry using the supplied concurrency condition.</summary>
    ValueTask<WorkspaceMutationResult> DeleteAsync(
        WorkspacePath path,
        WorkspaceWriteCondition condition,
        CancellationToken cancellationToken = default);

    /// <summary>Creates a directory and any required parents within the root.</summary>
    ValueTask CreateDirectoryAsync(
        WorkspacePath path,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists entries without escaping the logical root. The backend applies
    /// glob, depth, hidden-path, and ignore-file filtering before enforcing
    /// the entry cap.
    /// </summary>
    ValueTask<WorkspaceListResultSet> ListAsync(
        WorkspacePath directory,
        WorkspaceListOptions options,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Searches text files without escaping the requested logical root. The
    /// backend applies content filters, matching modes, output bounds, and
    /// context generation to the content visible to the execution unit.
    /// </summary>
    ValueTask<WorkspaceSearchResultSet> SearchTextAsync(
        WorkspaceSearchRequest request,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Translates logical workspace paths to backend-specific execution paths for
/// trusted HPD tooling.
/// </summary>
public interface IWorkspacePathMapper
{
    /// <summary>Maps a logical path to the effective backend path.</summary>
    WorkspaceExecutionPath ToExecutionPath(WorkspacePath path);

    /// <summary>Maps a backend path back to a logical path when it is in scope.</summary>
    WorkspacePath? TryFromExecutionPath(
        WorkspaceExecutionPath path);

    /// <summary>Creates the effective file URI used by trusted tooling.</summary>
    Uri ToExecutionFileUri(WorkspacePath path);

    /// <summary>Maps an effective file URI back to a logical path when it is in scope.</summary>
    WorkspacePath? TryFromExecutionFileUri(Uri uri);
}

/// <summary>
/// Provider-neutral input for opening the workspace content visible to one
/// execution unit. The source and destination are kept explicit so a provider
/// can reject requests whose file store would not match the process view.
/// </summary>
public sealed record WorkspaceFileStoreSpec
{
    /// <summary>Stable logical root identifier for the session.</summary>
    public required WorkspaceRootId RootId { get; init; }

    /// <summary>Content selected for the workspace projection.</summary>
    public required ContentSelector Source { get; init; }

    /// <summary>Trusted path where the execution unit sees the root.</summary>
    public required WorkspaceExecutionPath ExecutionPath { get; init; }

    /// <summary>Access granted to model-facing operations.</summary>
    public AccessMode Access { get; init; } = AccessMode.ReadOnly;

    /// <summary>Path behavior guaranteed by the provider.</summary>
    public required WorkspacePathSemantics PathSemantics { get; init; }
}

/// <summary>
/// Files and path mapping opened together for one sandbox session. The
/// optional lifetime is disposed after projections and before the execution
/// unit so provider-owned transport or file handles cannot outlive the session.
/// </summary>
public sealed record WorkspaceFileStoreAttachment : IAsyncDisposable
{
    /// <summary>Model-facing file operations over the selected content.</summary>
    public required IWorkspaceFileStore Files { get; init; }

    /// <summary>Trusted logical-to-execution path mapping.</summary>
    public required IWorkspacePathMapper Paths { get; init; }

    /// <summary>Effective path semantics reported by the provider.</summary>
    public required WorkspacePathSemantics PathSemantics { get; init; }

    /// <summary>Optional resource released when the workspace session closes.</summary>
    public IAsyncDisposable? Lifetime { get; init; }

    /// <summary>Releases the provider-owned file and transport handles.</summary>
    public ValueTask DisposeAsync() =>
        Lifetime?.DisposeAsync() ?? ValueTask.CompletedTask;
}

/// <summary>
/// Opens provider-owned workspace file access for one execution unit. A
/// provider must fail rather than substitute host filesystem access when it
/// cannot address the execution unit's effective workspace.
/// </summary>
public interface IWorkspaceFileStoreProvider
{
    /// <summary>Environment provider that owns the returned file operations.</summary>
    ProviderId ProviderId { get; }

    /// <summary>Opens a session-scoped attachment for the requested workspace.</summary>
    ValueTask<WorkspaceFileStoreAttachment> OpenAsync(
        WorkspaceFileStoreSpec spec,
        TargetHandle<ExecutionUnit>? unit,
        CancellationToken cancellationToken = default);
}
