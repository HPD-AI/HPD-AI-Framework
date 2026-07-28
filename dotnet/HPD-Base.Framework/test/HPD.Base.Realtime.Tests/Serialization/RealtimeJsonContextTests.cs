namespace HPD.Base.Realtime.Tests.Serialization;

public sealed class RealtimeJsonContextTests
{
    [Fact]
    public void ProtocolMessagesRoundTripThroughSourceGeneratedContext()
    {
        var message = new BaseRealtimeClientMessage
        {
            Type = BaseRealtimeProtocolTypes.Join,
            Ref = "1",
            Channel = "base:records:items",
            Config = new BaseRealtimeChannelJoinRequest
            {
                Kind = BaseRealtimeChannelKinds.RecordChanges,
                CollectionId = "items",
                Operations = [BaseOperationKind.Create]
            }
        };

        var json = JsonSerializer.Serialize(message, HPDBaseRealtimeJsonSerializerContext.Default.BaseRealtimeClientMessage);
        var roundTrip = JsonSerializer.Deserialize(json, HPDBaseRealtimeJsonSerializerContext.Default.BaseRealtimeClientMessage);

        roundTrip!.Config!.Kind.Should().Be(BaseRealtimeChannelKinds.RecordChanges);
        json.Should().Contain("\"operations\":[\"create\"]");
    }

    [Fact]
    public void RemovedProtocolSurfaceIsAbsentFromClrAndJsonContracts()
    {
        var assembly = typeof(BaseRealtimeClientMessage).Assembly;
        string[] removedTypes =
        {
            "HPD.Base.Realtime.BaseRealtimeSubscribeRequest",
            "HPD.Base.Realtime.BaseRealtimeSnapshotOptions",
            "HPD.Base.Realtime.BaseRealtimeConnectionDescriptor",
            "HPD.Base.Realtime.BaseRealtimeChannelDescriptor"
        };

        foreach (var typeName in removedTypes)
            assembly.GetType(typeName).Should().BeNull();
        typeof(BaseRealtimeClientMessage).GetProperty("Token").Should().BeNull();
        typeof(BaseRealtimeProtocolTypes).GetFields()
            .Select(field => field.GetRawConstantValue())
            .Should().NotContain(["connect", "authenticate", "system"]);
        typeof(BaseRealtimeErrorCodes).GetFields()
            .Select(field => field.GetRawConstantValue())
            .Should().NotContain("base.realtime.resume.unsupported");

        var staleJson = """
        {
          "type": "join",
          "channel": "base:records:items",
          "token": "stale-token",
          "config": { "kind": "base.record_changes" }
        }
        """;

        var deserialize = () => JsonSerializer.Deserialize(
            staleJson,
            HPDBaseRealtimeJsonSerializerContext.Default.BaseRealtimeClientMessage);
        deserialize.Should().Throw<JsonException>();
    }

    [Fact]
    public void RealtimeEventContractDoesNotExposeProviderOrderingTokens()
    {
        typeof(BaseRealtimeEvent).GetProperty("SequenceNumber").Should().BeNull();

        var realtimeEvent = new BaseRealtimeEvent
        {
            EventId = "evt_1",
            Type = "record.created",
            SchemaVersion = BaseEventSchemaVersions.V1,
            OccurredAt = DateTimeOffset.UnixEpoch,
            Resource = new BaseRealtimeRecordResource
            {
                CollectionId = "items",
                RecordId = new RecordId("one")
            },
            Operation = BaseOperationKind.Create
        };

        var json = JsonSerializer.Serialize(
            realtimeEvent,
            HPDBaseRealtimeJsonSerializerContext.Default.BaseRealtimeEvent);
        var normalizedJson = json.ToLowerInvariant();

        normalizedJson.Should().NotContain("sequence");
        normalizedJson.Should().NotContain("position");
        normalizedJson.Should().NotContain("sequence");
    }

    [Fact]
    public void RealtimeContractExposesOnlyTheMinimalPolicyVisibleSurface()
    {
        AssertProperties<BaseRealtimeEvent>(
            "EventId",
            "Type",
            "SchemaVersion",
            "OccurredAt",
            "Resource",
            "Operation",
            "Before",
            "After",
            "Cursor");
        AssertProperties<BaseRealtimeRecordResource>("CollectionId", "RecordId");
        AssertProperties<BaseRealtimeRecordSnapshot>("Payload");

        var assembly = typeof(BaseRealtimeEvent).Assembly;
        assembly.GetType("HPD.Base.Realtime.BaseRealtimePrincipalSummary").Should().BeNull();
        typeof(BaseRealtimeChannelJoinRequest).GetProperty("IncludePrincipal").Should().BeNull();
        typeof(BaseRealtimeChannelJoinRequest).GetProperty("IncludeExtensions").Should().BeNull();
        typeof(BaseRealtimeChannelJoinRequest).GetProperty("Visibility").Should().BeNull();
        typeof(BaseRealtimeDtoIds).GetField("PrincipalSummary").Should().BeNull();
    }

    [Fact]
    public void RemovedDisclosureFieldsAreRejectedByTheJsonContract()
    {
        string[] removedEventFields =
        [
            "tenantId",
            "correlationId",
            "causationId",
            "changedFields",
            "visibility",
            "principal",
            "extensions"
        ];

        foreach (var field in removedEventFields)
        {
            var json = $$"""
            {
              "eventId": "event-secret",
              "type": "record.created",
              "schemaVersion": "1",
              "occurredAt": "1970-01-01T00:00:00Z",
              "resource": { "collectionId": "items", "recordId": "one" },
              "operation": "create",
              "{{field}}": "forbidden-marker"
            }
            """;

            var deserialize = () => JsonSerializer.Deserialize(
                json,
                HPDBaseRealtimeJsonSerializerContext.Default.BaseRealtimeEvent);
            deserialize.Should().Throw<JsonException>();
        }

        var staleJoin = """
        {
          "kind": "base.record_changes",
          "includePrincipal": true
        }
        """;
        var deserializeJoin = () => JsonSerializer.Deserialize(
            staleJoin,
            HPDBaseRealtimeJsonSerializerContext.Default.BaseRealtimeChannelJoinRequest);
        deserializeJoin.Should().Throw<JsonException>();
    }

    private static void AssertProperties<T>(params string[] expected)
    {
        typeof(T).GetProperties()
            .Select(property => property.Name)
            .Should().BeEquivalentTo(expected);
    }
}
