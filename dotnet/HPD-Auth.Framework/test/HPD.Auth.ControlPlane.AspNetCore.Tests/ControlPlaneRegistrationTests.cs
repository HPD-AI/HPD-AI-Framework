using FluentAssertions;
using HPD.Auth.ControlPlane;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace HPD.Auth.ControlPlane.AspNetCore.Tests;

public sealed class ControlPlaneRegistrationTests
{
    [Fact]
    public void Registers_without_changing_authentication_defaults()
    {
        var services = new ServiceCollection();
        services.AddAuthorization(options =>
            options.AddPolicy("Readers", policy => policy.RequireAuthenticatedUser()));

        services.AddHPDControlPlane(options =>
        {
            AddProfile(options);
            options.MapCapability("sample.data.read", "Readers");
        });

        services.Should().NotContain(descriptor =>
            descriptor.ServiceType.FullName == "Microsoft.AspNetCore.Authentication.IAuthenticationSchemeProvider");
    }

    [Fact]
    public void Rejects_duplicate_capability_mapping()
    {
        var action = () => new ServiceCollection().AddHPDControlPlane(options =>
        {
            AddProfile(options);
            options.MapCapability("sample.data.read", "Readers");
            options.MapCapability("sample.data.read", "OtherReaders");
        });

        action.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public async Task Startup_rejects_policy_that_selects_a_scheme()
    {
        var builder = Host.CreateApplicationBuilder();
        builder.Services.AddAuthorization(options =>
            options.AddPolicy("Readers", policy =>
            {
                policy.AuthenticationSchemes.Add("OtherScheme");
                policy.RequireAuthenticatedUser();
            }));
        builder.Services.AddHPDControlPlane(options =>
        {
            AddProfile(options);
            options.MapCapability("sample.data.read", "Readers");
        });

        using var host = builder.Build();
        var action = () => host.StartAsync();

        await action.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task Startup_rejects_missing_mapped_policy()
    {
        var builder = Host.CreateApplicationBuilder();
        builder.Services.AddAuthorization();
        builder.Services.AddHPDControlPlane(options =>
        {
            AddProfile(options);
            options.MapCapability("sample.data.read", "MissingReaders");
        });

        using var host = builder.Build();
        var action = () => host.StartAsync();

        await action.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public void Rejects_one_claim_type_for_multiple_facts()
    {
        var action = () => new ServiceCollection().AddHPDControlPlane(options =>
            options.AddProfile("management", profile =>
            {
                profile.AuthenticationScheme = "HPD";
                profile.AuthenticationProfile = "hpd";
                profile.ActorIdentifierClaim = "subject";
                profile.TenantClaim = "subject";
            }));

        action.Should().Throw<InvalidOperationException>();
    }

    private static void AddProfile(HPDControlPlaneOptions options) =>
        options.AddProfile("management", profile =>
        {
            profile.AuthenticationScheme = "HPD";
            profile.AuthenticationProfile = "hpd";
            profile.ActorIdentifierClaim = "subject";
        });
}
