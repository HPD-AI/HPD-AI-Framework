using System.Text.Json;
using HPD.Events;
using HPDAgent.Graph.Abstractions.Artifacts;
using HPDAgent.Graph.Connectors.Abstractions.Assets;
using HPDAgent.Graph.Connectors.Abstractions.Connections;
using HPDAgent.Graph.Connectors.Abstractions.Events;
using HPDAgent.Graph.Connectors.Abstractions.Materialization;
using HPDAgent.Graph.Connectors.Abstractions.Options;
using HPDAgent.Graph.Connectors.Abstractions.Sources;
using HPDAgent.Graph.Connectors.AspNetCore.Data;
using HPDAgent.Graph.Connectors.Core.Catalog;
using HPDAgent.Graph.Connectors.Core.IO;
using HPDAgent.Graph.Connectors.Core.Materialization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;

namespace HPDAgent.Graph.Connectors.AspNetCore.EndpointMapping;

public static class ConnectorEndpointRouteBuilderExtensions
{
    public static RouteGroupBuilder MapHPDGraphConnectors(this IEndpointRouteBuilder endpoints, string prefix = "/workflows")
    {
        ArgumentNullException.ThrowIfNull(endpoints);
        ArgumentException.ThrowIfNullOrWhiteSpace(prefix);

        var group = endpoints.MapGroup(prefix);

        group.MapGet("/connectors", GetConnectors);
        group.MapGet("/connectors/{connectorId}", GetConnector);
        group.MapPost("/connectors/{connectorId}/options/{optionProvider}", GetOptionsAsync);
        group.MapGet("/connectors/{connectorId}/assets", GetConnectorAssetsAsync);
        group.MapPost("/connectors/{connectorId}/assets/refresh", RefreshConnectorAssetsAsync);

        group.MapGet("/connections", ListConnectionsAsync);
        group.MapPost("/connections", CreateConnectionAsync);
        group.MapGet("/connections/{connectionId}", GetConnectionAsync);
        group.MapPut("/connections/{connectionId}", UpdateConnectionAsync);
        group.MapDelete("/connections/{connectionId}", DeleteConnectionAsync);

        group.MapGet("/sources", ListSourcesAsync);
        group.MapPost("/{graphId}/sources", CreateSourceAsync);
        group.MapGet("/{graphId}/sources", ListGraphSourcesAsync);
        group.MapGet("/sources/{sourceId}", GetSourceAsync);
        group.MapPut("/sources/{sourceId}", UpdateSourceAsync);
        group.MapDelete("/sources/{sourceId}", DeleteSourceAsync);
        group.MapPost("/sources/{sourceId}/enable", EnableSourceAsync);
        group.MapPost("/sources/{sourceId}/disable", DisableSourceAsync);
        group.MapGet("/sources/{sourceId}/status", GetSourceStatusAsync);
        group.MapGet("/sources/{sourceId}/state", GetSourceStateAsync);
        group.MapPost("/sources/{sourceId}/webhook", ReceiveWebhookAsync);

        group.MapGet("/assets", ListArtifactsAsync);
        group.MapPost("/assets/{artifactKey}/materialize", MaterializeArtifactAsync);
        group.MapPost("/assets/{artifactKey}/backfill", BackfillArtifactAsync);
        group.MapPost("/assets/{artifactKey}/observe", ObserveArtifactAsync);
        group.MapPost("/assets/{artifactKey}/checks", RecordCheckAsync);

        group.MapGet("/artifact-io-managers", ListArtifactIOManagers);
        group.MapGet("/artifact-io-managers/{name}", GetArtifactIOManager);
        group.MapGet("/events", StreamEventsAsync);

        return group;
    }

    private static IResult GetConnectors(IConnectorCatalog catalog) =>
        Results.Ok(new ConnectorListResponse { Connectors = catalog.ListConnectors() });

    private static IResult GetConnector(string connectorId, IConnectorCatalog catalog)
    {
        var connector = catalog.GetConnector(connectorId);
        return connector is null ? Results.NotFound() : Results.Ok(connector);
    }

    private static async Task<IResult> GetOptionsAsync(
        string optionProvider,
        ConnectorOptionRequest request,
        IEnumerable<IConnectorOptionProvider> providers,
        CancellationToken ct)
    {
        var provider = providers.FirstOrDefault(p => string.Equals(p.OptionProviderName, optionProvider, StringComparison.Ordinal));
        if (provider is null)
        {
            return Results.NotFound(new ProblemDetails { Title = "Option provider not found" });
        }

        var options = await provider.GetOptionsAsync(request, ct).ConfigureAwait(false);
        return Results.Ok(new ConnectorOptionPage { Options = options });
    }

    private static async Task<IResult> GetConnectorAssetsAsync(
        IConnectorAssetCatalog catalog,
        CancellationToken ct)
    {
        var assets = await catalog.LoadAssetsAsync(new ConnectorAssetCatalogRequest(), ct).ConfigureAwait(false);
        return Results.Ok(new ConnectorAssetListResponse { Assets = assets });
    }

    private static async Task<IResult> RefreshConnectorAssetsAsync(
        IConnectorAssetCatalog catalog,
        ConnectorAssetCatalogRequest request,
        CancellationToken ct)
    {
        var assets = await catalog.LoadAssetsAsync(request, ct).ConfigureAwait(false);
        return Results.Ok(new ConnectorAssetListResponse { Assets = assets });
    }

    private static async Task<IResult> ListConnectionsAsync(IConnectionStore store, CancellationToken ct)
    {
        var connections = await store.ListAsync(ct).ConfigureAwait(false);
        return Results.Ok(new ConnectionListResponse { Connections = connections });
    }

    private static async Task<IResult> CreateConnectionAsync(ConnectionDefinition connection, IConnectionStore store, CancellationToken ct)
    {
        await store.SaveAsync(connection, ct).ConfigureAwait(false);
        return Results.Created($"/workflows/connections/{Uri.EscapeDataString(connection.ConnectionId)}", connection);
    }

    private static async Task<IResult> GetConnectionAsync(string connectionId, IConnectionStore store, CancellationToken ct)
    {
        var connection = await store.LoadAsync(connectionId, ct).ConfigureAwait(false);
        return connection is null ? Results.NotFound() : Results.Ok(connection);
    }

    private static async Task<IResult> UpdateConnectionAsync(
        string connectionId,
        ConnectionDefinition connection,
        IConnectionStore store,
        CancellationToken ct)
    {
        await store.SaveAsync(connection with { ConnectionId = connectionId }, ct).ConfigureAwait(false);
        return Results.Ok(connection with { ConnectionId = connectionId });
    }

    private static async Task<IResult> DeleteConnectionAsync(string connectionId, IConnectionStore store, CancellationToken ct)
    {
        await store.DeleteAsync(connectionId, ct).ConfigureAwait(false);
        return Results.NoContent();
    }

    private static async Task<IResult> ListSourcesAsync(IWorkflowSourceStore store, CancellationToken ct)
    {
        var sources = await store.ListAsync(ct).ConfigureAwait(false);
        return Results.Ok(new WorkflowSourceListResponse { Sources = sources });
    }

    private static async Task<IResult> CreateSourceAsync(
        string graphId,
        WorkflowSource source,
        IWorkflowSourceStore store,
        CancellationToken ct)
    {
        var saved = source with { GraphId = graphId };
        await store.SaveAsync(saved, ct).ConfigureAwait(false);
        return Results.Created($"/workflows/sources/{Uri.EscapeDataString(saved.SourceId)}", saved);
    }

    private static async Task<IResult> ListGraphSourcesAsync(string graphId, IWorkflowSourceStore store, CancellationToken ct)
    {
        var sources = await store.ListByGraphAsync(graphId, ct).ConfigureAwait(false);
        return Results.Ok(new WorkflowSourceListResponse { Sources = sources });
    }

    private static async Task<IResult> GetSourceAsync(string sourceId, IWorkflowSourceStore store, CancellationToken ct)
    {
        var source = await store.LoadAsync(sourceId, ct).ConfigureAwait(false);
        return source is null ? Results.NotFound() : Results.Ok(source);
    }

    private static async Task<IResult> UpdateSourceAsync(
        string sourceId,
        WorkflowSource source,
        IWorkflowSourceStore store,
        CancellationToken ct)
    {
        var saved = source with { SourceId = sourceId };
        await store.SaveAsync(saved, ct).ConfigureAwait(false);
        return Results.Ok(saved);
    }

    private static async Task<IResult> DeleteSourceAsync(string sourceId, IWorkflowSourceStore store, CancellationToken ct)
    {
        await store.DeleteAsync(sourceId, ct).ConfigureAwait(false);
        return Results.NoContent();
    }

    private static Task<IResult> EnableSourceAsync(string sourceId, IWorkflowSourceStore store, CancellationToken ct) =>
        SetSourceEnabledAsync(sourceId, enabled: true, store, ct);

    private static Task<IResult> DisableSourceAsync(string sourceId, IWorkflowSourceStore store, CancellationToken ct) =>
        SetSourceEnabledAsync(sourceId, enabled: false, store, ct);

    private static async Task<IResult> SetSourceEnabledAsync(
        string sourceId,
        bool enabled,
        IWorkflowSourceStore store,
        CancellationToken ct)
    {
        var source = await store.LoadAsync(sourceId, ct).ConfigureAwait(false);
        if (source is null)
        {
            return Results.NotFound();
        }

        var updated = source with { Enabled = enabled, UpdatedAt = DateTimeOffset.UtcNow };
        await store.SaveAsync(updated, ct).ConfigureAwait(false);
        return Results.Ok(updated);
    }

    private static async Task<IResult> GetSourceStatusAsync(
        string sourceId,
        IWorkflowSourceStore store,
        IEnumerable<IWorkflowSourceProvider> providers,
        CancellationToken ct)
    {
        var source = await store.LoadAsync(sourceId, ct).ConfigureAwait(false);
        if (source is null)
        {
            return Results.NotFound();
        }

        var provider = providers.FirstOrDefault(p => string.Equals(p.SourceType, source.SourceType, StringComparison.Ordinal));
        if (provider is null)
        {
            return Results.Ok(new WorkflowSourceStatus
            {
                SourceId = source.SourceId,
                SourceType = source.SourceType,
                Enabled = source.Enabled,
                Active = false,
                Message = "No provider is registered."
            });
        }

        var statuses = await provider.GetStatusAsync(ct).ConfigureAwait(false);
        var status = statuses.FirstOrDefault(s => string.Equals(s.SourceId, sourceId, StringComparison.Ordinal));
        return Results.Ok(status ?? new WorkflowSourceStatus
        {
            SourceId = source.SourceId,
            SourceType = source.SourceType,
            Enabled = source.Enabled,
            Active = source.Enabled
        });
    }

    private static async Task<IResult> GetSourceStateAsync(string sourceId, IWorkflowSourceStore store, CancellationToken ct)
    {
        var state = await store.LoadStateAsync(sourceId, ct).ConfigureAwait(false);
        return state is null ? Results.NotFound() : Results.Ok(state);
    }

    private static async Task<IResult> ReceiveWebhookAsync(
        string sourceId,
        HttpContext httpContext,
        IWorkflowSourceStore store,
        IEnumerable<IWebhookWorkflowSourceProvider> providers,
        CancellationToken ct)
    {
        var source = await store.LoadAsync(sourceId, ct).ConfigureAwait(false);
        if (source is null)
        {
            return Results.NotFound();
        }

        var provider = providers.FirstOrDefault(p => string.Equals(p.SourceType, source.SourceType, StringComparison.Ordinal));
        if (provider is null)
        {
            return Results.NotFound(new ProblemDetails { Title = "Webhook source provider not found" });
        }

        using var memory = new MemoryStream();
        await httpContext.Request.Body.CopyToAsync(memory, ct).ConfigureAwait(false);
        var bodyBytes = memory.ToArray();
        JsonElement? body = null;
        if (bodyBytes.Length > 0)
        {
            using var document = JsonDocument.Parse(bodyBytes);
            body = document.RootElement.Clone();
        }

        await provider.ReceiveAsync(source, new WebhookEnvelope
        {
            Method = httpContext.Request.Method,
            Path = httpContext.Request.Path,
            Headers = httpContext.Request.Headers.ToDictionary(static h => h.Key, static h => h.Value.ToString(), StringComparer.OrdinalIgnoreCase),
            Body = body,
            BodyBytes = bodyBytes,
            QueryString = httpContext.Request.QueryString.Value
        }, ct).ConfigureAwait(false);

        return Results.Accepted();
    }

    private static async Task<IResult> ListArtifactsAsync(IArtifactRegistry artifacts, CancellationToken ct)
    {
        var keys = new List<string>();
        await foreach (var key in artifacts.ListArtifactsAsync(ct).ConfigureAwait(false))
        {
            keys.Add(key.ToString());
        }

        return Results.Ok(keys);
    }

    private static async Task<IResult> MaterializeArtifactAsync(
        string artifactKey,
        ConnectorMaterializeRequest request,
        IConnectorMaterializationDispatcher dispatcher,
        IConnectionProvider connections,
        IArtifactRegistry artifacts,
        IEventCoordinator events,
        CancellationToken ct)
    {
        var context = new ConnectorMaterializationContext
        {
            GraphId = request.GraphId ?? "connector-materialization",
            ArtifactKey = ArtifactKey.Parse(Uri.UnescapeDataString(artifactKey)),
            Connections = connections,
            Artifacts = artifacts,
            Events = events,
            Config = request.Config
        };

        var eventTypes = new List<string>();
        await foreach (var evt in dispatcher.MaterializeAsync(request.MaterializationType, context, ct).ConfigureAwait(false))
        {
            eventTypes.Add(evt.GetType().Name);
        }

        return Results.Accepted(value: new ConnectorMaterializeResponse { EventTypes = eventTypes });
    }

    private static async Task<IResult> BackfillArtifactAsync(
        string artifactKey,
        ConnectorBackfillRequest request,
        IConnectorMaterializationDispatcher dispatcher,
        IConnectionProvider connections,
        IArtifactRegistry artifacts,
        IEventCoordinator events,
        CancellationToken ct)
    {
        var parsedKey = ArtifactKey.Parse(Uri.UnescapeDataString(artifactKey));
        var partitions = request.Partitions.Count == 0
            ? new PartitionKey?[] { null }
            : request.Partitions.Select(partition => (PartitionKey?)PartitionKey.Parse(partition)).ToArray();
        var eventTypes = new List<string>();

        foreach (var partition in partitions)
        {
            ct.ThrowIfCancellationRequested();

            var context = new ConnectorMaterializationContext
            {
                GraphId = request.GraphId ?? "connector-backfill",
                ArtifactKey = parsedKey with { Partition = partition },
                Partition = partition,
                Connections = connections,
                Artifacts = artifacts,
                Events = events,
                Config = request.Config
            };

            await foreach (var evt in dispatcher.MaterializeAsync(request.MaterializationType, context, ct).ConfigureAwait(false))
            {
                eventTypes.Add(evt.GetType().Name);
            }
        }

        return Results.Accepted(value: new
        {
            artifactKey = parsedKey.ToString(),
            request.MaterializationType,
            request.Partitions,
            eventTypes
        });
    }

    private static async Task<IResult> ObserveArtifactAsync(
        string artifactKey,
        ConnectorObserveRequest request,
        IConnectorArtifactEventRecorder recorder,
        IArtifactRegistry artifacts,
        CancellationToken ct)
    {
        var evt = new ArtifactObservedEvent
        {
            ArtifactKey = ArtifactKey.Parse(Uri.UnescapeDataString(artifactKey)),
            ConnectionId = request.ConnectionId,
            ExternalRunId = request.ExternalRunId,
            ObservedAt = request.ObservedAt ?? DateTimeOffset.UtcNow,
            Metadata = request.Metadata
        };

        await recorder.RecordAsync(evt, artifacts, ct).ConfigureAwait(false);
        return Results.Accepted(value: evt);
    }

    private static async Task<IResult> RecordCheckAsync(
        string artifactKey,
        ConnectorCheckRequest request,
        IConnectorArtifactEventRecorder recorder,
        IArtifactRegistry artifacts,
        CancellationToken ct)
    {
        var evt = new ArtifactCheckCompletedEvent
        {
            ArtifactKey = ArtifactKey.Parse(Uri.UnescapeDataString(artifactKey)),
            CheckName = request.CheckName,
            Passed = request.Passed,
            Severity = request.Severity,
            Metadata = request.Metadata
        };

        await recorder.RecordAsync(evt, artifacts, ct).ConfigureAwait(false);
        return Results.Accepted(value: evt);
    }

    private static IResult ListArtifactIOManagers(IArtifactIOManagerRegistry registry) =>
        Results.Ok(new ArtifactIOManagerListResponse
        {
            Managers = registry.List().Select(static manager => new ArtifactIOManagerDto { Name = manager.Name }).ToArray()
        });

    private static IResult GetArtifactIOManager(string name, IArtifactIOManagerRegistry registry)
    {
        var manager = registry.Find(name);
        return manager is null ? Results.NotFound() : Results.Ok(new ArtifactIOManagerDto { Name = manager.Name });
    }

    private static async Task StreamEventsAsync(
        IEventCoordinator events,
        HttpContext httpContext,
        CancellationToken ct)
    {
        httpContext.Response.ContentType = "text/event-stream";
        await using var subscription = ((IEventInboxSource)events).CreateChannelInbox(EventChannel.Synchronous);
        await foreach (var evt in subscription.Reader.ReadAllAsync(ct).ConfigureAwait(false))
        {
            await httpContext.Response.WriteAsync("event: ", ct).ConfigureAwait(false);
            await httpContext.Response.WriteAsync(evt.GetType().Name, ct).ConfigureAwait(false);
            await httpContext.Response.WriteAsync("\n", ct).ConfigureAwait(false);
            await httpContext.Response.WriteAsync("data: ", ct).ConfigureAwait(false);
            await WriteEventDataAsync(evt, httpContext.Response, ct).ConfigureAwait(false);
            await httpContext.Response.WriteAsync("\n\n", ct).ConfigureAwait(false);
            await httpContext.Response.Body.FlushAsync(ct).ConfigureAwait(false);
        }
    }

    private static async Task WriteEventDataAsync(Event evt, HttpResponse response, CancellationToken ct)
    {
        await using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();
            writer.WriteString("type", evt.GetType().Name);
            writer.WriteString("kind", evt.Kind.ToString());
            writer.WriteString("channel", evt.Channel.ToString());
            writer.WriteNumber("sequenceNumber", evt.SequenceNumber);
            writer.WriteString("eventFlowId", evt.EventFlowId);
            writer.WriteString("timestamp", evt.Timestamp);
            writer.WriteNumber("exchangeTimestampNs", evt.ExchangeTimestampNs);
            writer.WriteEndObject();
        }

        stream.Position = 0;
        await stream.CopyToAsync(response.Body, ct).ConfigureAwait(false);
    }
}
