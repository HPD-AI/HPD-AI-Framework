using System.Text.Json;
using HPD.Agent.ClientTools;
using HPD.Agent.Hosting.Data;
using HPD.Agent.Hosting.Extensions;
using HPD.Agent.Middleware;
using Microsoft.Extensions.AI;

namespace HPD.Agent.Maui;

/// <summary>
/// Base class for MAUI HybridWebView agent proxy.
/// Provides all methods that JavaScript can call via InvokeDotNet().
///
/// All public methods are callable from JavaScript without attributes.
/// Methods return JSON strings for complex objects (DTOs).
/// </summary>
public abstract class HybridWebViewAgentProxy
{
    protected readonly MauiSessionManager Manager;
    protected readonly MauiAgentManager AgentManager;
    protected readonly IHybridWebView HybridWebView;
    protected readonly EventStreamManager EventStreamManager;
    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, CancellationTokenSource> _activeStreams = new();

    protected HybridWebViewAgentProxy(
        MauiSessionManager manager,
        MauiAgentManager agentManager,
        IHybridWebView hybridWebView)
    {
        Manager = manager;
        AgentManager = agentManager;
        HybridWebView = hybridWebView;
        EventStreamManager = new EventStreamManager(hybridWebView);
    }

    // ============================================================
    // STREAMING
    // ============================================================

    /// <summary>
    /// Start streaming agent responses. Returns stream ID immediately.
    /// Events sent via HybridWebViewMessageReceived listener.
    /// Call StopStream(streamId) to cancel a running stream.
    /// </summary>
    public async Task<string> StartStream(
        string message,
        string sessionId,
        string? branchId = null,
        string? runConfigJson = null)
    {
        var streamId = Guid.NewGuid().ToString();

        if (string.IsNullOrWhiteSpace(message))
            throw new ArgumentException("Message cannot be empty", nameof(message));

        var cts = new CancellationTokenSource();
        _activeStreams[streamId] = cts;

        // Fire and forget - events sent via SendRawMessage
        _ = Task.Run(async () =>
        {
            string? lockedBranchId = null;
            try
            {
                var agent = await AgentManager.GetOrBuildAgentAsync("default");
                var session = await Manager.Store.LoadSessionAsync(sessionId);
                if (session == null)
                {
                    EventStreamManager.SendError(streamId, "Session not found");
                    return;
                }

                var branch = branchId != null
                    ? await Manager.Store.LoadBranchAsync(sessionId, branchId)
                    : await Manager.Store.LoadBranchAsync(sessionId, "main");

                if (branch == null)
                {
                    EventStreamManager.SendError(streamId, "Branch not found");
                    return;
                }

                if (!Manager.TryAcquireStreamLock(sessionId, branch.Id))
                {
                    EventStreamManager.SendError(streamId, "Branch is already streaming");
                    return;
                }

                lockedBranchId = branch.Id;

                try
                {
                    var runConfig = runConfigJson != null
                        ? JsonSerializer.Deserialize<StreamRunConfigDto>(runConfigJson)?.ToAgentRunConfig()
                        : null;

                    await foreach (var evt in agent.RunAsync(
                        message,
                        sessionId,
                        branch.Id,
                        options: runConfig,
                        cancellationToken: cts.Token))
                    {
                        if (cts.Token.IsCancellationRequested) break;
                        EventStreamManager.SendEvent(streamId, evt);
                    }

                    EventStreamManager.SendComplete(streamId);
                }
                catch (OperationCanceledException)
                {
                    EventStreamManager.SendError(streamId, "Stream cancelled");
                }
                finally
                {
                    Manager.ReleaseStreamLock(sessionId, lockedBranchId);
                }
            }
            catch (Exception ex)
            {
                EventStreamManager.SendError(streamId, ex.Message);
            }
            finally
            {
                _activeStreams.TryRemove(streamId, out var s);
                s?.Dispose();
            }
        });

        return streamId;
    }

    /// <summary>
    /// Cancel a running stream by its stream ID.
    /// </summary>
    public void StopStream(string streamId)
    {
        if (_activeStreams.TryGetValue(streamId, out var cts))
            cts.Cancel();
    }

    // ============================================================
    // SESSION CRUD (uses shared DTOs from HPD.Agent.Hosting.Data)
    // ============================================================

    public async Task<string> CreateSession(string? sessionId = null, string? metadataJson = null)
    {
        // Create temporary session ID if not provided
        var tempSessionId = sessionId ?? Guid.NewGuid().ToString();

        Dictionary<string, object>? metadata = null;
        if (metadataJson != null)
            metadata = JsonSerializer.Deserialize<Dictionary<string, object>>(metadataJson);

        await Manager.CreateSessionAsync(tempSessionId, metadata);

        var session = await Manager.Store.LoadSessionAsync(tempSessionId)
            ?? throw new InvalidOperationException($"Session '{tempSessionId}' not found after creation.");

        return JsonSerializer.Serialize(session.ToDto());  // Extension method from Hosting
    }

    public async Task<string> GetSession(string sessionId)
    {
        var session = await Manager.Store.LoadSessionAsync(sessionId);
        if (session == null)
            throw new InvalidOperationException($"Session '{sessionId}' not found");

        return JsonSerializer.Serialize(session.ToDto());
    }

    public async Task<string> UpdateSession(string sessionId, string updateRequestJson)
    {
        var request = JsonSerializer.Deserialize<UpdateSessionRequest>(updateRequestJson);
        if (request == null)
            throw new ArgumentException("Invalid update request JSON", nameof(updateRequestJson));

        var session = await Manager.Store.LoadSessionAsync(sessionId);
        if (session == null)
            throw new InvalidOperationException($"Session '{sessionId}' not found");

        session.Store = Manager.Store;

        // Merge semantics: update or add provided keys, remove keys set to null
        if (request.Metadata != null)
        {
            foreach (var kvp in request.Metadata)
            {
                // Check for null or JsonElement with Null/Undefined value kind
                bool isNullValue = kvp.Value == null ||
                    (kvp.Value is JsonElement je && (
                        je.ValueKind == JsonValueKind.Null ||
                        je.ValueKind == JsonValueKind.Undefined));

                if (isNullValue)
                {
                    // Remove the key from metadata entirely
                    if (session.Metadata.ContainsKey(kvp.Key))
                    {
                        session.Metadata.Remove(kvp.Key);
                    }
                }
                else
                {
                    // Add or update the metadata value
                    session.Metadata[kvp.Key] = kvp.Value;
                }
            }
        }

        // Update LastActivity timestamp after metadata changes
        session.LastActivity = DateTime.UtcNow;

        await Manager.Store.SaveSessionAsync(session);

        // Return DTO - ensure no null values in metadata and always return a dictionary
        var cleanedMetadata = session.Metadata
            .Where(kvp => kvp.Value != null)
            .ToDictionary(kvp => kvp.Key, kvp => kvp.Value);

        var dto = new SessionDto(
            session.Id,
            session.CreatedAt,
            session.LastActivity,
            cleanedMetadata);

        return JsonSerializer.Serialize(dto);
    }

    public async Task DeleteSession(string sessionId)
    {
        var session = await Manager.Store.LoadSessionAsync(sessionId);
        if (session == null)
            throw new InvalidOperationException($"Session '{sessionId}' not found");

        // Delete the session (this will delete all branches and assets via ISessionStore)
        await Manager.Store.DeleteSessionAsync(sessionId);

        // Clean up in-memory stream/session locks
        Manager.RemoveSession(sessionId);
    }

    public async Task<string> SearchSessions(string? searchRequestJson = null)
    {
        var request = searchRequestJson != null
            ? JsonSerializer.Deserialize<SearchSessionsRequest>(searchRequestJson)
            : null;

        var sessionIds = await Manager.Store.ListSessionIdsAsync();
        var dtos = new List<SessionDto>();

        foreach (var sessionId in sessionIds)
        {
            var session = await Manager.Store.LoadSessionAsync(sessionId);
            if (session == null) continue;

            // Apply metadata filtering if provided
            if (request?.Metadata != null && request.Metadata.Count > 0)
            {
                var matchesFilter = true;
                foreach (var filter in request.Metadata)
                {
                    if (!session.Metadata.TryGetValue(filter.Key, out var value))
                    {
                        matchesFilter = false;
                        break;
                    }

                    // Compare values using string representation for robust comparison
                    var sessionValue = value?.ToString() ?? "";
                    var filterValue = filter.Value?.ToString() ?? "";
                    if (sessionValue != filterValue)
                    {
                        matchesFilter = false;
                        break;
                    }
                }

                if (!matchesFilter)
                    continue;
            }

            dtos.Add(new SessionDto(
                session.Id,
                session.CreatedAt,
                session.LastActivity,
                session.Metadata));
        }

        // Apply offset and limit
        var offset = request?.Offset ?? 0;
        var limit = request?.Limit ?? 50;

        var result = dtos
            .OrderByDescending(s => s.LastActivity)
            .Skip(offset)
            .Take(limit)
            .ToList();

        return JsonSerializer.Serialize(result);
    }

    // ============================================================
    // BRANCH CRUD
    // ============================================================

    public async Task<string> ListBranches(string sessionId)
    {
        var session = await Manager.Store.LoadSessionAsync(sessionId);
        if (session == null)
            throw new InvalidOperationException($"Session '{sessionId}' not found");

        var branchIds = await Manager.Store.ListBranchIdsAsync(sessionId);
        var dtos = new List<BranchDto>();

        foreach (var branchId in branchIds)
        {
            var branch = await Manager.Store.LoadBranchAsync(sessionId, branchId);
            if (branch != null)
            {
                dtos.Add(ToBranchDto(branch, sessionId));
            }
        }

        return JsonSerializer.Serialize(dtos);
    }

    public async Task<string> GetBranch(string sessionId, string branchId)
    {
        var branch = await Manager.Store.LoadBranchAsync(sessionId, branchId);
        if (branch == null)
            throw new InvalidOperationException($"Branch '{branchId}' not found in session '{sessionId}'");

        var dto = ToBranchDto(branch, sessionId);
        return JsonSerializer.Serialize(dto);
    }

    public async Task<string> CreateBranch(string sessionId, string createBranchRequestJson)
    {
        var request = JsonSerializer.Deserialize<CreateBranchRequest>(createBranchRequestJson);
        if (request == null)
            throw new ArgumentException("Invalid create branch request JSON", nameof(createBranchRequestJson));

        var session = await Manager.Store.LoadSessionAsync(sessionId);
        if (session == null)
            throw new InvalidOperationException($"Session '{sessionId}' not found");

        session.Store = Manager.Store;

        // Generate branch ID if not provided
        var branchId = string.IsNullOrWhiteSpace(request.BranchId)
            ? Guid.NewGuid().ToString()
            : request.BranchId;

        // Check if branch already exists (return error)
        var existingBranch = await Manager.Store.LoadBranchAsync(sessionId, branchId);
        if (existingBranch != null)
            throw new InvalidOperationException($"Branch '{branchId}' already exists in session '{sessionId}'");

        var agent = await AgentManager.GetOrBuildAgentAsync("default");
        await agent.ForkBranchAsync(sessionId, "main", branchId, 0);

        var branch = await Manager.Store.LoadBranchAsync(sessionId, branchId)
            ?? throw new InvalidOperationException($"Branch '{branchId}' not found after creation.");

        if (!string.IsNullOrEmpty(request.Name))
            branch.Name = request.Name;

        if (!string.IsNullOrEmpty(request.Description))
            branch.Description = request.Description;

        if (request.Tags != null && request.Tags.Count > 0)
            branch.Tags = request.Tags;

        await Manager.Store.SaveBranchAsync(sessionId, branch);

        var dto = ToBranchDto(branch, sessionId);
        return JsonSerializer.Serialize(dto);
    }

    public async Task<string> ForkBranch(string sessionId, string sourceBranchId, string forkBranchRequestJson)
    {
        var request = JsonSerializer.Deserialize<ForkBranchRequest>(forkBranchRequestJson);
        if (request == null)
            throw new ArgumentException("Invalid fork branch request JSON", nameof(forkBranchRequestJson));

        // V3: Use session-level lock for atomic sibling updates
        return await Manager.WithSessionLockAsync(sessionId, async () =>
        {
            var sessionExists = await Manager.Store.LoadSessionAsync(sessionId);
            if (sessionExists == null)
                throw new InvalidOperationException($"Session '{sessionId}' not found");

            var sourceBranchExists = await Manager.Store.LoadBranchAsync(sessionId, sourceBranchId);
            if (sourceBranchExists == null)
                throw new InvalidOperationException($"Source branch '{sourceBranchId}' not found");

            var newBranchId = string.IsNullOrWhiteSpace(request.NewBranchId)
                ? Guid.NewGuid().ToString()
                : request.NewBranchId;

            var existingBranch = await Manager.Store.LoadBranchAsync(sessionId, newBranchId);
            if (existingBranch != null)
                throw new InvalidOperationException($"Branch '{newBranchId}' already exists in session '{sessionId}'");

            var agent = await AgentManager.GetOrBuildAgentAsync("default");
            await agent.ForkBranchAsync(sessionId, sourceBranchId, newBranchId, request.FromMessageIndex);

            var forkedBranch = await Manager.Store.LoadBranchAsync(sessionId, newBranchId)
                ?? throw new InvalidOperationException($"Branch '{newBranchId}' not found after fork.");

            if (!string.IsNullOrEmpty(request.Name))
                forkedBranch.Name = request.Name;

            if (!string.IsNullOrEmpty(request.Description))
                forkedBranch.Description = request.Description;

            if (request.Tags != null && request.Tags.Count > 0)
                forkedBranch.Tags = request.Tags;

            await Manager.Store.SaveBranchAsync(sessionId, forkedBranch);

            var dto = ToBranchDto(forkedBranch, sessionId);
            return JsonSerializer.Serialize(dto);
        });
    }

    public async Task<string> UpdateBranch(string sessionId, string branchId, string updateBranchRequestJson)
    {
        var request = JsonSerializer.Deserialize<UpdateBranchRequest>(updateBranchRequestJson);
        if (request == null)
            throw new ArgumentException("Invalid update branch request JSON", nameof(updateBranchRequestJson));

        var branch = await Manager.Store.LoadBranchAsync(sessionId, branchId);
        if (branch == null)
            throw new InvalidOperationException($"Branch '{branchId}' not found in session '{sessionId}'");

        return await Manager.WithSessionLockAsync(sessionId, async () =>
        {
            if (request.Name != null) branch.Name = request.Name;
            if (request.Description != null) branch.Description = request.Description;
            if (request.Tags != null) branch.Tags = request.Tags;
            branch.LastActivity = DateTime.UtcNow;

            await Manager.Store.SaveBranchAsync(sessionId, branch);
            return JsonSerializer.Serialize(ToBranchDto(branch, sessionId));
        });
    }

    public async Task DeleteBranch(string sessionId, string branchId, bool recursive = false)
    {
        // 1. Protect "main" branch from deletion
        if (branchId == "main")
            throw new InvalidOperationException("Cannot delete the 'main' branch");

        // 2. Load the branch to delete
        var branch = await Manager.Store.LoadBranchAsync(sessionId, branchId);
        if (branch == null)
            throw new InvalidOperationException($"Branch '{branchId}' not found in session '{sessionId}'");

        // 3. V3: Guard children — reject unless recursive is explicitly requested and permitted
        if (branch.ChildBranches.Count > 0)
        {
            if (!recursive)
                throw new InvalidOperationException(
                    $"Cannot delete branch with {branch.ChildBranches.Count} child branches. " +
                    $"Use recursive=true to delete the entire subtree, or delete children first: " +
                    $"{string.Join(", ", branch.ChildBranches)}");

            if (!Manager.AllowRecursiveBranchDelete)
                throw new InvalidOperationException(
                    "Recursive branch deletion is not enabled. " +
                    "Set AllowRecursiveBranchDelete = true in HPDAgentConfig to enable it.");
        }

        // 4. Acquire and HOLD stream lock through the entire delete
        if (!Manager.TryAcquireStreamLock(sessionId, branchId))
            throw new InvalidOperationException(
                "Branch is actively streaming and cannot be deleted. Try again later.");

        // 5. V3: Perform atomic deletion with sibling reindexing (stream lock held throughout)
        try
        {
            await Manager.WithSessionLockAsync(sessionId, async () =>
            {
                // 5a. Recursively delete all descendants first (if requested)
                if (recursive)
                {
                    foreach (var childId in branch.ChildBranches.ToList())
                        await DeleteSubtreeAsync(sessionId, childId);
                }

                // 5b. Reindex siblings and remove from parent's ChildBranches
                await ReindexSiblingsAfterDeleteAsync(sessionId, branchId, branch);

                // 5c. Update session's LastActivity
                var session = await Manager.Store.LoadSessionAsync(sessionId);
                if (session != null)
                {
                    session.LastActivity = DateTime.UtcNow;
                    await Manager.Store.SaveSessionAsync(session);
                }

                // 5d. Delete the branch (after all updates complete)
                await Manager.Store.DeleteBranchAsync(sessionId, branchId);
            });
        }
        finally
        {
            Manager.ReleaseStreamLock(sessionId, branchId);
            Manager.RemoveBranchStreamLock(sessionId, branchId);
        }
    }

    /// <summary>
    /// Depth-first recursive delete of a branch subtree.
    /// Caller is responsible for holding the session lock.
    /// </summary>
    private async Task DeleteSubtreeAsync(string sessionId, string branchId)
    {
        var branch = await Manager.Store.LoadBranchAsync(sessionId, branchId);
        if (branch == null) return;

        foreach (var childId in branch.ChildBranches.ToList())
            await DeleteSubtreeAsync(sessionId, childId);

        await ReindexSiblingsAfterDeleteAsync(sessionId, branchId, branch);
        await Manager.Store.DeleteBranchAsync(sessionId, branchId);
    }

    /// <summary>
    /// Removes a branch from its parent's ChildBranches list and reindexes remaining siblings.
    /// Caller is responsible for holding the session lock.
    /// </summary>
    private async Task ReindexSiblingsAfterDeleteAsync(string sessionId, string branchId, Branch branch)
    {
        // Remove from parent's ChildBranches list
        if (branch.ForkedFrom != null)
        {
            var parent = await Manager.Store.LoadBranchAsync(sessionId, branch.ForkedFrom);
            if (parent != null && parent.ChildBranches.Contains(branchId))
            {
                parent.ChildBranches.Remove(branchId);
                parent.LastActivity = DateTime.UtcNow;
                await Manager.Store.SaveBranchAsync(sessionId, parent);
            }
        }

        // Load remaining siblings (same forkedFrom + same forkedAtMessageIndex)
        var allBranchIds = await Manager.Store.ListBranchIdsAsync(sessionId);
        var remainingSiblings = new List<Branch>();

        foreach (var bid in allBranchIds)
        {
            if (bid == branchId) continue;

            var sibling = await Manager.Store.LoadBranchAsync(sessionId, bid);
            if (sibling != null &&
                sibling.ForkedFrom == branch.ForkedFrom &&
                sibling.ForkedAtMessageIndex == branch.ForkedAtMessageIndex)
            {
                remainingSiblings.Add(sibling);
            }
        }

        remainingSiblings = remainingSiblings.OrderBy(b => b.SiblingIndex).ToList();

        for (int i = 0; i < remainingSiblings.Count; i++)
        {
            var sibling = remainingSiblings[i];
            sibling.SiblingIndex = i;
            sibling.TotalSiblings = remainingSiblings.Count;
            sibling.PreviousSiblingId = i > 0 ? remainingSiblings[i - 1].Id : null;
            sibling.NextSiblingId = i < remainingSiblings.Count - 1 ? remainingSiblings[i + 1].Id : null;
            sibling.LastActivity = DateTime.UtcNow;
            await Manager.Store.SaveBranchAsync(sessionId, sibling);
        }
    }

    public async Task<string> GetBranchMessages(string sessionId, string branchId)
    {
        var branch = await Manager.Store.LoadBranchAsync(sessionId, branchId);
        if (branch == null)
            throw new InvalidOperationException($"Branch '{branchId}' not found in session '{sessionId}'");

        var dtos = new List<MessageDto>();
        for (int i = 0; i < branch.Messages.Count; i++)
        {
            var msg = branch.Messages[i];
            var contents = msg.Contents
                .Where(c => c is not UsageContent)
                .ToList();
            dtos.Add(new MessageDto(
                msg.MessageId ?? $"msg-{i}",
                msg.Role.Value,
                contents,
                msg.AuthorName,
                msg.CreatedAt?.ToString("O") ?? DateTime.UtcNow.ToString("O")));
        }

        return JsonSerializer.Serialize(dtos);
    }

    /// <summary>
    /// V3: Get sibling branch metadata with full navigation info.
    /// Returns siblings sorted by SiblingIndex (deterministic ordering).
    /// </summary>
    public async Task<string> GetSiblingBranches(string sessionId, string branchId)
    {
        // Load target branch
        var targetBranch = await Manager.Store.LoadBranchAsync(sessionId, branchId);
        if (targetBranch == null)
            throw new InvalidOperationException($"Branch '{branchId}' not found in session '{sessionId}'");

        // Get all branches in session
        var branchIds = await Manager.Store.ListBranchIdsAsync(sessionId);
        var siblingDtos = new List<SiblingBranchDto>();

        // Filter siblings (same ForkedFrom + ForkedAtMessageIndex)
        foreach (var bid in branchIds)
        {
            var branch = await Manager.Store.LoadBranchAsync(sessionId, bid);
            if (branch == null) continue;

            // V3: CRITICAL - Check BOTH ForkedFrom AND ForkedAtMessageIndex
            bool isSibling = branch.ForkedFrom == targetBranch.ForkedFrom &&
                             branch.ForkedAtMessageIndex == targetBranch.ForkedAtMessageIndex;

            if (isSibling)
            {
                siblingDtos.Add(new SiblingBranchDto(
                    Id: branch.Id,
                    Name: branch.GetDisplayName(),
                    SiblingIndex: branch.SiblingIndex,
                    TotalSiblings: branch.TotalSiblings,
                    IsOriginal: branch.IsOriginal,
                    MessageCount: branch.MessageCount,
                    CreatedAt: branch.CreatedAt,
                    LastActivity: branch.LastActivity
                ));
            }
        }

        // Sort by SiblingIndex (should already be correct, but guarantee it)
        siblingDtos = siblingDtos
            .OrderBy(s => s.SiblingIndex)
            .ToList();

        return JsonSerializer.Serialize(siblingDtos);
    }

    // ============================================================
    // ASSET MANAGEMENT
    // ============================================================

    public async Task<string> UploadAsset(string sessionId, string base64Data, string contentType, string filename)
    {
        var session = await Manager.Store.LoadSessionAsync(sessionId);
        if (session == null)
            throw new InvalidOperationException($"Session '{sessionId}' not found");

        var contentStore = Manager.Store.GetContentStore(sessionId);
        if (contentStore == null)
            throw new InvalidOperationException("Content storage is not available for this session store");

        // Convert base64 to byte array
        var data = Convert.FromBase64String(base64Data);

        // Upload to content store with /uploads folder tag
        var assetId = await contentStore.PutAsync(
            scope: sessionId,
            data: data,
            contentType: contentType,
            metadata: new ContentMetadata
            {
                Name = filename,
                Origin = ContentSource.User,
                Tags = new Dictionary<string, string>
                {
                    ["folder"] = "/uploads",
                    ["session"] = sessionId
                }
            },
            cancellationToken: default);

        // Get metadata from the store to return DTO
        var content = await contentStore.GetAsync(sessionId, assetId);
        if (content == null)
            throw new InvalidOperationException("Asset was uploaded but could not be retrieved");

        var dto = new AssetDto(
            assetId,
            content.ContentType,
            content.Data.Length,
            content.Info.CreatedAt.ToString("O"));

        return JsonSerializer.Serialize(dto);
    }

    public async Task<string> ListAssets(string sessionId)
    {
        var session = await Manager.Store.LoadSessionAsync(sessionId);
        if (session == null)
            throw new InvalidOperationException($"Session '{sessionId}' not found");

        var contentStore = Manager.Store.GetContentStore(sessionId);
        if (contentStore == null)
        {
            return JsonSerializer.Serialize(new List<AssetDto>());
        }

        // Query /uploads folder for this session
        var assets = await contentStore.QueryAsync(
            scope: sessionId,
            query: new ContentQuery { Tags = new Dictionary<string, string> { ["folder"] = "/uploads" } },
            cancellationToken: default);
        var dtos = assets.Select(a => new AssetDto(
            a.Id,
            a.ContentType,
            a.SizeBytes,
            a.CreatedAt.ToString("O"))).ToList();

        return JsonSerializer.Serialize(dtos);
    }

    public async Task DeleteAsset(string sessionId, string assetId)
    {
        var session = await Manager.Store.LoadSessionAsync(sessionId);
        if (session == null)
            throw new InvalidOperationException($"Session '{sessionId}' not found");

        var contentStore = Manager.Store.GetContentStore(sessionId);
        if (contentStore == null)
            throw new InvalidOperationException("Content storage is not available for this session store");

        // Check if asset exists before deleting
        var content = await contentStore.GetAsync(sessionId, assetId);
        if (content == null)
            throw new InvalidOperationException($"Asset '{assetId}' not found");

        await contentStore.DeleteAsync(sessionId, assetId);
    }

    // ============================================================
    // MIDDLEWARE RESPONSES
    // ============================================================

    public void RespondToPermission(string permissionResponseJson)
    {
        var request = JsonSerializer.Deserialize<PermissionResponseRequest>(permissionResponseJson);
        if (request == null)
            throw new ArgumentException("Invalid permission response JSON", nameof(permissionResponseJson));

        // Get the running agent - must be actively streaming to respond
        var agent = AgentManager.GetAgent(request.AgentId ?? "default");
        if (agent == null)
            throw new InvalidOperationException("No running agent found for this session");

        // Convert string choice to PermissionChoice enum
        var choice = request.Choice?.ToLower() switch
        {
            "allow_always" => PermissionChoice.AlwaysAllow,
            "deny_always" => PermissionChoice.AlwaysDeny,
            _ => PermissionChoice.Ask
        };

        // Send response to waiting permission middleware
        agent.SendMiddlewareResponse(
            request.PermissionId,
            new PermissionResponseEvent(
                request.PermissionId,
                "PermissionMiddleware",
                request.Approved,
                request.Reason,
                choice));
    }

    public void RespondToClientTool(string clientToolResponseJson)
    {
        var request = JsonSerializer.Deserialize<ClientToolResponseRequest>(clientToolResponseJson);
        if (request == null)
            throw new ArgumentException("Invalid client tool response JSON", nameof(clientToolResponseJson));

        // Get the running agent - must be actively streaming to respond
        var agent = AgentManager.GetAgent(request.AgentId ?? "default");
        if (agent == null)
            throw new InvalidOperationException("No running agent found for this session");

        // Convert content to IToolResultContent list
        var content = request.Content?.Select<ClientToolContentDto, IToolResultContent>(c => c.Type switch
        {
            "text" => new HPD.Agent.ClientTools.TextContent(c.Text ?? ""),
            "binary" or "data" => new BinaryContent(
                c.MediaType ?? "application/octet-stream",
                Convert.ToBase64String(c.Data ?? Array.Empty<byte>()),
                null,  // url
                null,  // id
                null), // filename
            _ => new HPD.Agent.ClientTools.TextContent(c.Text ?? "")
        }).ToList() ?? new List<IToolResultContent>();

        // Send response to waiting ClientToolMiddleware
        agent.SendMiddlewareResponse(
            request.RequestId,
            new ClientToolInvokeResponseEvent(
                RequestId: request.RequestId,
                Content: content,
                Success: request.Success,
                ErrorMessage: request.ErrorMessage,
                Augmentation: null));
    }

    // ============================================================
    // HELPER METHODS
    // ============================================================

    private static BranchDto ToBranchDto(Branch branch, string sessionId)
    {
        return new BranchDto(
            branch.Id,
            sessionId,
            branch.GetDisplayName(),
            branch.Description,
            branch.ForkedFrom,
            branch.ForkedAtMessageIndex,
            branch.CreatedAt,
            branch.LastActivity,
            branch.Messages.Count,
            branch.Tags,
            branch.Ancestors,
            // V3: Tree navigation metadata
            branch.SiblingIndex,
            branch.TotalSiblings,
            branch.IsOriginal,
            branch.OriginalBranchId,
            branch.PreviousSiblingId,
            branch.NextSiblingId,
            branch.TotalForks);
    }
}
