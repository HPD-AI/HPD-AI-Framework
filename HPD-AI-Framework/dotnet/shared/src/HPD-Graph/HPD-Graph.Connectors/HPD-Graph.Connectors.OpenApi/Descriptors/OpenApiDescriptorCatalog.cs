using HPDAgent.Graph.Abstractions.Discovery;
using HPDAgent.Graph.Connectors.Abstractions.Actions;
using HPDAgent.Graph.Connectors.OpenApi.Catalog;

namespace HPDAgent.Graph.Connectors.OpenApi.Descriptors;

public interface IOpenApiDescriptorCatalog
{
    IReadOnlyDictionary<string, HandlerDescriptor> GetHandlers();
    IReadOnlyList<ConnectorActionDescriptor> GetActions();
}

public sealed class OpenApiDescriptorCatalog : IOpenApiDescriptorCatalog
{
    private readonly IReadOnlyList<OpenApiOperationRegistration> _registrations;

    public OpenApiDescriptorCatalog(IEnumerable<OpenApiOperationRegistration> registrations)
    {
        ArgumentNullException.ThrowIfNull(registrations);
        _registrations = registrations
            .Where(registration => !string.IsNullOrWhiteSpace(registration.Operation.Id))
            .ToArray();
    }

    public IReadOnlyDictionary<string, HandlerDescriptor> GetHandlers()
    {
        return _registrations.ToDictionary(
            registration => $"{registration.ConnectorId}.{registration.Operation.Id}",
            registration => OpenApiDescriptorFactory.CreateHandlerDescriptor(
                registration.ConnectorId,
                registration.Operation),
            StringComparer.Ordinal);
    }

    public IReadOnlyList<ConnectorActionDescriptor> GetActions()
    {
        return _registrations
            .Select(registration => OpenApiDescriptorFactory.CreateConnectorActionDescriptor(
                registration.ConnectorId,
                registration.Operation))
            .ToArray();
    }
}
