using Microsoft.Extensions.DependencyInjection;

namespace HPD.Agent.Audio.LiveKit;

public static class ManagedLiveKitBuilderExtensions
{
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
}
