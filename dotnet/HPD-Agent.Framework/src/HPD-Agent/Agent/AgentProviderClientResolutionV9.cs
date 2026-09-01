using HPD.Agent.Providers;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using System.Collections.Concurrent;

namespace HPD.Agent;

public sealed partial class Agent
{
    private async ValueTask<AgentClientSet?> ResolveRunClientSetV9Async(
        AgentRunConfig runConfig,
        CancellationToken cancellationToken)
    {
        var agent = Config ?? throw new InvalidOperationException("Agent configuration is not available.");
        var hasConfiguredFamily = Enum.GetValues<ProviderClientFamily>()
            .Where(static family => family is not ProviderClientFamily.Chat)
            .Any(family => runConfig.SubAgentClientInheritance?.GetMode(family) is not ClientFamilyInheritanceMode.UseOwn ||
                runConfig.Clients.GetFamilyConfig(family) is not null ||
                agent.Clients.GetFamilyConfig(family) is not null ||
                agent.ProviderDefaults.Any(value => value.Family == family));
        if (!hasConfiguredFamily)
            return null;
        var leases = new List<IAsyncDisposable>();
        var resolved = new ConcurrentDictionary<ProviderClientFamily, ProviderClientConfig>();
        var identities = new ConcurrentDictionary<ProviderClientFamily, ProviderClientExecutionIdentity>();
        var result = new AgentClientSet
        {
            ResolvedConfigs = resolved,
            ExecutionIdentities = identities
        };
        result.SetOwnedClients(new HashSet<object>(ReferenceEqualityComparer.Instance));
        result.SetLeases(leases);
        result.SetComponentInheritance(runConfig.SubAgentClientInheritance);
        result.SetFamilyResolver((family, token) => ResolveRequestedFamilyAsync(
            family, runConfig, leases, resolved, identities, token));
        return result;
    }

    private async ValueTask<object?> ResolveRequestedFamilyAsync(
        ProviderClientFamily family,
        AgentRunConfig runConfig,
        List<IAsyncDisposable> leases,
        ConcurrentDictionary<ProviderClientFamily, ProviderClientConfig> resolved,
        ConcurrentDictionary<ProviderClientFamily, ProviderClientExecutionIdentity> identities,
        CancellationToken cancellationToken) => family switch
    {
        ProviderClientFamily.TextToSpeech => await ResolveFamilyAsync(
            family, runConfig.SubAgentClientInheritance, runConfig.Clients.TextToSpeech,
            _clientSet?.TextToSpeech, Config!.ClientMiddleware?.TextToSpeech,
            _textToSpeechClientManager, leases, resolved, identities, cancellationToken).ConfigureAwait(false),
        ProviderClientFamily.SpeechToText => await ResolveFamilyAsync(
            family, runConfig.SubAgentClientInheritance, runConfig.Clients.SpeechToText,
            _clientSet?.SpeechToText, Config!.ClientMiddleware?.SpeechToText,
            _speechToTextClientManager, leases, resolved, identities, cancellationToken).ConfigureAwait(false),
        ProviderClientFamily.Realtime => await ResolveFamilyAsync(
            family, runConfig.SubAgentClientInheritance, runConfig.Clients.Realtime,
            _clientSet?.Realtime, Config!.ClientMiddleware?.Realtime,
            _realtimeClientManager, leases, resolved, identities, cancellationToken).ConfigureAwait(false),
        ProviderClientFamily.ImageGeneration => await ResolveFamilyAsync(
            family, runConfig.SubAgentClientInheritance, runConfig.Clients.ImageGeneration,
            _clientSet?.ImageGenerator, Config!.ClientMiddleware?.ImageGeneration,
            _imageGeneratorManager, leases, resolved, identities, cancellationToken).ConfigureAwait(false),
        ProviderClientFamily.Embeddings => await ResolveFamilyAsync(
            family, runConfig.SubAgentClientInheritance, runConfig.Clients.Embeddings,
            _clientSet?.EmbeddingGenerator, Config!.ClientMiddleware?.Embeddings,
            _embeddingGeneratorManager, leases, resolved, identities, cancellationToken).ConfigureAwait(false),
        ProviderClientFamily.HostedFiles => await ResolveFamilyAsync(
            family, runConfig.SubAgentClientInheritance, runConfig.Clients.HostedFiles,
            _clientSet?.HostedFiles, Config!.ClientMiddleware?.HostedFiles,
            _hostedFileClientManager, leases, resolved, identities, cancellationToken).ConfigureAwait(false),
        ProviderClientFamily.VoiceActivityDetection or ProviderClientFamily.EndOfTurnDetection => null,
        _ => throw new ArgumentOutOfRangeException(nameof(family))
    };

    private async ValueTask<TClient?> ResolveFamilyAsync<TClient>(
        ProviderClientFamily family,
        SubAgentClientInheritanceSource? inheritance,
        ProviderClientConfig? runConfig,
        TClient? builderDefault,
        IReadOnlyList<Func<TClient, IServiceProvider?, TClient>>? middleware,
        ProviderClientManager<TClient> manager,
        List<IAsyncDisposable> leases,
        ConcurrentDictionary<ProviderClientFamily, ProviderClientConfig> resolved,
        ConcurrentDictionary<ProviderClientFamily, ProviderClientExecutionIdentity> identities,
        CancellationToken cancellationToken)
        where TClient : class
    {
        var inheritanceMode = inheritance?.GetMode(family) ?? ClientFamilyInheritanceMode.UseOwn;
        if (inheritanceMode == ClientFamilyInheritanceMode.InheritResolved)
            return await GetRequiredParentClientAsync<TClient>(
                family, inheritance, resolved, identities, cancellationToken).ConfigureAwait(false);

        if (inheritanceMode == ClientFamilyInheritanceMode.FallbackToParent)
        {
            var hasOwnPlan = runConfig is not null || builderDefault is not null ||
                Config!.Clients.GetFamilyConfig(family) is not null ||
                Config.ProviderDefaults.Any(value => value.Family == family);
            if (!hasOwnPlan)
                return await GetRequiredParentClientAsync<TClient>(
                    family, inheritance, resolved, identities, cancellationToken).ConfigureAwait(false);
            try
            {
                return await ResolveOwnFamilyAsync().ConfigureAwait(false);
            }
            catch (AgentRunConfigurationException exception) when (
                exception.Code is "ProviderDefaultRequired" or "ProviderProfileRequired")
            {
                return await GetRequiredParentClientAsync<TClient>(
                    family, inheritance, resolved, identities, cancellationToken).ConfigureAwait(false);
            }
        }

        return await ResolveOwnFamilyAsync().ConfigureAwait(false);

        async ValueTask<TClient?> ResolveOwnFamilyAsync()
        {
        var runtimeOverride = GetOverride<TClient>(runConfig);
        if (runtimeOverride is not null)
            return InstallRunOverride(runtimeOverride, family, runConfig, leases, resolved, identities);
        var agentOverride = GetOverride<TClient>(Config!.Clients.GetFamilyConfig(family));
        if (agentOverride is not null)
            return InstallRunOverride(
                agentOverride, family, Config.Clients.GetFamilyConfig(family), leases, resolved, identities);
        if (runConfig is null && builderDefault is not null)
        {
            CopyBuilderSelection(family, resolved, identities);
            return builderDefault;
        }

        var agent = Config!;
        if (runConfig is null && agent.Clients.GetFamilyConfig(family) is null &&
            !agent.ProviderDefaults.Any(value => value.Family == family))
        {
            CopyBuilderSelection(family, resolved, identities);
            return builderDefault;
        }

        var composition = _chatClientResolver.Composition;
        if (composition is null || _providerRegistry is null)
            throw new AgentRunConfigurationException(
                "ProviderCompositionNotInstalled",
                $"clients.{family}",
                "Provider client resolution requires a generated provider composition.");
        var effective = new EffectiveProviderClientConfigResolver(composition)
            .Resolve(agent, family, runConfig is null ? null : CreateRunFamily(family, runConfig), _providerProfileIndex);
        var providerKey = effective.Provider.Backend.ProviderKey;
        var provider = composition.Runtime.GetFactory(
            providerKey, effective.Provider.Backend.BackendKey, family).Factory();
        var factory = provider is CompositeProvider composite
            ? composite.GetTypedFamilyProvider<IProviderClientFactory<TClient>>(family)
            : provider as IProviderClientFactory<TClient>
                ?? throw new InvalidOperationException(
                    $"Provider '{providerKey}' backend '{effective.Provider.Backend.BackendKey}' does not implement family '{family}'.");
        var validation = provider.ValidateConfiguration(effective);
        if (!validation.IsValid)
            throw new AgentRunConfigurationException(
                "ProviderConfigurationInvalid",
                $"clients.{family}",
                string.Join("; ", validation.Errors),
                providerKey);

        var bindingDescriptor = new ProviderClientBindingDescriptor
        {
            EffectiveConfig = effective
        };
        var binding = factory.ResolveCredentialBinding(bindingDescriptor);
        var credentialSource = _serviceProvider?.GetService<IProviderCredentialSource>()
            ?? throw new InvalidOperationException("IProviderCredentialSource is required for provider construction.");
        var scope = _serviceProvider?.GetService<ProviderAuthorizationScope>()
            ?? new ProviderAuthorizationScope { TrustDomainId = "local-process" };
        var runAuthentication = runConfig?.Provider?.Authentication;
        if (runAuthentication is not null)
        {
            var authorizer = _serviceProvider?.GetService<IProviderAuthenticationSelectionAuthorizer>();
            if (authorizer is null)
                throw new AgentRunConfigurationException(
                    "AuthenticationSelectionAuthorizerRequired",
                    $"clients.{family}.provider.authentication",
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
                Family = family,
                Authentication = effective.Provider.Authentication,
                Source = ProviderSelectionSource.LocalRun
            }, cancellationToken).ConfigureAwait(false);
        }
        var plan = await credentialSource.PrepareAsync(new ProviderCredentialRequest
        {
            ProviderKey = providerKey,
            BackendKey = effective.Provider.Backend.BackendKey,
            Family = family,
            Authentication = effective.Provider.Authentication.Configuration,
            AuthorizationScope = scope,
            Audience = factory.ResolveCredentialAudience(bindingDescriptor)
        }, cancellationToken).ConfigureAwait(false);
        if (!composition.Descriptors.TryGet(providerKey, out var providerDescriptor) || providerDescriptor is null)
            throw new InvalidOperationException($"Provider descriptor '{providerKey}' is not registered.");
        var backend = providerDescriptor.Backends[effective.Provider.Backend.BackendKey];
        var bindsModel = backend.Families[family].BindsModelToClient;
        var runtimeServices = _serviceProvider?.GetService<IProviderRuntimeServices>()
            ?? new DefaultProviderRuntimeServices(_serviceProvider);
        var construction = CreateConstructionFactory(
            factory, effective, plan, scope, runtimeServices, credentialSource, binding, middleware, _serviceProvider);
        var cacheCredential = binding == ProviderClientCredentialBinding.RequestTime
            ? new ProviderClientCredentialCacheIdentity.RequestTime(
                plan.StableCredentialIdentity,
                plan.Grant.GrantIdentity)
            : null;

        IProviderClientLease<TClient> lease;
        if (cacheCredential is not null)
        {
            lease = await manager.AcquireAsync(
                CreateCacheKey(effective, plan, bindsModel, cacheCredential),
                construction,
                cancellationToken).ConfigureAwait(false);
        }
        else
        {
            var credential = await credentialSource.AcquireAsync(plan, cancellationToken).ConfigureAwait(false);
            var transfer = new ProviderCredentialTransfer(credential);
            try
            {
                lease = await manager.AcquireAsync(
                    CreateCacheKey(effective, plan, bindsModel,
                        new ProviderClientCredentialCacheIdentity.ConstructionTime(
                            plan.StableCredentialIdentity,
                            credential.Generation)),
                    CreateConstructionFactory(
                        factory, effective, plan, scope, runtimeServices, credentialSource, binding, middleware,
                        _serviceProvider, transfer),
                    cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                await transfer.DisposeUnconsumedAsync().ConfigureAwait(false);
            }
        }

        lock (leases)
            leases.Add(lease);
        lock (resolved)
        {
        var authoring = runConfig ?? agent.Clients.GetFamilyConfig(family);
        if (authoring is not null)
            resolved[family] = ProviderClientConfigSnapshot.Clone(authoring);
        identities[family] = ProviderClientExecutionIdentity.CreateSafe(
            effective.Provider.Backend.ProviderKey,
            effective.Provider.Backend.BackendKey,
            family,
            effective.ModelName,
            $"{effective.Provider.Backend.ProviderKey}/{effective.Provider.Backend.BackendKey}/{family}",
            effective.Provider.Backend.ProviderKey);
        }
        return lease.Client;
        }
    }

    private void CopyBuilderSelection(
        ProviderClientFamily family,
        ConcurrentDictionary<ProviderClientFamily, ProviderClientConfig> resolved,
        ConcurrentDictionary<ProviderClientFamily, ProviderClientExecutionIdentity> identities)
    {
        lock (resolved)
        {
            if (_clientSet?.GetResolvedConfig(family) is { } config)
                resolved[family] = config;
            if (_clientSet?.GetExecutionIdentity(family) is { } identity)
                identities[family] = identity;
        }
    }

    private static async ValueTask<TClient> GetRequiredParentClientAsync<TClient>(
        ProviderClientFamily family,
        SubAgentClientInheritanceSource? inheritance,
        ConcurrentDictionary<ProviderClientFamily, ProviderClientConfig> resolved,
        ConcurrentDictionary<ProviderClientFamily, ProviderClientExecutionIdentity> identities,
        CancellationToken cancellationToken)
        where TClient : class
    {
        var parent = inheritance?.ParentClients;
        var client = parent is null ? null : await parent.ResolveFamilyAsync<TClient>(
            family, cancellationToken).ConfigureAwait(false);
        if (client is null)
            throw new AgentRunConfigurationException(
                "subagent_parent_client_unavailable",
                $"clients.{family}",
                $"The controlling execution has no resolved {family} client.");
        lock (resolved)
        {
            if (parent!.GetResolvedConfig(family) is { } config)
                resolved[family] = config;
            if (parent.GetExecutionIdentity(family) is { } identity)
                identities[family] = identity;
        }
        return client;
    }

    private static Func<CancellationToken, ValueTask<ProviderClientConstruction<TClient>>>
        CreateConstructionFactory<TClient>(
            IProviderClientFactory<TClient> factory,
            EffectiveProviderClientConfig effective,
            ProviderCredentialPlan plan,
            ProviderAuthorizationScope scope,
            IProviderRuntimeServices runtimeServices,
            IProviderCredentialSource credentialSource,
            ProviderClientCredentialBinding binding,
            IReadOnlyList<Func<TClient, IServiceProvider?, TClient>>? middleware,
            IServiceProvider? services,
            ProviderCredentialTransfer? transfer = null)
        where TClient : class => async cancellationToken =>
        {
            IProviderCredentialLease? exactCredential = null;
            try
            {
                ProviderCredentialBindingContext credentialBinding;
                if (binding == ProviderClientCredentialBinding.RequestTime)
                {
                    credentialBinding = new ProviderCredentialBindingContext.RequestTime(
                        credentialSource,
                        plan);
                }
                else
                {
                    exactCredential = transfer!.Consume();
                    credentialBinding = new ProviderCredentialBindingContext.ConstructionTime(plan, exactCredential);
                }
                var created = await factory.CreateAsync(new ProviderClientConstructionContext
                {
                    EffectiveConfig = effective,
                    AuthorizationScope = new ProviderAuthorizationScopeSnapshot
                    {
                        TrustDomainId = scope.TrustDomainId,
                        TenantId = scope.TenantId,
                        PrincipalId = scope.PrincipalId
                    },
                    Grant = plan.Grant,
                    CredentialBinding = credentialBinding,
                    Lifetime = new ProviderComponentLifetimeContext(Lifetime: ProviderFamilyLifetime.ReusableClient),
                    Services = runtimeServices
                }, cancellationToken).ConfigureAwait(false);
                var client = ApplyClientMiddleware(created.Client, middleware, services);
                return exactCredential is null
                    ? new ProviderClientConstruction<TClient> { Client = client, Owner = created.Owner }
                    : new ProviderClientConstruction<TClient>
                    {
                        Client = client,
                        Owner = new AggregateAsyncOwner(created.Owner, exactCredential)
                    };
            }
            catch
            {
                if (exactCredential is not null)
                    await exactCredential.DisposeAsync().ConfigureAwait(false);
                throw;
            }
        };

    private static TClient ApplyClientMiddleware<TClient>(
        TClient client,
        IReadOnlyList<Func<TClient, IServiceProvider?, TClient>>? middleware,
        IServiceProvider? services)
        where TClient : class
    {
        if (middleware is null)
            return client;
        var current = client;
        for (var index = middleware.Count - 1; index >= 0; index--)
            current = middleware[index](current, services)
                ?? throw new InvalidOperationException("Provider client middleware returned null.");
        return current;
    }

    private static ProviderClientCacheKey CreateCacheKey(
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

    private static AgentClientsConfig CreateRunFamily(
        ProviderClientFamily family,
        ProviderClientConfig config)
    {
        var clients = new AgentClientsConfig();
        clients.SetFamilyConfig(family, config);
        return clients;
    }

    private static ClientOverride<TClient>? GetOverride<TClient>(ProviderClientConfig? config)
        where TClient : class => config switch
    {
        TextToSpeechClientConfig value => value.Override as ClientOverride<TClient>,
        SpeechToTextClientConfig value => value.Override as ClientOverride<TClient>,
        RealtimeClientConfig value => value.Override as ClientOverride<TClient>,
        ImageGenerationClientConfig value => value.Override as ClientOverride<TClient>,
        EmbeddingsClientConfig value => value.Override as ClientOverride<TClient>,
        HostedFilesClientConfig value => value.Override as ClientOverride<TClient>,
        _ => null
    };

    private static TClient InstallRunOverride<TClient>(
        ClientOverride<TClient> runtimeOverride,
        ProviderClientFamily family,
        ProviderClientConfig? config,
        List<IAsyncDisposable> leases,
        ConcurrentDictionary<ProviderClientFamily, ProviderClientConfig> resolved,
        ConcurrentDictionary<ProviderClientFamily, ProviderClientExecutionIdentity> identities)
        where TClient : class
    {
        if (runtimeOverride is ClientOverride<TClient>.Transferred transferred)
        {
            if (transferred.Lifetime != RuntimeOverrideLifetime.Run)
                throw new InvalidOperationException("A per-run override must declare Run lifetime.");
            if (!transferred.TryConsume())
                throw new InvalidOperationException("A transferred client override can be installed exactly once.");
            lock (leases) leases.Add(transferred.Owner);
        }
        if (string.IsNullOrWhiteSpace(runtimeOverride.ProviderKey) ||
            string.IsNullOrWhiteSpace(runtimeOverride.BackendKey) ||
            string.IsNullOrWhiteSpace(runtimeOverride.OperationAdapterKey))
            throw new AgentRunConfigurationException(
                "subagent_provider_attribution_missing",
                $"clients.{family}",
                "A selected provider client override must declare provider, backend, and operation-adapter identity.");
        lock (resolved)
        {
            if (config is not null)
                resolved[family] = config;
            identities[family] = ProviderClientExecutionIdentity.CreateSafe(
                runtimeOverride.ProviderKey,
                runtimeOverride.BackendKey,
                family,
                config?.ModelName,
                runtimeOverride.OperationAdapterKey,
                runtimeOverride.ProviderKey);
        }
        return runtimeOverride.Client;
    }

    private sealed class ProviderCredentialTransfer(IProviderCredentialLease credential)
    {
        private IProviderCredentialLease? _credential = credential;

        internal IProviderCredentialLease Consume() =>
            Interlocked.Exchange(ref _credential, null)
            ?? throw new InvalidOperationException("The construction credential was already transferred.");

        internal ValueTask DisposeUnconsumedAsync() =>
            Interlocked.Exchange(ref _credential, null)?.DisposeAsync() ?? ValueTask.CompletedTask;
    }
}
