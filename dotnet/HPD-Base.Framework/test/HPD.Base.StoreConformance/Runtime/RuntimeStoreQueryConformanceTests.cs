using HPD.Base.Runtime.Operations;

namespace HPD.Base.StoreConformance.Runtime;

public abstract class RuntimeStoreQueryConformanceTests<TFixture> : RuntimeStoreRegistrationConformanceTests<TFixture>
    where TFixture : IRuntimeStoreConformanceFixture, new()
{
    [Fact]
    public async Task RuntimeRejectsUnsupportedQueryShapeBeforeStoreExecution()
    {
        if (!Capabilities.Read.List)
        {
            return;
        }

        var services = await Fixture.CreateRuntimeServicesAsync();
        var runtime = Required<IBaseRecordRuntime>(services);

        if (Capabilities.Query.Include?.Supported != true)
        {
            var include = await runtime.ListAsync(
                Collection.Id,
                new RecordQuery { Include = [new QueryInclude { Path = "relation" }] },
                Principal,
                Operation(BaseOperationKind.List));
            RecordStoreConformanceAssertions.Failure(include, OperationStatus.Unsupported, OperationStatus.CapabilityUnavailable, OperationStatus.ValidationFailed);
        }

        if (Capabilities.Query.Filter.Operators?.Contains(FilterOperator.Like) != true)
        {
            var unsupportedOperator = await runtime.ListAsync(
                Collection.Id,
                new RecordQuery { Filter = RecordStoreConformanceQueries.UnsupportedLike("title", "a") },
                Principal,
                Operation(BaseOperationKind.List));
            RecordStoreConformanceAssertions.Failure(unsupportedOperator, OperationStatus.Unsupported, OperationStatus.CapabilityUnavailable, OperationStatus.ValidationFailed);
        }
    }
}
