using System.Net;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using HPD.Gateway;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace HPD.Gateway.Tests;

public sealed class BoundedInspectionTests
{
    [Fact]
    public async Task PrefixInspectsOnlyPrefixAndReplaysCompleteBody()
    {
        var inspector = new RecordingInspector();
        await using var fixture = await InspectionFixture.Start(inspector, Prefix(maximumAccepted: 32, maximumInspected: 3));

        using var response = await fixture.Client.PostAsync("/upload", new StringContent("abcdefgh", Encoding.UTF8, "text/plain"));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await response.Content.ReadAsStringAsync()).Should().Be("abcdefgh");
        inspector.Body.Should().Be("abc");
        inspector.Completeness.Should().Be(GatewayInspectionCompleteness.PrefixOnly);
        fixture.BackendHits.Should().Be(1);
    }

    [Fact]
    public async Task PrefixRejectsKnownOversizeAndUnknownLengthBeforeUpstream()
    {
        var inspector = new RecordingInspector();
        await using var fixture = await InspectionFixture.Start(inspector, Prefix(maximumAccepted: 4, maximumInspected: 2));

        using var oversize = await fixture.Client.PostAsync("/upload", new StringContent("12345"));
        using var unknownContent = new StreamContent(new NonSeekableReadStream(Encoding.UTF8.GetBytes("123")));
        using var unknown = await fixture.Client.PostAsync("/upload", unknownContent);

        oversize.StatusCode.Should().Be(HttpStatusCode.RequestEntityTooLarge);
        unknown.StatusCode.Should().Be(HttpStatusCode.LengthRequired);
        fixture.BackendHits.Should().Be(0);
    }

    [Fact]
    public async Task CompleteAcceptsUnknownLengthAndRejectsStreamedLimitBeforeUpstream()
    {
        var accepting = new RecordingInspector();
        await using var accepted = await InspectionFixture.Start(accepting, Complete(maximumAccepted: 8, threshold: 8));
        using var acceptedContent = new StreamContent(new NonSeekableReadStream(Encoding.UTF8.GetBytes("abcdef")));
        using var acceptedResponse = await accepted.Client.PostAsync("/upload", acceptedContent);

        acceptedResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        (await acceptedResponse.Content.ReadAsStringAsync()).Should().Be("abcdef");
        accepting.Body.Should().Be("abcdef");
        accepting.Completeness.Should().Be(GatewayInspectionCompleteness.CompleteBody);

        var rejecting = new RecordingInspector();
        await using var exceeded = await InspectionFixture.Start(rejecting, Complete(maximumAccepted: 4, threshold: 4));
        using var exceededContent = new StreamContent(new NonSeekableReadStream(Encoding.UTF8.GetBytes("12345")));
        using var exceededResponse = await exceeded.Client.PostAsync("/upload", exceededContent);

        exceededResponse.StatusCode.Should().Be(HttpStatusCode.RequestEntityTooLarge);
        exceeded.BackendHits.Should().Be(0);
        rejecting.Calls.Should().Be(0);
    }

    [Fact]
    public async Task CompleteRejectsLargeUnknownLengthChunkAsPayloadTooLarge()
    {
        var inspector = new RecordingInspector();
        await using var fixture = await InspectionFixture.Start(inspector, Complete(maximumAccepted: 4, threshold: 4));
        using var content = new StreamContent(new NonSeekableReadStream(new byte[64 * 1024]));

        using var response = await fixture.Client.PostAsync("/upload", content);

        response.StatusCode.Should().Be(HttpStatusCode.RequestEntityTooLarge);
        fixture.BackendHits.Should().Be(0);
        inspector.Calls.Should().Be(0);
    }

    [Fact]
    public async Task InspectorDenialAndExceptionFailBeforeUpstream()
    {
        await using var denied = await InspectionFixture.Start(new RecordingInspector(GatewayInspectionDecision.Reject("content-denied")), Prefix(16, 4));
        using var deniedResponse = await denied.Client.PostAsync("/upload", new StringContent("body"));
        deniedResponse.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        denied.BackendHits.Should().Be(0);

        var throwing = new ThrowingInspector();
        await using var failed = await InspectionFixture.Start(throwing, Complete(16, 1, RequestInspectionSpillPolicy.Allowed), allowSpill: true);
        using var failedResponse = await failed.Client.PostAsync("/upload", new StringContent("body"));
        failedResponse.StatusCode.Should().Be(HttpStatusCode.InternalServerError);
        failed.BackendHits.Should().Be(0);
        await WaitUntilAsync(() => !File.Exists(throwing.SpillPath), TimeSpan.FromSeconds(2));
        File.Exists(throwing.SpillPath).Should().BeFalse();
    }

    [Fact]
    public async Task CompleteSpillExistsOnlyDuringRequest()
    {
        var inspector = new RecordingInspector();
        await using var fixture = await InspectionFixture.Start(inspector, Complete(32 * 1024, 128, RequestInspectionSpillPolicy.Allowed), allowSpill: true);
        using var response = await fixture.Client.PostAsync("/upload", new ByteArrayContent(new byte[8 * 1024]));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        inspector.SpillPath.Should().NotBeNull();
        inspector.SpillExistedDuringInspection.Should().BeTrue();
        await WaitUntilAsync(() => !File.Exists(inspector.SpillPath), TimeSpan.FromSeconds(2));
        File.Exists(inspector.SpillPath).Should().BeFalse();
    }

    [Theory]
    [InlineData("application/grpc")]
    [InlineData("multipart/form-data; boundary=hpd")]
    public async Task CompleteRejectsUnsupportedRepresentationsBeforeUpstream(string contentType)
    {
        var inspector = new RecordingInspector();
        await using var fixture = await InspectionFixture.Start(inspector, Complete(1024, 1024));
        using var content = new StringContent("body");
        content.Headers.ContentType = System.Net.Http.Headers.MediaTypeHeaderValue.Parse(contentType);

        using var response = await fixture.Client.PostAsync("/upload", content);

        response.StatusCode.Should().Be(HttpStatusCode.UnsupportedMediaType);
        inspector.Calls.Should().Be(0);
        fixture.BackendHits.Should().Be(0);
    }

    [Fact]
    public async Task EmptyBodyProducesClosedNoBodyOutcome()
    {
        var inspector = new RecordingInspector();
        await using var fixture = await InspectionFixture.Start(inspector, Complete(16, 16));

        using var response = await fixture.Client.PostAsync("/upload", new ByteArrayContent([]));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        inspector.Completeness.Should().Be(GatewayInspectionCompleteness.NoBody);
        inspector.Body.Should().BeEmpty();
    }

    [Fact]
    public void RegistryAndDecisionContractsRejectOpenOrDuplicateValues()
    {
        var builder = new GatewayInspectionRegistryBuilder();
        builder.Add("content-check", new RecordingInspector());

        var duplicate = () => builder.Add("content-check", new RecordingInspector());
        var invalidName = () => new GatewayInspectionRegistryBuilder().Add("Content Check", new RecordingInspector());
        var invalidReason = () => GatewayInspectionDecision.Reject("Body leaked");
        var invalidStatus = () => GatewayInspectionDecision.Reject("content-denied", 418);

        duplicate.Should().Throw<ArgumentException>();
        invalidName.Should().Throw<ArgumentException>();
        invalidReason.Should().Throw<ArgumentException>();
        invalidStatus.Should().Throw<ArgumentOutOfRangeException>();
        typeof(IGatewayInspectionFeature).GetProperties().Should().NotContain(property =>
            typeof(Stream).IsAssignableFrom(property.PropertyType) || property.PropertyType == typeof(byte[]) || property.Name.Contains("Path", StringComparison.Ordinal));
    }

    [Fact]
    public async Task CancellationBeforeInspectionNeverInvokesInspectorOrForwarder()
    {
        var inspector = new RecordingInspector();
        var registryBuilder = new GatewayInspectionRegistryBuilder();
        registryBuilder.Add("content-check", inspector);
        var executor = new GatewayInspectionExecutor(registryBuilder.Build());
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var context = new DefaultHttpContext();
        context.RequestAborted = cancellation.Token;
        context.Request.ContentLength = 4;
        context.Request.Body = new MemoryStream("body"u8.ToArray());
        var forwarded = false;

        await executor.ExecuteAsync(
            context,
            new GatewayInspectionSelection("content-check", RequestInspectionMode.BoundedPrefix, 16, 2, null, RequestInspectionSpillPolicy.Disabled),
            _ => { forwarded = true; return Task.CompletedTask; });

        forwarded.Should().BeFalse();
        inspector.Calls.Should().Be(0);
    }

    [Theory]
    [InlineData("HTTP/3", "POST")]
    [InlineData("HTTP/2", "CONNECT")]
    public async Task Http3AndConnectRejectBeforeInspectorOrForwarder(string protocol, string method)
    {
        var inspector = new RecordingInspector();
        var registryBuilder = new GatewayInspectionRegistryBuilder();
        registryBuilder.Add("content-check", inspector);
        var executor = new GatewayInspectionExecutor(registryBuilder.Build());
        var context = new DefaultHttpContext();
        context.Request.Protocol = protocol;
        context.Request.Method = method;
        context.Request.ContentLength = 0;
        var forwarded = false;

        await executor.ExecuteAsync(
            context,
            new GatewayInspectionSelection("content-check", RequestInspectionMode.BoundedPrefix, 16, 2, null, RequestInspectionSpillPolicy.Disabled),
            _ => { forwarded = true; return Task.CompletedTask; });

        context.Response.StatusCode.Should().Be(StatusCodes.Status415UnsupportedMediaType);
        forwarded.Should().BeFalse();
        inspector.Calls.Should().Be(0);
    }

    [Fact]
    public async Task NonWebSocketUpgradeRejectsBeforeInspectorOrForwarder()
    {
        var inspector = new RecordingInspector();
        var registryBuilder = new GatewayInspectionRegistryBuilder();
        registryBuilder.Add("content-check", inspector);
        var executor = new GatewayInspectionExecutor(registryBuilder.Build());
        var context = new DefaultHttpContext();
        context.Features.Set<IHttpUpgradeFeature>(new TestUpgradeFeature());
        context.Request.Protocol = "HTTP/1.1";
        context.Request.Method = "POST";
        context.Request.Headers.Connection = "Upgrade";
        context.Request.Headers.Upgrade = "h2c";
        context.Request.ContentLength = 4;
        context.Request.Body = new MemoryStream("body"u8.ToArray());
        var forwarded = false;

        await executor.ExecuteAsync(
            context,
            new GatewayInspectionSelection("content-check", RequestInspectionMode.BoundedPrefix, 16, 2, null, RequestInspectionSpillPolicy.Disabled),
            _ => { forwarded = true; return Task.CompletedTask; });

        context.Response.StatusCode.Should().Be(StatusCodes.Status415UnsupportedMediaType);
        forwarded.Should().BeFalse();
        inspector.Calls.Should().Be(0);
    }

    [Fact]
    public async Task MissingOwnedMappingFailsHostStartup()
    {
        var builder = WebApplication.CreateSlimBuilder();
        builder.Services.AddReverseProxy();
        builder.Services.AddHpdGatewayYarpPublication();
        builder.Services.AddHpdGatewayYarpMaterialization();
        builder.Services.AddHpdGatewayYarpInspection(registry => registry.Add("content-check", new RecordingInspector()));
        await using var app = builder.Build();
        app.MapReverseProxy();

        var action = () => app.StartAsync();

        await action.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task InspectionRegistrationAndOwnedMappingAreSingleInstance()
    {
        var services = new ServiceCollection();
        services.AddReverseProxy().LoadFromMemory([], []);
        services.AddHpdGatewayYarpInspection(registry => registry.Add("content-check", new RecordingInspector()));
        var duplicateRegistration = () => services.AddHpdGatewayYarpInspection(registry => registry.Add("other", new RecordingInspector()));
        duplicateRegistration.Should().Throw<InvalidOperationException>();

        var webBuilder = WebApplication.CreateSlimBuilder();
        foreach (var descriptor in services) webBuilder.Services.Add(descriptor);
        await using var app = webBuilder.Build();
        app.MapHpdGatewayReverseProxy();
        var duplicateMapping = () => app.MapHpdGatewayReverseProxy();
        duplicateMapping.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public async Task InspectionSelectionReloadsRemovesAndReaddsAtomically()
    {
        var inspector = new RecordingInspector();
        await using var fixture = await InspectionFixture.Start(inspector, Prefix(64, 2), allowSpill: true);

        using var first = await fixture.Client.PostAsync("/upload", new StringContent("first"));
        (await first.Content.ReadAsStringAsync()).Should().Be("first");
        inspector.Body.Should().Be("fi");

        await fixture.Reload(Complete(64, 16, RequestInspectionSpillPolicy.Allowed), 2);
        using var changed = await fixture.Client.PostAsync("/upload", new StringContent("changed"));
        (await changed.Content.ReadAsStringAsync()).Should().Be("changed");
        inspector.Body.Should().Be("changed");

        await fixture.Reload(null, 3);
        using var removed = await fixture.Client.PostAsync("/upload", new StringContent("removed"));
        removed.StatusCode.Should().Be(HttpStatusCode.NotFound);

        await fixture.Reload(Prefix(64, 3), 4);
        using var readded = await fixture.Client.PostAsync("/upload", new StringContent("again"));
        (await readded.Content.ReadAsStringAsync()).Should().Be("again");
        inspector.Body.Should().Be("aga");
    }

    [Fact]
    public async Task UnselectedRouteKeepsUnknownLengthTransparentStreamingPath()
    {
        var inspector = new RecordingInspector();
        await using var fixture = await InspectionFixture.Start(inspector, Prefix(64, 2));
        await fixture.ReloadUninspected(2);
        using var content = new StreamContent(new NonSeekableReadStream(Encoding.UTF8.GetBytes("streamed")));

        using var response = await fixture.Client.PostAsync("/upload", content);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await response.Content.ReadAsStringAsync()).Should().Be("streamed");
        inspector.Calls.Should().Be(0);
    }

    [Fact]
    public async Task PrefixAndCompleteExecuteOverHttp2()
    {
        var prefixInspector = new RecordingInspector();
        await using var prefix = await InspectionFixture.Start(prefixInspector, Prefix(64, 3), http2: true);
        using var prefixResponse = await prefix.Client.PostAsync("/upload", new StringContent("http2-prefix"));
        prefixResponse.Version.Should().Be(HttpVersion.Version20);
        prefixResponse.StatusCode.Should().Be(HttpStatusCode.OK, await prefixResponse.Content.ReadAsStringAsync());
        prefixInspector.Body.Should().Be("htt");

        var completeInspector = new RecordingInspector();
        await using var complete = await InspectionFixture.Start(completeInspector, Complete(64, 64), http2: true);
        using var completeResponse = await complete.Client.PostAsync("/upload", new StringContent("http2-complete"));
        completeResponse.Version.Should().Be(HttpVersion.Version20);
        completeResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        completeInspector.Body.Should().Be("http2-complete");
    }

    [Fact]
    public async Task PublishedHpdAndDirectYarpWithIdenticalInspectionMiddlewareAreEquivalent()
    {
        var hpdInspector = new RecordingInspector();
        var directInspector = new RecordingInspector();
        await using var fixture = await InspectionFixture.Start(hpdInspector, Prefix(64, 4));
        await using var direct = await fixture.StartDirectProxy(directInspector);
        using var directClient = new HttpClient { BaseAddress = new Uri(Address(direct)) };

        using var hpdResponse = await fixture.Client.PostAsync("/upload", new StringContent("equivalent"));
        using var directResponse = await directClient.PostAsync("/upload", new StringContent("equivalent"));

        directResponse.StatusCode.Should().Be(hpdResponse.StatusCode);
        (await directResponse.Content.ReadAsStringAsync()).Should().Be(await hpdResponse.Content.ReadAsStringAsync());
        directInspector.Body.Should().Be(hpdInspector.Body).And.Be("equi");
    }

    private static RequestInspectionBinding Prefix(long maximumAccepted, int maximumInspected) => new()
    {
        InspectorName = "content-check",
        Mode = RequestInspectionMode.BoundedPrefix,
        MaximumAcceptedBodyBytes = maximumAccepted,
        MaximumInspectedBytes = maximumInspected
    };

    private static RequestInspectionBinding Complete(long maximumAccepted, int threshold, RequestInspectionSpillPolicy spill = RequestInspectionSpillPolicy.Disabled) => new()
    {
        InspectorName = "content-check",
        Mode = RequestInspectionMode.CompleteBody,
        MaximumAcceptedBodyBytes = maximumAccepted,
        MemoryThresholdBytes = threshold,
        SpillPolicy = spill
    };

    private static async Task WaitUntilAsync(Func<bool> condition, TimeSpan timeout)
    {
        var stop = DateTime.UtcNow + timeout;
        while (!condition() && DateTime.UtcNow < stop) await Task.Delay(10);
    }

    private sealed class InspectionFixture : IAsyncDisposable
    {
        private readonly WebApplication _proxy;
        private readonly WebApplication _backend;
        private readonly Counter _counter;
        private readonly string _backendAddress;
        private readonly bool _allowSpill;
        private readonly bool _http2;
        private GatewayPreparedApplication? _lastBundle;
        private InspectionFixture(WebApplication proxy, WebApplication backend, Counter counter, bool allowSpill, bool http2)
        {
            _proxy = proxy;
            _backend = backend;
            _counter = counter;
            _backendAddress = Address(backend);
            _allowSpill = allowSpill;
            _http2 = http2;
            Client = new HttpClient { BaseAddress = new Uri(Address(proxy)) };
        }
        internal HttpClient Client { get; }
        internal int BackendHits => _counter.Value;

        internal static async Task<InspectionFixture> Start(IGatewayRequestInspector inspector, RequestInspectionBinding binding, bool allowSpill = false, bool http2 = false)
        {
            var counter = new Counter();
            var backendBuilder = WebApplication.CreateSlimBuilder();
            if (http2) backendBuilder.WebHost.ConfigureKestrel(options => options.Listen(IPAddress.Loopback, 0, listen => listen.Protocols = HttpProtocols.Http2));
            else backendBuilder.WebHost.UseUrls("http://127.0.0.1:0");
            var backend = backendBuilder.Build();
            backend.MapPost("/{**path}", async context =>
            {
                Interlocked.Increment(ref counter.Value);
                await context.Request.Body.CopyToAsync(context.Response.Body, context.RequestAborted);
            });
            await backend.StartAsync();

            var proxyBuilder = WebApplication.CreateSlimBuilder();
            if (http2) proxyBuilder.WebHost.ConfigureKestrel(options => options.Listen(IPAddress.Loopback, 0, listen => listen.Protocols = HttpProtocols.Http2));
            else proxyBuilder.WebHost.UseUrls("http://127.0.0.1:0");
            proxyBuilder.Services.AddReverseProxy();
            proxyBuilder.Services.AddHpdGatewayYarpPublication();
            proxyBuilder.Services.AddHpdGatewayYarpMaterialization();
            proxyBuilder.Services.AddHpdGatewayYarpInspection(registry => registry.Add("content-check", inspector));
            var proxy = proxyBuilder.Build();
            proxy.MapHpdGatewayReverseProxy();
            await proxy.StartAsync();

            var fixture = new InspectionFixture(proxy, backend, counter, allowSpill, http2);
            if (http2)
            {
                fixture.Client.DefaultRequestVersion = HttpVersion.Version20;
                fixture.Client.DefaultVersionPolicy = HttpVersionPolicy.RequestVersionExact;
            }
            await fixture.Reload(binding, 1, allowSpill);
            return fixture;
        }

        internal Task Reload(RequestInspectionBinding? binding, ulong version) => Reload(binding, version, _allowSpill);
        internal Task ReloadUninspected(ulong version) => Reload(null, version, _allowSpill, includeRoute: true);

        internal async Task<WebApplication> StartDirectProxy(IGatewayRequestInspector inspector)
        {
            var builder = WebApplication.CreateSlimBuilder();
            builder.WebHost.UseUrls("http://127.0.0.1:0");
            builder.Services.AddReverseProxy().LoadFromMemory(_lastBundle!.Routes, _lastBundle.Clusters);
            builder.Services.AddHpdGatewayYarpInspection(registry => registry.Add("content-check", inspector));
            var app = builder.Build();
            app.MapHpdGatewayReverseProxy();
            await app.StartAsync();
            return app;
        }

        private async Task Reload(RequestInspectionBinding? binding, ulong version, bool allowSpill, bool includeRoute = false)
        {
            var configuration = new GatewayConfiguration
            {
                SchemaVersion = new GatewaySchemaVersion(1, 0),
                CanonicalizationVersion = 1,
                Routes = binding is null && !includeRoute ? [] :
                    [new RouteDeclaration
                    {
                        Id = new RouteId("upload"),
                        Match = new HttpRouteMatch { Path = "/{**path}", Methods = ["POST"] },
                        Upstream = new UpstreamId("backend"),
                        Declarations = binding is null ? new RouteDeclarations() : new RouteDeclarations { Inspection = new DeclarationReference<RequestInspectionBinding> { Inline = binding } }
                    }],
                Upstreams =
                [
                    new UpstreamDeclaration
                    {
                        Id = new UpstreamId("backend"),
                        Request = _http2
                            ? new UpstreamRequestDeclaration { Version = UpstreamHttpVersion.Http2, VersionSelection = HttpVersionSelection.Exact }
                            : new UpstreamRequestDeclaration(),
                        Endpoints = new StaticEndpointSource
                        {
                            Destinations = [new DestinationDeclaration { Id = new DestinationId("one"), Address = new Uri(_backendAddress) }]
                        }
                    }
                ]
            };
            var capabilities = HostCapabilitySnapshot.Create(new HostCapabilityRegistration
            {
                InstalledFamilies = GatewayDeclarationFamilies.Inspection,
                RequestInspectors = ["content-check"],
                AllowInspectionFileSpill = allowSpill
            });
            var json = JsonSerializer.SerializeToUtf8Bytes(configuration, GatewayJsonSerializerContext.Default.GatewayConfiguration);
            var accepted = GatewayCandidateReader.Read(json, capabilities);
            accepted.IsAccepted.Should().BeTrue(string.Join(", ", accepted.Errors.Select(error => error.Message)));
            var identity = new PublicationCandidateIdentity(new CandidateId($"inspection-{version}"), "authority", "epoch", version, accepted.CanonicalDocument!.ContentHash);
            var materialized = await _proxy.Services.GetRequiredService<GatewayRuntimePlanner>().PlanAsync(accepted, identity, $"inspection-native-{version}");
            materialized.IsPlanned.Should().BeTrue(string.Join(", ", materialized.Diagnostics.Select(error => error.Code)));
            _lastBundle = materialized.PreparedApplication!;
            var outcome = await _proxy.Services.GetRequiredService<GatewayRuntimePublisher>().PublishAsync(materialized.PreparedApplication!, TimeSpan.FromSeconds(5));
            outcome.State.Should().Be(GatewayPublicationState.ActiveAcknowledged);
        }

        public async ValueTask DisposeAsync()
        {
            Client.Dispose();
            await _proxy.DisposeAsync();
            await _backend.DisposeAsync();
        }
    }

    private sealed class RecordingInspector(GatewayInspectionDecision? decision = null) : IGatewayRequestInspector
    {
        internal int Calls { get; private set; }
        internal string? Body { get; private set; }
        internal GatewayInspectionCompleteness Completeness { get; private set; }
        internal string? SpillPath { get; private set; }
        internal bool SpillExistedDuringInspection { get; private set; }
        public async ValueTask<GatewayInspectionDecision> InspectAsync(GatewayInspectionContext context, CancellationToken cancellationToken)
        {
            Calls++;
            Completeness = context.Completeness;
            if (context.Body is FileBufferingReadStream buffering)
            {
                SpillPath = buffering.TempFileName;
                SpillExistedDuringInspection = SpillPath is not null && File.Exists(SpillPath);
            }
            using var reader = new StreamReader(context.Body, Encoding.UTF8, leaveOpen: true);
            Body = await reader.ReadToEndAsync(cancellationToken);
            return decision ?? GatewayInspectionDecision.Allow();
        }
    }

    private sealed class ThrowingInspector : IGatewayRequestInspector
    {
        internal string? SpillPath { get; private set; }
        public ValueTask<GatewayInspectionDecision> InspectAsync(GatewayInspectionContext context, CancellationToken cancellationToken)
        {
            SpillPath = (context.Body as FileBufferingReadStream)?.TempFileName;
            throw new InvalidOperationException("sensitive body must never escape");
        }
    }

    private sealed class NonSeekableReadStream(byte[] bytes) : MemoryStream(bytes)
    {
        public override bool CanSeek => false;
        public override long Length => throw new NotSupportedException();
    }

    private sealed class TestUpgradeFeature : IHttpUpgradeFeature
    {
        public bool IsUpgradableRequest => true;
        public Task<Stream> UpgradeAsync() => throw new InvalidOperationException("Upgrade must not be invoked.");
    }

    private sealed class Counter { internal int Value; }

    private static string Address(WebApplication application) => application.Services
        .GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single();
}
