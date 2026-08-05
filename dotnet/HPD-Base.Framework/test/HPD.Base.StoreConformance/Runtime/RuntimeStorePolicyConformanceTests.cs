using HPD.Base;

namespace HPD.Base.StoreConformance.Runtime;

public abstract class RuntimeStorePolicyConformanceTests<TFixture> : RuntimeStoreRegistrationConformanceTests<TFixture>
    where TFixture : IConfigurableRuntimeStoreConformanceFixture, new()
{
    [Fact]
    public async Task RuntimePolicyDeniedWriteFailsBeforeMutation()
    {
        if (!Capabilities.Mutation.Create)
        {
            return;
        }

        var services = await Fixture.CreateRuntimeServicesAsync(new RuntimeStoreConformanceOptions
        {
            PolicyEvaluator = new ConformanceDenyPolicyEvaluator()
        });
        var runtime = Required<IBaseRecordRuntime>(services);

        var result = await runtime.CreateAsync(
            Collection.Id,
            new RecordCreateRequest { Payload = RecordStoreConformanceData.Payload(("title", "denied")) },
            Principal,
            Operation(BaseOperationKind.Create));

        RecordStoreConformanceAssertions.Failure(result, OperationStatus.PolicyDenied);
    }

    [Fact]
    public async Task RuntimePublicGetMapsCandidatePolicyDenialToNotFound()
    {
        if (!Capabilities.Mutation.Create || !Capabilities.Read.Get)
        {
            return;
        }

        var store = await CreateStoreAsync();
        var record = await CreateRecordAsync(store, "policy-cloaked", ("title", "hidden"));
        var services = await Fixture.CreateRuntimeServicesAsync(new RuntimeStoreConformanceOptions
        {
            PolicyEvaluator = new ConformanceDenyExistingRecordPolicyEvaluator(),
            StoreOverride = store
        });
        var runtime = Required<IBaseRecordRuntime>(services);

        var result = await runtime.GetAsync(
            Collection.Id,
            record.Id,
            Principal,
            Operation(BaseOperationKind.Get, record.Id));

        RecordStoreConformanceAssertions.Failure(result, OperationStatus.NotFound);
    }
}
