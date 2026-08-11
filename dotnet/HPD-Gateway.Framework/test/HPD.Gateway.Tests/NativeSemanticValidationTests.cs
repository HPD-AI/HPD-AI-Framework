using System.Buffers.Binary;
using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text.Json;
using FluentAssertions;
using HPD.Gateway;
using Xunit;

namespace HPD.Gateway.Tests;

public sealed class NativeSemanticValidationTests
{
    [Fact]
    public void OnlyCoreCandidateResultExposesAcceptance()
    {
        typeof(GatewayPortableDocumentResult).GetProperty("IsAccepted").Should().BeNull();
        typeof(GatewayPortableDocumentResult).GetProperty(nameof(GatewayPortableDocumentResult.IsStructurallyValid)).Should().NotBeNull();
        typeof(GatewayCandidateReadResult).GetProperty(nameof(GatewayCandidateReadResult.IsAccepted)).Should().NotBeNull();
        typeof(GatewayCandidateReadResult).GetConstructors().Should().BeEmpty();
        typeof(GatewayConfiguration).Assembly.GetType("HPD.Gateway.GatewayConfigurationReader").Should().BeNull();
    }

    [Fact]
    public void PortableDocumentCannotBecomeAcceptedWithoutCoreGate()
    {
        var configuration = WithMatch(new HttpRouteMatch { Path = "/orders/{id" });
        var json = JsonSerializer.SerializeToUtf8Bytes(configuration, GatewayJsonSerializerContext.Default.GatewayConfiguration);

        GatewayPortableDocumentReader.Read(json).IsStructurallyValid.Should().BeTrue();
        GatewayCandidateReader.Read(json, Capabilities()).IsAccepted.Should().BeFalse();
    }

    [Fact]
    public void AuthoritativeReaderReturnsErrorsForNullDiscoverySchemesWithoutThrowing()
    {
        var valid = GatewayConfigurationTests.CreateValidConfiguration();
        var discovered = valid.Upstreams[0] with
        {
            Endpoints = new ServiceDiscoveryEndpointSource
            {
                Profile = new DiscoveryProfileId("dns"),
                Service = new ServiceDiscoveryName("orders"),
                Schemes = [],
                StaleBehavior = DiscoveryStaleBehavior.RejectActivationUntilFresh
            }
        };
        var json = JsonSerializer.Serialize(
            valid with { Upstreams = [discovered] },
            GatewayJsonSerializerContext.Default.GatewayConfiguration)
            .Replace("\"schemes\":[]", "\"schemes\":null", StringComparison.Ordinal);
        var capabilities = HostCapabilitySnapshot.Create(new HostCapabilityRegistration
        {
            InstalledFamilies = GatewayDeclarationFamilies.Authorization,
            AuthorizationPolicies = ["orders.read"],
            DiscoveryProfiles = [DiscoveryProfile()]
        });

        var action = () => GatewayCandidateReader.Read(System.Text.Encoding.UTF8.GetBytes(json), capabilities);

        action.Should().NotThrow();
        action().IsAccepted.Should().BeFalse();
        action().Errors.Should().NotBeEmpty().And.HaveCountLessThanOrEqualTo(256);
    }

    [Fact]
    public void LegacyDiscoveryWireShapeIsRejectedRatherThanAdapted()
    {
        const string legacy = """
            {"schemaVersion":{"major":1,"minor":0},"canonicalizationVersion":1,"routes":[],"upstreams":[{"id":{"value":"orders"},"endpoints":{"kind":"discovery","provider":{"value":"dns"},"service":{"value":"orders"},"staleBehavior":"rejectActivationUntilFresh"}}]}
            """;

        GatewayPortableDocumentReader.Read(System.Text.Encoding.UTF8.GetBytes(legacy)).IsStructurallyValid.Should().BeFalse();
    }

    [Theory]
    [InlineData("Orders", null)]
    [InlineData("xn--orders", null)]
    [InlineData("orders.", null)]
    [InlineData("orders_api", null)]
    [InlineData("orders%2eapi", null)]
    [InlineData("orders", "read.v1")]
    [InlineData("orders", "Read")]
    [InlineData("orders", "_grpc")]
    public void ServiceDiscoveryNamesUseTheClosedInjectiveGrammar(string service, string? endpoint)
    {
        GatewayConfiguration valid = GatewayConfigurationTests.CreateValidConfiguration();
        GatewayConfiguration configuration = valid with
        {
            Upstreams =
            [
                valid.Upstreams[0] with
                {
                    Endpoints = new ServiceDiscoveryEndpointSource
                    {
                        Profile = new DiscoveryProfileId("dns"),
                        Service = new ServiceDiscoveryName(service),
                        Endpoint = endpoint is null ? null : new ServiceDiscoveryEndpointName(endpoint),
                        Schemes = [ServiceDiscoveryScheme.Http],
                        StaleBehavior = DiscoveryStaleBehavior.RejectActivationUntilFresh,
                    },
                },
            ],
        };

        GatewayConfigurationValidator.Validate(configuration).IsValid.Should().BeFalse();
    }

    [Fact]
    public void ServiceDiscoverySchemeOrderParticipatesInCanonicalIdentity()
    {
        GatewayConfiguration valid = GatewayConfigurationTests.CreateValidConfiguration();
        ServiceDiscoveryEndpointSource endpoints = new()
        {
            Profile = new DiscoveryProfileId("dns"),
            Service = new ServiceDiscoveryName("orders.api"),
            Schemes = [ServiceDiscoveryScheme.Https, ServiceDiscoveryScheme.Http],
            StaleBehavior = DiscoveryStaleBehavior.RejectActivationUntilFresh,
        };
        GatewayConfiguration first = valid with { Upstreams = [valid.Upstreams[0] with { Endpoints = endpoints }] };
        GatewayConfiguration second = valid with
        {
            Upstreams = [valid.Upstreams[0] with { Endpoints = endpoints with { Schemes = [ServiceDiscoveryScheme.Http, ServiceDiscoveryScheme.Https] } }],
        };

        GatewayConfigurationCanonicalizer.TryCanonicalize(first).Document!.ContentHash.Should()
            .NotBe(GatewayConfigurationCanonicalizer.TryCanonicalize(second).Document!.ContentHash);
    }

    [Fact]
    public void TypedServiceDiscoveryGraphRoundTripsThroughTheAuthoritativeReader()
    {
        GatewayConfiguration valid = GatewayConfigurationTests.CreateValidConfiguration();
        GatewayConfiguration configuration = valid with
        {
            Upstreams =
            [
                valid.Upstreams[0] with
                {
                    Endpoints = new ServiceDiscoveryEndpointSource
                    {
                        Profile = new DiscoveryProfileId("dns"),
                        Service = new ServiceDiscoveryName("orders.api"),
                        Endpoint = new ServiceDiscoveryEndpointName("grpc"),
                        Schemes = [ServiceDiscoveryScheme.Https, ServiceDiscoveryScheme.Http],
                        StaleBehavior = DiscoveryStaleBehavior.PermitLastKnownMembership,
                    },
                    Transport = valid.Upstreams[0].Transport with
                    {
                        Tls = new UpstreamTlsDeclaration { ServerName = "orders.api" },
                    },
                },
            ],
        };
        HostCapabilitySnapshot capabilities = HostCapabilitySnapshot.Create(new HostCapabilityRegistration
        {
            InstalledFamilies = GatewayDeclarationFamilies.Authorization,
            AuthorizationPolicies = ["orders.read"],
            DiscoveryProfiles =
            [
                DiscoveryProfile([ServiceDiscoveryScheme.Https, ServiceDiscoveryScheme.Http]) with
                {
                    StaleBehaviors = [DiscoveryStaleBehavior.PermitLastKnownMembership],
                },
            ],
        });

        byte[] json = JsonSerializer.SerializeToUtf8Bytes(configuration, GatewayJsonSerializerContext.Default.GatewayConfiguration);
        GatewayCandidateReadResult result = GatewayCandidateReader.Read(json, capabilities);

        result.IsAccepted.Should().BeTrue();
        ServiceDiscoveryEndpointSource roundTripped = result.Configuration!.Upstreams[0].Endpoints
            .Should().BeOfType<ServiceDiscoveryEndpointSource>().Subject;
        roundTripped.Profile.Should().Be(new DiscoveryProfileId("dns"));
        roundTripped.Service.Should().Be(new ServiceDiscoveryName("orders.api"));
        roundTripped.Endpoint.Should().Be(new ServiceDiscoveryEndpointName("grpc"));
        roundTripped.Schemes.Should().Equal(ServiceDiscoveryScheme.Https, ServiceDiscoveryScheme.Http);
        roundTripped.StaleBehavior.Should().Be(DiscoveryStaleBehavior.PermitLastKnownMembership);
    }

    [Theory]
    [InlineData("Orders.API")]
    [InlineData("orders_api")]
    [InlineData("https://orders.api")]
    [InlineData("127.0.0.1")]
    [InlineData("orders%2eapi")]
    [InlineData("xn--orders.api")]
    public void HttpsServiceDiscoveryRejectsNoncanonicalTlsServerNames(string serverName)
    {
        GatewayConfiguration valid = GatewayConfigurationTests.CreateValidConfiguration();
        GatewayConfiguration configuration = valid with
        {
            Upstreams =
            [
                valid.Upstreams[0] with
                {
                    Endpoints = new ServiceDiscoveryEndpointSource
                    {
                        Profile = new DiscoveryProfileId("dns"),
                        Service = new ServiceDiscoveryName("orders.api"),
                        Schemes = [ServiceDiscoveryScheme.Https],
                        StaleBehavior = DiscoveryStaleBehavior.RejectActivationUntilFresh,
                    },
                    Transport = valid.Upstreams[0].Transport with
                    {
                        Tls = new UpstreamTlsDeclaration { ServerName = serverName },
                    },
                },
            ],
        };
        HostCapabilitySnapshot capabilities = HostCapabilitySnapshot.Create(new HostCapabilityRegistration
        {
            InstalledFamilies = GatewayDeclarationFamilies.Authorization,
            AuthorizationPolicies = ["orders.read"],
            DiscoveryProfiles = [DiscoveryProfile([ServiceDiscoveryScheme.Https])],
        });

        GatewayCandidateValidator.Validate(configuration, capabilities).Errors.Should()
            .Contain(error => error.Path == "upstreams[0].transport.tls.serverName");
    }

    [Fact]
    public void HttpsServiceDiscoveryRejectsOversizedTlsServerName()
    {
        string serverName = string.Join('.', Enumerable.Repeat(new string('a', 63), 4));
        HttpsServiceDiscoveryRejectsNoncanonicalTlsServerNames(serverName);
    }

    [Fact]
    public void InvalidMethodTokensAreRejected()
    {
        var configuration = WithMatch(new HttpRouteMatch { Path = "/orders", Methods = ["G ET"] });
        GatewayConfigurationValidator.Validate(configuration).IsValid.Should().BeFalse();
    }

    [Theory]
    [InlineData("BREW")]
    [InlineData("PROPFIND")]
    public void ValidExtensionMethodsAreAccepted(string method)
    {
        var configuration = WithMatch(new HttpRouteMatch { Path = "/orders", Methods = [method] });
        GatewayConfigurationValidator.Validate(configuration).IsValid.Should().BeTrue();
    }

    [Fact]
    public void MethodsAndHostsRejectCaseInsensitiveDuplicates()
    {
        var configuration = WithMatch(new HttpRouteMatch
        {
            Path = "/orders",
            Methods = ["GET", "get"],
            Hosts = ["API.Example.com", "api.example.com"]
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
            Path = "/orders",
            Hosts = ["bad host"],
            Headers = [predicate, predicate],
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
        var route = valid.Routes[0] with
        {
            Declarations = new RouteDeclarations
            {
                RequestTransforms = new OrderedRequestTransforms { Headers = [new RequestHeaderTransform { Kind = HeaderTransformKind.Set, Name = name, Value = value }] }
            }
        };
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
        var result = GatewayCandidateValidator.Validate(valid with { Upstreams = [upstream] }, HostCapabilitySnapshot.Create(new HostCapabilityRegistration()));
        result.Errors.Should().Contain(error => error.Path.Contains("transport.tls", StringComparison.Ordinal));
        result.Errors.Should().Contain(error => error.Path.Contains("authorization", StringComparison.Ordinal));
    }

    [Fact]
    public void ListenerAttachmentRejectsManagementAndHostnameExpansion()
    {
        var valid = WithMatch(new HttpRouteMatch { Path = "/orders", Hosts = ["outside.example.com"] });
        var route = valid.Routes[0] with { Listener = new ListenerId("management") };
        var capabilities = HostCapabilitySnapshot.Create(new HostCapabilityRegistration
        {
            InstalledFamilies = GatewayDeclarationFamilies.Authorization,
            AuthorizationPolicies = ["orders.read"],
            Listeners = [new ListenerCapability(new ListenerId("management"), ListenerRole.Management, ListenerProtocols.Http1, ["api.example.com"], true)]
        });

        var result = GatewayCandidateValidator.Validate(valid with { Routes = [route] }, capabilities);

        result.Errors.Should().Contain(error => error.Message.Contains("management", StringComparison.Ordinal));
        result.Errors.Should().Contain(error => error.Message.Contains("outside", StringComparison.Ordinal));
    }

    [Fact]
    public void HostRestrictedListenerRejectsHostlessRoute()
    {
        var valid = WithMatch(new HttpRouteMatch { Path = "/orders" });
        var route = valid.Routes[0] with { Listener = new ListenerId("public") };
        var capabilities = HostCapabilitySnapshot.Create(new HostCapabilityRegistration
        {
            InstalledFamilies = GatewayDeclarationFamilies.Authorization,
            AuthorizationPolicies = ["orders.read"],
            Listeners = [new ListenerCapability(new ListenerId("public"), ListenerRole.DataPlane, ListenerProtocols.Http2, ["api.example.com"], true)]
        });

        GatewayCandidateValidator.Validate(valid with { Routes = [route] }, capabilities).Errors
            .Should().Contain(error => error.Message.Contains("hostless", StringComparison.Ordinal));
    }

    [Fact]
    public void DiscoveryProfileAndSchemeSelectionResolveWithoutTls()
    {
        var valid = GatewayConfigurationTests.CreateValidConfiguration();
        var discovered = valid.Upstreams[0] with
        {
            Endpoints = new ServiceDiscoveryEndpointSource
            {
                Profile = new DiscoveryProfileId("dns"),
                Service = new ServiceDiscoveryName("orders"),
                Schemes = [ServiceDiscoveryScheme.Https],
                StaleBehavior = DiscoveryStaleBehavior.RejectActivationUntilFresh
            }
        };
        var capabilities = HostCapabilitySnapshot.Create(new HostCapabilityRegistration
        {
            InstalledFamilies = GatewayDeclarationFamilies.Authorization,
            AuthorizationPolicies = ["orders.read"],
            DiscoveryProfiles = [DiscoveryProfile(schemes: [ServiceDiscoveryScheme.Http])]
        });

        var errors = GatewayCandidateValidator.Validate(valid with { Upstreams = [discovered] }, capabilities).Errors;
        errors.Should().Contain(error => error.Message.Contains("scheme", StringComparison.OrdinalIgnoreCase));
        errors.Should().Contain(error => error.Message.Contains("TLS", StringComparison.Ordinal));

        var absent = HostCapabilitySnapshot.Create(new HostCapabilityRegistration
        {
            InstalledFamilies = GatewayDeclarationFamilies.Authorization,
            AuthorizationPolicies = ["orders.read"]
        });
        GatewayCandidateValidator.Validate(valid with { Upstreams = [discovered] }, absent).Errors
            .Should().Contain(error => error.Message.Contains("not installed", StringComparison.Ordinal));
    }

    [Fact]
    public void SecretProvidersAndInstalledFamiliesMustResolve()
    {
        var valid = GatewayConfigurationTests.CreateValidConfiguration();
        var upstream = valid.Upstreams[0] with
        {
            Transport = new UpstreamTransportDeclaration
            {
                Tls = new UpstreamTlsDeclaration
                {
                    ServerName = "orders.internal",
                    ClientCertificate = new SecretReference(new ProviderId("vault"), new ProviderObjectId("client"))
                }
            }
        };
        var route = valid.Routes[0] with
        {
            Declarations = valid.Routes[0].Declarations! with
            {
                Inspection = new DeclarationReference<RequestInspectionBinding> { Inline = new RequestInspectionBinding { InspectorName = "inspector", Mode = RequestInspectionMode.BoundedPrefix, MaximumAcceptedBodyBytes = 10, MaximumInspectedBytes = 10 } }
            }
        };
        var capabilities = HostCapabilitySnapshot.Create(new HostCapabilityRegistration
        {
            InstalledFamilies = GatewayDeclarationFamilies.Authorization,
            AuthorizationPolicies = ["orders.read"]
        });

        var errors = GatewayCandidateValidator.Validate(valid with { Routes = [route], Upstreams = [upstream] }, capabilities).Errors;
        errors.Should().Contain(error => error.Path.Contains("clientCertificate.provider", StringComparison.Ordinal));
        errors.Should().Contain(error => error.Path == "inspection");
    }

    [Fact]
    public void InspectionRequiresNamedInspectorAndHostSpillCapability()
    {
        var valid = GatewayConfigurationTests.CreateValidConfiguration();
        var inspection = new RequestInspectionBinding
        {
            InspectorName = "content-check",
            Mode = RequestInspectionMode.CompleteBody,
            MaximumAcceptedBodyBytes = 4096,
            MemoryThresholdBytes = 1024,
            SpillPolicy = RequestInspectionSpillPolicy.Allowed
        };
        var configuration = valid with
        {
            Routes =
            [
                valid.Routes[0] with
                {
                    Declarations = valid.Routes[0].Declarations! with
                    {
                        Inspection = new DeclarationReference<RequestInspectionBinding> { Inline = inspection }
                    }
                }
            ]
        };
        var without = HostCapabilitySnapshot.Create(new HostCapabilityRegistration
        {
            InstalledFamilies = GatewayDeclarationFamilies.Authorization | GatewayDeclarationFamilies.Inspection,
            AuthorizationPolicies = ["orders.read"]
        });
        var with = HostCapabilitySnapshot.Create(new HostCapabilityRegistration
        {
            InstalledFamilies = GatewayDeclarationFamilies.Authorization | GatewayDeclarationFamilies.Inspection,
            AuthorizationPolicies = ["orders.read"],
            RequestInspectors = ["content-check"],
            AllowInspectionFileSpill = true
        });

        GatewayCandidateValidator.Validate(configuration, without).Errors.Should().Contain(error =>
            error.Path.EndsWith("inspectorName", StringComparison.Ordinal) || error.Path.EndsWith("spillPolicy", StringComparison.Ordinal));
        GatewayCandidateValidator.Validate(configuration, with).IsValid.Should().BeTrue();
    }

    [Fact]
    public void CapabilitySnapshotFactoryRejectsInvalidRegistrationsAndExposesGetOnlyState()
    {
        var action = () => HostCapabilitySnapshot.Create(new HostCapabilityRegistration
        {
            AuthorizationPolicies = null!
        });
        action.Should().Throw<ArgumentException>();
        typeof(HostCapabilitySnapshot).GetConstructors().Should().BeEmpty();
        typeof(HostCapabilitySnapshot).GetProperties().Should().OnlyContain(property => !property.CanWrite);
    }

    [Fact]
    public void CapabilitySnapshotEnumeratesRegistrationSequencesExactlyOnce()
    {
        var enumerations = 0;
        IEnumerable<string> Once()
        {
            if (++enumerations > 1) throw new InvalidOperationException("Sequence was enumerated twice.");
            yield return "orders.read";
        }

        var snapshot = HostCapabilitySnapshot.Create(new HostCapabilityRegistration
        {
            InstalledFamilies = GatewayDeclarationFamilies.Authorization,
            AuthorizationPolicies = Once()
        });

        snapshot.AuthorizationPolicies.Should().ContainSingle("orders.read");
        enumerations.Should().Be(1);
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

    private static HostCapabilitySnapshot Capabilities() => HostCapabilitySnapshot.Create(new HostCapabilityRegistration
    {
        InstalledFamilies = GatewayDeclarationFamilies.Authorization,
        AuthorizationPolicies = ["orders.read"]
    });

    private static DiscoveryProfileCapability DiscoveryProfile(
        ImmutableArray<ServiceDiscoveryScheme> schemes = default) => new(
        new DiscoveryProfileId("dns"),
        1,
        DiscoveryRuntimeKind.Microsoft,
        [DiscoveryProviderKind.Configuration],
        schemes.IsDefault ? [ServiceDiscoveryScheme.Http] : schemes,
        [DiscoveryStaleBehavior.RejectActivationUntilFresh],
        256,
        true,
        true,
        true,
        !schemes.IsDefault && schemes.Contains(ServiceDiscoveryScheme.Https),
        new ContentHash("sha-256", new string('a', 64)));
}
