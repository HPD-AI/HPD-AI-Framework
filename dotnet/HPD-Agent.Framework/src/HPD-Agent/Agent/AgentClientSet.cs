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
    private AgentClientFamilyResolutionSource? _familyResolver;
    private SubAgentClientInheritanceSource? _componentInheritance;
    private readonly object _componentGate = new();
    private readonly Dictionary<ProviderClientFamily, Task<ComponentSelection>> _components = [];
    private bool _componentsClosed;
    public IChatClient? Chat { get; init; }
    public ITextToSpeechClient? TextToSpeech { get; init; }
    public ISpeechToTextClient? SpeechToText { get; init; }
    public IRealtimeClient? Realtime { get; init; }
    public IImageGenerator? ImageGenerator { get; init; }
    public IEmbeddingGenerator? EmbeddingGenerator { get; init; }
    public IHostedFileClient? HostedFiles { get; init; }
    public IReadOnlyDictionary<ProviderClientFamily, ProviderClientConfig> ResolvedConfigs { get; init; }
        = new Dictionary<ProviderClientFamily, ProviderClientConfig>();
    /// <summary>Gets safe identities for the runtime clients that actually won family selection.</summary>
    public IReadOnlyDictionary<ProviderClientFamily, ProviderClientExecutionIdentity> ExecutionIdentities { get; init; }
        = new Dictionary<ProviderClientFamily, ProviderClientExecutionIdentity>();

    public static AgentClientSet Empty { get; } = new();

    public static AgentClientSet ForChat(
        IChatClient? chat,
        ProviderClientConfig? chatConfig = null,
        ProviderClientExecutionIdentity? executionIdentity = null)
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
            ResolvedConfigs = configs,
            ExecutionIdentities = executionIdentity is null
                ? new Dictionary<ProviderClientFamily, ProviderClientExecutionIdentity>()
                : new Dictionary<ProviderClientFamily, ProviderClientExecutionIdentity>
                {
                    [ProviderClientFamily.Chat] = ProviderClientExecutionIdentity.CreateSafe(
                        executionIdentity.ProviderKey,
                        executionIdentity.BackendKey,
                        ProviderClientFamily.Chat,
                        executionIdentity.ModelName,
                        executionIdentity.OperationAdapterKey,
                        executionIdentity.UsageSemanticsKey)
                }
        };
    }

    public ProviderClientConfig? GetResolvedConfig(ProviderClientFamily family)
        => ResolvedConfigs.TryGetValue(family, out var config) ? config : null;

    /// <summary>Gets the safe selected-client identity for a family when available.</summary>
    public ProviderClientExecutionIdentity? GetExecutionIdentity(ProviderClientFamily family)
        => ExecutionIdentities.TryGetValue(family, out var identity) ? identity : null;

    internal void SetOwnedClients(IReadOnlySet<object> ownedClients)
        => _ownedClients = ownedClients;

    internal void SetLeases(IReadOnlyList<IAsyncDisposable> leases)
        => _leases = leases;

    internal void SetFamilyResolver(
        Func<ProviderClientFamily, CancellationToken, ValueTask<object?>> resolver)
        => _familyResolver = new AgentClientFamilyResolutionSource(resolver);

    internal void SetComponentInheritance(SubAgentClientInheritanceSource? inheritance) =>
        _componentInheritance = inheritance;

    internal async ValueTask<TClient?> ResolveFamilyAsync<TClient>(
        ProviderClientFamily family,
        CancellationToken cancellationToken = default)
        where TClient : class
    {
        lock (_lifetimeGate)
            ObjectDisposedException.ThrowIf(_disposed || _disposeRequested && _borrowCount == 0, this);
        var existing = GetFamilyClient(family);
        if (existing is not null)
            return existing as TClient ?? throw new InvalidOperationException(
                $"Resolved family '{family}' does not implement '{typeof(TClient).Name}'.");
        if (_familyResolver is null)
            return null;
        var resolved = await _familyResolver.ResolveAsync(family, cancellationToken).ConfigureAwait(false);
        return resolved as TClient ?? (resolved is null ? null : throw new InvalidOperationException(
            $"Resolved family '{family}' does not implement '{typeof(TClient).Name}'."));
    }

    /// <summary>Acquires the run-selected text-to-speech client at its consumption boundary.</summary>
    public ValueTask<ITextToSpeechClient?> GetTextToSpeechAsync(CancellationToken cancellationToken = default) =>
        AcquireSelectedFamilyAsync<ITextToSpeechClient>(ProviderClientFamily.TextToSpeech, cancellationToken);

    /// <summary>Acquires the run-selected speech-to-text client at its consumption boundary.</summary>
    public ValueTask<ISpeechToTextClient?> GetSpeechToTextAsync(CancellationToken cancellationToken = default) =>
        AcquireSelectedFamilyAsync<ISpeechToTextClient>(ProviderClientFamily.SpeechToText, cancellationToken);

    /// <summary>Acquires the run-selected realtime client at its consumption boundary.</summary>
    public ValueTask<IRealtimeClient?> GetRealtimeAsync(CancellationToken cancellationToken = default) =>
        AcquireSelectedFamilyAsync<IRealtimeClient>(ProviderClientFamily.Realtime, cancellationToken);

    /// <summary>Acquires the run-selected image-generation client at its consumption boundary.</summary>
    public ValueTask<IImageGenerator?> GetImageGeneratorAsync(CancellationToken cancellationToken = default) =>
        AcquireSelectedFamilyAsync<IImageGenerator>(ProviderClientFamily.ImageGeneration, cancellationToken);

    /// <summary>Acquires the run-selected embedding client at its consumption boundary.</summary>
    public ValueTask<IEmbeddingGenerator?> GetEmbeddingGeneratorAsync(CancellationToken cancellationToken = default) =>
        AcquireSelectedFamilyAsync<IEmbeddingGenerator>(ProviderClientFamily.Embeddings, cancellationToken);

    /// <summary>Acquires the run-selected hosted-file client at its consumption boundary.</summary>
    public ValueTask<IHostedFileClient?> GetHostedFilesAsync(CancellationToken cancellationToken = default) =>
        AcquireSelectedFamilyAsync<IHostedFileClient>(ProviderClientFamily.HostedFiles, cancellationToken);

    /// <summary>
    /// Acquires a voice-activity or end-of-turn component under the run's durable family policy.
    /// The returned component remains owned by this execution-scoped client set.
    /// </summary>
    /// <typeparam name="TComponent">The leaf provider's component contract.</typeparam>
    /// <param name="family">VoiceActivityDetection or EndOfTurnDetection.</param>
    /// <param name="identity">Safe explicit identity of the child-owned component plan.</param>
    /// <param name="ownFactory">Constructs the complete child-owned component plan.</param>
    /// <param name="cancellationToken">Cancels component acquisition.</param>
    public async ValueTask<TComponent> GetProviderComponentAsync<TComponent>(
        ProviderClientFamily family,
        ProviderClientExecutionIdentity identity,
        Func<CancellationToken, ValueTask<ProviderClientConstruction<TComponent>>> ownFactory,
        CancellationToken cancellationToken = default)
        where TComponent : class
    {
        if (family is not (ProviderClientFamily.VoiceActivityDetection or ProviderClientFamily.EndOfTurnDetection))
            throw new ArgumentOutOfRangeException(nameof(family));
        ArgumentNullException.ThrowIfNull(identity);
        ArgumentNullException.ThrowIfNull(ownFactory);
        Task<ComponentSelection> task;
        lock (_componentGate)
        {
            ObjectDisposedException.ThrowIf(_componentsClosed, this);
            if (!_components.TryGetValue(family, out task!))
            {
                task = ResolveComponentAsync(family, identity, ownFactory, cancellationToken);
                _components.Add(family, task);
            }
        }
        var selection = await task.ConfigureAwait(false);
        return selection.Client as TComponent ?? throw new InvalidOperationException(
            $"Resolved component family '{family}' does not implement '{typeof(TComponent).Name}'.");
    }

    private async ValueTask<TClient?> AcquireSelectedFamilyAsync<TClient>(
        ProviderClientFamily family,
        CancellationToken cancellationToken)
        where TClient : class
    {
        var client = await ResolveFamilyAsync<TClient>(family, cancellationToken).ConfigureAwait(false);
        if (client is not null && GetExecutionIdentity(family) is null)
            throw new AgentRunConfigurationException(
                "subagent_provider_attribution_missing",
                $"clients.{family}",
                "The selected runtime client must declare a safe provider execution identity.");
        return client;
    }

    private object? GetFamilyClient(ProviderClientFamily family) => family switch
    {
        ProviderClientFamily.Chat => Chat,
        ProviderClientFamily.TextToSpeech => TextToSpeech,
        ProviderClientFamily.SpeechToText => SpeechToText,
        ProviderClientFamily.Realtime => Realtime,
        ProviderClientFamily.ImageGeneration => ImageGenerator,
        ProviderClientFamily.Embeddings => EmbeddingGenerator,
        ProviderClientFamily.HostedFiles => HostedFiles,
        _ => null
    };

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
        if (_familyResolver is not null)
            await _familyResolver.CloseAndDrainAsync().ConfigureAwait(false);
        Task<ComponentSelection>[] componentTasks;
        lock (_componentGate)
        {
            _componentsClosed = true;
            componentTasks = _components.Values.ToArray();
        }
        try { await Task.WhenAll(componentTasks).ConfigureAwait(false); }
        catch { }
        foreach (var task in componentTasks)
            if (task.Status == TaskStatus.RanToCompletion)
                await task.Result.Owner.DisposeAsync().ConfigureAwait(false);
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

    private async Task<ComponentSelection> ResolveComponentAsync<TComponent>(
        ProviderClientFamily family,
        ProviderClientExecutionIdentity identity,
        Func<CancellationToken, ValueTask<ProviderClientConstruction<TComponent>>> ownFactory,
        CancellationToken cancellationToken)
        where TComponent : class
    {
        var mode = _componentInheritance?.GetMode(family) ?? ClientFamilyInheritanceMode.UseOwn;
        if (mode == ClientFamilyInheritanceMode.InheritResolved)
            return await GetRequiredParentComponentAsync(family).ConfigureAwait(false);
        if (mode == ClientFamilyInheritanceMode.FallbackToParent)
        {
            try { return await CreateOwnAsync().ConfigureAwait(false); }
            catch (AgentRunConfigurationException exception) when (
                exception.Code is "ProviderDefaultRequired" or "ProviderProfileRequired")
            {
                return await GetRequiredParentComponentAsync(family).ConfigureAwait(false);
            }
        }
        return await CreateOwnAsync().ConfigureAwait(false);

        async Task<ComponentSelection> CreateOwnAsync()
        {
            var created = await ownFactory(cancellationToken).ConfigureAwait(false);
            var safeIdentity = ProviderClientExecutionIdentity.CreateSafe(
                identity.ProviderKey, identity.BackendKey, family, identity.ModelName,
                identity.OperationAdapterKey, identity.UsageSemanticsKey);
            return new ComponentSelection(created.Client, created.Owner, safeIdentity);
        }
    }

    private async Task<ComponentSelection> GetRequiredParentComponentAsync(ProviderClientFamily family)
    {
        var parent = _componentInheritance?.ParentClients;
        Task<ComponentSelection>? task = null;
        if (parent is not null)
        {
            lock (parent._componentGate)
                parent._components.TryGetValue(family, out task);
        }
        if (task is null)
            throw new AgentRunConfigurationException(
                "subagent_parent_client_unavailable",
                $"clients.{family}",
                $"The controlling execution has no resolved {family} component.");
        var parentSelection = await task.ConfigureAwait(false);
        return new ComponentSelection(
            parentSelection.Client,
            NoopAsyncDisposable.Instance,
            parentSelection.Identity);
    }

    private sealed record ComponentSelection(
        object Client,
        IAsyncDisposable Owner,
        ProviderClientExecutionIdentity Identity);

    private sealed class NoopAsyncDisposable : IAsyncDisposable
    {
        internal static NoopAsyncDisposable Instance { get; } = new();
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
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

    private sealed class AgentClientFamilyResolutionSource(
        Func<ProviderClientFamily, CancellationToken, ValueTask<object?>> resolver)
    {
        private readonly object _gate = new();
        private readonly Dictionary<ProviderClientFamily, Task<object?>> _resolved = [];
        private bool _closed;

        internal ValueTask<object?> ResolveAsync(
            ProviderClientFamily family,
            CancellationToken cancellationToken)
        {
            lock (_gate)
            {
                ObjectDisposedException.ThrowIf(_closed, this);
                if (!_resolved.TryGetValue(family, out var task))
                {
                    task = resolver(family, cancellationToken).AsTask();
                    _resolved.Add(family, task);
                }
                return new ValueTask<object?>(task);
            }
        }

        internal async ValueTask CloseAndDrainAsync()
        {
            Task<object?>[] admitted;
            lock (_gate)
            {
                if (_closed) return;
                _closed = true;
                admitted = _resolved.Values.ToArray();
            }
            try { await Task.WhenAll(admitted).ConfigureAwait(false); }
            catch { }
        }
    }
}
