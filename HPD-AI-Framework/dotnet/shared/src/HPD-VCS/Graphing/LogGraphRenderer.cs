using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using HPD.VCS.Core;

namespace HPD.VCS.Graphing;

/// <summary>
/// Renders ASCII graph visualization for commit history using lane-based algorithm.
/// Implements Task 8.3 - ASCII graph rendering for log command.
/// </summary>
public static class LogGraphRenderer
{
    /// <summary>
    /// Renders ASCII graph lines for the given commits and their graph edges.
    /// Uses lane-based algorithm to draw o, |, /, \, and * characters.
    /// </summary>
    /// <param name="commitsWithEdges">List of commits with their graph edges from GetGraphLogAsync</param>
    /// <returns>List of tuples containing the ASCII graph prefix and commit description</returns>
    public static IReadOnlyList<(string GraphPrefix, string CommitLine)> Render(
        IReadOnlyList<(CommitData Commit, IReadOnlyList<GraphEdge> Edges)> commitsWithEdges)
    {
        // Add null check for input parameter
        if (commitsWithEdges == null)
            return new List<(string, string)>();
            
        if (commitsWithEdges.Count == 0)
            return new List<(string, string)>();

        var result = new List<(string GraphPrefix, string CommitLine)>();
        var lanes = new List<CommitId?>(); // null = empty lane
          
        for (int commitIndex = 0; commitIndex < commitsWithEdges.Count; commitIndex++)
        {
            var (commit, edges) = commitsWithEdges[commitIndex];
            
            // Null safety checks - only check edges since CommitData is a value type
            if (edges == null)
                throw new ArgumentException($"Edges at index {commitIndex} is null");
            
            // Get commit ID using ObjectHasher
            var commitId = ObjectHasher.ComputeCommitId(commit);
            
            // Find or assign lane for current commit
            var currentLaneIndex = FindOrAssignLane(lanes, commitId);
            
            // Generate the graph line for this commit
            var graphPrefix = GenerateCommitLine(lanes, currentLaneIndex, edges);
            
            // Format commit information
            var commitLine = FormatCommitLine(commit);
            
            result.Add((graphPrefix, commitLine));
            
            // Update lanes for next iteration
            UpdateLanes(lanes, currentLaneIndex, edges);
            
            // Add connector line between commits (except after the last commit)
            if (commitIndex < commitsWithEdges.Count - 1)
            {
                var connectorPrefix = GenerateConnectorLine(lanes);
                result.Add((connectorPrefix, ""));
            }
        }
        
        return result;
    }
    
    /// <summary>
    /// Finds existing lane for commit or assigns a new one
    /// </summary>
    private static int FindOrAssignLane(List<CommitId?> lanes, CommitId commitId)
    {
        // First try to find existing lane
        for (int i = 0; i < lanes.Count; i++)
        {
            if (lanes[i]?.Equals(commitId) == true)
            {
                return i;
            }
        }
        
        // Find first empty lane
        for (int i = 0; i < lanes.Count; i++)
        {
            if (lanes[i] == null)
            {
                lanes[i] = commitId;
                return i;
            }
        }
        
        // Add new lane
        lanes.Add(commitId);
        return lanes.Count - 1;
    }

    /// <summary>
    /// Generates the ASCII graph line for a commit (shows the commit itself with o, *, etc.)
    /// </summary>
    private static string GenerateCommitLine(List<CommitId?> lanes, int currentLaneIndex, IReadOnlyList<GraphEdge> edges)
    {
        var line = new StringBuilder();
        
        // Ensure lanes has enough capacity
        while (lanes.Count <= currentLaneIndex)
        {
            lanes.Add(null);
        }
        
        // Draw each lane
        for (int i = 0; i < lanes.Count; i++)
        {
            if (i == currentLaneIndex)
            {
                // Current commit position - null check for edges
                char commitChar = (edges?.Count > 1) ? '*' : 'o'; // * for merge commits, o for regular
                line.Append(commitChar);
            }
            else if (lanes[i] != null)
            {
                // Other lanes just show empty space for commit lines
                line.Append(' ');
            }
            else
            {
                line.Append(' ');
            }
            
            // Add spacing between lanes (except for last lane)
            if (i < lanes.Count - 1)
            {
                line.Append(' ');
            }        }
        
        // Add merge/fork connectors if needed
        if (edges != null)
        {
            AddMergeConnectors(line, lanes, currentLaneIndex, edges);
        }
        
        return line.ToString();
    }

    /// <summary>
    /// Generates connector lines that appear between commits (shows | characters for continuity)
    /// </summary>
    private static string GenerateConnectorLine(List<CommitId?> lanes)
    {
        var line = new StringBuilder();
        
        // Draw connectors for each lane
        for (int i = 0; i < lanes.Count; i++)
        {
            if (lanes[i] != null)
            {
                line.Append('|');
            }
            else
            {
                line.Append(' ');
            }
            
            // Add spacing between lanes (except for last lane)
            if (i < lanes.Count - 1)
            {
                line.Append(' ');
            }
        }
        
        return line.ToString();
    }

    /// <summary>
    /// Adds merge/fork connector characters (/ and \) to show branching
    /// </summary>
    private static void AddMergeConnectors(StringBuilder line, List<CommitId?> lanes, 
        int currentLaneIndex, IReadOnlyList<GraphEdge> edges)
    {
        // Add null check for edges
        if (edges == null || edges.Count <= 1)
            return;
            
        // For merge commits with multiple parents, add visual connectors
        for (int i = 1; i < edges.Count; i++)
        {
            var parentId = edges[i].Target;
            
            // Find lane for this parent (if it exists)
            int parentLane = -1;
            for (int j = 0; j < lanes.Count; j++)
            {
                if (lanes[j]?.Equals(parentId) == true)
                {
                    parentLane = j;
                    break;
                }
            }
            
            // Add connector line - simplified for now
            if (parentLane > currentLaneIndex)
            {
                line.Append(" \\");
            }
            else if (parentLane >= 0 && parentLane < currentLaneIndex)
            {
                line.Insert(parentLane * 2 + 1, '/');
            }
        }
    }

    /// <summary>
    /// Updates lanes after processing current commit
    /// </summary>
    private static void UpdateLanes(List<CommitId?> lanes, int currentLaneIndex, 
        IReadOnlyList<GraphEdge> edges)
    {
        // Add null check for edges
        if (edges == null)
        {
            // No parents (root commit), clear the lane
            if (currentLaneIndex < lanes.Count)
                lanes[currentLaneIndex] = null;
            return;
        }
        
        // Collect parent commits that need to be placed in lanes
        var parentsToAdd = new List<CommitId>();
        foreach (var edge in edges)
        {
            if (edge.Type != GraphEdgeType.Missing)
            {
                parentsToAdd.Add(edge.Target);
            }
        }
        
        // Place parent commits in lanes
        if (parentsToAdd.Count == 1)
        {
            // For single parent (linear history), keep it in the same lane
            // This maintains lane continuity for drawing connection lines
            lanes[currentLaneIndex] = parentsToAdd[0];
        }
        else if (parentsToAdd.Count > 1)
        {
            // For merge commits, place first parent in current lane, others in new lanes
            lanes[currentLaneIndex] = parentsToAdd[0];
            for (int i = 1; i < parentsToAdd.Count; i++)
            {
                FindOrAssignLane(lanes, parentsToAdd[i]);
            }
        }
        else
        {
            // No parents (root commit), clear the lane
            lanes[currentLaneIndex] = null;
        }
        
        // Clean up empty lanes at the end
        while (lanes.Count > 0 && lanes[lanes.Count - 1] == null)
        {
            lanes.RemoveAt(lanes.Count - 1);
        }
    }

    /// <summary>
    /// Formats commit information for display
    /// </summary>
    private static string FormatCommitLine(CommitData commit)
    {
        var commitId = ObjectHasher.ComputeCommitId(commit);
        var shortId = commitId.ToShortHexString();
        var description = commit.Description.Length > 50 
            ? commit.Description.Substring(0, 47) + "..." 
            : commit.Description;
        
        return $"{shortId} {description}";
    }
}