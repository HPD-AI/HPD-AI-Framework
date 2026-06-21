using System.Text.Json;
using FluentAssertions;
using HPD.Events;
using HPD.Graph.Abstractions.Artifacts;
using HPD.Graph.Connectors.Abstractions.Actions;
using HPD.Graph.Connectors.Abstractions.Assets;
using HPD.Graph.Connectors.Abstractions.Configuration;
using HPD.Graph.Connectors.Abstractions.Connections;
using HPD.Graph.Connectors.Abstractions.Descriptors;
using HPD.Graph.Connectors.Abstractions.Events;
using HPD.Graph.Connectors.Abstractions.IO;
using HPD.Graph.Connectors.Abstractions.Serialization;
using HPD.Graph.Connectors.Abstractions.Sources;

namespace HPD.Graph.Tests.V21;

public sealed class ConnectorAbstractionsTests
{
    [Fact]
    public void ConnectorPackageDescriptor_RoundTrips_WithSourceConnectionAndAsset()
    {
        var descriptor = new ConnectorPackageDescriptor
        {
            ConnectorId = "github",
            DisplayName = "GitHub",
            Apps =
            [
                new AppDescriptor
                {
                    AppId = "github",
                    DisplayName = "GitHub"
                }
            ],
            Connections =
            [
                new ConnectionDescriptor
                {
                    ConnectionType = "github.pat",
                    AppId = "github",
                    DisplayName = "GitHub PAT",
                    AuthKind = ConnectionAuthKind.BearerToken,
                    Scopes = ["repo"]
                }
            ],
            Sources =
            [
                new WorkflowSourceDescriptor
                {
                    SourceType = "github.issue.opened",
                    AppId = "github",
                    DisplayName = "Issue Opened",
                    TriggerKind = SourceTriggerKind.Webhook
                }
            ],
            Assets =
            [
                new ConnectorAssetDescriptor
                {
                    AssetType = "github.repository",
                    AppId = "github",
                    ArtifactKey = ArtifactKey.FromPath("github", "repo")
                }
            ]
        };

        var json = JsonSerializer.Serialize(
            descriptor,
            ConnectorAbstractionsJsonSerializerContext.Default.ConnectorPackageDescriptor);
        var roundTripped = JsonSerializer.Deserialize(
            json,
            ConnectorAbstractionsJsonSerializerContext.Default.ConnectorPackageDescriptor);

        roundTripped.Should().NotBeNull();
        roundTripped!.ConnectorId.Should().Be("github");
        roundTripped.Connections.Should().ContainSingle(c => c.ConnectionType == "github.pat");
        roundTripped.Sources.Should().ContainSingle(s => s.SourceType == "github.issue.opened");
        roundTripped.Assets.Should().ContainSingle(a => a.ArtifactKey.ToString() == "github/repo");
    }

    [Fact]
    public void WorkflowSource_Defaults_ToEnabledUniqueWebhookShape()
    {
        var source = new WorkflowSource
        {
            SourceId = "source-1",
            GraphId = "graph-1",
            SourceType = "github.issue.opened",
            CreatedAt = DateTimeOffset.UnixEpoch,
            UpdatedAt = DateTimeOffset.UnixEpoch
        };

        source.Enabled.Should().BeTrue();
        source.ConnectionId.Should().BeNull();
        source.Metadata.Should().BeEmpty();
    }

    [Fact]
    public void WorkflowSourceDescriptor_Defaults_ToUniqueDedupe()
    {
        var descriptor = new WorkflowSourceDescriptor
        {
            SourceType = "github.issue.opened",
            AppId = "github",
            DisplayName = "Issue Opened",
            TriggerKind = SourceTriggerKind.Webhook
        };

        descriptor.DefaultDedupeStrategy.Should().Be(DedupeStrategy.Unique);
        descriptor.Metadata.Should().BeEmpty();
    }

    [Fact]
    public void ConnectorActionDescriptor_RoundTrips_WithFieldsAndTraits()
    {
        var descriptor = new ConnectorActionDescriptor
        {
            ActionType = "github.create_issue",
            HandlerName = "github.create_issue",
            AppId = "github",
            DisplayName = "Create Issue",
            Traits = ConnectorOperationTraits.OpenWorld
                | ConnectorOperationTraits.Idempotent
                | ConnectorOperationTraits.RequiresApproval,
            Fields =
            [
                new ConnectorFieldDescriptor
                {
                    Name = "connectionId",
                    TypeName = "string",
                    Label = "Connection",
                    Required = true,
                    ConnectionType = "github"
                },
                new ConnectorFieldDescriptor
                {
                    Name = "repository",
                    TypeName = "string",
                    Required = true,
                    OptionProviderName = "github.repositories"
                }
            ]
        };

        var json = JsonSerializer.Serialize(
            descriptor,
            ConnectorAbstractionsJsonSerializerContext.Default.ConnectorActionDescriptor);
        var roundTripped = JsonSerializer.Deserialize(
            json,
            ConnectorAbstractionsJsonSerializerContext.Default.ConnectorActionDescriptor);

        roundTripped.Should().NotBeNull();
        roundTripped!.Traits.Should().HaveFlag(ConnectorOperationTraits.OpenWorld);
        roundTripped.Traits.Should().HaveFlag(ConnectorOperationTraits.Idempotent);
        roundTripped.Traits.Should().HaveFlag(ConnectorOperationTraits.RequiresApproval);
        roundTripped.Fields.Should().ContainSingle(f => f.ConnectionType == "github");
        roundTripped.Fields.Should().ContainSingle(f => f.OptionProviderName == "github.repositories");
    }

    [Fact]
    public void WorkflowSourceEmittedEvent_RoundTrips_WithSourceGeneratedJsonMetadata()
    {
        using var payload = JsonDocument.Parse("""{"issue":{"number":123}}""");
        var emitted = new WorkflowSourceEmittedEvent
        {
            SourceId = "source-1",
            GraphId = "graph-1",
            SourceType = "github.issue.opened",
            Payload = payload.RootElement.Clone(),
            EventId = "delivery-1",
            Summary = "Issue opened",
            OccurredAt = DateTimeOffset.UnixEpoch
        };

        var json = JsonSerializer.Serialize(
            emitted,
            ConnectorAbstractionsJsonSerializerContext.Default.WorkflowSourceEmittedEvent);
        var roundTripped = JsonSerializer.Deserialize(
            json,
            ConnectorAbstractionsJsonSerializerContext.Default.WorkflowSourceEmittedEvent);

        roundTripped.Should().NotBeNull();
        roundTripped!.SourceId.Should().Be("source-1");
        roundTripped.Kind.Should().Be(HPD.Events.EventKind.Content);
        roundTripped.Channel.Should().Be(HPD.Events.EventChannel.Synchronous);
        roundTripped.Payload.GetProperty("issue").GetProperty("number").GetInt32().Should().Be(123);
    }

    [Fact]
    public void WorkflowExecutionDispatchedEvent_RoundTrips_WithLifecycleClassification()
    {
        var dispatched = new WorkflowExecutionDispatchedEvent
        {
            GraphId = "graph-1",
            ExecutionId = "execution-1",
            SourceId = "source-1",
            SourceType = "github.issue.opened",
            EventId = "delivery-1"
        };

        var json = JsonSerializer.Serialize(
            dispatched,
            ConnectorAbstractionsJsonSerializerContext.Default.WorkflowExecutionDispatchedEvent);
        var roundTripped = JsonSerializer.Deserialize(
            json,
            ConnectorAbstractionsJsonSerializerContext.Default.WorkflowExecutionDispatchedEvent);

        roundTripped.Should().NotBeNull();
        roundTripped!.Kind.Should().Be(EventKind.Lifecycle);
        roundTripped.Channel.Should().Be(EventChannel.Synchronous);
        roundTripped.ExecutionId.Should().Be("execution-1");
    }

    [Fact]
    public void ArtifactEvents_RoundTrip_WithArtifactKeysAndMetadata()
    {
        var artifactKey = ArtifactKey.FromPath("warehouse", "marts", "orders");
        using var metadata = JsonDocument.Parse("""{"rows":42}""");

        var materialized = new ExternalArtifactMaterializedEvent
        {
            ArtifactKey = artifactKey,
            Version = "v1",
            ConnectionId = "warehouse-prod",
            ExternalRunId = "run-1",
            MaterializedAt = DateTimeOffset.UnixEpoch,
            InputVersions =
            [
                new ArtifactInputVersion
                {
                    ArtifactKey = ArtifactKey.FromPath("warehouse", "raw", "orders"),
                    Version = "raw-v1"
                }
            ],
            Metadata = metadata.RootElement.Clone()
        };

        var json = JsonSerializer.Serialize(
            materialized,
            ConnectorAbstractionsJsonSerializerContext.Default.ExternalArtifactMaterializedEvent);
        var roundTripped = JsonSerializer.Deserialize(
            json,
            ConnectorAbstractionsJsonSerializerContext.Default.ExternalArtifactMaterializedEvent);

        roundTripped.Should().NotBeNull();
        roundTripped!.ArtifactKey.ToString().Should().Be("warehouse/marts/orders");
        roundTripped.InputVersions.Should().ContainSingle();
        roundTripped.InputVersions.Single().ArtifactKey.ToString().Should().Be("warehouse/raw/orders");
        roundTripped.InputVersions.Single().Version.Should().Be("raw-v1");
        roundTripped.Metadata!.Value.GetProperty("rows").GetInt32().Should().Be(42);
    }

    [Fact]
    public void ArtifactIOContexts_RoundTrip_WithResolvedConnection()
    {
        var writeContext = new ArtifactWriteContext
        {
            ArtifactKey = ArtifactKey.FromPath("warehouse", "marts", "orders"),
            Version = "v1",
            Connection = new ResolvedConnection
            {
                ConnectionId = "duckdb-local",
                ConnectionType = "duckdb.file",
                AppId = "duckdb",
                Secrets = new Dictionary<string, string>
                {
                    ["path"] = "/tmp/warehouse.duckdb"
                }
            }
        };

        var json = JsonSerializer.Serialize(
            writeContext,
            ConnectorAbstractionsJsonSerializerContext.Default.ArtifactWriteContext);
        var roundTripped = JsonSerializer.Deserialize(
            json,
            ConnectorAbstractionsJsonSerializerContext.Default.ArtifactWriteContext);

        roundTripped.Should().NotBeNull();
        roundTripped!.ArtifactKey.ToString().Should().Be("warehouse/marts/orders");
        roundTripped.Connection.ConnectionId.Should().Be("duckdb-local");
        roundTripped.Connection.Secrets.Should().ContainKey("path");
    }
}
