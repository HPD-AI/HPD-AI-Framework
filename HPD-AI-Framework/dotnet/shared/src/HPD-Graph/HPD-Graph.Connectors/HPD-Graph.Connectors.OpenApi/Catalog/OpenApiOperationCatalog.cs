using HPD.OpenApi.Core.Model;

namespace HPDAgent.Graph.Connectors.OpenApi.Catalog;

public sealed record OpenApiOperationRegistration(
    string ConnectorId,
    RestApiOperation Operation);

public interface IOpenApiOperationCatalog
{
    RestApiOperation? GetOperation(string connectorId, string operationId);
    IReadOnlyList<RestApiOperation> ListOperations(string connectorId);
}

public sealed class OpenApiOperationCatalog : IOpenApiOperationCatalog
{
    private readonly Dictionary<string, Dictionary<string, RestApiOperation>> _operations;

    public OpenApiOperationCatalog(IEnumerable<OpenApiOperationRegistration> registrations)
    {
        ArgumentNullException.ThrowIfNull(registrations);

        _operations = new Dictionary<string, Dictionary<string, RestApiOperation>>(StringComparer.Ordinal);
        foreach (var registration in registrations)
        {
            if (string.IsNullOrWhiteSpace(registration.ConnectorId))
                continue;
            if (string.IsNullOrWhiteSpace(registration.Operation.Id))
                continue;

            if (!_operations.TryGetValue(registration.ConnectorId, out var connectorOperations))
            {
                connectorOperations = new Dictionary<string, RestApiOperation>(StringComparer.Ordinal);
                _operations[registration.ConnectorId] = connectorOperations;
            }

            connectorOperations[registration.Operation.Id!] = registration.Operation;
        }
    }

    public RestApiOperation? GetOperation(string connectorId, string operationId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectorId);
        ArgumentException.ThrowIfNullOrWhiteSpace(operationId);

        return _operations.TryGetValue(connectorId, out var connectorOperations) &&
            connectorOperations.TryGetValue(operationId, out var operation)
                ? operation
                : null;
    }

    public IReadOnlyList<RestApiOperation> ListOperations(string connectorId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectorId);

        return _operations.TryGetValue(connectorId, out var connectorOperations)
            ? connectorOperations.Values.ToArray()
            : Array.Empty<RestApiOperation>();
    }
}
