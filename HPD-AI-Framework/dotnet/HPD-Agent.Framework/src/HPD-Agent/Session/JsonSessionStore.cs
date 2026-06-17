using System.Text.Json;


namespace HPD.Agent;

/// <summary>
/// File-based session store using JSON files.
/// V3 Architecture: Separate storage for Session metadata and branch event documents.
/// </summary>
/// <remarks>
/// <para><b>Storage Structure:</b></para>
/// <code>
/// sessions/{sessionId}/
///   ├── session.json          ← Session metadata + session-scoped middleware state
///   ├── branches/              ← All conversation branches
///   │   ├── main/
///   │   │   ├── branch.meta.json      ← Event stream metadata + next sequence number
///   │   │   ├── branch.events.jsonl   ← Append-only branch event stream
///   │   │   └── branch.projection.json ← Lazy Branch projection cache
///   │   ├── formal/
///   │   │   ├── branch.meta.json
///   │   │   ├── branch.events.jsonl
///   │   │   └── branch.projection.json
///   │   └── casual/
///   │       ├── branch.meta.json
///   │       ├── branch.events.jsonl
///   │       └── branch.projection.json
///   └── uncommitted.json       ← Crash recovery buffer (session-scoped, contains branchId)
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
    // BRANCH EVENT PERSISTENCE
    // ═══════════════════════════════════════════════════════════════════

    public Task<Branch?> LoadBranchAsync(
        string sessionId,
        string branchId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(branchId);

        var metadataPath = GetBranchMetadataFilePath(sessionId, branchId);
        var eventsPath = GetBranchEventsFilePath(sessionId, branchId);

        if (!File.Exists(metadataPath) && !File.Exists(eventsPath))
            return Task.FromResult<Branch?>(null);

        lock (_lock)
        {
            var projection = LoadBranchProjectionCacheNoLock(sessionId, branchId);
            Branch branch;
            long lastSequenceNumber;

            if (projection is not null)
            {
                branch = projection.Branch;
                lastSequenceNumber = projection.LastSequenceNumber;
            }
            else
            {
                branch = new Branch(sessionId, branchId);
                lastSequenceNumber = 0;
            }

            var tailEvents = ReadBranchEventsNoLock(sessionId, branchId)
                .Where(evt => evt.SequenceNumber > lastSequenceNumber)
                .ToList();

            if (tailEvents.Count > 0)
                BranchProjector.Apply(branch, tailEvents);

            if (projection is null || tailEvents.Count > 0)
            {
                var metadata = LoadBranchMetadataNoLock(sessionId, branchId);
                var checkpointSequence = tailEvents.Count == 0
                    ? lastSequenceNumber
                    : tailEvents.Max(evt => evt.SequenceNumber);
                SaveBranchProjectionCacheNoLock(
                    metadata,
                    branch,
                    checkpointSequence,
                    checkpointSequence == 0 ? DateTimeOffset.UtcNow : tailEvents.Last().Timestamp);
            }

            return Task.FromResult(branch);
        }
    }

    public Task<BranchEventDocument?> LoadBranchDocumentAsync(
        string sessionId,
        string branchId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(branchId);

        var metadataPath = GetBranchMetadataFilePath(sessionId, branchId);
        var eventsPath = GetBranchEventsFilePath(sessionId, branchId);

        if (!File.Exists(metadataPath) && !File.Exists(eventsPath))
            return Task.FromResult<BranchEventDocument?>(null);

        lock (_lock)
        {
            var metadata = LoadBranchMetadataNoLock(sessionId, branchId);
            var events = ReadBranchEventsNoLock(sessionId, branchId).ToList();
            var document = new BranchEventDocument
            {
                SessionId = metadata.SessionId,
                BranchId = metadata.BranchId,
                CreatedAt = metadata.CreatedAt,
                UpdatedAt = metadata.UpdatedAt,
                NextSequenceNumber = metadata.NextSequenceNumber,
                Events = events
            };
            BranchEventValidation.RequireDocumentScope(document, sessionId, branchId);
            return Task.FromResult<BranchEventDocument?>(document);
        }
    }

    public Task AppendBranchEventAsync(
        string sessionId,
        string branchId,
        AgentEvent evt,
        long? expectedSequenceNumber = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(branchId);
        ArgumentNullException.ThrowIfNull(evt);

        var eventsPath = GetBranchEventsFilePath(sessionId, branchId);

        lock (_lock)
        {
            var metadata = LoadBranchMetadataNoLock(sessionId, branchId, evt.Timestamp);

            if (expectedSequenceNumber is not null &&
                metadata.NextSequenceNumber - 1 != expectedSequenceNumber.Value)
            {
                throw new InvalidOperationException(
                    $"Branch '{branchId}' sequence mismatch. Expected {expectedSequenceNumber}, actual {metadata.NextSequenceNumber - 1}.");
            }

            evt = BranchEventValidation.PrepareForAppend(sessionId, branchId, evt);
            evt.SequenceNumber = metadata.NextSequenceNumber;

            var directory = Path.GetDirectoryName(eventsPath);
            if (!string.IsNullOrEmpty(directory))
                Directory.CreateDirectory(directory);

            File.AppendAllText(
                eventsPath,
                JsonSerializer.Serialize(evt, BranchEventJson.CompactOptions) + System.Environment.NewLine);

            metadata = metadata with
            {
                UpdatedAt = evt.Timestamp,
                NextSequenceNumber = metadata.NextSequenceNumber + 1
            };
            metadata = ApplyBranchHeader(metadata, evt);

            SaveBranchMetadataNoLock(metadata);

        }

        return Task.CompletedTask;
    }

    public async IAsyncEnumerable<AgentEvent> ReadBranchEventsAsync(
        string sessionId,
        string branchId,
        HPD.Events.ReplayReadOptions options,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var document = await LoadBranchDocumentAsync(sessionId, branchId, cancellationToken).ConfigureAwait(false);
        if (document is null)
            yield break;

        await foreach (var evt in document.Events.FilterByReplayOptions(options, cancellationToken).ConfigureAwait(false))
            yield return evt;
    }

    public Task<List<string>> ListBranchIdsAsync(
        string sessionId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);

        var branchIds = new List<string>();
        var branchesDir = GetBranchesDirectoryPath(sessionId);

        if (Directory.Exists(branchesDir))
        {
            var branchDirs = Directory.GetDirectories(branchesDir);
            branchIds.AddRange(branchDirs.Select(d => Path.GetFileName(d)!));
        }

        return Task.FromResult(branchIds);
    }

    public Task DeleteBranchAsync(
        string sessionId,
        string branchId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(branchId);

        lock (_lock)
        {
            var branchDir = GetBranchDirectoryPath(sessionId, branchId);
            if (Directory.Exists(branchDir))
            {
                Directory.Delete(branchDir, recursive: true);
            }
        }

        return Task.CompletedTask;
    }

    // ═══════════════════════════════════════════════════════════════════
    // UNCOMMITTED TURN (Crash Recovery - session-scoped)
    // ═══════════════════════════════════════════════════════════════════

    public Task<UncommittedTurn?> LoadUncommittedTurnAsync(
        string sessionId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);

        var filePath = GetUncommittedTurnFilePath(sessionId);

        if (!File.Exists(filePath))
            return Task.FromResult<UncommittedTurn?>(null);

        lock (_lock)
        {
            var json = File.ReadAllText(filePath);
            var turn = JsonSerializer.Deserialize(json, SessionJsonContext.Combined.UncommittedTurn);
            return Task.FromResult(turn);
        }
    }

    public Task SaveUncommittedTurnAsync(
        UncommittedTurn turn,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(turn);

        var filePath = GetUncommittedTurnFilePath(turn.SessionId);
        var json = JsonSerializer.Serialize(turn, SessionJsonContext.Combined.UncommittedTurn);

        lock (_lock)
        {
            WriteAtomically(filePath, json);
        }

        return Task.CompletedTask;
    }

    public Task DeleteUncommittedTurnAsync(
        string sessionId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);

        var filePath = GetUncommittedTurnFilePath(sessionId);

        lock (_lock)
        {
            if (File.Exists(filePath))
                File.Delete(filePath);
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

    private string GetBranchesDirectoryPath(string sessionId)
        => Path.Combine(GetSessionDirectoryPath(sessionId), "branches");

    private string GetBranchDirectoryPath(string sessionId, string branchId)
        => Path.Combine(GetBranchesDirectoryPath(sessionId), branchId);

    private string GetBranchMetadataFilePath(string sessionId, string branchId)
        => Path.Combine(GetBranchDirectoryPath(sessionId, branchId), "branch.meta.json");

    private string GetBranchEventsFilePath(string sessionId, string branchId)
        => Path.Combine(GetBranchDirectoryPath(sessionId, branchId), "branch.events.jsonl");

    private string GetBranchProjectionCacheFilePath(string sessionId, string branchId)
        => Path.Combine(GetBranchDirectoryPath(sessionId, branchId), "branch.projection.json");

    private string GetUncommittedTurnFilePath(string sessionId)
        => Path.Combine(GetSessionDirectoryPath(sessionId), "uncommitted.json");

    private BranchEventStreamMetadata LoadBranchMetadataNoLock(
        string sessionId,
        string branchId,
        DateTimeOffset? createdAt = null)
    {
        var metadataPath = GetBranchMetadataFilePath(sessionId, branchId);
        if (File.Exists(metadataPath))
        {
            var json = File.ReadAllText(metadataPath);
            var metadata = JsonSerializer.Deserialize(
                json,
                SessionJsonContext.Combined.BranchEventStreamMetadata)
                ?? throw new InvalidDataException($"Branch metadata '{metadataPath}' is empty.");
            RequireMetadataScope(metadata, sessionId, branchId);
            return metadata;
        }

        var eventsPath = GetBranchEventsFilePath(sessionId, branchId);
        if (File.Exists(eventsPath))
        {
            throw new InvalidDataException(
                $"Branch event stream '{eventsPath}' is missing required metadata file '{metadataPath}'.");
        }

        var now = createdAt ?? DateTimeOffset.UtcNow;
        return new BranchEventStreamMetadata
        {
            SessionId = sessionId,
            BranchId = branchId,
            CreatedAt = now,
            UpdatedAt = now,
            NextSequenceNumber = 1
        };
    }

    private static BranchEventStreamMetadata ApplyBranchHeader(
        BranchEventStreamMetadata metadata,
        AgentEvent evt)
    {
        return evt switch
        {
            BranchCreatedEvent data => metadata with
            {
                Name = data.Name,
                Description = data.Description,
                Tags = data.Tags,
                Kind = data.BranchKind,
                Visibility = data.Visibility,
                ParentSessionId = data.ParentSessionId,
                ParentBranchId = data.ParentBranchId,
                SubAgentName = data.SubAgentName,
                SubAgentRunId = data.SubAgentRunId,
                SubAgentSourceKind = data.SubAgentSourceKind,
                ParentToolCallId = data.ParentToolCallId,
                SessionPolicy = data.SessionPolicy,
                BranchPolicy = data.BranchPolicy
            },
            BranchMetadataUpdatedEvent data => metadata with
            {
                Name = data.Name,
                Description = data.Description,
                Tags = data.Tags,
                Kind = data.BranchKind,
                Visibility = data.Visibility,
                ParentSessionId = data.ParentSessionId,
                ParentBranchId = data.ParentBranchId,
                SubAgentName = data.SubAgentName,
                SubAgentRunId = data.SubAgentRunId,
                SubAgentSourceKind = data.SubAgentSourceKind,
                ParentToolCallId = data.ParentToolCallId,
                SessionPolicy = data.SessionPolicy,
                BranchPolicy = data.BranchPolicy
            },
            MessageStartedEvent => metadata with { MessageCount = metadata.MessageCount + 1 },
            BranchHistoryCompactedEvent data => metadata with { MessageCount = data.ReplacementMessages.Count },
            _ => metadata
        };
    }

    private static void RequireMetadataScope(
        BranchEventStreamMetadata metadata,
        string sessionId,
        string branchId)
    {
        if (!StringComparer.Ordinal.Equals(metadata.SessionId, sessionId))
        {
            throw new InvalidDataException(
                $"Branch metadata session scope '{metadata.SessionId}' does not match requested session '{sessionId}'.");
        }

        if (!StringComparer.Ordinal.Equals(metadata.BranchId, branchId))
        {
            throw new InvalidDataException(
                $"Branch metadata branch scope '{metadata.BranchId}' does not match requested branch '{branchId}'.");
        }
    }

    private void SaveBranchMetadataNoLock(BranchEventStreamMetadata metadata)
    {
        var metadataPath = GetBranchMetadataFilePath(metadata.SessionId, metadata.BranchId);
        var json = JsonSerializer.Serialize(metadata, SessionJsonContext.Combined.BranchEventStreamMetadata);
        WriteAtomically(metadataPath, json);
    }

    private List<AgentEvent> ReadBranchEventsNoLock(string sessionId, string branchId)
    {
        var eventsPath = GetBranchEventsFilePath(sessionId, branchId);
        var events = new List<AgentEvent>();
        if (!File.Exists(eventsPath))
            return events;

        foreach (var line in File.ReadLines(eventsPath))
        {
            if (string.IsNullOrWhiteSpace(line))
                continue;

            var evt = JsonSerializer.Deserialize<AgentEvent>(line, BranchEventJson.Options)
                ?? throw new InvalidDataException($"Branch event stream '{eventsPath}' contains an empty event line.");
            evt = BranchEventValidation.HydrateEventScope(sessionId, branchId, evt);
            BranchEventValidation.RequirePersistableScope(sessionId, branchId, evt);
            events.Add(evt);
        }

        return events;
    }

    private BranchProjectionCache? LoadBranchProjectionCacheNoLock(string sessionId, string branchId)
    {
        var projectionPath = GetBranchProjectionCacheFilePath(sessionId, branchId);
        if (!File.Exists(projectionPath))
            return null;

        var json = File.ReadAllText(projectionPath);
        var projection = JsonSerializer.Deserialize(
            json,
            SessionJsonContext.Combined.BranchProjectionCache)
            ?? throw new InvalidDataException($"Branch projection cache '{projectionPath}' is empty.");

        if (!StringComparer.Ordinal.Equals(projection.SessionId, sessionId))
        {
            throw new InvalidDataException(
                $"Branch projection cache session scope '{projection.SessionId}' does not match requested session '{sessionId}'.");
        }

        if (!StringComparer.Ordinal.Equals(projection.BranchId, branchId))
        {
            throw new InvalidDataException(
                $"Branch projection cache branch scope '{projection.BranchId}' does not match requested branch '{branchId}'.");
        }

        return projection;
    }

    private void SaveBranchProjectionCacheNoLock(
        BranchEventStreamMetadata metadata,
        Branch branch,
        long lastSequenceNumber,
        DateTimeOffset updatedAt)
    {
        var projection = new BranchProjectionCache
        {
            SessionId = metadata.SessionId,
            BranchId = metadata.BranchId,
            LastSequenceNumber = lastSequenceNumber,
            CreatedAt = metadata.CreatedAt,
            UpdatedAt = updatedAt,
            Branch = branch
        };

        var projectionPath = GetBranchProjectionCacheFilePath(metadata.SessionId, metadata.BranchId);
        var json = JsonSerializer.Serialize(projection, SessionJsonContext.Combined.BranchProjectionCache);
        WriteAtomically(projectionPath, json);
    }

    private void RequireExpectedSequenceNoLock(string sessionId, string branchId, long expectedSequenceNumber)
    {
        var metadata = LoadBranchMetadataNoLock(sessionId, branchId);
        if (metadata.NextSequenceNumber - 1 != expectedSequenceNumber)
        {
            throw new InvalidOperationException(
                $"Branch '{branchId}' sequence mismatch. Expected {expectedSequenceNumber}, actual {metadata.NextSequenceNumber - 1}.");
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
