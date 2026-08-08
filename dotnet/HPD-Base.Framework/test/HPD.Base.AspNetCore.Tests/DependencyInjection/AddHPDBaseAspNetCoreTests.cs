using HPD.Base.AspNetCore;
using HPD.Base;

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
        provider.GetRequiredService<HPDBaseAspNetCoreSnapshot>().Limits.MaxRouteIdLength.Should().Be(42);
        provider.GetService<IRecordStoreRegistry>().Should().NotBeNull();
    }

    [Fact]
    public void CallbackOwnedOptionsCannotMutateTheRuntimeSnapshot()
    {
        HPDBaseAspNetCoreOptions? retained = null;
        var services = new ServiceCollection();
        services.AddHPDBaseAspNetCore(options =>
        {
            retained = options;
            options.Auth.CopiedClaimTypes = ["safe"];
            options.Auth.MaxClaims = 1;
        });

        retained!.Auth.CopiedClaimTypes = ["refresh_token"];
        retained.Auth.MaxClaims = 64;
        using var provider = services.BuildServiceProvider();
        HPDBaseAspNetCoreSnapshot snapshot = provider.GetRequiredService<HPDBaseAspNetCoreSnapshot>();

        snapshot.Auth.CopiedClaimTypes.Should().Equal("safe");
        snapshot.Auth.MaxClaims.Should().Be(1);
    }

    [Fact]
    public void CompetingClosedOptionsAuthorityFailsRegistration()
    {
        var services = new ServiceCollection();
        services.AddSingleton<Microsoft.Extensions.Options.IOptions<HPDBaseAspNetCoreOptions>>(
            Microsoft.Extensions.Options.Options.Create(new HPDBaseAspNetCoreOptions()));

        Action action = () => services.AddHPDBaseAspNetCore();

        action.Should().Throw<InvalidOperationException>().WithMessage("base.http.options.ambiguous");
    }
}
