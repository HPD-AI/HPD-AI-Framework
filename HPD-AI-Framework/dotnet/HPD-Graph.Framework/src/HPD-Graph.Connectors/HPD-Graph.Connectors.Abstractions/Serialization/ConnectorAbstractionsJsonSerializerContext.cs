using System.Text.Json;
using System.Text.Json.Serialization;
using HPD.Graph.Connectors.Abstractions.Actions;
using HPD.Graph.Connectors.Abstractions.Assets;
using HPD.Graph.Connectors.Abstractions.Configuration;
using HPD.Graph.Connectors.Abstractions.Connections;
using HPD.Graph.Connectors.Abstractions.Descriptors;
using HPD.Graph.Connectors.Abstractions.Events;
using HPD.Graph.Connectors.Abstractions.IO;
using HPD.Graph.Connectors.Abstractions.Options;
using HPD.Graph.Connectors.Abstractions.Sources;

namespace HPD.Graph.Connectors.Abstractions.Serialization;

[JsonSourceGenerationOptions(
    GenerationMode = JsonSourceGenerationMode.Default,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    WriteIndented = true,
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
    UseStringEnumConverter = true)]
[JsonSerializable(typeof(ConnectorPackageDescriptor))]
[JsonSerializable(typeof(AppDescriptor))]
[JsonSerializable(typeof(ConnectionDescriptor))]
[JsonSerializable(typeof(ConnectionDefinition))]
[JsonSerializable(typeof(ResolvedConnection))]
[JsonSerializable(typeof(WorkflowSourceDescriptor))]
[JsonSerializable(typeof(WorkflowSource))]
[JsonSerializable(typeof(WorkflowSourceState))]
[JsonSerializable(typeof(WorkflowSourceStatus))]
[JsonSerializable(typeof(WebhookEnvelope))]
[JsonSerializable(typeof(ConnectorConfigDescriptor))]
[JsonSerializable(typeof(ConnectorFieldDescriptor))]
[JsonSerializable(typeof(ConnectorActionDescriptor))]
[JsonSerializable(typeof(ConnectorOptionRequest))]
[JsonSerializable(typeof(ConnectorOption))]
[JsonSerializable(typeof(ConnectorOptionPage))]
[JsonSerializable(typeof(ConnectorAssetDescriptor))]
[JsonSerializable(typeof(ConnectorExternalAssetDescriptor))]
[JsonSerializable(typeof(ConnectorAssetMaterializationDescriptor))]
[JsonSerializable(typeof(ConnectorAssetObservationDescriptor))]
[JsonSerializable(typeof(ConnectorAssetCheckDescriptor))]
[JsonSerializable(typeof(ConnectorFreshnessPolicy))]
[JsonSerializable(typeof(ConnectorAssetCatalogRequest))]
[JsonSerializable(typeof(ArtifactWriteContext))]
[JsonSerializable(typeof(ArtifactReadContext))]
[JsonSerializable(typeof(WorkflowSourceEmittedEvent))]
[JsonSerializable(typeof(WorkflowExecutionDispatchedEvent))]
[JsonSerializable(typeof(ArtifactObservedEvent))]
[JsonSerializable(typeof(ExternalArtifactMaterializedEvent))]
[JsonSerializable(typeof(ArtifactInputVersion))]
[JsonSerializable(typeof(ArtifactCheckCompletedEvent))]
[JsonSerializable(typeof(ConnectionAuthKind))]
[JsonSerializable(typeof(SourceTriggerKind))]
[JsonSerializable(typeof(DedupeStrategy))]
[JsonSerializable(typeof(ConnectorOperationTraits))]
[JsonSerializable(typeof(Dictionary<string, string>))]
[JsonSerializable(typeof(Dictionary<string, object>))]
[JsonSerializable(typeof(JsonElement))]
[JsonSerializable(typeof(string))]
public partial class ConnectorAbstractionsJsonSerializerContext : JsonSerializerContext
{
}
