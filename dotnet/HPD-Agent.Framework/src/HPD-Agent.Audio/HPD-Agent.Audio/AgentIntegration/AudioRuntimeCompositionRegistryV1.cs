using System.Runtime.CompilerServices;

namespace HPD.Agent.Audio;

internal interface IAudioRuntimeCompositionV1
{
    AudioRuntimeAttachmentOptions Apply(
        AgentBuilder builder,
        AudioRuntimeAttachmentOptions options);
}

internal static class AudioRuntimeCompositionRegistryV1
{
    private static readonly ConditionalWeakTable<AgentBuilder, IAudioRuntimeCompositionV1> Registrations = new();

    internal static void Register(AgentBuilder builder, IAudioRuntimeCompositionV1 composition)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(composition);
        if (!Registrations.TryAdd(builder, composition))
            throw new InvalidOperationException("An Audio runtime composition is already registered for this builder.");
    }

    internal static AudioRuntimeAttachmentOptions Apply(
        AgentBuilder builder,
        AudioRuntimeAttachmentOptions options) =>
        Registrations.TryGetValue(builder, out var composition)
            ? composition.Apply(builder, options)
            : options;
}
