using HPD.Agent.Providers;
using HPD.Agent.Secrets;
using Microsoft.Extensions.AI;
using System.Security.Cryptography;
using System.Text.Json;

namespace HPD.Agent;

internal enum AgentChatClientSource
{
    InjectedOverride,
    RuntimeProvider,
    BuilderDefault,
    ParentResolved,
    SpecializedRole
}

internal sealed class AgentChatClientHandle
{
    private readonly object _gate = new();
    private readonly bool _ownsClient;
    private readonly Func<ValueTask>? _release;
    private int _leaseCount;
    private bool _closed;
    private bool _disposed;

    private AgentChatClientHandle(
        IChatClient client,
        AgentChatClientSource source,
        ProviderClientConfig? resolvedConfig,
        bool ownsClient,
        Func<ValueTask>? release = null)
    {
        Client = client ?? throw new ArgumentNullException(nameof(client));
        Source = source;
        ResolvedConfig = resolvedConfig;
        _ownsClient = ownsClient;
        _release = release;
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

    public static AgentChatClientHandle Leased(
        IProviderClientLease<IChatClient> lease,
        AgentChatClientSource source,
        ProviderClientConfig? resolvedConfig = null)
        => new(lease.Client, source, resolvedConfig, ownsClient: false, lease.DisposeAsync);

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
        Func<ValueTask>? release = null;

        lock (_gate)
        {
            if (_leaseCount <= 0)
                return;

            _leaseCount--;
            if (_leaseCount == 0 && !_disposed && (_ownsClient || _release is not null))
            {
                _closed = true;
                _disposed = true;
                if (_ownsClient)
                    clientToDispose = Client;
                release = _release;
            }
        }

        if (clientToDispose is IAsyncDisposable asyncDisposable)
            await asyncDisposable.DisposeAsync().ConfigureAwait(false);
        else
            clientToDispose?.Dispose();
        if (release is not null)
            await release().ConfigureAwait(false);
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
    public AgentChatClientHandle? BuilderDefault { get; init; }
    public AgentChatClientHandle? ParentResolved { get; init; }
    public ClientFamilyInheritanceMode ParentInheritance { get; init; } = ClientFamilyInheritanceMode.UseOwn;
    public ChatClientConfig? SpecializedChat { get; init; }
    public ClientFamilyInheritanceMode SpecializedInheritance { get; init; } =
        ClientFamilyInheritanceMode.InheritResolved;
}

internal sealed class AgentChatClientResolver : IDisposable
{
    private readonly IProviderRegistry? _providerRegistry;
    private readonly IServiceProvider? _services;
    private readonly AgentProviderChatClientManager _clientManager;
    private readonly ProviderComposition? _composition;

    internal ProviderComposition? Composition => _composition;

    public AgentChatClientResolver(
        IProviderRegistry? providerRegistry,
        IServiceProvider? services,
        AgentClientMiddlewareConfig? middleware = null)
    {
        _providerRegistry = providerRegistry;
        _composition = (providerRegistry as ProviderRegistry)?.Composition;
        _services = services;
        _clientManager = new AgentProviderChatClientManager(providerRegistry, services, middleware?.Chat);
    }

    public async ValueTask<AgentChatClientLease> ResolveAsync(
        AgentChatClientResolutionRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        if (request.SpecializedChat is { } specialized)
        {
            if (specialized.Override is { } specializedOverride)
            {
                return AgentChatClientHandle
                    .Borrowed(specializedOverride.Client, AgentChatClientSource.InjectedOverride, specialized)
                    .AcquireLease();
            }

            var primary = request.SpecializedInheritance == ClientFamilyInheritanceMode.UseOwn
                ? null
                : request.RunConfig?.Clients.Chat is not null
                    ? request.AgentConfig.ResolveClientConfig(
                        ProviderClientFamily.Chat,
                        request.RunConfig.Clients) as ChatClientConfig
                    : request.BuilderDefault?.ResolvedConfig as ChatClientConfig
                        ?? request.AgentConfig.ResolveClientConfig(ProviderClientFamily.Chat) as ChatClientConfig;
            var specializedClients = new AgentClientsConfig { Chat = specialized };
            var primaryClients = primary is null ? null : new AgentClientsConfig { Chat = primary };
            var effective = ProviderClientConfigResolver.Resolve(
                    primaryClients,
                    ProviderClientFamily.Chat,
                    specializedClients)
                ?? throw new AgentRunConfigurationException(
                    "SpecializedChatResolutionFailed",
                    "Summarizer",
                    "The specialized Chat role did not resolve a usable configuration.");

            return await CreateProviderLeaseAsync(
                effective,
                AgentChatClientSource.SpecializedRole,
                cancellationToken).ConfigureAwait(false);
        }

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

        if (request.ParentInheritance == ClientFamilyInheritanceMode.InheritResolved &&
            request.ParentResolved is not null)
            return request.ParentResolved.AcquireLease();

        if (request.BuilderDefault is not null)
            return request.BuilderDefault.AcquireLease();

        var configuredDefault = request.AgentConfig.ResolveClientConfig(ProviderClientFamily.Chat);
        if (configuredDefault is not null &&
            !string.IsNullOrWhiteSpace(configuredDefault.ProviderKey))
        {
            return await CreateProviderLeaseAsync(
                configuredDefault,
                AgentChatClientSource.BuilderDefault,
                cancellationToken).ConfigureAwait(false);
        }

        if (request.ParentInheritance == ClientFamilyInheritanceMode.FallbackToParent &&
            request.ParentResolved is not null)
            return request.ParentResolved.AcquireLease();

        throw new InvalidOperationException(
            "No chat model is available for this invocation. Configure the agent, provide a runtime provider/model, supply an explicit client override, or invoke it from a parent with an inheritable chat client.");
    }

    private async ValueTask<AgentChatClientLease> CreateProviderLeaseAsync(
        ProviderClientConfig config,
        AgentChatClientSource source,
        CancellationToken cancellationToken)
    {
        var ownedConfig = ProviderClientConfigResolver.Clone(config);
        if (ownedConfig.ProviderConfig is not null)
        {
            var contract = RequirePayloadContract(
                ownedConfig.ProviderKey,
                ProviderClientFamily.Chat,
                ProviderPayloadKind.Configuration,
                ownedConfig.ProviderConfig,
                "Clients.Chat.ProviderConfig");
            ownedConfig.ProviderConfig = (IProviderConfig)contract.Snapshot(ownedConfig.ProviderConfig);
        }
        if (ownedConfig is ChatClientConfig { ProviderOptions: not null } chatConfig)
        {
            var contract = RequirePayloadContract(
                ownedConfig.ProviderKey,
                ProviderClientFamily.Chat,
                ProviderPayloadKind.OperationOptions,
                chatConfig.ProviderOptions,
                "Clients.Chat.ProviderOptions");
            chatConfig.ProviderOptions = (IChatRequestOptions)contract.Snapshot(chatConfig.ProviderOptions);
        }
        if (string.IsNullOrWhiteSpace(ownedConfig.ProviderKey))
            throw new InvalidOperationException("A chat provider key is required to resolve an invocation client.");

        if (string.IsNullOrWhiteSpace(ownedConfig.ModelName))
            throw new InvalidOperationException(
                $"No model is configured for provider '{ownedConfig.ProviderKey}'. Configure the agent client or the invocation override.");

        var resolvedConfig = await ResolveNamedAuthenticationAsync(ownedConfig, cancellationToken)
            .ConfigureAwait(false);
        var authenticationIdentity = !string.IsNullOrWhiteSpace(resolvedConfig.AuthenticationKey)
            ? $"registration:{resolvedConfig.AuthenticationKey}"
            : string.IsNullOrWhiteSpace(ownedConfig.ApiKey)
                ? "canonical"
                : null;
        var providerConfigFingerprint = GetProviderConfigFingerprint(resolvedConfig);
        return await _clientManager.AcquireAsync(
            resolvedConfig,
            authenticationIdentity,
            providerConfigFingerprint,
            source,
            cancellationToken).ConfigureAwait(false);
    }

    private ProviderPayloadJsonContract RequirePayloadContract(
        string? providerKey,
        ProviderClientFamily family,
        ProviderPayloadKind kind,
        object payload,
        string path)
    {
        if (_composition is null)
            throw new AgentRunConfigurationException(
                "ProviderCompositionNotInstalled",
                path,
                $"A generated provider composition is required to use the typed payload at '{path}'.",
                providerKey);

        _composition.ValidatePayload(providerKey, family, kind, payload, path);
        var canonical = _composition.Descriptors.Canonicalize(providerKey!);
        _composition.Serialization.TryGet(canonical, family, kind, out var contract);
        return contract!;
    }

    private string? GetProviderConfigFingerprint(ProviderClientConfig config)
    {
        if (config.ProviderConfig is null)
            return null;
        var contract = RequirePayloadContract(
            config.ProviderKey,
            ProviderClientFamily.Chat,
            ProviderPayloadKind.Configuration,
            config.ProviderConfig,
            "Clients.Chat.ProviderConfig");
        var bytes = JsonSerializer.SerializeToUtf8Bytes(config.ProviderConfig, contract.JsonTypeInfo);
        return Convert.ToHexString(SHA256.HashData(bytes));
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

/// <summary>
/// Adapts a specialized Chat role to MEAI while acquiring and releasing a normal
/// runtime-manager lease for each operation.
/// </summary>
internal sealed class AgentSpecializedChatClient(
    AgentChatClientResolver resolver,
    AgentConfig agentConfig,
    AgentRunConfig runConfig,
    AgentChatClientHandle? resolvedPrimary,
    ChatClientConfig? specialized,
    ClientFamilyInheritanceMode inheritance) : IChatClient
{
    public async Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        await using var lease = await AcquireAsync(cancellationToken).ConfigureAwait(false);
        return await lease.Client.GetResponseAsync(messages, CompileOptions(options), cancellationToken)
            .ConfigureAwait(false);
    }

    public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await using var lease = await AcquireAsync(cancellationToken).ConfigureAwait(false);
        await foreach (var update in lease.Client.GetStreamingResponseAsync(
            messages,
            CompileOptions(options),
            cancellationToken).ConfigureAwait(false))
        {
            yield return update;
        }
    }

    public object? GetService(Type serviceType, object? serviceKey = null) => null;

    public void Dispose()
    {
        // Resolver and borrowed clients belong to the hosting Agent.
    }

    private ValueTask<AgentChatClientLease> AcquireAsync(CancellationToken cancellationToken) =>
        resolver.ResolveAsync(new AgentChatClientResolutionRequest
        {
            AgentConfig = agentConfig,
            RunConfig = runConfig,
            BuilderDefault = resolvedPrimary,
            SpecializedChat = specialized,
            SpecializedInheritance = inheritance
        }, cancellationToken);

    private ChatOptions CompileOptions(ChatOptions? options)
    {
        var compiled = specialized?.MergeWith(options) ?? options?.Clone() ?? new ChatOptions();
        compiled.Tools = [];
        compiled.ToolMode = ChatToolMode.None;
        compiled.AllowMultipleToolCalls = false;
        return compiled;
    }
}

internal sealed class AgentProviderChatClientManager : IDisposable
{
    private readonly IProviderRegistry? _providerRegistry;
    private readonly IServiceProvider? _services;
    private readonly IReadOnlyList<Func<IChatClient, IServiceProvider?, IChatClient>>? _middleware;
    private readonly ProviderClientManager<IChatClient> _clients = new();
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
        string? providerConfigFingerprint,
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

        var key = new ProviderClientCacheKey
        {
            ProviderKey = config.ProviderKey,
            Family = ProviderClientFamily.Chat,
            AuthenticationIdentity = authenticationIdentity,
            Endpoint = config.Endpoint,
            ProviderConfigFingerprint = providerConfigFingerprint,
            ClientBoundModel = config.ModelName
        };
        var lease = await _clients.AcquireAsync(
            key,
            ct => CreateClientAsync(config, ct),
            cancellationToken).ConfigureAwait(false);
        return AgentChatClientHandle.Leased(lease, source, config).AcquireLease();
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
            return;

        _clients.DisposeAsync().AsTask().GetAwaiter().GetResult();
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

}
