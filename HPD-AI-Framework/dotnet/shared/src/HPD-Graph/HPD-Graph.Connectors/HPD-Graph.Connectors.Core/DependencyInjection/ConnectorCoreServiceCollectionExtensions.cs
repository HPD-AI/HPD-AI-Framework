using HPDAgent.Graph.Connectors.Abstractions.Connections;
using HPDAgent.Graph.Connectors.Abstractions.Sources;
using HPDAgent.Graph.Connectors.Core.Catalog;
using HPDAgent.Graph.Connectors.Core.Connections;
using HPDAgent.Graph.Connectors.Core.Dedupe;
using HPDAgent.Graph.Connectors.Core.Dispatch;
using HPDAgent.Graph.Connectors.Core.IO;
using HPDAgent.Graph.Connectors.Core.Materialization;
using HPDAgent.Graph.Connectors.Core.Polling;
using HPDAgent.Graph.Connectors.Core.Sources;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace HPDAgent.Graph.Connectors.Core.DependencyInjection;

public static class ConnectorCoreServiceCollectionExtensions
{
    public static IServiceCollection AddHPDGraphConnectorsCore(
        this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.TryAddSingleton<IWorkflowSourceStore, InMemoryWorkflowSourceStore>();
        services.TryAddSingleton<IConnectionStore, InMemoryConnectionStore>();
        services.TryAddSingleton<IConnectionProvider, StoreBackedConnectionProvider>();
        services.TryAddSingleton<IWorkflowSourceDedupeService, WorkflowSourceDedupeService>();
        services.TryAddSingleton<IWorkflowSourceDispatcher, WorkflowSourceDispatcher>();
        services.TryAddSingleton<IWorkflowSourcePollingService, WorkflowSourcePollingService>();
        services.TryAddSingleton<WorkflowSourcePollingOptions>();
        services.TryAddSingleton<IWorkflowSourcePollingBackgroundService, WorkflowSourcePollingBackgroundService>();
        services.TryAddSingleton<IConnectorCatalog, ConnectorCatalog>();
        services.TryAddSingleton<IConnectorAssetCatalog, ConnectorAssetCatalog>();
        services.TryAddSingleton<IConnectorArtifactEventRecorder, ConnectorArtifactEventRecorder>();
        services.TryAddSingleton<IConnectorMaterializationDispatcher, ConnectorMaterializationDispatcher>();
        services.TryAddSingleton<IArtifactIOManagerRegistry, ArtifactIOManagerRegistry>();

        return services;
    }
}
