using HPD.Agent.Middleware;

namespace HPD.Agent;

/// <summary>
/// Global catalog populated by source-generated module initializers.
/// </summary>
public static class AgentGeneratedRegistry
{
    private static readonly object s_lock = new();
    private static readonly List<HarnessFactory> s_harneses = new();
    private static readonly List<MiddlewareFactory> s_middlewares = new();
    private static readonly List<MiddlewareStateFactory> s_states = new();

    /// <summary>
    /// Registers generated agent catalogs from an assembly.
    /// Called by source-generated module initializers.
    /// </summary>
    public static void Register(
        IEnumerable<HarnessFactory>? harneses = null,
        IEnumerable<MiddlewareFactory>? middlewares = null,
        IEnumerable<MiddlewareStateFactory>? states = null)
    {
        lock (s_lock)
        {
            if (harneses is not null)
                s_harneses.AddRange(harneses);

            if (middlewares is not null)
                s_middlewares.AddRange(middlewares);

            if (states is not null)
                s_states.AddRange(states);
        }
    }

    internal static (HarnessFactory[] Harneses, MiddlewareFactory[] Middlewares, MiddlewareStateFactory[] States) Snapshot()
    {
        lock (s_lock)
        {
            return (s_harneses.ToArray(), s_middlewares.ToArray(), s_states.ToArray());
        }
    }

    internal static void ClearForTesting()
    {
        lock (s_lock)
        {
            s_harneses.Clear();
            s_middlewares.Clear();
            s_states.Clear();
        }
    }
}
