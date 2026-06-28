namespace HPD.Agent;

/// <summary>
/// Registers optional package-owned agent features that can participate in agent build.
/// </summary>
public static class AgentFeatureActivatorRegistry
{
    private static readonly object s_lock = new();
    private static readonly Dictionary<string, IAgentBuilderContributor> s_activators =
        new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Registers a named build activator. Later registrations with the same name replace earlier ones.
    /// </summary>
    public static void Register(string name, Action<AgentBuilder> activate)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(activate);

        Register(name, new DelegateAgentBuilderContributor(activate));
    }

    public static void Register(string name, IAgentBuilderContributor contributor)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(contributor);

        lock (s_lock)
        {
            s_activators[name] = contributor;
        }
    }

    internal static IReadOnlyList<AgentFeatureActivatorContribution> Snapshot()
    {
        lock (s_lock)
        {
            return s_activators
                .Select(pair => new AgentFeatureActivatorContribution(
                    pair.Key,
                    pair.Value,
                    new HpdContributionOwner(pair.Key, "framework-feature", DisplayName: pair.Key)))
                .ToArray();
        }
    }

    internal static void ClearForTesting()
    {
        lock (s_lock)
        {
            s_activators.Clear();
        }
    }
}

internal sealed record AgentFeatureActivatorContribution(
    string Key,
    IAgentBuilderContributor Contributor,
    HpdContributionOwner Owner);
