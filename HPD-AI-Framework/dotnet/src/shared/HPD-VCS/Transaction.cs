using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using HPD.VCS.Core;
using HPD.VCS.Storage;

namespace HPD.VCS;

/// <summary>
/// Represents a transaction that manages complex, multi-commit rewrite operations atomically.
/// This class implements the Unit of Work pattern for repository mutations.
/// </summary>
public class Transaction
{
    private readonly Repository _repo;
    private readonly UserSettings _settings;
    private ViewData _mutableView;
    private readonly Dictionary<CommitId, CommitId> _rewrittenCommits = new();
    private readonly Dictionary<CommitId, IReadOnlyList<CommitId>> _abandonedCommits = new();

    /// <summary>
    /// Initializes a new transaction with the given repository and user settings.
    /// </summary>
    /// <param name="repo">The repository this transaction operates on</param>
    /// <param name="settings">User settings for operation metadata</param>
    public Transaction(Repository repo, UserSettings settings)
    {
        _repo = repo ?? throw new ArgumentNullException(nameof(repo));
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        
        // Create a mutable copy of the current view data
        _mutableView = repo.CurrentViewData;
    }

    /// <summary>
    /// Gets the repository this transaction operates on.
    /// </summary>
    public Repository Repository => _repo;

    /// <summary>
    /// Gets the current mutable view data within this transaction.
    /// </summary>
    public ViewData MutableView => _mutableView;

    /// <summary>
    /// Gets the mappings of rewritten commits within this transaction.
    /// </summary>
    public IReadOnlyDictionary<CommitId, CommitId> RewrittenCommits => _rewrittenCommits;

    /// <summary>
    /// Gets the mappings of abandoned commits to their replacement parents.
    /// </summary>
    public IReadOnlyDictionary<CommitId, IReadOnlyList<CommitId>> AbandonedCommits => _abandonedCommits;

    /// <summary>    /// <summary>
    /// Creates a CommitBuilder for rewriting an existing commit.
    /// If the commit was already rewritten in this transaction, uses the latest version.
    /// </summary>
    /// <param name="commitToRewrite">The commit to rewrite</param>
    /// <returns>A CommitBuilder for creating the rewritten commit</returns>
    public CommitBuilder RewriteCommit(CommitData commitToRewrite)
    {
        // Check if this commit was already rewritten in this transaction
        var targetCommitId = ObjectHasher.ComputeCommitId(commitToRewrite);
        if (_rewrittenCommits.TryGetValue(targetCommitId, out var rewrittenId))
        {
            // Use the latest rewritten version
            var latestCommitData = _repo.ObjectStore.ReadCommitAsync(rewrittenId).Result;
            if (latestCommitData.HasValue)
            {
                targetCommitId = rewrittenId;
                commitToRewrite = latestCommitData.Value;
            }
        }

        return new CommitBuilder(this, commitToRewrite);
    }

    /// <summary>
    /// Creates a CommitBuilder for creating a brand new commit.
    /// </summary>
    /// <returns>A CommitBuilder for creating the new commit</returns>
    public CommitBuilder NewCommit()
    {
        return new CommitBuilder(this);
    }    /// <summary>
    /// Records a commit as abandoned. Descendants will be rebased onto the specified new parents.
    /// </summary>
    /// <param name="oldCommitId">The commit ID to abandon</param>
    /// <param name="newParentsForChildren">The new parent(s) that children should be rebased onto</param>
    public void AbandonCommit(CommitId oldCommitId, IReadOnlyList<CommitId> newParentsForChildren)
    {
        ArgumentNullException.ThrowIfNull(newParentsForChildren);

        _abandonedCommits[oldCommitId] = newParentsForChildren;
    }

    /// <summary>
    /// Commits this transaction atomically, applying all changes to the repository.
    /// </summary>
    /// <param name="description">Description for the operation metadata</param>
    /// <returns>The ID of the created operation</returns>
    public async Task<OperationId> CommitAsync(string description)
    {
        ArgumentNullException.ThrowIfNull(description);

        // Step 1: Rebase all descendants of rewritten and abandoned commits
        await Rewriter.RebaseAllDescendantsAsync(this);        // Step 2: Update all references to point to new commit IDs
        UpdateAllReferences();

        // Step 3: Write the final view and create operation
        var finalViewId = await _repo.OperationStore.WriteViewAsync(_mutableView);

        var now = DateTimeOffset.UtcNow;
        var operationMetadata = new OperationMetadata(
            startTime: now,
            endTime: now.AddMilliseconds(1),
            description: description,
            username: _settings.GetUsername(),
            hostname: _settings.GetHostname(),
            tags: new Dictionary<string, string> { { "type", "rewrite" } }
        );

        var operationData = new OperationData(
            associatedViewId: finalViewId,
            parentOperationIds: new List<OperationId> { _repo.CurrentOperationId },
            metadata: operationMetadata
        );

        var newOperationId = await _repo.OperationStore.WriteOperationAsync(operationData);

        // Step 4: Update operation head and repository state
        await _repo.OperationHeadStore.UpdateHeadOperationIdsAsync(
            new List<OperationId> { _repo.CurrentOperationId }, 
            newOperationId);

        // Step 5: Update the parent repository's in-memory state using reflection
        // This is necessary because Repository fields are private
        var repoType = typeof(Repository);
        
        var currentOperationIdField = repoType.GetField("_currentOperationId", 
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        currentOperationIdField?.SetValue(_repo, newOperationId);

        var currentViewDataField = repoType.GetField("_currentViewData", 
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        currentViewDataField?.SetValue(_repo, _mutableView);

        // Update current commit data if workspace commit changed
        if (_mutableView.WorkspaceCommitIds.TryGetValue("default", out var newWorkspaceCommitId))
        {
            var newCommitData = await _repo.ObjectStore.ReadCommitAsync(newWorkspaceCommitId);
            if (newCommitData.HasValue)
            {
                var currentCommitDataField = repoType.GetField("_currentCommitData", 
                    System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                currentCommitDataField?.SetValue(_repo, newCommitData.Value);
            }
        }

        return newOperationId;
    }

    /// <summary>
    /// Internal method called by CommitBuilder to register a rewrite mapping.
    /// </summary>
    /// <param name="oldCommitId">The original commit ID</param>
    /// <param name="newCommitId">The new commit ID</param>
    internal void RegisterRewrite(CommitId oldCommitId, CommitId newCommitId)
    {
        _rewrittenCommits[oldCommitId] = newCommitId;
    }    /// <summary>
    /// Updates all references (branches, workspace commit IDs, working copy ID) to point to rewritten commits.
    /// </summary>
    private void UpdateAllReferences()
    {        // Update workspace commit IDs
        var newWorkspaceCommitIds = new Dictionary<string, CommitId>(_mutableView.WorkspaceCommitIds);
        foreach (var kvp in _mutableView.WorkspaceCommitIds)
        {
            var newCommitId = ResolveCommit(kvp.Value);
            if (!newCommitId.Equals(kvp.Value))
            {
                newWorkspaceCommitIds[kvp.Key] = newCommitId;
            }
        }

        // Update head commit IDs
        var newHeadCommitIds = new List<CommitId>();
        foreach (var headId in _mutableView.HeadCommitIds)
        {
            var newHeadId = ResolveCommit(headId);
            newHeadCommitIds.Add(newHeadId);
        }

        // Update branch references
        var newBranches = new Dictionary<string, CommitId>(_mutableView.Branches);
        foreach (var kvp in _mutableView.Branches)
        {
            var newCommitId = ResolveCommit(kvp.Value);
            if (!newCommitId.Equals(kvp.Value))
            {
                newBranches[kvp.Key] = newCommitId;
            }
        }

        // Update working copy ID if it has been rewritten
        CommitId? newWorkingCopyId = _mutableView.WorkingCopyId;
        if (_mutableView.WorkingCopyId.HasValue)
        {
            var resolvedWorkingCopyId = ResolveCommit(_mutableView.WorkingCopyId.Value);
            if (!resolvedWorkingCopyId.Equals(_mutableView.WorkingCopyId.Value))
            {
                newWorkingCopyId = resolvedWorkingCopyId;
            }
        }

        // Create updated view data
        _mutableView = new ViewData(newWorkspaceCommitIds, newHeadCommitIds, newBranches, newWorkingCopyId);
    }/// <summary>
    /// Resolves a commit ID to its final rewritten version, following the chain of rewrites.
    /// </summary>
    /// <param name="commitId">The commit ID to resolve</param>
    /// <returns>The final commit ID after following all rewrites and abandonments</returns>
    private CommitId ResolveCommit(CommitId commitId)
    {
        // Follow the chain of rewrites
        var currentId = commitId;
        var visited = new HashSet<CommitId>();
        
        while (visited.Add(currentId))
        {
            if (_rewrittenCommits.TryGetValue(currentId, out var rewrittenId))
            {
                currentId = rewrittenId;
            }
            else if (_abandonedCommits.TryGetValue(currentId, out var newParents))
            {
                // For abandoned commits, use the first new parent
                // In practice, this might be more complex depending on the scenario
                if (newParents.Count > 0)
                {
                    currentId = newParents[0];
                }
                else
                {
                    break;
                }
            }
            else
            {
                break;
            }
        }

        return currentId;
    }
}
