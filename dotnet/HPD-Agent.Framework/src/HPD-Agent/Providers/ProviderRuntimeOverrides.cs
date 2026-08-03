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
