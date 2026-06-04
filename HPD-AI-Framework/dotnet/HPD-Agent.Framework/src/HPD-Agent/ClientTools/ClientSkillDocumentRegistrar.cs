// Copyright 2026 Einstein Essibu
// SPDX-License-Identifier: AGPL-3.0-only

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace HPD.Agent.ClientTools;

/// <summary>
/// Registers client skill documents through the workspace with role=skill under the agent skill space.
/// Uses the "client" origin tag to distinguish client-uploaded docs from compile-time skill docs.
/// </summary>
public class ClientSkillDocumentRegistrar
{
    private readonly IWorkspaceStore _workspace;
    private readonly string _agentName;
    private readonly ILogger _logger;

    /// <summary>
    /// Prefix applied to client document IDs to prevent collision with compile-time skill docs.
    /// </summary>
    public const string ClientDocumentPrefix = "client:";

    /// <summary>
    /// Creates a new registrar backed by the workspace store.
    /// </summary>
    public ClientSkillDocumentRegistrar(
        IWorkspaceStore workspace,
        string agentName,
        ILogger? logger = null)
    {
        _workspace = workspace ?? throw new ArgumentNullException(nameof(workspace));
        ArgumentException.ThrowIfNullOrWhiteSpace(agentName);
        _agentName = agentName;
        _logger = logger ?? NullLogger.Instance;
    }

    /// <summary>
    /// Registers all documents from all skills in a toolharness.
    /// Uses versioned write semantics; idempotent and safe to call on reconnect.
    /// </summary>
    public async Task<int> RegisterToolHarnessDocumentsAsync(
        clientToolHarnessDefinition toolharness,
        CancellationToken ct = default)
    {
        if (toolharness.Skills == null || toolharness.Skills.Count == 0)
        {
            _logger.LogDebug("ToolHarness '{ToolHarnessName}' has no skills, skipping document registration", toolharness.Name);
            return 0;
        }

        var registeredCount = 0;

        foreach (var skill in toolharness.Skills)
        {
            if (skill.Documents == null || skill.Documents.Count == 0)
                continue;

            foreach (var document in skill.Documents)
            {
                try
                {
                    await RegisterDocumentAsync(toolharness.Name, skill.Name, document, ct).ConfigureAwait(false);
                    registeredCount++;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex,
                        "Failed to register document '{DocumentId}' from skill '{SkillName}' in toolharness '{ToolHarnessName}'",
                        document.DocumentId, skill.Name, toolharness.Name);
                    throw;
                }
            }
        }

        _logger.LogInformation(
            "Registered {Count} documents from toolharness '{ToolHarnessName}'",
            registeredCount, toolharness.Name);

        return registeredCount;
    }

    /// <summary>
    /// Unregisters all documents from all skills in a toolharness.
    /// </summary>
    public async Task<int> UnregisterToolHarnessDocumentsAsync(
        clientToolHarnessDefinition toolharness,
        CancellationToken ct = default)
    {
        if (toolharness.Skills == null || toolharness.Skills.Count == 0)
            return 0;

        var agentSpace = await ResolveAgentSpaceAsync(ct).ConfigureAwait(false);
        if (agentSpace is null)
            return 0;

        var unregisteredCount = 0;

        foreach (var skill in toolharness.Skills)
        {
            if (skill.Documents == null || skill.Documents.Count == 0)
                continue;

            foreach (var document in skill.Documents)
            {
                try
                {
                    var storeId = GetStoreDocumentId(document.DocumentId);
                    var attachments = await _workspace.ListContentAsync(
                        WorkspacePrincipalRef.System,
                        agentSpace.Id,
                        new WorkspaceContentAttachmentQuery
                        {
                            Name = storeId,
                            Role = WorkspaceContentRoles.Skill
                        },
                        ct).ConfigureAwait(false);
                    var attachment = attachments.FirstOrDefault(candidate =>
                        string.Equals(candidate.PathHint, WorkspaceContentPaths.AgentSkills(_agentName), StringComparison.Ordinal));
                    if (attachment is null)
                        continue;

                    await _workspace.DetachContentAsync(
                        WorkspacePrincipalRef.System,
                        agentSpace.Id,
                        attachment.Id,
                        attachment.Version,
                        ct).ConfigureAwait(false);
                    unregisteredCount++;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex,
                        "Failed to unregister document '{DocumentId}' from skill '{SkillName}'",
                        document.DocumentId, skill.Name);
                }
            }
        }

        _logger.LogInformation(
            "Unregistered {Count} documents from toolharness '{ToolHarnessName}'",
            unregisteredCount, toolharness.Name);

        return unregisteredCount;
    }

    private async Task RegisterDocumentAsync(
        string toolName,
        string skillName,
        ClientSkillDocument document,
        CancellationToken ct)
    {
        var storeId = GetStoreDocumentId(document.DocumentId);
        var content = await GetDocumentContentAsync(document, ct).ConfigureAwait(false);

        await _workspace.UploadSkillDocumentAsync(
            agentName: _agentName,
            documentId: storeId,
            content: content,
            description: document.Description,
            cancellationToken: ct).ConfigureAwait(false);

        _logger.LogDebug(
            "Registered client document '{StoreId}' from skill '{SkillName}' in toolharness '{ToolHarnessName}'",
            storeId, skillName, toolName);
    }

    private async Task<string> GetDocumentContentAsync(
        ClientSkillDocument document,
        CancellationToken ct)
    {
        if (!string.IsNullOrEmpty(document.Content))
            return document.Content;

        if (!string.IsNullOrEmpty(document.Url))
            return await FetchDocumentFromUrlAsync(document.Url, document.DocumentId, ct).ConfigureAwait(false);

        throw new ArgumentException($"Document '{document.DocumentId}' has neither content nor URL");
    }

    private async Task<string> FetchDocumentFromUrlAsync(
        string url,
        string documentId,
        CancellationToken ct)
    {
        using var httpClient = new HttpClient();
        try
        {
            _logger.LogDebug("Fetching document '{DocumentId}' from URL: {Url}", documentId, url);
            var response = await httpClient.GetAsync(url, ct).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
            return await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException(
                $"Failed to fetch document '{documentId}' from URL '{url}': {ex.Message}", ex);
        }
    }

    public static string GetStoreDocumentId(string documentId)
        => $"{ClientDocumentPrefix}{documentId}";

    public static string? GetClientDocumentId(string storeId)
        => storeId.StartsWith(ClientDocumentPrefix, StringComparison.Ordinal)
            ? storeId[ClientDocumentPrefix.Length..]
            : null;

    public static bool IsClientDocument(string storeId)
        => storeId.StartsWith(ClientDocumentPrefix, StringComparison.Ordinal);

    private async Task<WorkspaceSpaceInfo?> ResolveAgentSpaceAsync(CancellationToken cancellationToken) =>
        await _workspace.FindSpaceAsync(
            WorkspacePrincipalRef.System,
            new WorkspaceSpaceQuery
            {
                Kind = WorkspaceAgentRepository.AgentKind,
                ExternalId = _agentName
            },
            cancellationToken).ConfigureAwait(false);
}
