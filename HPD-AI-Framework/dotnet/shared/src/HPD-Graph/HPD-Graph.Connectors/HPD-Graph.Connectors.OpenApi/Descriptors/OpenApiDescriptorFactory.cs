using HPD.OpenApi.Core.Model;
using HPDAgent.Graph.Abstractions.Discovery;
using HPDAgent.Graph.Connectors.Abstractions.Actions;
using HPDAgent.Graph.Connectors.Abstractions.Configuration;
using HPDAgent.Graph.Connectors.OpenApi.Handlers;
using HPDAgent.Graph.Core.Context;

namespace HPDAgent.Graph.Connectors.OpenApi.Descriptors;

public static class OpenApiDescriptorFactory
{
    public static HandlerDescriptor CreateHandlerDescriptor(
        string connectorId,
        RestApiOperation operation)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectorId);
        ArgumentNullException.ThrowIfNull(operation);

        var operationId = RequireOperationId(operation);
        return new HandlerDescriptor
        {
            HandlerName = $"{connectorId}.{operationId}",
            DisplayName = ToDisplayName(operationId),
            Domain = connectorId,
            HandlerType = typeof(OpenApiCallOperationHandler).FullName!,
            ContextType = typeof(GraphContext).FullName!,
            Description = operation.Description,
            Inputs = CreateInputs(operation),
            Outputs =
            [
                new SocketDescriptor
                {
                    Name = "response",
                    TypeName = typeof(object).FullName!,
                    Direction = SocketDirection.Output,
                    Required = false
                },
                new SocketDescriptor
                {
                    Name = "error",
                    TypeName = typeof(object).FullName!,
                    Direction = SocketDirection.Output,
                    Required = false
                }
            ],
            Metadata = new Dictionary<string, string>
            {
                ["connector.id"] = connectorId,
                ["connector.actionType"] = $"{connectorId}.{operationId}",
                ["openapi.operationId"] = operationId,
                ["openapi.method"] = operation.Method.Method,
                ["openapi.path"] = operation.Path
            }
        };
    }

    public static ConnectorActionDescriptor CreateConnectorActionDescriptor(
        string connectorId,
        RestApiOperation operation)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectorId);
        ArgumentNullException.ThrowIfNull(operation);

        var operationId = RequireOperationId(operation);
        return new ConnectorActionDescriptor
        {
            ActionType = $"{connectorId}.{operationId}",
            HandlerName = OpenApiCallOperationHandler.Name,
            AppId = connectorId,
            DisplayName = ToDisplayName(operationId),
            Fields = CreateFields(operation),
            Traits = InferTraits(operation),
            Metadata = new Dictionary<string, string>
            {
                ["openapi.operationId"] = operationId,
                ["openapi.method"] = operation.Method.Method,
                ["openapi.path"] = operation.Path
            }
        };
    }

    private static IReadOnlyList<SocketDescriptor> CreateInputs(RestApiOperation operation)
    {
        var inputs = new List<SocketDescriptor>
        {
            new()
            {
                Name = "arguments",
                TypeName = typeof(object).FullName!,
                Direction = SocketDirection.Input,
                Required = false,
                Description = "OpenAPI operation arguments."
            }
        };

        foreach (var parameter in operation.Parameters)
        {
            inputs.Add(new SocketDescriptor
            {
                Name = parameter.Name,
                TypeName = TypeNameFor(parameter.Type),
                Direction = SocketDirection.Input,
                Required = parameter.IsRequired,
                Description = parameter.Description
            });
        }

        foreach (var property in operation.Payload?.Properties ?? [])
        {
            inputs.Add(new SocketDescriptor
            {
                Name = property.Name,
                TypeName = TypeNameFor(property.Type),
                Direction = SocketDirection.Input,
                Required = property.IsRequired,
                Description = property.Description
            });
        }

        return inputs;
    }

    private static IReadOnlyList<ConnectorFieldDescriptor> CreateFields(RestApiOperation operation)
    {
        var fields = new List<ConnectorFieldDescriptor>();
        foreach (var parameter in operation.Parameters)
        {
            fields.Add(new ConnectorFieldDescriptor
            {
                Name = parameter.Name,
                TypeName = TypeNameFor(parameter.Type),
                Required = parameter.IsRequired,
                JsonSchema = parameter.Schema
            });
        }

        foreach (var property in operation.Payload?.Properties ?? [])
        {
            fields.Add(new ConnectorFieldDescriptor
            {
                Name = property.Name,
                TypeName = TypeNameFor(property.Type),
                Required = property.IsRequired,
                JsonSchema = property.Schema
            });
        }

        return fields;
    }

    private static ConnectorOperationTraits InferTraits(RestApiOperation operation)
    {
        if (operation.Method == HttpMethod.Get || operation.Method == HttpMethod.Head)
            return ConnectorOperationTraits.ReadOnly | ConnectorOperationTraits.Idempotent;
        if (operation.Method == HttpMethod.Delete)
            return ConnectorOperationTraits.Destructive;
        if (operation.Method == HttpMethod.Put)
            return ConnectorOperationTraits.Idempotent;
        return ConnectorOperationTraits.OpenWorld;
    }

    private static string RequireOperationId(RestApiOperation operation)
        => string.IsNullOrWhiteSpace(operation.Id)
            ? throw new ArgumentException("OpenAPI operation requires an operation id.", nameof(operation))
            : operation.Id!;

    private static string TypeNameFor(string? schemaType)
        => schemaType switch
        {
            "integer" => typeof(long).FullName!,
            "number" => typeof(double).FullName!,
            "boolean" => typeof(bool).FullName!,
            "array" => typeof(object[]).FullName!,
            "object" => typeof(object).FullName!,
            _ => typeof(string).FullName!
        };

    private static string ToDisplayName(string value)
    {
        var chars = value
            .Replace("_", " ", StringComparison.Ordinal)
            .Replace("-", " ", StringComparison.Ordinal)
            .Select((ch, index) => index > 0 && char.IsUpper(ch) ? " " + ch : ch.ToString());
        var text = string.Concat(chars).Trim();
        return string.IsNullOrWhiteSpace(text) ? value : char.ToUpperInvariant(text[0]) + text[1..];
    }
}
