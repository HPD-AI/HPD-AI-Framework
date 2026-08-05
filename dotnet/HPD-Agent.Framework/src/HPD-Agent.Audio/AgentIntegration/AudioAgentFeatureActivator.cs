using System.Runtime.CompilerServices;
using HPD.Agent.Audio.AgentIntegration.Middleware;

namespace HPD.Agent.Audio.AgentIntegration;

public static class AudioAgentFeatureActivator
{
#pragma warning disable CA2255
    [ModuleInitializer]
#pragma warning restore CA2255
    public static void Register()
    {
        AgentFeatureActivatorRegistry.Register("HPD.Agent.Audio", Activate);
    }

    private static void Activate(AgentBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        if (builder.Middlewares.Any(middleware => middleware is AudioRuntimeAttachment))
        {
            return;
        }

        var options = AudioRuntimeOptionsCompiler.Compile(
            new AudioRuntimeAttachmentOptions(),
            builder.Config.Audio,
            textToSpeech: builder.Config.ResolveClientConfig(global::HPD.Agent.Providers.ProviderClientFamily.TextToSpeech)
                as TextToSpeechClientConfig);
        builder.Middlewares.Add(new AudioRuntimeAttachment(options));
    }
}
