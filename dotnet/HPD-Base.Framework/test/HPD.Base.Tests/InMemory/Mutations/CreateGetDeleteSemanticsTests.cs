using HPD.Base.Tests.InMemory.TestDoubles;

namespace HPD.Base.Tests.InMemory.Mutations;

public sealed class CreateGetDeleteSemanticsTests
{
    [Fact]
    public async Task CreateGetAndDeleteRoundTripWithMetadata()
    {
        var store = new InMemoryRecordStore(new HPDBaseInMemoryStoreOptions { StoreId = "primary" });
        var collection = InMemoryTestData.Collection();

        var create = await InMemoryMutationTestDriver.CreateAsync(store,
            collection,
            new RecordCreateRequest { Payload = InMemoryTestData.Payload(("title", "hello")) },
            InMemoryTestData.Operation(BaseOperationKind.Create));

        create.Status.Should().Be(OperationStatus.Created);
        create.Value!.Id.Value.Should().NotBeNullOrWhiteSpace();
        create.Value.Metadata.StoreId.Should().Be("primary");
        create.Value.Metadata.CreatedAt.Should().Be(DateTimeOffset.UnixEpoch);
        create.Value.Metadata.UpdatedAt.Should().Be(DateTimeOffset.UnixEpoch);
        create.Value.Metadata.Revision.Should().NotBeNull();
        create.Revision!.Guarantee.Should().Be(RevisionGuarantee.Store);

        var get = await store.GetAsync(collection, create.Value.Id, InMemoryTestData.Operation(BaseOperationKind.Get));
        get.Status.Should().Be(OperationStatus.Ok);
        get.Value!.Payload.Fields!["title"].GetString().Should().Be("hello");

        var delete = await InMemoryMutationTestDriver.DeleteAsync(store,
            collection,
            create.Value.Id,
            new RecordDeleteRequest { ReturnPrevious = true },
            InMemoryTestData.Operation(BaseOperationKind.Delete));

        delete.Status.Should().Be(OperationStatus.Deleted);
        delete.Value!.Previous.Should().NotBeNull();
        delete.Value.Previous!.Payload.Fields!["title"].GetString().Should().Be("hello");

        var missing = await store.GetAsync(collection, create.Value.Id, InMemoryTestData.Operation(BaseOperationKind.Get));
        missing.Status.Should().Be(OperationStatus.NotFound);
        missing.Error.Should().NotBeNull();
    }

    [Fact]
    public async Task SameRequestedIdCanExistInDifferentCollections()
    {
        var store = new InMemoryRecordStore();
        var id = new RecordId("shared");

        var first = await InMemoryMutationTestDriver.CreateAsync(store,
            InMemoryTestData.Collection("a"),
            new RecordCreateRequest { RequestedId = id, Payload = InMemoryTestData.Payload(("title", "a")) },
            InMemoryTestData.Operation(BaseOperationKind.Create, "a"));
        var second = await InMemoryMutationTestDriver.CreateAsync(store,
            InMemoryTestData.Collection("b"),
            new RecordCreateRequest { RequestedId = id, Payload = InMemoryTestData.Payload(("title", "b")) },
            InMemoryTestData.Operation(BaseOperationKind.Create, "b"));

        first.Status.Should().Be(OperationStatus.Created);
        second.Status.Should().Be(OperationStatus.Created);
    }

    [Fact]
    public async Task DuplicateRequestedIdConflictsWithinCollection()
    {
        var store = new InMemoryRecordStore();
        var collection = InMemoryTestData.Collection();
        var request = new RecordCreateRequest
        {
            RequestedId = new RecordId("same"),
            Payload = InMemoryTestData.Payload(("title", "one"))
        };

        (await InMemoryMutationTestDriver.CreateAsync(store, collection, request, InMemoryTestData.Operation(BaseOperationKind.Create))).Status.Should().Be(OperationStatus.Created);
        var duplicate = await InMemoryMutationTestDriver.CreateAsync(store, collection, request, InMemoryTestData.Operation(BaseOperationKind.Create));

        duplicate.Status.Should().Be(OperationStatus.Conflict);
        duplicate.Error.Should().NotBeNull();
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("bad\u0001id")]
    public async Task InvalidRequestedIdsAreRejected(string value)
    {
        var store = new InMemoryRecordStore();

        var result = await InMemoryMutationTestDriver.CreateAsync(store,
            InMemoryTestData.Collection(),
            new RecordCreateRequest
            {
                RequestedId = new RecordId(value),
                Payload = InMemoryTestData.Payload(("title", "one"))
            },
            InMemoryTestData.Operation(BaseOperationKind.Create));

        result.Status.Should().Be(OperationStatus.ValidationFailed);
        result.Error.Should().NotBeNull();
    }

    [Fact]
    public async Task MissingDeleteReturnsNotFoundWithError()
    {
        var store = new InMemoryRecordStore();

        var result = await InMemoryMutationTestDriver.DeleteAsync(store,
            InMemoryTestData.Collection(),
            new RecordId("missing"),
            new RecordDeleteRequest(),
            InMemoryTestData.Operation(BaseOperationKind.Delete));

        result.Status.Should().Be(OperationStatus.NotFound);
        result.Error.Should().NotBeNull();
    }
}
