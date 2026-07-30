using HPD.Base.Runtime.Operations;
using HPD.Base.Runtime.Stores;

namespace HPD.Base.StoreConformance.Runtime;

public abstract class RuntimeStoreRegistrationConformanceTests<TFixture> : RecordStoreConformanceTestBase<TFixture>
    where TFixture : IRuntimeStoreConformanceFixture, new()
{
    [Fact]
    public async Task RuntimeRegistersAndResolvesStoreByIdAndCollection()
    {
        var services = await Fixture.CreateRuntimeServicesAsync();
        var registry = Required<IRecordStoreRegistry>(services);

        var registrations = registry.GetRegistrations();
        Assert.Contains(registrations, registration => registration.StoreId == Capabilities.StoreId);
        Assert.Same(registry.GetStore(Capabilities.StoreId), registry.GetStoreForCollection(Collection.Id));
        Assert.NotNull(registry.GetStoreForCollection(Collection.Id));
    }

    [Fact]
    public async Task RuntimeCrudRoundTripComposesStoreAndSchema()
    {
        if (!Capabilities.Mutation.Create || !Capabilities.Read.Get || !Capabilities.Read.List || !Capabilities.Mutation.Delete)
        {
            return;
        }

        var services = await Fixture.CreateRuntimeServicesAsync();
        var runtime = Required<IBaseRecordRuntime>(services);
        var id = new RecordId("runtime-roundtrip");

        var create = await runtime.CreateAsync(
            Collection.Id,
            new RecordCreateRequest
            {
                RequestedId = id,
                Payload = RecordStoreConformanceData.Payload(("title", "runtime"))
            },
            Principal,
            Operation(BaseOperationKind.Create, id));
        RecordStoreConformanceAssertions.Success(create, OperationStatus.Created);

        var get = await runtime.GetAsync(Collection.Id, id, Principal, Operation(BaseOperationKind.Get, id));
        RecordStoreConformanceAssertions.Success(get, OperationStatus.Ok);
        RecordStoreConformanceAssertions.HasField(get.Value!, "title", "runtime");

        var list = await runtime.ListAsync(Collection.Id, RecordStoreConformanceQueries.Empty, Principal, Operation(BaseOperationKind.List));
        RecordStoreConformanceAssertions.Success(list, OperationStatus.Ok);
        Assert.Contains(list.Value!.Items, item => item.Id == id);

        var delete = await runtime.DeleteAsync(
            Collection.Id,
            id,
            new RecordDeleteRequest { ReturnPrevious = true },
            Principal,
            Operation(BaseOperationKind.Delete, id));
        RecordStoreConformanceAssertions.Success(delete, OperationStatus.Deleted);
    }

    protected static T Required<T>(IServiceProvider services)
        where T : class =>
        services.GetService(typeof(T)) as T
        ?? throw new InvalidOperationException($"Required service '{typeof(T).FullName}' was not registered.");

    protected static PrincipalContext Principal => new()
    {
        AuthenticationState = PrincipalAuthenticationState.Anonymous
    };
}
