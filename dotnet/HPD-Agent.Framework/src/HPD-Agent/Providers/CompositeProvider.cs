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
    IHostedFileClientProvider,
    IVoiceActivityDetectorProvider,
    IEndOfTurnDetectorProvider
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

    public ValueTask<IChatClient> CreateChatClientAsync(
        ClientProviderConfig config,
        IServiceProvider? services = null,
        CancellationToken cancellationToken = default) =>
        GetFamilyProvider<IChatClientProvider>(ProviderClientFamily.Chat)
            .CreateChatClientAsync(config, services, cancellationToken);

    public ITextToSpeechClient CreateTextToSpeechClient(ClientProviderConfig config, IServiceProvider? services = null) =>
        GetFamilyProvider<ITextToSpeechClientProvider>(ProviderClientFamily.TextToSpeech).CreateTextToSpeechClient(config, services);

    public ISpeechToTextClient CreateSpeechToTextClient(ClientProviderConfig config, IServiceProvider? services = null) =>
        GetFamilyProvider<ISpeechToTextClientProvider>(ProviderClientFamily.SpeechToText).CreateSpeechToTextClient(config, services);

    public IRealtimeClient CreateRealtimeClient(ClientProviderConfig config, IServiceProvider? services = null) =>
        GetFamilyProvider<IRealtimeClientProvider>(ProviderClientFamily.Realtime).CreateRealtimeClient(config, services);

    public IImageGenerator CreateImageGenerator(ClientProviderConfig config, IServiceProvider? services = null) =>
        GetFamilyProvider<IImageGeneratorProvider>(ProviderClientFamily.ImageGeneration).CreateImageGenerator(config, services);

    public IEmbeddingGenerator CreateEmbeddingGenerator(ClientProviderConfig config, IServiceProvider? services = null) =>
        GetFamilyProvider<IEmbeddingGeneratorProvider>(ProviderClientFamily.Embeddings).CreateEmbeddingGenerator(config, services);

    public IHostedFileClient CreateHostedFileClient(ClientProviderConfig config, IServiceProvider? services = null) =>
        GetFamilyProvider<IHostedFileClientProvider>(ProviderClientFamily.HostedFiles).CreateHostedFileClient(config, services);

    public IVoiceActivityDetector CreateVoiceActivityDetector(
        ClientProviderConfig config,
        ProviderComponentLifetimeContext context,
        IServiceProvider? services = null) =>
        GetFamilyProvider<IVoiceActivityDetectorProvider>(ProviderClientFamily.VoiceActivityDetection)
            .CreateVoiceActivityDetector(config, context, services);

    public IEotDetector CreateEndOfTurnDetector(
        ClientProviderConfig config,
        ProviderComponentLifetimeContext context,
        IServiceProvider? services = null) =>
        GetFamilyProvider<IEndOfTurnDetectorProvider>(ProviderClientFamily.EndOfTurnDetection)
            .CreateEndOfTurnDetector(config, context, services);

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

    public ProviderValidationResult ValidateConfiguration(ClientProviderConfig config, ProviderClientFamily family) =>
        GetFamilyProvider<IProvider>(family).ValidateConfiguration(config, family);

    public Task<ProviderValidationResult>? ValidateConfigurationAsync(
        ClientProviderConfig config,
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
        family switch
        {
            ProviderClientFamily.Chat => provider is IChatClientProvider,
            ProviderClientFamily.TextToSpeech => provider is ITextToSpeechClientProvider,
            ProviderClientFamily.SpeechToText => provider is ISpeechToTextClientProvider,
            ProviderClientFamily.Realtime => provider is IRealtimeClientProvider,
            ProviderClientFamily.ImageGeneration => provider is IImageGeneratorProvider,
            ProviderClientFamily.Embeddings => provider is IEmbeddingGeneratorProvider,
            ProviderClientFamily.HostedFiles => provider is IHostedFileClientProvider,
            ProviderClientFamily.VoiceActivityDetection => provider is IVoiceActivityDetectorProvider,
            ProviderClientFamily.EndOfTurnDetection => provider is IEndOfTurnDetectorProvider,
            _ => false
        };

}
