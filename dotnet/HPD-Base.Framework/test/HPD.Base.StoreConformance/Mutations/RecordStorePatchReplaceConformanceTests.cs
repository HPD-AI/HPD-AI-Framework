namespace HPD.Base.StoreConformance.Mutations;

public abstract class RecordStorePatchReplaceConformanceTests<TFixture> : RecordStoreConformanceTestBase<TFixture>
    where TFixture : IRecordStoreConformanceFixture, new()
{
    [Fact]
    public async Task PatchMergesTopLevelFieldsAndStoresJsonNullWhenSupported()
    {
        if (!Capabilities.Crud.Create || !Capabilities.Crud.Patch)
        {
            return;
        }

        var store = await CreateStoreAsync();
        var record = await CreateRecordAsync(store, "patch-target", ("title", "old"), ("status", "active"));
        var patch = await store.PatchAsync(
            Collection,
            record.Id,
            new RecordPatchRequest
            {
                Patch = RecordStoreConformanceData.Patch(
                    ("title", RecordStoreConformanceData.StringElement("new")),
                    ("status", RecordStoreConformanceData.Element("null")))
            },
            Operation(BaseOperationKind.Patch, record.Id));

        RecordStoreConformanceAssertions.Success(patch, OperationStatus.Updated);
        RecordStoreConformanceAssertions.HasField(patch.Value!, "title", "new");
        RecordStoreConformanceAssertions.HasNullField(patch.Value!, "status");
    }

    [Fact]
    public async Task ReplaceIsFullReplacementWhenSupported()
    {
        if (!Capabilities.Crud.Create || !Capabilities.Crud.Replace)
        {
            return;
        }

        var store = await CreateStoreAsync();
        var record = await CreateRecordAsync(store, "replace-target", ("title", "old"), ("status", "active"));
        var replace = await store.ReplaceAsync(
            Collection,
            record.Id,
            new RecordReplaceRequest { Payload = RecordStoreConformanceData.Payload(("title", "replacement")) },
            Operation(BaseOperationKind.Replace, record.Id));

        RecordStoreConformanceAssertions.Success(replace, OperationStatus.Updated);
        RecordStoreConformanceAssertions.HasField(replace.Value!, "title", "replacement");
        Assert.DoesNotContain("status", replace.Value!.Payload.Fields!.Keys);
    }

    [Fact]
    public async Task MissingAndInvalidMutationRequestsFailWithoutMutation()
    {
        if (!Capabilities.Crud.Create)
        {
            return;
        }

        var store = await CreateStoreAsync();
        var unaffected = await CreateRecordAsync(store, "unaffected", ("title", "keep"));

        if (Capabilities.Crud.Patch)
        {
            var missingPatch = await store.PatchAsync(
                Collection,
                new RecordId("missing"),
                new RecordPatchRequest { Patch = RecordStoreConformanceData.Patch(("title", RecordStoreConformanceData.StringElement("new"))) },
                Operation(BaseOperationKind.Patch, new RecordId("missing")));
            RecordStoreConformanceAssertions.Failure(missingPatch, OperationStatus.NotFound);

            var emptyPatch = await store.PatchAsync(
                Collection,
                unaffected.Id,
                new RecordPatchRequest { Patch = new RecordPayload { Kind = RecordPayloadKind.FieldMap, Fields = [] } },
                Operation(BaseOperationKind.Patch, unaffected.Id));
            RecordStoreConformanceAssertions.Failure(emptyPatch, OperationStatus.ValidationFailed, OperationStatus.Unsupported);
        }

        if (Capabilities.Crud.Replace)
        {
            var missingReplace = await store.ReplaceAsync(
                Collection,
                new RecordId("missing"),
                new RecordReplaceRequest { Payload = RecordStoreConformanceData.Payload(("title", "new")) },
                Operation(BaseOperationKind.Replace, new RecordId("missing")));
            RecordStoreConformanceAssertions.Failure(missingReplace, OperationStatus.NotFound);
        }

        if (Capabilities.Crud.Get)
        {
            var get = await store.GetAsync(Collection, unaffected.Id, Operation(BaseOperationKind.Get, unaffected.Id));
            RecordStoreConformanceAssertions.Success(get, OperationStatus.Ok);
            RecordStoreConformanceAssertions.HasField(get.Value!, "title", "keep");
        }
    }
}
