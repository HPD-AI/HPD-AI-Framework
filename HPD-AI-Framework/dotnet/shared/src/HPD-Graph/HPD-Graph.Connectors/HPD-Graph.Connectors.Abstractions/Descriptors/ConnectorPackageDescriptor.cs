using HPDAgent.Graph.Abstractions.Discovery;
using HPDAgent.Graph.Connectors.Abstractions.Actions;
using HPDAgent.Graph.Connectors.Abstractions.Assets;
using HPDAgent.Graph.Connectors.Abstractions.Configuration;
using HPDAgent.Graph.Connectors.Abstractions.Connections;
using HPDAgent.Graph.Connectors.Abstractions.Sources;

namespace HPDAgent.Graph.Connectors.Abstractions.Descriptors;

public sealed record ConnectorPackageDescriptor
{
    public required string ConnectorId { get; init; }
    public required string DisplayName { get; init; }
    public string? Description { get; init; }
    public string? Version { get; init; }
    public string? IconUri { get; init; }

    public IReadOnlyList<AppDescriptor> Apps { get; init; } = [];
    public IReadOnlyList<ConnectionDescriptor> Connections { get; init; } = [];
    public IReadOnlyList<ConnectorConfigDescriptor> Configs { get; init; } = [];
    public IReadOnlyList<WorkflowSourceDescriptor> Sources { get; init; } = [];
    public IReadOnlyList<HandlerDescriptor> Actions { get; init; } = [];
    public IReadOnlyList<ConnectorActionDescriptor> ConnectorActions { get; init; } = [];
    public IReadOnlyList<ConnectorAssetDescriptor> Assets { get; init; } = [];
    public IReadOnlyList<ConnectorAssetCheckDescriptor> AssetChecks { get; init; } = [];
    public IReadOnlyList<ConnectorAssetObservationDescriptor> AssetObservations { get; init; } = [];
    public IReadOnlyList<string> ArtifactIOManagers { get; init; } = [];
    public IReadOnlyList<string> OptionProviders { get; init; } = [];

    public IReadOnlyDictionary<string, string> Metadata { get; init; }
        = new Dictionary<string, string>();
}

public sealed record AppDescriptor
{
    public required string AppId { get; init; }
    public required string DisplayName { get; init; }
    public string? Description { get; init; }
    public string? IconUri { get; init; }
    public IReadOnlyDictionary<string, string> Metadata { get; init; }
        = new Dictionary<string, string>();
}
