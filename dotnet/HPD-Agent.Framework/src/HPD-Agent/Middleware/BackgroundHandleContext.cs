using HPD.Agent;
using System.Text.Json.Serialization;

namespace HPD.Agent.Middleware;

/// <summary>
/// Describes a controllable background resource registered with the agent runtime.
/// </summary>
public sealed record BackgroundHandleDescriptor
{
    /// <summary>
    /// Gets the caller-supplied handle id. When omitted, the runtime generates one.
    /// </summary>
    public string? HandleId { get; init; }

    /// <summary>
    /// Gets the human-readable handle name.
    /// </summary>
    public required string Name { get; init; }

    /// <summary>
    /// Gets the generic kind of background resource.
    /// </summary>
    public required BackgroundHandleKind Kind { get; init; }

    /// <summary>
    /// Gets the source category that created the handle.
    /// </summary>
    public required BackgroundTaskSourceKind SourceKind { get; init; }

    /// <summary>
    /// Gets the source-specific id for correlation.
    /// </summary>
    public string? SourceId { get; init; }

    /// <summary>
    /// Gets the session id that owns the handle.
    /// </summary>
    public string? SessionId { get; init; }

    /// <summary>
    /// Gets the thread id that owns the handle.
    /// </summary>
    public string? ThreadId { get; init; }

    /// <summary>
    /// Gets the function invocation that created the handle.
    /// </summary>
    public FunctionInvocationSnapshot? Invocation { get; init; }

    /// <summary>
    /// Gets the operations supported by this handle.
    /// </summary>
    public BackgroundHandleOperation SupportedOperations { get; init; } =
        BackgroundHandleOperation.Status;

    /// <summary>
    /// Gets source-specific metadata associated with this handle.
    /// </summary>
    public IReadOnlyDictionary<string, string>? Metadata { get; init; }
}

/// <summary>
/// Runtime registration returned for a controllable background handle.
/// </summary>
/// <param name="HandleId">The runtime handle id.</param>
/// <param name="Name">The handle name.</param>
/// <param name="Kind">The handle kind.</param>
/// <param name="SourceKind">The source category.</param>
public sealed record BackgroundHandleRegistration(
    string HandleId,
    string Name,
    BackgroundHandleKind Kind,
    BackgroundTaskSourceKind SourceKind);

/// <summary>
/// Runtime-owned view of a registered background handle.
/// </summary>
/// <param name="HandleId">The runtime handle id.</param>
/// <param name="Descriptor">The normalized descriptor.</param>
/// <param name="Handle">The handle implementation.</param>
/// <param name="RegisteredAt">The time the handle was registered.</param>
public sealed record RegisteredBackgroundHandle(
    string HandleId,
    BackgroundHandleDescriptor Descriptor,
    IBackgroundHandle Handle,
    DateTimeOffset RegisteredAt);

/// <summary>
/// Generic category for a controllable background resource.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter<BackgroundHandleKind>))]
public enum BackgroundHandleKind
{
    /// <summary>A process or command handle.</summary>
    Process,

    /// <summary>An owned debugger session tree.</summary>
    DebugSession,

    /// <summary>A workflow handle.</summary>
    Workflow,

    /// <summary>An agent or subagent handle.</summary>
    Agent,

    /// <summary>An MCP long-running operation handle.</summary>
    McpOperation,

    /// <summary>A client-owned long-running tool operation.</summary>
    ClientToolOperation,

    /// <summary>A browser session handle.</summary>
    BrowserSession,

    /// <summary>A file watcher handle.</summary>
    FileWatcher,

    /// <summary>An export job handle.</summary>
    Export,

    /// <summary>An indexing job handle.</summary>
    IndexingJob,

    /// <summary>A runtime-owned miscellaneous handle.</summary>
    Runtime,

    /// <summary>An uncategorized handle.</summary>
    Other
}

/// <summary>
/// Operations that a background handle can support.
/// </summary>
[Flags]
[JsonConverter(typeof(JsonStringEnumConverter<BackgroundHandleOperation>))]
public enum BackgroundHandleOperation
{
    /// <summary>No operations are supported.</summary>
    None = 0,

    /// <summary>The handle can report status.</summary>
    Status = 1,

    /// <summary>The handle can be read or inspected.</summary>
    Read = 2,

    /// <summary>The handle can be stopped.</summary>
    Stop = 4,

    /// <summary>The handle can be cancelled.</summary>
    Cancel = 8,

    /// <summary>The handle can report artifacts.</summary>
    Artifacts = 16,

    /// <summary>The handle can expose event history or live events.</summary>
    Events = 32
}

/// <summary>
/// Minimal interface for a controllable background resource.
/// </summary>
public interface IBackgroundHandle
{
    /// <summary>
    /// Gets the current handle status.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token for the status read.</param>
    /// <returns>A snapshot of the handle state.</returns>
    ValueTask<BackgroundHandleSnapshot> GetStatusAsync(CancellationToken cancellationToken);
}

/// <summary>
/// Optional operation interface for handles that can return readable output.
/// </summary>
public interface IReadableBackgroundHandle : IBackgroundHandle
{
    /// <summary>
    /// Reads output or state from the handle.
    /// </summary>
    /// <param name="request">The read request.</param>
    /// <param name="cancellationToken">Cancellation token for the read.</param>
    /// <returns>The read result.</returns>
    ValueTask<BackgroundHandleReadResult> ReadAsync(
        BackgroundHandleReadRequest request,
        CancellationToken cancellationToken);
}

/// <summary>
/// Optional operation interface for handles that can be stopped.
/// </summary>
public interface IStoppableBackgroundHandle : IBackgroundHandle
{
    /// <summary>
    /// Stops the handle.
    /// </summary>
    /// <param name="request">The stop request.</param>
    /// <param name="cancellationToken">Cancellation token for the stop.</param>
    /// <returns>The stop result.</returns>
    ValueTask<BackgroundHandleStopResult> StopAsync(
        BackgroundHandleStopRequest request,
        CancellationToken cancellationToken);
}

/// <summary>
/// Optional operation interface for handles that can report artifacts.
/// </summary>
public interface IArtifactBackgroundHandle : IBackgroundHandle
{
    /// <summary>
    /// Gets artifacts associated with the handle.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token for artifact lookup.</param>
    /// <returns>The artifact result.</returns>
    ValueTask<BackgroundHandleArtifactResult> GetArtifactsAsync(CancellationToken cancellationToken);
}

/// <summary>
/// Snapshot of a background handle.
/// </summary>
public sealed record BackgroundHandleSnapshot
{
    /// <summary>Gets the handle id.</summary>
    public required string HandleId { get; init; }

    /// <summary>Gets the handle name.</summary>
    public required string Name { get; init; }

    /// <summary>Gets the handle kind.</summary>
    public required BackgroundHandleKind Kind { get; init; }

    /// <summary>Gets the source category.</summary>
    public required BackgroundTaskSourceKind SourceKind { get; init; }

    /// <summary>Gets the source-specific status.</summary>
    public required string Status { get; init; }

    /// <summary>Gets the source-specific id.</summary>
    public string? SourceId { get; init; }

    /// <summary>Gets the owning session id.</summary>
    public string? SessionId { get; init; }

    /// <summary>Gets the owning thread id.</summary>
    public string? ThreadId { get; init; }

    /// <summary>Gets when the handle started.</summary>
    public DateTimeOffset StartedAt { get; init; }

    /// <summary>Gets when the handle completed, if known.</summary>
    public DateTimeOffset? CompletedAt { get; init; }

    /// <summary>Gets handle metadata.</summary>
    public IReadOnlyDictionary<string, string>? Metadata { get; init; }

    /// <summary>Gets known artifacts associated with the handle.</summary>
    public IReadOnlyList<BackgroundHandleArtifact> Artifacts { get; init; } = [];
}

/// <summary>
/// Artifact associated with a background handle.
/// </summary>
public sealed record BackgroundHandleArtifact
{
    /// <summary>Gets the artifact kind.</summary>
    public required string Kind { get; init; }

    /// <summary>Gets the artifact path, if available.</summary>
    public string? Path { get; init; }

    /// <summary>Gets the artifact content id, if available.</summary>
    public string? ContentId { get; init; }

    /// <summary>Gets artifact metadata.</summary>
    public IReadOnlyDictionary<string, string>? Metadata { get; init; }
}

/// <summary>
/// Request for reading from a background handle.
/// </summary>
public sealed record BackgroundHandleReadRequest
{
    /// <summary>Gets the maximum number of trailing lines requested.</summary>
    public int? TailLines { get; init; }

    /// <summary>Gets operation-specific metadata.</summary>
    public IReadOnlyDictionary<string, string>? Metadata { get; init; }
}

/// <summary>
/// Result returned by a readable background handle.
/// </summary>
public sealed record BackgroundHandleReadResult
{
    /// <summary>Gets the current handle snapshot.</summary>
    public required BackgroundHandleSnapshot Snapshot { get; init; }

    /// <summary>Gets the readable text payload, if any.</summary>
    public string? Text { get; init; }

    /// <summary>Gets artifacts associated with this read.</summary>
    public IReadOnlyList<BackgroundHandleArtifact> Artifacts { get; init; } = [];

    /// <summary>Gets operation-specific metadata.</summary>
    public IReadOnlyDictionary<string, string>? Metadata { get; init; }
}

/// <summary>
/// Request for stopping a background handle.
/// </summary>
public sealed record BackgroundHandleStopRequest
{
    /// <summary>Gets the machine-readable stop reason.</summary>
    public string? Reason { get; init; }

    /// <summary>Gets operation-specific metadata.</summary>
    public IReadOnlyDictionary<string, string>? Metadata { get; init; }
}

/// <summary>
/// Result returned by a stoppable background handle.
/// </summary>
public sealed record BackgroundHandleStopResult
{
    /// <summary>Gets the current handle snapshot.</summary>
    public required BackgroundHandleSnapshot Snapshot { get; init; }

    /// <summary>Gets a source-specific completion kind.</summary>
    public string? CompletionKind { get; init; }

    /// <summary>Gets the exit code, when the stopped handle represents a process.</summary>
    public int? ExitCode { get; init; }

    /// <summary>Gets artifacts associated with this stop.</summary>
    public IReadOnlyList<BackgroundHandleArtifact> Artifacts { get; init; } = [];

    /// <summary>Gets operation-specific metadata.</summary>
    public IReadOnlyDictionary<string, string>? Metadata { get; init; }
}

/// <summary>
/// Result returned by an artifact-capable background handle.
/// </summary>
public sealed record BackgroundHandleArtifactResult
{
    /// <summary>Gets the current handle snapshot.</summary>
    public required BackgroundHandleSnapshot Snapshot { get; init; }

    /// <summary>Gets known artifacts associated with the handle.</summary>
    public IReadOnlyList<BackgroundHandleArtifact> Artifacts { get; init; } = [];
}

/// <summary>
/// Scope used to authorize handle lookup.
/// </summary>
public sealed record BackgroundHandleScope
{
    /// <summary>Gets the session id that must own the handle.</summary>
    public string? SessionId { get; init; }

    /// <summary>Gets the thread id that must own the handle.</summary>
    public string? ThreadId { get; init; }
}

/// <summary>
/// Query for listing registered background handles.
/// </summary>
public sealed record BackgroundHandleQuery
{
    /// <summary>Gets the session id filter.</summary>
    public string? SessionId { get; init; }

    /// <summary>Gets the thread id filter.</summary>
    public string? ThreadId { get; init; }

    /// <summary>Gets the handle kind filter.</summary>
    public BackgroundHandleKind? Kind { get; init; }

    /// <summary>Gets the source kind filter.</summary>
    public BackgroundTaskSourceKind? SourceKind { get; init; }

    /// <summary>Gets whether completed handles should be included.</summary>
    public bool IncludeCompleted { get; init; } = true;
}
