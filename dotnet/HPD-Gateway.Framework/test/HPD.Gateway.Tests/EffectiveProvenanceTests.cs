using System.Text.Json;
using FluentAssertions;
using HPD.Gateway.Abstractions;
using HPD.Gateway.Abstractions.Serialization;
using HPD.Gateway.Core;
using HPD.Gateway.Effective;
using HPD.Gateway.Effective.Serialization;
using HPD.Gateway.Yarp;
using Yarp.ReverseProxy.Configuration;
using Xunit;

namespace HPD.Gateway.Tests;

public sealed class EffectiveProvenanceTests
{
    [Fact]
    public async Task RootAndDefinitionOverrideProduceExactOrderedProvenance()
    {
        var configuration = Configuration() with
        {
            Definitions = new GatewayDefinitions
            {
                Authorization = [new DeclarationDefinition<NamedAuthorizationPolicy>
                {
                    Id = new DefinitionId("orders"),
                    Specification = new NamedAuthorizationPolicy("orders.write")
                }]
            },
            RootDefaults = new GatewayRootDeclarations
            {
                Authorization = new DeclarationReference<NamedAuthorizationPolicy>
                {
                    Inline = new NamedAuthorizationPolicy("authenticated")
                }
            },
            Routes = [Route() with
            {
                Declarations = new RouteDeclarations
                {
                    Authorization = new DeclarationReference<NamedAuthorizationPolicy>
                    {
                        Definition = new DefinitionId("orders")
                    }
                }
            }]
        };

        var result = await Materialize(configuration);

        var record = result.EffectiveSnapshot!.Records.Single(item => item.Family == GatewayEffectiveFamilies.Authorization);
        record.TargetId.Should().Be("route");
        record.Contributions.Select(item => item.SourceKind).Should().Equal(
            GatewayContributionSourceKind.RootDefault,
            GatewayContributionSourceKind.ReusableDefinition);
        record.Contributions.Select(item => item.Disposition).Should().Equal(
            GatewayContributionDisposition.Overridden,
            GatewayContributionDisposition.Selected);
        record.Contributions[1].Definition.Should().Be(new DefinitionId("orders"));
        result.Bundle!.Routes.Single().AuthorizationPolicy.Should().Be("orders.write");
    }

    [Fact]
    public async Task InlineAndDefinitionHaveDifferentSourcesButEqualEffectiveHash()
    {
        var definitionConfiguration = Configuration() with
        {
            Definitions = new GatewayDefinitions
            {
                Cors = [new DeclarationDefinition<CorsPolicyBinding>
                {
                    Id = new DefinitionId("cors"),
                    Specification = new CorsPolicyBinding("cors-policy")
                }]
            },
            Routes = [Route() with { Declarations = new RouteDeclarations
            {
                Cors = new DeclarationReference<CorsPolicyBinding> { Definition = new DefinitionId("cors") }
            }}]
        };
        var inlineConfiguration = Configuration() with
        {
            Routes = [Route() with { Declarations = new RouteDeclarations
            {
                Cors = new DeclarationReference<CorsPolicyBinding> { Inline = new CorsPolicyBinding("cors-policy") }
            }}]
        };

        var fromDefinition = (await Materialize(definitionConfiguration)).EffectiveSnapshot!.Records.Single();
        var fromInline = (await Materialize(inlineConfiguration)).EffectiveSnapshot!.Records.Single();

        fromDefinition.EffectiveContentHash.Should().Be(fromInline.EffectiveContentHash);
        fromDefinition.Contributions.Single().SourceKind.Should().Be(GatewayContributionSourceKind.ReusableDefinition);
        fromInline.Contributions.Single().SourceKind.Should().Be(GatewayContributionSourceKind.Inline);
    }

    [Fact]
    public async Task TransformProjectionPreservesFamilyAndOrderWithoutExposingValues()
    {
        var configuration = Configuration() with
        {
            Routes = [Route() with { Declarations = new RouteDeclarations
            {
                RequestTransforms = new OrderedRequestTransforms
                {
                    Headers =
                    [
                        new RequestHeaderTransform { Kind = HeaderTransformKind.Set, Name = "x-first", Value = "secret-one" },
                        new RequestHeaderTransform { Kind = HeaderTransformKind.Append, Name = "x-second", Value = "secret-two" }
                    ]
                }
            }}]
        };

        var result = await Materialize(configuration);
        var record = result.EffectiveSnapshot!.Records.Single();
        var json = JsonSerializer.Serialize(result.EffectiveSnapshot, GatewayEffectiveJsonSerializerContext.Default.GatewayEffectiveSnapshot);

        record.Family.Should().Be(GatewayEffectiveFamilies.RequestHeaderTransforms);
        record.Composition.Should().Be(GatewayEffectiveComposition.AdditiveOrdered);
        result.Bundle!.Routes.Single().Transforms!.Count.Should().Be(2);
        json.Should().NotContain("secret-one").And.NotContain("secret-two");
    }

    [Fact]
    public async Task RejectedNativeCandidateHasNoEffectiveSnapshot()
    {
        var accepted = Read(Configuration());
        var identity = Identity(accepted);
        var result = await new GatewayNativeMaterializer(new RejectingValidator())
            .MaterializeAsync(accepted, identity, "rejected-native");

        result.IsMaterialized.Should().BeFalse();
        result.EffectiveSnapshot.Should().BeNull();
        result.Bundle.Should().BeNull();
    }

    private static async Task<GatewayMaterializationResult> Materialize(GatewayConfiguration configuration)
    {
        var accepted = Read(configuration);
        return await new GatewayNativeMaterializer(new AcceptingValidator())
            .MaterializeAsync(accepted, Identity(accepted), "effective-native");
    }

    private static GatewayCandidateReadResult Read(GatewayConfiguration configuration)
    {
        var capabilities = HostCapabilitySnapshot.Create(new HostCapabilityRegistration
        {
            InstalledFamilies = GatewayDeclarationFamilies.Authorization | GatewayDeclarationFamilies.Cors |
                GatewayDeclarationFamilies.RequestTransforms,
            AuthorizationPolicies = ["authenticated", "orders.write"],
            CorsPolicies = ["cors-policy"]
        });
        var json = JsonSerializer.SerializeToUtf8Bytes(configuration, GatewayJsonSerializerContext.Default.GatewayConfiguration);
        var result = GatewayCandidateReader.Read(json, capabilities);
        result.IsAccepted.Should().BeTrue(string.Join(", ", result.Errors.Select(item => item.Message)));
        return result;
    }

    private static PublicationCandidateIdentity Identity(GatewayCandidateReadResult accepted) =>
        new(new CandidateId("candidate"), "authority", "epoch", 1, accepted.CanonicalDocument!.ContentHash);

    private static GatewayConfiguration Configuration() => new()
    {
        SchemaVersion = new GatewaySchemaVersion(1, 0),
        CanonicalizationVersion = 1,
        Routes = [Route()],
        Upstreams = [new UpstreamDeclaration
        {
            Id = new UpstreamId("upstream"),
            Endpoints = new StaticEndpointSource
            {
                Destinations = [new DestinationDeclaration
                {
                    Id = new DestinationId("destination"),
                    Address = new Uri("http://127.0.0.1:5001/")
                }]
            }
        }]
    };

    private static RouteDeclaration Route() => new()
    {
        Id = new RouteId("route"),
        Match = new HttpRouteMatch { Path = "/{**catch-all}" },
        Upstream = new UpstreamId("upstream")
    };

    private sealed class AcceptingValidator : IConfigValidator
    {
        public ValueTask<IList<Exception>> ValidateRouteAsync(RouteConfig route) => ValueTask.FromResult<IList<Exception>>([]);
        public ValueTask<IList<Exception>> ValidateClusterAsync(ClusterConfig cluster) => ValueTask.FromResult<IList<Exception>>([]);
    }

    private sealed class RejectingValidator : IConfigValidator
    {
        public ValueTask<IList<Exception>> ValidateRouteAsync(RouteConfig route) => ValueTask.FromResult<IList<Exception>>([new InvalidOperationException()]);
        public ValueTask<IList<Exception>> ValidateClusterAsync(ClusterConfig cluster) => ValueTask.FromResult<IList<Exception>>([]);
    }
}
