using HPD.Base;

namespace HPD.Base.StoreConformance.Runtime;

public abstract class RuntimeStoreCapabilityGateConformanceTests<TFixture> : RuntimeStoreRegistrationConformanceTests<TFixture>
    where TFixture : IRuntimeStoreConformanceFixture, new()
{
    [Fact]
    public async Task RuntimeRejectsClientIdsWhenStoreAuthorityDoesNotAllowThem()
    {
        if (!Capabilities.Mutation.Create || Capabilities.Mutation.IdAuthority is IdAuthority.Client or IdAuthority.Hybrid)
        {
            return;
        }

        var services = await Fixture.CreateRuntimeServicesAsync();
        var runtime = Required<IBaseRecordRuntime>(services);
        var result = await runtime.CreateAsync(
            Collection.Id,
            new RecordCreateRequest
            {
                RequestedId = new RecordId("client-id"),
                Payload = RecordStoreConformanceData.Payload(("title", "client"))
            },
            Principal,
            Operation(BaseOperationKind.Create, new RecordId("client-id")));

        RecordStoreConformanceAssertions.Failure(result, OperationStatus.Unsupported, OperationStatus.CapabilityUnavailable, OperationStatus.ValidationFailed);
    }
}
