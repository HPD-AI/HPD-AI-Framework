using HPD.Agent.Providers;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

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
    private readonly IAsyncDisposable? _owner;
    private int _leaseCount;
    private bool _closed;
    private bool _disposed;

    private AgentChatClientHandle(
        IChatClient client,
        AgentChatClientSource source,
        ProviderClientConfig? resolvedConfig,
        EffectiveProviderClientConfig? effectiveConfig,
        IAsyncDisposable? owner)
    {
        Client = client ?? throw new ArgumentNullException(nameof(client));
        Source = source;
        ResolvedConfig = resolvedConfig;
        EffectiveConfig = effectiveConfig;
        _owner = owner;
    }

    public IChatClient Client { get; }
    public AgentChatClientSource Source { get; }
    public ProviderClientConfig? ResolvedConfig { get; }
    public EffectiveProviderClientConfig? EffectiveConfig { get; }

    public static AgentChatClientHandle Borrowed(
        IChatClient client,
        AgentChatClientSource source,
        ProviderClientConfig? resolvedConfig = null) =>
        new(client, source, resolvedConfig, null, null);

    public static AgentChatClientHandle Owned(
        IChatClient client,
        IAsyncDisposable owner,
        AgentChatClientSource source,
        ProviderClientConfig? resolvedConfig = null,
        EffectiveProviderClientConfig? effectiveConfig = null) =>
        new(client, source, resolvedConfig, effectiveConfig, owner);

    public static AgentChatClientHandle Leased(
        IProviderClientLease<IChatClient> lease,
        AgentChatClientSource source,
        ProviderClientConfig? resolvedConfig,
        EffectiveProviderClientConfig effectiveConfig) =>
        new(lease.Client, source, resolvedConfig, effectiveConfig, lease);

    public AgentChatClientLease AcquireLease()
    {
        lock (_gate)
        {
            if (_closed) throw new InvalidOperationException("The chat-client handle is closed.");
            _leaseCount++;
            return new AgentChatClientLease(this);
        }
    }

    internal async ValueTask ReleaseAsync()
    {
        IAsyncDisposable? owner = null;
        lock (_gate)
        {
            if (_leaseCount <= 0) return;
            _leaseCount--;
            if (_leaseCount == 0 && !_disposed && _owner is not null)
            {
                _closed = true;
                _disposed = true;
                owner = _owner;
            }
        }
        if (owner is not null) await owner.DisposeAsync().ConfigureAwait(false);
    }
}

internal sealed class AgentChatClientLease : IAsyncDisposable
{
    private AgentChatClientHandle? _handle;
    internal AgentChatClientLease(AgentChatClientHandle handle) => _handle = handle;
    public AgentChatClientHandle Handle => _handle ?? throw new ObjectDisposedException(nameof(AgentChatClientLease));
    public IChatClient Client => Handle.Client;
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
    public ClientFamilyInheritanceMode SpecializedInheritance { get; init; } = ClientFamilyInheritanceMode.InheritResolved;
}

internal sealed class AgentChatClientResolver : IAsyncDisposable
{
    private readonly IProviderRegistry? _providers;
    private readonly ProviderComposition? _composition;
    private readonly IServiceProvider? _services;
    private readonly ProviderClientManager<IChatClient> _clients = new();
    private readonly object _overrideGate = new();
    private readonly Dictionary<ClientOverride<IChatClient>.Transferred, AgentChatClientLease> _agentOverrideRoots =
        new(ReferenceEqualityComparer.Instance);
    private int _disposed;

    internal ProviderComposition? Composition => _composition;
    internal IProviderRegistry? ProviderRegistry => _providers;

    public AgentChatClientResolver(IProviderRegistry? providerRegistry, IServiceProvider? services)
    {
        _providers = providerRegistry;
        _composition = (providerRegistry as ProviderRegistry)?.Composition;
        _services = services;
    }

    public async ValueTask<AgentChatClientLease> ResolveAsync(
        AgentChatClientResolutionRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);

        if (request.SpecializedChat is { } specialized)
        {
            if (specialized.Override is { } specializedOverride)
                return InstallOverride(specializedOverride, AgentChatClientSource.InjectedOverride, specialized);
            if (request.SpecializedInheritance == ClientFamilyInheritanceMode.InheritResolved &&
                specialized.Provider is null && request.ParentResolved is not null)
                return request.ParentResolved.AcquireLease();
            return await CreateProviderLeaseAsync(
                request.AgentConfig,
                new AgentClientsConfig { Chat = specialized },
                specialized,
                AgentChatClientSource.SpecializedRole,
                cancellationToken).ConfigureAwait(false);
        }

        if (request.RunConfig?.Clients.Chat?.Override is { } runOverride)
            return InstallOverride(runOverride, AgentChatClientSource.InjectedOverride, request.RunConfig.Clients.Chat);

        if (request.AgentConfig.Clients.Chat?.Override is { } agentOverride)
            return InstallOverride(
                agentOverride,
                AgentChatClientSource.InjectedOverride,
                request.AgentConfig.Clients.Chat);

        if (request.RunConfig?.Clients.Chat is { } runChat)
            return await CreateProviderLeaseAsync(
                request.AgentConfig,
                request.RunConfig.Clients,
                runChat,
                AgentChatClientSource.RuntimeProvider,
                cancellationToken).ConfigureAwait(false);

        if (request.ParentInheritance == ClientFamilyInheritanceMode.InheritResolved && request.ParentResolved is not null)
            return request.ParentResolved.AcquireLease();
        if (request.BuilderDefault is not null)
            return request.BuilderDefault.AcquireLease();

        try
        {
            return await CreateProviderLeaseAsync(
                request.AgentConfig,
                null,
                request.AgentConfig.Clients.Chat,
                AgentChatClientSource.BuilderDefault,
                cancellationToken).ConfigureAwait(false);
        }
        catch (AgentRunConfigurationException) when (
            request.ParentInheritance == ClientFamilyInheritanceMode.FallbackToParent &&
            request.ParentResolved is not null)
        {
            return request.ParentResolved.AcquireLease();
        }
    }

    public ValueTask DisposeAsync()
    {
        return Interlocked.Exchange(ref _disposed, 1) == 0
            ? DisposeCoreAsync()
            : ValueTask.CompletedTask;
    }

    private async ValueTask DisposeCoreAsync()
    {
        AgentChatClientLease[] roots;
        lock (_overrideGate)
        {
            roots = _agentOverrideRoots.Values.ToArray();
            _agentOverrideRoots.Clear();
        }
        foreach (var root in roots)
            await root.DisposeAsync().ConfigureAwait(false);
        await _clients.DisposeAsync().ConfigureAwait(false);
    }

    private AgentChatClientLease InstallOverride(
        ClientOverride<IChatClient> value,
        AgentChatClientSource source,
        ProviderClientConfig? config)
    {
        if (value is ClientOverride<IChatClient>.Transferred { Lifetime: RuntimeOverrideLifetime.Agent } agentTransfer)
        {
            lock (_overrideGate)
            {
                if (_agentOverrideRoots.TryGetValue(agentTransfer, out var existing))
                    return existing.Handle.AcquireLease();
                if (!agentTransfer.TryConsume())
                    throw new InvalidOperationException("A transferred client override can be installed exactly once.");
                var handle = AgentChatClientHandle.Owned(value.Client, agentTransfer.Owner, source, config);
                var root = handle.AcquireLease();
                _agentOverrideRoots.Add(agentTransfer, root);
                return handle.AcquireLease();
            }
        }

        return value switch
        {
            ClientOverride<IChatClient>.Borrowed =>
                AgentChatClientHandle.Borrowed(value.Client, source, config).AcquireLease(),
            ClientOverride<IChatClient>.Transferred transferred when transferred.TryConsume() =>
                AgentChatClientHandle.Owned(value.Client, transferred.Owner, source, config).AcquireLease(),
            ClientOverride<IChatClient>.Transferred =>
                throw new InvalidOperationException("A transferred client override can be installed exactly once."),
            _ => throw new ArgumentOutOfRangeException(nameof(value))
        };
    }

    private async ValueTask<AgentChatClientLease> CreateProviderLeaseAsync(
        AgentConfig agent,
        AgentClientsConfig? runClients,
        ProviderClientConfig? authoringConfig,
        AgentChatClientSource source,
        CancellationToken cancellationToken)
    {
        var composition = _composition ?? throw new AgentRunConfigurationException(
            "ProviderCompositionNotInstalled", "clients.chat",
            "Provider client resolution requires a generated provider composition.");
        var effective = new EffectiveProviderClientConfigResolver(composition)
            .Resolve(agent, ProviderClientFamily.Chat, runClients,
                AgentProviderProfileIndex.Create(agent, composition));
        var provider = composition.Runtime.GetFactory(
            effective.Provider.Backend.ProviderKey,
            effective.Provider.Backend.BackendKey,
            ProviderClientFamily.Chat).Factory();
        var factory = provider is CompositeProvider composite
            ? composite.GetTypedFamilyProvider<IProviderClientFactory<IChatClient>>(ProviderClientFamily.Chat)
            : provider as IProviderClientFactory<IChatClient>
                ?? throw new InvalidOperationException(
                    $"Provider '{effective.Provider.Backend.ProviderKey}' backend '{effective.Provider.Backend.BackendKey}' does not implement chat.");
        var validation = provider.ValidateConfiguration(effective);
        if (!validation.IsValid)
            throw new AgentRunConfigurationException(
                "ProviderConfigurationInvalid", "clients.chat",
                string.Join("; ", validation.Errors), effective.Provider.Backend.ProviderKey);

        var bindingDescriptor = new ProviderClientBindingDescriptor
        {
            EffectiveConfig = effective
        };
        var binding = factory.ResolveCredentialBinding(bindingDescriptor);
        var credentialSource = _services?.GetService<IProviderCredentialSource>()
            ?? throw new InvalidOperationException("IProviderCredentialSource is required for provider construction.");
        var scope = _services?.GetService<ProviderAuthorizationScope>()
            ?? new ProviderAuthorizationScope { TrustDomainId = "local-process" };
        var runAuthentication = runClients?.Chat?.Provider?.Authentication;
        if (runAuthentication is not null)
        {
            var authorizer = _services?.GetService<IProviderAuthenticationSelectionAuthorizer>();
            if (authorizer is null)
                throw new AgentRunConfigurationException(
                    "AuthenticationSelectionAuthorizerRequired",
                    "clients.chat.provider.authentication",
                    "Explicit per-run authentication references require a host selection authorizer.");
            await authorizer.AuthorizeAsync(new ProviderAuthenticationSelectionContext
            {
                Caller = new ProviderAuthorizationScopeSnapshot
                {
                    TrustDomainId = scope.TrustDomainId,
                    TenantId = scope.TenantId,
                    PrincipalId = scope.PrincipalId
                },
                Backend = effective.Provider.Backend,
                Family = ProviderClientFamily.Chat,
                Authentication = effective.Provider.Authentication,
                Source = ProviderSelectionSource.LocalRun
            }, cancellationToken).ConfigureAwait(false);
        }
        var plan = await credentialSource.PrepareAsync(new ProviderCredentialRequest
        {
            ProviderKey = effective.Provider.Backend.ProviderKey,
            BackendKey = effective.Provider.Backend.BackendKey,
            Family = ProviderClientFamily.Chat,
            Authentication = effective.Provider.Authentication.Configuration,
            AuthorizationScope = scope,
            Audience = factory.ResolveCredentialAudience(bindingDescriptor)
        }, cancellationToken).ConfigureAwait(false);
        var bindsModel = composition.Descriptors.TryGet(effective.Provider.Backend.ProviderKey, out var descriptor) &&
            descriptor!.Backends[effective.Provider.Backend.BackendKey].Families[ProviderClientFamily.Chat].BindsModelToClient;
        var runtimeServices = _services?.GetService<IProviderRuntimeServices>()
            ?? new DefaultProviderRuntimeServices(_services);

        if (binding == ProviderClientCredentialBinding.RequestTime)
        {
            var key = CreateKey(effective, plan, bindsModel,
                new ProviderClientCredentialCacheIdentity.RequestTime(
                    plan.StableCredentialIdentity, plan.Grant.GrantIdentity));
            var lease = await _clients.AcquireAsync(
                key,
                ct => factory.CreateAsync(CreateContext(
                    effective, plan, scope, runtimeServices,
                    new ProviderCredentialBindingContext.RequestTime(credentialSource, plan)), ct),
                cancellationToken).ConfigureAwait(false);
            return AgentChatClientHandle.Leased(lease, source, authoringConfig, effective).AcquireLease();
        }

        var credential = await credentialSource.AcquireAsync(plan, cancellationToken).ConfigureAwait(false);
        var transfer = new CredentialTransfer(credential);
        try
        {
            var key = CreateKey(effective, plan, bindsModel,
                new ProviderClientCredentialCacheIdentity.ConstructionTime(
                    plan.StableCredentialIdentity, credential.Generation));
            var lease = await _clients.AcquireAsync(
                key,
                async ct =>
                {
                    var exactCredential = transfer.Consume();
                    try
                    {
                        var created = await factory.CreateAsync(CreateContext(
                            effective, plan, scope, runtimeServices,
                            new ProviderCredentialBindingContext.ConstructionTime(plan, exactCredential)), ct).ConfigureAwait(false);
                        return new ProviderClientConstruction<IChatClient>
                        {
                            Client = created.Client,
                            Owner = new AggregateAsyncOwner(created.Owner, exactCredential)
                        };
                    }
                    catch
                    {
                        await exactCredential.DisposeAsync().ConfigureAwait(false);
                        throw;
                    }
                },
                cancellationToken).ConfigureAwait(false);
            await transfer.DisposeUnconsumedAsync().ConfigureAwait(false);
            return AgentChatClientHandle.Leased(lease, source, authoringConfig, effective).AcquireLease();
        }
        catch
        {
            await transfer.DisposeUnconsumedAsync().ConfigureAwait(false);
            throw;
        }
    }

    private static ProviderClientConstructionContext CreateContext(
        EffectiveProviderClientConfig effective,
        ProviderCredentialPlan plan,
        ProviderAuthorizationScope scope,
        IProviderRuntimeServices services,
        ProviderCredentialBindingContext binding) => new()
    {
        EffectiveConfig = effective,
        AuthorizationScope = new ProviderAuthorizationScopeSnapshot
        {
            TrustDomainId = scope.TrustDomainId,
            TenantId = scope.TenantId,
            PrincipalId = scope.PrincipalId
        },
        Grant = plan.Grant,
        CredentialBinding = binding,
        Lifetime = new ProviderComponentLifetimeContext(Lifetime: ProviderFamilyLifetime.ReusableClient),
        Services = services
    };

    private static ProviderClientCacheKey CreateKey(
        EffectiveProviderClientConfig effective,
        ProviderCredentialPlan plan,
        bool bindsModel,
        ProviderClientCredentialCacheIdentity credential) => new()
    {
        ProviderKey = effective.Provider.Backend.ProviderKey,
        BackendKey = effective.Provider.Backend.BackendKey,
        Family = effective.Family,
        ModelName = bindsModel ? effective.ModelName : null,
        Credential = credential,
        AuthorizationScopeIdentity = plan.AuthorizationScopeIdentity,
        EffectiveConfigurationFingerprint = effective.ConstructionFingerprint,
        ProviderManifestRevision = effective.ProviderManifestRevision
    };

    private sealed class CredentialTransfer(IProviderCredentialLease credential)
    {
        private IProviderCredentialLease? _credential = credential;
        internal IProviderCredentialLease Consume() =>
            Interlocked.Exchange(ref _credential, null)
            ?? throw new InvalidOperationException("The construction credential was already transferred.");
        internal ValueTask DisposeUnconsumedAsync() =>
            Interlocked.Exchange(ref _credential, null)?.DisposeAsync() ?? ValueTask.CompletedTask;
    }
}

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
        return await lease.Client.GetResponseAsync(messages, CompileOptions(options), cancellationToken).ConfigureAwait(false);
    }

    public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await using var lease = await AcquireAsync(cancellationToken).ConfigureAwait(false);
        await foreach (var update in lease.Client.GetStreamingResponseAsync(
            messages, CompileOptions(options), cancellationToken).ConfigureAwait(false))
            yield return update;
    }

    public object? GetService(Type serviceType, object? serviceKey = null) => null;
    public void Dispose() { }

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

internal sealed class DefaultProviderRuntimeServices : IProviderRuntimeServices
{
    internal DefaultProviderRuntimeServices(IServiceProvider? services)
    {
        LoggerFactory = services?.GetService<ILoggerFactory>() ?? NullLoggerFactory.Instance;
        HttpClientFactory = services?.GetService<IHttpClientFactory>() ?? new NewHttpClientFactory();
        TimeProvider = services?.GetService<TimeProvider>() ?? TimeProvider.System;
        Telemetry = services?.GetService<IProviderTelemetry>() ?? NullProviderTelemetry.Instance;
    }
    public ILoggerFactory LoggerFactory { get; }
    public IHttpClientFactory HttpClientFactory { get; }
    public TimeProvider TimeProvider { get; }
    public IProviderTelemetry Telemetry { get; }
    private sealed class NewHttpClientFactory : IHttpClientFactory
    { public HttpClient CreateClient(string name) => new(); }
    private sealed class NullProviderTelemetry : IProviderTelemetry
    { internal static NullProviderTelemetry Instance { get; } = new(); }
}

internal sealed class AggregateAsyncOwner(params IAsyncDisposable[] owners) : IAsyncDisposable
{
    private IAsyncDisposable[]? _owners = owners;
    public async ValueTask DisposeAsync()
    {
        var values = Interlocked.Exchange(ref _owners, null);
        if (values is null) return;
        List<Exception>? failures = null;
        foreach (var owner in values)
        {
            try { await owner.DisposeAsync().ConfigureAwait(false); }
            catch (Exception exception) { (failures ??= []).Add(exception); }
        }
        if (failures is not null) throw new AggregateException(failures);
    }
}
