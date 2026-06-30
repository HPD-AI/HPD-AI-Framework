using HPD.Events;
using HPD.Events.DependencyInjection;
using HPD.Graph.Abstractions.Artifacts;
using HPD.Graph.Abstractions.Checkpointing;
using HPD.Graph.Abstractions.Config;
using HPD.Graph.Abstractions.Discovery;
using HPD.Graph.Abstractions.Registry;
using HPD.Graph.Abstractions.Serialization;
using HPD.Graph.Abstractions.Storage;
using HPD.Graph.Core.Artifacts;
using HPD.Graph.Core.Checkpointing;
using HPD.Graph.Core.Registry;
using HPD.Graph.Core.Storage;
using HPD.Graph.Hosting.Hosting;
using HPD.Graph.Hosting.Lifecycle;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;

namespace HPD.Graph.Hosting.DependencyInjection;

public static class GraphHostingServiceCollectionExtensions
{
    public static IServiceCollection AddHPDGraphHosting(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.TryAddSingleton<IGraphDefinitionStore, InMemoryGraphDefinitionStore>();
        services.TryAddSingleton<IWorkflowExecutionStore, InMemoryWorkflowExecutionStore>();
        services.TryAddSingleton<IWorkflowLogStore, InMemoryWorkflowLogStore>();
        services.TryAddSingleton<IScheduledGraphStore, InMemoryScheduledGraphStore>();
        services.TryAddSingleton<IGraphCheckpointStore, InMemoryCheckpointStore>();
        services.AddHPDEvents();
        services.TryAddSingleton<IGeneratedHandlerCatalog, EmptyGeneratedHandlerCatalog>();
        services.TryAddSingleton<IWorkflowResumeRunner, InProcessWorkflowResumeRunner>();
        services.TryAddSingleton<GraphManager>();
        services.TryAddSingleton<ExecutionManager>();
        services.TryAddSingleton<IWorkflowExecutionRunner, InProcessWorkflowExecutionRunner>();
        services.TryAddSingleton<InProcessCronScheduleProvider>();
        services.TryAddSingleton<IScheduleProvider>(sp => sp.GetRequiredService<InProcessCronScheduleProvider>());
        services.TryAddSingleton<IScheduleTriggerProvider>(sp => sp.GetRequiredService<InProcessCronScheduleProvider>());
        services.TryAddSingleton<SchedulingManager>();
        services.TryAddSingleton<IWorkflowExecutionStateSink>(sp => sp.GetRequiredService<ExecutionManager>());
        services.TryAddSingleton<IWorkflowSuspensionSink>(sp => sp.GetRequiredService<ExecutionManager>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IHostedService, WorkflowExecutionBackgroundService>());

        return services;
    }

    public static IServiceCollection AddHPDGraphWorkflowFromConfigFile(
        this IServiceCollection services,
        string configPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(configPath);

        var config = GraphConfigSerializer.ReadConfigFile(configPath)
            ?? throw new InvalidOperationException($"Failed to load HPD graph config from '{configPath}'.");

        return services.AddHPDGraphWorkflow(config);
    }

    public static IServiceCollection AddHPDGraphWorkflow(
        this IServiceCollection services,
        GraphConfig config)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(config);

        services.AddHPDGraphHosting();
        services.AddSingleton(new SeedGraphDefinition(config));
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IHostedService, SeedGraphDefinitionHostedService>());
        return services;
    }

    public static IServiceCollection AddHPDGraphMaterialization(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.TryAddSingleton<IArtifactRegistry, InMemoryArtifactRegistry>();
        services.TryAddSingleton<IGraphRegistry, InMemoryGraphRegistry>();

        return services;
    }

    private sealed class EmptyGeneratedHandlerCatalog : IGeneratedHandlerCatalog
    {
        public IReadOnlyDictionary<string, HandlerDescriptor> GetHandlers()
        {
            return new Dictionary<string, HandlerDescriptor>(StringComparer.Ordinal);
        }
    }

    private sealed record SeedGraphDefinition(GraphConfig Config);

    private sealed class SeedGraphDefinitionHostedService(
        IEnumerable<SeedGraphDefinition> definitions,
        GraphManager graphManager) : IHostedService
    {
        public async Task StartAsync(CancellationToken cancellationToken)
        {
            foreach (var definition in definitions)
            {
                await graphManager.CreateDefinitionAsync(definition.Config, cancellationToken)
                    .ConfigureAwait(false);
            }
        }

        public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
