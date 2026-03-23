using FluentAssertions;
using HPD.Auth.Extensions;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace HPD.Auth.Tests;

/// <summary>
/// Verifies that public API methods throw ArgumentNullException for null arguments.
/// </summary>
public class GuardClauseTests
{
    // ── AddHPDAuth ────────────────────────────────────────────────────────────────

    [Fact]
    public void AddHPDAuth_Throws_When_Services_Is_Null()
    {
        IServiceCollection services = null!;

        var act = () => services.AddHPDAuth(o => o.AppName = "Guard");

        act.Should().Throw<ArgumentNullException>()
            .WithParameterName("services");
    }

    [Fact]
    public void AddHPDAuth_Throws_When_Configure_Is_Null()
    {
        var services = new ServiceCollection();

        var act = () => services.AddHPDAuth(null!);

        act.Should().Throw<ArgumentNullException>()
            .WithParameterName("configure");
    }

    // ── UseHPDAuth ────────────────────────────────────────────────────────────────

    [Fact]
    public void UseHPDAuth_Throws_When_App_Is_Null()
    {
        IApplicationBuilder app = null!;

        var act = () => app.UseHPDAuth();

        act.Should().Throw<ArgumentNullException>()
            .WithParameterName("app");
    }
}
