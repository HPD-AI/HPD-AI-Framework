using HPD.Agent.ErrorHandling;
using Microsoft.Extensions.AI;

namespace HPD.Agent.Providers;

internal sealed class CompositeProvider :
    IProvider,
    IProviderClientFactory<IChatClient>,
    IProviderClientFactory<ITextToSpeechClient>,
    IProviderClientFactory<ISpeechToTextClient>,
    IProviderClientFactory<IRealtimeClient>,
    IProviderClientFactory<IImageGenerator>,
    IProviderClientFactory<IEmbeddingGenerator>,
    IProviderClientFactory<IHostedFileClient>
{
    private readonly List<IProvider> _providers = [];

    public CompositeProvider(IProvider first, IProvider second)
    {
        Add(first);
        Add(second);
    }

    public string ProviderKey => _providers[0].ProviderKey;
    public string DisplayName => _providers[0].DisplayName;

    public void Add(IProvider provider)
    {
        for (var i = 0; i < _providers.Count; i++)
        {
            if (_providers[i].GetType() == provider.GetType())
            {
                _providers[i] = provider;
                return;
            }
        }

        _providers.Add(provider);
    }

    public bool Supports<TProvider>()
        where TProvider : class
        => _providers.Any(static provider => provider is TProvider);

    internal TProvider GetTypedFamilyProvider<TProvider>(ProviderClientFamily family)
        where TProvider : class
    {
        for (var i = _providers.Count - 1; i >= 0; i--)
        {
            if (_providers[i] is TProvider provider &&
                _providers[i].GetMetadata().Families.ContainsKey(family))
                return provider;
        }

        throw new InvalidOperationException(
            $"Provider '{ProviderKey}' is registered, but it does not implement '{typeof(TProvider).Name}' " +
            $"for client family '{family}'.");
    }

    ProviderClientCredentialBinding IProviderClientFactory<IChatClient>.ResolveCredentialBinding(ProviderClientBindingDescriptor descriptor) => ResolveBinding<IChatClient>(ProviderClientFamily.Chat, descriptor);
    ProviderClientCredentialBinding IProviderClientFactory<ITextToSpeechClient>.ResolveCredentialBinding(ProviderClientBindingDescriptor descriptor) => ResolveBinding<ITextToSpeechClient>(ProviderClientFamily.TextToSpeech, descriptor);
    ProviderClientCredentialBinding IProviderClientFactory<ISpeechToTextClient>.ResolveCredentialBinding(ProviderClientBindingDescriptor descriptor) => ResolveBinding<ISpeechToTextClient>(ProviderClientFamily.SpeechToText, descriptor);
    ProviderClientCredentialBinding IProviderClientFactory<IRealtimeClient>.ResolveCredentialBinding(ProviderClientBindingDescriptor descriptor) => ResolveBinding<IRealtimeClient>(ProviderClientFamily.Realtime, descriptor);
    ProviderClientCredentialBinding IProviderClientFactory<IImageGenerator>.ResolveCredentialBinding(ProviderClientBindingDescriptor descriptor) => ResolveBinding<IImageGenerator>(ProviderClientFamily.ImageGeneration, descriptor);
    ProviderClientCredentialBinding IProviderClientFactory<IEmbeddingGenerator>.ResolveCredentialBinding(ProviderClientBindingDescriptor descriptor) => ResolveBinding<IEmbeddingGenerator>(ProviderClientFamily.Embeddings, descriptor);
    ProviderClientCredentialBinding IProviderClientFactory<IHostedFileClient>.ResolveCredentialBinding(ProviderClientBindingDescriptor descriptor) => ResolveBinding<IHostedFileClient>(ProviderClientFamily.HostedFiles, descriptor);

    ValueTask<ProviderClientConstruction<IChatClient>> IProviderClientFactory<IChatClient>.CreateAsync(ProviderClientConstructionContext context, CancellationToken cancellationToken) => CreateAsync<IChatClient>(ProviderClientFamily.Chat, context, cancellationToken);
    ValueTask<ProviderClientConstruction<ITextToSpeechClient>> IProviderClientFactory<ITextToSpeechClient>.CreateAsync(ProviderClientConstructionContext context, CancellationToken cancellationToken) => CreateAsync<ITextToSpeechClient>(ProviderClientFamily.TextToSpeech, context, cancellationToken);
    ValueTask<ProviderClientConstruction<ISpeechToTextClient>> IProviderClientFactory<ISpeechToTextClient>.CreateAsync(ProviderClientConstructionContext context, CancellationToken cancellationToken) => CreateAsync<ISpeechToTextClient>(ProviderClientFamily.SpeechToText, context, cancellationToken);
    ValueTask<ProviderClientConstruction<IRealtimeClient>> IProviderClientFactory<IRealtimeClient>.CreateAsync(ProviderClientConstructionContext context, CancellationToken cancellationToken) => CreateAsync<IRealtimeClient>(ProviderClientFamily.Realtime, context, cancellationToken);
    ValueTask<ProviderClientConstruction<IImageGenerator>> IProviderClientFactory<IImageGenerator>.CreateAsync(ProviderClientConstructionContext context, CancellationToken cancellationToken) => CreateAsync<IImageGenerator>(ProviderClientFamily.ImageGeneration, context, cancellationToken);
    ValueTask<ProviderClientConstruction<IEmbeddingGenerator>> IProviderClientFactory<IEmbeddingGenerator>.CreateAsync(ProviderClientConstructionContext context, CancellationToken cancellationToken) => CreateAsync<IEmbeddingGenerator>(ProviderClientFamily.Embeddings, context, cancellationToken);
    ValueTask<ProviderClientConstruction<IHostedFileClient>> IProviderClientFactory<IHostedFileClient>.CreateAsync(ProviderClientConstructionContext context, CancellationToken cancellationToken) => CreateAsync<IHostedFileClient>(ProviderClientFamily.HostedFiles, context, cancellationToken);

    public IProviderErrorHandler CreateErrorHandler() => _providers[0].CreateErrorHandler();

    public ProviderMetadata GetMetadata()
    {
        var metadata = _providers.Select(static provider => provider.GetMetadata()).ToList();
        var families = new Dictionary<ProviderClientFamily, ProviderFamilyDescriptor>();

        foreach (var providerMetadata in metadata)
        {
            foreach (var family in providerMetadata.Families)
                families[family.Key] = family.Value;
        }

        return new ProviderMetadata
        {
            ProviderKey = ProviderKey,
            DisplayName = DisplayName,
            Families = families,
            DocumentationUri = metadata.Select(static item => item.DocumentationUri).FirstOrDefault(static value => value is not null)
        };
    }

    public ProviderValidationResult ValidateConfiguration(EffectiveProviderClientConfig config) =>
        GetFamilyProvider<IProvider>(config.Family).ValidateConfiguration(config);

    public Task<ProviderValidationResult>? ValidateConfigurationAsync(
        EffectiveProviderClientConfig config,
        CancellationToken cancellationToken = default) =>
        GetFamilyProvider<IProvider>(config.Family).ValidateConfigurationAsync(config, cancellationToken);

    private ProviderClientCredentialBinding ResolveBinding<TClient>(
        ProviderClientFamily family,
        ProviderClientBindingDescriptor descriptor) where TClient : class =>
        GetFamilyProvider<IProviderClientFactory<TClient>>(family).ResolveCredentialBinding(descriptor);

    private ValueTask<ProviderClientConstruction<TClient>> CreateAsync<TClient>(
        ProviderClientFamily family,
        ProviderClientConstructionContext context,
        CancellationToken cancellationToken) where TClient : class =>
        GetFamilyProvider<IProviderClientFactory<TClient>>(family).CreateAsync(context, cancellationToken);

    private TProvider GetFamilyProvider<TProvider>(ProviderClientFamily family)
        where TProvider : class
    {
        for (var i = _providers.Count - 1; i >= 0; i--)
        {
            if (_providers[i] is TProvider provider && SupportsFamily(_providers[i], family))
                return provider;
        }

        throw new InvalidOperationException(
            $"Provider '{ProviderKey}' is registered, but it does not support client family '{family}'.");
    }

    private static bool SupportsFamily(IProvider provider, ProviderClientFamily family) =>
        provider.GetMetadata().Families.ContainsKey(family);

}
