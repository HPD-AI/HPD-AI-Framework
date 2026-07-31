using HPD.Base.Tests.Volatile.TestDoubles;

namespace HPD.Base.Tests.Volatile.Mutations;

public sealed class PatchReplaceRevisionTests
{
    [Fact]
    public async Task PatchMergesTopLevelFieldsAndPreservesExistingFields()
    {
        var store = new VolatileRecordStore();
        var collection = VolatileTestData.Collection();
        var create = await VolatileMutationTestDriver.CreateAsync(store,
            collection,
            new RecordCreateRequest { Payload = VolatileTestData.Payload(("title", "old"), ("status", "active")) },
            VolatileTestData.Operation(BaseOperationKind.Create));

        var patch = await VolatileMutationTestDriver.PatchAsync(store,
            collection,
            create.Value!.Id,
            new RecordPatchRequest { Patch = VolatileTestData.Patch("title", "new") },
            VolatileTestData.Operation(BaseOperationKind.Patch));

        patch.Status.Should().Be(OperationStatus.Updated);
        patch.Value!.Payload.Fields!["title"].GetString().Should().Be("new");
        patch.Value.Payload.Fields["status"].GetString().Should().Be("active");
        patch.Value.Metadata.Revision.Should().NotBe(create.Value.Metadata.Revision);
    }

    [Fact]
    public async Task JsonNullPatchStoresNullAndDoesNotRemoveField()
    {
        var store = new VolatileRecordStore();
        var collection = VolatileTestData.Collection();
        var create = await VolatileMutationTestDriver.CreateAsync(store,
            collection,
            new RecordCreateRequest { Payload = VolatileTestData.Payload(("title", "old"), ("status", "active")) },
            VolatileTestData.Operation(BaseOperationKind.Create));

        using var document = JsonDocument.Parse("""{"status":null}""");
        var patch = await VolatileMutationTestDriver.PatchAsync(store,
            collection,
            create.Value!.Id,
            new RecordPatchRequest
            {
                Patch = new RecordPayload
                {
                    Kind = RecordPayloadKind.FieldMap,
                    Fields = new Dictionary<string, JsonElement>
                    {
                        ["status"] = document.RootElement.GetProperty("status").Clone()
                    }
                }
            },
            VolatileTestData.Operation(BaseOperationKind.Patch));

        patch.Status.Should().Be(OperationStatus.Updated);
        patch.Value!.Payload.Fields!.Should().ContainKey("status");
        patch.Value.Payload.Fields["status"].ValueKind.Should().Be(JsonValueKind.Null);
        patch.Value.Payload.Fields["title"].GetString().Should().Be("old");
    }

    [Fact]
    public async Task ReplaceIsFullReplacement()
    {
        var store = new VolatileRecordStore();
        var collection = VolatileTestData.Collection();
        var create = await VolatileMutationTestDriver.CreateAsync(store,
            collection,
            new RecordCreateRequest { Payload = VolatileTestData.Payload(("title", "old"), ("status", "active")) },
            VolatileTestData.Operation(BaseOperationKind.Create));

        var replace = await VolatileMutationTestDriver.ReplaceAsync(store,
            collection,
            create.Value!.Id,
            new RecordReplaceRequest { Payload = VolatileTestData.Payload(("title", "replacement")) },
            VolatileTestData.Operation(BaseOperationKind.Replace));

        replace.Status.Should().Be(OperationStatus.Updated);
        replace.Value!.Payload.Fields!.Keys.Should().Equal("title");
    }

    [Fact]
    public async Task RevisionedPatchConflictsOnStaleRevision()
    {
        var store = new VolatileRecordStore();
        var collection = VolatileTestData.Collection();
        var create = await VolatileMutationTestDriver.CreateAsync(store,
            collection,
            new RecordCreateRequest { Payload = VolatileTestData.Payload(("title", "old")) },
            VolatileTestData.Operation(BaseOperationKind.Create));

        var stale = new RevisionToken("mem:stale");
        var result = await VolatileMutationTestDriver.PatchAsync(store,
            collection,
            create.Value!.Id,
            new RecordPatchRequest
            {
                Patch = VolatileTestData.Patch("title", "new"),
                ExpectedRevision = stale
            },
            VolatileTestData.Operation(BaseOperationKind.Patch));

        result.Status.Should().Be(OperationStatus.Conflict);
        result.Error!.Conflict!.Kind.Should().Be(ConflictKind.Revision);
    }

    [Fact]
    public async Task ReplaceHonorsExpectedRevision()
    {
        var store = new VolatileRecordStore();
        var collection = VolatileTestData.Collection();
        var create = await VolatileMutationTestDriver.CreateAsync(store,
            collection,
            new RecordCreateRequest { Payload = VolatileTestData.Payload(("title", "old")) },
            VolatileTestData.Operation(BaseOperationKind.Create));

        var conflict = await VolatileMutationTestDriver.ReplaceAsync(store,
            collection,
            create.Value!.Id,
            new RecordReplaceRequest
            {
                ExpectedRevision = new RevisionToken("mem:stale"),
                Payload = VolatileTestData.Payload(("title", "new"))
            },
            VolatileTestData.Operation(BaseOperationKind.Replace));

        conflict.Status.Should().Be(OperationStatus.Conflict);
        conflict.Error!.Conflict!.Kind.Should().Be(ConflictKind.Revision);

        var updated = await VolatileMutationTestDriver.ReplaceAsync(store,
            collection,
            create.Value.Id,
            new RecordReplaceRequest
            {
                ExpectedRevision = create.Value.Metadata.Revision,
                Payload = VolatileTestData.Payload(("title", "new"))
            },
            VolatileTestData.Operation(BaseOperationKind.Replace));

        updated.Status.Should().Be(OperationStatus.Updated);
        updated.Value!.Payload.Fields!["title"].GetString().Should().Be("new");
    }

    [Fact]
    public async Task MissingPatchAndReplaceReturnNotFoundWithError()
    {
        var store = new VolatileRecordStore();
        var collection = VolatileTestData.Collection();

        var patch = await VolatileMutationTestDriver.PatchAsync(store,
            collection,
            new RecordId("missing"),
            new RecordPatchRequest { Patch = VolatileTestData.Patch("title", "new") },
            VolatileTestData.Operation(BaseOperationKind.Patch));
        var replace = await VolatileMutationTestDriver.ReplaceAsync(store,
            collection,
            new RecordId("missing"),
            new RecordReplaceRequest { Payload = VolatileTestData.Payload(("title", "new")) },
            VolatileTestData.Operation(BaseOperationKind.Replace));

        patch.Status.Should().Be(OperationStatus.NotFound);
        patch.Error.Should().NotBeNull();
        replace.Status.Should().Be(OperationStatus.NotFound);
        replace.Error.Should().NotBeNull();
    }

    [Fact]
    public async Task DirectDeleteHonorsExpectedRevision()
    {
        var store = new VolatileRecordStore();
        var collection = VolatileTestData.Collection();
        var create = await VolatileMutationTestDriver.CreateAsync(store,
            collection,
            new RecordCreateRequest { Payload = VolatileTestData.Payload(("title", "old")) },
            VolatileTestData.Operation(BaseOperationKind.Create));

        var conflict = await VolatileMutationTestDriver.DeleteAsync(store,
            collection,
            create.Value!.Id,
            new RecordDeleteRequest { ExpectedRevision = new RevisionToken("mem:stale") },
            VolatileTestData.Operation(BaseOperationKind.Delete));
        conflict.Status.Should().Be(OperationStatus.Conflict);

        var deleted = await VolatileMutationTestDriver.DeleteAsync(store,
            collection,
            create.Value.Id,
            new RecordDeleteRequest { ExpectedRevision = create.Value.Metadata.Revision },
            VolatileTestData.Operation(BaseOperationKind.Delete));
        deleted.Status.Should().Be(OperationStatus.Deleted);
    }
}
