using HPD.Agent.Hosting.Data;
using HPD.Agent.Hosting.Extensions;
using Microsoft.Extensions.AI;

namespace HPD.Agent.Hosting.Lifecycle;

public sealed class AgentBranchService : IAgentBranchService
{
    private readonly SessionManager _sessionManager;
    private readonly AgentManager _agentManager;

    public AgentBranchService(SessionManager sessionManager, AgentManager agentManager)
    {
        _sessionManager = sessionManager ?? throw new ArgumentNullException(nameof(sessionManager));
        _agentManager = agentManager ?? throw new ArgumentNullException(nameof(agentManager));
    }

    public async Task<AgentServiceResult<IReadOnlyList<BranchDto>>> ListBranchesAsync(
        string sessionId,
        CancellationToken cancellationToken = default)
    {
        if (await _sessionManager.Store.LoadSessionAsync(sessionId, cancellationToken) == null)
            return AgentServiceResult<IReadOnlyList<BranchDto>>.NotFound;

        var branchIds = await _sessionManager.Store.ListBranchIdsAsync(sessionId, cancellationToken);
        var dtos = new List<BranchDto>();
        foreach (var branchId in branchIds)
        {
            var branch = await _sessionManager.Store.LoadBranchAsync(sessionId, branchId, cancellationToken);
            if (branch != null)
                dtos.Add(branch.ToDto(sessionId));
        }

        return AgentServiceResult<IReadOnlyList<BranchDto>>.Success(dtos);
    }

    public async Task<AgentServiceResult<BranchDto>> GetBranchAsync(
        string sessionId,
        string branchId,
        CancellationToken cancellationToken = default)
    {
        var branch = await _sessionManager.Store.LoadBranchAsync(sessionId, branchId, cancellationToken);
        return branch == null
            ? AgentServiceResult<BranchDto>.NotFound
            : AgentServiceResult<BranchDto>.Success(branch.ToDto(sessionId));
    }

    public async Task<AgentServiceResult<BranchDto>> CreateBranchAsync(
        string agentId,
        string sessionId,
        CreateBranchRequest request,
        CancellationToken cancellationToken = default)
    {
        if (await _sessionManager.Store.LoadSessionAsync(sessionId, cancellationToken) == null)
            return AgentServiceResult<BranchDto>.NotFound;

        var branchId = string.IsNullOrWhiteSpace(request.BranchId)
            ? Guid.NewGuid().ToString()
            : request.BranchId;

        if (await _sessionManager.Store.LoadBranchAsync(sessionId, branchId, cancellationToken) != null)
            return AgentServiceResult<BranchDto>.Conflict;

        var session = await _sessionManager.Store.LoadSessionAsync(sessionId, cancellationToken)
            ?? throw new InvalidOperationException($"Session '{sessionId}' not found after existence check.");
        session.Store = _sessionManager.Store;

        var branch = session.CreateBranch(branchId);
        branch.Name = request.Name ?? branchId;
        branch.Description = request.Description;
        branch.Tags = request.Tags;
        MergeBranchMetadata(branch, request.Metadata);
        await _sessionManager.Store.SaveInitialBranchAsync(sessionId, branch, cancellationToken);

        branch = await _sessionManager.Store.LoadBranchAsync(sessionId, branchId, cancellationToken)
            ?? throw new InvalidOperationException($"Branch '{branchId}' not found after creation.");

        return AgentServiceResult<BranchDto>.Success(branch.ToDto(sessionId));
    }

    public Task<AgentServiceResult<BranchDto>> ForkBranchAsync(
        string agentId,
        string sessionId,
        string branchId,
        ForkBranchRequest request,
        CancellationToken cancellationToken = default)
    {
        return _sessionManager.WithSessionLockAsync(
            sessionId,
            () => ForkBranchCoreAsync(agentId, sessionId, branchId, request, cancellationToken),
            cancellationToken);
    }

    public async Task<AgentServiceResult<BranchDto>> UpdateBranchAsync(
        string sessionId,
        string branchId,
        UpdateBranchRequest request,
        CancellationToken cancellationToken = default)
    {
        var branch = await _sessionManager.Store.LoadBranchAsync(sessionId, branchId, cancellationToken);
        if (branch == null)
            return AgentServiceResult<BranchDto>.NotFound;

        return await _sessionManager.WithSessionLockAsync(
            sessionId,
            async () =>
            {
                if (request.Name != null) branch.Name = request.Name;
                if (request.Description != null) branch.Description = request.Description;
                if (request.Tags != null) branch.Tags = request.Tags;
                MergeBranchMetadata(branch, request.Metadata);
                branch.LastActivity = DateTime.UtcNow;

                await _sessionManager.Store.AppendBranchMetadataUpdatedAsync(branch, cancellationToken);
                return AgentServiceResult<BranchDto>.Success(branch.ToDto(sessionId));
            },
            cancellationToken);
    }

    public async Task<AgentServiceResult> DeleteBranchAsync(
        string sessionId,
        string branchId,
        bool recursive = false,
        CancellationToken cancellationToken = default)
    {
        if (branchId == "main")
            return AgentServiceResult.Validation("ProtectedBranch", "Cannot delete the 'main' branch.");

        var branch = await _sessionManager.Store.LoadBranchAsync(sessionId, branchId, cancellationToken);
        if (branch == null)
            return AgentServiceResult.NotFound;

        if (branch.ChildBranches.Count > 0)
        {
            if (!recursive)
            {
                return AgentServiceResult.Validation(
                    "HasChildren",
                    $"Cannot delete branch with {branch.ChildBranches.Count} child branches. " +
                    $"Use ?recursive=true to delete the entire subtree, or delete children first: " +
                    $"{string.Join(", ", branch.ChildBranches)}");
            }

            if (!_sessionManager.AllowRecursiveBranchDelete)
            {
                return AgentServiceResult.Validation(
                    "RecursiveDeleteDisabled",
                    "Recursive branch deletion is not enabled on this server. " +
                    "Set AllowRecursiveBranchDelete = true in HPDAgentConfig to enable it.");
            }
        }

        if (!_sessionManager.TryAcquireBranchOperationLock(sessionId, branchId))
            return AgentServiceResult.Conflict;

        try
        {
            return await _sessionManager.WithSessionLockAsync(
                sessionId,
                () => DeleteBranchCoreAsync(sessionId, branchId, branch, recursive, cancellationToken),
                cancellationToken);
        }
        finally
        {
            _sessionManager.ReleaseBranchOperationLock(sessionId, branchId);
            _sessionManager.RemoveBranchOperationLock(sessionId, branchId);
        }
    }

    public async Task<AgentServiceResult<IReadOnlyList<AgentEvent>>> GetEventsAsync(
        string sessionId,
        string branchId,
        CancellationToken cancellationToken = default)
    {
        var document = await _sessionManager.Store.LoadBranchDocumentAsync(sessionId, branchId, cancellationToken);
        if (document == null && await _sessionManager.Store.LoadBranchAsync(sessionId, branchId, cancellationToken) == null)
            return AgentServiceResult<IReadOnlyList<AgentEvent>>.NotFound;

        return AgentServiceResult<IReadOnlyList<AgentEvent>>.Success(
            document?.Events.OrderBy(e => e.SequenceNumber).ToList() ?? []);
    }

    public async Task<AgentServiceResult<IReadOnlyList<BranchDto>>> GetSiblingsAsync(
        string sessionId,
        string branchId,
        CancellationToken cancellationToken = default)
    {
        var targetBranch = await _sessionManager.Store.LoadBranchAsync(sessionId, branchId, cancellationToken);
        if (targetBranch == null)
            return AgentServiceResult<IReadOnlyList<BranchDto>>.NotFound;

        var branchIds = await _sessionManager.Store.ListBranchIdsAsync(sessionId, cancellationToken);
        var siblingDtos = new List<BranchDto>();

        foreach (var id in branchIds)
        {
            var branch = await _sessionManager.Store.LoadBranchAsync(sessionId, id, cancellationToken);
            if (branch == null)
                continue;

            var isSameGroup = branch.ForkedFrom == targetBranch.ForkedFrom &&
                branch.ForkedAtMessageId == targetBranch.ForkedAtMessageId;
            var isSource = targetBranch.ForkedFrom != null && id == targetBranch.ForkedFrom;

            if (isSameGroup || isSource)
                siblingDtos.Add(branch.ToDto(sessionId));
        }

        return AgentServiceResult<IReadOnlyList<BranchDto>>.Success(
            siblingDtos.OrderBy(s => s.SiblingIndex).ToList());
    }

    private async Task<AgentServiceResult<BranchDto>> ForkBranchCoreAsync(
        string agentId,
        string sessionId,
        string branchId,
        ForkBranchRequest request,
        CancellationToken cancellationToken)
    {
        if (await _sessionManager.Store.LoadSessionAsync(sessionId, cancellationToken) == null)
            return AgentServiceResult<BranchDto>.NotFound;

        if (await _sessionManager.Store.LoadBranchAsync(sessionId, branchId, cancellationToken) == null)
            return AgentServiceResult<BranchDto>.NotFound;

        var newBranchId = string.IsNullOrWhiteSpace(request.NewBranchId)
            ? Guid.NewGuid().ToString()
            : request.NewBranchId;

        var agent = await _agentManager.GetOrBuildAgentAsync(agentId, cancellationToken);
        try
        {
            await agent.ForkBranchAsync(
                sessionId,
                branchId,
                newBranchId,
                request.FromMessageId,
                request.Metadata,
                cancellationToken);
        }
        catch (MessageNotPresentOnBranchException ex)
        {
            return AgentServiceResult<BranchDto>.Validation(
                "ForkMessageNotPresent",
                ex.Message);
        }

        var newBranch = await _sessionManager.Store.LoadBranchAsync(sessionId, newBranchId, cancellationToken)
            ?? throw new InvalidOperationException($"Branch '{newBranchId}' not found after fork.");

        ApplyBranchMetadata(newBranch, request.Name, request.Description, request.Tags, metadata: null);
        await _sessionManager.Store.AppendBranchMetadataUpdatedAsync(newBranch, cancellationToken);

        return AgentServiceResult<BranchDto>.Success(newBranch.ToDto(sessionId));
    }

    private async Task<AgentServiceResult> DeleteBranchCoreAsync(
        string sessionId,
        string branchId,
        Branch branch,
        bool recursive,
        CancellationToken cancellationToken)
    {
        if (recursive)
        {
            foreach (var childId in branch.ChildBranches.ToList())
                await DeleteSubtreeAsync(sessionId, childId, cancellationToken);
        }

        await ReindexSiblingsAfterDeleteAsync(sessionId, branchId, branch, cancellationToken);

        var session = await _sessionManager.Store.LoadSessionAsync(sessionId, cancellationToken);
        if (session != null)
        {
            session.LastActivity = DateTime.UtcNow;
            await _sessionManager.Store.SaveSessionAsync(session, cancellationToken);
        }

        await _sessionManager.Store.DeleteBranchAsync(sessionId, branchId, cancellationToken);
        return AgentServiceResult.Success;
    }

    private async Task DeleteSubtreeAsync(
        string sessionId,
        string branchId,
        CancellationToken cancellationToken)
    {
        var branch = await _sessionManager.Store.LoadBranchAsync(sessionId, branchId, cancellationToken);
        if (branch == null)
            return;

        foreach (var childId in branch.ChildBranches.ToList())
            await DeleteSubtreeAsync(sessionId, childId, cancellationToken);

        await ReindexSiblingsAfterDeleteAsync(sessionId, branchId, branch, cancellationToken);
        await _sessionManager.Store.DeleteBranchAsync(sessionId, branchId, cancellationToken);
    }

    private async Task ReindexSiblingsAfterDeleteAsync(
        string sessionId,
        string branchId,
        Branch branch,
        CancellationToken cancellationToken)
    {
        if (branch.ForkedFrom != null)
        {
            var parent = await _sessionManager.Store.LoadBranchAsync(sessionId, branch.ForkedFrom, cancellationToken);
            if (parent != null && parent.ChildBranches.Contains(branchId))
            {
                parent.ChildBranches.Remove(branchId);
                parent.LastActivity = DateTime.UtcNow;
                await _sessionManager.Store.AppendBranchTreeUpdatedAsync(parent, cancellationToken);
            }
        }

        var allBranchIds = await _sessionManager.Store.ListBranchIdsAsync(sessionId, cancellationToken);
        var remainingSiblings = new List<Branch>();

        foreach (var id in allBranchIds)
        {
            if (id == branchId)
                continue;

            var sibling = await _sessionManager.Store.LoadBranchAsync(sessionId, id, cancellationToken);
            if (sibling == null)
                continue;

            var isSameGroup = sibling.ForkedFrom == branch.ForkedFrom &&
                sibling.ForkedAtMessageId == branch.ForkedAtMessageId;
            var isSource = branch.ForkedFrom != null && id == branch.ForkedFrom;

            if (isSameGroup || isSource)
                remainingSiblings.Add(sibling);
        }

        remainingSiblings = remainingSiblings.OrderBy(b => b.SiblingIndex).ToList();
        for (var i = 0; i < remainingSiblings.Count; i++)
        {
            var sibling = remainingSiblings[i];
            sibling.SiblingIndex = i;
            sibling.TotalSiblings = remainingSiblings.Count;
            sibling.PreviousSiblingId = i > 0 ? remainingSiblings[i - 1].Id : null;
            sibling.NextSiblingId = i < remainingSiblings.Count - 1 ? remainingSiblings[i + 1].Id : null;
            sibling.LastActivity = DateTime.UtcNow;
            await _sessionManager.Store.AppendBranchTreeUpdatedAsync(sibling, cancellationToken);
        }
    }

    private static void ApplyBranchMetadata(
        Branch branch,
        string? name,
        string? description,
        List<string>? tags,
        Dictionary<string, object>? metadata)
    {
        if (!string.IsNullOrEmpty(name))
            branch.Name = name;
        if (!string.IsNullOrEmpty(description))
            branch.Description = description;
        if (tags != null && tags.Count > 0)
            branch.Tags = tags;
        if (metadata != null)
        {
            var runtimeMetadata = metadata
                .Where(kvp => kvp.Value != null)
                .ToDictionary(kvp => kvp.Key, kvp => kvp.Value!, StringComparer.Ordinal);
            branch.ApplyRuntimeMetadata(runtimeMetadata);
            branch.Metadata.Clear();
            foreach (var (key, value) in runtimeMetadata)
                branch.Metadata[key] = value;
        }
    }

    private static void MergeBranchMetadata(
        Branch branch,
        Dictionary<string, object?>? patch)
    {
        if (patch == null)
            return;

        var extensionPatch = patch
            .Where(kvp => kvp.Value != null)
            .ToDictionary(kvp => kvp.Key, kvp => kvp.Value!, StringComparer.Ordinal);
        branch.ApplyRuntimeMetadata(extensionPatch);

        foreach (var (key, value) in patch)
        {
            if (value == null)
                branch.Metadata.Remove(key);
        }

        foreach (var (key, value) in extensionPatch)
        {
            branch.Metadata[key] = value;
        }
    }
}
