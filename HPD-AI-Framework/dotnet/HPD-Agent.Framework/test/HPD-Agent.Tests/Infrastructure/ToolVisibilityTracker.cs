using Microsoft.Extensions.AI;

namespace HPD.Agent.Tests.Infrastructure;

/// <summary>
/// Tracks tool visibility changes across agent iterations.
/// Used for testing container expansion and Toolkit Collapsing.
/// </summary>
public sealed class ToolVisibilityTracker
{
    private readonly List<ToolSnapshot> _snapshots = new();
    private readonly object _lock = new();

    /// <summary>
    /// Represents the state of available tools at a specific iteration.
    /// </summary>
    public record ToolSnapshot(
        int Iteration,
        List<string> AvailableTools,
        List<string> Containers,
        List<string> RegularFunctions);

    /// <summary>
    /// Records the tools available at a specific iteration.
    /// </summary>
    public void RecordIteration(int iteration, IEnumerable<AIFunction> availableTools)
    {
        var toolList = availableTools.ToList();
        var toolNames = toolList.Select(t => t.Name ?? "").ToList();
        var containers = toolList.Where(CollapsedToolkitTestHelper.IsContainer).Select(t => t.Name ?? "").ToList();
        var regularFunctions = toolList.Where(t => !CollapsedToolkitTestHelper.IsContainer(t)).Select(t => t.Name ?? "").ToList();

        var snapshot = new ToolSnapshot(iteration, toolNames, containers, regularFunctions);

        lock (_lock)
        {
            _snapshots.Add(snapshot);
        }
    }

    /// <summary>
    /// Gets all recorded snapshots.
    /// </summary>
    public IReadOnlyList<ToolSnapshot> Snapshots
    {
        get
        {
            lock (_lock)
            {
                return _snapshots.ToList();
            }
        }
    }

    /// <summary>
    /// Gets the snapshot for a specific iteration.
    /// </summary>
    public ToolSnapshot? GetSnapshot(int iteration)
    {
        lock (_lock)
        {
            return _snapshots.FirstOrDefault(s => s.Iteration == iteration);
        }
    }

    /// <summary>
    /// Gets tools available at a specific iteration.
    /// </summary>
    public List<string> GetToolsAtIteration(int iteration)
    {
        var snapshot = GetSnapshot(iteration);
        return snapshot?.AvailableTools ?? new List<string>();
    }

    /// <summary>
    /// Checks if a specific tool was visible at a given iteration.
    /// </summary>
    public bool WasToolVisible(int iteration, string toolName)
    {
        var snapshot = GetSnapshot(iteration);
        return snapshot?.AvailableTools.Contains(toolName) ?? false;
    }

    /// <summary>
    /// Gets the iteration when a tool first became visible.
    /// Returns -1 if tool was never visible.
    /// </summary>
    public int GetFirstVisibleIteration(string toolName)
    {
        lock (_lock)
        {
            return _snapshots
                .Where(s => s.AvailableTools.Contains(toolName))
                .Select(s => s.Iteration)
                .DefaultIfEmpty(-1)
                .First();
        }
    }

    /// <summary>
    /// Gets the iteration when a tool disappeared (became invisible).
    /// Returns -1 if tool never disappeared.
    /// </summary>
    public int GetDisappearanceIteration(string toolName)
    {
        lock (_lock)
        {
            for (int i = 1; i < _snapshots.Count; i++)
            {
                var previous = _snapshots[i - 1];
                var current = _snapshots[i];

                if (previous.AvailableTools.Contains(toolName) && !current.AvailableTools.Contains(toolName))
                {
                    return current.Iteration;
                }
            }
            return -1;
        }
    }

    /// <summary>
    /// Clears all recorded snapshots.
    /// </summary>
    public void Clear()
    {
        lock (_lock)
        {
            _snapshots.Clear();
        }
    }

    /// <summary>
    /// Gets a summary of tool visibility changes.
    /// </summary>
    public string GetVisibilitySummary()
    {
        lock (_lock)
        {
            var sb = new System.Text.StringBuilder();
            sb.AppendLine("Tool Visibility Across Iterations:");
            sb.AppendLine();

            foreach (var snapshot in _snapshots)
            {
                sb.AppendLine($"Iteration {snapshot.Iteration}:");
                sb.AppendLine($"  Containers: {string.Join(", ", snapshot.Containers)}");
                sb.AppendLine($"  Functions: {string.Join(", ", snapshot.RegularFunctions)}");
                sb.AppendLine();
            }

            return sb.ToString();
        }
    }
}

/// <summary>
/// Extension methods for ToolVisibilityTracker assertions.
/// </summary>
public static class ToolVisibilityTrackerExtensions
{
    /// <summary>
    /// Asserts that a tool was visible at a specific iteration.
    /// </summary>
    public static void AssertToolVisible(this ToolVisibilityTracker tracker, int iteration, string toolName, string? because = null)
    {
        var visible = tracker.WasToolVisible(iteration, toolName);
        if (!visible)
        {
            var snapshot = tracker.GetSnapshot(iteration);
            var availableTools = snapshot?.AvailableTools ?? new List<string>();
            throw new Xunit.Sdk.XunitException(
                $"Expected tool '{toolName}' to be visible at iteration {iteration}{(because != null ? $" because {because}" : "")}. " +
                $"Available tools: [{string.Join(", ", availableTools)}]");
        }
    }

    /// <summary>
    /// Asserts that a tool was NOT visible at a specific iteration.
    /// </summary>
    public static void AssertToolHidden(this ToolVisibilityTracker tracker, int iteration, string toolName, string? because = null)
    {
        var visible = tracker.WasToolVisible(iteration, toolName);
        if (visible)
        {
            throw new Xunit.Sdk.XunitException(
                $"Expected tool '{toolName}' to be hidden at iteration {iteration}{(because != null ? $" because {because}" : "")}. " +
                $"But it was visible.");
        }
    }

    /// <summary>
    /// Asserts that a container disappeared (was replaced by its members) at a specific iteration.
    /// </summary>
    public static void AssertContainerExpanded(
        this ToolVisibilityTracker tracker,
        string containerName,
        int beforeIteration,
        int afterIteration,
        params string[] expectedMembers)
    {
        // Container should be visible before expansion
        tracker.AssertToolVisible(beforeIteration, containerName, "container should be visible before expansion");

        // Container should be hidden after expansion
        tracker.AssertToolHidden(afterIteration, containerName, "container should be hidden after expansion");

        // Member functions should be visible after expansion
        foreach (var member in expectedMembers)
        {
            tracker.AssertToolVisible(afterIteration, member, $"member '{member}' should be visible after expansion");
        }

        // Member functions should be hidden before expansion
        foreach (var member in expectedMembers)
        {
            tracker.AssertToolHidden(beforeIteration, member, $"member '{member}' should be hidden before expansion");
        }
    }
}
