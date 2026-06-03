using HPD.Agent.Middleware;

namespace HPD.Agent;

/// <summary>
/// Global catalog populated by source-generated module initializers.
/// </summary>
public static class AgentGeneratedRegistry
{
    private static readonly object s_lock = new();
    private static readonly List<ToolHarnessFactory> s_toolharnesses = new();
    private static readonly List<MiddlewareFactory> s_middlewares = new();
    private static readonly List<MiddlewareStateFactory> s_states = new();

    /// <summary>
    /// Registers generated agent catalogs from an assembly.
    /// Called by source-generated module initializers.
    /// </summary>
    public static void Register(
        IEnumerable<ToolHarnessFactory>? toolharnesses = null,
        IEnumerable<MiddlewareFactory>? middlewares = null,
        IEnumerable<MiddlewareStateFactory>? states = null)
    {
        lock (s_lock)
        {
            if (toolharnesses is not null)
                s_toolharnesses.AddRange(toolharnesses);

            if (middlewares is not null)
                s_middlewares.AddRange(middlewares);

            if (states is not null)
                s_states.AddRange(states);
        }
    }

    internal static (ToolHarnessFactory[] ToolHarnesses, MiddlewareFactory[] Middlewares, MiddlewareStateFactory[] States) Snapshot()
    {
        lock (s_lock)
        {
            return (s_toolharnesses.ToArray(), s_middlewares.ToArray(), s_states.ToArray());
        }
    }

    internal static void ClearForTesting()
    {
        lock (s_lock)
        {
            s_toolharnesses.Clear();
            s_middlewares.Clear();
            s_states.Clear();
        }
    }
}
