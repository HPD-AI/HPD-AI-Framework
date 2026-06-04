using System.Text;

namespace HPD.Agent;

/// <summary>
/// Convenience helpers for common workspace content roles.
/// </summary>
public static class WorkspaceContentExtensions
{
    public static async Task<WorkspaceContentInfo> UploadSkillDocumentAsync(
        this IWorkspaceStore workspace,
        string agentName,
        string documentId,
        string content,
        string description,
        CancellationToken cancellationToken = default)
    {
        var space = await EnsureAgentSpaceAsync(workspace, agentName, cancellationToken).ConfigureAwait(false);
        return await WriteNamedTextAsync(
            workspace,
            space.Id,
            content,
            WorkspaceContentRoles.Skill,
            documentId,
            WorkspaceContentPaths.AgentSkills(agentName),
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["description"] = description,
                ["origin"] = ContentSource.System.ToString()
            },
            cancellationToken).ConfigureAwait(false);
    }

    public static async Task LinkSkillDocumentAsync(
        this IWorkspaceStore workspace,
        string agentName,
        string documentId,
        string skillName,
        string descriptionOverride,
        CancellationToken cancellationToken = default)
    {
        var space = await EnsureAgentSpaceAsync(workspace, agentName, cancellationToken).ConfigureAwait(false);
        var attachments = await workspace.ListContentAsync(
            WorkspacePrincipalRef.System,
            space.Id,
            new WorkspaceContentAttachmentQuery
            {
                Role = WorkspaceContentRoles.Skill,
                Name = documentId
            },
            cancellationToken).ConfigureAwait(false);

        var attachment = attachments.FirstOrDefault(candidate =>
            string.Equals(candidate.PathHint, WorkspaceContentPaths.AgentSkills(agentName), StringComparison.Ordinal));
        if (attachment is null)
            throw new InvalidOperationException(
                $"Skill document '{documentId}' not found. Upload it first via UploadSkillDocumentAsync.");

        var info = await workspace.StatContentAsync(
            WorkspacePrincipalRef.System,
            attachment.ContentId,
            attachment.ContentVersion,
            cancellationToken).ConfigureAwait(false);
        if (info is null)
            return;

        await using var stream = await workspace.OpenContentAsync(
            WorkspacePrincipalRef.System,
            attachment.ContentId,
            attachment.ContentVersion,
            cancellationToken).ConfigureAwait(false);
        if (stream is null)
            return;

        var metadata = new Dictionary<string, string>(
            info.Metadata ?? new Dictionary<string, string>(StringComparer.Ordinal),
            StringComparer.Ordinal)
        {
            [$"description:{skillName}"] = descriptionOverride
        };

        await workspace.WriteContentAsync(
            WorkspacePrincipalRef.System,
            space.Id,
            attachment.Id,
            stream,
            new WriteWorkspaceSpaceContentRequest
            {
                IfMatchContentVersion = info.Version,
                IfMatchAttachmentVersion = attachment.Version,
                ContentType = info.ContentType,
                Role = attachment.Role,
                Name = attachment.Name,
                PathHint = attachment.PathHint,
                Permission = attachment.Permission,
                ContentMetadata = metadata,
                AttachmentMetadata = attachment.Metadata
            },
            cancellationToken).ConfigureAwait(false);
    }

    public static async Task<WorkspaceContentInfo> UploadKnowledgeDocumentAsync(
        this IWorkspaceStore workspace,
        string agentName,
        string documentName,
        byte[] data,
        string contentType,
        string? description = null,
        IReadOnlyDictionary<string, string>? extraTags = null,
        CancellationToken cancellationToken = default)
    {
        var space = await EnsureAgentSpaceAsync(workspace, agentName, cancellationToken).ConfigureAwait(false);
        var metadata = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["origin"] = ContentSource.System.ToString()
        };
        if (!string.IsNullOrWhiteSpace(description))
            metadata["description"] = description;
        if (extraTags is not null)
        {
            foreach (var tag in extraTags)
                metadata[tag.Key] = tag.Value;
        }

        return await WriteNamedBytesAsync(
            workspace,
            space.Id,
            data,
            contentType,
            WorkspaceContentRoles.Knowledge,
            documentName,
            WorkspaceContentPaths.AgentKnowledge(agentName),
            metadata,
            cancellationToken).ConfigureAwait(false);
    }

    public static async Task<WorkspaceContentInfo> WriteMemoryAsync(
        this IWorkspaceStore workspace,
        string agentName,
        string title,
        string content,
        CancellationToken cancellationToken = default)
    {
        var space = await EnsureAgentSpaceAsync(workspace, agentName, cancellationToken).ConfigureAwait(false);
        return await WriteNamedTextAsync(
            workspace,
            space.Id,
            content,
            WorkspaceContentRoles.Memory,
            title,
            WorkspaceContentPaths.AgentMemoryEvents(agentName),
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["origin"] = ContentSource.Agent.ToString(),
                ["memory.kind"] = "agent_note"
            },
            cancellationToken).ConfigureAwait(false);
    }

    private static async Task<WorkspaceContentInfo> WriteNamedTextAsync(
        IWorkspaceStore workspace,
        string spaceId,
        string content,
        string role,
        string name,
        string pathHint,
        IReadOnlyDictionary<string, string> metadata,
        CancellationToken cancellationToken)
    {
        var data = Encoding.UTF8.GetBytes(content);
        return await WriteNamedBytesAsync(
            workspace,
            spaceId,
            data,
            "text/plain",
            role,
            name,
            pathHint,
            metadata,
            cancellationToken).ConfigureAwait(false);
    }

    private static async Task<WorkspaceContentInfo> WriteNamedBytesAsync(
        IWorkspaceStore workspace,
        string spaceId,
        byte[] data,
        string contentType,
        string role,
        string name,
        string pathHint,
        IReadOnlyDictionary<string, string> metadata,
        CancellationToken cancellationToken)
    {
        var attachments = await workspace.ListContentAsync(
            WorkspacePrincipalRef.System,
            spaceId,
            new WorkspaceContentAttachmentQuery
            {
                Role = role,
                Name = name
            },
            cancellationToken).ConfigureAwait(false);

        var existing = attachments.FirstOrDefault(candidate =>
            string.Equals(candidate.PathHint, pathHint, StringComparison.Ordinal));
        await using var stream = new MemoryStream(data, writable: false);
        var attachment = await workspace.WriteContentAsync(
            WorkspacePrincipalRef.System,
            spaceId,
            existing?.Id,
            stream,
            new WriteWorkspaceSpaceContentRequest
            {
                IfMatchAttachmentVersion = existing?.Version,
                IfMatchContentVersion = existing?.ContentVersion,
                ContentType = contentType,
                Role = role,
                Name = name,
                PathHint = pathHint,
                Permission = WorkspacePermissions.ReadWrite,
                ContentMetadata = metadata
            },
            cancellationToken).ConfigureAwait(false);

        return await workspace.StatContentAsync(
            WorkspacePrincipalRef.System,
            attachment.ContentId,
            attachment.ContentVersion,
            cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException($"Workspace content '{attachment.ContentId}' was not found after write.");
    }

    private static async Task<WorkspaceSpaceInfo> EnsureAgentSpaceAsync(
        IWorkspaceStore workspace,
        string agentName,
        CancellationToken cancellationToken)
    {
        var existing = await workspace.FindSpaceAsync(
            WorkspacePrincipalRef.System,
            new WorkspaceSpaceQuery
            {
                Kind = WorkspaceAgentRepository.AgentKind,
                ExternalId = agentName
            },
            cancellationToken).ConfigureAwait(false);
        if (existing is not null)
            return existing;

        return await workspace.CreateSpaceAsync(
            WorkspacePrincipalRef.System,
            new CreateWorkspaceSpaceRequest
            {
                Kind = WorkspaceAgentRepository.AgentKind,
                ExternalId = agentName,
                Name = agentName,
                Slug = agentName
            },
            cancellationToken).ConfigureAwait(false);
    }
}
