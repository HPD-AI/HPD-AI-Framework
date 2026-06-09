using HPDAgent.Graph.Abstractions.Artifacts;
using HPDAgent.Graph.Abstractions.Checkpointing;
using HPDAgent.Graph.Abstractions.Config;
using HPDAgent.Graph.Abstractions.Discovery;
using HPDAgent.Graph.Abstractions.Registry;
using HPDAgent.Graph.Abstractions.Serialization;
using HPDAgent.Graph.Abstractions.Storage;
using HPDAgent.Graph.AspNetCore.Hosting;
using HPDAgent.Graph.AspNetCore.Serialization;
using HPDAgent.Graph.Core.Artifacts;
using HPDAgent.Graph.Core.Checkpointing;
using HPDAgent.Graph.Core.Registry;
using HPDAgent.Graph.Core.Storage;
using HPDAgent.Graph.Hosting.Lifecycle;
using HPDAgent.Graph.Hosting.Serialization;
using HPD.Events;
using HPD.Events.Core;
using Microsoft.AspNetCore.Http.Json;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;

namespace HPDAgent.Graph.AspNetCore.DependencyInjection;

public static class GraphAspNetCoreServiceCollectionExtensions
{
    public static IServiceCollection AddHPDGraphAspNetCore(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.TryAddSingleton<IGraphDefinitionStore, InMemoryGraphDefinitionStore>();
        services.TryAddSingleton<IWorkflowExecutionStore, InMemoryWorkflowExecutionStore>();
        services.TryAddSingleton<IWorkflowLogStore, InMemoryWorkflowLogStore>();
        services.TryAddSingleton<IScheduledGraphStore, InMemoryScheduledGraphStore>();
        services.TryAddSingleton<IGraphCheckpointStore, InMemoryCheckpointStore>();
        services.TryAddSingleton<IEventCoordinator, EventCoordinator>();
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
        services.AddOptions<JsonOptions>();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IConfigureOptions<JsonOptions>, GraphJsonOptionsSetup>());

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

        services.AddHPDGraphAspNetCore();
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

    private sealed class GraphJsonOptionsSetup(
        IEnumerable<IGraphJsonTypeInfoResolverContributor> contributors) : IConfigureOptions<JsonOptions>
    {
        public void Configure(JsonOptions options)
        {
            var chain = options.SerializerOptions.TypeInfoResolverChain;
            chain.Insert(0, GraphAspNetCoreJsonSerializerContext.Default);
            chain.Insert(1, GraphHostingJsonSerializerContext.Default);
            chain.Insert(2, GraphConfigJsonSerializerContext.Default);

            var insertIndex = 3;
            foreach (var contributor in contributors)
            {
                chain.Insert(insertIndex++, contributor.Resolver);
            }
        }
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
