using HPD.Agent;
using HPD.Agent.Audio.AgentIntegration;
using HPD.Agent.Audio.AgentIntegration.Thread;
using HPD.Agent.Audio.AgentIntegration.Middleware;
using HPD.Agent.Audio.Ledger;

namespace HPD.Agent.Audio;

public static class AgentBuilderAudioRuntimeExtensions
{
    public static AgentBuilder WithAudio(this AgentBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        return builder.WithAudio(_ => { });
    }

    public static AgentBuilder WithAudio(
        this AgentBuilder builder,
        Action<AudioConfig> configure)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(configure);

        var audio = builder.Config.Audio ?? new AudioConfig();
        configure(audio);
        builder.Config.Audio = audio;

        return builder.WithAudioRuntimeAttachment(
            AudioRuntimeOptionsCompiler.Compile(
                new AudioRuntimeAttachmentOptions(),
                audio,
                textToSpeech: ResolveTextToSpeech(builder)));
    }

    public static AgentBuilder WithAudioRuntimeAttachment(this AgentBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        return builder.WithAudioRuntimeAttachment(
            AudioRuntimeOptionsCompiler.Compile(
                new AudioRuntimeAttachmentOptions(),
                builder.Config.Audio,
                textToSpeech: ResolveTextToSpeech(builder)));
    }

    public static AgentBuilder WithAudioRuntimeAttachment(
        this AgentBuilder builder,
        Action<AudioRuntimeAttachmentOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(configure);

        var options = new AudioRuntimeAttachmentOptions();
        configure(options);
        return builder.WithAudioRuntimeAttachment(
            AudioRuntimeOptionsCompiler.Compile(
                options,
                builder.Config.Audio,
                textToSpeech: ResolveTextToSpeech(builder)));
    }

    public static AgentBuilder WithAudioRuntimeAttachment(
        this AgentBuilder builder,
        AudioRuntimeAttachmentOptions options)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(options);

        options = AudioRuntimeCompositionRegistryV1.Apply(builder, options);
        return builder.WithMiddleware(new AudioRuntimeAttachment(options));
    }

    public static AgentBuilder WithAudioRuntimeAttachment(
        this AgentBuilder builder,
        IThreadProjectionSink threadProjectionSink)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(threadProjectionSink);

        return builder.WithAudioRuntimeAttachment(new AudioRuntimeAttachmentOptions
        {
            ThreadProjectionSink = threadProjectionSink
        });
    }

    public static AgentBuilder WithAudioRuntimeAttachment(
        this AgentBuilder builder,
        ISessionStore sessionStore)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(sessionStore);

        return builder.WithAudioRuntimeAttachment(new SessionThreadProjectionSink(sessionStore));
    }

    private static TextToSpeechClientConfig? ResolveTextToSpeech(AgentBuilder builder) =>
        builder.Config.ResolveClientConfig(global::HPD.Agent.Providers.ProviderClientFamily.TextToSpeech)
            as TextToSpeechClientConfig;
}
