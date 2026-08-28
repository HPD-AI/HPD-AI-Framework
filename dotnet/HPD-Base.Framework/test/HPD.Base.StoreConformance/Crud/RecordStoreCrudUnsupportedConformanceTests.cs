namespace HPD.Base.StoreConformance.Crud;

public abstract class RecordStoreCrudUnsupportedConformanceTests<TFixture> : RecordStoreConformanceTestBase<TFixture>
    where TFixture : IRecordStoreConformanceFixture, new()
{
    [Fact]
    public async Task CrudMethodsAdvertisedUnsupportedFailClosed()
    {
        var store = await CreateStoreAsync();
        var id = RecordId.Create("unsupported-crud");

        if (!Capabilities.Read.List)
        {
            var result = await store.ListAsync(Collection, RecordStoreConformanceQueries.Empty, Operation(BaseOperationKind.List));
            RecordStoreConformanceAssertions.Failure(result, OperationStatus.Unsupported, OperationStatus.CapabilityUnavailable);
        }

        if (!Capabilities.Read.Get)
        {
            var result = await store.GetAsync(Collection, id, Operation(BaseOperationKind.Get, id));
            RecordStoreConformanceAssertions.Failure(result, OperationStatus.Unsupported, OperationStatus.CapabilityUnavailable);
        }

        if (!Capabilities.Mutation.Create)
        {
            var result = await store.CreateAsync(
                Collection,
                new RecordCreateRequest { RequestedId = id, Payload = RecordStoreConformanceData.Payload(("title", "one")) },
                Operation(BaseOperationKind.Create, id));
            RecordStoreConformanceAssertions.Failure(result, OperationStatus.Unsupported, OperationStatus.CapabilityUnavailable, OperationStatus.ValidationFailed);
        }

        if (!Capabilities.Mutation.Patch)
        {
            var result = await store.PatchAsync(
                Collection,
                id,
                new RecordPatchRequest { Patch = RecordStoreConformanceData.Patch(("title", RecordStoreConformanceData.StringElement("two"))), RemovedFieldIds = [] },
                Operation(BaseOperationKind.Patch, id));
            RecordStoreConformanceAssertions.Failure(result, OperationStatus.Unsupported, OperationStatus.CapabilityUnavailable, OperationStatus.ValidationFailed);
        }

        if (!Capabilities.Mutation.Replace)
        {
            var result = await store.ReplaceAsync(
                Collection,
                id,
                new RecordReplaceRequest { Payload = RecordStoreConformanceData.Payload(("title", "two")) },
                Operation(BaseOperationKind.Replace, id));
            RecordStoreConformanceAssertions.Failure(result, OperationStatus.Unsupported, OperationStatus.CapabilityUnavailable, OperationStatus.ValidationFailed);
        }

        if (!Capabilities.Mutation.Delete)
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
