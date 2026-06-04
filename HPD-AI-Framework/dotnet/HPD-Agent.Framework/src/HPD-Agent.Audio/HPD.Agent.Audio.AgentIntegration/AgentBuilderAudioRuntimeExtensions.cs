using HPD.Agent;
using HPD.Agent.Audio.AgentIntegration.Branch;
using HPD.Agent.Audio.AgentIntegration.Middleware;
using HPD.Agent.Audio.Ledger;

namespace HPD.Agent.Audio.AgentIntegration;

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
                audio));
    }

    public static AgentBuilder WithAudioRuntimeAttachment(this AgentBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        return builder.WithAudioRuntimeAttachment(
            AudioRuntimeOptionsCompiler.Compile(
                new AudioRuntimeAttachmentOptions(),
                builder.Config.Audio));
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
            AudioRuntimeOptionsCompiler.Compile(options, builder.Config.Audio));
    }

    public static AgentBuilder WithAudioRuntimeAttachment(
        this AgentBuilder builder,
        AudioRuntimeAttachmentOptions options)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(options);

        return builder.WithMiddleware(new AudioRuntimeAttachment(options));
    }

    public static AgentBuilder WithAudioRuntimeAttachment(
        this AgentBuilder builder,
        IBranchProjectionSink branchProjectionSink)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(branchProjectionSink);

        return builder.WithAudioRuntimeAttachment(new AudioRuntimeAttachmentOptions
        {
            BranchProjectionSink = branchProjectionSink
        });
    }

    public static AgentBuilder WithAudioRuntimeAttachment(
        this AgentBuilder builder,
        ISessionRepository sessionRepository)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(sessionRepository);

        return builder.WithAudioRuntimeAttachment(new SessionBranchProjectionSink(sessionRepository));
    }

}
