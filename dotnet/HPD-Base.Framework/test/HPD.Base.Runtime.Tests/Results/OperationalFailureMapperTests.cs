using HPD.Base.Results;
using HPD.Base.Runtime.DependencyInjection;
using HPD.Base.Runtime.Results;
using Microsoft.Extensions.DependencyInjection;

namespace HPD.Base.Runtime.Tests.Results;

public sealed class OperationalFailureMapperTests
{
    [Fact]
    public void TimeoutMapsToRetryableStoreError()
    {
        using var provider = Provider();

        var mapped = provider.GetRequiredService<IBaseOperationalFailureMapper>().TryMap(
            new TimeoutException("timeout"),
            RuntimeTestData.Operation(BaseOperationKind.Get),
            out var error,
            out var status);

        Assert.True(mapped);
        Assert.Equal(OperationStatus.StoreError, status);
        Assert.Equal("base.runtime.store.dependencyFailure", error.Code);
        Assert.True(error.Store!.Retryable);
    }

    [Fact]
    public void UnexpectedExceptionIsNotMappedByDefault()
    {
        using var provider = Provider();

        var mapped = provider.GetRequiredService<IBaseOperationalFailureMapper>().TryMap(
            new InvalidOperationException("bug"),
            RuntimeTestData.Operation(BaseOperationKind.Get),
            out _,
            out _);

        Assert.False(mapped);
    }

    private static ServiceProvider Provider()
    {
        var services = new ServiceCollection();
        services.AddHPDBaseRuntime();
        return services.BuildServiceProvider();
    }
}
