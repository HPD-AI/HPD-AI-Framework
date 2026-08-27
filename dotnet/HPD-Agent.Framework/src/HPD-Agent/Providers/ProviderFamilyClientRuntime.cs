using Microsoft.Extensions.DependencyInjection;

namespace HPD.Agent.Providers;

/// <summary>
/// Constructs any of the nine provider client families through the uniform asynchronous
/// authentication, validation, and ownership contract.
/// </summary>
public sealed class ProviderFamilyClientRuntime
{
    private readonly ProviderComposition _composition;
    private readonly IProviderRegistry _providers;
    private readonly IServiceProvider _services;

    /// <summary>Creates a family-neutral provider client runtime.</summary>
    /// <param name="composition">The immutable generated provider composition.</param>
    /// <param name="providers">The runtime provider registry.</param>
    /// <param name="services">Host services containing credential and authorization infrastructure.</param>
    public ProviderFamilyClientRuntime(
        ProviderComposition composition,
        IProviderRegistry providers,
        IServiceProvider services)
    {
        _composition = composition ?? throw new ArgumentNullException(nameof(composition));
        _providers = providers ?? throw new ArgumentNullException(nameof(providers));
        _services = services ?? throw new ArgumentNullException(nameof(services));
    }

    /// <summary>Resolves and constructs one owned provider-family client asynchronously.</summary>
    /// <typeparam name="TClient">The exact client or leaf-package product contract.</typeparam>
    /// <param name="agentConfig">The durable agent provider profiles and defaults.</param>
    /// <param name="family">The requested provider client family.</param>
    /// <param name="runClients">Optional portable per-run family selections.</param>
    /// <param name="source">The provenance used for mandatory selection authorization.</param>
    /// <param name="lifetime">The component lifetime required by the caller.</param>
    /// <param name="cancellationToken">A token that cancels resolution or construction.</param>
    /// <returns>The client and its exact asynchronous lifetime owner.</returns>
    public async ValueTask<ProviderClientConstruction<TClient>> CreateAsync<TClient>(
        AgentConfig agentConfig,
        ProviderClientFamily family,
        AgentClientsConfig? runClients = null,
        ProviderSelectionSource source = ProviderSelectionSource.BuilderLocal,
        ProviderComponentLifetimeContext? lifetime = null,
        CancellationToken cancellationToken = default)
        where TClient : class
    {
        ArgumentNullException.ThrowIfNull(agentConfig);
        var effective = new EffectiveProviderClientConfigResolver(_composition)
            .Resolve(agentConfig, family, runClients);
        var providerKey = effective.Provider.Backend.ProviderKey;
        var provider = _composition.Runtime.GetFactory(
            providerKey, effective.Provider.Backend.BackendKey, family).Factory();
        var factory = provider is CompositeProvider composite
            ? composite.GetTypedFamilyProvider<IProviderClientFactory<TClient>>(family)
            : provider as IProviderClientFactory<TClient>
                ?? throw new InvalidOperationException(
                    $"Provider '{providerKey}' does not implement the async factory for family '{family}'.");
        var validation = provider.ValidateConfiguration(effective);
        if (!validation.IsValid)
            throw new AgentRunConfigurationException(
                "ProviderConfigurationInvalid",
                $"clients.{family}",
                string.Join("; ", validation.Errors),
                providerKey);

        var scope = _services.GetService<ProviderAuthorizationScope>()
            ?? new ProviderAuthorizationScope { TrustDomainId = "local-process" };
        var runAuthentication = runClients?.GetFamilyConfig(family)?.Provider?.Authentication;
        if (source is not ProviderSelectionSource.BuilderLocal && runAuthentication is not null)
        {
            var authorizer = _services.GetService<IProviderAuthenticationSelectionAuthorizer>();
            if (authorizer is null)
                throw new AgentRunConfigurationException(
                    "AuthenticationSelectionAuthorizerRequired",
                    $"clients.{family}.provider.authentication",
                    "Explicit non-builder authentication references require a host selection authorizer.");
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
                Source = source
            }, cancellationToken).ConfigureAwait(false);
        }

        var credentialSource = _services.GetRequiredService<IProviderCredentialSource>();
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
        var binding = factory.ResolveCredentialBinding(new ProviderClientBindingDescriptor
        {
            EffectiveConfig = effective
        });
        IProviderCredentialLease? credential = null;
        try
        {
            ProviderCredentialBindingContext credentialBinding;
            if (binding == ProviderClientCredentialBinding.RequestTime)
            {
                credentialBinding = new ProviderCredentialBindingContext.RequestTime(credentialSource, plan);
            }
            else
            {
                credential = await credentialSource.AcquireAsync(plan, cancellationToken).ConfigureAwait(false);
                credentialBinding = new ProviderCredentialBindingContext.ConstructionTime(plan, credential);
            }
            var descriptor = _composition.Descriptors.TryGet(providerKey, out var found) && found is not null
                ? found.Backends[effective.Provider.Backend.BackendKey].Families[family]
                : throw new InvalidOperationException($"Provider descriptor '{providerKey}' is not registered.");
            if (lifetime is not null && lifetime.Lifetime != descriptor.Lifetime)
                throw new AgentRunConfigurationException(
                    "ProviderLifetimeMismatch",
                    $"clients.{family}.lifetime",
                    $"Provider family '{family}' requires lifetime '{descriptor.Lifetime}', not '{lifetime.Lifetime}'.",
                    providerKey);
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
                Lifetime = lifetime ?? new ProviderComponentLifetimeContext(Lifetime: descriptor.Lifetime),
                Services = _services.GetService<IProviderRuntimeServices>()
                    ?? new DefaultProviderRuntimeServices(_services)
            }, cancellationToken).ConfigureAwait(false);
            created = created with
            {
                Client = AddUsageAccounting(created.Client, family, providerKey, effective.ModelName)
            };
            if (credential is null)
                return created;
            var owner = new AggregateAsyncOwner(created.Owner, credential);
            credential = null;
            return new ProviderClientConstruction<TClient> { Client = created.Client, Owner = owner };
        }
        catch
        {
            if (credential is not null)
                await credential.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }

    private static TClient AddUsageAccounting<TClient>(
        TClient client,
        ProviderClientFamily family,
        string providerKey,
        string? modelId) where TClient : class
    {
        object accounted = family switch
        {
            ProviderClientFamily.Embeddings when client is Microsoft.Extensions.AI.IEmbeddingGenerator embeddings =>
                new UsageAccountingEmbeddingGenerator(
                    (Microsoft.Extensions.AI.IEmbeddingGenerator<string, Microsoft.Extensions.AI.Embedding<float>>)embeddings,
                    providerKey,
                    modelId),
            ProviderClientFamily.ImageGeneration when client is Microsoft.Extensions.AI.IImageGenerator images =>
                new UsageAccountingImageGenerator(images, providerKey, modelId),
            ProviderClientFamily.HostedFiles when client is Microsoft.Extensions.AI.IHostedFileClient files =>
                new UsageAccountingHostedFileClient(files, providerKey),
            _ => client
        };
        return (TClient)accounted;
    }
}
