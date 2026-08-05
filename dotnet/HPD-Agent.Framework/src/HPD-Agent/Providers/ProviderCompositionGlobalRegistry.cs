using System;

namespace HPD.Agent.Providers;

/// <summary>
/// Global catalog of the source-generated provider composition, populated by a
/// generated module initializer. This lets a bare <c>new AgentBuilder()</c> (with no
/// DI wiring) discover the composed provider metadata — including secret aliases such as
/// <c>deepseek:ApiKey</c> → <c>DEEPSEEK_API_KEY</c> — for the referenced provider assemblies.
/// </summary>
public static class ProviderCompositionGlobalRegistry
{
    private static readonly object s_lock = new();
    private static ProviderComposition? s_composition;

    /// <summary>
    /// Installs the closed provider composition for the hosting application.
    /// Called by source-generated module initializers; idempotent if already set.
    /// </summary>
    public static void Register(ProviderComposition composition)
    {
        ArgumentNullException.ThrowIfNull(composition);
        lock (s_lock)
        {
            s_composition ??= composition;
        }
    }

    /// <summary>Gets the installed composition, or null if none has been registered.</summary>
    public static ProviderComposition? Current
    {
        get
        {
            lock (s_lock)
            {
                return s_composition;
            }
        }
    }

    /// <summary>Gets the secret-alias registry from the installed composition, if present.</summary>
    public static IProviderSecretAliasRegistry? SecretAliases => Current?.SecretAliases;

    internal static void ClearForTesting()
    {
        lock (s_lock)
        {
            s_composition = null;
        }
    }
}
