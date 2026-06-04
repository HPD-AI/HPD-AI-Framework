using System.Text;
using System.Text.Json;
using HPD.Agent.Serialization;

namespace HPD.Agent;

/// <summary>
/// Workspace-backed session facade. Sessions are spaces with a session metadata document;
/// branches are child spaces with appendable branch event streams.
/// </summary>
public sealed class WorkspaceSessionRepository : ISessionRepository
{
    public const string SessionKind = "session";
    public const string BranchKind = "branch";
    public const string SessionMetadataRole = "session_metadata";
    public const string BranchEventStreamRole = "branch_event_stream";

    private readonly IWorkspaceStore _workspace;
    private readonly WorkspacePrincipalRef _principal;

    public WorkspaceSessionRepository(
        IWorkspaceStore workspace,
        WorkspacePrincipalRef? principal = null)
    {
        _workspace = workspace ?? throw new ArgumentNullException(nameof(workspace));
        _principal = principal ?? WorkspacePrincipalRef.System;
    }

    /// <summary>The workspace substrate backing this repository.</summary>
    public IWorkspaceStore Workspace => _workspace;

    public async Task<Session?> LoadSessionAsync(
        string sessionId,
        CancellationToken cancellationToken = default)
    {
        var sessionSpace = await FindSessionSpaceAsync(sessionId, cancellationToken).ConfigureAwait(false);
        if (sessionSpace is null)
            return null;

        var attachment = await GetLatestAttachmentAsync(
            sessionSpace.Id,
            SessionMetadataRole,
            cancellationToken).ConfigureAwait(false);
        if (attachment is null)
            return null;

        await using var stream = await _workspace.OpenContentAsync(
            _principal,
            attachment.ContentId,
            attachment.ContentVersion,
            cancellationToken).ConfigureAwait(false);
        if (stream is null)
            return null;

        var session = await JsonSerializer.DeserializeAsync(
            stream,
            SessionJsonContext.Combined.Session,
            cancellationToken).ConfigureAwait(false);
        WorkspaceMetadataNormalizer.Normalize(session?.Metadata);

        return session;
    }

    public async Task SaveSessionAsync(
        Session session,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(session);

        var sessionSpace = await GetOrCreateSessionSpaceAsync(session.Id, session.Id, cancellationToken)
            .ConfigureAwait(false);

        await ReplaceRoleDocumentAsync(
            sessionSpace.Id,
            SessionMetadataRole,
            "session.json",
            "application/json",
            stream => JsonSerializer.SerializeAsync(stream, session, SessionJsonContext.Combined.Session, cancellationToken),
            cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<string>> ListSessionIdsAsync(
        CancellationToken cancellationToken = default)
    {
        var spaces = await _workspace.ListSpacesAsync(
            _principal,
            new WorkspaceSpaceQuery { Kind = SessionKind },
            cancellationToken).ConfigureAwait(false);

        return spaces.Select(space => space.ExternalId).OrderBy(id => id, StringComparer.Ordinal).ToList();
    }

    public async Task DeleteSessionAsync(
        string sessionId,
        CancellationToken cancellationToken = default)
    {
        var sessionSpace = await FindSessionSpaceAsync(sessionId, cancellationToken).ConfigureAwait(false);
        if (sessionSpace is null)
            return;

        await _workspace.DeleteSpaceAsync(
            _principal,
            sessionSpace.Id,
            sessionSpace.Version,
            recursive: true,
            cancellationToken).ConfigureAwait(false);
    }

    public async Task<Branch?> LoadBranchAsync(
        string sessionId,
        string branchId,
        CancellationToken cancellationToken = default)
    {
        var document = await LoadBranchDocumentAsync(sessionId, branchId, cancellationToken).ConfigureAwait(false);
        if (document is null)
            return null;

        var branch = BranchProjector.Project(document);
        branch.Session = await LoadSessionAsync(sessionId, cancellationToken).ConfigureAwait(false);
        return branch;
    }

    public async Task<BranchEventDocument?> LoadBranchDocumentAsync(
        string sessionId,
        string branchId,
        CancellationToken cancellationToken = default)
    {
        var branchSpace = await FindBranchSpaceAsync(sessionId, branchId, cancellationToken).ConfigureAwait(false);
        if (branchSpace is null)
            return null;

        var events = new List<AgentEvent>();
        await foreach (var evt in ReadBranchEventsAsync(
            sessionId,
            branchId,
            HPD.Events.ReplayReadOptions.All,
            cancellationToken).ConfigureAwait(false))
        {
            events.Add(evt);
        }

        if (events.Count == 0)
            return null;

        return new BranchEventDocument
        {
            SessionId = sessionId,
            BranchId = branchId,
            CreatedAt = events[0].Timestamp,
            UpdatedAt = events[^1].Timestamp,
            NextSequenceNumber = events[^1].SequenceNumber + 1,
            Events = events
        };
    }

    public async Task SaveBranchDocumentAsync(
        BranchEventDocument document,
        long? expectedSequenceNumber = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(document);

        var current = await LoadBranchDocumentAsync(document.SessionId, document.BranchId, cancellationToken)
            .ConfigureAwait(false);
        var currentSequence = current?.NextSequenceNumber - 1 ?? 0;
        if (expectedSequenceNumber is not null && expectedSequenceNumber.Value != currentSequence)
        {
            throw new WorkspaceConflictException(
                $"Branch '{document.BranchId}' sequence conflict.",
                expectedSequenceNumber.Value.ToString(),
                currentSequence.ToString());
        }

        if (currentSequence != 0)
        {
            throw new NotSupportedException(
                "Replacing an existing branch event stream is not supported by the workspace session repository.");
        }

        foreach (var evt in document.Events.OrderBy(e => e.SequenceNumber))
        {
            await AppendBranchEventAsync(
                document.SessionId,
                document.BranchId,
                evt,
                expectedSequenceNumber: evt.SequenceNumber - 1,
                cancellationToken).ConfigureAwait(false);
        }
    }

    public async Task AppendBranchEventAsync(
        string sessionId,
        string branchId,
        AgentEvent evt,
        long? expectedSequenceNumber = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(branchId);
        ArgumentNullException.ThrowIfNull(evt);

        var branchSpace = await GetOrCreateBranchSpaceAsync(sessionId, branchId, cancellationToken)
            .ConfigureAwait(false);

        evt = evt with
        {
            EventId = evt.EventId ?? Guid.NewGuid().ToString("N"),
            SessionId = evt.SessionId ?? sessionId,
            BranchId = evt.BranchId ?? branchId
        };

        var payload = Encoding.UTF8.GetBytes(AgentEventSerializer.ToJson(evt));
        var appended = await _workspace.AppendEventAsync(
            _principal,
            branchSpace.Id,
            new AppendWorkspaceEventRequest
            {
                Role = BranchEventStreamRole,
                Name = "events.jsonl",
                ExpectedSequenceNumber = expectedSequenceNumber,
                Payload = payload
            },
            cancellationToken).ConfigureAwait(false);

        evt.SequenceNumber = appended.SequenceNumber;
    }

    public async IAsyncEnumerable<AgentEvent> ReadBranchEventsAsync(
        string sessionId,
        string branchId,
        HPD.Events.ReplayReadOptions options,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var branchSpace = await FindBranchSpaceAsync(sessionId, branchId, cancellationToken).ConfigureAwait(false);
        if (branchSpace is null)
            yield break;

        var events = new List<AgentEvent>();
        await foreach (var record in _workspace.ReadEventsAsync(
            _principal,
            branchSpace.Id,
            new WorkspaceEventStreamQuery { Role = BranchEventStreamRole },
            cancellationToken).ConfigureAwait(false))
        {
            var json = Encoding.UTF8.GetString(record.Payload.ToArray());
            if (AgentEventSerializer.FromEventJson(json) is not { } evt)
                continue;

            evt = evt with
            {
                SessionId = evt.SessionId ?? sessionId,
                BranchId = evt.BranchId ?? branchId
            };
            evt.SequenceNumber = record.SequenceNumber;
            events.Add(evt);
        }

        await foreach (var evt in events.FilterByReplayOptions(options, cancellationToken).ConfigureAwait(false))
            yield return evt;
    }

    public async Task<IReadOnlyList<string>> ListBranchIdsAsync(
        string sessionId,
        CancellationToken cancellationToken = default)
    {
        var sessionSpace = await FindSessionSpaceAsync(sessionId, cancellationToken).ConfigureAwait(false);
        if (sessionSpace is null)
            return [];

        var spaces = await _workspace.ListChildSpacesAsync(
            _principal,
            sessionSpace.Id,
            new WorkspaceSpaceQuery { Kind = BranchKind },
            cancellationToken).ConfigureAwait(false);

        return spaces.Select(space => space.ExternalId).OrderBy(id => id, StringComparer.Ordinal).ToList();
    }

    public async Task DeleteBranchAsync(
        string sessionId,
        string branchId,
        CancellationToken cancellationToken = default)
    {
        var branchSpace = await FindBranchSpaceAsync(sessionId, branchId, cancellationToken).ConfigureAwait(false);
        if (branchSpace is null)
            return;

        await _workspace.DeleteSpaceAsync(
            _principal,
            branchSpace.Id,
            branchSpace.Version,
            recursive: true,
            cancellationToken).ConfigureAwait(false);
    }

    public async Task<int> DeleteInactiveSessionsAsync(
        TimeSpan inactivityThreshold,
        bool dryRun = false,
        CancellationToken cancellationToken = default)
    {
        var cutoff = DateTime.UtcNow - inactivityThreshold;
        var deleted = 0;
        foreach (var sessionId in await ListSessionIdsAsync(cancellationToken).ConfigureAwait(false))
        {
            var session = await LoadSessionAsync(sessionId, cancellationToken).ConfigureAwait(false);
            if (session is null || session.LastActivity > cutoff)
                continue;

            deleted++;
            if (!dryRun)
                await DeleteSessionAsync(sessionId, cancellationToken).ConfigureAwait(false);
        }

        return deleted;
    }

    private async Task<WorkspaceSpaceInfo> GetOrCreateSessionSpaceAsync(
        string sessionId,
        string name,
        CancellationToken cancellationToken)
    {
        var existing = await FindSessionSpaceAsync(sessionId, cancellationToken).ConfigureAwait(false);
        if (existing is not null)
            return existing;

        return await _workspace.CreateSpaceAsync(
            _principal,
            new CreateWorkspaceSpaceRequest
            {
                Kind = SessionKind,
                ExternalId = sessionId,
                Name = name
            },
            cancellationToken).ConfigureAwait(false);
    }

    private async Task<WorkspaceSpaceInfo> GetOrCreateBranchSpaceAsync(
        string sessionId,
        string branchId,
        CancellationToken cancellationToken)
    {
        var existing = await FindBranchSpaceAsync(sessionId, branchId, cancellationToken).ConfigureAwait(false);
        if (existing is not null)
            return existing;

        var sessionSpace = await GetOrCreateSessionSpaceAsync(sessionId, sessionId, cancellationToken)
            .ConfigureAwait(false);
        return await _workspace.CreateChildSpaceAsync(
            _principal,
            sessionSpace.Id,
            new CreateWorkspaceSpaceRequest
            {
                Kind = BranchKind,
                ExternalId = branchId,
                Name = branchId
            },
            cancellationToken).ConfigureAwait(false);
    }

    private Task<WorkspaceSpaceInfo?> FindSessionSpaceAsync(
        string sessionId,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);
        return _workspace.FindSpaceAsync(
            _principal,
            new WorkspaceSpaceQuery
            {
                Kind = SessionKind,
                ExternalId = sessionId
            },
            cancellationToken);
    }

    private async Task<WorkspaceSpaceInfo?> FindBranchSpaceAsync(
        string sessionId,
        string branchId,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(branchId);
        var sessionSpace = await FindSessionSpaceAsync(sessionId, cancellationToken).ConfigureAwait(false);
        if (sessionSpace is null)
            return null;

        return await _workspace.FindSpaceAsync(
            _principal,
            new WorkspaceSpaceQuery
            {
                Kind = BranchKind,
                ExternalId = branchId,
                ParentSpaceId = sessionSpace.Id
            },
            cancellationToken).ConfigureAwait(false);
    }

    private async Task ReplaceRoleDocumentAsync(
        string spaceId,
        string role,
        string name,
        string contentType,
        Func<Stream, Task> writeAsync,
        CancellationToken cancellationToken)
    {
        var existing = await _workspace.ListContentAsync(
            _principal,
            spaceId,
            new WorkspaceContentAttachmentQuery { Role = role },
            cancellationToken).ConfigureAwait(false);
        foreach (var attachment in existing)
        {
            await _workspace.DetachContentAsync(
                _principal,
                spaceId,
                attachment.Id,
                attachment.Version,
                cancellationToken).ConfigureAwait(false);
        }

        using var buffer = new MemoryStream();
        await writeAsync(buffer).ConfigureAwait(false);
        buffer.Position = 0;
        await _workspace.WriteContentAsync(
            _principal,
            spaceId,
            existingAttachmentId: null,
            buffer,
            new WriteWorkspaceSpaceContentRequest
            {
                ContentType = contentType,
                Role = role,
                Name = name,
                Permission = "read_write"
            },
            cancellationToken).ConfigureAwait(false);
    }

    private async Task<WorkspaceContentAttachmentInfo?> GetLatestAttachmentAsync(
        string spaceId,
        string role,
        CancellationToken cancellationToken)
    {
        var attachments = await _workspace.ListContentAsync(
            _principal,
            spaceId,
            new WorkspaceContentAttachmentQuery { Role = role },
            cancellationToken).ConfigureAwait(false);
        return attachments.OrderBy(attachment => attachment.CreatedAt).LastOrDefault();
    }

}
