using HPD.Base.AspNetCore;
using Microsoft.Extensions.Options;

namespace HPD.Base.AspNetCore.Tests.DependencyInjection;

public sealed class AddHPDBaseAspNetCoreRuntimeBuilderTests
{
    [Fact]
    public void RuntimeBuilderExtensionReturnsSameBuilderAndAppliesOptions()
    {
        var services = new ServiceCollection();
        var builder = services.AddHPDBaseRuntime();

        var returned = builder.AddHPDBaseAspNetCore(options => options.RequestContext.IncludeIpAddress = true);

        returned.Should().BeSameAs(builder);
        using var provider = services.BuildServiceProvider();
        provider.GetRequiredService<IBaseHttpPrincipalContextFactory>().Should().NotBeNull();
        provider.GetRequiredService<IOptions<HPDBaseAspNetCoreOptions>>().Value.RequestContext.IncludeIpAddress.Should().BeTrue();
    }
}
