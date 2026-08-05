using System.Text.Json;
using FluentAssertions;
using HPD.Gateway.Abstractions;
using HPD.Gateway.Abstractions.Serialization;
using Xunit;

namespace HPD.Gateway.Tests;

public sealed class GatewayConfigurationTests
{
    [Theory]
    [InlineData("route-1")]
    [InlineData("tenant.api_v2")]
    [InlineData("0")]
    public void CanonicalIdentifiersAreAccepted(string value) =>
        GatewayIdentifier.IsCanonical(value).Should().BeTrue();

    [Theory]
    [InlineData("")]
    [InlineData("Route-1")]
    [InlineData("-route")]
    [InlineData("route/1")]
    [InlineData("routé")]
    public void NoncanonicalIdentifiersAreRejected(string value) =>
        GatewayIdentifier.IsCanonical(value).Should().BeFalse();

    [Fact]
    public void ValidConfigurationRoundTripsThroughGeneratedJson()
    {
        var configuration = CreateValidConfiguration();

        var json = JsonSerializer.Serialize(configuration, GatewayJsonSerializerContext.Default.GatewayConfiguration);
        var roundTripped = JsonSerializer.Deserialize(json, GatewayJsonSerializerContext.Default.GatewayConfiguration);

        roundTripped.Should().BeEquivalentTo(configuration);
        GatewayConfigurationValidator.Validate(roundTripped!).IsValid.Should().BeTrue();
    }

    [Fact]
    public void MissingUpstreamAndAmbiguousDeclarationAreRejected()
    {
        var configuration = CreateValidConfiguration() with
        {
            Routes =
            [
                CreateValidConfiguration().Routes[0] with
                {
                    Upstream = new UpstreamId("missing"),
                    Declarations = new RouteDeclarations
                    {
                        Authorization = new DeclarationReference<NamedAuthorizationPolicy>
                        {
                            Inline = new NamedAuthorizationPolicy("api"),
                            Definition = new DefinitionId("shared")
                        }
                    }
                }
            ]
        };

        var result = GatewayConfigurationValidator.Validate(configuration);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(error => error.Code == GatewayValidationErrorCode.UnresolvedReference);
        result.Errors.Should().Contain(error => error.Code == GatewayValidationErrorCode.InvalidDeclarationReference);
    }

    [Fact]
    public void MissingFamilyDefinitionIsRejected()
    {
        var valid = CreateValidConfiguration();
        var configuration = valid with
        {
            Routes =
            [
                valid.Routes[0] with
                {
                    Declarations = new RouteDeclarations
                    {
                        Cors = new DeclarationReference<CorsPolicyBinding>
                        {
                            Definition = new DefinitionId("shared-cors")
                        }
                    }
                }
            ]
        };

        var result = GatewayConfigurationValidator.Validate(configuration);

        result.Errors.Should().ContainSingle(error =>
            error.Code == GatewayValidationErrorCode.UnresolvedReference &&
            error.Path.EndsWith("cors.definition", StringComparison.Ordinal));
    }

    internal static GatewayConfiguration CreateValidConfiguration() => new()
    {
        SchemaVersion = new GatewaySchemaVersion(1, 0),
        CanonicalizationVersion = 1,
        Upstreams =
        [
            new UpstreamDeclaration
            {
                Id = new UpstreamId("orders"),
                Endpoints = new StaticEndpointSource
                {
                    Destinations =
                    [
                        new DestinationDeclaration
                        {
                            Id = new DestinationId("primary"),
                            Address = new Uri("https://orders.internal")
                        }
                    ]
                }
            }
        ],
        Routes =
        [
            new RouteDeclaration
            {
                Id = new RouteId("orders-api"),
                Match = new HttpRouteMatch { Path = "/orders/{**catch-all}" },
                Upstream = new UpstreamId("orders"),
                Declarations = new RouteDeclarations
                {
                    Authorization = new DeclarationReference<NamedAuthorizationPolicy>
                    {
                        Inline = new NamedAuthorizationPolicy("orders.read")
                    }
                }
            }
        ]
    };
}
