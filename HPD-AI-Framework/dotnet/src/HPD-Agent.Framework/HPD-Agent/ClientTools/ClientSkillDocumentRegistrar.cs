// Copyright 2026 Einstein Essibu
// SPDX-License-Identifier: AGPL-3.0-only

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace HPD.Agent.ClientTools;

/// <summary>
/// Registers Client skill documents into the V3 content store under the /skills folder.
/// Uses the "client" origin tag to distinguish client-uploaded docs from compile-time skill docs.
/// </summary>
/// <remarks>
/// <para><b>Document ID Namespace:</b></para>
/// <para>
/// Client documents are stored with a "client:" prefix in the document ID to prevent
/// collision with compile-time skill documents. For example:
/// - ClientSkillDocument with DocumentId="checkout-flow"
/// - Stored as "client:checkout-flow" in the /skills folder
/// - Agent retrieves via content_read("/skills/client:checkout-flow")
/// </para>
///
/// <para><b>Usage:</b></para>
/// <code>
/// var registrar = new ClientSkillDocumentRegistrar(contentStore, logger);
///
/// // Register all documents from a toolkit
/// await registrar.RegisterToolkitDocumentsAsync(ecommerceToolkit, ct);
///
/// // Later, when toolkit is removed
/// await registrar.UnregisterToolkitDocumentsAsync(ecommerceToolkit, ct);
/// </code>
/// </remarks>
public class ClientSkillDocumentRegistrar
{
    private readonly IContentStore _contentStore;
    private readonly ILogger _logger;

    /// <summary>
    /// Prefix applied to client document IDs to prevent collision with compile-time skill docs.
    /// </summary>
    public const string ClientDocumentPrefix = "client:";

    /// <summary>
    /// Creates a new registrar backed by the V3 content store.
    /// </summary>
    public ClientSkillDocumentRegistrar(
        IContentStore contentStore,
        ILogger? logger = null)
    {
        _contentStore = contentStore ?? throw new ArgumentNullException(nameof(contentStore));
        _logger = logger ?? NullLogger.Instance;
    }

    /// <summary>
    /// Registers all documents from all skills in a toolkit.
    /// Uses named upsert semantics — idempotent, safe to call on reconnect.
    /// </summary>
    public async Task<int> RegisterToolkitDocumentsAsync(
        clientToolKitDefinition toolkit,
        CancellationToken ct = default)
    {
        if (toolkit.Skills == null || toolkit.Skills.Count == 0)
        {
            _logger.LogDebug("Toolkit '{ToolkitName}' has no skills, skipping document registration", toolkit.Name);
            return 0;
        }

        var registeredCount = 0;

        foreach (var skill in toolkit.Skills)
        {
            if (skill.Documents == null || skill.Documents.Count == 0)
                continue;

            foreach (var document in skill.Documents)
            {
                try
                {
                    await RegisterDocumentAsync(toolkit.Name, skill.Name, document, ct);
                    registeredCount++;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex,
                        "Failed to register document '{DocumentId}' from skill '{SkillName}' in toolkit '{ToolkitName}'",
                        document.DocumentId, skill.Name, toolkit.Name);
                    throw;
                }
            }
        }

        _logger.LogInformation(
            "Registered {Count} documents from toolkit '{ToolkitName}'",
            registeredCount, toolkit.Name);

        return registeredCount;
    }

    /// <summary>
    /// Unregisters all documents from all skills in a toolkit.
    /// </summary>
    public async Task<int> UnregisterToolkitDocumentsAsync(
        clientToolKitDefinition toolkit,
        CancellationToken ct = default)
    {
        if (toolkit.Skills == null || toolkit.Skills.Count == 0)
            return 0;

        var unregisteredCount = 0;

        foreach (var skill in toolkit.Skills)
        {
            if (skill.Documents == null || skill.Documents.Count == 0)
                continue;

            foreach (var document in skill.Documents)
            {
                try
                {
                    var storeId = GetStoreDocumentId(document.DocumentId);
                    // Scope=null for global /skills folder (consistent with UploadSkillDocumentAsync)
                    var existing = await _contentStore.QueryAsync(null, new ContentQuery { Name = storeId }, ct);
                    if (existing.Count > 0)
                    {
                        await _contentStore.DeleteAsync(null, existing[0].Id, ct);
                        unregisteredCount++;
                    }
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
            "Unregistered {Count} documents from toolkit '{ToolkitName}'",
            unregisteredCount, toolkit.Name);

        return unregisteredCount;
    }

    /// <summary>
    /// Registers a single document into the /skills folder of the content store.
    /// </summary>
    private async Task RegisterDocumentAsync(
        string toolName,
        string skillName,
        ClientSkillDocument document,
        CancellationToken ct)
    {
        var storeId = GetStoreDocumentId(document.DocumentId);
        var content = await GetDocumentContentAsync(document, ct);

        await _contentStore.UploadSkillDocumentAsync(
            documentId: storeId,
            content: content,
            description: document.Description,
            cancellationToken: ct);

        _logger.LogDebug(
            "Registered client document '{StoreId}' from skill '{SkillName}' in toolkit '{ToolkitName}'",
            storeId, skillName, toolName);
    }

    /// <summary>
    /// Gets document content from either inline content or URL.
    /// </summary>
    private async Task<string> GetDocumentContentAsync(
        ClientSkillDocument document,
        CancellationToken ct)
    {
        if (!string.IsNullOrEmpty(document.Content))
            return document.Content;

        if (!string.IsNullOrEmpty(document.Url))
            return await FetchDocumentFromUrlAsync(document.Url, document.DocumentId, ct);

        throw new ArgumentException($"Document '{document.DocumentId}' has neither content nor URL");
    }

    /// <summary>
    /// Fetches document content from a URL.
    /// </summary>
    private async Task<string> FetchDocumentFromUrlAsync(
        string url,
        string documentId,
        CancellationToken ct)
    {
        using var httpClient = new HttpClient();
        try
        {
            _logger.LogDebug("Fetching document '{DocumentId}' from URL: {Url}", documentId, url);
            var response = await httpClient.GetAsync(url, ct);
            response.EnsureSuccessStatusCode();
            return await response.Content.ReadAsStringAsync(ct);
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException(
                $"Failed to fetch document '{documentId}' from URL '{url}': {ex.Message}", ex);
        }
    }

    /// <summary>
    /// Returns the store document ID for a client document (prefixed).
    /// </summary>
    public static string GetStoreDocumentId(string documentId)
        => $"{ClientDocumentPrefix}{documentId}";

    /// <summary>
    /// Strips the client prefix from a store document ID. Returns null if not a client document.
    /// </summary>
    public static string? GetClientDocumentId(string storeId)
        => storeId.StartsWith(ClientDocumentPrefix) ? storeId[ClientDocumentPrefix.Length..] : null;

    /// <summary>
    /// Returns true if the store document ID belongs to a client-uploaded document.
    /// </summary>
    public static bool IsClientDocument(string storeId)
        => storeId.StartsWith(ClientDocumentPrefix);
}
