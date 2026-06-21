using HPD.VCS.Core;
using HPD.VCS.Storage;

namespace HPD.VCS.Graphing;

/// <summary>
/// Simple in-memory index that tracks commit generation numbers for topological sorting.
/// Generation number is the maximum distance from any root commit (commit with no parents).
/// </summary>
public interface IIndex
{
    Task<int> GetGenerationNumberAsync(CommitId commitId);
    Task BuildIndexAsync(IObjectStore objectStore, IEnumerable<CommitId> startingCommits);
}

public class InMemoryIndex : IIndex
{
    private readonly Dictionary<CommitId, int> _generationNumbers = new();
    private readonly object _lock = new();

    public Task<int> GetGenerationNumberAsync(CommitId commitId)
    {
        lock (_lock)
        {
            return Task.FromResult(_generationNumbers.TryGetValue(commitId, out var generation) ? generation : 0);
        }
    }

    public async Task BuildIndexAsync(IObjectStore objectStore, IEnumerable<CommitId> startingCommits)
    {
        var visited = new HashSet<CommitId>();
        var processing = new Queue<CommitId>();
        var tempGenerations = new Dictionary<CommitId, int>();

        // Start with all provided commits
        foreach (var commitId in startingCommits)
        {
            processing.Enqueue(commitId);
        }

        // First pass: collect all reachable commits
        var allCommits = new HashSet<CommitId>();
        while (processing.Count > 0)
        {
            var currentId = processing.Dequeue();
            if (visited.Contains(currentId))
                continue;

            visited.Add(currentId);
            allCommits.Add(currentId);

            var commitData = await objectStore.ReadCommitAsync(currentId);
            if (commitData.HasValue)
            {
                foreach (var parentId in commitData.Value.ParentIds)
                {
                    if (!visited.Contains(parentId))
                    {
                        processing.Enqueue(parentId);
                    }
                }
            }
        }

        // Second pass: calculate generation numbers
        // Start with commits that have no parents (generation 0)
        var rootCommits = new List<CommitId>();
        foreach (var commitId in allCommits)
        {
            var commitData = await objectStore.ReadCommitAsync(commitId);
            if (!commitData.HasValue || commitData.Value.ParentIds.Count == 0)
            {
                tempGenerations[commitId] = 0;
                rootCommits.Add(commitId);
            }
        }

        // Process commits in topological order to calculate generation numbers
        var processQueue = new Queue<CommitId>(rootCommits);
        var processedParents = new Dictionary<CommitId, HashSet<CommitId>>();

        while (processQueue.Count > 0)
        {
            var currentId = processQueue.Dequeue();
            
            // Find all children of this commit
            foreach (var potentialChildId in allCommits)
            {
                var childData = await objectStore.ReadCommitAsync(potentialChildId);
                if (childData.HasValue && childData.Value.ParentIds.Contains(currentId))
                {
                    // Track which parents have been processed
                    if (!processedParents.ContainsKey(potentialChildId))
                    {
                        processedParents[potentialChildId] = new HashSet<CommitId>();
                    }
                    processedParents[potentialChildId].Add(currentId);

                    // If all parents have been processed, calculate generation
                    if (processedParents[potentialChildId].Count == childData.Value.ParentIds.Count)
                    {
                        var maxParentGeneration = processedParents[potentialChildId]
                            .Max(parentId => tempGenerations[parentId]);
                        tempGenerations[potentialChildId] = maxParentGeneration + 1;
                        processQueue.Enqueue(potentialChildId);
                    }
                }
            }
        }

        // Update the index
        lock (_lock)
        {
            foreach (var (commitId, generation) in tempGenerations)
            {
                _generationNumbers[commitId] = generation;
            }
        }
    }
}
