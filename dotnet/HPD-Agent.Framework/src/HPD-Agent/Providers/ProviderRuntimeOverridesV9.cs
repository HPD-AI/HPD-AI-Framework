namespace HPD.Agent.Providers;

/// <summary>Defines the runtime boundary that owns a transferred override.</summary>
public enum RuntimeOverrideLifetime
{
    /// <summary>The owning agent runtime.</summary>
    Agent,
    /// <summary>One agent run.</summary>
    Run,
    /// <summary>One stateful provider component.</summary>
    Component
}

/// <summary>A runtime-only borrowed or transferred provider client.</summary>
public abstract class ClientOverride<TClient> where TClient : class
{
    private protected ClientOverride(
        TClient client,
        string? providerKey,
        string? backendKey,
        string? operationAdapterKey)
    {
        Client = client ?? throw new ArgumentNullException(nameof(client));
        ProviderKey = providerKey;
        BackendKey = backendKey;
        OperationAdapterKey = operationAdapterKey;
    }

    /// <summary>Gets the runtime client.</summary>
    public TClient Client { get; }
    /// <summary>Gets the optional provider identity used for capability validation.</summary>
    public string? ProviderKey { get; }
    /// <summary>Gets the optional backend identity used for capability validation.</summary>
    public string? BackendKey { get; }
    /// <summary>Gets the optional generated operation-adapter identity.</summary>
    public string? OperationAdapterKey { get; }

    /// <summary>A client whose lifetime remains owned by the caller.</summary>
    public sealed class Borrowed : ClientOverride<TClient>
    {
        internal Borrowed(TClient client, string? providerKey, string? backendKey, string? operationAdapterKey)
            : base(client, providerKey, backendKey, operationAdapterKey) { }
    }

    /// <summary>A client whose owner transfers exactly once to an HPD runtime handle.</summary>
    public sealed class Transferred : ClientOverride<TClient>
    {
        private int _consumed;
        internal Transferred(
            TClient client,
            IAsyncDisposable owner,
            RuntimeOverrideLifetime lifetime,
            string? providerKey,
            string? backendKey,
            string? operationAdapterKey)
            : base(client, providerKey, backendKey, operationAdapterKey)
        {
            Owner = owner ?? throw new ArgumentNullException(nameof(owner));
            Lifetime = lifetime;
        }
        internal IAsyncDisposable Owner { get; }
        internal RuntimeOverrideLifetime Lifetime { get; }
        internal bool TryConsume() => Interlocked.Exchange(ref _consumed, 1) == 0;
    }

    /// <summary>Creates a borrowed runtime override.</summary>
    public static ClientOverride<TClient> Borrow(
        TClient client,
        string? providerKey = null,
        string? backendKey = null,
        string? operationAdapterKey = null) =>
        new Borrowed(client, providerKey, backendKey, operationAdapterKey);

    /// <summary>Creates an unconsumed single-transfer runtime override.</summary>
    public static ClientOverride<TClient> Transfer(
        TClient client,
        IAsyncDisposable owner,
        RuntimeOverrideLifetime lifetime,
        string? providerKey = null,
        string? backendKey = null,
        string? operationAdapterKey = null) =>
        new Transferred(client, owner, lifetime, providerKey, backendKey, operationAdapterKey);
}

/// <summary>Creates an owned provider component using the uniform asynchronous construction shape.</summary>
public sealed class ComponentFactoryOverride<TComponent> where TComponent : class
{
    /// <summary>Gets the runtime-only component construction factory.</summary>
    public required Func<ProviderComponentLifetimeContext, CancellationToken,
        ValueTask<ProviderClientConstruction<TComponent>>> Factory { get; init; }

    /// <summary>Gets the optional provider identity used for capability validation.</summary>
    public string? ProviderKey { get; init; }

    /// <summary>Gets the optional backend identity used for capability validation.</summary>
    public string? BackendKey { get; init; }

    /// <summary>Gets the optional generated operation-adapter identity.</summary>
    public string? OperationAdapterKey { get; init; }
}
