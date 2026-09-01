using HPD.Base.Tests.InMemory.TestDoubles;

namespace HPD.Base.Tests.InMemory.Mutations;

public sealed class PatchReplaceRevisionTests
{
    [Fact]
    public async Task Patch_removes_an_optional_non_nullable_field_by_stable_ID()
    {
        var store = new InMemoryRecordStore();
        CollectionDefinition collection = InMemoryTestData.Collection() with
        {
            Fields =
            [
                new FieldDefinition
                {
                    Id = "item.title", ApplicationName = "Title", WireName = "title", Type = BaseFieldTypes.String,
                    Presence = BaseFieldPresence.Optional, Nullability = BaseFieldNullability.NonNullable,
                },
                new FieldDefinition
                {
                    Id = "item.status", ApplicationName = "Status", WireName = "status", Type = BaseFieldTypes.String,
                    Presence = BaseFieldPresence.Optional, Nullability = BaseFieldNullability.NonNullable,
                },
            ],
        };
        OperationResult<RecordEnvelope> created = await InMemoryMutationTestDriver.CreateAsync(store, collection,
            new RecordCreateRequest { Payload = InMemoryTestData.Payload(("title", "old"), ("status", "active")) },
            InMemoryTestData.Operation(BaseOperationKind.Create));

        OperationResult<RecordEnvelope> result = await InMemoryMutationTestDriver.PatchAsync(store, collection,
            created.Value!.Id,
            new RecordPatchRequest
            {
                Patch = new RecordPayload { Kind = RecordPayloadKind.FieldMap, Fields = [] },
                RemovedFieldIds = ["item.status"],
            }, InMemoryTestData.Operation(BaseOperationKind.Patch));

        result.Status.Should().Be(OperationStatus.Updated);
        result.Value!.Payload.Fields.Should().ContainKey("title").And.NotContainKey("status");
    }

    [Fact]
    public async Task PatchMergesTopLevelFieldsAndPreservesExistingFields()
    {
        var store = new InMemoryRecordStore();
        var collection = InMemoryTestData.Collection();
        var create = await InMemoryMutationTestDriver.CreateAsync(store,
            collection,
            new RecordCreateRequest { Payload = InMemoryTestData.Payload(("title", "old"), ("status", "active")) },
            InMemoryTestData.Operation(BaseOperationKind.Create));

        var patch = await InMemoryMutationTestDriver.PatchAsync(store,
            collection,
            create.Value!.Id,
            new RecordPatchRequest { Patch = InMemoryTestData.Patch("title", "new"), RemovedFieldIds = [] },
            InMemoryTestData.Operation(BaseOperationKind.Patch));

        patch.Status.Should().Be(OperationStatus.Updated);
        patch.Value!.Payload.Fields!["title"].GetString().Should().Be("new");
        patch.Value.Payload.Fields["status"].GetString().Should().Be("active");
        patch.Value.Metadata.Revision.Should().NotBe(create.Value.Metadata.Revision);
    }

    [Fact]
    public async Task JsonNullPatchStoresNullAndDoesNotRemoveField()
    {
        var store = new InMemoryRecordStore();
        var collection = InMemoryTestData.Collection();
        var create = await InMemoryMutationTestDriver.CreateAsync(store,
            collection,
            new RecordCreateRequest { Payload = InMemoryTestData.Payload(("title", "old"), ("status", "active")) },
            InMemoryTestData.Operation(BaseOperationKind.Create));

        using var document = JsonDocument.Parse("""{"status":null}""");
        var patch = await InMemoryMutationTestDriver.PatchAsync(store,
            collection,
            create.Value!.Id,
            new RecordPatchRequest
            {
                RemovedFieldIds = [],
                Patch = new RecordPayload
                {
                    Kind = RecordPayloadKind.FieldMap,
                    Fields = new Dictionary<string, JsonElement>
                    {
                        ["status"] = document.RootElement.GetProperty("status").Clone()
                    }
                }
            },
            InMemoryTestData.Operation(BaseOperationKind.Patch));

        patch.Status.Should().Be(OperationStatus.Updated);
        patch.Value!.Payload.Fields!.Should().ContainKey("status");
        patch.Value.Payload.Fields["status"].ValueKind.Should().Be(JsonValueKind.Null);
        patch.Value.Payload.Fields["title"].GetString().Should().Be("old");
    }

    [Fact]
    public async Task ReplaceIsFullReplacement()
    {
        var store = new InMemoryRecordStore();
        var collection = InMemoryTestData.Collection();
        var create = await InMemoryMutationTestDriver.CreateAsync(store,
            collection,
            new RecordCreateRequest { Payload = InMemoryTestData.Payload(("title", "old"), ("status", "active")) },
            InMemoryTestData.Operation(BaseOperationKind.Create));

        var replace = await InMemoryMutationTestDriver.ReplaceAsync(store,
            collection,
            create.Value!.Id,
            new RecordReplaceRequest { Payload = InMemoryTestData.Payload(("title", "replacement")) },
            InMemoryTestData.Operation(BaseOperationKind.Replace));

        replace.Status.Should().Be(OperationStatus.Updated);
        replace.Value!.Payload.Fields!.Keys.Should().Equal("title");
    }

    [Fact]
    public async Task RevisionedPatchConflictsOnStaleRevision()
    {
        var store = new InMemoryRecordStore();
        var collection = InMemoryTestData.Collection();
        var create = await InMemoryMutationTestDriver.CreateAsync(store,
            collection,
            new RecordCreateRequest { Payload = InMemoryTestData.Payload(("title", "old")) },
            InMemoryTestData.Operation(BaseOperationKind.Create));

        var stale = new RevisionToken("mem:stale");
        var result = await InMemoryMutationTestDriver.PatchAsync(store,
            collection,
            create.Value!.Id,
            new RecordPatchRequest
            {
                RemovedFieldIds = [],
                Patch = InMemoryTestData.Patch("title", "new"),
                ExpectedRevision = stale
            },
            InMemoryTestData.Operation(BaseOperationKind.Patch));

        result.Status.Should().Be(OperationStatus.Conflict);
        result.Error!.Conflict!.Kind.Should().Be(ConflictKind.Revision);
    }

    [Fact]
    public async Task ReplaceHonorsExpectedRevision()
    {
        var store = new InMemoryRecordStore();
        var collection = InMemoryTestData.Collection();
        var create = await InMemoryMutationTestDriver.CreateAsync(store,
            collection,
            new RecordCreateRequest { Payload = InMemoryTestData.Payload(("title", "old")) },
            InMemoryTestData.Operation(BaseOperationKind.Create));

        var conflict = await InMemoryMutationTestDriver.ReplaceAsync(store,
            collection,
            create.Value!.Id,
            new RecordReplaceRequest
            {
                ExpectedRevision = new RevisionToken("mem:stale"),
                Payload = InMemoryTestData.Payload(("title", "new"))
            },
            InMemoryTestData.Operation(BaseOperationKind.Replace));

        conflict.Status.Should().Be(OperationStatus.Conflict);
        conflict.Error!.Conflict!.Kind.Should().Be(ConflictKind.Revision);

        var updated = await InMemoryMutationTestDriver.ReplaceAsync(store,
            collection,
            create.Value.Id,
            new RecordReplaceRequest
            {
                ExpectedRevision = create.Value.Metadata.Revision,
                Payload = InMemoryTestData.Payload(("title", "new"))
            },
            InMemoryTestData.Operation(BaseOperationKind.Replace));

        updated.Status.Should().Be(OperationStatus.Updated);
        updated.Value!.Payload.Fields!["title"].GetString().Should().Be("new");
    }

    [Fact]
    public async Task MissingPatchAndReplaceReturnNotFoundWithError()
    {
        var store = new InMemoryRecordStore();
        var collection = InMemoryTestData.Collection();

        var patch = await InMemoryMutationTestDriver.PatchAsync(store,
            collection,
            RecordId.Create("missing"),
            new RecordPatchRequest { Patch = InMemoryTestData.Patch("title", "new"), RemovedFieldIds = [] },
            InMemoryTestData.Operation(BaseOperationKind.Patch));
        var replace = await InMemoryMutationTestDriver.ReplaceAsync(store,
            collection,
            RecordId.Create("missing"),
            new RecordReplaceRequest { Payload = InMemoryTestData.Payload(("title", "new")) },
            InMemoryTestData.Operation(BaseOperationKind.Replace));

        patch.Status.Should().Be(OperationStatus.NotFound);
        patch.Error.Should().NotBeNull();
        replace.Status.Should().Be(OperationStatus.NotFound);
        replace.Error.Should().NotBeNull();
    }

    [Fact]
    public async Task DirectDeleteHonorsExpectedRevision()
    {
        var store = new InMemoryRecordStore();
        var collection = InMemoryTestData.Collection();
        var create = await InMemoryMutationTestDriver.CreateAsync(store,
            collection,
            new RecordCreateRequest { Payload = InMemoryTestData.Payload(("title", "old")) },
            InMemoryTestData.Operation(BaseOperationKind.Create));

        var conflict = await InMemoryMutationTestDriver.DeleteAsync(store,
            collection,
            create.Value!.Id,
            new RecordDeleteRequest { ExpectedRevision = new RevisionToken("mem:stale") },
            InMemoryTestData.Operation(BaseOperationKind.Delete));
        conflict.Status.Should().Be(OperationStatus.Conflict);

        var deleted = await InMemoryMutationTestDriver.DeleteAsync(store,
            collection,
            create.Value.Id,
            new RecordDeleteRequest { ExpectedRevision = create.Value.Metadata.Revision },
            InMemoryTestData.Operation(BaseOperationKind.Delete));
        deleted.Status.Should().Be(OperationStatus.Deleted);
    }
}
