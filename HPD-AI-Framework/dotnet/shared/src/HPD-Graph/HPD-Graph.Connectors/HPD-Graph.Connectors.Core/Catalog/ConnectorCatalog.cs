using HPDAgent.Graph.Connectors.Abstractions.Descriptors;

namespace HPDAgent.Graph.Connectors.Core.Catalog;

public interface IConnectorCatalog
{
    IReadOnlyList<ConnectorPackageDescriptor> ListConnectors();
    ConnectorPackageDescriptor? GetConnector(string connectorId);
}

public sealed class ConnectorCatalog : IConnectorCatalog
{
    private readonly IReadOnlyList<ConnectorPackageDescriptor> _connectors;

    public ConnectorCatalog(IEnumerable<ConnectorPackageDescriptor> connectors)
    {
        ArgumentNullException.ThrowIfNull(connectors);
        _connectors = connectors
            .OrderBy(static connector => connector.ConnectorId, StringComparer.Ordinal)
            .ToArray();
    }

    public IReadOnlyList<ConnectorPackageDescriptor> ListConnectors() => _connectors;

    public ConnectorPackageDescriptor? GetConnector(string connectorId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectorId);

        return _connectors.FirstOrDefault(
            connector => string.Equals(connector.ConnectorId, connectorId, StringComparison.Ordinal));
    }
}
