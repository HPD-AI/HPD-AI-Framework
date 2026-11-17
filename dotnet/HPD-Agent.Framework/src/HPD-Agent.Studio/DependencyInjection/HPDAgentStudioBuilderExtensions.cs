using HPD.AI.Platform.Studio;
using HPD.Agent.Studio;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace HPD.AI.Platform;

public static class HPDAgentStudioBuilderExtensions
{
    public static HPDAIPlatformBuilder AddAgentStudio(this HPDAIPlatformBuilder builder)
        => builder.AddAgentStudio(static _ => { });

    public static HPDAIPlatformBuilder AddAgentStudio(
        this HPDAIPlatformBuilder builder,
        Action<AgentStudioTelemetryOptions> configureTelemetry)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(configureTelemetry);

        builder.AddStudioEditionModuleAsset(HPD.Agent.Studio.AgentStudioModuleRegistry.CreateEditionAssetContribution());
        builder.Services.AddOptions<AgentStudioTelemetryOptions>().Configure(configureTelemetry);
        builder.Services.TryAddSingleton<IAgentStudioTelemetryStore>(static provider =>
        {
            AgentStudioTelemetryOptions options =
                provider.GetRequiredService<Microsoft.Extensions.Options.IOptions<AgentStudioTelemetryOptions>>().Value;
            return String.IsNullOrWhiteSpace(options.SpanStorePath)
                ? new InMemoryAgentStudioTelemetryStore(
                    Microsoft.Extensions.Options.Options.Create(options))
                : new FileAgentStudioTelemetryStore(
                    Microsoft.Extensions.Options.Options.Create(options));
        });
        builder.Services.AddOptions<AgentStudioPayloadOptions>();
        builder.Services.TryAddSingleton<IAgentStudioEventPayloadAuthority,
            DefaultAgentStudioEventPayloadAuthority>();
        builder.Services.TryAddEnumerable(ServiceDescriptor.Singleton<IHostedService,
            AgentStudioActivityTelemetryCollector>());
        builder.Services.TryAddSingleton<HPD.Agent.Studio.IAgentStudioInspectionAuthority,
            HPD.Agent.Studio.DefaultAgentStudioInspectionAuthority>();
        builder.Services.TryAddSingleton<HPD.Agent.Studio.IAgentStudioConfigurationAuthority,
            HPD.Agent.Studio.DefaultAgentStudioConfigurationAuthority>();
        builder.Services.TryAddEnumerable(ServiceDescriptor.Singleton<IStudioFrameworkEndpointSurface,
            HPD.Agent.Studio.AgentStudioEndpointSurface>());
        builder.Services.TryAddEnumerable(ServiceDescriptor.Singleton<IStudioModuleRuntimeContributionFactory,
            HPD.Agent.Studio.AgentStudioRuntimeContributionFactory>());
        return builder.AddStudioModule<HPD.Agent.Studio.AgentStudioModuleContribution>();
    }
}
