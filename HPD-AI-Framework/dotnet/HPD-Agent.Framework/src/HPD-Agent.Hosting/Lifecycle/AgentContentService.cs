using HPD.Agent.Hosting.Data;

namespace HPD.Agent.Hosting.Lifecycle;

public sealed class AgentContentService : IAgentContentService
{
    private readonly SessionManager _sessionManager;
    private readonly IWorkspaceStore _workspace;

    public AgentContentService(SessionManager sessionManager, IWorkspaceStore? workspace = null)
    {
        _sessionManager = sessionManager ?? throw new ArgumentNullException(nameof(sessionManager));
        _workspace = workspace ?? ResolveWorkspace(sessionManager.Repository);
    }

    private static IWorkspaceStore ResolveWorkspace(ISessionRepository repository)
    {
        if (repository is not WorkspaceSessionRepository workspaceRepository)
        {
            throw new InvalidOperationException(
                "AgentContentService requires a workspace-backed session repository when no workspace is supplied.");
        }

        return workspaceRepository.Workspace;
    }

    public async Task<AgentServiceResult<ContentDto>> UploadContentAsync(
        string sessionId,
        string branchId,
        Stream content,
        string fileName,
        string? contentType,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(branchId);
        ArgumentNullException.ThrowIfNull(content);

        if (await _sessionManager.Repository.LoadSessionAsync(sessionId, cancellationToken) == null)
            return AgentServiceResult<ContentDto>.NotFound;

        if (await _sessionManager.Repository.LoadBranchAsync(sessionId, branchId, cancellationToken) == null)
            return AgentServiceResult<ContentDto>.NotFound;

        var branchSpace = await GetBranchSpaceAsync(sessionId, branchId, cancellationToken).ConfigureAwait(false);
        if (branchSpace is null)
            return AgentServiceResult<ContentDto>.NotFound;

        var attachment = await _workspace.WriteContentAsync(
            WorkspacePrincipalRef.System,
            branchSpace.Id,
            existingAttachmentId: null,
            content,
            new WriteWorkspaceSpaceContentRequest
            {
                ContentType = contentType ?? "application/octet-stream",
                Role = WorkspaceContentRoles.Upload,
                Name = fileName,
                PathHint = WorkspaceContentPaths.BranchUploads(sessionId, branchId),
                Permission = WorkspacePermissions.ReadWrite,
                ContentMetadata = new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["origin"] = ContentSource.User.ToString(),
                    ["session_id"] = sessionId,
                    ["branch_id"] = branchId
                }
            },
            cancellationToken).ConfigureAwait(false);

        var stored = await _workspace.StatContentAsync(
            WorkspacePrincipalRef.System,
            attachment.ContentId,
            attachment.ContentVersion,
            cancellationToken).ConfigureAwait(false);
        if (stored is null)
            return AgentServiceResult<ContentDto>.NotFound;

        var dto = new ContentDto(
            stored.Id,
            stored.Version,
            stored.ContentType,
            stored.SizeBytes,
            stored.CreatedAt.ToString("O"));

        return AgentServiceResult<ContentDto>.Success(dto);
    }

    public async Task<AgentServiceResult<IReadOnlyList<ContentDto>>> ListContentAsync(
        string sessionId,
        string branchId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(branchId);

        if (await _sessionManager.Repository.LoadSessionAsync(sessionId, cancellationToken) == null)
            return AgentServiceResult<IReadOnlyList<ContentDto>>.NotFound;

        if (await _sessionManager.Repository.LoadBranchAsync(sessionId, branchId, cancellationToken) == null)
            return AgentServiceResult<IReadOnlyList<ContentDto>>.NotFound;

        var branchSpace = await GetBranchSpaceAsync(sessionId, branchId, cancellationToken).ConfigureAwait(false);
        if (branchSpace is null)
            return AgentServiceResult<IReadOnlyList<ContentDto>>.NotFound;

        var attachments = await _workspace.ListContentAsync(
            WorkspacePrincipalRef.System,
            branchSpace.Id,
            new WorkspaceContentAttachmentQuery { Role = WorkspaceContentRoles.Upload },
            cancellationToken).ConfigureAwait(false);

        var dtos = new List<ContentDto>();
        foreach (var attachment in attachments)
        {
            var content = await _workspace.StatContentAsync(
                WorkspacePrincipalRef.System,
                attachment.ContentId,
                attachment.ContentVersion,
                cancellationToken).ConfigureAwait(false);
            if (content is not null)
                dtos.Add(ToDto(content));
        }

        return AgentServiceResult<IReadOnlyList<ContentDto>>.Success(dtos);
    }

    public async Task<AgentServiceResult<AgentContentDownload>> DownloadContentAsync(
        string sessionId,
        string branchId,
        string contentId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(branchId);
        ArgumentException.ThrowIfNullOrWhiteSpace(contentId);

        if (await _sessionManager.Repository.LoadSessionAsync(sessionId, cancellationToken) == null)
            return AgentServiceResult<AgentContentDownload>.NotFound;

        if (await _sessionManager.Repository.LoadBranchAsync(sessionId, branchId, cancellationToken) == null)
            return AgentServiceResult<AgentContentDownload>.NotFound;

        var attachment = await FindUploadAttachmentAsync(sessionId, branchId, contentId, cancellationToken).ConfigureAwait(false);
        if (attachment is null)
            return AgentServiceResult<AgentContentDownload>.NotFound;

        var info = await _workspace.StatContentAsync(
            WorkspacePrincipalRef.System,
            attachment.ContentId,
            attachment.ContentVersion,
            cancellationToken).ConfigureAwait(false);
        if (info is null)
            return AgentServiceResult<AgentContentDownload>.NotFound;

        await using var stream = await _workspace.OpenContentAsync(
            WorkspacePrincipalRef.System,
            attachment.ContentId,
            attachment.ContentVersion,
            cancellationToken).ConfigureAwait(false);
        if (stream is null)
            return AgentServiceResult<AgentContentDownload>.NotFound;

        using var memory = new MemoryStream();
        await stream.CopyToAsync(memory, cancellationToken).ConfigureAwait(false);

        return AgentServiceResult<AgentContentDownload>.Success(
            new AgentContentDownload(memory.ToArray(), info.ContentType, attachment.Name));
    }

    public async Task<AgentServiceResult> DeleteContentAsync(
        string sessionId,
        string branchId,
        string contentId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(branchId);
        ArgumentException.ThrowIfNullOrWhiteSpace(contentId);

        if (await _sessionManager.Repository.LoadSessionAsync(sessionId, cancellationToken) == null)
            return AgentServiceResult.NotFound;

        if (await _sessionManager.Repository.LoadBranchAsync(sessionId, branchId, cancellationToken) == null)
            return AgentServiceResult.NotFound;

        var branchSpace = await GetBranchSpaceAsync(sessionId, branchId, cancellationToken).ConfigureAwait(false);
        if (branchSpace is null)
            return AgentServiceResult.NotFound;

        var attachment = await FindUploadAttachmentAsync(sessionId, branchId, contentId, cancellationToken).ConfigureAwait(false);
        if (attachment is null)
            return AgentServiceResult.NotFound;

        await _workspace.DetachContentAsync(
            WorkspacePrincipalRef.System,
            branchSpace.Id,
            attachment.Id,
            attachment.Version,
            cancellationToken).ConfigureAwait(false);
        return AgentServiceResult.Success;
    }

    private async Task<WorkspaceSpaceInfo?> GetSessionSpaceAsync(
        string sessionId,
        CancellationToken cancellationToken)
    {
        return await _workspace.FindSpaceAsync(
            WorkspacePrincipalRef.System,
            new WorkspaceSpaceQuery
            {
                Kind = WorkspaceSessionRepository.SessionKind,
                ExternalId = sessionId
            },
            cancellationToken).ConfigureAwait(false);
    }

    private async Task<WorkspaceSpaceInfo?> GetBranchSpaceAsync(
        string sessionId,
        string branchId,
        CancellationToken cancellationToken)
    {
        var sessionSpace = await GetSessionSpaceAsync(sessionId, cancellationToken).ConfigureAwait(false);
        if (sessionSpace is null)
            return null;

        return await _workspace.FindSpaceAsync(
            WorkspacePrincipalRef.System,
            new WorkspaceSpaceQuery
            {
                Kind = WorkspaceSessionRepository.BranchKind,
                ExternalId = branchId,
                ParentSpaceId = sessionSpace.Id
            },
            cancellationToken).ConfigureAwait(false);
    }

    private async Task<WorkspaceContentAttachmentInfo?> FindUploadAttachmentAsync(
        string sessionId,
        string branchId,
        string contentId,
        CancellationToken cancellationToken)
    {
        var branchSpace = await GetBranchSpaceAsync(sessionId, branchId, cancellationToken).ConfigureAwait(false);
        if (branchSpace is null)
            return null;

        var attachments = await _workspace.ListContentAsync(
            WorkspacePrincipalRef.System,
            branchSpace.Id,
            new WorkspaceContentAttachmentQuery { Role = WorkspaceContentRoles.Upload },
            cancellationToken).ConfigureAwait(false);

        return attachments.FirstOrDefault(attachment => attachment.ContentId == contentId);
    }

    private static ContentDto ToDto(WorkspaceContentInfo content) =>
        new(
            content.Id,
            content.Version,
            content.ContentType,
            content.SizeBytes,
            content.CreatedAt.ToString("O"));
}
