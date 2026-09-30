using Microsoft.Extensions.DependencyInjection;
using HPD.Agent.Audio.AgentIntegration;
using HPD.Agent.Providers;
using System.Text.Json;
using Microsoft.Extensions.AI;

namespace HPD.Agent.Audio.LiveKit;

#pragma warning disable MEAI001

public static class ManagedLiveKitBuilderExtensions
{
    /// <summary>
    /// Selects LiveKit as the retained Audio transport. A following
    /// <c>WithAudio(...)</c> compiles it with the configured STT and TTS clients.
    /// </summary>
    public static AgentBuilder WithLiveKitAudioTransport(
        this AgentBuilder builder,
        string endpoint,
        Func<ManagedAudioSessionStartRequestV1, CancellationToken, ValueTask<char[]>> credentialResolver,
        Action<AudioConfig>? configureAudio = null,
        LiveKitTransportProviderConfig? providerConfig = null)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentException.ThrowIfNullOrWhiteSpace(endpoint);
        ArgumentNullException.ThrowIfNull(credentialResolver);

        var speechToText = builder.Config.ResolveClientConfig(ProviderClientFamily.SpeechToText)
            as SpeechToTextClientConfig
            ?? throw new InvalidOperationException(
                "Configure a speech-to-text provider before selecting the managed LiveKit transport.");
        if (string.IsNullOrWhiteSpace(speechToText.ModelName))
            throw new InvalidOperationException(
                "Managed LiveKit streaming transcription requires an explicit speech-to-text model.");

        providerConfig ??= new LiveKitTransportProviderConfig();
        var providerConfigElement = JsonSerializer.SerializeToElement(
            providerConfig,
            LiveKitManagedSessionJsonContext.Default.LiveKitTransportProviderConfig);
        var ownedProviderConfig = providerConfigElement.Deserialize(
            LiveKitManagedSessionJsonContext.Default.LiveKitTransportProviderConfig)
            ?? throw new InvalidOperationException("LiveKit transport configuration could not be snapshotted.");
        var audio = builder.Config.Audio ?? new AudioConfig();
        audio.Transport = new AudioTransportConfig
        {
            ComponentInstance = LiveKitAudioTransport.ComponentInstance,
            Endpoint = endpoint,
            Schema = "hpd.provider.livekit.audiotransport.config",
            Version = 1,
            ProviderConfig = providerConfigElement
        };
        builder.Config.Audio = audio;
        AudioRuntimeCompositionRegistryV1.Register(builder, new LiveKitRuntimeComposition(
            endpoint,
            credentialResolver,
            ownedProviderConfig,
            speechToText.ModelName,
            speechToText.SpeechLanguage,
            speechToText.SpeechSampleRate ?? 16_000,
            speechToText.Override?.Client));
        return configureAudio is null ? builder : builder.WithAudio(configureAudio);
    }

    /// <summary>Configures the direct AgentBuilder hosting path.</summary>
    public static AgentBuilder WithManagedLiveKitAudio(
        this AgentBuilder builder,
        LiveKitManagedAudioSessionBackendOptions options,
        Action<AudioRuntimeAttachmentOptions>? configureAudio = null)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(options);
        var authority = new ManagedAudioSessionAuthorityV1(
            new LiveKitManagedAudioSessionBackend(options));
        var runtime = new AudioRuntimeAttachmentOptions
        {
            SessionControlAuthority = authority,
            EnableAssistantOutputPlayback = true
        };
        configureAudio?.Invoke(runtime);
        return builder.WithAudioRuntimeAttachment(runtime);
    }

    /// <summary>Registers the same managed-session graph for ASP.NET Core/DI hosting.</summary>
    public static IServiceCollection AddManagedLiveKitAudio(
        this IServiceCollection services,
        LiveKitManagedAudioSessionBackendOptions options)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(options);
        services.AddSingleton(options);
        services.AddSingleton<IManagedAudioSessionBackendV1, LiveKitManagedAudioSessionBackend>();
        services.AddSingleton<ManagedAudioSessionAuthorityV1>();
        services.AddSingleton<IAudioSessionControlAuthorityV1>(provider =>
            provider.GetRequiredService<ManagedAudioSessionAuthorityV1>());
        return services;
    }

    private sealed class LiveKitRuntimeComposition(
        string endpoint,
        Func<ManagedAudioSessionStartRequestV1, CancellationToken, ValueTask<char[]>> credentialResolver,
        LiveKitTransportProviderConfig providerConfig,
        string speechModel,
        string? speechLanguage,
        int sampleRate,
        ISpeechToTextClient? borrowedSpeechToText) : IAudioRuntimeCompositionV1
    {
        private int _applied;

        public AudioRuntimeAttachmentOptions Apply(
            AgentBuilder builder,
            AudioRuntimeAttachmentOptions options)
        {
            if (Interlocked.Exchange(ref _applied, 1) != 0)
                throw new InvalidOperationException("The LiveKit Audio runtime composition was already compiled.");
            var transcriptSource = new ConfiguredManagedStreamingSpeechToTextSourceV1(
                cancellationToken => AcquireSpeechToTextAsync(builder, borrowedSpeechToText, cancellationToken),
                new ManagedStreamingSpeechToTextOptionsV1
                {
                    ModelId = speechModel,
                    LanguageCode = speechLanguage,
                    UseProviderVoiceActivityDetection = true
                });
            options.SessionControlAuthority = new ManagedAudioSessionAuthorityV1(
                new LiveKitManagedAudioSessionBackend(new LiveKitManagedAudioSessionBackendOptions
                {
                    Endpoint = endpoint,
                    CredentialResolver = credentialResolver,
                    TranscriptSource = transcriptSource,
                    AudioSampleRateHz = sampleRate,
                    Transport = providerConfig
                }));
            options.EnableAssistantOutputPlayback = true;
            return options;
        }

        private static async ValueTask<ProviderClientConstruction<ISpeechToTextClient>> AcquireSpeechToTextAsync(
            AgentBuilder builder,
            ISpeechToTextClient? borrowedClient,
            CancellationToken cancellationToken)
        {
            var services = builder.ServiceProvider ?? throw new InvalidOperationException(
                "Build the Agent before opening a managed LiveKit Audio session.");
            ProviderClientConstruction<ISpeechToTextClient> construction;
            if (borrowedClient is not null)
            {
                construction = new ProviderClientConstruction<ISpeechToTextClient>
                {
                    Client = borrowedClient,
                    Owner = BorrowedClientOwner.Instance
                };
            }
            else
            {
                var composition = services.GetService<ProviderComposition>() ?? ProviderCompositionHost.Current
                    ?? throw new InvalidOperationException("The generated provider composition is unavailable.");
                var runtime = services.GetService<ProviderFamilyClientRuntime>() ??
                    new ProviderFamilyClientRuntime(composition, builder.ProviderRegistry, services);
                construction = await runtime.CreateAsync<ISpeechToTextClient>(
                    builder.Config,
                    ProviderClientFamily.SpeechToText,
                    source: ProviderSelectionSource.BuilderLocal,
                    cancellationToken: cancellationToken).ConfigureAwait(false);
            }
            var client = construction.Client;
            var middleware = builder.Config.ClientMiddleware?.SpeechToText;
            if (middleware is not null)
                for (var index = middleware.Count - 1; index >= 0; index--)
                    client = middleware[index](client, services) ?? throw new InvalidOperationException(
                        "Speech-to-text client middleware returned null.");
            return construction with { Client = client };
        }

        private sealed class BorrowedClientOwner : IAsyncDisposable
        {
            internal static BorrowedClientOwner Instance { get; } = new();
            public ValueTask DisposeAsync() => ValueTask.CompletedTask;
        }
    }
}
