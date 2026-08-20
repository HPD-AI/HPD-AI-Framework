using HPD.Agent.ErrorHandling;
using Microsoft.Extensions.AI;

namespace HPD.Agent.Providers;

internal sealed class CompositeProvider :
    IChatClientProvider,
    ITextToSpeechClientProvider,
    ISpeechToTextClientProvider,
    IRealtimeClientProvider,
    IImageGeneratorProvider,
    IEmbeddingGeneratorProvider,
    IHostedFileClientProvider
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
        where TProvider : class, IProvider
        => _providers.Any(static provider => provider is TProvider);

    internal TProvider GetTypedFamilyProvider<TProvider>(ProviderClientFamily family)
        where TProvider : class, IProvider
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

    public ValueTask<IChatClient> CreateChatClientAsync(
        ProviderClientConfig config,
        IServiceProvider? services = null,
        CancellationToken cancellationToken = default) =>
        GetFamilyProvider<IChatClientProvider>(ProviderClientFamily.Chat)
            .CreateChatClientAsync(config, services, cancellationToken);

    public ITextToSpeechClient CreateTextToSpeechClient(ProviderClientConfig config, IServiceProvider? services = null) =>
        GetFamilyProvider<ITextToSpeechClientProvider>(ProviderClientFamily.TextToSpeech).CreateTextToSpeechClient(config, services);

    public ISpeechToTextClient CreateSpeechToTextClient(ProviderClientConfig config, IServiceProvider? services = null) =>
        GetFamilyProvider<ISpeechToTextClientProvider>(ProviderClientFamily.SpeechToText).CreateSpeechToTextClient(config, services);

    public IRealtimeClient CreateRealtimeClient(ProviderClientConfig config, IServiceProvider? services = null) =>
        GetFamilyProvider<IRealtimeClientProvider>(ProviderClientFamily.Realtime).CreateRealtimeClient(config, services);

    public IImageGenerator CreateImageGenerator(ProviderClientConfig config, IServiceProvider? services = null) =>
        GetFamilyProvider<IImageGeneratorProvider>(ProviderClientFamily.ImageGeneration).CreateImageGenerator(config, services);

    public IEmbeddingGenerator CreateEmbeddingGenerator(ProviderClientConfig config, IServiceProvider? services = null) =>
        GetFamilyProvider<IEmbeddingGeneratorProvider>(ProviderClientFamily.Embeddings).CreateEmbeddingGenerator(config, services);

    public IHostedFileClient CreateHostedFileClient(ProviderClientConfig config, IServiceProvider? services = null) =>
        GetFamilyProvider<IHostedFileClientProvider>(ProviderClientFamily.HostedFiles).CreateHostedFileClient(config, services);

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

    public ProviderValidationResult ValidateConfiguration(ProviderClientConfig config, ProviderClientFamily family) =>
        GetFamilyProvider<IProvider>(family).ValidateConfiguration(config, family);

    public Task<ProviderValidationResult>? ValidateConfigurationAsync(
        ProviderClientConfig config,
        ProviderClientFamily family,
        CancellationToken cancellationToken = default) =>
        GetFamilyProvider<IProvider>(family).ValidateConfigurationAsync(config, family, cancellationToken);

    private TProvider GetFamilyProvider<TProvider>(ProviderClientFamily family)
        where TProvider : class, IProvider
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
