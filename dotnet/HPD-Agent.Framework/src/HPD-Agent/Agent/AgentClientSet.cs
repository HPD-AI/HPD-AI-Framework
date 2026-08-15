using HPD.Agent.Providers;
using Microsoft.Extensions.AI;

namespace HPD.Agent;

/// <summary>
/// Resolved client-family instances for an agent build.
/// </summary>
public sealed class AgentClientSet : IDisposable
{
    private readonly object _lifetimeGate = new();
    private IReadOnlySet<object>? _ownedClients;
    private IReadOnlyList<IAsyncDisposable>? _leases;
    private int _borrowCount;
    private bool _disposeRequested;
    private bool _disposed;
    public IChatClient? Chat { get; init; }
    public ITextToSpeechClient? TextToSpeech { get; init; }
    public ISpeechToTextClient? SpeechToText { get; init; }
    public IRealtimeClient? Realtime { get; init; }
    public IImageGenerator? ImageGenerator { get; init; }
    public IEmbeddingGenerator? EmbeddingGenerator { get; init; }
    public IHostedFileClient? HostedFiles { get; init; }
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

    internal IDisposable AcquireBorrowedLease()
    {
        lock (_lifetimeGate)
        {
            ObjectDisposedException.ThrowIf(_disposeRequested || _disposed, this);
            _borrowCount++;
            return new BorrowedLease(this);
        }
    }

    public void Dispose()
    {
        lock (_lifetimeGate)
        {
            if (_disposeRequested || _disposed)
                return;
            _disposeRequested = true;
            if (_borrowCount != 0)
                return;
            _disposed = true;
        }
        DisposeCore();
    }

    private void ReleaseBorrowedLease()
    {
        var dispose = false;
        lock (_lifetimeGate)
        {
            if (_borrowCount > 0)
                _borrowCount--;
            if (_borrowCount == 0 && _disposeRequested && !_disposed)
            {
                _disposed = true;
                dispose = true;
            }
        }
        if (dispose)
            DisposeCore();
    }

    private void DisposeCore()
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

    private sealed class BorrowedLease(AgentClientSet owner) : IDisposable
    {
        private AgentClientSet? _owner = owner;
        public void Dispose() => Interlocked.Exchange(ref _owner, null)?.ReleaseBorrowedLease();
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
