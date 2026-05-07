using System.Text.Json;
using System.Text.Json.Serialization;
using HPDAgent.Graph.Connectors.Abstractions.Assets;
using HPDAgent.Graph.Connectors.Abstractions.Connections;
using HPDAgent.Graph.Connectors.Abstractions.Descriptors;
using HPDAgent.Graph.Connectors.Abstractions.Events;
using HPDAgent.Graph.Connectors.Abstractions.Options;
using HPDAgent.Graph.Connectors.Abstractions.Sources;
using HPDAgent.Graph.Connectors.AspNetCore.Data;

namespace HPDAgent.Graph.Connectors.AspNetCore.Serialization;

[JsonSourceGenerationOptions(
    GenerationMode = JsonSourceGenerationMode.Default,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    NumberHandling = JsonNumberHandling.AllowReadingFromString,
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    UnmappedMemberHandling = JsonUnmappedMemberHandling.Skip,
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
