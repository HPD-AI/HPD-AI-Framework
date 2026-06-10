using System.Text.Json;
using HPD.OpenApi.Core;
using HPD.Graph.Abstractions.Execution;
using HPD.Graph.Abstractions.Handlers;
using HPD.Graph.Connectors.Abstractions.Connections;
using HPD.Graph.Connectors.OpenApi.Catalog;
using HPD.Graph.Core.Context;

namespace HPD.Graph.Connectors.OpenApi.Handlers;

public sealed record OpenApiCallOperationConfig
{
    public required string ConnectorId { get; init; }
    public required string ConnectionId { get; init; }
    public required string OperationId { get; init; }
    public Uri? ServerUrlOverride { get; init; }
    public JsonElement? Arguments { get; init; }
}

public sealed class OpenApiCallOperationHandler : IGraphNodeHandler<GraphContext>
{
    public const string Name = "openapi.call_operation";

    private readonly IOpenApiOperationCatalog _operations;
    private readonly IConnectionProvider _connections;
    private readonly IEnumerable<IOpenApiConnectionAdapter> _adapters;

    public OpenApiCallOperationHandler(
        IOpenApiOperationCatalog operations,
        IConnectionProvider connections,
        IEnumerable<IOpenApiConnectionAdapter> adapters)
    {
        _operations = operations;
        _connections = connections;
        _adapters = adapters;
    }

    public string HandlerName => Name;

    public async Task<NodeExecutionResult> ExecuteAsync(
        GraphContext context,
        HandlerInputs inputs,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(inputs);

        var config = ReadConfig(context);
        var operation = _operations.GetOperation(config.ConnectorId, config.OperationId)
            ?? throw new InvalidOperationException(
                $"OpenAPI operation '{config.OperationId}' for connector '{config.ConnectorId}' was not found.");

        var connection = await _connections.ResolveAsync(config.ConnectionId, cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException($"Connector connection '{config.ConnectionId}' could not be resolved.");
        var adapter = _adapters.FirstOrDefault(a => a.CanAdapt(connection))
            ?? throw new InvalidOperationException(
                $"No OpenAPI connection adapter could adapt connection '{config.ConnectionId}' ({connection.ConnectionType}).");

        var coreConfig = adapter.CreateConfig(connection, cancellationToken);
        var httpClient = coreConfig.HttpClient
            ?? throw new InvalidOperationException(
                $"OpenAPI connection adapter for '{connection.ConnectionId}' did not provide an HttpClient.");

        var arguments = BuildArguments(config, inputs);
        var runner = new OpenApiOperationRunner(
            httpClient,
            coreConfig.AuthCallback,
            coreConfig.UserAgent,
            coreConfig.EnableDynamicPayload,
            coreConfig.EnablePayloadNamespacing,
            coreConfig.ErrorDetector);

        var result = await runner
            .RunAsync(operation, arguments, config.ServerUrlOverride ?? coreConfig.ServerUrlOverride, cancellationToken)
            .ConfigureAwait(false);

        var output = new Dictionary<string, object>();
        if (result is OpenApiErrorResponse error)
        {
            output["error"] = error;
        }
        else if (result is not null)
        {
            output["response"] = result;
        }

        return NodeExecutionResult.Success.Single(
            output,
            TimeSpan.Zero,
            new NodeExecutionMetadata());
    }

    private static OpenApiCallOperationConfig ReadConfig(GraphContext context)
    {
        var node = context.CurrentNodeId is null ? null : context.Graph.GetNode(context.CurrentNodeId);
        if (node?.Config is null)
            throw new InvalidOperationException("OpenAPI operation handler requires node config.");

        return JsonSerializer.Deserialize(
            node.Config.Value.GetRawText(),
            OpenApiConnectorJsonSerializerContext.Default.OpenApiCallOperationConfig)
            ?? throw new InvalidOperationException("OpenAPI operation handler config could not be deserialized.");
    }

    private static IDictionary<string, object?> BuildArguments(
        OpenApiCallOperationConfig config,
        HandlerInputs inputs)
    {
        var builder = new OpenApiArgumentBuilder();
        if (config.Arguments is { } configArguments)
            builder.Merge(configArguments);

        if (inputs.TryGet<IDictionary<string, object?>>("arguments", out var dictionary) && dictionary is not null)
            builder.Merge(dictionary);
        else if (inputs.TryGet<JsonElement>("arguments", out var element))
            builder.Merge(element);

        foreach (var (name, value) in inputs.GetAll())
        {
            if (string.Equals(name, "arguments", StringComparison.Ordinal))
                continue;

            builder.Add(name, value);
        }

        return builder.Build();
    }
}
