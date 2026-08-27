using HPD.Agent.Providers;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;

namespace HPD.Agent;

public sealed partial class Agent
{
    private async ValueTask<AgentClientSet?> ResolveRunClientSetV9Async(
        AgentRunConfig runConfig,
        CancellationToken cancellationToken)
    {
        var agent = Config ?? throw new InvalidOperationException("Agent configuration is not available.");
        var composition = _chatClientResolver.Composition;
        var hasConfiguredFamily = Enum.GetValues<ProviderClientFamily>()
            .Where(static family => family is not ProviderClientFamily.Chat)
            .Any(family => runConfig.Clients.GetFamilyConfig(family) is not null ||
                agent.Clients.GetFamilyConfig(family) is not null ||
                agent.ProviderDefaults.Any(value => value.Family == family));
        if (!hasConfiguredFamily)
            return null;
        var needsProviderComposition = Enum.GetValues<ProviderClientFamily>()
            .Where(static family => family is not ProviderClientFamily.Chat)
            .Any(family => NeedsProviderResolution(
                family,
                runConfig.Clients.GetFamilyConfig(family),
                agent.Clients.GetFamilyConfig(family),
                agent.ProviderDefaults));
        if (needsProviderComposition && (composition is null || _providerRegistry is null))
            throw new AgentRunConfigurationException(
                "ProviderCompositionNotInstalled",
                "clients",
                "Provider client resolution requires a generated provider composition.");

        var leases = new List<IAsyncDisposable>();
        var resolved = new Dictionary<ProviderClientFamily, ProviderClientConfig>();
        try
        {
            var textToSpeech = await ResolveFamilyAsync(
                ProviderClientFamily.TextToSpeech,
                runConfig.Clients.TextToSpeech,
                _clientSet?.TextToSpeech,
                Config.ClientMiddleware?.TextToSpeech,
                _textToSpeechClientManager,
                leases,
                resolved,
                cancellationToken).ConfigureAwait(false);
            var speechToText = await ResolveFamilyAsync(
                ProviderClientFamily.SpeechToText,
                runConfig.Clients.SpeechToText,
                _clientSet?.SpeechToText,
                Config.ClientMiddleware?.SpeechToText,
                _speechToTextClientManager,
                leases,
                resolved,
                cancellationToken).ConfigureAwait(false);
            var realtime = await ResolveFamilyAsync(
                ProviderClientFamily.Realtime,
                runConfig.Clients.Realtime,
                _clientSet?.Realtime,
                Config.ClientMiddleware?.Realtime,
                _realtimeClientManager,
                leases,
                resolved,
                cancellationToken).ConfigureAwait(false);
            var image = await ResolveFamilyAsync(
                ProviderClientFamily.ImageGeneration,
                runConfig.Clients.ImageGeneration,
                _clientSet?.ImageGenerator,
                Config.ClientMiddleware?.ImageGeneration,
                _imageGeneratorManager,
                leases,
                resolved,
                cancellationToken).ConfigureAwait(false);
            var embeddings = await ResolveFamilyAsync(
                ProviderClientFamily.Embeddings,
                runConfig.Clients.Embeddings,
                _clientSet?.EmbeddingGenerator,
                Config.ClientMiddleware?.Embeddings,
                _embeddingGeneratorManager,
                leases,
                resolved,
                cancellationToken).ConfigureAwait(false);
            var hostedFiles = await ResolveFamilyAsync(
                ProviderClientFamily.HostedFiles,
                runConfig.Clients.HostedFiles,
                _clientSet?.HostedFiles,
                Config.ClientMiddleware?.HostedFiles,
                _hostedFileClientManager,
                leases,
                resolved,
                cancellationToken).ConfigureAwait(false);

            var result = new AgentClientSet
            {
                TextToSpeech = textToSpeech,
                SpeechToText = speechToText,
                Realtime = realtime,
                ImageGenerator = image,
                EmbeddingGenerator = embeddings,
                HostedFiles = hostedFiles,
                ResolvedConfigs = resolved
            };
            result.SetLeases(leases);
            return result;
        }
        catch
        {
            for (var index = leases.Count - 1; index >= 0; index--)
                await leases[index].DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }

    private async ValueTask<TClient?> ResolveFamilyAsync<TClient>(
        ProviderClientFamily family,
        ProviderClientConfig? runConfig,
        TClient? builderDefault,
        IReadOnlyList<Func<TClient, IServiceProvider?, TClient>>? middleware,
        ProviderClientManager<TClient> manager,
        List<IAsyncDisposable> leases,
        Dictionary<ProviderClientFamily, ProviderClientConfig> resolved,
        CancellationToken cancellationToken)
        where TClient : class
    {
        var runtimeOverride = GetOverride<TClient>(runConfig);
        if (runtimeOverride is not null)
            return InstallRunOverride(runtimeOverride, leases);
        var agentOverride = GetOverride<TClient>(Config!.Clients.GetFamilyConfig(family));
        if (agentOverride is not null)
            return InstallRunOverride(agentOverride, leases);
        if (runConfig is null && builderDefault is not null)
            return builderDefault;

        var agent = Config!;
        if (runConfig is null && agent.Clients.GetFamilyConfig(family) is null &&
            !agent.ProviderDefaults.Any(value => value.Family == family))
            return builderDefault;

        var composition = _chatClientResolver.Composition!;
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

        var binding = factory.ResolveCredentialBinding(new ProviderClientBindingDescriptor
        {
            EffectiveConfig = effective
        });
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
            Audience = new ProviderCredentialAudience
            {
                Resource = effective.Endpoint,
                Scopes = effective.Provider.Authentication.Scopes
            }
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

        leases.Add(lease);
        var authoring = runConfig ?? agent.Clients.GetFamilyConfig(family);
        if (authoring is not null)
            resolved[family] = ProviderClientConfigSnapshot.Clone(authoring);
        return lease.Client;
    }

    private static bool NeedsProviderResolution(
        ProviderClientFamily family,
        ProviderClientConfig? runConfig,
        ProviderClientConfig? agentConfig,
        IEnumerable<AgentProviderFamilyDefault> defaults)
    {
        if (HasOverride(family, runConfig) || HasOverride(family, agentConfig))
            return false;

        return runConfig is not null || agentConfig is not null ||
            defaults.Any(value => value.Family == family);
    }

    private static bool HasOverride(ProviderClientFamily family, ProviderClientConfig? config) =>
        family switch
        {
            ProviderClientFamily.TextToSpeech => GetOverride<ITextToSpeechClient>(config) is not null,
            ProviderClientFamily.SpeechToText => GetOverride<ISpeechToTextClient>(config) is not null,
            ProviderClientFamily.Realtime => GetOverride<IRealtimeClient>(config) is not null,
            ProviderClientFamily.ImageGeneration => GetOverride<IImageGenerator>(config) is not null,
            ProviderClientFamily.Embeddings => GetOverride<IEmbeddingGenerator<string, Embedding<float>>>(config) is not null,
            ProviderClientFamily.HostedFiles => GetOverride<IHostedFileClient>(config) is not null,
            _ => false
        };

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
        List<IAsyncDisposable> leases)
        where TClient : class
    {
        if (runtimeOverride is ClientOverride<TClient>.Transferred transferred)
        {
            if (transferred.Lifetime != RuntimeOverrideLifetime.Run)
                throw new InvalidOperationException("A per-run override must declare Run lifetime.");
            if (!transferred.TryConsume())
                throw new InvalidOperationException("A transferred client override can be installed exactly once.");
            leases.Add(transferred.Owner);
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
