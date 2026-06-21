using HPD.Agent.Providers;
using Microsoft.Extensions.AI;

namespace HPD.Agent;

/// <summary>
/// Resolved client-family instances for an agent build.
/// </summary>
public sealed class AgentClientSet : IDisposable
{
    public IChatClient? Chat { get; init; }
    public IChatClient? Summarizer { get; init; }
    public ITextToSpeechClient? TextToSpeech { get; init; }
    public ISpeechToTextClient? SpeechToText { get; init; }
    public IRealtimeClient? Realtime { get; init; }
    public IImageGenerator? ImageGenerator { get; init; }
    public IEmbeddingGenerator? EmbeddingGenerator { get; init; }
    public IHostedFileClient? HostedFiles { get; init; }
    public Func<ProviderComponentLifetimeContext, IVoiceActivityDetector>? VoiceActivityDetectorFactory { get; init; }
    public Func<ProviderComponentLifetimeContext, IEotDetector>? EndOfTurnDetectorFactory { get; init; }

    public IReadOnlyDictionary<ProviderClientFamily, ClientProviderConfig> ResolvedConfigs { get; init; }
        = new Dictionary<ProviderClientFamily, ClientProviderConfig>();

    public static AgentClientSet Empty { get; } = new();

    public static AgentClientSet ForChat(
        IChatClient? chat,
        IChatClient? summarizer = null,
        ClientProviderConfig? chatConfig = null)
    {
        var configs = chatConfig == null
            ? new Dictionary<ProviderClientFamily, ClientProviderConfig>()
            : new Dictionary<ProviderClientFamily, ClientProviderConfig>
            {
                [ProviderClientFamily.Chat] = chatConfig
            };

        return new AgentClientSet
        {
            Chat = chat,
            Summarizer = summarizer,
            ResolvedConfigs = configs
        };
    }

    public ClientProviderConfig? GetResolvedConfig(ProviderClientFamily family)
        => ResolvedConfigs.TryGetValue(family, out var config) ? config : null;

    public void Dispose()
    {
        var disposed = new HashSet<object>(ReferenceEqualityComparer.Instance);

        DisposeOnce(Chat, disposed);
        DisposeOnce(Summarizer, disposed);
        DisposeOnce(TextToSpeech, disposed);
        DisposeOnce(SpeechToText, disposed);
        DisposeOnce(Realtime, disposed);
        DisposeOnce(ImageGenerator, disposed);
        DisposeOnce(EmbeddingGenerator, disposed);
        DisposeOnce(HostedFiles, disposed);
    }

    private static void DisposeOnce(object? value, HashSet<object> disposed)
    {
        if (value is not IDisposable disposable || !disposed.Add(value))
            return;

        disposable.Dispose();
    }
}
