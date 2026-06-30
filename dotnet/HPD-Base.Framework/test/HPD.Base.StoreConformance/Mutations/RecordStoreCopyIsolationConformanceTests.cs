namespace HPD.Base.StoreConformance.Mutations;

public abstract class RecordStoreCopyIsolationConformanceTests<TFixture> : RecordStoreConformanceTestBase<TFixture>
    where TFixture : IRecordStoreConformanceFixture, new()
{
    [Fact]
    public async Task CreateInputAndGetResultsAreDeepCopiedWhenCrudIsSupported()
    {
        if (!Capabilities.Crud.Create || !Capabilities.Crud.Get)
        {
            return;
        }

        var store = await CreateStoreAsync();
        RecordId id;
        using (var document = JsonDocument.Parse("""{"title":"safe"}"""))
        {
            var create = await store.CreateAsync(
                Collection,
                new RecordCreateRequest
                {
                    RequestedId = new RecordId("copy-create"),
                    Payload = new RecordPayload
                    {
                        Kind = RecordPayloadKind.Json,
                        Json = document.RootElement
                    }
                },
                Operation(BaseOperationKind.Create, new RecordId("copy-create")));
            RecordStoreConformanceAssertions.Success(create, OperationStatus.Created);
            id = create.Value!.Id;
        }

        var firstGet = await store.GetAsync(Collection, id, Operation(BaseOperationKind.Get, id));
        RecordStoreConformanceAssertions.Success(firstGet, OperationStatus.Ok);
        RecordStoreConformanceAssertions.HasField(firstGet.Value!, "title", "safe");

        firstGet.Value!.Payload.Fields!["title"] = RecordStoreConformanceData.StringElement("mutated");

        var secondGet = await store.GetAsync(Collection, id, Operation(BaseOperationKind.Get, id));
        RecordStoreConformanceAssertions.Success(secondGet, OperationStatus.Ok);
        RecordStoreConformanceAssertions.HasField(secondGet.Value!, "title", "safe");
    }

    [Fact]
    public async Task ListAndDeletePreviousResultsAreDeepCopiedWhenSupported()
    {
        if (!Capabilities.Crud.Create || !Capabilities.Crud.List || !Capabilities.Crud.Get || !Capabilities.Crud.Delete)
        {
            return;
        }

        var store = await CreateStoreAsync();
        var record = await CreateRecordAsync(store, "copy-list-delete", ("title", "safe"));

        var list = await store.ListAsync(Collection, RecordStoreConformanceQueries.Empty, Operation(BaseOperationKind.List));
        RecordStoreConformanceAssertions.Success(list, OperationStatus.Ok);
        var listed = Assert.Single(list.Value!.Items, item => item.Id == record.Id);
        listed.Payload.Fields!["title"] = RecordStoreConformanceData.StringElement("mutated");

        var get = await store.GetAsync(Collection, record.Id, Operation(BaseOperationKind.Get, record.Id));
        RecordStoreConformanceAssertions.Success(get, OperationStatus.Ok);
        RecordStoreConformanceAssertions.HasField(get.Value!, "title", "safe");

        var delete = await store.DeleteAsync(
            Collection,
            record.Id,
            new RecordDeleteRequest { ReturnPrevious = true },
            Operation(BaseOperationKind.Delete, record.Id));
        RecordStoreConformanceAssertions.Success(delete, OperationStatus.Deleted);
        if (delete.Value!.Previous is not null)
        {
            delete.Value.Previous.Payload.Fields!["title"] = RecordStoreConformanceData.StringElement("mutated-delete");
        }
    }

    [Fact]
    public async Task PatchInputIsDeepCopiedWhenSupported()
    {
        if (!Capabilities.Crud.Create || !Capabilities.Crud.Patch || !Capabilities.Crud.Get)
        {
            return;
        }

        var store = await CreateStoreAsync();
        var record = await CreateRecordAsync(store, "copy-patch", ("title", "old"), ("status", "active"));
        var fields = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        using (var document = JsonDocument.Parse("""{"title":"patched"}"""))
        {
            fields["title"] = document.RootElement.GetProperty("title");
            var patch = await store.PatchAsync(
                Collection,
                record.Id,
                new RecordPatchRequest
                {
                    Patch = new RecordPayload
                    {
                        Kind = RecordPayloadKind.FieldMap,
                        Fields = fields
                    }
                },
                Operation(BaseOperationKind.Patch, record.Id));
            RecordStoreConformanceAssertions.Success(patch, OperationStatus.Updated);
        }

        fields["title"] = RecordStoreConformanceData.StringElement("mutated-after-patch");

        var get = await store.GetAsync(Collection, record.Id, Operation(BaseOperationKind.Get, record.Id));
        RecordStoreConformanceAssertions.Success(get, OperationStatus.Ok);
        RecordStoreConformanceAssertions.HasField(get.Value!, "title", "patched");
        RecordStoreConformanceAssertions.HasField(get.Value!, "status", "active");
    }

    [Fact]
    public async Task ReplaceInputIsDeepCopiedWhenSupported()
    {
        if (!Capabilities.Crud.Create || !Capabilities.Crud.Replace || !Capabilities.Crud.Get)
        {
            return;
        }

        var store = await CreateStoreAsync();
        var record = await CreateRecordAsync(store, "copy-replace", ("title", "old"));
        RecordPayload payload;
        using (var document = JsonDocument.Parse("""{"title":"replaced"}"""))
        {
            payload = new RecordPayload
            {
                Kind = RecordPayloadKind.Json,
                Json = document.RootElement
            };

            var replace = await store.ReplaceAsync(
                Collection,
                record.Id,
                new RecordReplaceRequest { Payload = payload },
                Operation(BaseOperationKind.Replace, record.Id));
            RecordStoreConformanceAssertions.Success(replace, OperationStatus.Updated);
        }

        var get = await store.GetAsync(Collection, record.Id, Operation(BaseOperationKind.Get, record.Id));
        RecordStoreConformanceAssertions.Success(get, OperationStatus.Ok);
        RecordStoreConformanceAssertions.HasField(get.Value!, "title", "replaced");
    }
}
