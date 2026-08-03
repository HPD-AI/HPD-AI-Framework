using System.Collections.Immutable;
using System.Net;
using System.Text.Json;
using System.Threading.RateLimiting;
using FluentAssertions;
using HPD.Gateway.Abstractions;
using HPD.Gateway.Abstractions.Serialization;
using HPD.Gateway.Core;
using HPD.Gateway.Hosting;
using HPD.Gateway.Inspection;
using HPD.Gateway.OutputCaching;
using HPD.Gateway.Resilience;
using HPD.Gateway.Status;
using HPD.Gateway.Yarp;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace HPD.Gateway.Tests;

public sealed class GatewayCompositionTests
{
    [Fact]
    public void AddHpdGatewayTransactionSealsCoreCapabilitiesAndRejectsLateMutation()
    {
        var services = new ServiceCollection();
        GatewayBuilder? captured = null;

        services.AddHpdGateway(builder =>
        {
            captured = builder;
            builder.AddCoreFamilies();
        });

        using var provider = services.BuildServiceProvider();
        var capabilities = provider.GetRequiredService<HostCapabilitySnapshot>();
        capabilities.InstalledFamilies.Should().Be(
            GatewayDeclarationFamilies.RequestTimeout |
            GatewayDeclarationFamilies.RequestTransforms |
            GatewayDeclarationFamilies.ResponseTransforms |
            GatewayDeclarationFamilies.CredentialDisposition);
        capabilities.Listeners.Should().BeEmpty();
        FluentActions.Invoking(() => captured!.AddCoreFamilies())
            .Should().Throw<InvalidOperationException>();
        FluentActions.Invoking(() => services.AddHpdGateway(static _ => { }))
            .Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void FailedConfigurationDoesNotPartiallyMutateServices()
    {
        var services = new ServiceCollection();
        var before = services.Count;

        FluentActions.Invoking(() => services.AddHpdGateway(builder =>
            {
                builder.AddCoreFamilies();
                throw new InvalidOperationException("configuration failed");
            }))
            .Should().Throw<InvalidOperationException>();

        services.Should().HaveCount(before);
        services.AddHpdGateway(static builder => builder.AddCoreFamilies());
    }

    [Fact]
    public void HpdHostedConfigurationIsTheOnlyListenerCapabilitySource()
    {
        var candidate = GatewayHostCandidateReader.Create(HostConfiguration()).Candidate!;
        var services = new ServiceCollection();
        services.AddSingleton(new GatewayHostRuntimeStatus(candidate));
        services.AddHpdGateway(static builder => builder.AddCoreFamilies());

        using var provider = services.BuildServiceProvider();
        var capabilities = provider.GetRequiredService<HostCapabilitySnapshot>();
        capabilities.Listeners.Should().ContainSingle();
        var listener = capabilities.Listeners[new ListenerId("https")];
        listener.Role.Should().Be(ListenerRole.DataPlane);
        listener.Protocols.Should().Be(ListenerProtocols.Http1 | ListenerProtocols.Http2);
        listener.Hostnames.Should().Equal("*.example", "exact.example");
        listener.Tls.Should().BeTrue();
    }

    [Fact]
    public void OptionalRegistriesContributeCapabilitiesFromTheExactRuntimeTransaction()
    {
        var services = new ServiceCollection();
        services.AddHpdGateway(builder =>
        {
            builder.AddCoreFamilies();
            builder.AddRequestInspection(registry => registry.Add("inspector", new AllowInspector()), allowFileSpill: true);
            builder.ProtectCredentialHeaders("X-Api-Key");
            builder.AddAuthorizationPolicy("auth", policy => policy.RequireAssertion(_ => true));
            builder.AddCorsPolicy("cors", policy => policy.AllowAnyOrigin());
            builder.AddTrafficAdmissionPolicy("admission", static _ =>
                RateLimitPartition.GetNoLimiter("global"));
            builder.AddRequestTimeoutPolicy("timeout", TimeSpan.FromSeconds(5));
            builder.AddUpstreamResilience(registry => registry.Add(new GatewayResilienceProfile
            {
                Name = "safe",
                Version = 3,
                Retry = new GatewayResponseRetryProfile
                {
                    StatusCodes = [HttpStatusCode.ServiceUnavailable],
                    MaximumRetryAttempts = 2
                }
            }));
            builder.AddOutputCaching(registry => registry.Add(new GatewayOutputCacheProfile
            {
                Name = "cache",
                Version = 4,
                Expiration = TimeSpan.FromMinutes(2)
            }));
        });

        using var provider = services.BuildServiceProvider();
        var capabilities = provider.GetRequiredService<HostCapabilitySnapshot>();
        capabilities.InstalledFamilies.Should().HaveFlag(GatewayDeclarationFamilies.Inspection);
        capabilities.InstalledFamilies.Should().HaveFlag(GatewayDeclarationFamilies.UpstreamResilience);
        capabilities.InstalledFamilies.Should().HaveFlag(GatewayDeclarationFamilies.OutputCache);
        capabilities.RequestInspectors.Should().ContainSingle().Which.Should().Be("inspector");
        capabilities.AllowInspectionFileSpill.Should().BeTrue();
        capabilities.ProtectedCredentialHeaders.Should().Contain("x-api-key");
        capabilities.AuthorizationPolicies.Should().ContainSingle("auth");
        capabilities.CorsPolicies.Should().ContainSingle("cors");
        capabilities.TrafficAdmissionPolicies.Should().ContainSingle("admission");
        capabilities.RequestTimeoutPolicies.Should().ContainSingle("timeout");
        capabilities.UpstreamResilienceProfiles["safe"].Version.Should().Be(3);
        capabilities.OutputCacheProfiles["cache"].Version.Should().Be(4);
        provider.GetHpdGatewayResilienceCapabilities()
            .Should().BeEquivalentTo(capabilities.UpstreamResilienceProfiles.Values);
        provider.GetHpdGatewayOutputCacheCapabilities()
            .Should().BeEquivalentTo(capabilities.OutputCacheProfiles.Values);
    }

    [Fact]
    public void EmbeddedCompositionRejectsListenerReferencesBecauseItAdvertisesNone()
    {
        var services = new ServiceCollection();
        services.AddHpdGateway(static builder => builder.AddCoreFamilies());
        using var provider = services.BuildServiceProvider();
        var result = GatewayCandidateValidator.Validate(
            GatewayConfigurationWithListener(),
            provider.GetRequiredService<HostCapabilitySnapshot>());

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(error =>
            error.Code == GatewayValidationErrorCode.UnresolvedReference &&
            error.Path == "routes[0].listener");
    }

    [Fact]
    public async Task NodeActivatorUsesAuthoritativeReaderAndPreservesPublisherIdentitySemantics()
    {
        await using var backend = await StartBackend("A");
        await using var proxy = await StartGateway();
        var activator = proxy.Services.GetRequiredService<IGatewayNodeActivator>();
        var firstBytes = Bytes(GatewayConfigurationFor(new Uri(Address(backend))));
        var first = Request("candidate-a", 2, firstBytes);

        var active = await activator.ActivateAsync(first);
        var duplicate = await activator.ActivateAsync(first);
        var conflict = await activator.ActivateAsync(Request(
            "candidate-b",
            2,
            Bytes(GatewayConfigurationFor(new Uri(Address(backend)), "/changed/{**catchall}"))));
        var stale = await activator.ActivateAsync(Request("candidate-stale", 1, firstBytes));

        active.IsActiveAcknowledged.Should().BeTrue();
        duplicate.Publication!.State.Should().Be(GatewayPublicationState.Duplicate);
        conflict.Publication!.State.Should().Be(GatewayPublicationState.IdentityConflict);
        stale.Publication!.State.Should().Be(GatewayPublicationState.Stale);
        using var client = new HttpClient { BaseAddress = new Uri(Address(proxy)) };
        (await client.GetStringAsync("/hello")).Should().Be("A:/hello");
    }

    [Fact]
    public async Task NodeActivatorRejectsInvalidIdentityAndMalformedUtf8BeforePublication()
    {
        await using var proxy = await StartGateway();
        var activator = proxy.Services.GetRequiredService<IGatewayNodeActivator>();
        var invalidIdentity = await activator.ActivateAsync(new GatewayNodeActivationRequest(
            new CandidateId("Bad"),
            "authority",
            "epoch",
            0,
            [123, 125]));
        var malformed = await activator.ActivateAsync(Request("candidate", 1, [123, 125]));

        invalidIdentity.State.Should().Be(GatewayNodeActivationState.RejectedBeforeMaterialization);
        invalidIdentity.Diagnostics.Should().Contain(error => error.Code == "activation.candidate-id-invalid");
        invalidIdentity.Diagnostics.Should().Contain(error => error.Code == "activation.authority-version-invalid");
        malformed.State.Should().Be(GatewayNodeActivationState.RejectedBeforeMaterialization);
        malformed.Diagnostics.Should().Contain(error => error.Code.StartsWith("candidate.", StringComparison.Ordinal));
        proxy.Services.GetRequiredService<IGatewayStatusReader>()
            .GetCurrent().Publication.State.Should().Be(GatewayStatusPublicationState.NotAttempted);
    }

    [Fact]
    public async Task InitialCandidateActivatesDuringStartupAndCoherentMappingIsExactlyOnce()
    {
        await using var backend = await StartBackend("initial");
        var request = Request(
            "initial",
            1,
            Bytes(GatewayConfigurationFor(new Uri(Address(backend)))));
        var builder = WebApplication.CreateSlimBuilder();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Services.AddHpdGateway(gateway =>
        {
            gateway.AddCoreFamilies();
            gateway.UseInitialCandidate(request);
        });
        await using var application = builder.Build();
        application.MapHpdGateway();
        FluentActions.Invoking(() => application.MapHpdGateway())
            .Should().Throw<InvalidOperationException>();

        await application.StartAsync();
        using var client = new HttpClient { BaseAddress = new Uri(Address(application)) };
        (await client.GetStringAsync("/started")).Should().Be("initial:/started");
        application.Services.GetRequiredService<IGatewayStatusReader>()
            .GetCurrent().Publication.State.Should().Be(GatewayStatusPublicationState.ActiveAcknowledged);
    }

    private static GatewayHostConfiguration HostConfiguration() => new()
    {
        SchemaVersion = new(1, 0),
        CanonicalizationVersion = 1,
        HostId = new("host"),
        DataListeners =
        [
            new GatewayHttpsListenerDeclaration
            {
                Id = new ListenerId("https"),
                Binding = GatewayListenerBindingKind.Loopback,
                Port = 443,
                Protocols = GatewayListenerProtocols.Http1 | GatewayListenerProtocols.Http2,
                Tls = new GatewayInboundTlsDeclaration
                {
                    Sni =
                    [
                        new GatewaySniTlsDeclaration
                        {
                            HostnamePattern = "exact.example",
                            Certificate = new(new ProviderId("test"), new ProviderObjectId("exact"), "v1")
                        },
                        new GatewaySniTlsDeclaration
                        {
                            HostnamePattern = "*.example",
                            Certificate = new(new ProviderId("test"), new ProviderObjectId("wildcard"), "v1")
                        }
                    ]
                }
            }
        ]
    };

    private static GatewayConfiguration GatewayConfigurationWithListener() => new()
    {
        SchemaVersion = new(1, 0),
        CanonicalizationVersion = 1,
        Definitions = new GatewayDefinitions(),
        RootDefaults = new GatewayRootDeclarations(),
        Upstreams =
        [
            new UpstreamDeclaration
            {
                Id = new UpstreamId("backend"),
                Endpoints = new StaticEndpointSource
                {
                    Destinations =
                    [
                        new DestinationDeclaration
                        {
                            Id = new DestinationId("one"),
                            Address = new Uri("http://127.0.0.1:5001")
                        }
                    ]
                }
            }
        ],
        Routes =
        [
            new RouteDeclaration
            {
                Id = new RouteId("route"),
                Upstream = new UpstreamId("backend"),
                Listener = new ListenerId("https"),
                Match = new HttpRouteMatch
                {
                    Path = "/{**catchall}"
                },
                Declarations = new RouteDeclarations()
            }
        ]
    };

    private static GatewayNodeActivationRequest Request(
        string candidate,
        ulong version,
        ImmutableArray<byte> utf8) => new(
            new CandidateId(candidate),
            "authority",
            "epoch",
            version,
            utf8);

    private static ImmutableArray<byte> Bytes(GatewayConfiguration configuration) =>
        ImmutableArray.CreateRange(JsonSerializer.SerializeToUtf8Bytes(
            configuration,
            GatewayJsonSerializerContext.Default.GatewayConfiguration));

    private static GatewayConfiguration GatewayConfigurationFor(Uri backend, string path = "/{**catchall}") => new()
    {
        SchemaVersion = new(1, 0),
        CanonicalizationVersion = 1,
        Definitions = new GatewayDefinitions(),
        RootDefaults = new GatewayRootDeclarations(),
        Upstreams =
        [
            new UpstreamDeclaration
            {
                Id = new UpstreamId("backend"),
                Endpoints = new StaticEndpointSource
                {
                    Destinations = [new DestinationDeclaration { Id = new DestinationId("one"), Address = backend }]
                },
                Transport = new UpstreamTransportDeclaration { UseProxy = false }
            }
        ],
        Routes =
        [
            new RouteDeclaration
            {
                Id = new RouteId("route"),
                Upstream = new UpstreamId("backend"),
                Match = new HttpRouteMatch { Path = path },
                Declarations = new RouteDeclarations()
            }
        ]
    };

    private static async Task<WebApplication> StartBackend(string name)
    {
        var builder = WebApplication.CreateSlimBuilder();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        var app = builder.Build();
        app.Map("/{**path}", (HttpContext context) => $"{name}:{context.Request.Path}");
        await app.StartAsync();
        return app;
    }

    private static async Task<WebApplication> StartGateway()
    {
        var builder = WebApplication.CreateSlimBuilder();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Services.AddHpdGateway(static gateway => gateway.AddCoreFamilies());
        var app = builder.Build();
        app.MapHpdGateway();
        await app.StartAsync();
        return app;
    }

    private static string Address(WebApplication application) => application.Services
        .GetRequiredService<IServer>()
        .Features.Get<IServerAddressesFeature>()!
        .Addresses.Single();

    private sealed class AllowInspector : IGatewayRequestInspector
    {
        public ValueTask<GatewayInspectionDecision> InspectAsync(
            GatewayInspectionContext context,
            CancellationToken cancellationToken) =>
            ValueTask.FromResult(GatewayInspectionDecision.Allow());
    }
}
