using System;

namespace HPD.Agent.Providers;

/// <summary>
/// Holds the source-generated provider composition for the current assembly.
/// <para>
/// A source-emitted <c>[ModuleInitializer]</c> (see <c>ProviderCompositionSourceGenerator</c>)
/// registers the assembly's closed composition here once at module load, so that
/// <c>new AgentBuilder()</c> can pick it up without any manual registration. This restores
/// implicit provider registration while remaining strict-Native-AOT-safe: discovery is fully
/// compile-time and there is no runtime reflection.
/// </para>
/// </summary>
public static class ProviderCompositionHost
{
    private static ProviderComposition? _current;
    private static readonly object _gate = new();

    /// <summary>Gets the composition registered by the first loaded provider module, or <c>null</c> when none.</summary>
    public static ProviderComposition? Current
    {
        get
        {
            lock (_gate) { return _current; }
        }
    }

    /// <summary>
    /// Registers the current assembly's generated composition. First registration wins so that
    /// when several referenced assemblies each carry a composition the resolution is deterministic.
    /// </summary>
    public static void Register(ProviderComposition composition)
    {
        ArgumentNullException.ThrowIfNull(composition);
        lock (_gate)
        {
            _current ??= composition;
        }
    }
}
