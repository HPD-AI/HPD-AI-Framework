using HPD.Agent;
using HPD.Agent.Audio;
using HPD.Agent.Audio.Output;
using HPD.Agent.Audio.Interaction;
using HPD.Agent.Audio.Media;
using HPD.Agent.Audio.Providers;
using HPD.Agent.Providers;
using Microsoft.Extensions.AI;

namespace HPD.Agent.Providers.Audio.Meai;

#pragma warning disable EXTEXP0001

internal sealed class ProviderRegistrySpeechToTextInteractionSessionFactory :
    IAudioInteractionSessionFactory,
    IAsyncDisposable,
    IDisposable
{
    private long _nextSessionId;
    private readonly IProviderRegistry _providerRegistry;
    private readonly ProviderClientConfig _providerConfig;
    private readonly IInputContentSourceResolver _sourceResolver;
    private readonly MeaiBatchSpeechToTextInteractionSessionOptions _sessionOptions;
    private readonly IServiceProvider? _services;
    private readonly bool _disposeCreatedClient;
    private ISpeechToTextClient? _client;
    private IProvider? _provider;
    private bool _disposed;

    public ProviderRegistrySpeechToTextInteractionSessionFactory(
        IProviderRegistry providerRegistry,
        ProviderClientConfig providerConfig,
        IInputContentSourceResolver sourceResolver,
        MeaiBatchSpeechToTextInteractionSessionOptions sessionOptions,
        bool disposeCreatedClient,
        IServiceProvider? services = null)
    {
        _providerRegistry = providerRegistry ?? throw new ArgumentNullException(nameof(providerRegistry));
        _providerConfig = providerConfig ?? throw new ArgumentNullException(nameof(providerConfig));
        _sourceResolver = sourceResolver ?? throw new ArgumentNullException(nameof(sourceResolver));
        _sessionOptions = sessionOptions ?? throw new ArgumentNullException(nameof(sessionOptions));
        _disposeCreatedClient = disposeCreatedClient;
        _services = services;

        if (string.IsNullOrWhiteSpace(_providerConfig.ProviderKey))
        {
            throw new ArgumentException(
                "Input media speech-to-text provider configuration requires a ProviderKey.",
                nameof(providerConfig));
        }
    }

    public ValueTask<IAudioInteractionSession> CreateAsync(
        ProviderRouteDecision decision,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(decision);
        cancellationToken.ThrowIfCancellationRequested();

        var id = new InteractionSessionId(
            $"meai-provider-stt-{Interlocked.Increment(ref _nextSessionId):D4}");

        return ValueTask.FromResult<IAudioInteractionSession>(
            new MeaiBatchSpeechToTextInteractionSession(
                id,
                GetOrCreateClient(),
                _sourceResolver,
                ResolveOptions(decision)));
    }

    public ValueTask DisposeAsync()
    {
        Dispose();
        return ValueTask.CompletedTask;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        if (_disposeCreatedClient)
        {
            _client?.Dispose();
        }

        _client = null;
    }

    private ISpeechToTextClient GetOrCreateClient()
    {
        if (_client is not null)
        {
            return _client;
        }

        var provider = _providerRegistry.GetRequiredProvider<ISpeechToTextClientProvider>(
            _providerConfig.ProviderKey);
        _provider = provider;
        _client = provider.CreateSpeechToTextClient(_providerConfig, _services);
        return _client;
    }

    private MeaiBatchSpeechToTextInteractionSessionOptions ResolveOptions(
        ProviderRouteDecision decision)
    {
        var providerKey = decision.Plan?.RouteEpoch.ProviderKey;
        var resolvedProviderKey = string.IsNullOrWhiteSpace(providerKey)
            ? _sessionOptions.ProviderKey
            : providerKey;
        var providerErrorHandler = _sessionOptions.ProviderErrorHandler ?? ResolveProviderErrorHandler();

        if (string.IsNullOrWhiteSpace(_sessionOptions.ProviderKey) ||
            string.IsNullOrWhiteSpace(providerKey) ||
            _sessionOptions.ProviderKey == providerKey)
        {
            return _sessionOptions with
            {
                ProviderErrorHandler = providerErrorHandler
            };
        }

        return _sessionOptions with
        {
            ProviderKey = resolvedProviderKey,
            ProviderErrorHandler = providerErrorHandler
        };
    }

    private HPD.Agent.ErrorHandling.IProviderErrorHandler? ResolveProviderErrorHandler()
    {
        if (_sessionOptions.ProviderErrorHandler is not null)
        {
            return _sessionOptions.ProviderErrorHandler;
        }

        _ = GetOrCreateClient();
        return _provider?.CreateErrorHandler();
    }
}

#pragma warning restore EXTEXP0001
