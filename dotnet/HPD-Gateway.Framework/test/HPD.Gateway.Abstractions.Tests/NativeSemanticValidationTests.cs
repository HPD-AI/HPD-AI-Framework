using System.Buffers.Binary;
using System.Collections.Immutable;
using System.Security.Cryptography;
using FluentAssertions;
using HPD.Gateway.Abstractions;
using HPD.Gateway.Core;
using Xunit;

namespace HPD.Gateway.Tests;

public sealed class NativeSemanticValidationTests
{
    [Theory]
    [InlineData("G ET")]
    [InlineData("BREW")]
    public void InvalidOrUnsupportedMethodsAreRejected(string method)
    {
        var configuration = WithMatch(new HttpRouteMatch { Path = "/orders", Methods = [method] });
        GatewayConfigurationValidator.Validate(configuration).IsValid.Should().BeFalse();
    }

    [Fact]
    public void MethodsAndHostsRejectCaseInsensitiveDuplicates()
    {
        var configuration = WithMatch(new HttpRouteMatch
        {
            Path = "/orders", Methods = ["GET", "get"], Hosts = ["API.Example.com", "api.example.com"]
        });
        GatewayConfigurationValidator.Validate(configuration).Errors.Should().HaveCountGreaterThanOrEqualTo(2);
    }

    [Fact]
    public void AspNetRouteParserRejectsInvalidPath()
    {
        var configuration = WithMatch(new HttpRouteMatch { Path = "/orders/{id" });
        GatewayCandidateValidator.Validate(configuration, Capabilities()).Errors
            .Should().Contain(error => error.Path.EndsWith("match.path", StringComparison.Ordinal));
    }

    [Fact]
    public void InvalidHostHeaderQueryAndDuplicatePredicatesAreRejected()
    {
        var predicate = new HttpHeaderMatch { Name = "Bad Header", Kind = TextMatchKind.Exact, Values = ["x"] };
        var configuration = WithMatch(new HttpRouteMatch
        {
            Path = "/orders", Hosts = ["bad host"], Headers = [predicate, predicate],
            Query = [new HttpQueryMatch { Name = "x&y", Kind = TextMatchKind.NotExists }]
        });
        GatewayConfigurationValidator.Validate(configuration).Errors.Should().HaveCountGreaterThanOrEqualTo(4);
    }

    [Fact]
    public void DuplicateEffectiveRouteShapeAtSameOrderIsRejected()
    {
        var valid = GatewayConfigurationTests.CreateValidConfiguration();
        var configuration = valid with { Routes = [valid.Routes[0], valid.Routes[0] with { Id = new RouteId("orders-copy") }] };
        GatewayConfigurationValidator.Validate(configuration).Errors.Should()
            .Contain(error => error.Code == GatewayValidationErrorCode.AmbiguousRoute);
    }

    [Fact]
    public void DestinationComponentsHostOverrideAndDurationAreRejected()
    {
        var valid = GatewayConfigurationTests.CreateValidConfiguration();
        var upstream = valid.Upstreams[0] with
        {
            Endpoints = new StaticEndpointSource
            {
                Destinations = [new DestinationDeclaration
                {
                    Id = new DestinationId("bad"), Address = new Uri("https://user@example.com/path?q=1#fragment"), HostOverride = "bad\rhost"
                }]
            },
            Request = new UpstreamRequestDeclaration { ActivityTimeout = TimeSpan.FromDays(2) }
        };
        GatewayConfigurationValidator.Validate(valid with { Upstreams = [upstream] }).Errors.Should().HaveCountGreaterThanOrEqualTo(3);
    }

    [Theory]
    [InlineData("Bad Header", "ok")]
    [InlineData("Connection", "close")]
    [InlineData("X-Test", "bad\r\nvalue")]
    public void InvalidHeaderTransformsAreRejected(string name, string value)
    {
        var valid = GatewayConfigurationTests.CreateValidConfiguration();
        var route = valid.Routes[0] with { Declarations = new RouteDeclarations
        {
            RequestTransforms = new OrderedRequestTransforms { Headers = [new RequestHeaderTransform { Kind = HeaderTransformKind.Set, Name = name, Value = value }] }
        }};
        GatewayConfigurationValidator.Validate(valid with { Routes = [route] }).IsValid.Should().BeFalse();
    }

    [Fact]
    public void TlsAndNamedPoliciesResolveAgainstHostCapabilities()
    {
        var valid = GatewayConfigurationTests.CreateValidConfiguration();
        var upstream = valid.Upstreams[0] with
        {
            Endpoints = new StaticEndpointSource { Destinations = [new DestinationDeclaration { Id = new DestinationId("plain"), Address = new Uri("http://orders.internal") }] },
            Transport = new UpstreamTransportDeclaration { Tls = new UpstreamTlsDeclaration { ServerName = "orders.internal" } }
        };
        var result = GatewayCandidateValidator.Validate(valid with { Upstreams = [upstream] }, new HostCapabilitySnapshot());
        result.Errors.Should().Contain(error => error.Path.Contains("transport.tls", StringComparison.Ordinal));
        result.Errors.Should().Contain(error => error.Path.Contains("authorization", StringComparison.Ordinal));
    }

    [Fact]
    public void CanonicalIdentityNormalizesMethodsHostsAndUsesVersionFraming()
    {
        var upper = WithMatch(new HttpRouteMatch { Path = "/orders", Methods = ["GET"], Hosts = ["API.Example.COM"] });
        var lower = WithMatch(new HttpRouteMatch { Path = "/orders", Methods = ["get"], Hosts = ["api.example.com"] });
        var first = GatewayConfigurationCanonicalizer.TryCanonicalize(upper).Document!;
        var second = GatewayConfigurationCanonicalizer.TryCanonicalize(lower).Document!;
        first.ContentHash.Should().Be(second.ContentHash);
        first.Utf8Json.Should().Equal(second.Utf8Json);

        var framed = new byte[first.Utf8Json.Length + 6];
        BinaryPrimitives.WriteUInt16BigEndian(framed.AsSpan(0, 2), 1);
        BinaryPrimitives.WriteUInt16BigEndian(framed.AsSpan(2, 2), 0);
        BinaryPrimitives.WriteUInt16BigEndian(framed.AsSpan(4, 2), 1);
        first.Utf8Json.AsSpan().CopyTo(framed.AsSpan(6));
        first.ContentHash.Should().Be(new ContentHash("sha-256", Convert.ToHexStringLower(SHA256.HashData(framed))));
        typeof(GatewayCanonicalDocument).GetConstructors().Should().BeEmpty();
        typeof(GatewayCanonicalDocument).GetProperty(nameof(GatewayCanonicalDocument.Utf8Json))!.CanWrite.Should().BeFalse();
    }

    private static GatewayConfiguration WithMatch(HttpRouteMatch match)
    {
        var valid = GatewayConfigurationTests.CreateValidConfiguration();
        return valid with { Routes = [valid.Routes[0] with { Match = match }] };
    }

    private static HostCapabilitySnapshot Capabilities() => new()
    {
        AuthorizationPolicies = ImmutableHashSet.Create(StringComparer.Ordinal, "orders.read")
    };
}
