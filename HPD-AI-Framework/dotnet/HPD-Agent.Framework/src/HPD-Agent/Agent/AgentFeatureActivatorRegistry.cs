namespace HPD.Agent;

/// <summary>
/// Registers optional package-owned agent features that can participate in agent build.
/// </summary>
public static class AgentFeatureActivatorRegistry
{
    private static readonly object s_lock = new();
    private static readonly Dictionary<string, Action<AgentBuilder>> s_activators =
        new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Registers a named build activator. Later registrations with the same name replace earlier ones.
    /// </summary>
    public static void Register(string name, Action<AgentBuilder> activate)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(activate);

        lock (s_lock)
        {
            s_activators[name] = activate;
        }
    }

    internal static Action<AgentBuilder>[] Snapshot()
    {
        lock (s_lock)
        {
            return s_activators.Values.ToArray();
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
