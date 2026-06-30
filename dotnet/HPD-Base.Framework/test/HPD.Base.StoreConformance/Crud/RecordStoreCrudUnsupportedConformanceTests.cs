namespace HPD.Base.StoreConformance.Crud;

public abstract class RecordStoreCrudUnsupportedConformanceTests<TFixture> : RecordStoreConformanceTestBase<TFixture>
    where TFixture : IRecordStoreConformanceFixture, new()
{
    [Fact]
    public async Task CrudMethodsAdvertisedUnsupportedFailClosed()
    {
        var store = await CreateStoreAsync();
        var id = new RecordId("unsupported-crud");

        if (!Capabilities.Crud.List)
        {
            var result = await store.ListAsync(Collection, RecordStoreConformanceQueries.Empty, Operation(BaseOperationKind.List));
            RecordStoreConformanceAssertions.Failure(result, OperationStatus.Unsupported, OperationStatus.CapabilityUnavailable);
        }

        if (!Capabilities.Crud.Get)
        {
            var result = await store.GetAsync(Collection, id, Operation(BaseOperationKind.Get, id));
            RecordStoreConformanceAssertions.Failure(result, OperationStatus.Unsupported, OperationStatus.CapabilityUnavailable);
        }

        if (!Capabilities.Crud.Create)
        {
            var result = await store.CreateAsync(
                Collection,
                new RecordCreateRequest { RequestedId = id, Payload = RecordStoreConformanceData.Payload(("title", "one")) },
                Operation(BaseOperationKind.Create, id));
            RecordStoreConformanceAssertions.Failure(result, OperationStatus.Unsupported, OperationStatus.CapabilityUnavailable, OperationStatus.ValidationFailed);
        }

        if (!Capabilities.Crud.Patch)
        {
            var result = await store.PatchAsync(
                Collection,
                id,
                new RecordPatchRequest { Patch = RecordStoreConformanceData.Patch(("title", RecordStoreConformanceData.StringElement("two"))) },
                Operation(BaseOperationKind.Patch, id));
            RecordStoreConformanceAssertions.Failure(result, OperationStatus.Unsupported, OperationStatus.CapabilityUnavailable, OperationStatus.ValidationFailed);
        }

        if (!Capabilities.Crud.Replace)
        {
            var result = await store.ReplaceAsync(
                Collection,
                id,
                new RecordReplaceRequest { Payload = RecordStoreConformanceData.Payload(("title", "two")) },
                Operation(BaseOperationKind.Replace, id));
            RecordStoreConformanceAssertions.Failure(result, OperationStatus.Unsupported, OperationStatus.CapabilityUnavailable, OperationStatus.ValidationFailed);
        }

        if (!Capabilities.Crud.Delete)
        {
            var result = await store.DeleteAsync(
                Collection,
                id,
                new RecordDeleteRequest(),
                Operation(BaseOperationKind.Delete, id));
            RecordStoreConformanceAssertions.Failure(result, OperationStatus.Unsupported, OperationStatus.CapabilityUnavailable);
        }
    }
}
