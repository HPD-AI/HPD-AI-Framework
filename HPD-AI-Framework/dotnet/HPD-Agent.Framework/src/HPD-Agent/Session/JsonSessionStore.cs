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
///   │   │   └── branch.json   ← Branch event document projected into Branch
///   │   ├── formal/
///   │   │   └── branch.json
///   │   └── casual/
///   │       └── branch.json
///   ├── uncommitted.json       ← Crash recovery buffer (session-scoped, contains branchId)
///   └── assets/                ← Binary assets (session-scoped, shared by all branches)
///       ├── {assetId}.pdf
///       └── {assetId}.jpg
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

        var branchPath = GetBranchFilePath(sessionId, branchId);

        if (!File.Exists(branchPath))
            return Task.FromResult<Branch?>(null);

        lock (_lock)
        {
            var json = File.ReadAllText(branchPath);
            var document = JsonSerializer.Deserialize<BranchEventDocument>(json, SessionJsonContext.Combined.Options);
            var branch = document is null ? null : BranchProjector.Project(document);
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

        var branchPath = GetBranchFilePath(sessionId, branchId);

        if (!File.Exists(branchPath))
            return Task.FromResult<BranchEventDocument?>(null);

        lock (_lock)
        {
            var json = File.ReadAllText(branchPath);
            var document = JsonSerializer.Deserialize<BranchEventDocument>(json, SessionJsonContext.Combined.Options);
            return Task.FromResult(document);
        }
    }

    public Task SaveBranchDocumentAsync(
        BranchEventDocument document,
        long? expectedSequenceNumber = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(document);

        var branchPath = GetBranchFilePath(document.SessionId, document.BranchId);

        lock (_lock)
        {
            if (expectedSequenceNumber is not null && File.Exists(branchPath))
            {
                var existingJson = File.ReadAllText(branchPath);
                var existing = JsonSerializer.Deserialize<BranchEventDocument>(existingJson, SessionJsonContext.Combined.Options);
                if (existing is not null && existing.NextSequenceNumber - 1 != expectedSequenceNumber.Value)
                {
                    throw new InvalidOperationException(
                        $"Branch '{document.BranchId}' sequence mismatch. Expected {expectedSequenceNumber}, actual {existing.NextSequenceNumber - 1}.");
                }
            }

            var json = JsonSerializer.Serialize(document, SessionJsonContext.Combined.Options);
            WriteAtomically(branchPath, json);
        }

        return Task.CompletedTask;
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

        var branchPath = GetBranchFilePath(sessionId, branchId);

        lock (_lock)
        {
            BranchEventDocument document;
            if (File.Exists(branchPath))
            {
                var json = File.ReadAllText(branchPath);
                document = JsonSerializer.Deserialize<BranchEventDocument>(json, SessionJsonContext.Combined.Options)
                    ?? new BranchEventDocument { SessionId = sessionId, BranchId = branchId };
            }
            else
            {
                document = new BranchEventDocument
                {
                    SessionId = sessionId,
                    BranchId = branchId,
                    CreatedAt = evt.Timestamp
                };
            }

            if (expectedSequenceNumber is not null &&
                document.NextSequenceNumber - 1 != expectedSequenceNumber.Value)
            {
                throw new InvalidOperationException(
                    $"Branch '{branchId}' sequence mismatch. Expected {expectedSequenceNumber}, actual {document.NextSequenceNumber - 1}.");
            }

            evt = evt with
            {
                EventId = evt.EventId ?? Guid.NewGuid().ToString("N"),
                SessionId = evt.SessionId ?? sessionId,
                BranchId = evt.BranchId ?? branchId
            };
            evt.SequenceNumber = document.NextSequenceNumber;
            var events = document.Events.ToList();
            events.Add(evt);
            document = document with
            {
                UpdatedAt = evt.Timestamp,
                NextSequenceNumber = document.NextSequenceNumber + 1,
                Events = events
            };

            var outputJson = JsonSerializer.Serialize(document, SessionJsonContext.Combined.Options);
            WriteAtomically(branchPath, outputJson);
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
    // CONTENT STORAGE (Session-Scoped)
    // ═══════════════════════════════════════════════════════════════════

    /// <inheritdoc />
    public IContentStore? GetContentStore(string sessionId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);

        return new LocalFileContentStore(Path.Combine(_basePath, sessionId, "content"));
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

    private string GetBranchFilePath(string sessionId, string branchId)
        => Path.Combine(GetBranchDirectoryPath(sessionId, branchId), "branch.json");

    private string GetUncommittedTurnFilePath(string sessionId)
        => Path.Combine(GetSessionDirectoryPath(sessionId), "uncommitted.json");

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
