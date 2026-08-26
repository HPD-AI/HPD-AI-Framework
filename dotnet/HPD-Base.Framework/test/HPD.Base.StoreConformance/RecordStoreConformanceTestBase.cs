namespace HPD.Base.StoreConformance;

public abstract class RecordStoreConformanceTestBase<TFixture> : IAsyncLifetime
    where TFixture : IRecordStoreConformanceFixture, new()
{
    protected TFixture Fixture { get; } = new();

    protected CollectionDefinition Collection => Fixture.Collection;

    protected StoreCapabilityDescriptor Capabilities => Fixture.Capabilities;

    public async Task InitializeAsync()
    {
        await Fixture.ResetAsync();
    }

    public Task DisposeAsync() => Task.CompletedTask;

    protected ValueTask<IRecordStore> CreateStoreAsync() => Fixture.CreateStoreAsync();

    protected OperationContext Operation(BaseOperationKind operation, RecordId? id = null) =>
        Fixture.Operation(operation, id);

    protected async ValueTask<RecordEnvelope> CreateRecordAsync(
        IRecordStore store,
        string id,
        params (string Field, string Value)[] fields)
    {
        if (Fixture is IRecordStoreConformanceSeeder seeder)
        {
            return await seeder.CreateRecordAsync(
                store,
                Collection,
                id,
                fields.Select(field => (field.Field, RecordStoreConformanceData.StringElement(field.Value))).ToArray());
        }

        var result = await store.CreateAsync(
            Collection,
            new RecordCreateRequest
            {
                RequestedId = RecordId.Create(id),
                Payload = RecordStoreConformanceData.Payload(fields)
            },
            Operation(BaseOperationKind.Create, RecordId.Create(id)));

        RecordStoreConformanceAssertions.Success(result, OperationStatus.Created);
        return result.Value!;
    }

    protected async ValueTask<RecordEnvelope> CreateRecordAsync(
        IRecordStore store,
        string id,
        params (string Field, JsonElement Value)[] fields)
    {
        if (Fixture is IRecordStoreConformanceSeeder seeder)
        {
            return await seeder.CreateRecordAsync(store, Collection, id, fields);
        }

        var result = await store.CreateAsync(
            Collection,
            new RecordCreateRequest
            {
                RequestedId = RecordId.Create(id),
                Payload = RecordStoreConformanceData.Patch(fields)
            },
            Operation(BaseOperationKind.Create, RecordId.Create(id)));

        RecordStoreConformanceAssertions.Success(result, OperationStatus.Created);
        return result.Value!;
    }
}
