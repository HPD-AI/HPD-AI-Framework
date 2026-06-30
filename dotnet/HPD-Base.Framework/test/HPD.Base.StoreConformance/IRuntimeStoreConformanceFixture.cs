namespace HPD.Base.StoreConformance;

/// <summary>
/// Optional runtime integration fixture. Direct conformance does not depend on it.
/// </summary>
public interface IRuntimeStoreConformanceFixture : IRecordStoreConformanceFixture
{
    ValueTask<IServiceProvider> CreateRuntimeServicesAsync(CancellationToken cancellationToken = default);
}

public interface IConfigurableRuntimeStoreConformanceFixture : IRuntimeStoreConformanceFixture
{
    ValueTask<IServiceProvider> CreateRuntimeServicesAsync(
        RuntimeStoreConformanceOptions options,
        CancellationToken cancellationToken = default);
}

public sealed record RuntimeStoreConformanceOptions
{
    public IPolicyEvaluator? PolicyEvaluator { get; init; }
    public IBaseEventPublisher? EventPublisher { get; init; }
    public IRecordStore? StoreOverride { get; init; }
}
