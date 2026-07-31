using HPD.Base;

namespace HPD.Base.StoreConformance.Runtime;

public abstract class RuntimeStoreEventConformanceTests<TFixture> : RuntimeStoreRegistrationConformanceTests<TFixture>
    where TFixture : IConfigurableRuntimeStoreConformanceFixture, new()
{
    [Fact]
    public async Task RuntimeSuccessfulMutationDispatchesEventReference()
    {
        if (!Capabilities.Mutation.Create)
        {
            return;
        }

        var publisher = new ConformanceCapturingEventPublisher();
        var services = await Fixture.CreateRuntimeServicesAsync(new RuntimeStoreConformanceOptions
        {
            EventPublisher = publisher
        });
        var runtime = Required<IBaseRecordRuntime>(services);

        var result = await runtime.CreateAsync(
            Collection.Id,
            new RecordCreateRequest { Payload = RecordStoreConformanceData.Payload(("title", "evented")) },
            Principal,
            Operation(BaseOperationKind.Create));

        RecordStoreConformanceAssertions.Success(result, OperationStatus.Created);
        Assert.NotNull(publisher.LastEvent);
        Assert.NotNull(result.Events);
        Assert.NotEmpty(result.Events!);
        Assert.Contains(result.Events!, reference => reference.EventId == publisher.LastEvent!.EventId);
    }
}
