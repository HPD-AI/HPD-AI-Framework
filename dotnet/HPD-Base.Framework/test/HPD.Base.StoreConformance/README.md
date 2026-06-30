# HPD.BASE Store Conformance

`HPD.Base.StoreConformance` is the reusable test-support assembly for `IRecordStore` providers. It is intentionally provider-neutral: fixtures seed and observe data only through BASE store/runtime contracts, not SQL, DDL, migrations, joins, transactions, native predicates, or provider-specific setup.

## What Providers Implement

Create a fixture in the provider test project:

```csharp
public sealed class MyStoreConformanceFixture : IRecordStoreConformanceFixture
{
    public string ProviderName => "My.Store";

    public StoreCapabilityDescriptor Capabilities => new MyRecordStore().Capabilities;

    public CollectionDefinition Collection => new()
    {
        Id = "conformance-items",
        Name = "conformance-items",
        Kind = BaseCollectionKinds.Document,
        SchemaMode = SchemaMode.Loose,
        UnknownFields = UnknownFieldPolicy.Preserve
    };

    public OperationContext Operation(BaseOperationKind operation, RecordId? id = null) => new()
    {
        Operation = operation,
        CollectionId = Collection.Id,
        RecordId = id?.Value,
        Now = DateTimeOffset.UnixEpoch
    };

    public ValueTask<IRecordStore> CreateStoreAsync(CancellationToken cancellationToken = default) =>
        ValueTask.FromResult<IRecordStore>(new MyRecordStore());

    public ValueTask ResetAsync(CancellationToken cancellationToken = default) =>
        ValueTask.CompletedTask;
}
```

If a provider needs custom seeding, implement `IRecordStoreConformanceSeeder`. Seeding must still use BASE record operations and `CollectionDefinition`; it should not require native tables, migrations, or provider query languages.

## Direct Store Suites

Derive concrete xUnit classes in the provider test project:

```csharp
public sealed class MyCrudConformanceTests
    : RecordStoreCrudConformanceTests<MyStoreConformanceFixture>
{
}

public sealed class MyQueryConformanceTests
    : RecordStoreQueryConformanceTests<MyStoreConformanceFixture>
{
}
```

Available direct suites:

- `RecordStoreDescriptorConformanceTests<TFixture>`
- `RecordStoreCrudConformanceTests<TFixture>`
- `RecordStoreCrudUnsupportedConformanceTests<TFixture>`
- `RecordStorePatchReplaceConformanceTests<TFixture>`
- `RecordStoreRevisionConformanceTests<TFixture>`
- `RecordStoreCopyIsolationConformanceTests<TFixture>`
- `RecordStoreQueryConformanceTests<TFixture>`
- `RecordStoreStreamingConformanceTests<TFixture>`

Direct conformance uses `StoreCapabilityDescriptor` as the source of truth. If a capability says a behavior is supported, the suite proves it works. If it says unsupported, the suite proves the provider fails closed with normalized results.

## Streaming Expectations

`StreamingCapability` currently advertises whether stream opens are supported, but it does not fully describe snapshot/live/replay behavior. Providers that intentionally promise stronger stream behavior can opt into extra checks:

```csharp
public sealed class MyStoreConformanceFixture :
    IRecordStoreConformanceFixture,
    IStreamingRecordStoreConformanceExpectations
{
    public bool ExpectsSnapshotStreams => true;
    public bool ExpectsEnumerationCancellation => true;
}
```

Only opt in when the provider treats those semantics as part of its contract.

## Runtime Integration Suites

Runtime conformance is separate from direct store conformance. Use it when the provider registers with `IBaseRecordRuntime`, contributes descriptors/capabilities, or needs runtime composition proof.

Implement `IRuntimeStoreConformanceFixture`:

```csharp
public sealed class MyStoreConformanceFixture : IRuntimeStoreConformanceFixture
{
    public ValueTask<IServiceProvider> CreateRuntimeServicesAsync(
        CancellationToken cancellationToken = default)
    {
        var services = new ServiceCollection();
        services.AddSingleton<IPolicyEvaluator, ConformanceAllowPolicyEvaluator>();
        services.AddHPDBaseRuntime()
            .AddMyStore(...);

        var provider = services.BuildServiceProvider();
        provider.GetRequiredService<IRecordStoreRegistry>().AddMyStore(provider);
        provider.GetRequiredService<IBaseDescriptorRegistry>()
            .RebuildAsync(cancellationToken)
            .AsTask()
            .GetAwaiter()
            .GetResult();

        return ValueTask.FromResult<IServiceProvider>(provider);
    }
}
```

Available runtime suites:

- `RuntimeStoreRegistrationConformanceTests<TFixture>`
- `RuntimeStoreCapabilityGateConformanceTests<TFixture>`
- `RuntimeStoreQueryConformanceTests<TFixture>`
- `RuntimeStoreDescriptorHonestyConformanceTests<TFixture>`

For policy, event dispatch, and exception-normalization conformance, implement `IConfigurableRuntimeStoreConformanceFixture`. The fixture must rebuild the same runtime with optional overrides from `RuntimeStoreConformanceOptions`:

- `PolicyEvaluator`
- `EventPublisher`
- `StoreOverride`

Then derive:

- `RuntimeStorePolicyConformanceTests<TFixture>`
- `RuntimeStoreResultNormalizationConformanceTests<TFixture>`
- `RuntimeStoreEventConformanceTests<TFixture>`

## Provider-Local Tests

Keep provider-specific behavior local to the provider test project. Examples:

- options defaults and validation;
- dependency injection registration details;
- exact provider error codes beyond broad status/category;
- health and diagnostic contributor details;
- provider-specific durability, indexing, storage, or performance behavior;
- AOT smoke tests.

The conformance assembly proves portable BASE behavior. Provider-local tests prove provider personality.

## Verification

At minimum, run the provider tests plus the conformance support build:

```bash
dotnet build test/HPD.Base.StoreConformance/HPD.Base.StoreConformance.csproj -c Debug -f net10.0
dotnet test test/<provider-tests>/<provider-tests>.csproj -c Debug -f net10.0
```

When touching shared BASE contracts, also run the BASE gates:

```bash
dotnet test ../shared/test/HPD-Events.Tests/HPD-Events.Tests.csproj -c Debug -f net10.0
dotnet test test/HPD.Base.Abstractions.Tests/HPD.Base.Abstractions.Tests.csproj -c Debug -f net10.0
dotnet test test/HPD.Base.Runtime.Tests/HPD.Base.Runtime.Tests.csproj -c Debug -f net10.0
dotnet test HPD-Base.slnx -c Debug -f net10.0 --no-restore
```
