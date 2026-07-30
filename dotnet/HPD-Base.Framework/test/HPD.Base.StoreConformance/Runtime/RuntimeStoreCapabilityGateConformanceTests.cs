using HPD.Base.Runtime.Operations;

namespace HPD.Base.StoreConformance.Runtime;

public abstract class RuntimeStoreCapabilityGateConformanceTests<TFixture> : RuntimeStoreRegistrationConformanceTests<TFixture>
    where TFixture : IRuntimeStoreConformanceFixture, new()
{
    [Fact]
    public async Task RuntimeRejectsIdempotencyKeysBeforeStoreMutation()
    {
        if (!Capabilities.Mutation.Create)
        {
            return;
        }

        var services = await Fixture.CreateRuntimeServicesAsync();
        var runtime = Required<IBaseRecordRuntime>(services);
        var result = await runtime.CreateAsync(
            Collection.Id,
            new RecordCreateRequest
            {
                IdempotencyKey = "same-request",
                Payload = RecordStoreConformanceData.Payload(("title", "one"))
            },
            Principal,
            Operation(BaseOperationKind.Create));

        RecordStoreConformanceAssertions.Failure(result, OperationStatus.Unsupported, OperationStatus.CapabilityUnavailable, OperationStatus.ValidationFailed);

        if (Capabilities.Read.List)
        {
            var list = await runtime.ListAsync(Collection.Id, RecordStoreConformanceQueries.Empty, Principal, Operation(BaseOperationKind.List));
            RecordStoreConformanceAssertions.Success(list, OperationStatus.Ok);
            Assert.DoesNotContain(list.Value!.Items, item =>
                item.Payload.Fields?.TryGetValue("title", out var title) == true && title.GetString() == "one");
        }
    }

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
