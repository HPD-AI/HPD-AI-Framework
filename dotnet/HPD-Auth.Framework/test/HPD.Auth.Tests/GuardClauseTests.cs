using FluentAssertions;
using HPD.Auth.Extensions;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
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

    [Fact]
    public async Task AddHPDAuth_Without_Storage_Fails_Host_Startup()
    {
        using var host = Host.CreateDefaultBuilder()
            .ConfigureServices(services =>
            {
                services.AddHttpContextAccessor();
                services.AddHPDAuth(o => o.AppName = "MissingStorage");
            })
            .Build();

        var act = () => host.StartAsync();

        await act.Should().ThrowAsync<OptionsValidationException>()
            .WithMessage("*HPD.Auth storage is required*");
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
