using HPD.Agent.Authority;

namespace HPD.Agent.Runtime;

/// <summary>
/// Contains a validated, deterministic dependency order for neutral runtime participants.
/// </summary>
/// <remarks>
/// A plan is pure configuration. Constructing one does not prepare, start, or otherwise activate a participant.
/// </remarks>
public sealed class RuntimeParticipantPlanV1
{
    /// <summary>The maximum number of participants admitted to one runtime plan.</summary>
    public const int MaximumParticipants = 64;

    private RuntimeParticipantPlanV1(RuntimeParticipantDescriptorV1[] orderedDescriptors)
    {
        OrderedDescriptors = Array.AsReadOnly(orderedDescriptors);
    }

    /// <summary>Gets descriptors in deterministic dependency-first order.</summary>
    public IReadOnlyList<RuntimeParticipantDescriptorV1> OrderedDescriptors { get; }

    /// <summary>
    /// Compiles descriptors into a deterministic dependency-first plan without activating them.
    /// </summary>
    /// <param name="descriptors">The bounded set of immutable participant descriptors.</param>
    /// <returns>The validated participant plan.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="descriptors"/> is null.</exception>
    /// <exception cref="ArgumentException">
    /// A descriptor is null, an identifier is duplicated, a dependency is missing or self-referential,
    /// or the dependency graph contains a cycle.
    /// </exception>
    /// <exception cref="ArgumentOutOfRangeException">The plan is empty or exceeds 64 participants.</exception>
    public static RuntimeParticipantPlanV1 Compile(IEnumerable<RuntimeParticipantDescriptorV1> descriptors)
    {
        ArgumentNullException.ThrowIfNull(descriptors);

        var byId = new Dictionary<string, RuntimeParticipantDescriptorV1>(StringComparer.Ordinal);
        foreach (var descriptor in descriptors)
        {
            if (descriptor is null)
                throw new ArgumentException("A runtime participant descriptor cannot be null.", nameof(descriptors));
            if (byId.Count == MaximumParticipants)
                throw new ArgumentOutOfRangeException(nameof(descriptors), "A runtime participant plan cannot exceed 64 participants.");
            if (!byId.TryAdd(descriptor.Id.ToString(), descriptor))
                throw new ArgumentException("A runtime participant identifier is duplicated.", nameof(descriptors));
        }

        if (byId.Count == 0)
            throw new ArgumentOutOfRangeException(nameof(descriptors), "A runtime participant plan cannot be empty.");

        var dependentIds = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        var remainingDependencies = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var (id, descriptor) in byId)
        {
            remainingDependencies.Add(id, descriptor.Dependencies.Count);
            foreach (var dependency in descriptor.Dependencies)
            {
                var dependencyId = dependency.ToString();
                if (StringComparer.Ordinal.Equals(id, dependencyId))
                    throw new ArgumentException("A runtime participant cannot depend on itself.", nameof(descriptors));
                if (!byId.ContainsKey(dependencyId))
                    throw new ArgumentException("A runtime participant dependency is missing from the plan.", nameof(descriptors));
                if (!dependentIds.TryGetValue(dependencyId, out var dependents))
                    dependentIds.Add(dependencyId, dependents = []);
                dependents.Add(id);
            }
        }

        var ready = new SortedSet<string>(StringComparer.Ordinal);
        foreach (var (id, count) in remainingDependencies)
        {
            if (count == 0)
                ready.Add(id);
        }

        var ordered = new RuntimeParticipantDescriptorV1[byId.Count];
        var index = 0;
        while (ready.Count != 0)
        {
            var id = ready.Min!;
            ready.Remove(id);
            ordered[index++] = byId[id];

            if (!dependentIds.TryGetValue(id, out var dependents))
                continue;
            dependents.Sort(StringComparer.Ordinal);
            foreach (var dependentId in dependents)
            {
                var count = remainingDependencies[dependentId] - 1;
                remainingDependencies[dependentId] = count;
                if (count == 0)
                    ready.Add(dependentId);
            }
        }

        if (index != ordered.Length)
            throw new ArgumentException("The runtime participant dependency graph contains a cycle.", nameof(descriptors));

        return new RuntimeParticipantPlanV1(ordered);
    }
}
