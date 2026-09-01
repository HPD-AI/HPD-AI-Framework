using FluentAssertions;
using HPD.Gateway;
using HPD.Gateway.ControlPlane;
using HPD.AI.Platform.Studio;
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
        provider.GetService<GatewayControlPlaneRegistration>()!.StudioConfigured.Should().BeFalse();
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
    public void Studio_uses_the_exact_installed_admin_surface_without_caller_configuration()
    {
        var valid = new ServiceCollection();
        valid.AddHpdGatewayControlPlane(static controlPlane => controlPlane
            .UseProcessLocalAuthority()
            .AddAdminApi(options =>
            {
                options.RoutePrefix = "/management/custom";
                options.EndpointSurfaceId = "custom-admin";
                options.RequireManagementListener = false;
            }).AddStudio());

        using ServiceProvider provider = valid.BuildServiceProvider();
        GatewayControlPlaneRegistration registration = provider.GetRequiredService<GatewayControlPlaneRegistration>();
        registration.AdminOptions!.RoutePrefix.Should().Be("/management/custom");
        registration.StudioConfigured.Should().BeTrue();
        IBaseStudioFrameworkEndpointSurface surface = provider
            .GetServices<IBaseStudioFrameworkEndpointSurface>().Should().ContainSingle().Subject;
        surface.EndpointSurfaceId.Should().Be("gateway.admin.v1");
        surface.Operations.Should().HaveCount(23).And.BeInAscendingOrder(static operation => operation.OperationId);
        BaseStudioSha256.FixedTimeEquals(surface.OperationInventoryChecksum,
            BaseStudioFrameworkSurfaceOperation.ComputeInventoryChecksum(surface.EndpointSurfaceId, surface.Operations)).Should().BeTrue();
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

    private sealed class ExistingRegistrationMarker;
}
