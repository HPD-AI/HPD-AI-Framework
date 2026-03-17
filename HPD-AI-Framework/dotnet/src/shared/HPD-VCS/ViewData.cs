using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace HPD.VCS.Core;

/// <summary>
/// Represents the state of a repository at a specific point in time.
/// This includes workspace commit mappings, head commits, and named branches.
/// Based on jj's View but simplified for initial implementation.
/// </summary>
public readonly record struct ViewData : IContentHashable
{
    /// <summary>
    /// Maps workspace names to their current commit IDs
    /// </summary>
    public IReadOnlyDictionary<string, CommitId> WorkspaceCommitIds { get; init; }
    
    /// <summary>
    /// List of commit IDs that are considered "heads" (latest commits in various lines of development)
    /// </summary>
    public IReadOnlyList<CommitId> HeadCommitIds { get; init; }    /// <summary>
    /// Maps branch names to their current commit IDs (mutable pointers to commits)
    /// </summary>
    public IReadOnlyDictionary<string, CommitId> Branches { get; init; }

    /// <summary>
    /// The current working copy commit ID for live working copy mode.
    /// This field is used when the working copy operates as a special commit that gets amended automatically.
    /// </summary>
    public CommitId? WorkingCopyId { get; init; }

    /// <summary>
    /// Creates a new ViewData with the specified workspace commits, head commits, and branches
    /// </summary>
    public ViewData(
        IReadOnlyDictionary<string, CommitId> workspaceCommitIds,
        IReadOnlyList<CommitId> headCommitIds,
        IReadOnlyDictionary<string, CommitId>? branches = null,
        CommitId? workingCopyId = null)    {
        WorkspaceCommitIds = workspaceCommitIds ?? throw new ArgumentNullException(nameof(workspaceCommitIds));
        HeadCommitIds = headCommitIds ?? throw new ArgumentNullException(nameof(headCommitIds));
        Branches = branches ?? new Dictionary<string, CommitId>();
        WorkingCopyId = workingCopyId;
        
        // Validate workspace names
        foreach (var workspaceName in workspaceCommitIds.Keys)
        {
            if (string.IsNullOrWhiteSpace(workspaceName))
            {
                throw new ArgumentException("Workspace name cannot be null or whitespace", nameof(workspaceCommitIds));
            }
        }

        // Validate branch names
        foreach (var branchName in Branches.Keys)
        {
            if (string.IsNullOrWhiteSpace(branchName))
            {
                throw new ArgumentException("Branch name cannot be null or whitespace", nameof(branches));
            }
        }
    }
    
    /// <summary>
    /// Creates an empty ViewData with no workspaces, heads, or branches
    /// </summary>
    public static ViewData Empty => new ViewData(
        new Dictionary<string, CommitId>(),
        new List<CommitId>(),
        new Dictionary<string, CommitId>());    /// <summary>
    /// Gets the canonical byte representation for content hashing.
    /// Format:
    /// workspace_count\n
    /// workspace1_name_length\nworkspace1_name\nworkspace1_commit_hex\n
    /// ...
    /// head_count\n
    /// head1_commit_hex\n
    /// ...
    /// branch_count\n
    /// branch1_name_length\nbranch1_name\nbranch1_commit_hex\n
    /// ...
    /// </summary>
    public byte[] GetBytesForHashing()
    {
        var builder = new StringBuilder();
        
        // Sort workspace commits by workspace name for deterministic output
        var sortedWorkspaces = WorkspaceCommitIds
            .OrderBy(kvp => kvp.Key, StringComparer.Ordinal)
            .ToList();
        
        builder.AppendLine(sortedWorkspaces.Count.ToString());
        
        foreach (var (workspaceName, commitId) in sortedWorkspaces)
        {
            var nameBytes = Encoding.UTF8.GetBytes(workspaceName);
            builder.AppendLine(nameBytes.Length.ToString());
            builder.AppendLine(workspaceName);
            builder.AppendLine(commitId.ToHexString());
        }
        
        // Sort head commits by hex string for deterministic output
        var sortedHeads = HeadCommitIds
            .OrderBy(commitId => commitId.ToHexString(), StringComparer.Ordinal)
            .ToList();
        
        builder.AppendLine(sortedHeads.Count.ToString());
        
        foreach (var commitId in sortedHeads)
        {
            builder.AppendLine(commitId.ToHexString());
        }

        // Sort branches by branch name for deterministic output
        var sortedBranches = Branches
            .OrderBy(kvp => kvp.Key, StringComparer.Ordinal)
            .ToList();
        
        builder.AppendLine(sortedBranches.Count.ToString());
          foreach (var (branchName, commitId) in sortedBranches)
        {
            var nameBytes = Encoding.UTF8.GetBytes(branchName);
            builder.AppendLine(nameBytes.Length.ToString());
            builder.AppendLine(branchName);
            builder.AppendLine(commitId.ToHexString());
        }
        
        // Add working copy ID
        if (WorkingCopyId.HasValue)
        {
            builder.AppendLine("1");
            builder.AppendLine(WorkingCopyId.Value.ToHexString());
        }
        else
        {
            builder.AppendLine("0");
        }
        
        return Encoding.UTF8.GetBytes(builder.ToString());
    }
    
    /// <summary>
    /// Parses ViewData from canonical byte representation
    /// </summary>
    public static ViewData ParseFromCanonicalBytes(byte[] contentBytes)
    {
        ArgumentNullException.ThrowIfNull(contentBytes);
          // Handle cross-platform newlines by normalizing to \n
        var content = Encoding.UTF8.GetString(contentBytes).Replace("\r\n", "\n");
        var lines = content.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        
        // Remove any remaining \r characters from lines
        for (int i = 0; i < lines.Length; i++)
        {
            lines[i] = lines[i].TrimEnd('\r');
        }
        
        var lineIndex = 0;
        
        try
        {
            // Parse workspace commits
            var workspaceCount = int.Parse(lines[lineIndex++]);
            var workspaceCommits = new Dictionary<string, CommitId>();
            
            for (int i = 0; i < workspaceCount; i++)
            {
                var nameLength = int.Parse(lines[lineIndex++]);
                var workspaceName = lines[lineIndex++];
                  // Validate the workspace name length
                var actualNameBytes = Encoding.UTF8.GetBytes(workspaceName);
                if (actualNameBytes.Length != nameLength)
                {
                    throw new ArgumentException($"Workspace name length mismatch: expected {nameLength}, got {actualNameBytes.Length}");
                }
                
                var commitHex = lines[lineIndex++];
                var commitId = ObjectIdBase.FromHexString<CommitId>(commitHex);
                
                workspaceCommits[workspaceName] = commitId;
            }
              // Parse head commits
            var headCount = int.Parse(lines[lineIndex++]);
            var headCommits = new List<CommitId>();
              for (int i = 0; i < headCount; i++)
            {
                var commitHex = lines[lineIndex++];
                var commitId = ObjectIdBase.FromHexString<CommitId>(commitHex);
                headCommits.Add(commitId);
            }

            // Parse branches (if present - for backward compatibility)
            var branches = new Dictionary<string, CommitId>();
            if (lineIndex < lines.Length)
            {
                var branchCount = int.Parse(lines[lineIndex++]);
                
                for (int i = 0; i < branchCount; i++)
                {
                    var nameLength = int.Parse(lines[lineIndex++]);
                    var branchName = lines[lineIndex++];
                    
                    // Validate the branch name length
                    var actualNameBytes = Encoding.UTF8.GetBytes(branchName);
                    if (actualNameBytes.Length != nameLength)
                    {
                        throw new ArgumentException($"Branch name length mismatch: expected {nameLength}, got {actualNameBytes.Length}");
                    }
                    
                    var commitHex = lines[lineIndex++];
                    var commitId = ObjectIdBase.FromHexString<CommitId>(commitHex);
                      branches[branchName] = commitId;
                }
            }
            
            // Parse working copy ID (if present - for backward compatibility)
            CommitId? workingCopyId = null;
            if (lineIndex < lines.Length)
            {
                var hasWorkingCopy = int.Parse(lines[lineIndex++]);
                if (hasWorkingCopy == 1 && lineIndex < lines.Length)
                {
                    var commitHex = lines[lineIndex++];
                    workingCopyId = ObjectIdBase.FromHexString<CommitId>(commitHex);
                }
            }
            
            return new ViewData(workspaceCommits, headCommits, branches, workingCopyId);
        }
        catch (Exception ex) when (ex is FormatException or IndexOutOfRangeException or ArgumentOutOfRangeException)
        {
            throw new ArgumentException("Invalid ViewData byte format", nameof(contentBytes), ex);
        }
    }
      /// <summary>
    /// Returns a new ViewData with the specified workspace commit updated
    /// </summary>
    public ViewData WithWorkspaceCommit(string workspaceName, CommitId commitId)
    {
        var newWorkspaceCommits = new Dictionary<string, CommitId>(WorkspaceCommitIds)
        {
            [workspaceName] = commitId
        };
        
        return new ViewData(newWorkspaceCommits, HeadCommitIds, Branches);
    }
    
    /// <summary>
    /// Returns a new ViewData with the specified head commit added
    /// </summary>
    public ViewData WithHeadCommit(CommitId commitId)
    {
        if (HeadCommitIds.Contains(commitId))
        {
            return this; // Already present
        }
        
        var newHeadCommits = new List<CommitId>(HeadCommitIds) { commitId };
        return new ViewData(WorkspaceCommitIds, newHeadCommits, Branches);
    }

    /// <summary>
    /// Returns a new ViewData with the specified branch updated
    /// </summary>
    public ViewData WithBranch(string branchName, CommitId commitId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(branchName);
        
        var newBranches = new Dictionary<string, CommitId>(Branches)
        {
            [branchName] = commitId
        };
        
        return new ViewData(WorkspaceCommitIds, HeadCommitIds, newBranches);
    }

    /// <summary>
    /// Returns a new ViewData with the specified branch removed
    /// </summary>
    public ViewData WithoutBranch(string branchName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(branchName);
        
        if (!Branches.ContainsKey(branchName))
        {
            return this; // Branch doesn't exist
        }
        
        var newBranches = new Dictionary<string, CommitId>(Branches);
        newBranches.Remove(branchName);
        
        return new ViewData(WorkspaceCommitIds, HeadCommitIds, newBranches);
    }
}
