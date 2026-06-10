using System.Text.Json;
using System.Text.Json.Serialization;
using HPD.Graph.Connectors.Abstractions.Assets;
using HPD.Graph.Connectors.Abstractions.Connections;
using HPD.Graph.Connectors.Abstractions.Descriptors;
using HPD.Graph.Connectors.Abstractions.Events;
using HPD.Graph.Connectors.Abstractions.Options;
using HPD.Graph.Connectors.Abstractions.Sources;
using HPD.Graph.Connectors.AspNetCore.Data;

namespace HPD.Graph.Connectors.AspNetCore.Serialization;

[JsonSourceGenerationOptions(
    GenerationMode = JsonSourceGenerationMode.Default,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
    UseStringEnumConverter = true)]
[JsonSerializable(typeof(ConnectorListResponse))]
[JsonSerializable(typeof(ConnectionListResponse))]
[JsonSerializable(typeof(WorkflowSourceListResponse))]
[JsonSerializable(typeof(WorkflowSourceStatusListResponse))]
[JsonSerializable(typeof(ConnectorAssetListResponse))]
[JsonSerializable(typeof(ArtifactIOManagerListResponse))]
[JsonSerializable(typeof(ArtifactIOManagerDto))]
[JsonSerializable(typeof(ConnectorMaterializeRequest))]
[JsonSerializable(typeof(ConnectorMaterializeResponse))]
[JsonSerializable(typeof(ConnectorBackfillRequest))]
[JsonSerializable(typeof(ConnectorObserveRequest))]
[JsonSerializable(typeof(ConnectorCheckRequest))]
[JsonSerializable(typeof(ConnectorPackageDescriptor))]
[JsonSerializable(typeof(ConnectionDefinition))]
[JsonSerializable(typeof(WorkflowSource))]
[JsonSerializable(typeof(WorkflowSourceState))]
[JsonSerializable(typeof(WorkflowSourceStatus))]
[JsonSerializable(typeof(ConnectorOptionRequest))]
[JsonSerializable(typeof(ConnectorOptionPage))]
[JsonSerializable(typeof(ConnectorAssetCatalogRequest))]
[JsonSerializable(typeof(ArtifactObservedEvent))]
[JsonSerializable(typeof(ArtifactCheckCompletedEvent))]
[JsonSerializable(typeof(WebhookEnvelope))]
[JsonSerializable(typeof(JsonElement))]
public partial class ConnectorAspNetCoreJsonSerializerContext : JsonSerializerContext
{
}
