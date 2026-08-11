using FluentAssertions;
using HPD.Gateway.ControlPlane;
using HPD.Gateway.ControlPlane.HPDAuth;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace HPD.Gateway.Tests;

public sealed class GatewayControlPlaneHpdAuthTests
{
    [Fact]
    public void Public_surface_is_exact_and_provider_specific()
    {
        typeof(GatewayHpdAuthAdminServiceCollectionExtensions).Assembly.GetExportedTypes()
            .Should().BeEquivalentTo([
                typeof(GatewayHpdAuthAdminServiceCollectionExtensions),
                typeof(GatewayManagementActorProjection),
            ]);
        typeof(GatewayHpdAuthAdminServiceCollectionExtensions).Namespace
            .Should().Be("HPD.Gateway.ControlPlane.HPDAuth");
    }

    [Fact]
    public void Adapter_requires_admin_and_registration_is_transactional()
    {
        var services = new ServiceCollection();
        services.AddSingleton(new ExistingMarker());
        ServiceDescriptor[] before = [.. services];

        Action missingAdmin = () => services.AddHpdGatewayControlPlane(controlPlane => controlPlane
            .UseProcessLocalAuthority()
            .AddHpdAuth("gateway-admin"));

        missingAdmin.Should().Throw<InvalidOperationException>().WithMessage("*Admin API before*");
        services.Should().Equal(before);

        services.AddHpdGatewayControlPlane(controlPlane => controlPlane
            .UseProcessLocalAuthority()
            .AddAdminApi()
            .AddHpdAuth("gateway-admin"));
        services.Count(static descriptor => descriptor.ServiceType == typeof(GatewayControlPlaneRegistration))
            .Should().Be(1);
    }

    [Fact]
    public void Duplicate_or_invalid_adapter_configuration_leaves_services_unchanged()
    {
        Action<GatewayControlPlaneBuilder>[] invalid =
        [
            static controlPlane => controlPlane
                .UseProcessLocalAuthority()
                .AddAdminApi()
                .AddHpdAuth("gateway-admin")
                .AddHpdAuth("gateway-admin"),
            static controlPlane => controlPlane
                .UseProcessLocalAuthority()
                .AddAdminApi()
                .AddHpdAuth(" "),
        ];

        foreach (Action<GatewayControlPlaneBuilder> configure in invalid)
        {
            var services = new ServiceCollection();
            services.AddSingleton(new ExistingMarker());
            ServiceDescriptor[] before = [.. services];
            Action registration = () => services.AddHpdGatewayControlPlane(configure);

            registration.Should().Throw<Exception>();
            services.Should().Equal(before);
        }
    }

    private sealed class ExistingMarker;
}
