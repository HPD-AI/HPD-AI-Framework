// Copyright 2026 Einstein Essibu
// SPDX-License-Identifier: AGPL-3.0-only

using HPD.Agent.Middleware;
using HPD.Agent.Providers;
using HPD.Events;
using HPD.Events.Struct;
using Microsoft.Extensions.AI;

namespace HPD.Agent.Audio.Realtime;

/// <summary>
/// Hosts a Microsoft.Extensions.AI realtime session inside the HPD runtime.
/// </summary>
public sealed class RealtimeAudioMiddleware : IAgentMiddleware
{
    private readonly object _sync = new();
    private AudioConfig _config;
    private IRealtimeClientSession? _session;
    private string? _activeSessionId;
    private string? _activeBranchId;

    /// <summary>Create realtime middleware with a realtime-mode audio config.</summary>
    public RealtimeAudioMiddleware()
        : this(new AudioConfig
        {
            ProcessingMode = AudioProcessingMode.Realtime,
            Realtime = new RealtimeAudioConfig()
        })
    {
    }

    /// <summary>Create realtime middleware with explicit configuration.</summary>
    public RealtimeAudioMiddleware(AudioConfig config)
    {
        ArgumentNullException.ThrowIfNull(config);
        _config = config.Clone();
    }

    /// <summary>Optional prebuilt realtime client.</summary>
    public IRealtimeClient? RealtimeClient { get; set; }

    /// <summary>Unified provider registry for resolving realtime clients.</summary>
    public IProviderRegistry? ProviderRegistry { get; set; }

    /// <summary>Replace middleware-level realtime audio configuration.</summary>
    public void Configure(AudioConfig config)
    {
        ArgumentNullException.ThrowIfNull(config);
        config.Validate();
        _config = config.Clone();
    }

    /// <inheritdoc />
    public async Task BeforeStartAsync(BeforeStartContext context, CancellationToken cancellationToken)
    {
        var effectiveConfig = _config.MergeWith(context.RunConfig?.Audio as AudioConfig);
        if (context.RunConfig?.Audio is AudioRunConfig runAudio)
            effectiveConfig = _config.MergeWith(runAudio.ToFullConfig());

        effectiveConfig.Validate();

        if (effectiveConfig.Disabled == true ||
            effectiveConfig.ProcessingMode != AudioProcessingMode.Realtime)
        {
            return;
        }

        var realtimeConfig = effectiveConfig.Realtime
            ?? throw new InvalidOperationException("Realtime audio config is required.");
        var sessionBinding = ResolveSessionBinding(
            context.Config,
            realtimeConfig,
            context.ClientSet,
            context.Services);
        var session = await CreateSessionAsync(
            sessionBinding,
            realtimeConfig,
            context.Config.SystemInstructions,
            cancellationToken).ConfigureAwait(false);

        lock (_sync)
        {
            _session = session;
            _activeSessionId = null;
            _activeBranchId = null;
        }

        var frames = context.StructEvents.Route<AudioInputFrame>().Subscribe();
        context.RegisterDisposable(frames);

        var projector = new RealtimeEventProjector(
            context.EventCoordinator,
            context.StructEvents,
            context.Config.AgentId ?? context.AgentName,
            scopeProvider: GetProjectionScope,
            afterEmitAsync: (evt, token) => PersistProjectedEventAsync(context.Config.SessionStore, evt, token),
            contentStoreProvider: sessionId => string.IsNullOrWhiteSpace(sessionId)
                ? null
                : context.ContentStore,
            provider: sessionBinding.ProviderConfig?.ProviderKey,
            model: sessionBinding.ProviderConfig?.ModelName);

        context.RegisterBackgroundTask(runtimeToken =>
            SendAudioInputAsync(frames, realtimeConfig, runtimeToken));
        context.RegisterBackgroundTask(runtimeToken =>
            ReceiveRealtimeOutputAsync(projector, context.RuntimeCapabilities, runtimeToken));
    }

    /// <inheritdoc />
    public async Task BeforeStopAsync(BeforeStopContext context, CancellationToken cancellationToken)
    {
        IRealtimeClientSession? session;

        lock (_sync)
        {
            session = _session;
            _session = null;
            _activeSessionId = null;
            _activeBranchId = null;
        }

        if (session != null)
            await session.DisposeAsync().ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task BeforeIterationAsync(BeforeIterationContext context, CancellationToken cancellationToken)
    {
        var effectiveConfig = _config.MergeWith(context.RunConfig.Audio as AudioConfig);
        if (context.RunConfig.Audio is AudioRunConfig runAudio)
            effectiveConfig = _config.MergeWith(runAudio.ToFullConfig());

        if (effectiveConfig.Disabled == true ||
            effectiveConfig.ProcessingMode != AudioProcessingMode.Realtime ||
            effectiveConfig.Realtime == null)
        {
            return;
        }

        var session = GetActiveSession();
        if (session == null)
        {
            throw new InvalidOperationException(
                "Realtime runtime session was not bound at StartAsync. Start the runtime with an AgentRunConfig whose Audio resolves to Realtime mode.");
        }

        if (context.UserMessageText() is not { Length: > 0 } text)
            return;

        SetProjectionScope(context.Session?.Id, context.Branch?.Id);

        var item = new RealtimeConversationItem(
            [new TextContent(text)],
            id: Guid.NewGuid().ToString("N"),
            role: ChatRole.User);

        await session.SendAsync(
            new CreateConversationItemRealtimeClientMessage(item),
            cancellationToken).ConfigureAwait(false);

        if (effectiveConfig.Realtime.CreateResponseOnCommit)
        {
            await session.SendAsync(
                new CreateResponseRealtimeClientMessage(),
                cancellationToken).ConfigureAwait(false);
        }

        context.SkipLLMCall = true;
        context.OverrideResponse = new ChatMessage(ChatRole.Assistant, string.Empty);
    }

    private IRealtimeClientSession? GetActiveSession()
    {
        lock (_sync)
        {
            return _session;
        }
    }

    private RealtimeProjectionScope GetProjectionScope()
    {
        lock (_sync)
        {
            return new RealtimeProjectionScope(_activeSessionId, _activeBranchId);
        }
    }

    private void SetProjectionScope(string? sessionId, string? branchId)
    {
        if (string.IsNullOrWhiteSpace(sessionId) || string.IsNullOrWhiteSpace(branchId))
            return;

        lock (_sync)
        {
            _activeSessionId = sessionId;
            _activeBranchId = branchId;
        }
    }

    private IRealtimeClient ResolveRealtimeClient(
        ClientProviderConfig? providerConfig,
        IServiceProvider? services)
    {
        if (providerConfig == null || string.IsNullOrWhiteSpace(providerConfig.ProviderKey))
            throw new InvalidOperationException(
                "Realtime mode requires AgentConfig.Clients.Realtime with a ProviderKey, or an injected RealtimeClient.");

        var registry = ProviderRegistry
            ?? throw new InvalidOperationException(
                "Realtime mode requires a provider registry, or an injected RealtimeClient.");
        var provider = registry.GetRequiredProvider<IRealtimeClientProvider>(providerConfig.ProviderKey);
        return provider.CreateRealtimeClient(providerConfig, services);
    }

    private SessionBinding ResolveSessionBinding(
        AgentConfig agentConfig,
        RealtimeAudioConfig realtimeConfig,
        AgentClientSet? clientSet,
        IServiceProvider? services)
    {
        var providerConfig = MergeClientConfig(
            agentConfig.ResolveClientConfig(ProviderClientFamily.Realtime),
            realtimeConfig.Client);

        if (realtimeConfig.OverrideClient != null)
        {
            return new SessionBinding(
                realtimeConfig.OverrideClient,
                providerConfig);
        }

        var client = RealtimeClient;
        if (client == null && realtimeConfig.Client != null)
            client = ResolveRealtimeClient(providerConfig, services);

        client ??= clientSet?.Realtime
            ?? ResolveRealtimeClient(providerConfig, services);

        return new SessionBinding(client, providerConfig);
    }

    private static ClientProviderConfig? MergeClientConfig(
        ClientProviderConfig? defaults,
        ClientProviderConfig? overrides)
    {
        if (defaults == null && overrides == null)
            return null;

        var merged = new ClientProviderConfig();
        ApplyClientConfig(merged, defaults);
        ApplyClientConfig(merged, overrides);
        return string.IsNullOrWhiteSpace(merged.ProviderKey) &&
            string.IsNullOrWhiteSpace(merged.ModelName) &&
            string.IsNullOrWhiteSpace(merged.ApiKey) &&
            string.IsNullOrWhiteSpace(merged.Endpoint) &&
            string.IsNullOrWhiteSpace(merged.ProviderOptionsJson) &&
            merged.CustomHeaders == null &&
            merged.AdditionalProperties == null &&
            merged.DefaultChatOptions == null &&
            string.IsNullOrWhiteSpace(merged.HttpReferer) &&
            string.IsNullOrWhiteSpace(merged.AppName) &&
            merged.PromptFormatter == null
                ? null
                : merged;
    }

    private static void ApplyClientConfig(ClientProviderConfig target, ClientProviderConfig? source)
    {
        if (source == null)
            return;

        if (!string.IsNullOrWhiteSpace(source.ProviderKey))
            target.ProviderKey = source.ProviderKey;

        if (!string.IsNullOrWhiteSpace(source.ModelName))
            target.ModelName = source.ModelName;

        target.ApiKey = source.ApiKey ?? target.ApiKey;
        target.Endpoint = source.Endpoint ?? target.Endpoint;
        target.DefaultChatOptions = source.DefaultChatOptions ?? target.DefaultChatOptions;
        target.HttpReferer = source.HttpReferer ?? target.HttpReferer;
        target.AppName = source.AppName ?? target.AppName;
        target.PromptFormatter = source.PromptFormatter ?? target.PromptFormatter;
        target.ProviderOptionsJson = source.ProviderOptionsJson ?? target.ProviderOptionsJson;

        if (source.CustomHeaders != null)
        {
            target.CustomHeaders ??= new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var pair in source.CustomHeaders)
                target.CustomHeaders[pair.Key] = pair.Value;
        }

        if (source.AdditionalProperties != null)
        {
            target.AdditionalProperties ??= new Dictionary<string, object>();
            foreach (var pair in source.AdditionalProperties)
                target.AdditionalProperties[pair.Key] = pair.Value;
        }
    }

    private static Task<IRealtimeClientSession> CreateSessionAsync(
        SessionBinding binding,
        RealtimeAudioConfig realtimeConfig,
        string? instructions,
        CancellationToken cancellationToken)
    {
        return binding.Client.CreateSessionAsync(
            realtimeConfig.ToSessionOptions(binding.ProviderConfig?.ModelName, instructions),
            cancellationToken);
    }

    private async Task SendAudioInputAsync(
        StructEventSubscription<AudioInputFrame> frames,
        RealtimeAudioConfig config,
        CancellationToken cancellationToken)
    {
        var batch = new AudioInputFrame[64];
        while (!cancellationToken.IsCancellationRequested)
        {
            var count = frames.TryReadBatch(batch);
            if (count == 0)
            {
                await Task.Yield();
                continue;
            }

            for (var i = 0; i < count; i++)
            {
                var frame = batch[i];
                SetProjectionScope(frame.SessionId, frame.BranchId);

                var session = GetActiveSession();
                if (session == null)
                    continue;

                if (!frame.Audio.IsEmpty)
                {
                    await session.SendAsync(
                        new InputAudioBufferAppendRealtimeClientMessage(
                            new DataContent(frame.Audio, frame.MimeType)),
                        cancellationToken).ConfigureAwait(false);
                }

                if (frame.IsFinal)
                {
                    await session.SendAsync(
                        new InputAudioBufferCommitRealtimeClientMessage(),
                        cancellationToken).ConfigureAwait(false);

                    if (config.CreateResponseOnCommit)
                    {
                        await session.SendAsync(
                            new CreateResponseRealtimeClientMessage(),
                            cancellationToken).ConfigureAwait(false);
                    }
                }
            }
        }
    }

    private async Task ReceiveRealtimeOutputAsync(
        RealtimeEventProjector projector,
        IRuntimeCapabilityRegistry runtimeCapabilities,
        CancellationToken cancellationToken)
    {
        var session = GetActiveSession();
        if (session == null)
            return;

        RealtimeToolBridge? toolBridge = null;
        Task? toolBridgeTask = null;
        if (runtimeCapabilities.TryGet<IRuntimeFunctionExecutor>(out var functionExecutor))
        {
            toolBridge = new RealtimeToolBridge(session, functionExecutor);
            toolBridgeTask = Task.Run(() => toolBridge.RunAsync(cancellationToken), CancellationToken.None);
        }

        await foreach (var message in session
            .GetStreamingResponseAsync(cancellationToken)
            .WithCancellation(cancellationToken)
            .ConfigureAwait(false))
        {
            await projector.ProjectAsync(message, cancellationToken).ConfigureAwait(false);
            toolBridge?.TryEnqueue(message);
        }

        toolBridge?.Dispose();
        if (toolBridgeTask != null)
            await toolBridgeTask.ConfigureAwait(false);
    }

    private static async ValueTask PersistProjectedEventAsync(
        ISessionStore? store,
        AgentEvent evt,
        CancellationToken cancellationToken)
    {
        if (store == null ||
            !evt.ShouldPersistToBranch() ||
            string.IsNullOrWhiteSpace(evt.SessionId) ||
            string.IsNullOrWhiteSpace(evt.BranchId))
        {
            return;
        }

        var branchEvent = BranchEventFactory.FromAgentEvent(
            evt.SessionId!,
            evt.BranchId!,
            evt,
            messageTurnId: null,
            conversationId: evt.SessionId,
            iteration: 0,
            inputMessageCount: 0,
            isResume: false,
            terminationReason: null,
            turnMessageCount: 0);

        if (branchEvent == null)
            return;

        try
        {
            await store.AppendBranchEventAsync(
                evt.SessionId!,
                evt.BranchId!,
                branchEvent,
                cancellationToken: cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            // Realtime projections are still emitted live if best-effort branch persistence fails.
        }
    }
}

internal sealed record SessionBinding(
    IRealtimeClient Client,
    ClientProviderConfig? ProviderConfig);

internal static class RealtimeAudioMiddlewareContextExtensions
{
    public static string? UserMessageText(this BeforeIterationContext context)
    {
        if (context.Messages.Count == 0)
            return null;

        var last = context.Messages[^1];
        if (last.Role != ChatRole.User)
            return null;

        return string.Concat(last.Contents.OfType<TextContent>().Select(static content => content.Text));
    }
}
