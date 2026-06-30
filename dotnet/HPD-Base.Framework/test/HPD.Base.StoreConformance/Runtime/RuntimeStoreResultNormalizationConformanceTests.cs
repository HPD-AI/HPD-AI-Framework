using HPD.Base.Runtime.Operations;

namespace HPD.Base.StoreConformance.Runtime;

public abstract class RuntimeStoreResultNormalizationConformanceTests<TFixture> : RuntimeStoreRegistrationConformanceTests<TFixture>
    where TFixture : IConfigurableRuntimeStoreConformanceFixture, new()
{
    [Fact]
    public async Task RuntimeMapsKnownStoreDependencyExceptionsToStoreError()
    {
        if (!Capabilities.Crud.Get)
        {
            return;
        }

        var throwingStore = new ConformanceThrowingRecordStore(Capabilities, new TimeoutException("timeout"));
        var services = await Fixture.CreateRuntimeServicesAsync(new RuntimeStoreConformanceOptions
        {
            StoreOverride = throwingStore
        });
        var runtime = Required<IBaseRecordRuntime>(services);

        var result = await runtime.GetAsync(
            Collection.Id,
            new RecordId("dependency-failure"),
            Principal,
            Operation(BaseOperationKind.Get, new RecordId("dependency-failure")));

        RecordStoreConformanceAssertions.Failure(result, OperationStatus.StoreError);
        Assert.True(result.Error!.Store?.Retryable);
    }

    [Fact]
    public async Task RuntimeDoesNotSwallowProgrammerInvariantExceptions()
    {
        if (!Capabilities.Crud.Get)
        {
            return;
        }

        var throwingStore = new ConformanceThrowingRecordStore(Capabilities, new InvalidOperationException("programmer bug"));
        var services = await Fixture.CreateRuntimeServicesAsync(new RuntimeStoreConformanceOptions
        {
            StoreOverride = throwingStore
        });
        var runtime = Required<IBaseRecordRuntime>(services);

        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
        {
            await runtime.GetAsync(
                Collection.Id,
                new RecordId("programmer-failure"),
                Principal,
                Operation(BaseOperationKind.Get, new RecordId("programmer-failure")));
        });
    }
}
