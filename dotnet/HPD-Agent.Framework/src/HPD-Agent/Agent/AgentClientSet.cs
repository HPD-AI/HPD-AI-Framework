using HPD.Agent.Providers;
using Microsoft.Extensions.AI;

namespace HPD.Agent;

/// <summary>
/// Resolved client-family instances for an agent build.
/// </summary>
public sealed class AgentClientSet : IDisposable
{
    private IReadOnlySet<object>? _ownedClients;
    private IReadOnlyList<IAsyncDisposable>? _leases;
    public IChatClient? Chat { get; init; }
    public ITextToSpeechClient? TextToSpeech { get; init; }
    public ISpeechToTextClient? SpeechToText { get; init; }
    public IRealtimeClient? Realtime { get; init; }
    public IImageGenerator? ImageGenerator { get; init; }
    public IEmbeddingGenerator? EmbeddingGenerator { get; init; }
    public IHostedFileClient? HostedFiles { get; init; }
    public Func<ProviderComponentLifetimeContext, IVoiceActivityDetector>? VoiceActivityDetectorFactory { get; init; }
    public Func<ProviderComponentLifetimeContext, IEotDetector>? EndOfTurnDetectorFactory { get; init; }

    public IReadOnlyDictionary<ProviderClientFamily, ProviderClientConfig> ResolvedConfigs { get; init; }
        = new Dictionary<ProviderClientFamily, ProviderClientConfig>();

    public static AgentClientSet Empty { get; } = new();

    public static AgentClientSet ForChat(
        IChatClient? chat,
        ProviderClientConfig? chatConfig = null)
    {
        var configs = chatConfig == null
            ? new Dictionary<ProviderClientFamily, ProviderClientConfig>()
            : new Dictionary<ProviderClientFamily, ProviderClientConfig>
            {
                [ProviderClientFamily.Chat] = chatConfig
            };

        return new AgentClientSet
        {
            Chat = chat,
            ResolvedConfigs = configs
        };
    }

    public ProviderClientConfig? GetResolvedConfig(ProviderClientFamily family)
        => ResolvedConfigs.TryGetValue(family, out var config) ? config : null;

    internal void SetOwnedClients(IReadOnlySet<object> ownedClients)
        => _ownedClients = ownedClients;

    internal void SetLeases(IReadOnlyList<IAsyncDisposable> leases)
        => _leases = leases;

    public void Dispose()
    {
        var disposed = new HashSet<object>(ReferenceEqualityComparer.Instance);

        DisposeOnce(Chat, disposed, _ownedClients);
        DisposeOnce(TextToSpeech, disposed, _ownedClients);
        DisposeOnce(SpeechToText, disposed, _ownedClients);
        DisposeOnce(Realtime, disposed, _ownedClients);
        DisposeOnce(ImageGenerator, disposed, _ownedClients);
        DisposeOnce(EmbeddingGenerator, disposed, _ownedClients);
        DisposeOnce(HostedFiles, disposed, _ownedClients);

        if (_leases is not null)
        {
            for (var index = _leases.Count - 1; index >= 0; index--)
                _leases[index].DisposeAsync().AsTask().GetAwaiter().GetResult();
        }
    }

    private static void DisposeOnce(object? value, HashSet<object> disposed, IReadOnlySet<object>? ownedClients)
    {
        if (value is not IDisposable disposable ||
            (ownedClients is not null && !ownedClients.Contains(value)) ||
            !disposed.Add(value))
            return;

        disposable.Dispose();
    }
}
