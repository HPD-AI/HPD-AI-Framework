using System.Collections.Immutable;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using HPD.Gateway.Abstractions;
using HPD.Gateway.Abstractions.Serialization;
using Xunit;

namespace HPD.Gateway.Tests;

public sealed class ContractHardeningTests
{
    [Fact]
    public void RootDeclarationsExposeOnlyRootLegalFamilies()
    {
        var properties = typeof(GatewayRootDeclarations).GetProperties()
            .Select(static property => property.Name)
            .ToArray();

        properties.Should().NotContain(nameof(RouteDeclarations.RequestTransforms));
        properties.Should().NotContain(nameof(RouteDeclarations.ResponseTransforms));
    }

    [Fact]
    public void ParserRejectsUnknownMembers()
    {
        var json = SerializeValid();
        json = json[..^1] + ",\"unknownMember\":true}";

        var result = GatewayConfigurationParser.Parse(Encoding.UTF8.GetBytes(json));

        result.IsParsed.Should().BeFalse();
    }

    [Fact]
    public void ParserRejectsNumericEnums()
    {
        var json = SerializeValid().Replace(
            "\"kind\":\"powerOfTwoChoices\"",
            "\"kind\":42",
            StringComparison.Ordinal);

        var result = GatewayConfigurationParser.Parse(Encoding.UTF8.GetBytes(json));

        result.IsParsed.Should().BeFalse();
    }

    [Fact]
    public void ParserRejectsUnknownStringEnums()
    {
        var json = SerializeValid().Replace(
            "\"kind\":\"powerOfTwoChoices\"",
            "\"kind\":\"futureBalancer\"",
            StringComparison.Ordinal);

        var result = GatewayConfigurationParser.Parse(Encoding.UTF8.GetBytes(json));

        result.IsParsed.Should().BeFalse();
    }

    [Fact]
    public void ParserRejectsUnknownPolymorphicDiscriminator()
    {
        var json = SerializeValid().Replace(
            "\"kind\":\"static\"",
            "\"kind\":\"future-source\"",
            StringComparison.Ordinal);

        GatewayConfigurationParser.Parse(Encoding.UTF8.GetBytes(json)).IsParsed.Should().BeFalse();
    }

    [Fact]
    public void ReaderRejectsParsedButUnsupportedVersionBeforeCanonicalization()
    {
        var json = SerializeValid().Replace(
            "\"canonicalizationVersion\":1",
            "\"canonicalizationVersion\":99",
            StringComparison.Ordinal);

        var result = GatewayConfigurationReader.Read(Encoding.UTF8.GetBytes(json));

        result.IsAccepted.Should().BeFalse();
        result.CanonicalDocument.Should().BeNull();
        result.Errors.Should().Contain(error => error.Code == GatewayValidationErrorCode.UnsupportedVersion);
    }

    [Theory]
    [InlineData(0, 0, 1)]
    [InlineData(1, 1, 1)]
    [InlineData(1, 0, 0)]
    [InlineData(1, 0, 2)]
    public void ValidatorRejectsUnsupportedVersions(ushort major, ushort minor, ushort canonicalization)
    {
        var configuration = GatewayConfigurationTests.CreateValidConfiguration() with
        {
            SchemaVersion = new GatewaySchemaVersion(major, minor),
            CanonicalizationVersion = canonicalization
        };

        GatewayConfigurationValidator.Validate(configuration).Errors
            .Should().Contain(error => error.Code == GatewayValidationErrorCode.UnsupportedVersion);
    }

    [Fact]
    public void ValidatorReturnsBoundedErrorsForMalformedProgrammaticGraph()
    {
        var configuration = GatewayConfigurationTests.CreateValidConfiguration() with
        {
            Definitions = null,
            RootDefaults = null,
            Routes =
            [
                GatewayConfigurationTests.CreateValidConfiguration().Routes[0] with
                {
                    Match = null!,
                    Declarations = null!,
                    Metadata = null!
                }
            ],
            Upstreams =
            [
                GatewayConfigurationTests.CreateValidConfiguration().Upstreams[0] with
                {
                    Transport = null!,
                    Request = null!,
                    Metadata = null!,
                    Endpoints = new StaticEndpointSource
                    {
                        Destinations =
                        [
                            new DestinationDeclaration
                            {
                                Id = new DestinationId("broken"),
                                Address = null!,
                                Metadata = null!
                            }
                        ]
                    }
                }
            ]
        };

        var action = () => GatewayConfigurationValidator.Validate(configuration);

        action.Should().NotThrow();
        action().Errors.Should().NotBeEmpty().And.HaveCountLessThanOrEqualTo(256);
    }

    [Fact]
    public void ValidatorRejectsInvalidBehavioralValues()
    {
        var valid = GatewayConfigurationTests.CreateValidConfiguration();
        var configuration = valid with
        {
            Routes =
            [
                valid.Routes[0] with
                {
                    Declarations = new RouteDeclarations
                    {
                        Inspection = new DeclarationReference<RequestInspectionBinding>
                        {
                            Inline = new RequestInspectionBinding
                            {
                                MaximumBodyBytes = 10,
                                MaximumInspectionBytes = 20
                            }
                        },
                        RequestTransforms = new OrderedRequestTransforms
                        {
                            Headers =
                            [
                                new RequestHeaderTransform
                                {
                                    Kind = HeaderTransformKind.Set,
                                    Name = "Content-Length",
                                    Value = "10"
                                }
                            ]
                        }
                    }
                }
            ],
            Upstreams =
            [
                valid.Upstreams[0] with
                {
                    Transport = new UpstreamTransportDeclaration
                    {
                        MaxConnectionsPerServer = 0,
                        ConnectTimeout = TimeSpan.Zero
                    }
                }
            ]
        };

        var result = GatewayConfigurationValidator.Validate(configuration);

        result.Errors.Should().Contain(error => error.Path.Contains("inspection", StringComparison.Ordinal));
        result.Errors.Should().Contain(error => error.Path.Contains("Content", StringComparison.OrdinalIgnoreCase) || error.Path.Contains("headers", StringComparison.Ordinal));
        result.Errors.Should().Contain(error => error.Path.Contains("maxConnections", StringComparison.Ordinal));
    }

    [Fact]
    public void ParserRejectsDocumentByteBoundBeforeMaterialization()
    {
        var oversized = new byte[GatewayJson.MaximumDocumentBytes + 1];

        var result = GatewayConfigurationParser.Parse(oversized);

        result.IsParsed.Should().BeFalse();
        result.Errors.Should().ContainSingle(error => error.Code == GatewayValidationErrorCode.BoundExceeded);
    }

    [Fact]
    public void CanonicalHashIgnoresResourceAndMetadataOrderingButPreservesMeaning()
    {
        var first = CreateCanonicalizationConfiguration(reverse: false);
        var second = CreateCanonicalizationConfiguration(reverse: true);

        var firstResult = GatewayConfigurationCanonicalizer.TryCanonicalize(first);
        var secondResult = GatewayConfigurationCanonicalizer.TryCanonicalize(second);

        firstResult.IsCanonicalized.Should().BeTrue();
        secondResult.IsCanonicalized.Should().BeTrue();
        secondResult.Document!.ContentHash.Should().Be(firstResult.Document!.ContentHash);
        secondResult.Document.Utf8Json.Should().Equal(firstResult.Document.Utf8Json);
    }

    private static string SerializeValid() => JsonSerializer.Serialize(
        GatewayConfigurationTests.CreateValidConfiguration(),
        GatewayJsonSerializerContext.Default.GatewayConfiguration);

    private static GatewayConfiguration CreateCanonicalizationConfiguration(bool reverse)
    {
        var valid = GatewayConfigurationTests.CreateValidConfiguration();
        var secondUpstream = valid.Upstreams[0] with
        {
            Id = new UpstreamId("billing"),
            Metadata = new ResourceMetadata
            {
                Labels = reverse
                    ? [new MetadataEntry("z", "2"), new MetadataEntry("a", "1")]
                    : [new MetadataEntry("a", "1"), new MetadataEntry("z", "2")]
            }
        };
        var secondRoute = valid.Routes[0] with
        {
            Id = new RouteId("billing-api"),
            Upstream = new UpstreamId("billing"),
            Match = new HttpRouteMatch { Path = "/billing/{**catch-all}" }
        };

        return valid with
        {
            Routes = reverse ? [valid.Routes[0], secondRoute] : [secondRoute, valid.Routes[0]],
            Upstreams = reverse ? [valid.Upstreams[0], secondUpstream] : [secondUpstream, valid.Upstreams[0]]
        };
    }
}
