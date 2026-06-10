using HPD.Graph.Connectors.Abstractions.Connections;
using HPD.Graph.Connectors.Abstractions.Sources;
using HPD.Graph.Connectors.Core.Catalog;
using HPD.Graph.Connectors.Core.Connections;
using HPD.Graph.Connectors.Core.Dedupe;
using HPD.Graph.Connectors.Core.Dispatch;
using HPD.Graph.Connectors.Core.IO;
using HPD.Graph.Connectors.Core.Materialization;
using HPD.Graph.Connectors.Core.Polling;
using HPD.Graph.Connectors.Core.Sources;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace HPD.Graph.Connectors.Core.DependencyInjection;

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
