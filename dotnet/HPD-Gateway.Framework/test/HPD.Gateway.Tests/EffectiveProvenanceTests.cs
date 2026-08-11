using System.Collections.Immutable;
using System.Reflection;
using System.Text.Json;
using FluentAssertions;
using HPD.Gateway;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Yarp.ReverseProxy;
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

        var record = result.PreparedProjectionSnapshot!.Records.Single(item => item.Family == GatewayEffectiveFamilies.Authorization);
        record.TargetId.Should().Be("route");
        record.Contributions.Select(item => item.SourceKind).Should().Equal(
            GatewayContributionSourceKind.RootDefault,
            GatewayContributionSourceKind.ReusableDefinition);
        record.Contributions.Select(item => item.Disposition).Should().Equal(
            GatewayContributionDisposition.Overridden,
            GatewayContributionDisposition.Selected);
        record.Contributions.Select(item => item.Scope).Should().Equal(
            GatewayContributionScope.RootDefault,
            GatewayContributionScope.RouteLocal);
        record.Contributions[1].Definition.Should().Be(new DefinitionId("orders"));
        result.PreparedApplication!.Routes.Single().AuthorizationPolicy.Should().Be("orders.write");
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

        var fromDefinition = (await Materialize(definitionConfiguration)).PreparedProjectionSnapshot!.Records.Single();
        var fromInline = (await Materialize(inlineConfiguration)).PreparedProjectionSnapshot!.Records.Single();

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
        var record = result.PreparedProjectionSnapshot!.Records.Single();

        record.Family.Should().Be(GatewayEffectiveFamilies.RequestHeaderTransforms);
        record.Composition.Should().Be(GatewayEffectiveComposition.AdditiveOrdered);
        result.PreparedApplication!.Routes.Single().Transforms!.Count.Should().Be(2);
        record.Contributions.Should().NotContain(item =>
            item.SourceIdentity.Contains("secret-one", StringComparison.Ordinal) ||
            item.SourceIdentity.Contains("secret-two", StringComparison.Ordinal));
    }

    [Fact]
    public async Task RejectedNativeCandidateHasNoPreparedProjectionSnapshot()
    {
        var accepted = Read(Configuration());
        var identity = Identity(accepted);
        var result = await new GatewayRuntimePlanner(new RejectingValidator())
            .PlanAsync(accepted, identity, "rejected-native");

        result.IsPlanned.Should().BeFalse();
        result.PreparedProjectionSnapshot.Should().BeNull();
        result.PreparedApplication.Should().BeNull();
    }

    [Fact]
    public void PublicationHandoffRejectsMissingTruncatedAndStructurallyInvalidSnapshots()
    {
        var identity = new PublicationCandidateIdentity(new CandidateId("candidate"), "authority", "epoch", 1, new ContentHash("sha-256", new string('a', 64)));
        var valid = new GatewayPreparedProjectionSnapshot(1, identity.CandidateId, identity.ContentHash, [], false);

        var missing = () => PreparedApplicationTestFactory.Create(identity, [], [], "native", null!);
        var truncated = () => PreparedApplicationTestFactory.Create(identity, [], [], "native", valid with { IsTruncated = true });
        var defaultRecords = () => PreparedApplicationTestFactory.Create(identity, [], [], "native", valid with { Records = default });
        var wrongSchema = () => PreparedApplicationTestFactory.Create(identity, [], [], "native", valid with { SchemaVersion = 2 });

        missing.Should().Throw<ArgumentNullException>();
        truncated.Should().Throw<ArgumentException>();
        defaultRecords.Should().Throw<ArgumentException>();
        wrongSchema.Should().Throw<ArgumentException>();
    }

    [Fact]
    public async Task SharedDefinitionFansOutWithStableDefinitionIdentity()
    {
        var configuration = Configuration() with
        {
            Definitions = new GatewayDefinitions
            {
                Authorization = [new DeclarationDefinition<NamedAuthorizationPolicy>
                {
                    Id = new DefinitionId("shared"),
                    Specification = new NamedAuthorizationPolicy("orders.write")
                }]
            },
            Routes =
            [
                Route() with { Id = new RouteId("a"), Match = new HttpRouteMatch { Path = "/a" }, Declarations = AuthorizationDefinition("shared") },
                Route() with { Id = new RouteId("b"), Match = new HttpRouteMatch { Path = "/b" }, Declarations = AuthorizationDefinition("shared") }
            ]
        };

        var records = (await Materialize(configuration)).PreparedProjectionSnapshot!.Records;

        records.Select(item => item.TargetId).Should().Equal("a", "b");
        records.Select(item => item.Contributions.Single().Definition).Should().OnlyContain(item => item == new DefinitionId("shared"));
        records.Select(item => item.EffectiveContentHash).Distinct().Should().ContainSingle();
    }

    [Fact]
    public async Task CanonicallyEquivalentResourceOrderProducesIdenticalRecords()
    {
        var routes = new[]
        {
            Route() with { Id = new RouteId("a"), Match = new HttpRouteMatch { Path = "/a" }, Declarations = InlineCors() },
            Route() with { Id = new RouteId("b"), Match = new HttpRouteMatch { Path = "/b" }, Declarations = InlineCors() }
        };
        var first = Configuration() with { Routes = [.. routes] };
        var second = Configuration() with { Routes = [.. routes.Reverse()] };

        var firstResult = await Materialize(first);
        var secondResult = await Materialize(second);

        firstResult.PreparedProjectionSnapshot!.CandidateContentHash.Should().Be(secondResult.PreparedProjectionSnapshot!.CandidateContentHash);
        firstResult.PreparedProjectionSnapshot.Records.Should().BeEquivalentTo(
            secondResult.PreparedProjectionSnapshot.Records, options => options.WithStrictOrdering());
    }

    [Fact]
    public void PublicationHandoffRejectsUnsortedDuplicateAndOverBoundContributions()
    {
        var identity = new PublicationCandidateIdentity(new CandidateId("candidate"), "authority", "epoch", 1, new ContentHash("sha-256", new string('a', 64)));
        var contribution = Contribution(0);
        var first = Record("b", [contribution]);
        var second = Record("a", [contribution]);
        var unsorted = new GatewayPreparedProjectionSnapshot(1, identity.CandidateId, identity.ContentHash, [first, second], false);
        var duplicate = unsorted with { Records = [first, first] };
        var overBound = unsorted with
        {
            Records = [Record("a", Enumerable.Range(0, GatewayEffectiveBounds.MaximumContributionsPerRecord + 1).Select(Contribution).ToImmutableArray())]
        };
        var overRecordBound = unsorted with
        {
            Records = Enumerable.Repeat(Record("a", [contribution]), GatewayEffectiveBounds.MaximumRecords + 1).ToImmutableArray()
        };

        Action publishUnsorted = () => PreparedApplicationTestFactory.Create(identity, [], [], "native", unsorted);
        Action publishDuplicate = () => PreparedApplicationTestFactory.Create(identity, [], [], "native", duplicate);
        Action publishOverBound = () => PreparedApplicationTestFactory.Create(identity, [], [], "native", overBound);
        Action publishOverRecordBound = () => PreparedApplicationTestFactory.Create(identity, [], [], "native", overRecordBound);
        publishUnsorted.Should().Throw<ArgumentException>();
        publishDuplicate.Should().Throw<ArgumentException>();
        publishOverBound.Should().Throw<ArgumentException>();
        publishOverRecordBound.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void SealedHandoffRejectsMissingTargetsWrongCompositionAndImpossibleContributions()
    {
        var identity = new PublicationCandidateIdentity(new CandidateId("candidate"), "authority", "epoch", 1, new ContentHash("sha-256", new string('a', 64)));
        var native = new RouteConfig
        {
            RouteId = "route",
            ClusterId = "upstream",
            Match = new RouteMatch { Path = "/{**catch-all}" },
            AuthorizationPolicy = "authenticated"
        };
        var empty = new GatewayPreparedProjectionSnapshot(1, identity.CandidateId, identity.ContentHash, [], false);
        var missingTarget = empty with { Records = [Record("ghost", [Contribution(0)])] };
        var wrongComposition = empty with
        {
            Records = [Record("route", [Contribution(0)]) with { Composition = GatewayEffectiveComposition.AdditiveOrdered }]
        };
        var impossibleContribution = Contribution(0) with
        {
            SourceKind = GatewayContributionSourceKind.HostProfile,
            Scope = GatewayContributionScope.RouteLocal,
            Disposition = GatewayContributionDisposition.Selected
        };
        var impossible = empty with { Records = [Record("route", [impossibleContribution])] };

        Action missingRecord = () => PreparedApplicationTestFactory.Create(identity, [native], [], "native", empty);
        Action absentTarget = () => PreparedApplicationTestFactory.Create(identity, [native], [], "native", missingTarget);
        Action invalidComposition = () => PreparedApplicationTestFactory.Create(identity, [native], [], "native", wrongComposition);
        Action invalidContribution = () => PreparedApplicationTestFactory.Create(identity, [native], [], "native", impossible);

        missingRecord.Should().Throw<ArgumentException>();
        absentTarget.Should().Throw<ArgumentException>();
        invalidComposition.Should().Throw<ArgumentException>();
        invalidContribution.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void ProductionHandoffAcceptsOnlyThePrivatePreparedProjectionToken()
    {
        typeof(GatewayEffectiveProjectionBuilder.PreparedProjection)
            .GetConstructors(BindingFlags.Instance | BindingFlags.NonPublic)
            .Should().ContainSingle();
        typeof(GatewayEffectiveProjectionBuilder)
            .GetField("PreparationKey", BindingFlags.Static | BindingFlags.NonPublic)
            .Should().NotBeNull().And.Match<FieldInfo>(field => field.IsPrivate && field.IsInitOnly);
        typeof(GatewayRuntimePlan).GetConstructors(BindingFlags.Instance | BindingFlags.NonPublic)
            .Single().GetParameters().Should().Contain(parameter => parameter.ParameterType ==
                typeof(GatewayEffectiveProjectionBuilder.PreparedProjection));
        typeof(GatewayPreparedApplication).GetMethods(BindingFlags.Static | BindingFlags.NonPublic)
            .Single(method => method.Name == "Create").GetParameters().First().ParameterType
            .Should().Be(typeof(GatewayRuntimeApplicationPreparer.NativeValidationReceipt));
        typeof(GatewayRuntimeApplicationPreparer.NativeValidationReceipt)
            .GetConstructors(BindingFlags.Instance | BindingFlags.NonPublic)
            .Should().ContainSingle();
        typeof(GatewayRuntimeApplicationPreparer)
            .GetField("NativeValidationKey", BindingFlags.Static | BindingFlags.NonPublic)
            .Should().NotBeNull().And.Match<FieldInfo>(field => field.IsPrivate && field.IsInitOnly);
        typeof(GatewayRuntimePlan).GetConstructors(BindingFlags.Instance | BindingFlags.NonPublic)
            .Single().GetParameters().Single(parameter => parameter.ParameterType ==
                typeof(GatewayEffectiveProjectionBuilder.PreparedProjection)).ParameterType
            .Should().Be(typeof(GatewayEffectiveProjectionBuilder.PreparedProjection));
        typeof(GatewayPreparedApplication).GetMethods(BindingFlags.Static | BindingFlags.NonPublic)
            .Where(static method => method.Name == "Create")
            .SelectMany(static method => method.GetParameters())
            .Should().NotContain(parameter => parameter.ParameterType == typeof(GatewayPreparedProjectionSnapshot));
    }

    [Fact]
    public async Task EveryNonCacheFamilyCorrelatesWithTheExactNativeRoute()
    {
        var inspection = new RequestInspectionBinding
        {
            InspectorName = "inspector",
            Mode = RequestInspectionMode.BoundedPrefix,
            MaximumAcceptedBodyBytes = 1024,
            MaximumInspectedBytes = 64,
            SpillPolicy = RequestInspectionSpillPolicy.Disabled
        };
        var configuration = Configuration() with
        {
            RootDefaults = new GatewayRootDeclarations
            {
                Authorization = Inline(new NamedAuthorizationPolicy("authenticated")),
                Cors = Inline(new CorsPolicyBinding("cors-policy")),
                TrafficAdmission = Inline(new TrafficAdmissionBinding("admission")),
                RequestTimeout = Inline(new RequestTimeoutBinding { Timeout = TimeSpan.FromSeconds(7) }),
                Inspection = Inline(inspection),
                CredentialDisposition = Inline(new CredentialDispositionBinding { Kind = CredentialDispositionKind.Strip })
            },
            Routes = [Route() with { Declarations = new RouteDeclarations
            {
                RequestTransforms = new OrderedRequestTransforms
                {
                    Headers = [new RequestHeaderTransform { Kind = HeaderTransformKind.Set, Name = "x-request", Value = "value" }]
                },
                ResponseTransforms = new OrderedResponseTransforms
                {
                    Headers = [new ResponseHeaderTransform { Kind = HeaderTransformKind.Append, Name = "x-response", Value = "value" }],
                    Trailers = [new ResponseHeaderTransform { Kind = HeaderTransformKind.Set, Name = "x-trailer", Value = "value" }]
                }
            }}]
        };
        var capabilities = HostCapabilitySnapshot.Create(new HostCapabilityRegistration
        {
            InstalledFamilies = GatewayDeclarationFamilies.Authorization | GatewayDeclarationFamilies.Cors |
                GatewayDeclarationFamilies.TrafficAdmission | GatewayDeclarationFamilies.RequestTimeout |
                GatewayDeclarationFamilies.Inspection | GatewayDeclarationFamilies.CredentialDisposition |
                GatewayDeclarationFamilies.RequestTransforms | GatewayDeclarationFamilies.ResponseTransforms,
            AuthorizationPolicies = ["authenticated"],
            CorsPolicies = ["cors-policy"],
            TrafficAdmissionPolicies = ["admission"],
            RequestInspectors = ["inspector"],
            ProtectedCredentialHeaders = ["x-api-key"]
        });
        var registry = new GatewayInspectionRegistry(
            ImmutableDictionary<string, IGatewayRequestInspector>.Empty.Add("inspector", new AllowingInspector()));
        var accepted = Read(configuration, capabilities);
        var result = await new GatewayRuntimePlanner(new AcceptingValidator(), registry)
            .PlanAsync(accepted, Identity(accepted), "all-family-native");

        var native = result.PreparedApplication!.Routes.Single();
        native.AuthorizationPolicy.Should().Be("authenticated");
        native.CorsPolicy.Should().Be("cors-policy");
        native.RateLimiterPolicy.Should().Be("admission");
        native.Timeout.Should().Be(TimeSpan.FromSeconds(7));
        native.Metadata![GatewayInspectionMetadata.Inspector].Should().Be("inspector");
        native.Transforms.Should().NotBeEmpty();
        result.PreparedProjectionSnapshot!.Records.Select(item => item.Family).Should().Equal(
            GatewayEffectiveFamilies.Authorization,
            GatewayEffectiveFamilies.Cors,
            GatewayEffectiveFamilies.CredentialDisposition,
            GatewayEffectiveFamilies.Inspection,
            GatewayEffectiveFamilies.RequestHeaderTransforms,
            GatewayEffectiveFamilies.RequestTimeout,
            GatewayEffectiveFamilies.ResponseHeaderTransforms,
            GatewayEffectiveFamilies.ResponseTrailerTransforms,
            GatewayEffectiveFamilies.TrafficAdmission);
        result.PreparedProjectionSnapshot.Records.Single(item => item.Family == GatewayEffectiveFamilies.Inspection)
            .Contributions.Last().ContentHash.Should().Be(GatewayEffectiveProjectionBuilder.Hash("hpd.gateway/inspector/v1", "inspector"));
        result.PreparedProjectionSnapshot.Records.Single(item => item.Family == GatewayEffectiveFamilies.CredentialDisposition)
            .Contributions.Last().ContentHash.Should().Be(GatewayEffectiveProjectionBuilder.Hash(
                "hpd.gateway/protected-credential-catalog/v1", "authorization\ncookie\nproxy-authorization\nx-api-key"));
    }

    [Fact]
    public async Task OutputCacheProfileHashExactlyMatchesAcceptedAndRuntimeCapability()
    {
        var profile = new GatewayOutputCacheProfile
        {
            Name = "cache",
            Version = 7,
            Expiration = TimeSpan.FromMinutes(2),
            QueryKeys = ["tenant"],
            HeaderNames = ["x-region"]
        };
        var capability = new OutputCacheCapability(
            profile.Name, profile.Version, true, "memory", OutputCacheStoreScope.ProcessLocal,
            profile.Expiration, 1_048_576, 16_777_216, profile.QueryKeys, profile.HeaderNames);
        var configuration = Configuration() with
        {
            RootDefaults = new GatewayRootDeclarations
            {
                CredentialDisposition = Inline(new CredentialDispositionBinding { Kind = CredentialDispositionKind.Strip })
            },
            Routes = [Route() with
            {
                Match = new HttpRouteMatch { Path = "/{**catch-all}", Methods = ["GET"] },
                Declarations = new RouteDeclarations
                {
                    OutputCache = Inline(new OutputCacheBinding("cache"))
                }
            }]
        };
        var capabilities = HostCapabilitySnapshot.Create(new HostCapabilityRegistration
        {
            InstalledFamilies = GatewayDeclarationFamilies.OutputCache | GatewayDeclarationFamilies.CredentialDisposition,
            OutputCacheProfiles = [capability]
        });
        var accepted = Read(configuration, capabilities);
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddReverseProxy();
        services.AddSingleton<IConfigValidator>(new AcceptingValidator());
        services.AddHpdGatewayYarpMaterialization();
        services.AddHpdGatewayOutputCaching(builder => builder.Add(profile));
        await using var provider = services.BuildServiceProvider();

        var result = await provider.GetRequiredService<GatewayRuntimePlanner>()
            .PlanAsync(accepted, Identity(accepted), "cache-native");

        result.PreparedApplication!.Routes.Single().OutputCachePolicy.Should().Be("cache");
        var record = result.PreparedProjectionSnapshot!.Records.Single(item => item.Family == GatewayEffectiveFamilies.OutputCache);
        record.Contributions.Last().ContentHash.Should().Be(GatewayEffectiveProjectionBuilder.Hash(
            "hpd.gateway/output-cache-profile/v1", "cache", "7", bool.TrueString, "memory",
            OutputCacheStoreScope.ProcessLocal.ToString(), profile.Expiration.Ticks.ToString(), "1048576", "16777216", "tenant", "x-region"));
        record.EffectiveContentHash.Should().Be(GatewayEffectiveProjectionBuilder.Hash(
            "hpd.gateway/effective-value/v1", GatewayEffectiveFamilies.OutputCache,
            GatewayEffectiveProjectionBuilder.Hash("output-cache/v1", "cache").Value,
            record.Contributions.Last().ContentHash.Value));
    }

    [Fact]
    public async Task PreparationIsNotActiveAndRealYarpReloadTracksAddChangeRemoveReadd()
    {
        var capabilities = HostCapabilitySnapshot.Create(new HostCapabilityRegistration
        {
            InstalledFamilies = GatewayDeclarationFamilies.Authorization | GatewayDeclarationFamilies.Cors,
            AuthorizationPolicies = ["authenticated"],
            CorsPolicies = ["cors-policy"]
        });
        var added = Configuration() with { Routes = [Route() with { Declarations = InlineCors() }] };
        var changed = Configuration() with { Routes = [Route() with { Declarations = new RouteDeclarations
        {
            Authorization = Inline(new NamedAuthorizationPolicy("authenticated"))
        }}] };
        var removed = Configuration() with { Routes = [] };

        var builder = WebApplication.CreateSlimBuilder();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Services.AddAuthorizationBuilder().AddPolicy("authenticated", policy => policy.RequireAssertion(_ => true));
        builder.Services.AddCors(options => options.AddPolicy("cors-policy", policy => policy.AllowAnyOrigin()));
        builder.Services.AddReverseProxy();
        builder.Services.AddHpdGatewayYarpPublication();
        builder.Services.AddHpdGatewayYarpMaterialization();
        await using var application = builder.Build();
        application.MapReverseProxy();
        await application.StartAsync();
        var materializer = application.Services.GetRequiredService<GatewayRuntimePlanner>();
        var publisher = application.Services.GetRequiredService<GatewayRuntimePublisher>();

        var first = await Prepare(materializer, added, capabilities, 1);
        publisher.GetCurrent().Active.Should().BeNull("preparation is not activation");
        (await publisher.PublishAsync(first.PreparedApplication!, TimeSpan.FromSeconds(5))).State.Should().Be(GatewayPublicationState.ActiveAcknowledged);
        first.PreparedProjectionSnapshot!.Records.Should().ContainSingle(item => item.Family == GatewayEffectiveFamilies.Cors);

        var second = await Prepare(materializer, changed, capabilities, 2);
        (await publisher.PublishAsync(second.PreparedApplication!, TimeSpan.FromSeconds(5))).State.Should().Be(GatewayPublicationState.ActiveAcknowledged);
        second.PreparedProjectionSnapshot!.Records.Should().ContainSingle(item => item.Family == GatewayEffectiveFamilies.Authorization);

        var third = await Prepare(materializer, removed, capabilities, 3);
        third.PreparedProjectionSnapshot!.Records.Should().BeEmpty();
        (await publisher.PublishAsync(third.PreparedApplication!, TimeSpan.FromSeconds(5))).State.Should().Be(GatewayPublicationState.ActiveAcknowledged);

        var fourth = await Prepare(materializer, added, capabilities, 4);
        (await publisher.PublishAsync(fourth.PreparedApplication!, TimeSpan.FromSeconds(5))).State.Should().Be(GatewayPublicationState.ActiveAcknowledged);
        fourth.PreparedProjectionSnapshot!.Records.Should().BeEquivalentTo(first.PreparedProjectionSnapshot.Records);
        publisher.GetCurrent().Active!.Candidate.CandidateId.Should().Be(new CandidateId("candidate-4"));
    }

    private static async Task<GatewayRuntimePlanningResult> Prepare(
        GatewayRuntimePlanner materializer,
        GatewayConfiguration configuration,
        HostCapabilitySnapshot capabilities,
        ulong version)
    {
        var accepted = Read(configuration, capabilities);
        var identity = new PublicationCandidateIdentity(new CandidateId($"candidate-{version}"), "authority", "epoch", version, accepted.CanonicalDocument!.ContentHash);
        var result = await materializer.PlanAsync(accepted, identity, $"effective-native-{version}");
        result.IsPlanned.Should().BeTrue(string.Join(", ", result.Diagnostics.Select(item => item.Code)));
        return result;
    }

    private static RouteDeclarations AuthorizationDefinition(string id) => new()
    {
        Authorization = new DeclarationReference<NamedAuthorizationPolicy> { Definition = new DefinitionId(id) }
    };

    private static RouteDeclarations InlineCors() => new()
    {
        Cors = new DeclarationReference<CorsPolicyBinding> { Inline = new CorsPolicyBinding("cors-policy") }
    };

    private static GatewayEffectiveContribution Contribution(int order) => new(
        GatewayContributionSourceKind.Inline,
        GatewayContributionScope.RouteLocal,
        GatewayContributionDisposition.Selected,
        "routes/a",
        null,
        order,
        new ContentHash("sha-256", new string('b', 64)));

    private static GatewayEffectiveRecord Record(string target, ImmutableArray<GatewayEffectiveContribution> contributions) => new(
        1,
        GatewayEffectiveTargetKind.Route,
        target,
        GatewayEffectiveFamilies.Authorization,
        GatewayEffectiveComposition.ReplaceMoreSpecific,
        contributions,
        new GatewayNativeProjection("ASP.NET Core/YARP", "RouteConfig.AuthorizationPolicy", "Yarp.ReverseProxy/2.3.0"),
        "HPD.Gateway",
        "1.0.0",
        GatewayMaterializationDisposition.Materialized,
        new ContentHash("sha-256", new string('c', 64)),
        []);

    private static async Task<GatewayRuntimePlanningResult> Materialize(GatewayConfiguration configuration)
    {
        var accepted = Read(configuration);
        return await new GatewayRuntimePlanner(new AcceptingValidator())
            .PlanAsync(accepted, Identity(accepted), "effective-native");
    }

    private static GatewayCandidateReadResult Read(GatewayConfiguration configuration, HostCapabilitySnapshot? capabilities = null)
    {
        capabilities ??= HostCapabilitySnapshot.Create(new HostCapabilityRegistration
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

    private static DeclarationReference<T> Inline<T>(T value) where T : class => new() { Inline = value };

    private sealed class AllowingInspector : IGatewayRequestInspector
    {
        public ValueTask<GatewayInspectionDecision> InspectAsync(GatewayInspectionContext context, CancellationToken cancellationToken) =>
            ValueTask.FromResult(GatewayInspectionDecision.Allow());
    }

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
