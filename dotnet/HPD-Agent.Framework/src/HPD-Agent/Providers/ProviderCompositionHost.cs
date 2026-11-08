using System;
using System.Collections.Generic;
using System.ComponentModel;

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
    private static ProviderComposition? _fragmentComposition;
    private static readonly List<ProviderManifestFragment> _fragments = [];
    private static readonly object _gate = new();

    /// <summary>Gets the composition registered by the first loaded provider module, or <c>null</c> when none.</summary>
    public static ProviderComposition? Current
    {
        get
        {
            lock (_gate)
            {
                if (_current is not null)
                    return _current;
                if (_fragments.Count == 0)
                    return null;
                return _fragmentComposition ??= ProviderComposition.Create(_fragments);
            }
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

    /// <summary>
    /// Registers manifest fragments emitted by provider packages.
    /// </summary>
    /// <remarks>
    /// This fallback supports project-reference and file-based consumers where the application
    /// provider-composition generator cannot inspect referenced assembly attributes. A complete
    /// application composition registered through <see cref="Register"/> takes precedence.
    /// </remarks>
    [EditorBrowsable(EditorBrowsableState.Never)]
    public static void RegisterFragments(IEnumerable<ProviderManifestFragment> fragments)
    {
        ArgumentNullException.ThrowIfNull(fragments);
        lock (_gate)
        {
            foreach (var fragment in fragments)
            {
                ArgumentNullException.ThrowIfNull(fragment);
                _fragments.Add(fragment);
            }
            _fragmentComposition = null;
        }
    }
}
