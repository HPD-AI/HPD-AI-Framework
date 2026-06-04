using System.Text.Json;

namespace HPD.Agent;

/// <summary>
/// Workspace-backed stored-agent facade. Agent definitions are documents attached to agent spaces.
/// </summary>
public sealed class WorkspaceAgentRepository : IAgentRepository
{
    public const string AgentKind = "agent";
    public const string AgentDefinitionRole = "agent_definition";

    private readonly IWorkspaceStore _workspace;
    private readonly WorkspacePrincipalRef _principal;

    public WorkspaceAgentRepository(
        IWorkspaceStore workspace,
        WorkspacePrincipalRef? principal = null)
    {
        _workspace = workspace ?? throw new ArgumentNullException(nameof(workspace));
        _principal = principal ?? WorkspacePrincipalRef.System;
    }

    /// <summary>The workspace substrate backing this repository.</summary>
    public IWorkspaceStore Workspace => _workspace;

    public async Task<StoredAgent?> LoadAsync(
        string agentId,
        CancellationToken cancellationToken = default)
    {
        var agentSpace = await FindAgentSpaceAsync(agentId, cancellationToken).ConfigureAwait(false);
        if (agentSpace is null)
            return null;

        var attachment = await GetLatestDefinitionAttachmentAsync(agentSpace.Id, cancellationToken)
            .ConfigureAwait(false);
        if (attachment is null)
            return null;

        await using var stream = await _workspace.OpenContentAsync(
            _principal,
            attachment.ContentId,
            attachment.ContentVersion,
            cancellationToken).ConfigureAwait(false);
        if (stream is null)
            return null;

        var agent = await JsonSerializer.DeserializeAsync(
            stream,
            HPDJsonContext.Default.StoredAgent,
            cancellationToken).ConfigureAwait(false);
        WorkspaceMetadataNormalizer.Normalize(agent?.Metadata);
        return agent;
    }

    public async Task SaveAsync(
        StoredAgent agent,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(agent);
        ArgumentException.ThrowIfNullOrWhiteSpace(agent.Id);

        var agentSpace = await GetOrCreateAgentSpaceAsync(agent, cancellationToken).ConfigureAwait(false);

        var existing = await _workspace.ListContentAsync(
            _principal,
            agentSpace.Id,
            new WorkspaceContentAttachmentQuery { Role = AgentDefinitionRole },
            cancellationToken).ConfigureAwait(false);
        foreach (var attachment in existing)
        {
            await _workspace.DetachContentAsync(
                _principal,
                agentSpace.Id,
                attachment.Id,
                attachment.Version,
                cancellationToken).ConfigureAwait(false);
        }

        using var buffer = new MemoryStream();
        await JsonSerializer.SerializeAsync(
            buffer,
            agent,
            HPDJsonContext.Default.StoredAgent,
            cancellationToken).ConfigureAwait(false);
        buffer.Position = 0;

        await _workspace.WriteContentAsync(
            _principal,
            agentSpace.Id,
            existingAttachmentId: null,
            buffer,
            new WriteWorkspaceSpaceContentRequest
            {
                ContentType = "application/json",
                Role = AgentDefinitionRole,
                Name = "definition.json",
                Permission = "read_write"
            },
            cancellationToken).ConfigureAwait(false);
    }

    public async Task DeleteAsync(
        string agentId,
        CancellationToken cancellationToken = default)
    {
        var agentSpace = await FindAgentSpaceAsync(agentId, cancellationToken).ConfigureAwait(false);
        if (agentSpace is null)
            return;

        await _workspace.DeleteSpaceAsync(
            _principal,
            agentSpace.Id,
            agentSpace.Version,
            recursive: true,
            cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<string>> ListIdsAsync(
        CancellationToken cancellationToken = default)
    {
        var spaces = await _workspace.ListSpacesAsync(
            _principal,
            new WorkspaceSpaceQuery { Kind = AgentKind },
            cancellationToken).ConfigureAwait(false);

        return spaces.Select(space => space.ExternalId).OrderBy(id => id, StringComparer.Ordinal).ToList();
    }

    private async Task<WorkspaceSpaceInfo> GetOrCreateAgentSpaceAsync(
        StoredAgent agent,
        CancellationToken cancellationToken)
    {
        var existing = await FindAgentSpaceAsync(agent.Id, cancellationToken).ConfigureAwait(false);
        if (existing is not null)
            return existing;

        return await _workspace.CreateSpaceAsync(
            _principal,
            new CreateWorkspaceSpaceRequest
            {
                Kind = AgentKind,
                ExternalId = agent.Id,
                Name = string.IsNullOrWhiteSpace(agent.Name) ? agent.Id : agent.Name
            },
            cancellationToken).ConfigureAwait(false);
    }

    private Task<WorkspaceSpaceInfo?> FindAgentSpaceAsync(
        string agentId,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(agentId);
        return _workspace.FindSpaceAsync(
            _principal,
            new WorkspaceSpaceQuery
            {
                Kind = AgentKind,
                ExternalId = agentId
            },
            cancellationToken);
    }

    private async Task<WorkspaceContentAttachmentInfo?> GetLatestDefinitionAttachmentAsync(
        string agentSpaceId,
        CancellationToken cancellationToken)
    {
        var definitions = await _workspace.ListContentAsync(
            _principal,
            agentSpaceId,
            new WorkspaceContentAttachmentQuery { Role = AgentDefinitionRole },
            cancellationToken).ConfigureAwait(false);

        return definitions.OrderBy(definition => definition.CreatedAt).LastOrDefault();
    }
}
