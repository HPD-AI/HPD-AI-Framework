namespace HPD.Agent.Providers;

/// <summary>Supplies a borrowed reusable client for one family.</summary>
/// <typeparam name="TClient">The closed client interface.</typeparam>
public sealed class ClientOverride<TClient> where TClient : class
{
    /// <summary>Gets the client borrowed for the operation.</summary>
    public required TClient Client { get; init; }

    /// <summary>Gets the optional canonical provider identity asserted by the caller.</summary>
    public string? ProviderKey { get; init; }

    /// <summary>Gets the generated provider operation-adapter identity.</summary>
    public string? OperationAdapterKey { get; init; }
}

/// <summary>Supplies a borrowed component factory while preserving provider lifecycle context.</summary>
/// <typeparam name="TComponent">The closed component interface.</typeparam>
public sealed class ComponentFactoryOverride<TComponent> where TComponent : class
{
    /// <summary>Gets the factory borrowed for the operation.</summary>
    public required Func<ProviderComponentLifetimeContext, TComponent> Factory { get; init; }

    /// <summary>Gets the optional canonical provider identity asserted by the caller.</summary>
    public string? ProviderKey { get; init; }

    /// <summary>Gets the generated provider operation-adapter identity.</summary>
    public string? OperationAdapterKey { get; init; }
}

/// <summary>
/// Captures one exact typed provider-family selection without exposing a
/// mutable registry or mutable configuration after the resolution cut.
/// </summary>
/// <typeparam name="TProvider">The leaf-owned provider-family contract.</typeparam>
public sealed class ResolvedProviderFamily<TProvider> where TProvider : class, IProvider
{
    private readonly ProviderClientConfig _configuration;

    internal ResolvedProviderFamily(
        TProvider provider,
        ProviderClientFamily family,
        ProviderClientConfig configuration,
        ProviderFamilyLifetime lifetime)
    {
        Provider = provider ?? throw new ArgumentNullException(nameof(provider));
        if (!Enum.IsDefined(family)) throw new ArgumentOutOfRangeException(nameof(family));
        ArgumentNullException.ThrowIfNull(configuration);
        if (!Enum.IsDefined(lifetime)) throw new ArgumentOutOfRangeException(nameof(lifetime));
        Family = family;
        _configuration = ProviderClientConfigResolver.Clone(configuration);
        Lifetime = lifetime;
    }

    /// <summary>Gets the exact provider selected by the existing registry.</summary>
    public TProvider Provider { get; }
    /// <summary>Gets the generated provider-family identity.</summary>
    public ProviderClientFamily Family { get; }
    /// <summary>Gets a fresh copy of the configuration captured at resolution.</summary>
    public ProviderClientConfig Configuration => ProviderClientConfigResolver.Clone(_configuration);
    /// <summary>Gets the generated provider lifecycle declaration.</summary>
    public ProviderFamilyLifetime Lifetime { get; }
}
