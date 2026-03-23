using FluentAssertions;
using HPD.Auth.Builder;
using HPD.Auth.Extensions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace HPD.Auth.Tests;

/// <summary>
/// Verifies the IHPDAuthBuilder contract (tests 10.1 – 10.3).
/// </summary>
public class BuilderContractTests
{
    // ── 10.1 ─────────────────────────────────────────────────────────────────────

    [Fact]
    public void IHPDAuthBuilder_Services_Is_Same_ServiceCollection()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddHttpContextAccessor();

        var builder = services.AddHPDAuth(o => o.AppName = "Builder_Services");

        builder.Services.Should().BeSameAs(services);
    }

    // ── 10.2 ─────────────────────────────────────────────────────────────────────

    [Fact]
    public void IHPDAuthBuilder_Options_Reflects_Configured_Values()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddHttpContextAccessor();

        var builder = services.AddHPDAuth(o =>
        {
            o.AppName = "MyBuildTest";
            o.Password.RequiredLength = 16;
        });

        builder.Options.AppName.Should().Be("MyBuildTest");
        builder.Options.Password.RequiredLength.Should().Be(16);
    }

    // ── 10.3 ─────────────────────────────────────────────────────────────────────

    [Fact]
    public void AddHPDAuth_Returns_Non_Null_Builder()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddHttpContextAccessor();

        var builder = services.AddHPDAuth(o => o.AppName = "Builder_NotNull");

        builder.Should().NotBeNull();
        builder.Should().BeAssignableTo<IHPDAuthBuilder>();
    }
}
