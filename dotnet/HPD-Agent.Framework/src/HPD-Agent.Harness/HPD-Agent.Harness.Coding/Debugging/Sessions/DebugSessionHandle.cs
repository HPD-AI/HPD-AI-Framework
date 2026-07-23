using System.Text;
using HPD.Agent.Middleware;

namespace HPD.Agent.ToolHarness.Coding.Debugging;

internal sealed class DebugSessionHandle :
    IReadableBackgroundHandle,
    IStoppableBackgroundHandle,
    IArtifactBackgroundHandle,
    IAsyncDisposable
{
    private readonly DebugSessionManager _manager;
    private readonly DebugTreeLookupScope _scope;
    private readonly string _treeId;
    private readonly DateTimeOffset _startedAt = DateTimeOffset.UtcNow;
    private string _handleId;
    private int _publicationState; // 0 starting, 1 live, 2 ended
    private int _disposed;

    public DebugSessionHandle(DebugSessionManager manager, DebugTreeLookupScope scope, string treeId)
    {
        _manager = manager;
        _scope = scope;
        _treeId = treeId;
        _handleId = treeId;
    }

    public void AttachRegistration(BackgroundHandleRegistration registration)
    {
        if (Volatile.Read(ref _publicationState) != 0)
            throw new InvalidOperationException("The debug handle is no longer starting.");
        _handleId = registration.HandleId;
    }

    public void CommitLive()
    {
        if (Interlocked.CompareExchange(ref _publicationState, 1, 0) != 0)
            throw new InvalidOperationException("The debug handle publication state is already settled.");
    }

    public void MarkPublicationFailed() => Interlocked.CompareExchange(ref _publicationState, 2, 0);

    public ValueTask<BackgroundHandleSnapshot> GetStatusAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromResult(CreateSnapshot());
    }

    public ValueTask<BackgroundHandleReadResult> ReadAsync(BackgroundHandleReadRequest request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var snapshot = CreateSnapshot();
        if (Volatile.Read(ref _publicationState) != 1)
            return ValueTask.FromResult(new BackgroundHandleReadResult { Snapshot = snapshot, Text = "Debug session is starting." });
        var tree = _manager.ResolveTree(_scope, _treeId);
        var text = new StringBuilder();
        long retainedBytes = 0, droppedRecords = 0, droppedBytes = 0;
        foreach (var session in tree.Sessions.Values.OrderBy(x => x.SessionId, StringComparer.Ordinal))
        {
            text.Append(session.SessionId).Append(": ").Append(session.State.Status);
            var stopped = session.State.Threads.Count(x => x.IsStopped);
            text.Append(" (threads=").Append(session.State.Threads.Count).Append(", stopped=").Append(stopped).AppendLine(")");
            var output = session.Output.Snapshot();
            retainedBytes += output.RetainedBytes;
            droppedRecords += output.DroppedRecords;
            droppedBytes += output.DroppedBytes;
            var requestedLines = Math.Clamp(request.TailLines ?? 200, 1, 2000);
            var tail = string.Concat(output.Records.Select(x => x.Text))
                .Split('\n').TakeLast(requestedLines);
            foreach (var line in tail)
            {
                if (text.Length + line.Length + 1 > 16 * 1024) break;
                text.AppendLine(line);
            }
        }
        return ValueTask.FromResult(new BackgroundHandleReadResult
        {
            Snapshot = snapshot,
            Text = text.ToString(),
            Artifacts = CreateArtifacts(tree),
            Metadata = new Dictionary<string, string>
            {
                ["retainedOutputBytes"] = retainedBytes.ToString(System.Globalization.CultureInfo.InvariantCulture),
                ["droppedOutputRecords"] = droppedRecords.ToString(System.Globalization.CultureInfo.InvariantCulture),
                ["droppedOutputBytes"] = droppedBytes.ToString(System.Globalization.CultureInfo.InvariantCulture)
            }
        });
    }

    public async ValueTask<BackgroundHandleStopResult> StopAsync(BackgroundHandleStopRequest request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (Interlocked.Exchange(ref _publicationState, 2) != 2)
            await _manager.RemoveAndDisposeAsync(_scope, _treeId).ConfigureAwait(false);
        return new() { Snapshot = CreateSnapshot(), CompletionKind = "Terminated" };
    }

    public ValueTask<BackgroundHandleArtifactResult> GetArtifactsAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var artifacts = Volatile.Read(ref _publicationState) == 1
            ? CreateArtifacts(_manager.ResolveTree(_scope, _treeId)) : [];
        return ValueTask.FromResult(new BackgroundHandleArtifactResult { Snapshot = CreateSnapshot(), Artifacts = artifacts });
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        await StopAsync(new() { Reason = "DEBUG_HANDLE_DISPOSED" }, CancellationToken.None).ConfigureAwait(false);
    }

    private BackgroundHandleSnapshot CreateSnapshot()
    {
        var publication = Volatile.Read(ref _publicationState);
        var status = publication switch
        {
            0 => "Starting",
            2 => "Terminated",
            _ => TryGetTreeStatus()
        };
        return new()
        {
            HandleId = _handleId,
            Name = $"Debug session {_treeId}",
            Kind = BackgroundHandleKind.DebugSession,
            SourceKind = BackgroundTaskSourceKind.ToolCall,
            Status = status,
            SourceId = _treeId,
            SessionId = _scope.SessionId,
            ThreadId = _scope.ThreadId,
            StartedAt = _startedAt,
            CompletedAt = publication == 2 ? DateTimeOffset.UtcNow : null,
            Metadata = CreateMetadata()
        };
    }

    private IReadOnlyDictionary<string, string> CreateMetadata()
    {
        var metadata = new Dictionary<string, string>(StringComparer.Ordinal) { ["debugTreeId"] = _treeId };
        if (Volatile.Read(ref _publicationState) != 1) return metadata;
        try
        {
            var tree = _manager.ResolveTree(_scope, _treeId);
            var snapshot = DebugSnapshotProjector.Project(tree);
            metadata["activeDebugSessionId"] = snapshot.ActiveDebugSessionId ?? string.Empty;
            metadata["debugSessionCount"] = snapshot.SessionCount.ToString(System.Globalization.CultureInfo.InvariantCulture);
            metadata["debugSessionsTruncated"] = snapshot.SessionsTruncated.ToString();
            metadata["childSessionCount"] = snapshot.ChildSessionCount
                .ToString(System.Globalization.CultureInfo.InvariantCulture);
            var active = snapshot.Sessions.FirstOrDefault(x =>
                string.Equals(x.DebugSessionId, snapshot.ActiveDebugSessionId, StringComparison.Ordinal));
            metadata["adapterId"] = active?.AdapterId ?? string.Empty;
            metadata["retainedOutputBytes"] = snapshot.RetainedOutputBytes
                .ToString(System.Globalization.CultureInfo.InvariantCulture);
            metadata["droppedOutputBytes"] = snapshot.DroppedOutputBytes
                .ToString(System.Globalization.CultureInfo.InvariantCulture);
            metadata["projectionFailures"] = snapshot.ProjectionFailures
                .ToString(System.Globalization.CultureInfo.InvariantCulture);
        }
        catch (KeyNotFoundException) { }
        return metadata;
    }

    private string TryGetTreeStatus()
    {
        try
        {
            var tree = _manager.ResolveTree(_scope, _treeId);
            return DebugSnapshotProjector.Project(tree).Status;
        }
        catch (KeyNotFoundException) { return "Terminated"; }
    }

    private static IReadOnlyList<BackgroundHandleArtifact> CreateArtifacts(DebugSessionTree tree)
        => tree.StoredArtifacts.Select(artifact => new BackgroundHandleArtifact
        {
            Kind = artifact.Kind,
            ContentId = artifact.ContentId,
            Metadata = artifact.Metadata.Concat(new Dictionary<string, string>
            {
                ["contentScope"] = artifact.Scope,
                ["contentVersion"] = artifact.Version ?? string.Empty
            }).ToDictionary(StringComparer.Ordinal)
        }).ToArray();
}
