namespace HPD.Base.StoreConformance.Crud;

public abstract class RecordStoreCrudConformanceTests<TFixture> : RecordStoreConformanceTestBase<TFixture>
    where TFixture : IRecordStoreConformanceFixture, new()
{
    [Fact]
    public async Task CreateGetListDeleteRoundTripWhenSupported()
    {
        if (!Capabilities.Mutation.Create || !Capabilities.Read.Get || !Capabilities.Read.List || !Capabilities.Mutation.Delete)
        {
            return;
        }

        var store = await CreateStoreAsync();
        var create = await store.CreateAsync(
            Collection,
            new RecordCreateRequest
            {
                RequestedId = new RecordId("crud-roundtrip"),
                Payload = RecordStoreConformanceData.Payload(("title", "hello"))
            },
            Operation(BaseOperationKind.Create, new RecordId("crud-roundtrip")));

        RecordStoreConformanceAssertions.Success(create, OperationStatus.Created);
        RecordStoreConformanceAssertions.EnvelopeShape(create.Value!, Collection);
        RecordStoreConformanceAssertions.HasField(create.Value!, "title", "hello");

        var get = await store.GetAsync(Collection, create.Value!.Id, Operation(BaseOperationKind.Get, create.Value.Id));
        RecordStoreConformanceAssertions.Success(get, OperationStatus.Ok);
        RecordStoreConformanceAssertions.HasField(get.Value!, "title", "hello");

        var list = await store.ListAsync(Collection, RecordStoreConformanceQueries.Empty, Operation(BaseOperationKind.List));
        RecordStoreConformanceAssertions.Success(list, OperationStatus.Ok);
        RecordStoreConformanceAssertions.PageShape(list.Value!);
        Assert.Contains(list.Value!.Items, item => item.Id == create.Value.Id);

        var repeatedList = await store.ListAsync(Collection, RecordStoreConformanceQueries.Empty, Operation(BaseOperationKind.List));
        RecordStoreConformanceAssertions.Success(repeatedList, OperationStatus.Ok);
        Assert.Equal(
            list.Value.Items.Select(item => item.Id.Value).ToArray(),
            repeatedList.Value!.Items.Select(item => item.Id.Value).ToArray());

        var delete = await store.DeleteAsync(
            Collection,
            create.Value.Id,
            new RecordDeleteRequest { ReturnPrevious = true },
            Operation(BaseOperationKind.Delete, create.Value.Id));

        RecordStoreConformanceAssertions.Success(delete, OperationStatus.Deleted);
        Assert.True(delete.Value!.Deleted);
        if (delete.Value.Previous is not null)
        {
            RecordStoreConformanceAssertions.HasField(delete.Value.Previous, "title", "hello");
        }

        var repeatedDelete = await store.DeleteAsync(
            Collection,
            create.Value.Id,
            new RecordDeleteRequest(),
            Operation(BaseOperationKind.Delete, create.Value.Id));
        RecordStoreConformanceAssertions.Failure(repeatedDelete, OperationStatus.NotFound);
    }

    [Fact]
    public async Task EmptyListAndGeneratedIdCreateAreCoherentWhenSupported()
    {
        var store = await CreateStoreAsync();

        if (Capabilities.Read.List)
        {
            var empty = await store.ListAsync(Collection, RecordStoreConformanceQueries.Empty, Operation(BaseOperationKind.List));
            RecordStoreConformanceAssertions.Success(empty, OperationStatus.Ok);
            Assert.Empty(empty.Value!.Items);
            RecordStoreConformanceAssertions.PageShape(empty.Value);
        }

        if (Capabilities.Mutation.Create)
        {
            var create = await store.CreateAsync(
                Collection,
                new RecordCreateRequest { Payload = RecordStoreConformanceData.Payload(("title", "generated")) },
                Operation(BaseOperationKind.Create));
            RecordStoreConformanceAssertions.Success(create, OperationStatus.Created);
            Assert.False(string.IsNullOrWhiteSpace(create.Value!.Id.Value));
        }
    }

    [Fact]
    public async Task MissingAndInvalidIdsReturnNormalizedFailuresWhenSupported()
    {
        var store = await CreateStoreAsync();

        if (Capabilities.Read.Get)
        {
            var missing = await store.GetAsync(Collection, new RecordId("missing"), Operation(BaseOperationKind.Get, new RecordId("missing")));
            RecordStoreConformanceAssertions.Failure(missing, OperationStatus.NotFound);

            var invalid = await store.GetAsync(Collection, new RecordId(" "), Operation(BaseOperationKind.Get, new RecordId(" ")));
            RecordStoreConformanceAssertions.Failure(invalid, OperationStatus.ValidationFailed);
        }

        if (Capabilities.Mutation.Delete)
        {
            var delete = await store.DeleteAsync(
                Collection,
                new RecordId("missing"),
                new RecordDeleteRequest(),
                Operation(BaseOperationKind.Delete, new RecordId("missing")));
            RecordStoreConformanceAssertions.Failure(delete, OperationStatus.NotFound);
        }
    }

    [Fact]
    public async Task DuplicateRequestedIdConflictsAndDoesNotOverwriteWhenClientIdsAreSupported()
    {
        if (!Capabilities.Mutation.Create || !Capabilities.Read.Get ||
            Capabilities.Mutation.IdAuthority is not (IdAuthority.Client or IdAuthority.Hybrid))
        {
            return;
        }

        var store = await CreateStoreAsync();
        var id = new RecordId("duplicate-id");
        var first = await store.CreateAsync(
            Collection,
            new RecordCreateRequest { RequestedId = id, Payload = RecordStoreConformanceData.Payload(("title", "one")) },
            Operation(BaseOperationKind.Create, id));
        RecordStoreConformanceAssertions.Success(first, OperationStatus.Created);

        var duplicate = await store.CreateAsync(
            Collection,
            new RecordCreateRequest { RequestedId = id, Payload = RecordStoreConformanceData.Payload(("title", "two")) },
            Operation(BaseOperationKind.Create, id));
        RecordStoreConformanceAssertions.Failure(duplicate, OperationStatus.Conflict);

        var get = await store.GetAsync(Collection, id, Operation(BaseOperationKind.Get, id));
        RecordStoreConformanceAssertions.Success(get, OperationStatus.Ok);
        RecordStoreConformanceAssertions.HasField(get.Value!, "title", "one");
    }

    [Fact]
    public async Task IdempotencyKeyFailsClosedUntilAdvertised()
    {
        if (!Capabilities.Mutation.Create)
        {
            return;
        }

        var store = await CreateStoreAsync();
        var result = await store.CreateAsync(
            Collection,
            new RecordCreateRequest
            {
                IdempotencyKey = "same-request",
                Payload = RecordStoreConformanceData.Payload(("title", "one"))
            },
            Operation(BaseOperationKind.Create));

        RecordStoreConformanceAssertions.Failure(
            result,
            OperationStatus.Unsupported,
            OperationStatus.CapabilityUnavailable,
            OperationStatus.ValidationFailed);
    }

    [Fact]
    public async Task MalformedCreatePayloadFailsClosedWhenCreateIsSupported()
    {
        if (!Capabilities.Mutation.Create)
        {
            return;
        }

        var store = await CreateStoreAsync();
        var result = await store.CreateAsync(
            Collection,
            new RecordCreateRequest { Payload = RecordStoreConformanceData.InvalidScalarPayload() },
            Operation(BaseOperationKind.Create));

        RecordStoreConformanceAssertions.Failure(
            result,
            OperationStatus.ValidationFailed,
            OperationStatus.Unsupported);

        var invalidId = await store.CreateAsync(
            Collection,
            new RecordCreateRequest
            {
                RequestedId = new RecordId(" "),
                Payload = RecordStoreConformanceData.Payload(("title", "bad"))
            },
            Operation(BaseOperationKind.Create, new RecordId(" ")));

        RecordStoreConformanceAssertions.Failure(invalidId, OperationStatus.ValidationFailed, OperationStatus.Unsupported);
    }
}
