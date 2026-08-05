using System.Security.Claims;
using FluentAssertions;
using HPD.Auth.ControlPlane;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace HPD.Auth.ControlPlane.AspNetCore.Tests;

public sealed class AuthenticatedActorProjectorTests
{
    [Fact]
    public async Task Projects_only_configured_bounded_facts()
    {
        var projector = CreateProjector();
        var context = Context(
            new Claim("subject", "actor-1"),
            new Claim("tenant", "tenant-1"),
            new Claim("unconfigured-secret", "must-not-project"));

        var result = await projector.ProjectAsync(context, "management");

        result.ActorId.Should().Be("actor-1");
        result.TenantId.Should().Be("tenant-1");
        result.AuthenticationProfile.Should().Be("hpd");
        result.AuthenticationMethod.Should().BeNull();
        result.GetType().GetProperties().Should().NotContain(property =>
            property.PropertyType == typeof(ClaimsPrincipal));
    }

    [Fact]
    public async Task Collapses_identical_claim_values()
    {
        var projector = CreateProjector();
        var context = Context(new Claim("subject", "actor-1"), new Claim("subject", "actor-1"));

        var result = await projector.ProjectAsync(context, "management");

        result.ActorId.Should().Be("actor-1");
    }

    [Fact]
    public async Task Rejects_conflicting_identifier_values()
    {
        var projector = CreateProjector();
        var context = Context(new Claim("subject", "actor-1"), new Claim("subject", "actor-2"));

        var action = () => projector.ProjectAsync(context, "management").AsTask();

        var exception = await action.Should().ThrowAsync<AuthenticatedActorProjectionException>();
        exception.Which.Code.Should().Be("hpd.auth.actor.identifierAmbiguous");
    }

    [Fact]
    public async Task Rejects_missing_endpoint_profile_metadata()
    {
        var projector = CreateProjector();
        var context = Context(new Claim("subject", "actor-1"));
        context.SetEndpoint(null);

        var action = () => projector.ProjectAsync(context, "management").AsTask();

        var exception = await action.Should().ThrowAsync<AuthenticatedActorProjectionException>();
        exception.Which.Code.Should().Be("hpd.auth.actor.profileMissing");
    }

    [Fact]
    public async Task Rejects_control_characters_without_exposing_the_value()
    {
        var projector = CreateProjector();
        var context = Context(new Claim("subject", "secret\nactor"));

        var action = () => projector.ProjectAsync(context, "management").AsTask();

        var exception = await action.Should().ThrowAsync<AuthenticatedActorProjectionException>();
        exception.Which.Code.Should().Be("hpd.auth.actor.factInvalid");
        exception.Which.Message.Should().NotContain("secret");
    }

    private static IAuthenticatedActorProjector CreateProjector()
    {
        var services = new ServiceCollection();
        services.AddAuthorization(options =>
            options.AddPolicy("Readers", policy => policy.RequireAuthenticatedUser()));
        services.AddHPDControlPlane(options =>
        {
            options.AddProfile("management", profile =>
            {
                profile.AuthenticationScheme = "HPD";
                profile.AuthenticationProfile = "hpd";
                profile.ActorIdentifierClaim = "subject";
                profile.TenantClaim = "tenant";
            });
            options.MapCapability("sample.data.read", "Readers");
        });
        return services.BuildServiceProvider().GetRequiredService<IAuthenticatedActorProjector>();
    }

    private static DefaultHttpContext Context(params Claim[] claims)
    {
        var context = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity(claims, "HPD"))
        };
        context.SetEndpoint(new Endpoint(
            null,
            new EndpointMetadataCollection(new ControlPlaneEndpointMetadata("management")),
            "test"));
        return context;
    }
}
