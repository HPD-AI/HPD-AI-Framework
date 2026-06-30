using HPD.Base.AspNetCore.Configuration;
using HPD.Base.AspNetCore.Http;
using HPD.Base.AspNetCore.QueryBinding;
using HPD.Base.AspNetCore.Results;
using HPD.Base.Runtime.DependencyInjection;
using Microsoft.Extensions.Options;

namespace HPD.Base.AspNetCore.Tests.DependencyInjection;

public sealed class AddHPDBaseAspNetCoreTests
{
    [Fact]
    public void RegistersProjectionServicesWithoutStoreDependency()
    {
        var services = new ServiceCollection();
        services.AddHPDBaseRuntime();
        services.AddHPDBaseAspNetCore(options => options.Limits.MaxRouteIdLength = 42);

        using var provider = services.BuildServiceProvider();

        provider.GetRequiredService<IBaseHttpPrincipalContextFactory>().Should().NotBeNull();
        provider.GetRequiredService<IBaseHttpOperationContextFactory>().Should().NotBeNull();
        provider.GetRequiredService<IBaseHttpResultMapper>().Should().NotBeNull();
        provider.GetRequiredService<IBaseHttpQueryBinder>().Should().NotBeNull();
        provider.GetRequiredService<IOptions<HPDBaseAspNetCoreOptions>>().Value.Limits.MaxRouteIdLength.Should().Be(42);
        provider.GetService<IRecordStoreRegistry>().Should().NotBeNull();
    }
}
