using HPD.Base.Tests.Volatile.TestDoubles;

namespace HPD.Base.Tests.Volatile.Mutations;

public sealed class CreateGetDeleteSemanticsTests
{
    [Fact]
    public async Task CreateGetAndDeleteRoundTripWithMetadata()
    {
        var store = new VolatileRecordStore(new HPDBaseVolatileStoreOptions { StoreId = "primary" });
        var collection = VolatileTestData.Collection();

        var create = await VolatileMutationTestDriver.CreateAsync(store,
            collection,
            new RecordCreateRequest { Payload = VolatileTestData.Payload(("title", "hello")) },
            VolatileTestData.Operation(BaseOperationKind.Create));

        create.Status.Should().Be(OperationStatus.Created);
        create.Value!.Id.Value.Should().NotBeNullOrWhiteSpace();
        create.Value.Metadata.StoreId.Should().Be("primary");
        create.Value.Metadata.CreatedAt.Should().Be(DateTimeOffset.UnixEpoch);
        create.Value.Metadata.UpdatedAt.Should().Be(DateTimeOffset.UnixEpoch);
        create.Value.Metadata.Revision.Should().NotBeNull();
        create.Revision!.Guarantee.Should().Be(RevisionGuarantee.Store);

        var get = await store.GetAsync(collection, create.Value.Id, VolatileTestData.Operation(BaseOperationKind.Get));
        get.Status.Should().Be(OperationStatus.Ok);
        get.Value!.Payload.Fields!["title"].GetString().Should().Be("hello");

        var delete = await VolatileMutationTestDriver.DeleteAsync(store,
            collection,
            create.Value.Id,
            new RecordDeleteRequest { ReturnPrevious = true },
            VolatileTestData.Operation(BaseOperationKind.Delete));

        delete.Status.Should().Be(OperationStatus.Deleted);
        delete.Value!.Previous.Should().NotBeNull();
        delete.Value.Previous!.Payload.Fields!["title"].GetString().Should().Be("hello");

        var missing = await store.GetAsync(collection, create.Value.Id, VolatileTestData.Operation(BaseOperationKind.Get));
        missing.Status.Should().Be(OperationStatus.NotFound);
        missing.Error.Should().NotBeNull();
    }

    [Fact]
    public async Task SameRequestedIdCanExistInDifferentCollections()
    {
        var store = new VolatileRecordStore();
        var id = new RecordId("shared");

        var first = await VolatileMutationTestDriver.CreateAsync(store,
            VolatileTestData.Collection("a"),
            new RecordCreateRequest { RequestedId = id, Payload = VolatileTestData.Payload(("title", "a")) },
            VolatileTestData.Operation(BaseOperationKind.Create, "a"));
        var second = await VolatileMutationTestDriver.CreateAsync(store,
            VolatileTestData.Collection("b"),
            new RecordCreateRequest { RequestedId = id, Payload = VolatileTestData.Payload(("title", "b")) },
            VolatileTestData.Operation(BaseOperationKind.Create, "b"));

        first.Status.Should().Be(OperationStatus.Created);
        second.Status.Should().Be(OperationStatus.Created);
    }

    [Fact]
    public async Task DuplicateRequestedIdConflictsWithinCollection()
    {
        var store = new VolatileRecordStore();
        var collection = VolatileTestData.Collection();
        var request = new RecordCreateRequest
        {
            RequestedId = new RecordId("same"),
            Payload = VolatileTestData.Payload(("title", "one"))
        };

        (await VolatileMutationTestDriver.CreateAsync(store, collection, request, VolatileTestData.Operation(BaseOperationKind.Create))).Status.Should().Be(OperationStatus.Created);
        var duplicate = await VolatileMutationTestDriver.CreateAsync(store, collection, request, VolatileTestData.Operation(BaseOperationKind.Create));

        duplicate.Status.Should().Be(OperationStatus.Conflict);
        duplicate.Error.Should().NotBeNull();
    }

    [Fact]
    public async Task IdempotencyKeyIsUnsupported()
    {
        var store = new VolatileRecordStore();

        var result = await VolatileMutationTestDriver.CreateAsync(store,
            VolatileTestData.Collection(),
            new RecordCreateRequest
            {
                IdempotencyKey = "same-request",
                Payload = VolatileTestData.Payload(("title", "one"))
            },
            VolatileTestData.Operation(BaseOperationKind.Create));

        result.Status.Should().Be(OperationStatus.Unsupported);
        result.Error.Should().NotBeNull();
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("bad\u0001id")]
    public async Task InvalidRequestedIdsAreRejected(string value)
    {
        var store = new VolatileRecordStore();

        var result = await VolatileMutationTestDriver.CreateAsync(store,
            VolatileTestData.Collection(),
            new RecordCreateRequest
            {
                RequestedId = new RecordId(value),
                Payload = VolatileTestData.Payload(("title", "one"))
            },
            VolatileTestData.Operation(BaseOperationKind.Create));

        result.Status.Should().Be(OperationStatus.ValidationFailed);
        result.Error.Should().NotBeNull();
    }

    [Fact]
    public async Task MissingDeleteReturnsNotFoundWithError()
    {
        var store = new VolatileRecordStore();

        var result = await VolatileMutationTestDriver.DeleteAsync(store,
            VolatileTestData.Collection(),
            new RecordId("missing"),
            new RecordDeleteRequest(),
            VolatileTestData.Operation(BaseOperationKind.Delete));

        result.Status.Should().Be(OperationStatus.NotFound);
        result.Error.Should().NotBeNull();
    }
}
