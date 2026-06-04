using Microsoft.Extensions.AI;
using HPD.Agent.Providers;

namespace HPD.Agent.Middleware;

/// <summary>
/// Intelligently routes DataContent uploads between provider-native HostedFileClient
/// and workspace storage based on provider capabilities and UploadStrategy.
/// 
/// Transforms DataContent into either:
/// - HostedFileContent (provider-native) when provider supports it
/// - UriContent(hpd-content://id) (workspace storage) as fallback
/// </summary>
/// <remarks>
/// <para>
/// This middleware is automatically registered in AgentBuilder. It checks at runtime
/// if a workspace store or provider registry is available — zero cost when unused.
/// </para>
/// <para><b>Behavior:</b></para>
/// <list type="bullet">
/// <item>Scans messages for DataContent with binary data</item>
/// <item>Queries provider registry to detect HostedFileClient support</item>
/// <item>Routes based on UploadStrategy (Auto/Hosted/Local) and provider capability</item>
/// <item>Uploads via HostedFileClient or workspace storage accordingly</item>
/// <item>Emits corresponding upload events for observability</item>
/// </list>
/// <para><b>Smart Fallback:</b></para>
/// <para>
/// In Auto mode, if HostedFileClient upload fails, automatically falls back to workspace storage.
/// If both fail, keeps original DataContent (no-op).
/// </para>
/// </remarks>
public class ContentUploadMiddleware : IAgentMiddleware
{
    private readonly IProviderRegistry? _providerRegistry;
    private readonly IWorkspaceStore? _workspaceStore;

    /// <summary>
    /// Creates a ContentUploadMiddleware with optional provider registry and workspace store.
    /// </summary>
    /// <param name="providerRegistry">Optional provider registry for detecting HostedFileClient support</param>
    /// <param name="workspaceStore">Optional workspace store for framework-managed uploads</param>
    /// <remarks>
    /// If both are null, middleware is a no-op (zero cost).
    /// If only workspaceStore is provided, only local uploads are available.
    /// If only providerRegistry is provided, only hosted uploads are available.
    /// If both provided, middleware intelligently routes based on UploadStrategy.
    /// </remarks>
    public ContentUploadMiddleware(IProviderRegistry? providerRegistry = null, IWorkspaceStore? workspaceStore = null)
    {
        _providerRegistry = providerRegistry;
        _workspaceStore = workspaceStore;
    }

    public async Task BeforeMessageTurnAsync(
        BeforeMessageTurnContext context,
        CancellationToken cancellationToken)
    {
        var session = context.Session;
        if (session == null)
            return;

        var message = context.UserMessage;
        if (message == null)
            return;

        // Check if message contains DataContent with bytes
        var hasDataBytes = message.Contents.Any(c =>
            c is DataContent data && data.Data.Length > 0);

        if (!hasDataBytes)
            return;

        // Zero-cost exit when no upload path is configured for this agent/run.
        if (_providerRegistry == null
            && _workspaceStore == null
            && context.RunConfig.OverrideHostedFileClient == null
            && context.ClientSet?.HostedFiles == null)
        {
            return;
        }

        // Get upload strategy from run config if available
        var strategy = context.RunConfig?.UploadStrategy ?? UploadStrategy.Auto;

        // Upload content and build new content list with URIs or hosted references
        var newContents = new List<AIContent>();

        foreach (var content in message.Contents)
        {
            AIContent transformedContent = content;

            if (content is DataContent data && data.Data.Length > 0)
            {
                transformedContent = await RouteUploadAsync(
                    context,
                    data,
                    session,
                    strategy,
                    cancellationToken);
            }

            newContents.Add(transformedContent);
        }

        // Create new message with transformed contents
        var updatedMessage = new ChatMessage(message.Role, newContents)
        {
            AuthorName = message.AuthorName,
            AdditionalProperties = message.AdditionalProperties,
            CreatedAt = message.CreatedAt,
            MessageId = message.MessageId,
            RawRepresentation = message.RawRepresentation
        };

        context.UserMessage = updatedMessage;
    }

    private async Task<AIContent> RouteUploadAsync(
        BeforeMessageTurnContext context,
        DataContent data,
        Session session,
        UploadStrategy strategy,
        CancellationToken cancellationToken)
    {
        var mediaType = data.MediaType ?? "application/octet-stream";

        // Determine if the current agent/run has a hosted file client available.
        var hostedFileClient = GetHostedFileClient(context);
        var canUseHosted = hostedFileClient != null;
        var canUseLocal = _workspaceStore != null;

        var useHosted = strategy switch
        {
            UploadStrategy.Hosted => true,
            UploadStrategy.Local => false,
            UploadStrategy.Auto => canUseHosted,  // Prefer hosted if available
            _ => false
        };

        // Validate strategy can be satisfied
        if (strategy == UploadStrategy.Hosted && !canUseHosted)
        {
            context.Emit(new HostedFileUploadFailedEvent(
                Error: "UploadStrategy.Hosted requested but current provider does not support HostedFileClient"));
            return data;  // Keep original
        }

        if (strategy == UploadStrategy.Local && !canUseLocal)
        {
            context.Emit(new ContentUploadFailedEvent(
                Error: "UploadStrategy.Local requested but no workspace store configured"));
            return data;  // Keep original
        }

        // Try primary path
        if (useHosted && canUseHosted)
        {
            var result = await UploadToHostedAsync(
                context,
                data,
                hostedFileClient!,
                mediaType,
                session,
                cancellationToken);

            if (result != null)
                return result;

            // Hosted failed in Auto mode — try fallback
            if (strategy == UploadStrategy.Auto && canUseLocal)
            {
                return await UploadToLocalAsync(
                    context,
                    data,
                    session,
                    mediaType,
                    cancellationToken) ?? data;
            }

            // Hosted failed in Hosted mode — error already emitted
            return data;
        }

        // Use local path
        if (canUseLocal)
        {
            return await UploadToLocalAsync(
                context,
                data,
                session,
                mediaType,
                cancellationToken) ?? data;
        }

        // No upload path available
        return data;
    }

    private async Task<AIContent?> UploadToHostedAsync(
        BeforeMessageTurnContext context,
        DataContent data,
        IHostedFileClient hostedClient,
        string mediaType,
        Session session,
        CancellationToken cancellationToken)
    {
        try
        {
            using var stream = new MemoryStream(data.Data.ToArray());
            var fileName = data.Name ?? ExtractFileName(data) ?? $"upload_{Guid.NewGuid():N}";

            var uploadOptions = new HostedFileClientOptions
            {
                Purpose = "assistants"  // Standard OpenAI purpose
            };

            var hostedContent = await hostedClient.UploadAsync(
                stream,
                mediaType,
                fileName,
                uploadOptions,
                cancellationToken);

            context.Emit(new HostedFileUploadedEvent(
                FileId: hostedContent.FileId,
                MediaType: mediaType,
                SizeBytes: data.Data.Length));

            return hostedContent;
        }
        catch (Exception ex)
        {
            context.Emit(new HostedFileUploadFailedEvent(
                Error: $"Hosted upload failed: {ex.Message}"));
            return null;
        }
    }

    private async Task<AIContent?> UploadToLocalAsync(
        BeforeMessageTurnContext context,
        DataContent data,
        Session session,
        string mediaType,
        CancellationToken cancellationToken)
    {
        try
        {
            var branchId = context.Branch?.Id ?? context.BranchId ?? "main";
            var branchSpace = await EnsureBranchSpaceAsync(session, branchId, cancellationToken).ConfigureAwait(false);
            var fileName = ExtractFileName(data) ?? $"upload_{Guid.NewGuid():N}";
            await using var stream = new MemoryStream(data.Data.ToArray(), writable: false);
            var attachment = await _workspaceStore!.WriteContentAsync(
                WorkspacePrincipalRef.System,
                branchSpace.Id,
                existingAttachmentId: null,
                stream,
                new WriteWorkspaceSpaceContentRequest
                {
                    ContentType = mediaType,
                    Role = WorkspaceContentRoles.Upload,
                    Name = fileName,
                    PathHint = WorkspaceContentPaths.BranchUploads(session.Id, branchId),
                    Permission = WorkspacePermissions.ReadWrite,
                    ContentMetadata = new Dictionary<string, string>(StringComparer.Ordinal)
                    {
                        ["origin"] = ContentSource.User.ToString(),
                        ["session_id"] = session.Id,
                        ["branch_id"] = branchId
                    }
                },
                cancellationToken: cancellationToken).ConfigureAwait(false);

            var uriContent = new UriContent(
                ContentReferenceResolverMiddleware.CreateContentUri(attachment.ContentId),
                mediaType);

            context.Emit(new ContentUploadedEvent(
                ContentId: attachment.ContentId,
                MediaType: mediaType,
                SizeBytes: data.Data.Length));

            return uriContent;
        }
        catch (Exception ex)
        {
            context.Emit(new ContentUploadFailedEvent(
                Error: $"Local upload failed: {ex.Message}"));
            return null;
        }
    }

    private async Task<WorkspaceSpaceInfo> EnsureBranchSpaceAsync(
        Session session,
        string branchId,
        CancellationToken cancellationToken)
    {
        var sessionSpace = await EnsureSessionSpaceAsync(session, cancellationToken).ConfigureAwait(false);
        var existing = await _workspaceStore!.FindSpaceAsync(
            WorkspacePrincipalRef.System,
            new WorkspaceSpaceQuery
            {
                Kind = WorkspaceSessionRepository.BranchKind,
                ExternalId = branchId,
                ParentSpaceId = sessionSpace.Id
            },
            cancellationToken).ConfigureAwait(false);
        if (existing is not null)
            return existing;

        return await _workspaceStore.CreateChildSpaceAsync(
            WorkspacePrincipalRef.System,
            sessionSpace.Id,
            new CreateWorkspaceSpaceRequest
            {
                Kind = WorkspaceSessionRepository.BranchKind,
                ExternalId = branchId,
                Name = branchId,
                Slug = branchId
            },
            cancellationToken).ConfigureAwait(false);
    }

    private async Task<WorkspaceSpaceInfo> EnsureSessionSpaceAsync(
        Session session,
        CancellationToken cancellationToken)
    {
        var existing = await _workspaceStore!.FindSpaceAsync(
            WorkspacePrincipalRef.System,
            new WorkspaceSpaceQuery
            {
                Kind = WorkspaceSessionRepository.SessionKind,
                ExternalId = session.Id
            },
            cancellationToken).ConfigureAwait(false);
        if (existing is not null)
            return existing;

        return await _workspaceStore.CreateSpaceAsync(
            WorkspacePrincipalRef.System,
            new CreateWorkspaceSpaceRequest
            {
                Kind = WorkspaceSessionRepository.SessionKind,
                ExternalId = session.Id,
                Name = session.Id,
                Slug = session.Id
            },
            cancellationToken).ConfigureAwait(false);
    }

    private IHostedFileClient? GetHostedFileClient(BeforeMessageTurnContext context)
    {
        if (context.RunConfig.OverrideHostedFileClient is { } runClient)
            return runClient;

        if (context.ClientSet?.HostedFiles is { } buildClient)
            return buildClient;

        if (_providerRegistry == null)
            return null;

        // Try to get provider key from run config or agent config
        var providerKey = context.RunConfig?.ProviderKey
            ?? context.Config?.Clients?.Chat?.ProviderKey;

        if (string.IsNullOrWhiteSpace(providerKey))
            return null;

        try
        {
            var provider = _providerRegistry.GetProvider<IHostedFileClientProvider>(providerKey);
            var config = context.ClientSet?.GetResolvedConfig(ProviderClientFamily.HostedFiles)
                ?? context.Config?.Clients?.HostedFiles
                ?? context.Config?.Clients?.Chat
                ?? new ClientProviderConfig();

            return provider.CreateHostedFileClient(config, context.Services);
        }
        catch
        {
            // Provider exists but doesn't implement IHostedFileClientProvider
            return null;
        }
    }

    private static string? ExtractFileName(AIContent content)
    {
        if (content is DataContent { Name.Length: > 0 } data)
            return data.Name;

        if (content.AdditionalProperties != null &&
            content.AdditionalProperties.TryGetValue("filename", out var fn))
            return fn?.ToString();
        return null;
    }
}
