using HPD.Agent.Providers;
using Microsoft.Extensions.AI;

namespace HPD.Agent;

/// <summary>
/// Resolved client-family instances for an agent build.
/// </summary>
public sealed class AgentClientSet : IAsyncDisposable
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

    internal IAsyncDisposable AcquireBorrowedLease()
    {
        lock (_lifetimeGate)
        {
            ObjectDisposedException.ThrowIf(_disposeRequested || _disposed, this);
            _borrowCount++;
            return new BorrowedLease(this);
        }
    }

    public async ValueTask DisposeAsync()
    {
        var dispose = false;
        lock (_lifetimeGate)
        {
            if (_disposeRequested || _disposed)
                return;
            _disposeRequested = true;
            if (_borrowCount != 0)
                return;
            _disposed = true;
            dispose = true;
        }
        if (dispose)
            await DisposeCoreAsync().ConfigureAwait(false);
    }

    private async ValueTask ReleaseBorrowedLeaseAsync()
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
            await DisposeCoreAsync().ConfigureAwait(false);
    }

    private async ValueTask DisposeCoreAsync()
    {
        var disposed = new HashSet<object>(ReferenceEqualityComparer.Instance);

        await DisposeOnceAsync(Chat, disposed, _ownedClients).ConfigureAwait(false);
        await DisposeOnceAsync(TextToSpeech, disposed, _ownedClients).ConfigureAwait(false);
        await DisposeOnceAsync(SpeechToText, disposed, _ownedClients).ConfigureAwait(false);
        await DisposeOnceAsync(Realtime, disposed, _ownedClients).ConfigureAwait(false);
        await DisposeOnceAsync(ImageGenerator, disposed, _ownedClients).ConfigureAwait(false);
        await DisposeOnceAsync(EmbeddingGenerator, disposed, _ownedClients).ConfigureAwait(false);
        await DisposeOnceAsync(HostedFiles, disposed, _ownedClients).ConfigureAwait(false);

        if (_leases is not null)
        {
            for (var index = _leases.Count - 1; index >= 0; index--)
                await _leases[index].DisposeAsync().ConfigureAwait(false);
        }
    }

    private sealed class BorrowedLease(AgentClientSet owner) : IAsyncDisposable
    {
        private AgentClientSet? _owner = owner;
        public ValueTask DisposeAsync() =>
            Interlocked.Exchange(ref _owner, null)?.ReleaseBorrowedLeaseAsync() ?? ValueTask.CompletedTask;
    }

    private static async ValueTask DisposeOnceAsync(
        object? value,
        HashSet<object> disposed,
        IReadOnlySet<object>? ownedClients)
    {
        if (value is null ||
            (ownedClients is not null && !ownedClients.Contains(value)) ||
            !disposed.Add(value))
            return;
        if (value is IAsyncDisposable asyncDisposable)
            await asyncDisposable.DisposeAsync().ConfigureAwait(false);
        else if (value is IDisposable disposable)
            disposable.Dispose();
    }
}
