namespace HPD.Base.StoreConformance.Mutations;

public abstract class RecordStoreRevisionConformanceTests<TFixture> : RecordStoreConformanceTestBase<TFixture>
    where TFixture : IRecordStoreConformanceFixture, new()
{
    [Fact]
    public async Task RevisionsChangeOnMutationAndRejectStaleExpectedRevisionWhenSupported()
    {
        if (Capabilities.Revision?.Supported != true || !Capabilities.Crud.Create || !Capabilities.Crud.Patch || !Capabilities.Crud.Replace)
        {
            return;
        }

        var store = await CreateStoreAsync();
        var create = await store.CreateAsync(
            Collection,
            new RecordCreateRequest
            {
                RequestedId = new RecordId("revision-target"),
                Payload = RecordStoreConformanceData.Payload(("title", "old"))
            },
            Operation(BaseOperationKind.Create, new RecordId("revision-target")));
        RecordStoreConformanceAssertions.Success(create, OperationStatus.Created);
        Assert.NotNull(create.Value!.Metadata.Revision);

        var patch = await store.PatchAsync(
            Collection,
            create.Value.Id,
            new RecordPatchRequest
            {
                ExpectedRevision = create.Value.Metadata.Revision,
                Patch = RecordStoreConformanceData.Patch(("title", RecordStoreConformanceData.StringElement("patched")))
            },
            Operation(BaseOperationKind.Patch, create.Value.Id));
        RecordStoreConformanceAssertions.Success(patch, OperationStatus.Updated);
        Assert.NotEqual(create.Value.Metadata.Revision, patch.Value!.Metadata.Revision);

        var staleReplace = await store.ReplaceAsync(
            Collection,
            create.Value.Id,
            new RecordReplaceRequest
            {
                ExpectedRevision = create.Value.Metadata.Revision,
                Payload = RecordStoreConformanceData.Payload(("title", "stale"))
            },
            Operation(BaseOperationKind.Replace, create.Value.Id));
        RecordStoreConformanceAssertions.Failure(staleReplace, OperationStatus.Conflict);

        if (Capabilities.Crud.Get)
        {
            var get = await store.GetAsync(Collection, create.Value.Id, Operation(BaseOperationKind.Get, create.Value.Id));
            RecordStoreConformanceAssertions.Success(get, OperationStatus.Ok);
            RecordStoreConformanceAssertions.HasField(get.Value!, "title", "patched");
        }
    }

    [Fact]
    public async Task RevisionedOptionalInterfaceEnforcesExpectedRevisionWhenAdvertised()
    {
        if (Capabilities.Revision?.Supported != true || Capabilities.Revision.Patch != true || !Capabilities.Crud.Create)
        {
            return;
        }

        var store = await CreateStoreAsync();
        if (store is not IRevisionedRecordStore revisioned)
        {
            Assert.Fail("Store advertises revisioned patch/replace but does not implement IRevisionedRecordStore.");
            return;
        }

        var record = await CreateRecordAsync(store, "revision-interface", ("title", "old"));
        var stale = new RevisionToken("stale-revision");
        var patch = await revisioned.PatchIfRevisionAsync(
            Collection,
            record.Id,
            new RecordPatchRequest { Patch = RecordStoreConformanceData.Patch(("title", RecordStoreConformanceData.StringElement("new"))) },
            stale,
            Operation(BaseOperationKind.Patch, record.Id));
        RecordStoreConformanceAssertions.Failure(patch, OperationStatus.Conflict);

        var expectedRevision = Assert.IsType<RevisionToken>(record.Metadata.Revision);
        var replace = await revisioned.ReplaceIfRevisionAsync(
            Collection,
            record.Id,
            new RecordReplaceRequest { Payload = RecordStoreConformanceData.Payload(("title", "new")) },
            expectedRevision,
            Operation(BaseOperationKind.Replace, record.Id));
        RecordStoreConformanceAssertions.Success(replace, OperationStatus.Updated);
    }

    [Fact]
    public async Task ExpectedRevisionDeleteIsAtomicWhenAdvertised()
    {
        if (Capabilities.Revision?.Supported != true || Capabilities.Revision.Delete != true || !Capabilities.Crud.Create || !Capabilities.Crud.Delete)
        {
            return;
        }

        var store = await CreateStoreAsync();
        var record = await CreateRecordAsync(store, "revision-delete", ("title", "old"));
        var conflict = await store.DeleteAsync(
            Collection,
            record.Id,
            new RecordDeleteRequest { ExpectedRevision = new RevisionToken("stale-revision") },
            Operation(BaseOperationKind.Delete, record.Id));
        RecordStoreConformanceAssertions.Failure(conflict, OperationStatus.Conflict);

        if (Capabilities.Crud.Get)
        {
            var stillThere = await store.GetAsync(Collection, record.Id, Operation(BaseOperationKind.Get, record.Id));
            RecordStoreConformanceAssertions.Success(stillThere, OperationStatus.Ok);
        }

        var deleted = await store.DeleteAsync(
            Collection,
            record.Id,
            new RecordDeleteRequest { ExpectedRevision = record.Metadata.Revision },
            Operation(BaseOperationKind.Delete, record.Id));
        RecordStoreConformanceAssertions.Success(deleted, OperationStatus.Deleted);
    }

    [Fact]
    public async Task ExpectedRevisionRequestsFailClosedWhenRevisionIsNotAdvertised()
    {
        if (Capabilities.Revision?.Supported == true || !Capabilities.Crud.Create)
        {
            return;
        }

        var store = await CreateStoreAsync();
        var record = await CreateRecordAsync(store, "revision-unsupported", ("title", "old"));
        var expected = new RevisionToken("opaque-expected");

        if (Capabilities.Crud.Patch)
        {
            var patch = await store.PatchAsync(
                Collection,
                record.Id,
                new RecordPatchRequest
                {
                    ExpectedRevision = expected,
                    Patch = RecordStoreConformanceData.Patch(("title", RecordStoreConformanceData.StringElement("patched")))
                },
                Operation(BaseOperationKind.Patch, record.Id));
            RecordStoreConformanceAssertions.Failure(patch, OperationStatus.Unsupported, OperationStatus.CapabilityUnavailable, OperationStatus.ValidationFailed);
        }

        if (Capabilities.Crud.Replace)
        {
            var replace = await store.ReplaceAsync(
                Collection,
                record.Id,
                new RecordReplaceRequest
                {
                    ExpectedRevision = expected,
                    Payload = RecordStoreConformanceData.Payload(("title", "replaced"))
                },
                Operation(BaseOperationKind.Replace, record.Id));
            RecordStoreConformanceAssertions.Failure(replace, OperationStatus.Unsupported, OperationStatus.CapabilityUnavailable, OperationStatus.ValidationFailed);
        }

        if (Capabilities.Crud.Delete)
        {
            var delete = await store.DeleteAsync(
                Collection,
                record.Id,
                new RecordDeleteRequest { ExpectedRevision = expected },
                Operation(BaseOperationKind.Delete, record.Id));
            RecordStoreConformanceAssertions.Failure(delete, OperationStatus.Unsupported, OperationStatus.CapabilityUnavailable, OperationStatus.ValidationFailed);
        }

        if (Capabilities.Crud.Get)
        {
            var get = await store.GetAsync(Collection, record.Id, Operation(BaseOperationKind.Get, record.Id));
            RecordStoreConformanceAssertions.Success(get, OperationStatus.Ok);
            RecordStoreConformanceAssertions.HasField(get.Value!, "title", "old");
        }
    }
}
