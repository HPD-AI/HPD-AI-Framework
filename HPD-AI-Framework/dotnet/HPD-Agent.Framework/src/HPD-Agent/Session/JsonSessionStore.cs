using System.Text.Json;


namespace HPD.Agent;

/// <summary>
/// File-based session store using JSON files.
/// V3 Architecture: Separate storage for Session metadata and thread event documents.
/// </summary>
/// <remarks>
/// <para><b>Storage Structure:</b></para>
/// <code>
/// sessions/{sessionId}/
///   ├── session.json          ← Session metadata + session-scoped middleware state
///   ├── threads/              ← All conversation threads
///   │   ├── main/
///   │   │   ├── thread.meta.json      ← Event stream metadata + next sequence number
///   │   │   ├── thread.events.jsonl   ← Append-only thread event stream
///   │   │   └── thread.projection.json ← Lazy Thread projection cache
///   │   ├── formal/
///   │   │   ├── thread.meta.json
///   │   │   ├── thread.events.jsonl
///   │   │   └── thread.projection.json
///   │   └── casual/
///   │       ├── thread.meta.json
///   │       ├── thread.events.jsonl
///   │       └── thread.projection.json
/// </code>
/// </remarks>
public class JsonSessionStore : ISessionStore
{
    private readonly string _basePath;
    private readonly object _lock = new();

    public JsonSessionStore(string basePath)
    {
        _basePath = basePath ?? throw new ArgumentNullException(nameof(basePath));
        Directory.CreateDirectory(_basePath);
    }

    // ═══════════════════════════════════════════════════════════════════
    // SESSION PERSISTENCE ( Metadata only)
    // ═══════════════════════════════════════════════════════════════════

    public Task<Session?> LoadSessionAsync(
        string sessionId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);

        var sessionPath = GetSessionFilePath(sessionId);

        if (!File.Exists(sessionPath))
            return Task.FromResult<Session?>(null);

        lock (_lock)
        {
            var json = File.ReadAllText(sessionPath);
            var session = JsonSerializer.Deserialize(json, SessionJsonContext.Combined.Session);
            return Task.FromResult(session);
        }
    }

    public Task SaveSessionAsync(
        Session session,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(session);

        var sessionPath = GetSessionFilePath(session.Id);
        var json = JsonSerializer.Serialize(session, SessionJsonContext.Combined.Session);

        lock (_lock)
        {
            WriteAtomically(sessionPath, json);
        }

        return Task.CompletedTask;
    }

    public Task<List<string>> ListSessionIdsAsync(CancellationToken cancellationToken = default)
    {
        var sessionIds = new List<string>();

        if (Directory.Exists(_basePath))
        {
            var sessionDirs = Directory.GetDirectories(_basePath);
            sessionIds.AddRange(sessionDirs.Select(d => Path.GetFileName(d)!));
        }

        return Task.FromResult(sessionIds);
    }

    public Task DeleteSessionAsync(string sessionId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);

        lock (_lock)
        {
            var sessionDir = GetSessionDirectoryPath(sessionId);
            if (Directory.Exists(sessionDir))
            {
                Directory.Delete(sessionDir, recursive: true);
            }
        }

        return Task.CompletedTask;
    }

    // ═══════════════════════════════════════════════════════════════════
    // THREAD EVENT PERSISTENCE
    // ═══════════════════════════════════════════════════════════════════

    public Task<Thread?> LoadThreadAsync(
        string sessionId,
        string threadId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(threadId);

        var metadataPath = GetThreadMetadataFilePath(sessionId, threadId);
        var eventsPath = GetThreadEventsFilePath(sessionId, threadId);

        if (!File.Exists(metadataPath) && !File.Exists(eventsPath))
            return Task.FromResult<Thread?>(null);

        lock (_lock)
        {
            var projection = LoadThreadProjectionCacheNoLock(sessionId, threadId);
            Thread thread;
            long lastSequenceNumber;

            if (projection is not null)
            {
                thread = projection.Thread;
                lastSequenceNumber = projection.LastSequenceNumber;
            }
            else
            {
                thread = new Thread(sessionId, threadId);
                lastSequenceNumber = 0;
            }

            var tailEvents = ReadThreadEventsNoLock(sessionId, threadId)
                .Where(evt => evt.SequenceNumber > lastSequenceNumber)
                .ToList();

            if (tailEvents.Count > 0)
                ThreadProjector.Apply(thread, tailEvents);

            if (projection is null || tailEvents.Count > 0)
            {
                var metadata = LoadThreadMetadataNoLock(sessionId, threadId);
                var checkpointSequence = tailEvents.Count == 0
                    ? lastSequenceNumber
                    : tailEvents.Max(evt => evt.SequenceNumber);
                SaveThreadProjectionCacheNoLock(
                    metadata,
                    thread,
                    checkpointSequence,
                    checkpointSequence == 0 ? DateTimeOffset.UtcNow : tailEvents.Last().Timestamp);
            }

            return Task.FromResult(thread);
        }
    }

    public Task<ThreadEventDocument?> LoadThreadDocumentAsync(
        string sessionId,
        string threadId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(threadId);

        var metadataPath = GetThreadMetadataFilePath(sessionId, threadId);
        var eventsPath = GetThreadEventsFilePath(sessionId, threadId);

        if (!File.Exists(metadataPath) && !File.Exists(eventsPath))
            return Task.FromResult<ThreadEventDocument?>(null);

        lock (_lock)
        {
            var metadata = LoadThreadMetadataNoLock(sessionId, threadId);
            var events = ReadThreadEventsNoLock(sessionId, threadId).ToList();
            var document = new ThreadEventDocument
            {
                SessionId = metadata.SessionId,
                ThreadId = metadata.ThreadId,
                CreatedAt = metadata.CreatedAt,
                UpdatedAt = metadata.UpdatedAt,
                NextSequenceNumber = metadata.NextSequenceNumber,
                Events = events
            };
            ThreadEventValidation.RequireDocumentScope(document, sessionId, threadId);
            return Task.FromResult<ThreadEventDocument?>(document);
        }
    }

    public Task AppendThreadEventAsync(
        string sessionId,
        string threadId,
        AgentEvent evt,
        long? expectedSequenceNumber = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(threadId);
        ArgumentNullException.ThrowIfNull(evt);

        var eventsPath = GetThreadEventsFilePath(sessionId, threadId);

        lock (_lock)
        {
            var metadata = LoadThreadMetadataNoLock(sessionId, threadId, evt.Timestamp);

            if (expectedSequenceNumber is not null &&
                metadata.NextSequenceNumber - 1 != expectedSequenceNumber.Value)
            {
                throw new InvalidOperationException(
                    $"Thread '{threadId}' sequence mismatch. Expected {expectedSequenceNumber}, actual {metadata.NextSequenceNumber - 1}.");
            }

            evt = ThreadEventValidation.PrepareForAppend(sessionId, threadId, evt);
            evt.SequenceNumber = metadata.NextSequenceNumber;

            var directory = Path.GetDirectoryName(eventsPath);
            if (!string.IsNullOrEmpty(directory))
                Directory.CreateDirectory(directory);

            File.AppendAllText(
                eventsPath,
                JsonSerializer.Serialize(evt, ThreadEventJson.CompactOptions) + System.Environment.NewLine);

            metadata = metadata with
            {
                UpdatedAt = evt.Timestamp,
                NextSequenceNumber = metadata.NextSequenceNumber + 1
            };
            metadata = ApplyThreadHeader(metadata, evt);

            SaveThreadMetadataNoLock(metadata);

        }

        return Task.CompletedTask;
    }

    public async IAsyncEnumerable<AgentEvent> ReadThreadEventsAsync(
        string sessionId,
        string threadId,
        HPD.Events.ReplayReadOptions options,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var document = await LoadThreadDocumentAsync(sessionId, threadId, cancellationToken).ConfigureAwait(false);
        if (document is null)
            yield break;

        await foreach (var evt in document.Events.FilterByReplayOptions(options, cancellationToken).ConfigureAwait(false))
            yield return evt;
    }

    public Task<List<string>> ListThreadIdsAsync(
        string sessionId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);

        var threadIds = new List<string>();
        var threadsDir = GetThreadsDirectoryPath(sessionId);

        if (Directory.Exists(threadsDir))
        {
            var threadDirs = Directory.GetDirectories(threadsDir);
            threadIds.AddRange(threadDirs.Select(d => Path.GetFileName(d)!));
        }

        return Task.FromResult(threadIds);
    }

    public Task DeleteThreadAsync(
        string sessionId,
        string threadId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(threadId);

        lock (_lock)
        {
            var threadDir = GetThreadDirectoryPath(sessionId, threadId);
            if (Directory.Exists(threadDir))
            {
                Directory.Delete(threadDir, recursive: true);
            }
        }

        return Task.CompletedTask;
    }

    // ═══════════════════════════════════════════════════════════════════
    // CLEANUP
    // ═══════════════════════════════════════════════════════════════════

    public Task<int> DeleteInactiveSessionsAsync(
        TimeSpan inactivityThreshold,
        bool dryRun = false,
        CancellationToken cancellationToken = default)
    {
        var cutoff = DateTime.UtcNow - inactivityThreshold;
        var toDelete = new List<string>();

        lock (_lock)
        {
            if (!Directory.Exists(_basePath))
                return Task.FromResult(0);

            var sessionDirs = Directory.GetDirectories(_basePath);
            foreach (var sessionDir in sessionDirs)
            {
                var dirInfo = new DirectoryInfo(sessionDir);
                if (dirInfo.LastWriteTimeUtc < cutoff)
                {
                    toDelete.Add(sessionDir);
                }
            }

            if (!dryRun)
            {
                foreach (var sessionDir in toDelete)
                {
                    Directory.Delete(sessionDir, recursive: true);
                }
            }
        }

        return Task.FromResult(toDelete.Count);
    }

    // ═══════════════════════════════════════════════════════════════════
    // PRIVATE HELPER METHODS
    // ═══════════════════════════════════════════════════════════════════

    private string GetSessionDirectoryPath(string sessionId)
        => Path.Combine(_basePath, sessionId);

    private string GetSessionFilePath(string sessionId)
        => Path.Combine(GetSessionDirectoryPath(sessionId), "session.json");

    private string GetThreadsDirectoryPath(string sessionId)
        => Path.Combine(GetSessionDirectoryPath(sessionId), "threads");

    private string GetThreadDirectoryPath(string sessionId, string threadId)
        => Path.Combine(GetThreadsDirectoryPath(sessionId), threadId);

    private string GetThreadMetadataFilePath(string sessionId, string threadId)
        => Path.Combine(GetThreadDirectoryPath(sessionId, threadId), "thread.meta.json");

    private string GetThreadEventsFilePath(string sessionId, string threadId)
        => Path.Combine(GetThreadDirectoryPath(sessionId, threadId), "thread.events.jsonl");

    private string GetThreadProjectionCacheFilePath(string sessionId, string threadId)
        => Path.Combine(GetThreadDirectoryPath(sessionId, threadId), "thread.projection.json");

    private ThreadEventStreamMetadata LoadThreadMetadataNoLock(
        string sessionId,
        string threadId,
        DateTimeOffset? createdAt = null)
    {
        var metadataPath = GetThreadMetadataFilePath(sessionId, threadId);
        if (File.Exists(metadataPath))
        {
            var json = File.ReadAllText(metadataPath);
            var metadata = JsonSerializer.Deserialize(
                json,
                SessionJsonContext.Combined.ThreadEventStreamMetadata)
                ?? throw new InvalidDataException($"Thread metadata '{metadataPath}' is empty.");
            RequireMetadataScope(metadata, sessionId, threadId);
            return metadata;
        }

        var eventsPath = GetThreadEventsFilePath(sessionId, threadId);
        if (File.Exists(eventsPath))
        {
            throw new InvalidDataException(
                $"Thread event stream '{eventsPath}' is missing required metadata file '{metadataPath}'.");
        }

        var now = createdAt ?? DateTimeOffset.UtcNow;
        return new ThreadEventStreamMetadata
        {
            SessionId = sessionId,
            ThreadId = threadId,
            CreatedAt = now,
            UpdatedAt = now,
            NextSequenceNumber = 1
        };
    }

    private static ThreadEventStreamMetadata ApplyThreadHeader(
        ThreadEventStreamMetadata metadata,
        AgentEvent evt)
    {
        return evt switch
        {
            ThreadCreatedEvent data => metadata with
            {
                Name = data.Name,
                Description = data.Description,
                Tags = data.Tags,
                Kind = data.ThreadKind,
                Visibility = data.Visibility,
                ParentSessionId = data.ParentSessionId,
                ParentThreadId = data.ParentThreadId,
                SubAgentName = data.SubAgentName,
                SubAgentRunId = data.SubAgentRunId,
                SubAgentSourceKind = data.SubAgentSourceKind,
                ParentToolCallId = data.ParentToolCallId,
                SessionPolicy = data.SessionPolicy,
                ThreadPolicy = data.ThreadPolicy
            },
            ThreadMetadataUpdatedEvent data => metadata with
            {
                Name = data.Name,
                Description = data.Description,
                Tags = data.Tags,
                Kind = data.ThreadKind,
                Visibility = data.Visibility,
                ParentSessionId = data.ParentSessionId,
                ParentThreadId = data.ParentThreadId,
                SubAgentName = data.SubAgentName,
                SubAgentRunId = data.SubAgentRunId,
                SubAgentSourceKind = data.SubAgentSourceKind,
                ParentToolCallId = data.ParentToolCallId,
                SessionPolicy = data.SessionPolicy,
                ThreadPolicy = data.ThreadPolicy
            },
            MessageStartedEvent => metadata with { MessageCount = metadata.MessageCount + 1 },
            ThreadHistoryCompactedEvent data => metadata with { MessageCount = data.ReplacementMessages.Count },
            _ => metadata
        };
    }

    private static void RequireMetadataScope(
        ThreadEventStreamMetadata metadata,
        string sessionId,
        string threadId)
    {
        if (!StringComparer.Ordinal.Equals(metadata.SessionId, sessionId))
        {
            throw new InvalidDataException(
                $"Thread metadata session scope '{metadata.SessionId}' does not match requested session '{sessionId}'.");
        }

        if (!StringComparer.Ordinal.Equals(metadata.ThreadId, threadId))
        {
            throw new InvalidDataException(
                $"Thread metadata thread scope '{metadata.ThreadId}' does not match requested thread '{threadId}'.");
        }
    }

    private void SaveThreadMetadataNoLock(ThreadEventStreamMetadata metadata)
    {
        var metadataPath = GetThreadMetadataFilePath(metadata.SessionId, metadata.ThreadId);
        var json = JsonSerializer.Serialize(metadata, SessionJsonContext.Combined.ThreadEventStreamMetadata);
        WriteAtomically(metadataPath, json);
    }

    private List<AgentEvent> ReadThreadEventsNoLock(string sessionId, string threadId)
    {
        var eventsPath = GetThreadEventsFilePath(sessionId, threadId);
        var events = new List<AgentEvent>();
        if (!File.Exists(eventsPath))
            return events;

        foreach (var line in File.ReadLines(eventsPath))
        {
            if (string.IsNullOrWhiteSpace(line))
                continue;

            var evt = JsonSerializer.Deserialize<AgentEvent>(line, ThreadEventJson.Options)
                ?? throw new InvalidDataException($"Thread event stream '{eventsPath}' contains an empty event line.");
            evt = ThreadEventValidation.HydrateEventScope(sessionId, threadId, evt);
            ThreadEventValidation.RequirePersistableScope(sessionId, threadId, evt);
            events.Add(evt);
        }

        return events;
    }

    private ThreadProjectionCache? LoadThreadProjectionCacheNoLock(string sessionId, string threadId)
    {
        var projectionPath = GetThreadProjectionCacheFilePath(sessionId, threadId);
        if (!File.Exists(projectionPath))
            return null;

        var json = File.ReadAllText(projectionPath);
        var projection = JsonSerializer.Deserialize(
            json,
            SessionJsonContext.Combined.ThreadProjectionCache)
            ?? throw new InvalidDataException($"Thread projection cache '{projectionPath}' is empty.");

        if (!StringComparer.Ordinal.Equals(projection.SessionId, sessionId))
        {
            throw new InvalidDataException(
                $"Thread projection cache session scope '{projection.SessionId}' does not match requested session '{sessionId}'.");
        }

        if (!StringComparer.Ordinal.Equals(projection.ThreadId, threadId))
        {
            throw new InvalidDataException(
                $"Thread projection cache thread scope '{projection.ThreadId}' does not match requested thread '{threadId}'.");
        }

        return projection;
    }

    private void SaveThreadProjectionCacheNoLock(
        ThreadEventStreamMetadata metadata,
        Thread thread,
        long lastSequenceNumber,
        DateTimeOffset updatedAt)
    {
        var projection = new ThreadProjectionCache
        {
            SessionId = metadata.SessionId,
            ThreadId = metadata.ThreadId,
            LastSequenceNumber = lastSequenceNumber,
            CreatedAt = metadata.CreatedAt,
            UpdatedAt = updatedAt,
            Thread = thread
        };

        var projectionPath = GetThreadProjectionCacheFilePath(metadata.SessionId, metadata.ThreadId);
        var json = JsonSerializer.Serialize(projection, SessionJsonContext.Combined.ThreadProjectionCache);
        WriteAtomically(projectionPath, json);
    }

    private void RequireExpectedSequenceNoLock(string sessionId, string threadId, long expectedSequenceNumber)
    {
        var metadata = LoadThreadMetadataNoLock(sessionId, threadId);
        if (metadata.NextSequenceNumber - 1 != expectedSequenceNumber)
        {
            throw new InvalidOperationException(
                $"Thread '{threadId}' sequence mismatch. Expected {expectedSequenceNumber}, actual {metadata.NextSequenceNumber - 1}.");
        }
    }

    private void WriteAtomically(string filePath, string content)
    {
        var directory = Path.GetDirectoryName(filePath);
        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);

        var tempPath = filePath + ".tmp";
        File.WriteAllText(tempPath, content);
        File.Move(tempPath, filePath, overwrite: true);
    }
}
