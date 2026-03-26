using HPD.Agent;
using HPD.Agent.AspNetCore.Lifecycle;
using HPD.Agent.Hosting.Data;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.AI;

namespace HPD.Agent.AspNetCore.EndpointMapping.Endpoints;

/// <summary>
/// Branch CRUD endpoints for the HPD-Agent API.
/// </summary>
internal static class BranchEndpoints
{
    /// <summary>
    /// Maps all branch-related endpoints.
    /// </summary>
    internal static void Map(
        IEndpointRouteBuilder endpoints,
        AspNetCoreSessionManager sessionManager,
        AspNetCoreAgentManager agentManager)
    {
        // GET /sessions/{sid}/branches - List branches
        endpoints.MapGet("/sessions/{sid}/branches", (string sid, CancellationToken ct) =>
                ListBranches(sid, sessionManager, ct))
            .WithName("ListBranches")
            .WithSummary("List all branches in a session");

        // GET /sessions/{sid}/branches/{bid} - Get branch metadata
        endpoints.MapGet("/sessions/{sid}/branches/{bid}", (string sid, string bid, CancellationToken ct) =>
                GetBranch(sid, bid, sessionManager, ct))
            .WithName("GetBranch")
            .WithSummary("Get branch metadata by ID");

        // POST /sessions/{sid}/branches - Create new branch
        endpoints.MapPost("/sessions/{sid}/branches", (string sid, CreateBranchRequest request, CancellationToken ct) =>
                CreateBranch(sid, request, sessionManager, agentManager, ct))
            .WithName("CreateBranch")
            .WithSummary("Create a new branch in a session");

        // POST /sessions/{sid}/branches/{bid}/fork - Fork at message index
        endpoints.MapPost("/sessions/{sid}/branches/{bid}/fork", (string sid, string bid, ForkBranchRequest request, CancellationToken ct) =>
                ForkBranch(sid, bid, request, sessionManager, agentManager, ct))
            .WithName("ForkBranch")
            .WithSummary("Fork an existing branch at a specific message index");

        // PATCH /sessions/{sid}/branches/{bid} - Update branch metadata
        endpoints.MapPatch("/sessions/{sid}/branches/{bid}", (string sid, string bid, UpdateBranchRequest request, CancellationToken ct) =>
                UpdateBranch(sid, bid, request, sessionManager, ct))
            .WithName("UpdateBranch")
            .WithSummary("Update branch name, description, or tags");

        // DELETE /sessions/{sid}/branches/{bid} - Delete branch
        // Optional query param: ?recursive=true to delete the entire subtree
        endpoints.MapDelete("/sessions/{sid}/branches/{bid}", (string sid, string bid, bool recursive = false, CancellationToken ct = default) =>
                DeleteBranch(sid, bid, recursive, sessionManager, ct))
            .WithName("DeleteBranch")
            .WithSummary("Delete a branch");

        // GET /sessions/{sid}/branches/{bid}/messages - Get branch messages
        endpoints.MapGet("/sessions/{sid}/branches/{bid}/messages", (string sid, string bid, CancellationToken ct) =>
                GetMessages(sid, bid, sessionManager, ct))
            .WithName("GetBranchMessages")
            .WithSummary("Get all messages in a branch");

        // GET /sessions/{sid}/branches/{bid}/siblings - Get sibling branch IDs
        endpoints.MapGet("/sessions/{sid}/branches/{bid}/siblings", (string sid, string bid, CancellationToken ct) =>
                GetSiblings(sid, bid, sessionManager, ct))
            .WithName("GetSiblingBranches")
            .WithSummary("Get sibling branch IDs (branches that share the same parent)");
    }

    private static async Task<Results<Ok<List<BranchDto>>, NotFound, ValidationProblem>> ListBranches(
        string sid,
        AspNetCoreSessionManager sessionManager,
        CancellationToken ct = default)
    {
        try
        {
            var session = await sessionManager.Store.LoadSessionAsync(sid, ct);
            if (session == null)
            {
                return TypedResults.NotFound();
            }

            var branchIds = await sessionManager.Store.ListBranchIdsAsync(sid, ct);
            var dtos = new List<BranchDto>();

            foreach (var branchId in branchIds)
            {
                var branch = await sessionManager.Store.LoadBranchAsync(sid, branchId, ct);
                if (branch != null)
                {
                    dtos.Add(ToBranchDto(branch, sid));
                }
            }

            return TypedResults.Ok(dtos);
        }
        catch (Exception ex)
        {
            return TypedResults.ValidationProblem(new Dictionary<string, string[]>
            {
                ["ListBranchesError"] = [ex.Message]
            });
        }
    }

    private static async Task<Results<Ok<BranchDto>, NotFound, ValidationProblem>> GetBranch(
        string sid,
        string bid,
        AspNetCoreSessionManager sessionManager,
        CancellationToken ct = default)
    {
        try
        {
            var branch = await sessionManager.Store.LoadBranchAsync(sid, bid, ct);
            if (branch == null)
            {
                return TypedResults.NotFound();
            }

            var dto = ToBranchDto(branch, sid);
            return TypedResults.Ok(dto);
        }
        catch (Exception ex)
        {
            return TypedResults.ValidationProblem(new Dictionary<string, string[]>
            {
                ["GetBranchError"] = [ex.Message]
            });
        }
    }

    private static async Task<Results<Created<BranchDto>, NotFound, Conflict, ValidationProblem>> CreateBranch(
        string sid,
        CreateBranchRequest request,
        AspNetCoreSessionManager sessionManager,
        AspNetCoreAgentManager agentManager,
        CancellationToken ct = default)
    {
        try
        {
            var sessionExists = await sessionManager.Store.LoadSessionAsync(sid, ct);
            if (sessionExists == null)
            {
                return TypedResults.NotFound();
            }

            // Generate branch ID if not provided
            var branchId = string.IsNullOrWhiteSpace(request.BranchId)
                ? Guid.NewGuid().ToString()
                : request.BranchId;

            // Check if branch already exists (return conflict)
            var existingBranch = await sessionManager.Store.LoadBranchAsync(sid, branchId, ct);
            if (existingBranch != null)
            {
                return TypedResults.Conflict();
            }

            // Use string-based ForkBranchAsync to create the new branch from message 0
            var agent = await agentManager.GetOrBuildAgentAsync(request.AgentId ?? "default", ct);
            await agent.ForkBranchAsync(sid, "main", branchId, 0, ct);

            var branch = await sessionManager.Store.LoadBranchAsync(sid, branchId, ct)
                ?? throw new InvalidOperationException($"Branch '{branchId}' not found after creation.");

            if (!string.IsNullOrEmpty(request.Name))
                branch.Name = request.Name;

            if (!string.IsNullOrEmpty(request.Description))
                branch.Description = request.Description;

            if (request.Tags != null && request.Tags.Count > 0)
                branch.Tags = request.Tags;

            await sessionManager.Store.SaveBranchAsync(sid, branch, ct);

            var dto = ToBranchDto(branch, sid);
            return TypedResults.Created($"/sessions/{sid}/branches/{branch.Id}", dto);
        }
        catch (Exception ex)
        {
            return TypedResults.ValidationProblem(new Dictionary<string, string[]>
            {
                ["CreateBranchError"] = [ex.Message]
            });
        }
    }

    private static async Task<Results<Created<BranchDto>, NotFound, ValidationProblem>> ForkBranch(
        string sid,
        string bid,
        ForkBranchRequest request,
        AspNetCoreSessionManager sessionManager,
        AspNetCoreAgentManager agentManager,
        CancellationToken ct = default)
    {
        try
        {
            //  Use session-level lock for atomic sibling updates
            return await sessionManager.WithSessionLockAsync(sid,
                () => DoForkBranchAsync(sid, bid, request, sessionManager, agentManager, ct),
                ct);
        }
        catch (Exception ex)
        {
            return TypedResults.ValidationProblem(new Dictionary<string, string[]>
            {
                ["ForkBranchError"] = [ex.Message]
            });
        }
    }

    private static async Task<Results<Ok<BranchDto>, NotFound, ValidationProblem>> UpdateBranch(
        string sid,
        string bid,
        UpdateBranchRequest request,
        AspNetCoreSessionManager sessionManager,
        CancellationToken ct = default)
    {
        try
        {
            var branch = await sessionManager.Store.LoadBranchAsync(sid, bid, ct);
            if (branch == null)
            {
                return TypedResults.NotFound();
            }

            return await sessionManager.WithSessionLockAsync(sid,
                () => DoUpdateBranchAsync(sid, branch, request, sessionManager, ct),
                ct);
        }
        catch (Exception ex)
        {
            return TypedResults.ValidationProblem(new Dictionary<string, string[]>
            {
                ["UpdateBranchError"] = [ex.Message]
            });
        }
    }

    private static async Task<Results<NoContent, NotFound, Conflict, ValidationProblem>> DeleteBranch(
        string sid,
        string bid,
        bool recursive,
        AspNetCoreSessionManager sessionManager,
        CancellationToken ct = default)
    {
        // 1. Protect "main" branch from deletion
        if (bid == "main")
        {
            return TypedResults.ValidationProblem(new Dictionary<string, string[]>
            {
                ["ProtectedBranch"] = ["Cannot delete the 'main' branch."]
            });
        }

        // 2. Load the branch to delete
        var branch = await sessionManager.Store.LoadBranchAsync(sid, bid, ct);
        if (branch == null)
        {
            return TypedResults.NotFound();
        }

        // 3.  Guard children — reject unless recursive is explicitly requested and permitted
        if (branch.ChildBranches.Count > 0)
        {
            if (!recursive)
            {
                return TypedResults.ValidationProblem(new Dictionary<string, string[]>
                {
                    ["HasChildren"] = [
                        $"Cannot delete branch with {branch.ChildBranches.Count} child branches. " +
                        $"Use ?recursive=true to delete the entire subtree, or delete children first: " +
                        $"{string.Join(", ", branch.ChildBranches)}"
                    ]
                });
            }

            if (!sessionManager.AllowRecursiveBranchDelete)
            {
                return TypedResults.ValidationProblem(new Dictionary<string, string[]>
                {
                    ["RecursiveDeleteDisabled"] = [
                        "Recursive branch deletion is not enabled on this server. " +
                        "Set AllowRecursiveBranchDelete = true in HPDAgentConfig to enable it."
                    ]
                });
            }
        }

        // 4. Check if branch is actively streaming — acquire and HOLD the stream lock
        if (!sessionManager.TryAcquireStreamLock(sid, bid))
        {
            return TypedResults.Conflict();
        }

        // 5.  Perform atomic deletion with sibling reindexing (stream lock held throughout)
        try
        {
            return await sessionManager.WithSessionLockAsync(sid,
                () => DoDeleteBranchAsync(sid, bid, branch, recursive, sessionManager, ct),
                ct);
        }
        catch (Exception ex)
        {
            return TypedResults.ValidationProblem(new Dictionary<string, string[]>
            {
                ["DeleteBranchError"] = [ex.Message]
            });
        }
        finally
        {
            sessionManager.ReleaseStreamLock(sid, bid);
            sessionManager.RemoveBranchStreamLock(sid, bid);
        }
    }

    /// <summary>
    /// Depth-first recursive delete of a branch subtree.
    /// Deletes all descendants before deleting the given branch node.
    /// Caller is responsible for holding the session lock.
    /// </summary>
    private static async Task DeleteSubtreeAsync(
        string sid,
        string bid,
        AspNetCoreSessionManager sessionManager,
        CancellationToken ct)
    {
        var branch = await sessionManager.Store.LoadBranchAsync(sid, bid, ct);
        if (branch == null) return;

        // Depth-first: delete all children before this node
        foreach (var childId in branch.ChildBranches.ToList())
            await DeleteSubtreeAsync(sid, childId, sessionManager, ct);

        // Reindex siblings and remove from parent pointer
        await ReindexSiblingsAfterDeleteAsync(sid, bid, branch, sessionManager, ct);

        await sessionManager.Store.DeleteBranchAsync(sid, bid, ct);
    }

    /// <summary>
    /// Removes a branch from its parent's ChildBranches list and reindexes
    /// the remaining siblings (SiblingIndex, TotalSiblings, navigation pointers).
    /// Caller is responsible for holding the session lock.
    /// </summary>
    private static async Task ReindexSiblingsAfterDeleteAsync(
        string sid,
        string bid,
        Branch branch,
        AspNetCoreSessionManager sessionManager,
        CancellationToken ct)
    {
        // Remove from parent's ChildBranches list
        if (branch.ForkedFrom != null)
        {
            var parent = await sessionManager.Store.LoadBranchAsync(sid, branch.ForkedFrom, ct);
            if (parent != null && parent.ChildBranches.Contains(bid))
            {
                parent.ChildBranches.Remove(bid);
                parent.LastActivity = DateTime.UtcNow;
                await sessionManager.Store.SaveBranchAsync(sid, parent, ct);
            }
        }

        // Load remaining siblings (same fork group, excluding deleted branch).
        // Fork group = source branch (slot 0) + all branches with same ForkedFrom + ForkedAtMessageIndex.
        var allBranchIds = await sessionManager.Store.ListBranchIdsAsync(sid, ct);
        var remainingSiblings = new List<Branch>();

        foreach (var branchId in allBranchIds)
        {
            if (branchId == bid) continue;

            var sibling = await sessionManager.Store.LoadBranchAsync(sid, branchId, ct);
            if (sibling == null) continue;

            bool isSameGroup = sibling.ForkedFrom == branch.ForkedFrom &&
                               sibling.ForkedAtMessageIndex == branch.ForkedAtMessageIndex;
            bool isSource = branch.ForkedFrom != null && branchId == branch.ForkedFrom;

            if (isSameGroup || isSource)
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
            await sessionManager.Store.SaveBranchAsync(sid, sibling, ct);
        }
    }

    private static async Task<Results<Ok<List<MessageDto>>, NotFound, ValidationProblem>> GetMessages(
        string sid,
        string bid,
        AspNetCoreSessionManager sessionManager,
        CancellationToken ct = default)
    {
        try
        {
            var branch = await sessionManager.Store.LoadBranchAsync(sid, bid, ct);
            if (branch == null)
            {
                return TypedResults.NotFound();
            }

            var messages = new List<MessageDto>();
            for (int i = 0; i < branch.MessageCount; i++)
            {
                var message = branch.Messages[i];
                // Exclude UsageContent — billing metadata, not conversation content
                var contents = message.Contents
                    .Where(c => c is not UsageContent)
                    .ToList();
                messages.Add(new MessageDto(
                    message.MessageId ?? $"msg-{i}",
                    message.Role.Value,
                    contents,
                    message.AuthorName,
                    message.CreatedAt?.ToString("O") ?? DateTime.UtcNow.ToString("O")));
            }

            // TypedResults.Ok uses options that chain HPDAgentApiJsonSerializerContext
            // (has List<MessageDto>) + HPDJsonContext (has AIContent polymorphism).
            return TypedResults.Ok(messages);
        }
        catch (Exception ex)
        {
            return TypedResults.ValidationProblem(new Dictionary<string, string[]>
            {
                ["GetMessagesError"] = [ex.Message]
            });
        }
    }

    /// <summary>
    ///  Get sibling branch metadata with full navigation info.
    /// Returns siblings sorted by SiblingIndex (deterministic ordering).
    /// </summary>
    private static async Task<Results<Ok<List<BranchDto>>, NotFound, ValidationProblem>> GetSiblings(
        string sid,
        string bid,
        AspNetCoreSessionManager sessionManager,
        CancellationToken ct = default)
    {
        try
        {
            // Load target branch
            var targetBranch = await sessionManager.Store.LoadBranchAsync(sid, bid, ct);
            if (targetBranch == null)
            {
                return TypedResults.NotFound();
            }

            // Get all branches in session
            var branchIds = await sessionManager.Store.ListBranchIdsAsync(sid, ct);
            var siblingDtos = new List<BranchDto>();

            // Filter siblings: same ForkedFrom + ForkedAtMessageIndex (peer forks),
            // plus the source branch (slot 0) when targetBranch is itself a fork.
            foreach (var branchId in branchIds)
            {
                var branch = await sessionManager.Store.LoadBranchAsync(sid, branchId, ct);
                if (branch == null) continue;

                bool isSameGroup = branch.ForkedFrom == targetBranch.ForkedFrom &&
                                   branch.ForkedAtMessageIndex == targetBranch.ForkedAtMessageIndex;
                bool isSource = targetBranch.ForkedFrom != null && branchId == targetBranch.ForkedFrom;

                if (isSameGroup || isSource)
                {
                    siblingDtos.Add(ToBranchDto(branch, sid));
                }
            }

            // Sort by SiblingIndex (should already be correct, but guarantee it)
            siblingDtos = siblingDtos
                .OrderBy(s => s.SiblingIndex)
                .ToList();

            return TypedResults.Ok(siblingDtos);
        }
        catch (Exception ex)
        {
            return TypedResults.ValidationProblem(new Dictionary<string, string[]>
            {
                ["GetSiblingsError"] = [ex.Message]
            });
        }
    }

    private static async Task<Results<Created<BranchDto>, NotFound, ValidationProblem>> DoForkBranchAsync(
        string sid,
        string bid,
        ForkBranchRequest request,
        AspNetCoreSessionManager sessionManager,
        AspNetCoreAgentManager agentManager,
        CancellationToken ct)
    {
        var sessionExists = await sessionManager.Store.LoadSessionAsync(sid, ct);
        if (sessionExists == null)
        {
            return TypedResults.NotFound();
        }

        var sourceBranchExists = await sessionManager.Store.LoadBranchAsync(sid, bid, ct);
        if (sourceBranchExists == null)
        {
            return TypedResults.NotFound();
        }

        var newBranchId = string.IsNullOrWhiteSpace(request.NewBranchId)
            ? Guid.NewGuid().ToString()
            : request.NewBranchId;

        var agent = await agentManager.GetOrBuildAgentAsync(request.AgentId ?? "default", ct);
        await agent.ForkBranchAsync(sid, bid, newBranchId, request.FromMessageIndex, ct);

        var newBranch = await sessionManager.Store.LoadBranchAsync(sid, newBranchId, ct)
            ?? throw new InvalidOperationException($"Branch '{newBranchId}' not found after fork.");

        if (!string.IsNullOrEmpty(request.Name))
            newBranch.Name = request.Name;

        if (!string.IsNullOrEmpty(request.Description))
            newBranch.Description = request.Description;

        if (request.Tags != null && request.Tags.Count > 0)
            newBranch.Tags = request.Tags;

        await sessionManager.Store.SaveBranchAsync(sid, newBranch, ct);

        var dto = ToBranchDto(newBranch, sid);
        return TypedResults.Created($"/sessions/{sid}/branches/{newBranch.Id}", dto);
    }

    private static async Task<Results<Ok<BranchDto>, NotFound, ValidationProblem>> DoUpdateBranchAsync(
        string sid,
        Branch branch,
        UpdateBranchRequest request,
        AspNetCoreSessionManager sessionManager,
        CancellationToken ct)
    {
        if (request.Name != null) branch.Name = request.Name;
        if (request.Description != null) branch.Description = request.Description;
        if (request.Tags != null) branch.Tags = request.Tags;
        branch.LastActivity = DateTime.UtcNow;

        await sessionManager.Store.SaveBranchAsync(sid, branch, ct);
        return TypedResults.Ok(ToBranchDto(branch, sid));
    }

    private static async Task<Results<NoContent, NotFound, Conflict, ValidationProblem>> DoDeleteBranchAsync(
        string sid,
        string bid,
        Branch branch,
        bool recursive,
        AspNetCoreSessionManager sessionManager,
        CancellationToken ct)
    {
        // Recursively delete all descendants first (if requested)
        if (recursive)
        {
            foreach (var childId in branch.ChildBranches.ToList())
                await DeleteSubtreeAsync(sid, childId, sessionManager, ct);
        }

        // Reindex siblings and remove this branch from parent's ChildBranches
        await ReindexSiblingsAfterDeleteAsync(sid, bid, branch, sessionManager, ct);

        // Update session's LastActivity
        var session = await sessionManager.Store.LoadSessionAsync(sid, ct);
        if (session != null)
        {
            session.LastActivity = DateTime.UtcNow;
            await sessionManager.Store.SaveSessionAsync(session, ct);
        }

        // Delete the branch (after all updates complete)
        await sessionManager.Store.DeleteBranchAsync(sid, bid, ct);

        return TypedResults.NoContent();
    }

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
            branch.MessageCount,
            branch.Tags,
            branch.Ancestors,
            //  Tree navigation metadata
            branch.SiblingIndex,
            branch.TotalSiblings,
            branch.IsOriginal,
            branch.OriginalBranchId,
            branch.PreviousSiblingId,
            branch.NextSiblingId,
            branch.TotalForks);
    }
}
