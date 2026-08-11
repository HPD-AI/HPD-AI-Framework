using FluentAssertions;
using HPD.Gateway;
using HPD.Gateway.ControlPlane;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace HPD.Gateway.Tests;

public sealed class GatewayControlPlaneCompositionTests
{
    [Fact]
    public async Task Process_local_authority_is_explicit_and_truthful()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddHpdGateway(static gateway => gateway.EnableCoreDeclarations());
        services.AddHpdGatewayControlPlane(controlPlane => controlPlane
            .UseProcessLocalAuthority(options => options.ManagementAuthorityId = "local-test"));

        await using ServiceProvider provider = services.BuildServiceProvider();
        GatewayAuthorityCapabilitySnapshot capability = await provider
            .GetRequiredService<IGatewayAuthorityRuntime>().InitializeAsync();

        capability.Durability.Should().Be(GatewayAuthorityDurability.ProcessLocal);
        provider.GetService<GatewayControlPlaneRegistration>()!.AdminOptions.Should().BeNull();
        provider.GetService<GatewayControlPlaneRegistration>()!.StudioOptions.Should().BeNull();
    }

    [Fact]
    public void Authority_selection_is_required_and_singular()
    {
        var missing = new ServiceCollection();
        Action noAuthority = () => missing.AddHpdGatewayControlPlane(static _ => { });
        noAuthority.Should().Throw<InvalidOperationException>().WithMessage("*exactly one*authority*");

        var duplicate = new ServiceCollection();
        Action twoAuthorities = () => duplicate.AddHpdGatewayControlPlane(controlPlane => controlPlane
            .UseProcessLocalAuthority()
            .UseProcessLocalAuthority());
        twoAuthorities.Should().Throw<InvalidOperationException>().WithMessage("*already configured*");
    }

    [Fact]
    public void Studio_requires_admin_and_registration_is_single()
    {
        var missingAdmin = new ServiceCollection();
        Action studioOnly = () => missingAdmin.AddHpdGatewayControlPlane(controlPlane => controlPlane
            .UseProcessLocalAuthority()
            .AddStudio());
        studioOnly.Should().Throw<InvalidOperationException>().WithMessage("*requires*Admin API*");

        var duplicate = new ServiceCollection();
        duplicate.AddHpdGatewayControlPlane(static controlPlane => controlPlane.UseProcessLocalAuthority());
        Action registerAgain = () => duplicate.AddHpdGatewayControlPlane(
            static controlPlane => controlPlane.UseProcessLocalAuthority());
        registerAgain.Should().Throw<InvalidOperationException>().WithMessage("*already registered*");
    }

    [Fact]
    public void Studio_and_admin_must_share_the_exact_governed_surface()
    {
        AssertRejected(static controlPlane => controlPlane
            .UseProcessLocalAuthority()
            .AddAdminApi(options => options.RoutePrefix = "/management/custom")
            .AddStudio(), "*ApiBasePath*RoutePrefix*");
        AssertRejected(static controlPlane => controlPlane
            .UseProcessLocalAuthority()
            .AddAdminApi(options => options.EndpointSurfaceId = "custom-admin")
            .AddStudio(), "*same endpoint surface ID*");
        AssertRejected(static controlPlane => controlPlane
            .UseProcessLocalAuthority()
            .AddAdminApi(options => options.RequireManagementListener = false)
            .AddStudio(), "*same management-listener requirement*");

        var valid = new ServiceCollection();
        valid.AddHpdGatewayControlPlane(static controlPlane => controlPlane
            .UseProcessLocalAuthority()
            .AddAdminApi(options =>
            {
                options.RoutePrefix = "/management/custom";
                options.EndpointSurfaceId = "custom-admin";
                options.RequireManagementListener = false;
            })
            .AddStudio(options =>
            {
                options.ApiBasePath = "/management/custom";
                options.EndpointSurfaceId = "custom-admin";
                options.RequireManagementListener = false;
            }));

        using ServiceProvider provider = valid.BuildServiceProvider();
        GatewayControlPlaneRegistration registration = provider.GetRequiredService<GatewayControlPlaneRegistration>();
        registration.AdminOptions!.RoutePrefix.Should().Be("/management/custom");
        registration.StudioOptions!.ApiBasePath.Should().Be("/management/custom");
    }

    [Fact]
    public void Every_failed_registration_leaves_the_caller_collection_unchanged()
    {
        Action<GatewayControlPlaneBuilder>[] failures =
        [
            static _ => { },
            static controlPlane => controlPlane.UseProcessLocalAuthority().UseProcessLocalAuthority(),
            static controlPlane => controlPlane.UseProcessLocalAuthority(options => options.MaximumTargets = 0),
            static controlPlane => controlPlane.UseProcessLocalAuthority().AddStudio(),
            static controlPlane => controlPlane
                .UseProcessLocalAuthority()
                .AddAdminApi(options => options.RoutePrefix = "/management/other")
                .AddStudio(),
            static controlPlane =>
            {
                controlPlane.UseProcessLocalAuthority().AddAdminApi().AddStudio();
                throw new InvalidOperationException("configuration failed");
            },
        ];

        foreach (Action<GatewayControlPlaneBuilder> configure in failures)
        {
            var services = new ServiceCollection();
            services.AddSingleton(new ExistingRegistrationMarker());
            ServiceDescriptor[] before = [.. services];

            Action registration = () => services.AddHpdGatewayControlPlane(configure);

            registration.Should().Throw<Exception>();
            services.Should().Equal(before);
            services.AddHpdGatewayControlPlane(static controlPlane =>
                controlPlane.UseProcessLocalAuthority());
            services.Count(static descriptor =>
                    descriptor.ServiceType == typeof(GatewayControlPlaneRegistration))
                .Should().Be(1);
        }
    }

    [Fact]
    public void Only_the_clean_control_plane_composition_surface_is_public()
    {
        Type serviceExtensions = typeof(GatewayControlPlaneServiceCollectionExtensions);
        Type endpointExtensions = typeof(GatewayControlPlaneEndpointRouteBuilderExtensions);
        serviceExtensions.GetMethods(System.Reflection.BindingFlags.Public |
                System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.DeclaredOnly)
            .Select(static method => method.Name).Should().Equal("AddHpdGatewayControlPlane");
        endpointExtensions.GetMethods(System.Reflection.BindingFlags.Public |
                System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.DeclaredOnly)
            .Select(static method => method.Name).Should().Equal("MapHpdGatewayControlPlane");

        System.Reflection.Assembly assembly = typeof(GatewayControlPlaneBuilder).Assembly;
        assembly.GetType("HPD.Gateway.Management.GatewayManagementServiceCollectionExtensions").Should().BeNull();
        assembly.GetType("HPD.Gateway.Admin.GatewayAdminServiceCollectionExtensions").Should().BeNull();
        assembly.GetType("HPD.Gateway.Studio.GatewayStudioExtensions").Should().BeNull();
        assembly.GetExportedTypes().Should().OnlyContain(static type =>
            type.Namespace == "HPD.Gateway.ControlPlane");
    }

    private static void AssertRejected(Action<GatewayControlPlaneBuilder> configure, string message)
    {
        var services = new ServiceCollection();
        ServiceDescriptor[] before = [.. services];
        Action registration = () => services.AddHpdGatewayControlPlane(configure);
        registration.Should().Throw<InvalidOperationException>().WithMessage(message);
        services.Should().Equal(before);
    }

    private sealed class ExistingRegistrationMarker;
}
