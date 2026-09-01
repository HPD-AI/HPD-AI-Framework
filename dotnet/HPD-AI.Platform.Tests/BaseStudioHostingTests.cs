using System.Text;
using System.Text.Json;
using HPD.AI.Platform.Studio;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace HPD.AI.Platform.Tests;

public sealed class BaseStudioHostingTests
{
    [Fact]
    public void Edition_assets_are_derived_from_the_frozen_module_manifests()
    {
        using ServiceProvider services = Services().BuildServiceProvider();
        BaseStudioEditionAssetGraph assets = BaseStudioEditionAssetGraph.Create(
            services.GetRequiredService<BaseStudioEditionAssetCatalogProvider>().GetRequiredCatalog(), services.GetRequiredService<BaseStudioShellContract>());

        Assert.True(assets.TryResolve("base", 1, "assets/base.js", out BaseStudioResolvedAsset asset));
        Assert.Contains("activateStudioModule", Encoding.UTF8.GetString(asset.Content), StringComparison.Ordinal);
        Assert.False(assets.TryResolve("base", 2, "assets/base.js", out _));
        Assert.False(assets.TryResolve("base", 1, "../assets/base.js", out _));
    }

    [Fact]
    public void Spa_fallback_is_derived_only_from_the_canonical_route_graph()
    {
        using ServiceProvider services = Services().BuildServiceProvider();
        BaseStudioApplicationGraph graph = services.GetRequiredService<BaseStudioApplicationGraphProvider>().GetRequiredGraph();
        var empty = new QueryCollection();

        Assert.True(BaseStudioRouteMatcher.Matches(graph, string.Empty, empty));
        Assert.True(BaseStudioRouteMatcher.Matches(graph, "data", empty));
        Assert.False(BaseStudioRouteMatcher.Matches(graph, "invented", empty));
        Assert.False(BaseStudioRouteMatcher.Matches(graph, "data/extra", empty));
    }

    [Fact]
    public void Endpoint_inventory_contains_bootstrap_and_manifest_assets_but_no_legacy_config()
    {
        WebApplicationBuilder host = WebApplication.CreateBuilder();
        Configure(host.Services);
        WebApplication app = host.Build();
        app.MapHPDAIPlatform();

        string[] names = ((IEndpointRouteBuilder)app).DataSources.SelectMany(static source => source.Endpoints)
            .Select(static endpoint => endpoint.Metadata.GetMetadata<IEndpointNameMetadata>()?.EndpointName ?? string.Empty).ToArray();
        Assert.Contains("BaseStudioBootstrap", names);
        Assert.Contains("BaseStudioModuleAsset", names);
        Assert.DoesNotContain("GetHPDAIPlatformConfig", names);
        Assert.Null(typeof(HPDAIPlatformBuilder).GetMethod("AddModule"));
    }

    [Fact]
    public async Task Bootstrap_is_authenticated_graph_pinned_and_no_store()
    {
        WebApplicationBuilder host = WebApplication.CreateBuilder();
        Configure(host.Services);
        WebApplication app = host.Build();
        app.MapHPDAIPlatform();
        RouteEndpoint endpoint = Assert.IsType<RouteEndpoint>(((IEndpointRouteBuilder)app).DataSources
            .SelectMany(static source => source.Endpoints)
            .Single(value => value.Metadata.GetMetadata<IEndpointNameMetadata>()?.EndpointName == "BaseStudioBootstrap"));
        BaseStudioApplicationGraph graph = app.Services.GetRequiredService<BaseStudioApplicationGraphProvider>().GetRequiredGraph();
        BaseStudioEditionAssetGraph assetGraph = BaseStudioEditionAssetGraph.Create(
            app.Services.GetRequiredService<BaseStudioEditionAssetCatalogProvider>().GetRequiredCatalog(), BaseStudioShellContract.Current);
        string body = JsonSerializer.Serialize(new
        {
            shellContractChecksum = Hex(BaseStudioShellContract.Current.Checksum),
            editionAssetGraphChecksum = Hex(assetGraph.Checksum),
            runtimeClientChecksum = Hex(HostingTestStudioContribution.Digest(8)),
            locale = "en-US",
            clientCapabilities = new[] { 1, 2 },
        });
        var context = new DefaultHttpContext { RequestServices = app.Services };
        context.Request.Body = new MemoryStream(Encoding.UTF8.GetBytes(body));
        context.Request.ContentLength = context.Request.Body.Length;
        context.Request.ContentType = "application/json";
        context.Response.Body = new MemoryStream();

        await endpoint.RequestDelegate!(context);

        Assert.Equal(StatusCodes.Status200OK, context.Response.StatusCode);
        Assert.Equal("no-store, private", context.Response.Headers.CacheControl);
        using JsonDocument response = JsonDocument.Parse(((MemoryStream)context.Response.Body).ToArray());
        Assert.Equal("sample.application", response.RootElement.GetProperty("applicationId").GetString());
        Assert.Equal("inspect", response.RootElement.GetProperty("mode").GetString());
        Assert.Equal("11", response.RootElement.GetProperty("authority").GetProperty("principalGeneration").GetString());
        Assert.Equal(JsonValueKind.Array, response.RootElement.GetProperty("contractMap").GetProperty("methods").ValueKind);
        HostingTestBootstrapRuntime runtime = Assert.IsType<HostingTestBootstrapRuntime>(app.Services.GetRequiredService<IBaseStudioBootstrapRuntime>());
        Assert.Same(graph, runtime.Invocation!.ApplicationGraph);
        Assert.Equal(11, runtime.Invocation.Authorization.Session.PrincipalGeneration);
    }

    [Fact]
    public async Task Module_asset_endpoint_serves_only_exact_manifest_identity()
    {
        WebApplicationBuilder host = WebApplication.CreateBuilder();
        Configure(host.Services);
        WebApplication app = host.Build();
        app.MapHPDAIPlatform();
        RouteEndpoint endpoint = Assert.IsType<RouteEndpoint>(((IEndpointRouteBuilder)app).DataSources
            .SelectMany(static source => source.Endpoints)
            .Single(value => value.Metadata.GetMetadata<IEndpointNameMetadata>()?.EndpointName == "BaseStudioModuleAsset"));
        var context = new DefaultHttpContext { RequestServices = app.Services };
        context.Request.RouteValues["moduleId"] = "base";
        context.Request.RouteValues["version"] = "1";
        context.Request.RouteValues["assetPath"] = "assets/base.js";
        context.Response.Body = new MemoryStream();

        await endpoint.RequestDelegate!(context);

        Assert.Equal(StatusCodes.Status200OK, context.Response.StatusCode);
        Assert.Equal("public,max-age=31536000,immutable", context.Response.Headers.CacheControl);
        Assert.StartsWith("\"sha256-", context.Response.Headers.ETag.ToString(), StringComparison.Ordinal);
        Assert.Contains("activateStudioModule", Encoding.UTF8.GetString(((MemoryStream)context.Response.Body).ToArray()), StringComparison.Ordinal);
    }

    private static string Hex(BaseStudioSha256 value) => Convert.ToHexString(value.ToArray()).ToLowerInvariant();

    private static IServiceCollection Services()
    {
        var services = new ServiceCollection();
        Configure(services);
        return services;
    }

    private static void Configure(IServiceCollection services)
    {
        services.AddHPDAIPlatform()
            .AddStudioEditionModuleAsset(HostingTestStudioContribution.CreateEditionAssetContribution())
            .AddStudioModule<HostingTestStudioContribution>()
            .AddStudioAuthentication(static _ => new Authentication());
        services.AddSingleton<IBaseStudioBootstrapRuntime, HostingTestBootstrapRuntime>();
    }

    private sealed class Authentication : IBaseStudioAuthenticationIntegration
    {
        public BaseStudioAuthenticationDescriptor Descriptor { get; } = BaseStudioAuthenticationDescriptor.Create(
            "studio.auth", 1, BaseStudioAuthenticationKind.Bearer, "/auth/login", "/auth/callback", "/auth/logout", "/auth/session",
            ["https://studio.example/"], null, null, TimeSpan.FromHours(1), false, []);
        public ValueTask<BaseStudioAuthenticationResult<BaseStudioSessionObservation>> ObserveSessionAsync(HttpContext context, CancellationToken token)
            => ValueTask.FromResult(BaseStudioAuthenticationResult<BaseStudioSessionObservation>.Success(Session()));
        public ValueTask<BaseStudioAuthenticationResult<BaseStudioProtectedReturnTarget>> ProtectReturnTargetAsync(HttpContext context, string? target, CancellationToken token)
            => ValueTask.FromResult(BaseStudioAuthenticationResult<BaseStudioProtectedReturnTarget>.Success(new BaseStudioProtectedReturnTarget(new byte[32])));
        public ValueTask BeginSignInAsync(HttpContext context, BaseStudioProtectedReturnTarget target, CancellationToken token) => ValueTask.CompletedTask;
        public ValueTask CompleteCallbackAsync(HttpContext context, CancellationToken token) => ValueTask.CompletedTask;
        public ValueTask BeginSignOutAsync(HttpContext context, CancellationToken token) => ValueTask.CompletedTask;
        public ValueTask<BaseStudioAuthenticationResult<BaseStudioTransportAuthorization>> AuthorizeRequestAsync(HttpContext context, BaseStudioTransportPurpose purpose, CancellationToken token)
        {
            BaseStudioSessionObservation session = Session();
            return ValueTask.FromResult(BaseStudioAuthenticationResult<BaseStudioTransportAuthorization>.Success(
                BaseStudioTransportAuthorization.Create(session, purpose, session.IssuedAtUtc.AddMinutes(1))));
        }
        public async ValueTask<BaseStudioAuthenticationResult<BaseStudioBrowserAuthorization>> AcquireBrowserAuthorizationAsync(
            HttpContext context, BaseStudioTransportPurpose purpose, CancellationToken token)
        {
            BaseStudioAuthenticationResult<BaseStudioTransportAuthorization> result = await AuthorizeRequestAsync(context, purpose, token);
            return BaseStudioAuthenticationResult<BaseStudioBrowserAuthorization>.Success(
                BaseStudioBrowserAuthorization.Create("X-HPD-Studio-Test", "opaque-test-authority", result.Value!));
        }
        public ValueTask<BaseStudioAuthenticationResult<BaseStudioFreshAuthenticationResult>> AcquireFreshAuthenticationAsync(
            HttpContext context, BaseStudioFreshAuthenticationRequest request, CancellationToken token)
            => ValueTask.FromResult(BaseStudioAuthenticationResult<BaseStudioFreshAuthenticationResult>.Success(new BaseStudioFreshAuthenticationResult.Unsupported()));
        public ValueTask<BaseStudioAuthenticationResult<BaseStudioFreshAuthenticationResult>> CompleteFreshAuthenticationAsync(
            HttpContext context, BaseStudioFreshAuthenticationContinuation continuation, CancellationToken token)
            => ValueTask.FromResult(BaseStudioAuthenticationResult<BaseStudioFreshAuthenticationResult>.Success(new BaseStudioFreshAuthenticationResult.Unsupported()));

        private static BaseStudioSessionObservation Session()
        {
            DateTimeOffset now = DateTimeOffset.UtcNow;
            return BaseStudioSessionObservation.Create(11, HostingTestStudioContribution.Digest(10), "control-plane",
                HostingTestStudioContribution.Digest(11), now, now.AddMinutes(5), HostingTestStudioContribution.Digest(12));
        }
    }
}
