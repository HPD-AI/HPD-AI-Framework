using FluentAssertions;
using HPD.Auth.ControlPlane;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Timeouts;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Text.Encodings.Web;
using Xunit;

namespace HPD.Auth.ControlPlane.AspNetCore.Tests;

public sealed class ControlPlaneRegistrationTests
{
    [Fact]
    public void Accepts_camel_case_capability_segments()
    {
        var services = new ServiceCollection();

        Action register = () => services.AddHPDControlPlane(options =>
        {
            AddProfile(options);
            options.MapCapability("base.subjectRetirement.barrier.inspect", "Readers");
        });

        register.Should().NotThrow();
    }

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
        AddAuthenticationScheme(builder.Services);
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
        AddAuthenticationScheme(builder.Services);
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

    [Fact]
    public async Task Startup_rejects_missing_authentication_scheme()
    {
        var builder = Host.CreateApplicationBuilder();
        builder.Services.AddAuthorization(options =>
            options.AddPolicy("Readers", policy => policy.RequireAuthenticatedUser()));
        builder.Services.AddHPDControlPlane(options =>
        {
            AddProfile(options);
            options.MapCapability("sample.data.read", "Readers");
        });

        using var host = builder.Build();
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => host.StartAsync());
        exception.Message.Should().Be("hpd.auth.controlPlane.scheme.missing");
    }

    [Fact]
    public async Task Startup_rejects_missing_timeout_policy()
    {
        var builder = Host.CreateApplicationBuilder();
        AddAuthenticationScheme(builder.Services);
        builder.Services.AddRequestTimeouts();
        builder.Services.AddAuthorization(options =>
            options.AddPolicy("Readers", policy => policy.RequireAuthenticatedUser()));
        builder.Services.AddHPDControlPlane(options =>
        {
            options.AddProfile("management", profile =>
            {
                profile.AuthenticationScheme = "HPD";
                profile.AuthenticationProfile = "hpd";
                profile.ActorIdentifierClaim = "subject";
                profile.RequestTimeoutPolicy = "MissingTimeout";
            });
            options.MapCapability("sample.data.read", "Readers");
        });

        using var host = builder.Build();
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => host.StartAsync());
        exception.Message.Should().Be("hpd.auth.controlPlane.timeoutPolicy.missing");
    }

    [Fact]
    public async Task Strict_OpenApi_rejects_missing_integration()
    {
        var builder = Host.CreateApplicationBuilder();
        AddAuthenticationScheme(builder.Services);
        builder.Services.AddAuthorization(options =>
            options.AddPolicy("Readers", policy => policy.RequireAuthenticatedUser()));
        builder.Services.AddHPDControlPlane(options =>
        {
            options.StrictOpenApiValidation = true;
            options.AddProfile("management", profile =>
            {
                profile.AuthenticationScheme = "HPD";
                profile.AuthenticationProfile = "hpd";
                profile.ActorIdentifierClaim = "subject";
                profile.OpenApiSecurityScheme = "Bearer";
            });
            options.MapCapability("sample.data.read", "Readers");
        });

        using var host = builder.Build();
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => host.StartAsync());
        exception.Message.Should().Be("hpd.auth.controlPlane.profile.invalid");
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public async Task Startup_rejects_non_exact_endpoint_metadata(bool duplicateProfiles, bool duplicateCapabilities)
    {
        var builder = Host.CreateApplicationBuilder();
        AddAuthenticationScheme(builder.Services);
        builder.Services.AddAuthorization(options =>
            options.AddPolicy("Readers", policy => policy.RequireAuthenticatedUser()));
        builder.Services.AddHPDControlPlane(options =>
        {
            AddProfile(options);
            options.MapCapability("sample.data.read", "Readers");
        });
        var profilePolicy = new AuthorizationPolicyBuilder("HPD").RequireAuthenticatedUser().Build();
        var metadata = new List<object>
        {
            new ControlPlaneEndpointMetadata("management"),
            new ControlPlaneCapabilityMetadata("sample.data.read"),
            profilePolicy,
            new AuthorizeAttribute("Readers")
        };
        if (duplicateProfiles)
            metadata.Add(new ControlPlaneEndpointMetadata("management"));
        if (duplicateCapabilities)
            metadata.Add(new ControlPlaneCapabilityMetadata("sample.data.read"));
        builder.Services.AddSingleton<EndpointDataSource>(
            new DefaultEndpointDataSource(new Endpoint(null, new EndpointMetadataCollection(metadata), "test")));

        using var host = builder.Build();
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => host.StartAsync());
        exception.Message.Should().Be(duplicateProfiles
            ? "hpd.auth.controlPlane.profile.invalid"
            : "hpd.auth.controlPlane.capability.duplicate");
    }

    private static void AddProfile(HPDControlPlaneOptions options) =>
        options.AddProfile("management", profile =>
        {
            profile.AuthenticationScheme = "HPD";
            profile.AuthenticationProfile = "hpd";
            profile.ActorIdentifierClaim = "subject";
        });

    private static void AddAuthenticationScheme(IServiceCollection services) =>
        services.AddAuthentication().AddScheme<AuthenticationSchemeOptions, TestHandler>("HPD", static _ => { });

    private sealed class TestHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder) : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        protected override Task<AuthenticateResult> HandleAuthenticateAsync() =>
            Task.FromResult(AuthenticateResult.NoResult());
    }
}
