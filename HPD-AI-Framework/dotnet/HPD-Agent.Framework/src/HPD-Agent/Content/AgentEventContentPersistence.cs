using System.Text;
using HPD.Agent.Serialization;

namespace HPD.Agent;

internal static class AgentEventContentPersistence
{
    public static async Task<WorkspaceContentInfo?> PersistAsync(
        IWorkspaceStore? workspace,
        AgentEvent evt,
        string? defaultScope,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(evt);

        var request = evt.GetContentPersistenceRequest();
        if (workspace == null || request == null)
            return null;

        var space = await ResolveSpaceAsync(
            workspace,
            request.Scope ?? defaultScope,
            evt,
            cancellationToken).ConfigureAwait(false);
        if (space is null)
            return null;

        var tags = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["event.type"] = AgentEventSerializer.GetEventTypeName(evt),
            ["origin"] = request.Origin.ToString()
        };

        AddIfPresent(tags, "event.id", evt.EventId);
        AddIfPresent(tags, "session", evt.SessionId);
        AddIfPresent(tags, "branch", evt.BranchId);
        AddIfPresent(tags, "trace", evt.TraceId);
        AddIfPresent(tags, "span", evt.SpanId);
        AddIfPresent(tags, "description", request.Description);

        if (evt.Metadata != null)
        {
            AddIfPresent(tags, "agent.name", evt.Metadata.AgentName);
            AddIfPresent(tags, "agent.id", evt.Metadata.AgentId);
        }

        if (request.Tags != null)
        {
            foreach (var tag in request.Tags)
            {
                if (!string.IsNullOrWhiteSpace(tag.Key) && tag.Value != null)
                    tags[tag.Key] = tag.Value;
            }
        }

        var existingAttachment = await FindExistingAttachmentAsync(
            workspace,
            space.Id,
            request,
            cancellationToken).ConfigureAwait(false);

        var json = AgentEventSerializer.ToJson(evt);
        await using var stream = new MemoryStream(Encoding.UTF8.GetBytes(json), writable: false);
        var attachment = await workspace.WriteContentAsync(
            WorkspacePrincipalRef.System,
            space.Id,
            existingAttachment?.Id,
            stream,
            new WriteWorkspaceSpaceContentRequest
            {
                IfMatchAttachmentVersion = request.IfMatchAttachmentVersion ?? existingAttachment?.Version,
                IfMatchContentVersion = request.IfMatchContentVersion ?? existingAttachment?.ContentVersion,
                ContentType = request.ContentType,
                Role = request.Role,
                Name = request.Name,
                PathHint = request.PathHint,
                Permission = WorkspacePermissions.ReadWrite,
                ContentMetadata = tags
            },
            cancellationToken).ConfigureAwait(false);

        return await workspace.StatContentAsync(
            WorkspacePrincipalRef.System,
            attachment.ContentId,
            attachment.ContentVersion,
            cancellationToken).ConfigureAwait(false);
    }

    private static async Task<WorkspaceSpaceInfo?> ResolveSpaceAsync(
        IWorkspaceStore workspace,
        string? scope,
        AgentEvent evt,
        CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(evt.SessionId) &&
            !string.IsNullOrWhiteSpace(evt.BranchId))
        {
            var session = await EnsureSpaceAsync(
                workspace,
                WorkspaceSessionRepository.SessionKind,
                evt.SessionId,
                evt.SessionId,
                cancellationToken).ConfigureAwait(false);

            var branch = await workspace.FindSpaceAsync(
                WorkspacePrincipalRef.System,
                new WorkspaceSpaceQuery
                {
                    Kind = WorkspaceSessionRepository.BranchKind,
                    ExternalId = evt.BranchId,
                    ParentSpaceId = session.Id
                },
                cancellationToken).ConfigureAwait(false);
            return branch ?? await workspace.CreateChildSpaceAsync(
                WorkspacePrincipalRef.System,
                session.Id,
                new CreateWorkspaceSpaceRequest
                {
                    Kind = WorkspaceSessionRepository.BranchKind,
                    ExternalId = evt.BranchId,
                    Name = evt.BranchId,
                    Slug = evt.BranchId
                },
                cancellationToken).ConfigureAwait(false);
        }

        if (!string.IsNullOrWhiteSpace(scope))
        {
            var scoped = await workspace.FindSpaceAsync(
                WorkspacePrincipalRef.System,
                new WorkspaceSpaceQuery { ExternalId = scope },
                cancellationToken).ConfigureAwait(false);
            if (scoped is not null)
                return scoped;
        }

        if (!string.IsNullOrWhiteSpace(evt.SessionId))
        {
            return await EnsureSpaceAsync(
                workspace,
                WorkspaceSessionRepository.SessionKind,
                evt.SessionId,
                evt.SessionId,
                cancellationToken).ConfigureAwait(false);
        }

        if (!string.IsNullOrWhiteSpace(evt.Metadata?.AgentName))
        {
            return await EnsureSpaceAsync(
                workspace,
                WorkspaceAgentRepository.AgentKind,
                evt.Metadata.AgentName,
                evt.Metadata.AgentName,
                cancellationToken).ConfigureAwait(false);
        }

        return null;
    }

    private static async Task<WorkspaceSpaceInfo> EnsureSpaceAsync(
        IWorkspaceStore workspace,
        string kind,
        string externalId,
        string name,
        CancellationToken cancellationToken)
    {
        var existing = await workspace.FindSpaceAsync(
            WorkspacePrincipalRef.System,
            new WorkspaceSpaceQuery
            {
                Kind = kind,
                ExternalId = externalId
            },
            cancellationToken).ConfigureAwait(false);
        if (existing is not null)
            return existing;

        return await workspace.CreateSpaceAsync(
            WorkspacePrincipalRef.System,
            new CreateWorkspaceSpaceRequest
            {
                Kind = kind,
                ExternalId = externalId,
                Name = name,
                Slug = externalId
            },
            cancellationToken).ConfigureAwait(false);
    }

    private static async Task<WorkspaceContentAttachmentInfo?> FindExistingAttachmentAsync(
        IWorkspaceStore workspace,
        string spaceId,
        ContentPersistenceRequest request,
        CancellationToken cancellationToken)
    {
        var existing = await workspace.ListContentAsync(
            WorkspacePrincipalRef.System,
            spaceId,
            new WorkspaceContentAttachmentQuery
            {
                Role = request.Role,
                Name = request.Name
            },
            cancellationToken).ConfigureAwait(false);

        return existing.FirstOrDefault(candidate =>
            string.Equals(candidate.PathHint, request.PathHint, StringComparison.Ordinal));
    }

    private static void AddIfPresent(Dictionary<string, string> tags, string key, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
            tags[key] = value;
    }
}
