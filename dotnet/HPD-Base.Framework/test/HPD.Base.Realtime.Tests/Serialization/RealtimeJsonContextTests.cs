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
    public void DescriptorDtosRoundTripThroughSourceGeneratedContext()
    {
        var connection = new BaseRealtimeConnectionDescriptor
        {
            ConnectionId = "conn_1",
            Transport = "websocket",
            ConnectedAt = DateTimeOffset.UnixEpoch,
            ActiveChannelCount = 1,
            Replayable = false,
            Resumable = false
        };
        var channel = new BaseRealtimeChannelDescriptor
        {
            Channel = "base:records:items",
            Kind = BaseRealtimeChannelKinds.RecordChanges,
            CollectionId = "items",
            Private = true,
            Replayable = false,
            Resumable = false
        };

        JsonSerializer.Deserialize(
            JsonSerializer.Serialize(connection, HPDBaseRealtimeJsonSerializerContext.Default.BaseRealtimeConnectionDescriptor),
            HPDBaseRealtimeJsonSerializerContext.Default.BaseRealtimeConnectionDescriptor)!.ConnectionId.Should().Be("conn_1");
        JsonSerializer.Deserialize(
            JsonSerializer.Serialize(channel, HPDBaseRealtimeJsonSerializerContext.Default.BaseRealtimeChannelDescriptor),
            HPDBaseRealtimeJsonSerializerContext.Default.BaseRealtimeChannelDescriptor)!.Channel.Should().Be("base:records:items");
    }
}
