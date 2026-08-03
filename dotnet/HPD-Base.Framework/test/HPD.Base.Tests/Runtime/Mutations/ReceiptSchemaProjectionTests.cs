using System.Text.Json;
using FluentAssertions;

namespace HPD.Base.Tests.Runtime.Mutations;

public sealed class ReceiptSchemaProjectionTests
{
    [Fact]
    public void StableFieldIdentityProjectsRenameIntoCurrentSchema()
    {
        CollectionDefinition stored = Collection(Field("stable-name", "oldName", BaseFieldTypes.String));
        CollectionDefinition current = Collection(Field("stable-name", "newName", BaseFieldTypes.String));
        BaseRecordMutationFact receipt = Fact(stored, ("oldName", "value"));

        bool compatible = DefaultBaseMutationProcessor.TryProjectReceiptMutation(receipt, current, out BaseRecordMutationFact projected);

        compatible.Should().BeTrue();
        projected.Collection.Should().BeSameAs(current);
        projected.After!.Payload.Fields.Should().ContainKey("newName").WhoseValue.GetString().Should().Be("value");
        projected.After.Payload.Fields.Should().NotContainKey("oldName");
        projected.ChangedFields.Should().Equal("newName");
    }

    [Fact]
    public void RemovedOrCodecChangedStableFieldFailsClosed()
    {
        CollectionDefinition stored = Collection(Field("stable-name", "oldName", BaseFieldTypes.String));
        BaseRecordMutationFact receipt = Fact(stored, ("oldName", "value"));
        CollectionDefinition removed = Collection();
        CollectionDefinition changedCodec = Collection(Field("stable-name", "newName", BaseFieldTypes.Integer));

        DefaultBaseMutationProcessor.TryProjectReceiptMutation(receipt, removed, out _).Should().BeFalse();
        DefaultBaseMutationProcessor.TryProjectReceiptMutation(receipt, changedCodec, out _).Should().BeFalse();
    }

    private static CollectionDefinition Collection(params FieldDefinition[] fields) => new()
    {
        Id = "items",
        Name = "items",
        Kind = BaseCollectionKinds.Document,
        SchemaMode = SchemaMode.Strict,
        UnknownFields = UnknownFieldPolicy.Reject,
        MutationMode = BaseCollectionMutationMode.Mutable,
        Fields = fields,
    };

    private static FieldDefinition Field(string id, string name, string type) => new()
    {
        Id = id,
        Name = name,
        Type = type,
    };

    private static BaseRecordMutationFact Fact(
        CollectionDefinition collection,
        params (string Name, string Value)[] values)
    {
        var fields = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        foreach ((string name, string value) in values)
            fields.Add(name, JsonSerializer.SerializeToElement(value));
        return new BaseRecordMutationFact
        {
            RequestedOperation = BaseRecordMutationKind.Create,
            CommittedOperation = BaseCommittedRecordMutationKind.Create,
            Collection = collection,
            Event = new EventReference { EventId = "event", Type = "base.record.created" },
            After = new RecordEnvelope
            {
                CollectionId = collection.Id,
                Id = new RecordId("record"),
                Payload = new RecordPayload { Kind = RecordPayloadKind.FieldMap, Fields = fields },
                Metadata = new RecordMetadata(),
            },
            ChangedFields = values.Select(static value => value.Name).ToArray(),
        };
    }
}
