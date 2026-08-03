using HPD.Agent.Providers;
using HPD.Agent.Secrets;
using Microsoft.Extensions.AI;
using System.Collections.Concurrent;

namespace HPD.Agent;

internal enum AgentChatClientSource
{
    InjectedOverride,
    RuntimeProvider,
    AgentDefault,
    InheritedFallback,
    DedicatedProvider
}

internal sealed class AgentChatClientHandle
{
    private readonly object _gate = new();
    private readonly bool _ownsClient;
    private int _leaseCount;
    private bool _closed;
    private bool _disposed;

    private AgentChatClientHandle(
        IChatClient client,
        AgentChatClientSource source,
        ProviderClientConfig? resolvedConfig,
        bool ownsClient)
    {
        Client = client ?? throw new ArgumentNullException(nameof(client));
        Source = source;
        ResolvedConfig = resolvedConfig;
        _ownsClient = ownsClient;
    }

    public IChatClient Client { get; }
    public AgentChatClientSource Source { get; }
    public ProviderClientConfig? ResolvedConfig { get; }

    public static AgentChatClientHandle Borrowed(
        IChatClient client,
        AgentChatClientSource source,
        ProviderClientConfig? resolvedConfig = null)
        => new(client, source, resolvedConfig, ownsClient: false);

    public static AgentChatClientHandle Owned(
        IChatClient client,
        AgentChatClientSource source,
        ProviderClientConfig? resolvedConfig = null)
        => new(client, source, resolvedConfig, ownsClient: true);

    public AgentChatClientLease AcquireLease()
    {
        lock (_gate)
        {
            if (_closed)
                throw new InvalidOperationException("The chat-client handle is closed and cannot issue another lease.");

            _leaseCount++;
            return new AgentChatClientLease(this);
        }
    }

    internal async ValueTask ReleaseAsync()
    {
        IChatClient? clientToDispose = null;

        lock (_gate)
        {
            if (_leaseCount <= 0)
                return;

            _leaseCount--;
            if (_ownsClient && _leaseCount == 0 && !_disposed)
            {
                _closed = true;
                _disposed = true;
                clientToDispose = Client;
            }
        }

        if (clientToDispose is IAsyncDisposable asyncDisposable)
            await asyncDisposable.DisposeAsync().ConfigureAwait(false);
        else
            clientToDispose?.Dispose();
    }
}

internal sealed class AgentChatClientLease : IAsyncDisposable, IDisposable
{
    private AgentChatClientHandle? _handle;

    internal AgentChatClientLease(AgentChatClientHandle handle)
    {
        _handle = handle;
    }

    public AgentChatClientHandle Handle
        => _handle ?? throw new ObjectDisposedException(nameof(AgentChatClientLease));

    public IChatClient Client => Handle.Client;

    public void Dispose()
        => DisposeAsync().AsTask().GetAwaiter().GetResult();

    public ValueTask DisposeAsync()
    {
        var handle = Interlocked.Exchange(ref _handle, null);
        return handle is null ? ValueTask.CompletedTask : handle.ReleaseAsync();
    }
}

internal sealed record AgentChatClientResolutionRequest
{
    public required AgentConfig AgentConfig { get; init; }
    public AgentRunConfig? RunConfig { get; init; }
    public AgentChatClientHandle? AgentDefault { get; init; }
    public AgentChatClientHandle? InheritedFallback { get; init; }
    public ProviderClientConfig? DedicatedProvider { get; init; }
}

internal sealed class AgentChatClientResolver : IDisposable
{
    private readonly IProviderRegistry? _providerRegistry;
    private readonly IServiceProvider? _services;
    private readonly AgentProviderChatClientManager _clientManager;

    public AgentChatClientResolver(
        IProviderRegistry? providerRegistry,
        IServiceProvider? services,
        AgentClientMiddlewareConfig? middleware = null)
    {
        _providerRegistry = providerRegistry;
        _services = services;
        _clientManager = new AgentProviderChatClientManager(providerRegistry, services, middleware?.Chat);
    }

    public async ValueTask<AgentChatClientLease> ResolveAsync(
        AgentChatClientResolutionRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        if (request.DedicatedProvider is { } dedicatedProvider)
            return await CreateProviderLeaseAsync(
                dedicatedProvider,
                AgentChatClientSource.DedicatedProvider,
                cancellationToken).ConfigureAwait(false);

        if (request.RunConfig?.Clients?.Chat?.Override is { } overrideClient)
        {
            return AgentChatClientHandle
                .Borrowed(overrideClient.Client, AgentChatClientSource.InjectedOverride)
                .AcquireLease();
        }

        var runClients = request.RunConfig?.Clients;
        if (runClients?.GetFamilyConfig(ProviderClientFamily.Chat) is not null)
        {
            var effectiveConfig = request.AgentConfig.ResolveClientConfig(
                ProviderClientFamily.Chat,
                runClients)
                ?? throw new InvalidOperationException("The runtime chat provider configuration could not be resolved.");

            return await CreateProviderLeaseAsync(
                effectiveConfig,
                AgentChatClientSource.RuntimeProvider,
                cancellationToken).ConfigureAwait(false);
        }

        if (request.AgentDefault is not null)
            return request.AgentDefault.AcquireLease();

        var configuredDefault = request.AgentConfig.ResolveClientConfig(ProviderClientFamily.Chat);
        if (configuredDefault is not null &&
            !string.IsNullOrWhiteSpace(configuredDefault.ProviderKey))
        {
            return await CreateProviderLeaseAsync(
                configuredDefault,
                AgentChatClientSource.AgentDefault,
                cancellationToken).ConfigureAwait(false);
        }

        if (request.InheritedFallback is not null)
            return request.InheritedFallback.AcquireLease();

        throw new InvalidOperationException(
            "No chat model is available for this invocation. Configure the agent, provide a runtime provider/model, supply an explicit client override, or invoke it from a parent with an inheritable chat client.");
    }

    private async ValueTask<AgentChatClientLease> CreateProviderLeaseAsync(
        ProviderClientConfig config,
        AgentChatClientSource source,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(config.ProviderKey))
            throw new InvalidOperationException("A chat provider key is required to resolve an invocation client.");

        if (string.IsNullOrWhiteSpace(config.ModelName))
            throw new InvalidOperationException(
                $"No model is configured for provider '{config.ProviderKey}'. Configure the agent client or the invocation override.");

        var resolvedConfig = await ResolveNamedAuthenticationAsync(config, cancellationToken)
            .ConfigureAwait(false);
        var authenticationIdentity = !string.IsNullOrWhiteSpace(resolvedConfig.AuthenticationKey)
            ? $"registration:{resolvedConfig.AuthenticationKey}"
            : string.IsNullOrWhiteSpace(config.ApiKey)
                ? "canonical"
                : null;
        return await _clientManager.AcquireAsync(
            resolvedConfig,
            authenticationIdentity,
            source,
            cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Disposes provider clients cached by this resolver.</summary>
    public void Dispose() => _clientManager.Dispose();

    private async ValueTask<ProviderClientConfig> ResolveNamedAuthenticationAsync(
        ProviderClientConfig config,
        CancellationToken cancellationToken)
    {
        RejectAuthenticationHeaders(config);
        var authenticationRegistry = _services?.GetService(typeof(IProviderAuthenticationRegistry))
            as IProviderAuthenticationRegistry;
        var authenticationKey = config.AuthenticationKey;
        if (string.IsNullOrWhiteSpace(authenticationKey))
        {
            if (authenticationRegistry is null)
                return config;

            var compatible = new List<ProviderAuthenticationRegistration>();
            await foreach (var candidate in authenticationRegistry.ListCompatibleAsync(
                new ProviderAuthenticationContext
                {
                    ProviderKey = config.ProviderKey,
                    Family = ProviderClientFamily.Chat
                },
                cancellationToken).ConfigureAwait(false))
            {
                compatible.Add(candidate);
            }

            var defaults = compatible.Where(static item => item.IsDefault).ToArray();
            if (defaults.Length > 1 || (defaults.Length == 0 && compatible.Count > 1))
            {
                throw new InvalidOperationException(
                    $"AuthenticationSelectionRequired: provider '{config.ProviderKey}' has multiple compatible authentication registrations and no unique host default.");
            }

            if (defaults.Length == 0)
                return config;

            authenticationKey = defaults[0].Key;
        }
        else if (authenticationRegistry is null)
        {
            throw new InvalidOperationException(
                $"Authentication registration '{authenticationKey}' cannot be resolved because no {nameof(IProviderAuthenticationRegistry)} is available.");
        }

        var registration = await authenticationRegistry.FindAsync(
            authenticationKey,
            new ProviderAuthenticationContext
            {
                ProviderKey = config.ProviderKey,
                Family = ProviderClientFamily.Chat
            },
            cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException(
                $"Authentication registration '{authenticationKey}' was not found or is not compatible with provider '{config.ProviderKey}' and family '{ProviderClientFamily.Chat}'.");

        var secretResolver = _services?.GetService(typeof(ISecretResolver)) as ISecretResolver
            ?? throw new InvalidOperationException(
                $"Authentication registration '{authenticationKey}' cannot resolve its secret because no {nameof(ISecretResolver)} is available.");
        var secret = await secretResolver.ResolveAsync(registration.SecretKey, cancellationToken)
            .ConfigureAwait(false)
            ?? throw new InvalidOperationException(
                $"Authentication registration '{authenticationKey}' did not resolve secret key '{registration.SecretKey}'.");

        var resolved = ProviderClientConfigResolver.Clone(config);
        resolved.AuthenticationKey = authenticationKey;
        resolved.ApiKey = secret.Value;
        return resolved;
    }

    private static void RejectAuthenticationHeaders(ProviderClientConfig config)
    {
        if (config.CustomHeaders is null)
            return;

        foreach (var header in config.CustomHeaders.Keys)
        {
            if (header.Equals("Authorization", StringComparison.OrdinalIgnoreCase) ||
                header.Equals("Proxy-Authorization", StringComparison.OrdinalIgnoreCase) ||
                header.Equals("api-key", StringComparison.OrdinalIgnoreCase) ||
                header.Equals("x-api-key", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    $"Header '{header}' cannot be used for provider authentication. Use ApiKey or AuthenticationKey.");
            }
        }
    }

}

internal sealed class AgentProviderChatClientManager : IDisposable
{
    private readonly IProviderRegistry? _providerRegistry;
    private readonly IServiceProvider? _services;
    private readonly IReadOnlyList<Func<IChatClient, IServiceProvider?, IChatClient>>? _middleware;
    private readonly ConcurrentDictionary<ChatClientCacheKey, Lazy<Task<IChatClient>>> _clients = new();
    private int _disposed;

    public AgentProviderChatClientManager(
        IProviderRegistry? providerRegistry,
        IServiceProvider? services,
        IReadOnlyList<Func<IChatClient, IServiceProvider?, IChatClient>>? middleware)
    {
        _providerRegistry = providerRegistry;
        _services = services;
        _middleware = middleware;
    }

    public async ValueTask<AgentChatClientLease> AcquireAsync(
        ProviderClientConfig config,
        string? authenticationIdentity,
        AgentChatClientSource source,
        CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        cancellationToken.ThrowIfCancellationRequested();

        if (authenticationIdentity is null)
        {
            var uncached = await CreateClientAsync(config, cancellationToken).ConfigureAwait(false);
            return AgentChatClientHandle.Owned(uncached, source, config).AcquireLease();
        }

        var key = ChatClientCacheKey.Create(config, authenticationIdentity);
        var lazy = _clients.GetOrAdd(
            key,
            _ => new Lazy<Task<IChatClient>>(
                // Cached construction belongs to the resolver, not whichever caller
                // happened to win the race to populate the cache.
                () => CreateClientAsync(config, CancellationToken.None).AsTask(),
                LazyThreadSafetyMode.ExecutionAndPublication));

        try
        {
            var client = await lazy.Value.WaitAsync(cancellationToken).ConfigureAwait(false);
            return AgentChatClientHandle.Borrowed(client, source, config).AcquireLease();
        }
        catch
        {
            // A caller abandoning its wait must not evict construction shared by
            // other invocations. Only the construction task itself poisons a key.
            if (lazy.IsValueCreated && lazy.Value.IsCompleted && !lazy.Value.IsCompletedSuccessfully)
                _clients.TryRemove(key, out _);
            throw;
        }
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
            return;

        var disposed = new HashSet<object>(ReferenceEqualityComparer.Instance);
        foreach (var lazy in _clients.Values)
        {
            if (!lazy.IsValueCreated)
                continue;

            var client = lazy.Value.GetAwaiter().GetResult();
            if (!disposed.Add(client))
                continue;

            client.Dispose();
        }

        _clients.Clear();
    }

    private ValueTask<IChatClient> CreateClientAsync(
        ProviderClientConfig config,
        CancellationToken cancellationToken)
    {
        var registry = _providerRegistry
            ?? throw new InvalidOperationException(
                $"Cannot resolve chat provider '{config.ProviderKey}' because no provider registry is available.");
        var provider = registry.GetRequiredProvider<IChatClientProvider>(config.ProviderKey);
        var validation = provider.ValidateConfiguration(config, ProviderClientFamily.Chat);
        if (!validation.IsValid)
        {
            throw new InvalidOperationException(
                $"Provider configuration for '{config.ProviderKey}' is invalid: {string.Join("; ", validation.Errors)}");
        }
        return CreateAndWrapAsync(provider, config, cancellationToken);
    }

    private async ValueTask<IChatClient> CreateAndWrapAsync(
        IChatClientProvider provider,
        ProviderClientConfig config,
        CancellationToken cancellationToken)
    {
        var client = await provider.CreateChatClientAsync(config, _services, cancellationToken)
            .ConfigureAwait(false);
        if (_middleware is null)
            return client;

        for (var index = _middleware.Count - 1; index >= 0; index--)
        {
            client = _middleware[index](client, _services)
                ?? throw new InvalidOperationException("Chat client middleware returned null.");
        }

        return client;
    }

    private readonly record struct ChatClientCacheKey(
        string ProviderKey,
        string ModelName,
        string AuthenticationIdentity,
        string? Endpoint,
        string? ConstructionOptions,
        string? Headers,
        int? MaxOutputTokens)
    {
        public static ChatClientCacheKey Create(
            ProviderClientConfig config,
            string authenticationIdentity)
            => new(
                config.ProviderKey,
                config.ModelName,
                authenticationIdentity,
                config.Endpoint,
                config.GetConstructionOptionsRawJson(),
                NormalizeHeaders(config.CustomHeaders),
                (config as ChatClientConfig)?.MaxOutputTokens);

        private static string? NormalizeHeaders(IReadOnlyDictionary<string, string>? headers)
            => headers is null
                ? null
                : string.Join(
                    "\n",
                    headers.OrderBy(static pair => pair.Key, StringComparer.OrdinalIgnoreCase)
                        .Select(static pair => $"{pair.Key}:{pair.Value}"));
    }
}
