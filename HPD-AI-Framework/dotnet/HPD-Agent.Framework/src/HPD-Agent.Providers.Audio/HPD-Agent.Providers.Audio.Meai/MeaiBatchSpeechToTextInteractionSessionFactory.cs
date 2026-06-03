using HPD.Agent.Audio;
using HPD.Agent.Audio.Interaction;
using HPD.Agent.Audio.Media;
using HPD.Agent.Audio.Providers;
using Microsoft.Extensions.AI;

namespace HPD.Agent.Providers.Audio.Meai;

#pragma warning disable EXTEXP0001

public sealed class MeaiBatchSpeechToTextInteractionSessionFactory : IAudioInteractionSessionFactory
{
    private long _nextSessionId;
    private readonly ISpeechToTextClient _client;
    private readonly IInputContentSourceResolver _sourceResolver;
    private readonly MeaiBatchSpeechToTextInteractionSessionOptions _options;

    public MeaiBatchSpeechToTextInteractionSessionFactory(
        ISpeechToTextClient client,
        IInputContentSourceResolver sourceResolver,
        MeaiBatchSpeechToTextInteractionSessionOptions? options = null)
    {
        _client = client ?? throw new ArgumentNullException(nameof(client));
        _sourceResolver = sourceResolver ?? throw new ArgumentNullException(nameof(sourceResolver));
        _options = options ?? new MeaiBatchSpeechToTextInteractionSessionOptions();
    }

    public ValueTask<IAudioInteractionSession> CreateAsync(
        ProviderRouteDecision decision,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(decision);
        cancellationToken.ThrowIfCancellationRequested();

        var id = new InteractionSessionId(
            $"meai-batch-stt-{Interlocked.Increment(ref _nextSessionId):D4}");

        return ValueTask.FromResult<IAudioInteractionSession>(
            new MeaiBatchSpeechToTextInteractionSession(
                id,
                _client,
                _sourceResolver,
                ResolveOptions(decision)));
    }

    private MeaiBatchSpeechToTextInteractionSessionOptions ResolveOptions(
        ProviderRouteDecision decision)
    {
        var providerKey = decision.Plan?.RouteEpoch.ProviderKey;
        if (string.IsNullOrWhiteSpace(_options.ProviderKey) ||
            string.IsNullOrWhiteSpace(providerKey) ||
            _options.ProviderKey == providerKey)
        {
            return _options;
        }

        return _options with
        {
            ProviderKey = providerKey
        };
    }
}

#pragma warning restore EXTEXP0001
