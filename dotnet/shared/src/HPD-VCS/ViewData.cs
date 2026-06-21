using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace HPD.VCS.Core;

/// <summary>
/// Represents the state of a repository at a specific point in time.
/// This includes workspace commit mappings, head commits, and named threads.
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
    /// Maps thread names to their current commit IDs (mutable pointers to commits)
    /// </summary>
    public IReadOnlyDictionary<string, CommitId> Threads { get; init; }

    /// <summary>
    /// The current working copy commit ID for live working copy mode.
    /// This field is used when the working copy operates as a special commit that gets amended automatically.
    /// </summary>
    public CommitId? WorkingCopyId { get; init; }

    /// <summary>
    /// Creates a new ViewData with the specified workspace commits, head commits, and threads
    /// </summary>
    public ViewData(
        IReadOnlyDictionary<string, CommitId> workspaceCommitIds,
        IReadOnlyList<CommitId> headCommitIds,
        IReadOnlyDictionary<string, CommitId>? threads = null,
        CommitId? workingCopyId = null)    {
        WorkspaceCommitIds = workspaceCommitIds ?? throw new ArgumentNullException(nameof(workspaceCommitIds));
        HeadCommitIds = headCommitIds ?? throw new ArgumentNullException(nameof(headCommitIds));
        Threads = threads ?? new Dictionary<string, CommitId>();
        WorkingCopyId = workingCopyId;
        
        // Validate workspace names
        foreach (var workspaceName in workspaceCommitIds.Keys)
        {
            if (string.IsNullOrWhiteSpace(workspaceName))
            {
                throw new ArgumentException("Workspace name cannot be null or whitespace", nameof(workspaceCommitIds));
            }
        }

        // Validate thread names
        foreach (var threadName in Threads.Keys)
        {
            if (string.IsNullOrWhiteSpace(threadName))
            {
                throw new ArgumentException("Thread name cannot be null or whitespace", nameof(threads));
            }
        }
    }
    
    /// <summary>
    /// Creates an empty ViewData with no workspaces, heads, or threads
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
    /// thread_count\n
    /// thread1_name_length\nthread1_name\nthread1_commit_hex\n
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

        // Sort threads by thread name for deterministic output
        var sortedThreads = Threads
            .OrderBy(kvp => kvp.Key, StringComparer.Ordinal)
            .ToList();
        
        builder.AppendLine(sortedThreads.Count.ToString());
          foreach (var (threadName, commitId) in sortedThreads)
        {
            var nameBytes = Encoding.UTF8.GetBytes(threadName);
            builder.AppendLine(nameBytes.Length.ToString());
            builder.AppendLine(threadName);
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

            // Parse threads (if present - for backward compatibility)
            var threads = new Dictionary<string, CommitId>();
            if (lineIndex < lines.Length)
            {
                var threadCount = int.Parse(lines[lineIndex++]);
                
                for (int i = 0; i < threadCount; i++)
                {
                    var nameLength = int.Parse(lines[lineIndex++]);
                    var threadName = lines[lineIndex++];
                    
                    // Validate the thread name length
                    var actualNameBytes = Encoding.UTF8.GetBytes(threadName);
                    if (actualNameBytes.Length != nameLength)
                    {
                        throw new ArgumentException($"Thread name length mismatch: expected {nameLength}, got {actualNameBytes.Length}");
                    }
                    
                    var commitHex = lines[lineIndex++];
                    var commitId = ObjectIdBase.FromHexString<CommitId>(commitHex);
                      threads[threadName] = commitId;
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
            
            return new ViewData(workspaceCommits, headCommits, threads, workingCopyId);
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
        
        return new ViewData(newWorkspaceCommits, HeadCommitIds, Threads);
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
        return new ViewData(WorkspaceCommitIds, newHeadCommits, Threads);
    }

    /// <summary>
    /// Returns a new ViewData with the specified thread updated
    /// </summary>
    public ViewData WithThread(string threadName, CommitId commitId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(threadName);
        
        var newThreads = new Dictionary<string, CommitId>(Threads)
        {
            [threadName] = commitId
        };
        
        return new ViewData(WorkspaceCommitIds, HeadCommitIds, newThreads);
    }

    /// <summary>
    /// Returns a new ViewData with the specified thread removed
    /// </summary>
    public ViewData WithoutThread(string threadName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(threadName);
        
        if (!Threads.ContainsKey(threadName))
        {
            return this; // Thread doesn't exist
        }
        
        var newThreads = new Dictionary<string, CommitId>(Threads);
        newThreads.Remove(threadName);
        
        return new ViewData(WorkspaceCommitIds, HeadCommitIds, newThreads);
    }
}
