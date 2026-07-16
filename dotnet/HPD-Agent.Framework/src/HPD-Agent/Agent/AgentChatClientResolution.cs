using HPD.Agent.Providers;
using Microsoft.Extensions.AI;

namespace HPD.Agent;

internal enum AgentChatClientSource
{
    InjectedOverride,
    RuntimeProvider,
    AgentDefault,
    InheritedFallback,
    DedicatedProvider
}

internal sealed class AgentChatClientHandle
{
    private readonly object _gate = new();
    private readonly bool _ownsClient;
    private int _leaseCount;
    private bool _closed;
    private bool _disposed;

    private AgentChatClientHandle(
        IChatClient client,
        AgentChatClientSource source,
        ClientProviderConfig? resolvedConfig,
        bool ownsClient)
    {
        Client = client ?? throw new ArgumentNullException(nameof(client));
        Source = source;
        ResolvedConfig = resolvedConfig;
        _ownsClient = ownsClient;
    }

    public IChatClient Client { get; }
    public AgentChatClientSource Source { get; }
    public ClientProviderConfig? ResolvedConfig { get; }

    public static AgentChatClientHandle Borrowed(
        IChatClient client,
        AgentChatClientSource source,
        ClientProviderConfig? resolvedConfig = null)
        => new(client, source, resolvedConfig, ownsClient: false);

    public static AgentChatClientHandle Owned(
        IChatClient client,
        AgentChatClientSource source,
        ClientProviderConfig? resolvedConfig = null)
        => new(client, source, resolvedConfig, ownsClient: true);

    public AgentChatClientLease AcquireLease()
    {
        lock (_gate)
        {
            if (_closed)
                throw new InvalidOperationException("The chat-client handle is closed and cannot issue another lease.");

            _leaseCount++;
            return new AgentChatClientLease(this);
        }
    }

    internal async ValueTask ReleaseAsync()
    {
        IChatClient? clientToDispose = null;

        lock (_gate)
        {
            if (_leaseCount <= 0)
                return;

            _leaseCount--;
            if (_ownsClient && _leaseCount == 0 && !_disposed)
            {
                _closed = true;
                _disposed = true;
                clientToDispose = Client;
            }
        }

        if (clientToDispose is IAsyncDisposable asyncDisposable)
            await asyncDisposable.DisposeAsync().ConfigureAwait(false);
        else
            clientToDispose?.Dispose();
    }
}

internal sealed class AgentChatClientLease : IAsyncDisposable, IDisposable
{
    private AgentChatClientHandle? _handle;

    internal AgentChatClientLease(AgentChatClientHandle handle)
    {
        _handle = handle;
    }

    public AgentChatClientHandle Handle
        => _handle ?? throw new ObjectDisposedException(nameof(AgentChatClientLease));

    public IChatClient Client => Handle.Client;

    public void Dispose()
        => DisposeAsync().AsTask().GetAwaiter().GetResult();

    public ValueTask DisposeAsync()
    {
        var handle = Interlocked.Exchange(ref _handle, null);
        return handle is null ? ValueTask.CompletedTask : handle.ReleaseAsync();
    }
}

internal sealed record AgentChatClientResolutionRequest
{
    public required AgentConfig AgentConfig { get; init; }
    public AgentRunConfig? RunConfig { get; init; }
    public AgentChatClientHandle? AgentDefault { get; init; }
    public AgentChatClientHandle? InheritedFallback { get; init; }
    public ClientProviderConfig? DedicatedProvider { get; init; }
}

internal sealed class AgentChatClientResolver
{
    private readonly IProviderRegistry? _providerRegistry;
    private readonly IServiceProvider? _services;

    public AgentChatClientResolver(IProviderRegistry? providerRegistry, IServiceProvider? services)
    {
        _providerRegistry = providerRegistry;
        _services = services;
    }

    public ValueTask<AgentChatClientLease> ResolveAsync(
        AgentChatClientResolutionRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        if (request.DedicatedProvider is { } dedicatedProvider)
            return ValueTask.FromResult(CreateProviderLease(dedicatedProvider, AgentChatClientSource.DedicatedProvider));

        if (request.RunConfig?.OverrideChatClient is { } overrideClient)
        {
            return ValueTask.FromResult(
                AgentChatClientHandle
                    .Borrowed(overrideClient, AgentChatClientSource.InjectedOverride)
                    .AcquireLease());
        }

        var runClients = CreateRunClientOverrides(request.RunConfig);
        if (runClients?.GetFamilyConfig(ProviderClientFamily.Chat) is not null)
        {
            var effectiveConfig = request.AgentConfig.ResolveClientConfig(
                ProviderClientFamily.Chat,
                runClients)
                ?? throw new InvalidOperationException("The runtime chat provider configuration could not be resolved.");

            return ValueTask.FromResult(CreateProviderLease(effectiveConfig, AgentChatClientSource.RuntimeProvider));
        }

        if (request.AgentDefault is not null)
            return ValueTask.FromResult(request.AgentDefault.AcquireLease());

        if (request.InheritedFallback is not null)
            return ValueTask.FromResult(request.InheritedFallback.AcquireLease());

        throw new InvalidOperationException(
            "No chat model is available for this invocation. Configure the agent, provide a runtime provider/model, supply an explicit client override, or invoke it from a parent with an inheritable chat client.");
    }

    private AgentChatClientLease CreateProviderLease(
        ClientProviderConfig config,
        AgentChatClientSource source)
    {
        if (string.IsNullOrWhiteSpace(config.ProviderKey))
            throw new InvalidOperationException("A chat provider key is required to resolve an invocation client.");

        if (string.IsNullOrWhiteSpace(config.ModelName))
            throw new InvalidOperationException(
                $"No model is configured for provider '{config.ProviderKey}'. Configure the agent client or the invocation override.");

        var registry = _providerRegistry
            ?? throw new InvalidOperationException(
                $"Cannot resolve chat provider '{config.ProviderKey}' because no provider registry is available.");
        var provider = registry.GetRequiredProvider<IChatClientProvider>(config.ProviderKey);
        var client = provider.CreateChatClient(config, _services)
            ?? throw new InvalidOperationException($"Chat provider '{config.ProviderKey}' returned no client.");

        return AgentChatClientHandle.Owned(client, source, config).AcquireLease();
    }

    private static AgentClientConfig? CreateRunClientOverrides(AgentRunConfig? options)
    {
        if (options is null)
            return null;

        if (options.Clients?.GetFamilyConfig(ProviderClientFamily.Chat) is not null)
            return options.Clients;

        var chat = options.GetChatProviderOverride();
        if (chat is null)
            return options.Clients;

        return options.Clients is null
            ? new AgentClientConfig { Chat = chat }
            : new AgentClientConfig
            {
                Providers = options.Clients.Providers,
                Chat = chat,
                Realtime = options.Clients.Realtime,
                TextToSpeech = options.Clients.TextToSpeech,
                SpeechToText = options.Clients.SpeechToText,
                ImageGeneration = options.Clients.ImageGeneration,
                Embeddings = options.Clients.Embeddings,
                HostedFiles = options.Clients.HostedFiles,
                VoiceActivityDetection = options.Clients.VoiceActivityDetection,
                EndOfTurnDetection = options.Clients.EndOfTurnDetection
            };
    }
}
