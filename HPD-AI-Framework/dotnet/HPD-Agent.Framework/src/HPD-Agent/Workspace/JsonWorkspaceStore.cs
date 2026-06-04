using System.Text.Json;
using System.Text.Json.Serialization;

namespace HPD.Agent;

/// <summary>
/// File-backed JSON implementation of <see cref="IWorkspaceStore"/>.
/// Intended for local development, tests, and simple hosted deployments that need
/// one workspace-backed persistence substrate without a database.
/// </summary>
public sealed class JsonWorkspaceStore : IWorkspaceStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private readonly string _filePath;
    private readonly WorkspacePolicyRegistry _policies;
    private readonly IWorkspaceContentObjects _contentObjects;
    private readonly IWorkspaceEventStreams _eventStreams;
    private readonly object _gate = new();
    private WorkspaceSnapshot _snapshot;

    public JsonWorkspaceStore(
        string basePath,
        WorkspacePolicyRegistry? policies = null,
        IWorkspaceContentObjects? contentObjects = null,
        IWorkspaceEventStreams? eventStreams = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(basePath);
        Directory.CreateDirectory(basePath);
        _filePath = Path.Combine(basePath, "workspace.json");
        _policies = policies ?? WorkspacePolicyRegistry.Default;
        _contentObjects = contentObjects ?? new FileWorkspaceContentObjects(basePath);
        _eventStreams = eventStreams ?? new FileWorkspaceEventStreams(basePath);
        _snapshot = LoadSnapshot(_filePath);
    }

    public Task<WorkspaceSpaceInfo> CreateSpaceAsync(
        WorkspacePrincipalRef principal,
        CreateWorkspaceSpaceRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        lock (_gate)
        {
            EnsureSpaceDoesNotExist(request.Kind, request.ExternalId, parentSpaceId: null);
            EnsurePolicyAllowed(_policies.Resolve(request.Kind).CanCreate(new WorkspacePolicyContext(principal), request));
            var record = CreateSpaceRecord(request, parentSpaceId: null);
            _snapshot.Spaces[record.Id] = record;
            GrantOwnerAccess(record.Id, principal);
            SaveSnapshot();
            return Task.FromResult(Map(record));
        }
    }

    public Task<WorkspaceSpaceInfo> CreateChildSpaceAsync(
        WorkspacePrincipalRef principal,
        string parentSpaceId,
        CreateWorkspaceSpaceRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(parentSpaceId);
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        lock (_gate)
        {
            if (!_snapshot.Spaces.ContainsKey(parentSpaceId))
                throw new KeyNotFoundException($"Parent space '{parentSpaceId}' was not found.");
            EnsureCanManageSpace(principal, parentSpaceId);

            EnsureSpaceDoesNotExist(request.Kind, request.ExternalId, parentSpaceId);
            EnsurePolicyAllowed(_policies.Resolve(request.Kind).CanCreate(new WorkspacePolicyContext(principal), request));
            var record = CreateSpaceRecord(request, parentSpaceId);
            _snapshot.Spaces[record.Id] = record;
            InheritAccess(parentSpaceId, record.Id);
            GrantOwnerAccess(record.Id, principal);
            SaveSnapshot();
            return Task.FromResult(Map(record));
        }
    }

    public Task<WorkspaceSpaceInfo?> GetSpaceAsync(
        WorkspacePrincipalRef principal,
        string spaceId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(spaceId);
        cancellationToken.ThrowIfCancellationRequested();

        lock (_gate)
        {
            return Task.FromResult(_snapshot.Spaces.TryGetValue(spaceId, out var record)
                && CanReadSpace(principal, spaceId)
                ? Map(record)
                : null);
        }
    }

    public async Task DeleteSpaceAsync(
        WorkspacePrincipalRef principal,
        string spaceId,
        string? ifMatchVersion = null,
        bool recursive = false,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(spaceId);
        cancellationToken.ThrowIfCancellationRequested();

        List<string> eventStreamIds = [];
        lock (_gate)
        {
            if (!_snapshot.Spaces.TryGetValue(spaceId, out var space))
                return;
            EnsureCanManageSpace(principal, spaceId);

            EnsureVersionMatches(space.Version, ifMatchVersion, $"Space '{spaceId}' version conflict.");

            var childIds = _snapshot.Spaces.Values
                .Where(candidate => candidate.ParentSpaceId == spaceId)
                .Select(candidate => candidate.Id)
                .ToList();
            if (childIds.Count > 0 && !recursive)
            {
                throw new InvalidOperationException(
                    $"Space '{spaceId}' has child spaces and cannot be deleted without recursive=true.");
            }

            var deleteIds = new HashSet<string>(StringComparer.Ordinal) { spaceId };
            if (recursive)
                CollectDescendantSpaceIds(spaceId, deleteIds);

            foreach (var deleteId in deleteIds)
            {
                _snapshot.Spaces.Remove(deleteId);
                foreach (var accessId in _snapshot.Access.Values
                    .Where(access => access.SpaceId == deleteId)
                    .Select(access => access.Id)
                    .ToList())
                {
                    _snapshot.Access.Remove(accessId);
                }

                foreach (var attachmentId in _snapshot.Attachments.Values
                    .Where(attachment => attachment.SpaceId == deleteId)
                    .Select(attachment => attachment.Id)
                    .ToList())
                {
                    if (_snapshot.Attachments.TryGetValue(attachmentId, out var attachment) &&
                        attachment.Metadata is not null &&
                        attachment.Metadata.TryGetValue("stream_id", out var streamId))
                    {
                        eventStreamIds.Add(streamId);
                    }
                    _snapshot.Attachments.Remove(attachmentId);
                }

                foreach (var streamId in _snapshot.EventStreams.Values
                    .Where(stream => stream.SpaceId == deleteId)
                    .Select(stream => stream.StreamId)
                    .ToList())
                {
                    eventStreamIds.Add(streamId);
                    _snapshot.EventStreams.Remove(streamId);
                }
            }

            SaveSnapshot();
        }

        foreach (var streamId in eventStreamIds.Distinct(StringComparer.Ordinal))
            await _eventStreams.DeleteAsync(streamId, cancellationToken).ConfigureAwait(false);
    }

    public Task<WorkspaceSpaceInfo?> FindSpaceAsync(
        WorkspacePrincipalRef principal,
        WorkspaceSpaceQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        cancellationToken.ThrowIfCancellationRequested();

        lock (_gate)
        {
            var result = _snapshot.Spaces.Values.FirstOrDefault(space =>
                Matches(space, query) && CanReadSpace(principal, space.Id));
            return Task.FromResult(result is null ? null : Map(result));
        }
    }

    public Task<IReadOnlyList<WorkspaceSpaceInfo>> ListSpacesAsync(
        WorkspacePrincipalRef principal,
        WorkspaceSpaceQuery? query = null,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        lock (_gate)
        {
            var result = _snapshot.Spaces.Values
                .Where(space => query is null || Matches(space, query))
                .Where(space => CanReadSpace(principal, space.Id))
                .OrderBy(space => space.CreatedAt)
                .Select(Map)
                .ToList();

            return Task.FromResult<IReadOnlyList<WorkspaceSpaceInfo>>(result);
        }
    }

    public Task<IReadOnlyList<WorkspaceSpaceInfo>> ListChildSpacesAsync(
        WorkspacePrincipalRef principal,
        string parentSpaceId,
        WorkspaceSpaceQuery? query = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(parentSpaceId);
        cancellationToken.ThrowIfCancellationRequested();
        if (!CanReadSpace(principal, parentSpaceId))
            return Task.FromResult<IReadOnlyList<WorkspaceSpaceInfo>>([]);

        var effectiveQuery = query is null
            ? new WorkspaceSpaceQuery { ParentSpaceId = parentSpaceId }
            : query with { ParentSpaceId = parentSpaceId };

        lock (_gate)
        {
            var result = _snapshot.Spaces.Values
                .Where(space => Matches(space, effectiveQuery))
                .Where(space => CanReadSpace(principal, space.Id))
                .OrderBy(space => space.CreatedAt)
                .Select(Map)
                .ToList();

            return Task.FromResult<IReadOnlyList<WorkspaceSpaceInfo>>(result);
        }
    }

    public Task<WorkspaceSpaceAccessInfo> GrantAccessAsync(
        WorkspacePrincipalRef principal,
        string spaceId,
        GrantWorkspaceSpaceAccessRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(spaceId);
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.Grantee);
        cancellationToken.ThrowIfCancellationRequested();

        lock (_gate)
        {
            if (!_snapshot.Spaces.ContainsKey(spaceId))
                throw new KeyNotFoundException($"Space '{spaceId}' was not found.");
            EnsureCanManageSpace(principal, spaceId);

            var now = DateTimeOffset.UtcNow;
            foreach (var existing in _snapshot.Access.Values.Where(access =>
                access.SpaceId == spaceId &&
                access.Principal == request.Grantee &&
                access.RevokedAt is null))
            {
                _snapshot.Access[existing.Id] = existing with { RevokedAt = now };
            }

            var record = AccessRecord.Create(spaceId, principal, request);
            _snapshot.Access[record.Id] = record;
            SaveSnapshot();
            return Task.FromResult(Map(record));
        }
    }

    public Task RevokeAccessAsync(
        WorkspacePrincipalRef principal,
        string spaceId,
        WorkspacePrincipalRef grantee,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(spaceId);
        ArgumentNullException.ThrowIfNull(grantee);
        cancellationToken.ThrowIfCancellationRequested();

        lock (_gate)
        {
            if (!_snapshot.Spaces.ContainsKey(spaceId))
                return Task.CompletedTask;
            EnsureCanManageSpace(principal, spaceId);

            var now = DateTimeOffset.UtcNow;
            foreach (var existing in _snapshot.Access.Values.Where(access =>
                access.SpaceId == spaceId &&
                access.Principal == grantee &&
                access.RevokedAt is null))
            {
                _snapshot.Access[existing.Id] = existing with { RevokedAt = now };
            }

            SaveSnapshot();
            return Task.CompletedTask;
        }
    }

    public Task<IReadOnlyList<WorkspaceSpaceAccessInfo>> ListAccessAsync(
        WorkspacePrincipalRef principal,
        string spaceId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(spaceId);
        cancellationToken.ThrowIfCancellationRequested();

        lock (_gate)
        {
            if (!CanManageSpace(principal, spaceId))
                return Task.FromResult<IReadOnlyList<WorkspaceSpaceAccessInfo>>([]);

            var result = _snapshot.Access.Values
                .Where(access => access.SpaceId == spaceId)
                .OrderBy(access => access.CreatedAt)
                .Select(Map)
                .ToList();

            return Task.FromResult<IReadOnlyList<WorkspaceSpaceAccessInfo>>(result);
        }
    }

    public async Task<WorkspaceContentAttachmentInfo> WriteContentAsync(
        WorkspacePrincipalRef principal,
        string spaceId,
        string? existingAttachmentId,
        Stream data,
        WriteWorkspaceSpaceContentRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(spaceId);
        ArgumentNullException.ThrowIfNull(data);
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        string contentId;
        string reservedVersion;
        PendingContentWriteRecord pending;
        string? expectedAttachmentVersion = null;
        string? expectedContentVersion = null;

        lock (_gate)
        {
            if (!_snapshot.Spaces.TryGetValue(spaceId, out var space))
                throw new KeyNotFoundException($"Space '{spaceId}' was not found.");
            EnsureCanWriteSpace(principal, spaceId);

            if (!string.IsNullOrWhiteSpace(existingAttachmentId))
            {
                if (!_snapshot.Attachments.TryGetValue(existingAttachmentId, out var attachment))
                    throw new KeyNotFoundException($"Attachment '{existingAttachmentId}' was not found.");
                if (attachment.SpaceId != spaceId)
                    throw new ArgumentException($"Attachment '{existingAttachmentId}' does not belong to space '{spaceId}'.", nameof(existingAttachmentId));
                if (!AllowsWrite(attachment.Permission))
                    throw new WorkspaceAccessDeniedException($"Attachment '{existingAttachmentId}' is not writable.");
                EnsureVersionMatches(
                    attachment.Version,
                    request.IfMatchAttachmentVersion,
                    $"Attachment '{attachment.Id}' version conflict.");
                EnsurePolicyAllowed(PolicyFor(space).CanWriteContent(
                    new WorkspacePolicyContext(principal),
                    Map(space),
                    Map(attachment),
                    request));

                if (!_snapshot.Content.TryGetValue(attachment.ContentId, out var existingContent))
                    throw new KeyNotFoundException($"Content '{attachment.ContentId}' was not found.");
                EnsureVersionMatches(
                    existingContent.Version,
                    request.IfMatchContentVersion,
                    $"Content '{existingContent.Id}' version conflict.");

                contentId = attachment.ContentId;
                expectedAttachmentVersion = attachment.Version;
                expectedContentVersion = existingContent.Version;
            }
            else
            {
                contentId = NewId("ct");
            }

            reservedVersion = NewVersion("content");
            pending = PendingContentWriteRecord.Create(contentId, reservedVersion, spaceId, existingAttachmentId);
            _snapshot.PendingContentWrites[pending.Id] = pending;
            SaveSnapshot();
        }

        WorkspaceContentObjectWriteResult write;
        try
        {
            write = await _contentObjects.WriteAsync(
                contentId,
                reservedVersion,
                data,
                ToObjectWriteRequest(request),
                cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            lock (_gate)
            {
                _snapshot.PendingContentWrites[pending.Id] = pending.Abort(ex.Message);
                SaveSnapshot();
            }

            await _contentObjects.DeleteVersionAsync(contentId, reservedVersion, CancellationToken.None).ConfigureAwait(false);
            throw;
        }

        lock (_gate)
        {
            try
            {
                if (!_snapshot.Spaces.TryGetValue(spaceId, out var space))
                    throw new KeyNotFoundException($"Space '{spaceId}' was not found.");
                EnsureCanWriteSpace(principal, spaceId);

                if (string.IsNullOrWhiteSpace(existingAttachmentId))
                {
                    var content = ContentRecord.Create(write);
                    var attachRequest = ToAttachRequest(request, content.Version);
                    EnsurePolicyAllowed(PolicyFor(space).CanAttachContent(
                        new WorkspacePolicyContext(principal),
                        Map(space),
                        Map(content),
                        attachRequest));

                    _snapshot.Content[content.Id] = content;
                    var createdAttachment = AttachmentRecord.Create(spaceId, content.Id, content.Version, attachRequest);
                    _snapshot.Attachments[createdAttachment.Id] = createdAttachment;
                    _snapshot.PendingContentWrites.Remove(pending.Id);
                    SaveSnapshot();
                    return Map(createdAttachment);
                }

                if (!_snapshot.Attachments.TryGetValue(existingAttachmentId, out var attachment))
                    throw new KeyNotFoundException($"Attachment '{existingAttachmentId}' was not found.");
                if (attachment.SpaceId != spaceId)
                    throw new ArgumentException($"Attachment '{existingAttachmentId}' does not belong to space '{spaceId}'.", nameof(existingAttachmentId));
                EnsureVersionMatches(
                    attachment.Version,
                    expectedAttachmentVersion,
                    $"Attachment '{attachment.Id}' version conflict.");
                EnsureVersionMatches(
                    attachment.Version,
                    request.IfMatchAttachmentVersion,
                    $"Attachment '{attachment.Id}' version conflict.");
                EnsurePolicyAllowed(PolicyFor(space).CanWriteContent(
                    new WorkspacePolicyContext(principal),
                    Map(space),
                    Map(attachment),
                    request));

                if (!_snapshot.Content.TryGetValue(attachment.ContentId, out var existingContent))
                    throw new KeyNotFoundException($"Content '{attachment.ContentId}' was not found.");
                EnsureVersionMatches(
                    existingContent.Version,
                    expectedContentVersion,
                    $"Content '{existingContent.Id}' version conflict.");
                EnsureVersionMatches(
                    existingContent.Version,
                    request.IfMatchContentVersion,
                    $"Content '{existingContent.Id}' version conflict.");

                var updatedContent = existingContent.Replace(write);
                _snapshot.Content[updatedContent.Id] = updatedContent;

                var updatedAttachment = attachment.WithContentVersion(updatedContent.Version);
                _snapshot.Attachments[updatedAttachment.Id] = updatedAttachment;
                _snapshot.PendingContentWrites.Remove(pending.Id);
                SaveSnapshot();
                return Map(updatedAttachment);
            }
            catch (Exception ex)
            {
                _snapshot.PendingContentWrites[pending.Id] = pending.Abort(ex.Message);
                SaveSnapshot();
                _ = _contentObjects.DeleteVersionAsync(contentId, reservedVersion, CancellationToken.None);
                throw;
            }
        }
    }

    public async Task<WorkspacePendingContentWriteCleanupResult> CleanupPendingContentWritesAsync(
        WorkspacePrincipalRef principal,
        WorkspacePendingContentWriteCleanupRequest? request = null,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!IsSystem(principal))
            throw new WorkspaceAccessDeniedException("Only the system principal can clean pending content writes.");

        request ??= new WorkspacePendingContentWriteCleanupRequest();
        var now = DateTimeOffset.UtcNow;
        List<PendingContentWriteCleanupCandidate> candidates;
        lock (_gate)
        {
            candidates = _snapshot.PendingContentWrites.Values
                .Where(write => ShouldCleanPendingWrite(write, request, now))
                .Select(write => new PendingContentWriteCleanupCandidate(write.Id, write.ContentId, write.Version))
                .ToList();
        }

        var deleted = new List<string>(candidates.Count);
        var failedDeletes = 0;
        foreach (var candidate in candidates)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                await _contentObjects.DeleteVersionAsync(
                    candidate.ContentId,
                    candidate.Version,
                    cancellationToken).ConfigureAwait(false);
                deleted.Add(candidate.Id);
            }
            catch
            {
                failedDeletes++;
            }
        }

        var removed = 0;
        lock (_gate)
        {
            foreach (var id in deleted)
            {
                if (_snapshot.PendingContentWrites.Remove(id))
                    removed++;
            }

            if (removed > 0)
                SaveSnapshot();
        }

        return new WorkspacePendingContentWriteCleanupResult
        {
            MatchedWrites = candidates.Count,
            DeletedVersions = deleted.Count,
            RemovedRecords = removed,
            FailedDeletes = failedDeletes
        };
    }

    public async Task<WorkspaceEventStreamRepairResult> RepairEventStreamMetadataAsync(
        WorkspacePrincipalRef principal,
        WorkspaceEventStreamRepairRequest? request = null,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!IsSystem(principal))
            throw new WorkspaceAccessDeniedException("Only the system principal can repair event stream metadata.");

        request ??= new WorkspaceEventStreamRepairRequest();
        List<EventStreamRecord> candidates;
        lock (_gate)
        {
            candidates = _snapshot.EventStreams.Values
                .Where(stream => ShouldRepairEventStream(stream, request))
                .ToList();
        }

        var repairs = new List<EventStreamRepairCandidate>();
        var missing = 0;
        foreach (var candidate in candidates)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var stat = await _eventStreams.StatAsync(candidate.StreamId, cancellationToken).ConfigureAwait(false);
            if (stat is null)
            {
                missing++;
                continue;
            }

            if (stat.LatestSequenceNumber != candidate.LatestSequenceNumber)
                repairs.Add(new EventStreamRepairCandidate(candidate.StreamId, stat.LatestSequenceNumber));
        }

        var repaired = 0;
        lock (_gate)
        {
            foreach (var repair in repairs)
            {
                if (_snapshot.EventStreams.TryGetValue(repair.StreamId, out var current) &&
                    current.LatestSequenceNumber != repair.LatestSequenceNumber)
                {
                    _snapshot.EventStreams[repair.StreamId] = current.WithLatestSequence(repair.LatestSequenceNumber);
                    repaired++;
                }
            }

            if (repaired > 0)
                SaveSnapshot();
        }

        return new WorkspaceEventStreamRepairResult
        {
            MatchedStreams = candidates.Count,
            RepairedStreams = repaired,
            MissingBackendStreams = missing
        };
    }

    public Task<Stream?> OpenContentAsync(
        WorkspacePrincipalRef principal,
        string contentId,
        string? version = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(contentId);
        cancellationToken.ThrowIfCancellationRequested();

        lock (_gate)
        {
            if (!_snapshot.Content.TryGetValue(contentId, out var record))
                return Task.FromResult<Stream?>(null);

            var contentVersion = record.FindVersion(version);
            if (contentVersion is null)
                return Task.FromResult<Stream?>(null);
            if (!CanReadContentVersion(principal, contentId, contentVersion.Version))
                return Task.FromResult<Stream?>(null);

            return _contentObjects.OpenReadAsync(contentId, contentVersion.Version, cancellationToken);
        }
    }

    public Task<WorkspaceContentInfo?> StatContentAsync(
        WorkspacePrincipalRef principal,
        string contentId,
        string? version = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(contentId);
        cancellationToken.ThrowIfCancellationRequested();

        lock (_gate)
        {
            if (!_snapshot.Content.TryGetValue(contentId, out var record))
                return Task.FromResult<WorkspaceContentInfo?>(null);

            var contentVersion = record.FindVersion(version);
            if (contentVersion is null)
                return Task.FromResult<WorkspaceContentInfo?>(null);
            if (!CanReadContentVersion(principal, contentId, contentVersion.Version))
                return Task.FromResult<WorkspaceContentInfo?>(null);

            return Task.FromResult<WorkspaceContentInfo?>(Map(record.Id, contentVersion));
        }
    }

    public Task<WorkspaceContentAttachmentInfo> AttachContentAsync(
        WorkspacePrincipalRef principal,
        string spaceId,
        string contentId,
        AttachWorkspaceContentRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(spaceId);
        ArgumentException.ThrowIfNullOrWhiteSpace(contentId);
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        lock (_gate)
        {
            if (!_snapshot.Spaces.TryGetValue(spaceId, out var space))
                throw new KeyNotFoundException($"Space '{spaceId}' was not found.");
            EnsureCanWriteSpace(principal, spaceId);
            if (!_snapshot.Content.TryGetValue(contentId, out var content))
                throw new KeyNotFoundException($"Content '{contentId}' was not found.");

            var contentVersion = request.ContentVersion ?? content.Version;
            var contentVersionRecord = content.FindVersion(contentVersion);
            if (contentVersionRecord is null)
                throw new KeyNotFoundException($"Content '{contentId}' version '{contentVersion}' was not found.");

            EnsurePolicyAllowed(PolicyFor(space).CanAttachContent(
                new WorkspacePolicyContext(principal),
                Map(space),
                Map(content.Id, contentVersionRecord),
                request));

            var attachment = AttachmentRecord.Create(spaceId, contentId, contentVersion, request);
            _snapshot.Attachments[attachment.Id] = attachment;
            SaveSnapshot();
            return Task.FromResult(Map(attachment));
        }
    }

    public Task<IReadOnlyList<WorkspaceContentAttachmentInfo>> ListContentAsync(
        WorkspacePrincipalRef principal,
        string spaceId,
        WorkspaceContentAttachmentQuery? query = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(spaceId);
        cancellationToken.ThrowIfCancellationRequested();
        if (!CanReadSpace(principal, spaceId))
            return Task.FromResult<IReadOnlyList<WorkspaceContentAttachmentInfo>>([]);

        lock (_gate)
        {
            var result = _snapshot.Attachments.Values
                .Where(attachment => attachment.SpaceId == spaceId)
                .Where(attachment => AllowsRead(attachment.Permission))
                .Where(attachment => PolicyAllowsReadContent(principal, attachment))
                .Where(attachment => query?.Role is null || attachment.Role == query.Role)
                .Where(attachment => query?.Name is null || attachment.Name == query.Name)
                .OrderBy(attachment => attachment.CreatedAt)
                .Select(Map)
                .ToList();

            return Task.FromResult<IReadOnlyList<WorkspaceContentAttachmentInfo>>(result);
        }
    }

    public Task<IReadOnlyList<WorkspaceVisibleContentResult>> SearchContentAsync(
        WorkspacePrincipalRef principal,
        WorkspaceVisibleContentQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        cancellationToken.ThrowIfCancellationRequested();

        lock (_gate)
        {
            var results = new List<WorkspaceVisibleContentResult>();
            foreach (var space in ResolveSearchSpaces(principal, query))
            {
                var spaceInfo = Map(space);
                var attachments = _snapshot.Attachments.Values
                    .Where(attachment => attachment.SpaceId == space.Id)
                    .Where(attachment => AllowsRead(attachment.Permission))
                    .Where(attachment => PolicyAllowsReadContent(principal, attachment))
                    .Where(attachment => query.Role is null || attachment.Role == query.Role)
                    .Where(attachment => query.Name is null || attachment.Name == query.Name)
                    .OrderBy(attachment => attachment.CreatedAt);

                foreach (var attachment in attachments)
                {
                    if (!_snapshot.Content.TryGetValue(attachment.ContentId, out var contentRecord))
                        continue;

                    var contentVersion = contentRecord.FindVersion(attachment.ContentVersion);
                    if (contentVersion is null)
                        continue;

                    var content = Map(contentRecord.Id, contentVersion);
                    if (query.ContentType is not null &&
                        !content.ContentType.Equals(query.ContentType, StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    results.Add(MapVisibleContent(spaceInfo, Map(attachment), content));
                    if (query.Limit is not null && results.Count >= query.Limit.Value)
                        return Task.FromResult<IReadOnlyList<WorkspaceVisibleContentResult>>(results);
                }
            }

            return Task.FromResult<IReadOnlyList<WorkspaceVisibleContentResult>>(results);
        }
    }

    public Task DetachContentAsync(
        WorkspacePrincipalRef principal,
        string spaceId,
        string attachmentId,
        string? ifMatchVersion = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(spaceId);
        ArgumentException.ThrowIfNullOrWhiteSpace(attachmentId);
        cancellationToken.ThrowIfCancellationRequested();

        lock (_gate)
        {
            if (!_snapshot.Attachments.TryGetValue(attachmentId, out var attachment) ||
                attachment.SpaceId != spaceId)
            {
                return Task.CompletedTask;
            }
            EnsureCanWriteSpace(principal, spaceId);
            EnsurePolicyAllowed(PolicyFor(spaceId).CanDetachContent(
                new WorkspacePolicyContext(principal),
                Map(_snapshot.Spaces[spaceId]),
                Map(attachment)));

            EnsureVersionMatches(attachment.Version, ifMatchVersion, $"Attachment '{attachmentId}' version conflict.");
            _snapshot.Attachments.Remove(attachmentId);
            SaveSnapshot();
            return Task.CompletedTask;
        }
    }

    public async Task<WorkspaceEventAppendResult> AppendEventAsync(
        WorkspacePrincipalRef principal,
        string spaceId,
        AppendWorkspaceEventRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(spaceId);
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        var streamId = EventStreamKey(spaceId, request.Role);
        PendingContentWriteRecord? pendingDescriptor = null;
        WorkspaceContentObjectWriteResult? descriptor = null;

        lock (_gate)
        {
            if (!_snapshot.Spaces.TryGetValue(spaceId, out var space))
                throw new KeyNotFoundException($"Space '{spaceId}' was not found.");
            EnsureCanWriteSpace(principal, spaceId);
            EnsurePolicyAllowed(PolicyFor(space).CanAppendEvent(
                new WorkspacePolicyContext(principal),
                Map(space),
                request));

            if (!_snapshot.EventStreams.ContainsKey(streamId))
            {
                var descriptorContentId = NewId("ct");
                var descriptorVersion = NewVersion("content");
                pendingDescriptor = PendingContentWriteRecord.Create(descriptorContentId, descriptorVersion, spaceId, attachmentId: null);
                _snapshot.PendingContentWrites[pendingDescriptor.Id] = pendingDescriptor;
                SaveSnapshot();
            }
        }

        if (pendingDescriptor is not null)
        {
            try
            {
                await using var empty = new MemoryStream([]);
                descriptor = await _contentObjects.WriteAsync(
                    pendingDescriptor.ContentId,
                    pendingDescriptor.Version,
                    empty,
                    new WorkspaceContentObjectWriteRequest
                    {
                        ContentType = request.ContentType,
                        Name = request.Name,
                        Metadata = StreamAttachmentMetadata(request, streamId, latestSequenceNumber: 0)
                    },
                    cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                lock (_gate)
                {
                    _snapshot.PendingContentWrites[pendingDescriptor.Id] = pendingDescriptor.Abort(ex.Message);
                    SaveSnapshot();
                }

                await _contentObjects.DeleteVersionAsync(
                    pendingDescriptor.ContentId,
                    pendingDescriptor.Version,
                    CancellationToken.None).ConfigureAwait(false);
                throw;
            }
        }

        lock (_gate)
        {
            if (!_snapshot.Spaces.TryGetValue(spaceId, out var space))
                throw new KeyNotFoundException($"Space '{spaceId}' was not found.");
            EnsureCanWriteSpace(principal, spaceId);
            EnsurePolicyAllowed(PolicyFor(space).CanAppendEvent(
                new WorkspacePolicyContext(principal),
                Map(space),
                request));

            if (!_snapshot.EventStreams.ContainsKey(streamId))
            {
                var attachment = EnsureEventStreamAttachment(spaceId, request, streamId, latestSequenceNumber: 0, descriptor);
                _snapshot.EventStreams[streamId] = EventStreamRecord.Create(
                    streamId,
                    spaceId,
                    request.Role,
                    request.Name,
                    attachment.Id);
                if (pendingDescriptor is not null)
                    _snapshot.PendingContentWrites.Remove(pendingDescriptor.Id);
                SaveSnapshot();
            }
        }

        var append = await _eventStreams.AppendAsync(
            streamId,
            new AppendWorkspaceEventStreamRequest
            {
                SpaceId = spaceId,
                Role = request.Role,
                Payload = request.Payload,
                ExpectedSequenceNumber = request.ExpectedSequenceNumber,
                Metadata = request.Metadata
            },
            cancellationToken).ConfigureAwait(false);

        lock (_gate)
        {
            if (!_snapshot.Spaces.TryGetValue(spaceId, out var space))
                throw new KeyNotFoundException($"Space '{spaceId}' was not found.");
            EnsureCanWriteSpace(principal, spaceId);
            EnsurePolicyAllowed(PolicyFor(space).CanAppendEvent(
                new WorkspacePolicyContext(principal),
                Map(space),
                request));

            if (_snapshot.EventStreams.TryGetValue(streamId, out var record))
                _snapshot.EventStreams[streamId] = record.WithLatestSequence(append.SequenceNumber);

            var attachment = FindEventStreamAttachment(spaceId, request)
                ?? throw new InvalidOperationException("Event stream attachment was not created.");
            SaveSnapshot();
            return new WorkspaceEventAppendResult
            {
                SpaceId = spaceId,
                EventStreamAttachmentId = attachment.Id,
                EventStreamContentId = attachment.ContentId,
                SequenceNumber = append.SequenceNumber,
                NextSequenceNumber = append.NextSequenceNumber
            };
        }
    }

    public async IAsyncEnumerable<WorkspaceEventRecord> ReadEventsAsync(
        WorkspacePrincipalRef principal,
        string spaceId,
        WorkspaceEventStreamQuery query,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(spaceId);
        ArgumentNullException.ThrowIfNull(query);
        cancellationToken.ThrowIfCancellationRequested();

        bool canRead;
        lock (_gate)
        {
            canRead = CanReadSpace(principal, spaceId);
        }

        if (!canRead)
            yield break;

        await foreach (var evt in _eventStreams.ReadAsync(
            EventStreamKey(spaceId, query.Role),
            query,
            cancellationToken).ConfigureAwait(false))
        {
            cancellationToken.ThrowIfCancellationRequested();
            yield return evt;
        }
    }

    private AttachmentRecord? FindEventStreamAttachment(
        string spaceId,
        AppendWorkspaceEventRequest request) =>
        _snapshot.Attachments.Values.FirstOrDefault(attachment =>
            attachment.SpaceId == spaceId &&
            attachment.Role == request.Role &&
            attachment.Name == request.Name);

    private AttachmentRecord EnsureEventStreamAttachment(
        string spaceId,
        AppendWorkspaceEventRequest request,
        string streamId,
        long latestSequenceNumber,
        WorkspaceContentObjectWriteResult? descriptor)
    {
        var existing = FindEventStreamAttachment(spaceId, request);
        if (existing is not null)
            return existing;

        if (descriptor is null)
            throw new InvalidOperationException("Event stream content descriptor was not created.");

        var content = ContentRecord.Create(descriptor);
        _snapshot.Content[content.Id] = content;

        var attachment = AttachmentRecord.Create(
            spaceId,
            content.Id,
            content.Version,
            new AttachWorkspaceContentRequest
            {
                Role = request.Role,
                Name = request.Name,
                Permission = "read_write",
                Metadata = StreamAttachmentMetadata(request, streamId, latestSequenceNumber)
            });
        _snapshot.Attachments[attachment.Id] = attachment;
        return attachment;
    }

    private void EnsureSpaceDoesNotExist(string kind, string externalId, string? parentSpaceId)
    {
        if (_snapshot.Spaces.Values.Any(space =>
            space.Kind == kind &&
            space.ExternalId == externalId &&
            space.ParentSpaceId == parentSpaceId))
        {
            throw new WorkspaceConflictException(
                $"Space kind '{kind}' with external id '{externalId}' already exists.");
        }
    }

    private void CollectDescendantSpaceIds(string parentSpaceId, HashSet<string> spaceIds)
    {
        foreach (var child in _snapshot.Spaces.Values.Where(space => space.ParentSpaceId == parentSpaceId).ToList())
        {
            if (spaceIds.Add(child.Id))
                CollectDescendantSpaceIds(child.Id, spaceIds);
        }
    }

    private IReadOnlyList<SpaceRecord> ResolveSearchSpaces(
        WorkspacePrincipalRef principal,
        WorkspaceVisibleContentQuery query)
    {
        IEnumerable<SpaceRecord> spaces = query.TraversalMode switch
        {
            WorkspaceContentTraversalMode.SpaceOnly => string.IsNullOrWhiteSpace(query.SpaceId)
                ? []
                : _snapshot.Spaces.TryGetValue(query.SpaceId, out var space) ? [space] : [],
            WorkspaceContentTraversalMode.SpaceDescendants => string.IsNullOrWhiteSpace(query.SpaceId)
                ? []
                : ResolveSpaceAndDescendants(query.SpaceId),
            _ => _snapshot.Spaces.Values
        };

        return spaces
            .Where(space => query.SpaceKind is null || space.Kind == query.SpaceKind)
            .Where(space => CanReadSpace(principal, space.Id))
            .OrderBy(space => space.CreatedAt)
            .ToList();
    }

    private IReadOnlyList<SpaceRecord> ResolveSpaceAndDescendants(string spaceId)
    {
        if (!_snapshot.Spaces.TryGetValue(spaceId, out var root))
            return [];

        var results = new List<SpaceRecord> { root };
        AddDescendantSpaces(spaceId, results);
        return results;
    }

    private void AddDescendantSpaces(string parentSpaceId, List<SpaceRecord> results)
    {
        foreach (var child in _snapshot.Spaces.Values
            .Where(space => space.ParentSpaceId == parentSpaceId)
            .OrderBy(space => space.CreatedAt)
            .ToList())
        {
            results.Add(child);
            AddDescendantSpaces(child.Id, results);
        }
    }

    private void GrantOwnerAccess(string spaceId, WorkspacePrincipalRef principal)
    {
        if (IsSystem(principal))
            return;

        var record = AccessRecord.Create(
            spaceId,
            principal,
            new GrantWorkspaceSpaceAccessRequest
            {
                Grantee = principal,
                Permission = WorkspacePermissions.Owner,
                Role = "owner"
            });
        _snapshot.Access[record.Id] = record;
    }

    private void InheritAccess(string parentSpaceId, string childSpaceId)
    {
        foreach (var inherited in _snapshot.Access.Values.Where(access =>
            access.SpaceId == parentSpaceId &&
            IsActive(access)).ToList())
        {
            var record = inherited with
            {
                Id = NewId("sa"),
                SpaceId = childSpaceId,
                CreatedAt = DateTimeOffset.UtcNow,
                RevokedAt = null
            };
            _snapshot.Access[record.Id] = record;
        }
    }

    private bool CanReadSpace(WorkspacePrincipalRef principal, string spaceId) =>
        IsSystem(principal) || HasSpacePermission(principal, spaceId, AllowsRead);

    private bool CanWriteSpace(WorkspacePrincipalRef principal, string spaceId) =>
        IsSystem(principal) || HasSpacePermission(principal, spaceId, AllowsWrite);

    private bool CanManageSpace(WorkspacePrincipalRef principal, string spaceId) =>
        IsSystem(principal) || HasSpacePermission(principal, spaceId, AllowsManage);

    private void EnsureCanWriteSpace(WorkspacePrincipalRef principal, string spaceId)
    {
        if (!CanWriteSpace(principal, spaceId))
            throw new WorkspaceAccessDeniedException($"Principal '{principal.Kind}:{principal.Id}' cannot write space '{spaceId}'.");
    }

    private void EnsureCanManageSpace(WorkspacePrincipalRef principal, string spaceId)
    {
        if (!CanManageSpace(principal, spaceId))
            throw new WorkspaceAccessDeniedException($"Principal '{principal.Kind}:{principal.Id}' cannot manage space '{spaceId}'.");
    }

    private bool CanReadContentVersion(WorkspacePrincipalRef principal, string contentId, string contentVersion) =>
        IsSystem(principal) ||
        _snapshot.Attachments.Values.Any(attachment =>
            attachment.ContentId == contentId &&
            attachment.ContentVersion == contentVersion &&
            AllowsRead(attachment.Permission) &&
            CanReadSpace(principal, attachment.SpaceId) &&
            PolicyAllowsReadContent(principal, attachment));

    private IContentSpacePolicy PolicyFor(string spaceId) => PolicyFor(_snapshot.Spaces[spaceId]);

    private IContentSpacePolicy PolicyFor(SpaceRecord space) => _policies.Resolve(space.Kind);

    private bool PolicyAllowsReadContent(WorkspacePrincipalRef principal, AttachmentRecord attachment)
    {
        if (!_snapshot.Spaces.TryGetValue(attachment.SpaceId, out var space))
            return false;

        return PolicyFor(space).CanReadContent(
            new WorkspacePolicyContext(principal),
            Map(space),
            Map(attachment)).Allowed;
    }

    private static void EnsurePolicyAllowed(WorkspacePolicyDecision decision)
    {
        if (!decision.Allowed)
            throw new WorkspaceAccessDeniedException(decision.Reason ?? "Workspace space policy denied the operation.");
    }

    private bool HasSpacePermission(
        WorkspacePrincipalRef principal,
        string spaceId,
        Func<string, bool> allows)
    {
        var now = DateTimeOffset.UtcNow;
        return _snapshot.Access.Values.Any(access =>
            access.SpaceId == spaceId &&
            access.Principal == principal &&
            IsActive(access, now) &&
            allows(access.Permission));
    }

    private static bool IsActive(AccessRecord access) => IsActive(access, DateTimeOffset.UtcNow);

    private static bool IsActive(AccessRecord access, DateTimeOffset now) =>
        access.RevokedAt is null &&
        (access.ExpiresAt is null || access.ExpiresAt > now);

    private static bool IsSystem(WorkspacePrincipalRef principal) =>
        principal == WorkspacePrincipalRef.System;

    private static bool ShouldCleanPendingWrite(
        PendingContentWriteRecord write,
        WorkspacePendingContentWriteCleanupRequest request,
        DateTimeOffset now)
    {
        if (request.IncludeAborted && string.Equals(write.Status, "aborted", StringComparison.Ordinal))
            return true;

        return request.IncludePendingOlderThan is { } age &&
            string.Equals(write.Status, "pending", StringComparison.Ordinal) &&
            write.CreatedAt <= now - age;
    }

    private static bool ShouldRepairEventStream(
        EventStreamRecord stream,
        WorkspaceEventStreamRepairRequest request) =>
        (request.SpaceId is null || stream.SpaceId == request.SpaceId) &&
        (request.Role is null || stream.Role == request.Role);

    private static bool AllowsRead(string permission) =>
        permission is WorkspacePermissions.Read
            or WorkspacePermissions.Write
            or WorkspacePermissions.ReadWrite
            or WorkspacePermissions.Manage
            or WorkspacePermissions.Owner;

    private static bool AllowsWrite(string permission) =>
        permission is WorkspacePermissions.Write
            or WorkspacePermissions.ReadWrite
            or WorkspacePermissions.Manage
            or WorkspacePermissions.Owner;

    private static bool AllowsManage(string permission) =>
        permission is WorkspacePermissions.Manage
            or WorkspacePermissions.Owner;

    private void SaveSnapshot()
    {
        var tempPath = _filePath + ".tmp";
        File.WriteAllText(tempPath, JsonSerializer.Serialize(_snapshot, JsonOptions));
        File.Move(tempPath, _filePath, overwrite: true);
    }

    private static WorkspaceSnapshot LoadSnapshot(string filePath)
    {
        if (!File.Exists(filePath))
            return new WorkspaceSnapshot();

        var loaded = JsonSerializer.Deserialize<WorkspaceSnapshot>(File.ReadAllText(filePath), JsonOptions);
        return loaded ?? new WorkspaceSnapshot();
    }

    private static SpaceRecord CreateSpaceRecord(CreateWorkspaceSpaceRequest request, string? parentSpaceId)
    {
        var now = DateTimeOffset.UtcNow;
        return new SpaceRecord(
            Id: NewId("sp"),
            Kind: Required(request.Kind, nameof(request.Kind)),
            ExternalId: Required(request.ExternalId, nameof(request.ExternalId)),
            Name: Required(request.Name, nameof(request.Name)),
            Slug: request.Slug,
            ParentSpaceId: parentSpaceId,
            Version: NewVersion("space"),
            CreatedAt: now,
            UpdatedAt: now,
            Metadata: request.Metadata);
    }

    private static bool Matches(SpaceRecord space, WorkspaceSpaceQuery query) =>
        (query.Kind is null || space.Kind == query.Kind) &&
        (query.ExternalId is null || space.ExternalId == query.ExternalId) &&
        (query.ParentSpaceId is null || space.ParentSpaceId == query.ParentSpaceId);

    private static void EnsureVersionMatches(string actual, string? expected, string message)
    {
        if (expected is not null && actual != expected)
            throw new WorkspaceConflictException(message, expected, actual);
    }

    private static WorkspaceSpaceInfo Map(SpaceRecord record) => new()
    {
        Id = record.Id,
        Kind = record.Kind,
        ExternalId = record.ExternalId,
        Name = record.Name,
        Slug = record.Slug,
        ParentSpaceId = record.ParentSpaceId,
        Version = record.Version,
        CreatedAt = record.CreatedAt,
        UpdatedAt = record.UpdatedAt,
        Metadata = record.Metadata
    };

    private static WorkspaceContentInfo Map(ContentRecord record) => new()
    {
        Id = record.Id,
        Version = record.Version,
        ContentType = record.ContentType,
        Checksum = record.Checksum,
        StorageKey = record.StorageKey,
        SizeBytes = record.SizeBytes,
        Name = record.Name,
        CreatedAt = record.CreatedAt,
        UpdatedAt = record.UpdatedAt,
        Metadata = record.Metadata
    };

    private static WorkspaceContentInfo Map(string contentId, ContentVersionRecord record) => new()
    {
        Id = contentId,
        Version = record.Version,
        ContentType = record.ContentType,
        Checksum = record.Checksum,
        StorageKey = record.StorageKey,
        SizeBytes = record.SizeBytes,
        Name = record.Name,
        CreatedAt = record.CreatedAt,
        UpdatedAt = record.UpdatedAt,
        Metadata = record.Metadata
    };

    private static WorkspaceContentAttachmentInfo Map(AttachmentRecord record) => new()
    {
        Id = record.Id,
        SpaceId = record.SpaceId,
        ContentId = record.ContentId,
        ContentVersion = record.ContentVersion,
        Role = record.Role,
        Name = record.Name,
        PathHint = record.PathHint,
        Permission = record.Permission,
        Version = record.Version,
        CreatedAt = record.CreatedAt,
        Metadata = record.Metadata
    };

    private static WorkspaceSpaceAccessInfo Map(AccessRecord record) => new()
    {
        Id = record.Id,
        SpaceId = record.SpaceId,
        Principal = record.Principal,
        Permission = record.Permission,
        Role = record.Role,
        CreatedAt = record.CreatedAt,
        CreatedBy = record.CreatedBy,
        ExpiresAt = record.ExpiresAt,
        RevokedAt = record.RevokedAt,
        Metadata = record.Metadata
    };

    private static WorkspaceVisibleContentResult MapVisibleContent(
        WorkspaceSpaceInfo space,
        WorkspaceContentAttachmentInfo attachment,
        WorkspaceContentInfo content) => new()
    {
        ContentId = attachment.ContentId,
        ContentVersion = attachment.ContentVersion,
        SpaceId = space.Id,
        SpaceKind = space.Kind,
        SpaceName = space.Name,
        SpaceContentId = attachment.Id,
        Name = attachment.Name,
        Role = attachment.Role,
        Permission = attachment.Permission,
        ContentType = content.ContentType,
        Space = space,
        Attachment = attachment,
        Content = content
    };

    private static string Required(string value, string parameterName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value, parameterName);
        return value;
    }

    private static string EventStreamKey(string spaceId, string role) => $"{spaceId}::{role}";

    private static IReadOnlyDictionary<string, string> StreamAttachmentMetadata(
        AppendWorkspaceEventRequest request,
        string streamId,
        long latestSequenceNumber)
    {
        var metadata = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["stream_id"] = streamId,
            ["latest_sequence"] = latestSequenceNumber.ToString(System.Globalization.CultureInfo.InvariantCulture)
        };

        if (request.Metadata is not null)
        {
            foreach (var pair in request.Metadata)
                metadata[pair.Key] = pair.Value;
        }

        return metadata;
    }

    private static string NewId(string prefix) => $"{prefix}_{Guid.NewGuid():N}";

    private static string NewVersion(string prefix) => $"{prefix}:{Guid.NewGuid():N}";

    private static WorkspaceContentObjectWriteRequest ToObjectWriteRequest(WriteWorkspaceSpaceContentRequest request) => new()
    {
        ContentType = request.ContentType,
        Name = request.Name,
        Metadata = request.ContentMetadata
    };

    private static AttachWorkspaceContentRequest ToAttachRequest(
        WriteWorkspaceSpaceContentRequest request,
        string contentVersion) => new()
    {
        Role = request.Role,
        Name = request.Name,
        PathHint = request.PathHint,
        Permission = request.Permission,
        ContentVersion = contentVersion,
        Metadata = request.AttachmentMetadata
    };

    private sealed class WorkspaceSnapshot
    {
        public Dictionary<string, SpaceRecord> Spaces { get; set; } = new(StringComparer.Ordinal);
        public Dictionary<string, ContentRecord> Content { get; set; } = new(StringComparer.Ordinal);
        public Dictionary<string, AttachmentRecord> Attachments { get; set; } = new(StringComparer.Ordinal);
        public Dictionary<string, AccessRecord> Access { get; set; } = new(StringComparer.Ordinal);
        public Dictionary<string, PendingContentWriteRecord> PendingContentWrites { get; set; } = new(StringComparer.Ordinal);
        public Dictionary<string, EventStreamRecord> EventStreams { get; set; } = new(StringComparer.Ordinal);
    }

    private sealed record SpaceRecord(
        string Id,
        string Kind,
        string ExternalId,
        string Name,
        string? Slug,
        string? ParentSpaceId,
        string Version,
        DateTimeOffset CreatedAt,
        DateTimeOffset UpdatedAt,
        IReadOnlyDictionary<string, string>? Metadata);

    private sealed record ContentRecord(
        string Id,
        string Version,
        string ContentType,
        string Checksum,
        string StorageKey,
        long SizeBytes,
        string? Name,
        DateTimeOffset CreatedAt,
        DateTimeOffset UpdatedAt,
        IReadOnlyDictionary<string, string>? Metadata,
        IReadOnlyList<ContentVersionRecord> PreviousVersions)
    {
        public ContentVersionRecord CurrentVersion => new(
            Version,
            ContentType,
            Checksum,
            StorageKey,
            SizeBytes,
            Name,
            CreatedAt,
            UpdatedAt,
            Metadata);

        public ContentVersionRecord? FindVersion(string? version)
        {
            if (version is null || Version == version)
                return CurrentVersion;

            return PreviousVersions.FirstOrDefault(candidate => candidate.Version == version);
        }

        public static ContentRecord Create(WorkspaceContentObjectWriteResult result) =>
            new(
                Id: result.ContentId,
                Version: result.Version,
                ContentType: result.ContentType,
                Checksum: result.Checksum,
                StorageKey: result.StorageKey,
                SizeBytes: result.SizeBytes,
                Name: result.Name,
                CreatedAt: result.CreatedAt,
                UpdatedAt: result.UpdatedAt,
                Metadata: result.Metadata,
                PreviousVersions: []);

        public ContentRecord Replace(WorkspaceContentObjectWriteResult result) =>
            this with
            {
                Version = result.Version,
                ContentType = result.ContentType,
                Checksum = result.Checksum,
                StorageKey = result.StorageKey,
                SizeBytes = result.SizeBytes,
                Name = result.Name ?? Name,
                UpdatedAt = result.UpdatedAt,
                Metadata = result.Metadata ?? Metadata,
                PreviousVersions = [.. PreviousVersions, CurrentVersion]
            };
    }

    private sealed record ContentVersionRecord(
        string Version,
        string ContentType,
        string Checksum,
        string StorageKey,
        long SizeBytes,
        string? Name,
        DateTimeOffset CreatedAt,
        DateTimeOffset UpdatedAt,
        IReadOnlyDictionary<string, string>? Metadata);

    private sealed record PendingContentWriteRecord(
        string Id,
        string ContentId,
        string Version,
        string SpaceId,
        string? AttachmentId,
        string Status,
        DateTimeOffset CreatedAt,
        DateTimeOffset UpdatedAt,
        string? Error)
    {
        public static PendingContentWriteRecord Create(
            string contentId,
            string version,
            string spaceId,
            string? attachmentId)
        {
            var now = DateTimeOffset.UtcNow;
            return new PendingContentWriteRecord(
                Id: NewId("pcw"),
                ContentId: contentId,
                Version: version,
                SpaceId: spaceId,
                AttachmentId: attachmentId,
                Status: "pending",
                CreatedAt: now,
                UpdatedAt: now,
                Error: null);
        }

        public PendingContentWriteRecord Abort(string error) =>
            this with
            {
                Status = "aborted",
                UpdatedAt = DateTimeOffset.UtcNow,
                Error = error
            };
    }

    private sealed record PendingContentWriteCleanupCandidate(
        string Id,
        string ContentId,
        string Version);

    private sealed record EventStreamRepairCandidate(
        string StreamId,
        long LatestSequenceNumber);

    private sealed record EventStreamRecord(
        string StreamId,
        string SpaceId,
        string Role,
        string Name,
        string AttachmentId,
        long LatestSequenceNumber,
        string Status,
        DateTimeOffset CreatedAt,
        DateTimeOffset UpdatedAt)
    {
        public static EventStreamRecord Create(
            string streamId,
            string spaceId,
            string role,
            string name,
            string attachmentId) =>
            new(
                StreamId: streamId,
                SpaceId: spaceId,
                Role: role,
                Name: name,
                AttachmentId: attachmentId,
                LatestSequenceNumber: 0,
                Status: "active",
                CreatedAt: DateTimeOffset.UtcNow,
                UpdatedAt: DateTimeOffset.UtcNow);

        public EventStreamRecord WithLatestSequence(long latestSequenceNumber) =>
            this with
            {
                LatestSequenceNumber = latestSequenceNumber,
                UpdatedAt = DateTimeOffset.UtcNow
            };
    }

    private sealed record AttachmentRecord(
        string Id,
        string SpaceId,
        string ContentId,
        string ContentVersion,
        string Role,
        string Name,
        string? PathHint,
        string Permission,
        string Version,
        DateTimeOffset CreatedAt,
        IReadOnlyDictionary<string, string>? Metadata)
    {
        public static AttachmentRecord Create(
            string spaceId,
            string contentId,
            string contentVersion,
            AttachWorkspaceContentRequest request) =>
            new(
                Id: NewId("sc"),
                SpaceId: spaceId,
                ContentId: contentId,
                ContentVersion: contentVersion,
                Role: Required(request.Role, nameof(request.Role)),
                Name: Required(request.Name, nameof(request.Name)),
                PathHint: request.PathHint,
                Permission: request.Permission,
                Version: NewVersion("attachment"),
                CreatedAt: DateTimeOffset.UtcNow,
                Metadata: request.Metadata);

        public AttachmentRecord WithContentVersion(string contentVersion) =>
            this with
            {
                ContentVersion = contentVersion,
                Version = NewVersion("attachment")
        };
    }

    private sealed record AccessRecord(
        string Id,
        string SpaceId,
        WorkspacePrincipalRef Principal,
        string Permission,
        string? Role,
        DateTimeOffset CreatedAt,
        WorkspacePrincipalRef CreatedBy,
        DateTimeOffset? ExpiresAt,
        DateTimeOffset? RevokedAt,
        IReadOnlyDictionary<string, string>? Metadata)
    {
        public static AccessRecord Create(
            string spaceId,
            WorkspacePrincipalRef createdBy,
            GrantWorkspaceSpaceAccessRequest request) =>
            new(
                Id: NewId("sa"),
                SpaceId: spaceId,
                Principal: request.Grantee,
                Permission: string.IsNullOrWhiteSpace(request.Permission)
                    ? WorkspacePermissions.Read
                    : request.Permission,
                Role: request.Role,
                CreatedAt: DateTimeOffset.UtcNow,
                CreatedBy: createdBy,
                ExpiresAt: request.ExpiresAt,
                RevokedAt: null,
                Metadata: request.Metadata);
    }

}
