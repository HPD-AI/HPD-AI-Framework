using System.Collections.Immutable;
using System.Net;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json;
using System.Threading.RateLimiting;
using FluentAssertions;
using HPD.Gateway;
using HPD.Gateway.ControlPlane;
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
            builder.EnableCoreDeclarations();
        });

        using var provider = services.BuildServiceProvider();
        var capabilities = provider.GetRequiredService<HostCapabilitySnapshot>();
        capabilities.InstalledFamilies.Should().Be(
            GatewayDeclarationFamilies.RequestTimeout |
            GatewayDeclarationFamilies.RequestTransforms |
            GatewayDeclarationFamilies.ResponseTransforms |
            GatewayDeclarationFamilies.CredentialDisposition);
        capabilities.Listeners.Should().BeEmpty();
        FluentActions.Invoking(() => captured!.EnableCoreDeclarations())
            .Should().Throw<InvalidOperationException>();
        FluentActions.Invoking(() => services.AddHpdGateway(static _ => { }))
            .Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void CoreDeclarationsEnableTheExactClosedCapabilitySet()
    {
        var services = new ServiceCollection();
        services.AddHpdGateway(static gateway => gateway.EnableCoreDeclarations());

        using var provider = services.BuildServiceProvider();
        var capabilities = provider.GetRequiredService<HostCapabilitySnapshot>();

        capabilities.InstalledFamilies.Should().Be(
            GatewayDeclarationFamilies.RequestTimeout |
            GatewayDeclarationFamilies.RequestTransforms |
            GatewayDeclarationFamilies.ResponseTransforms |
            GatewayDeclarationFamilies.CredentialDisposition);
        capabilities.RequestInspectors.Should().BeEmpty();
        capabilities.UpstreamResilienceProfiles.Should().BeEmpty();
        capabilities.OutputCacheProfiles.Should().BeEmpty();
        capabilities.DiscoveryProfiles.Should().BeEmpty();
    }

    [Fact]
    public void FailedConfigurationDoesNotPartiallyMutateServices()
    {
        var services = new ServiceCollection();
        var before = services.Count;

        FluentActions.Invoking(() => services.AddHpdGateway(builder =>
            {
                builder.EnableCoreDeclarations();
                throw new InvalidOperationException("configuration failed");
            }))
            .Should().Throw<InvalidOperationException>();

        services.Should().HaveCount(before);
        services.AddHpdGateway(static builder => builder.EnableCoreDeclarations());
    }

    [Fact]
    public void HpdHostedConfigurationIsTheOnlyListenerCapabilitySource()
    {
        var candidate = GatewayHostCandidateReader.Create(HostConfiguration()).Candidate!;
        var application = WebApplication.CreateSlimBuilder();
        var directory = Directory.CreateTempSubdirectory("hpd-gateway-composition-");
        var exactCertificate = CreateCertificate("exact.example", Path.Combine(directory.FullName, "exact.pfx"));
        var wildcardCertificate = CreateCertificate("*.example", Path.Combine(directory.FullName, "wildcard.pfx"));
        application.UseHpdGatewayHost(candidate, certificates =>
        {
            certificates.Add(
                new(new("test"), new("exact"), "v1"),
                new GatewayPfxCertificateSource { Path = exactCertificate.Path, Password = exactCertificate.Password });
            certificates.Add(
                new(new("test"), new("wildcard"), "v1"),
                new GatewayPfxCertificateSource { Path = wildcardCertificate.Path, Password = wildcardCertificate.Password });
        });
        application.Services.AddHpdGateway(static builder => builder.EnableCoreDeclarations());

        using var provider = application.Services.BuildServiceProvider();
        var capabilities = provider.GetRequiredService<HostCapabilitySnapshot>();
        capabilities.Listeners.Should().ContainSingle();
        var listener = capabilities.Listeners[new ListenerId("https")];
        listener.Role.Should().Be(ListenerRole.DataPlane);
        listener.Protocols.Should().Be(ListenerProtocols.Http1 | ListenerProtocols.Http2);
        listener.Hostnames.Should().Equal("*.example", "exact.example");
        listener.Tls.Should().BeTrue();
        directory.Delete(recursive: true);
    }

    [Fact]
    public async Task OptionalRegistriesContributeCapabilitiesFromTheExactRuntimeTransaction()
    {
        var applicationBuilder = WebApplication.CreateSlimBuilder();
        applicationBuilder.WebHost.UseUrls("http://127.0.0.1:0");
        var services = applicationBuilder.Services;
        services.AddHpdGateway(builder =>
        {
            builder.EnableCoreDeclarations();
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

        await using var application = applicationBuilder.Build();
        application.MapHpdGateway();
        await application.StartAsync();
        var provider = application.Services;
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
        services.AddHpdGateway(static builder => builder.EnableCoreDeclarations());
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
            "namespace",
            "node",
            new CandidateId("Bad"),
            "authority",
            "epoch",
            0,
            [123, 125]));
        var malformed = await activator.ActivateAsync(Request("candidate", 1, [123, 125]));

        invalidIdentity.State.Should().Be(GatewayNodeActivationState.RejectedBeforePlanning);
        invalidIdentity.Diagnostics.Should().Contain(error => error.Code == "activation.candidate-id-invalid");
        invalidIdentity.Diagnostics.Should().Contain(error => error.Code == "activation.authority-version-invalid");
        malformed.State.Should().Be(GatewayNodeActivationState.RejectedBeforePlanning);
        malformed.Diagnostics.Should().Contain(error => error.Code.StartsWith("candidate.", StringComparison.Ordinal));
        proxy.Services.GetRequiredService<IGatewayStatusReader>()
            .GetCurrent().Publication.State.Should().Be(GatewayStatusPublicationState.NotAttempted);
    }

    [Fact]
    public async Task NodeActivatorBoundsPreAdmissionCancellationAndStopping()
    {
        await using var proxy = await StartGateway();
        var activator = proxy.Services.GetRequiredService<IGatewayNodeActivator>();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        var canceled = await activator.ActivateAsync(
            Request("candidate", 1, Bytes(EmptyGatewayConfiguration())),
            cancellation.Token);
        canceled.State.Should().Be(GatewayNodeActivationState.RejectedBeforePlanning);
        canceled.Diagnostics.Should().ContainSingle(error =>
            error.Code == "activation.canceled-before-admission");

        proxy.Services.GetRequiredService<GatewayNodeActivator>().Dispose();
        var stopping = await activator.ActivateAsync(
            Request("candidate", 1, Bytes(EmptyGatewayConfiguration())));
        stopping.State.Should().Be(GatewayNodeActivationState.RejectedBeforePlanning);
        stopping.Diagnostics.Should().ContainSingle(error => error.Code == "activation.stopping");
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
            gateway.EnableCoreDeclarations();
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

    [Fact]
    public async Task NodeActivationChangesRemovesReaddsAndPreservesInflightGeneration()
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var oldBuilder = WebApplication.CreateSlimBuilder();
        oldBuilder.WebHost.UseUrls("http://127.0.0.1:0");
        await using var oldBackend = oldBuilder.Build();
        oldBackend.Map("/{**path}", async context =>
        {
            entered.TrySetResult();
            await release.Task.WaitAsync(context.RequestAborted);
            await context.Response.WriteAsync("old");
        });
        await oldBackend.StartAsync();
        await using var newBackend = await StartBackend("new");
        await using var proxy = await StartGateway();
        var activator = proxy.Services.GetRequiredService<IGatewayNodeActivator>();
        using var client = new HttpClient { BaseAddress = new Uri(Address(proxy)) };

        (await activator.ActivateAsync(Request("old", 1,
            Bytes(GatewayConfigurationFor(new Uri(Address(oldBackend))))))).IsActiveAcknowledged.Should().BeTrue();
        var inflight = client.GetStringAsync("/held");
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));

        (await activator.ActivateAsync(Request("new", 2,
            Bytes(GatewayConfigurationFor(new Uri(Address(newBackend))))))).IsActiveAcknowledged.Should().BeTrue();
        (await client.GetStringAsync("/new")).Should().Be("new:/new");
        release.TrySetResult();
        (await inflight).Should().Be("old");

        (await activator.ActivateAsync(Request("removed", 3,
            Bytes(EmptyGatewayConfiguration())))).IsActiveAcknowledged.Should().BeTrue();
        (await client.GetAsync("/removed")).StatusCode.Should().Be(HttpStatusCode.NotFound);

        (await activator.ActivateAsync(Request("readded", 4,
            Bytes(GatewayConfigurationFor(new Uri(Address(newBackend))))))).IsActiveAcknowledged.Should().BeTrue();
        (await client.GetStringAsync("/again")).Should().Be("new:/again");
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
            "namespace",
            "node",
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

    private static GatewayConfiguration EmptyGatewayConfiguration() => new()
    {
        SchemaVersion = new(1, 0),
        CanonicalizationVersion = 1,
        Definitions = new(),
        RootDefaults = new(),
        Upstreams = [],
        Routes = []
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
        builder.Services.AddHpdGateway(static gateway => gateway.EnableCoreDeclarations());
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

    private static (string Path, string Password) CreateCertificate(string dnsName, string path)
    {
        using var key = RSA.Create(2048);
        var request = new CertificateRequest(
            $"CN={dnsName}", key, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        request.CertificateExtensions.Add(new X509BasicConstraintsExtension(false, false, 0, true));
        request.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.DigitalSignature, true));
        request.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension(
            [new Oid("1.3.6.1.5.5.7.3.1")], true));
        var san = new SubjectAlternativeNameBuilder();
        san.AddDnsName(dnsName);
        request.CertificateExtensions.Add(san.Build());
        using var certificate = request.CreateSelfSigned(
            DateTimeOffset.UtcNow.AddMinutes(-1), DateTimeOffset.UtcNow.AddDays(1));
        const string password = "test-password";
        File.WriteAllBytes(path, certificate.Export(X509ContentType.Pfx, password));
        return (path, password);
    }
}
