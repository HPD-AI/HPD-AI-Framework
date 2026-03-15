using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using HPD.VCS.Core;

namespace HPD.VCS;

/// <summary>
/// Static helper class for rebasing descendants of rewritten and abandoned commits.
/// Implements the core logic for automatically updating commit graphs during history rewriting.
/// </summary>
public static class Rewriter
{
    /// <summary>
    /// Rebases all descendants of commits that were rewritten or abandoned in the given transaction.
    /// This ensures that the commit graph remains consistent after history rewriting operations.
    /// </summary>
    /// <param name="transaction">The transaction containing rewrite and abandonment mappings</param>
    public static async Task RebaseAllDescendantsAsync(Transaction transaction)
    {
        ArgumentNullException.ThrowIfNull(transaction);

        // Step 1: Find all commits that need to be rebased
        var commitsToRebase = await FindCommitsToRebaseAsync(transaction);

        // Step 2: Sort them topologically so parents are processed before children
        var sortedCommits = await TopologicalSortAsync(transaction.Repository, commitsToRebase);

        // Step 3: Rebase each commit in order
        foreach (var commitToRebase in sortedCommits)
        {
            await RebaseCommitAsync(transaction, commitToRebase);
        }
    }

    /// <summary>
    /// Finds all commits that are descendants of rewritten or abandoned commits.
    /// </summary>
    private static async Task<HashSet<CommitId>> FindCommitsToRebaseAsync(Transaction transaction)
    {
        var commitsToRebase = new HashSet<CommitId>();
        var rewrittenOrAbandonedIds = new HashSet<CommitId>();
        
        // Collect all rewritten and abandoned commit IDs
        rewrittenOrAbandonedIds.UnionWith(transaction.RewrittenCommits.Keys);
        rewrittenOrAbandonedIds.UnionWith(transaction.AbandonedCommits.Keys);

        if (rewrittenOrAbandonedIds.Count == 0)
        {
            return commitsToRebase;
        }

        // For V1: Walk from all heads to find descendants
        // This is a full walk but acceptable for initial implementation
        var allCommitIds = new HashSet<CommitId>();
        
        // Start from all heads and workspace commits
        var startingPoints = new HashSet<CommitId>();
        startingPoints.UnionWith(transaction.MutableView.HeadCommitIds);
        startingPoints.UnionWith(transaction.MutableView.WorkspaceCommitIds.Values);

        // Traverse the entire reachable commit graph
        await TraverseCommitGraphAsync(transaction.Repository, startingPoints, allCommitIds);

        // Find commits that have rewritten/abandoned ancestors
        foreach (var commitId in allCommitIds)
        {
            if (rewrittenOrAbandonedIds.Contains(commitId))
            {
                continue; // Don't rebase the rewritten/abandoned commits themselves
            }

            if (await HasRewrittenOrAbandonedAncestorAsync(transaction, commitId, rewrittenOrAbandonedIds))
            {
                commitsToRebase.Add(commitId);
            }
        }

        return commitsToRebase;
    }

    /// <summary>
    /// Checks if a commit has any rewritten or abandoned ancestors.
    /// </summary>
    private static async Task<bool> HasRewrittenOrAbandonedAncestorAsync(
        Transaction transaction, 
        CommitId commitId, 
        HashSet<CommitId> rewrittenOrAbandonedIds)
    {
        var visited = new HashSet<CommitId>();
        var toVisit = new Queue<CommitId>();
        toVisit.Enqueue(commitId);

        while (toVisit.Count > 0)
        {
            var current = toVisit.Dequeue();
            if (!visited.Add(current))
            {
                continue;
            }

            var commitData = await transaction.Repository.ObjectStore.ReadCommitAsync(current);
            if (!commitData.HasValue)
            {
                continue;
            }

            foreach (var parentId in commitData.Value.ParentIds)
            {
                if (rewrittenOrAbandonedIds.Contains(parentId))
                {
                    return true;
                }
                toVisit.Enqueue(parentId);
            }
        }

        return false;
    }

    /// <summary>
    /// Traverses the commit graph starting from the given commits.
    /// </summary>
    private static async Task TraverseCommitGraphAsync(
        Repository repository, 
        HashSet<CommitId> startingPoints, 
        HashSet<CommitId> visited)
    {
        var toVisit = new Queue<CommitId>();
        foreach (var startPoint in startingPoints)
        {
            toVisit.Enqueue(startPoint);
        }

        while (toVisit.Count > 0)
        {
            var current = toVisit.Dequeue();
            if (!visited.Add(current))
            {
                continue;
            }

            var commitData = await repository.ObjectStore.ReadCommitAsync(current);
            if (!commitData.HasValue)
            {
                continue;
            }

            foreach (var parentId in commitData.Value.ParentIds)
            {
                toVisit.Enqueue(parentId);
            }
        }
    }

    /// <summary>
    /// Performs a topological sort on the given commits so parents are processed before children.
    /// </summary>
    private static async Task<List<CommitId>> TopologicalSortAsync(Repository repository, HashSet<CommitId> commits)
    {
        var result = new List<CommitId>();
        var visited = new HashSet<CommitId>();
        var visiting = new HashSet<CommitId>();

        foreach (var commitId in commits)
        {
            await VisitCommitAsync(repository, commitId, commits, visited, visiting, result);
        }

        return result;
    }

    /// <summary>
    /// Recursive helper for topological sort using depth-first search.
    /// </summary>
    private static async Task VisitCommitAsync(
        Repository repository,
        CommitId commitId,
        HashSet<CommitId> commitsToSort,
        HashSet<CommitId> visited,
        HashSet<CommitId> visiting,
        List<CommitId> result)
    {
        if (visited.Contains(commitId))
        {
            return;
        }

        if (visiting.Contains(commitId))
        {
            // Cycle detected - for now, just continue
            return;
        }

        visiting.Add(commitId);

        var commitData = await repository.ObjectStore.ReadCommitAsync(commitId);
        if (commitData.HasValue)
        {
            // Visit parents first (they need to be processed before children)
            foreach (var parentId in commitData.Value.ParentIds)
            {
                if (commitsToSort.Contains(parentId))
                {
                    await VisitCommitAsync(repository, parentId, commitsToSort, visited, visiting, result);
                }
            }
        }

        visiting.Remove(commitId);
        visited.Add(commitId);
        result.Add(commitId);
    }

    /// <summary>
    /// Rebases a single commit, updating its parents and tree as needed.
    /// </summary>
    private static async Task RebaseCommitAsync(Transaction transaction, CommitId commitToRebaseId)
    {
        var oldCommitData = await transaction.Repository.ObjectStore.ReadCommitAsync(commitToRebaseId);
        if (!oldCommitData.HasValue)
        {
            return;
        }

        var oldCommit = oldCommitData.Value;

        // Step 1: Find new parents by resolving old parents through rewrite mappings
        var newParentIds = new List<CommitId>();
        foreach (var oldParentId in oldCommit.ParentIds)
        {
            var newParentId = ResolveParent(transaction, oldParentId);
            newParentIds.Add(newParentId);
        }

        // Step 2: Calculate new tree using 3-way merge
        var newTreeId = await CalculateRebasedTreeAsync(transaction.Repository, oldCommit, newParentIds);

        // Step 3: Create the rebased commit
        var rebasedCommit = await transaction.RewriteCommit(oldCommit)
            .SetParents(newParentIds)
            .SetTreeId(newTreeId)
            .WriteAsync();
    }    /// <summary>
    /// Resolves a parent commit ID through the rewrite and abandonment mappings.
    /// </summary>
    private static CommitId ResolveParent(Transaction transaction, CommitId oldParentId)
    {
        // Check if this parent was rewritten
        if (transaction.RewrittenCommits.TryGetValue(oldParentId, out var rewrittenParentId))
        {
            return rewrittenParentId;
        }

        // Check if this parent was abandoned
        if (transaction.AbandonedCommits.TryGetValue(oldParentId, out var newParents))
        {
            // For simplicity, use the first new parent
            // In practice, this might need more sophisticated handling for multiple parents
            return newParents.Count > 0 ? newParents[0] : oldParentId;
        }

        // Parent wasn't changed
        return oldParentId;
    }

    /// <summary>
    /// Calculates the new tree ID for a rebased commit using 3-way merge logic.
    /// </summary>
    private static async Task<TreeId> CalculateRebasedTreeAsync(
        Repository repository, 
        CommitData oldCommit, 
        List<CommitId> newParentIds)
    {
        // Find old merge bases
        var oldBases = await repository.FindMergeBasesAsync(oldCommit.ParentIds);
        TreeId oldBaseTreeId;
        
        if (oldBases.Count == 0)
        {
            // No merge base - use empty tree
            var emptyTreeData = new TreeData(new List<TreeEntry>());
            oldBaseTreeId = await repository.ObjectStore.WriteTreeAsync(emptyTreeData);
        }
        else
        {
            // Use first merge base
            var baseCommitData = await repository.ObjectStore.ReadCommitAsync(oldBases[0]);
            oldBaseTreeId = baseCommitData?.RootTreeId ?? await CreateEmptyTreeAsync(repository);
        }

        // Find new merge bases
        var newBases = await repository.FindMergeBasesAsync(newParentIds);
        TreeId newBaseTreeId;
        
        if (newBases.Count == 0)
        {
            // No merge base - use empty tree
            newBaseTreeId = await CreateEmptyTreeAsync(repository);
        }
        else
        {
            // Use first merge base
            var newBaseCommitData = await repository.ObjectStore.ReadCommitAsync(newBases[0]);
            newBaseTreeId = newBaseCommitData?.RootTreeId ?? await CreateEmptyTreeAsync(repository);
        }

        // Perform 3-way merge: merge(newBase, oldBase, oldCommitTree)
        var newTreeId = await TreeMerger.MergeTreesAsync(
            repository.ObjectStore, 
            newBaseTreeId, 
            oldBaseTreeId, 
            oldCommit.RootTreeId);

        return newTreeId;
    }

    /// <summary>
    /// Creates an empty tree and returns its ID.
    /// </summary>
    private static async Task<TreeId> CreateEmptyTreeAsync(Repository repository)
    {
        var emptyTreeData = new TreeData(new List<TreeEntry>());
        return await repository.ObjectStore.WriteTreeAsync(emptyTreeData);
    }
}
