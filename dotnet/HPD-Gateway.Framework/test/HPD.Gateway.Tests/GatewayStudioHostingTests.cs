using HPD.AI.Platform;
using HPD.Gateway;
using HPD.Gateway.ControlPlane;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using FluentAssertions;
using Xunit;

namespace HPD.Gateway.Tests;

public sealed class GatewayStudioHostingTests
{
    [Fact]
    public void Mapping_without_registration_fails_closed()
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddHPDAIPlatform();
        using WebApplication app = builder.Build();

        Action map = () => app.MapGatewayStudioCore();

        map.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public async Task Governed_assets_have_closed_routes_cache_and_security_headers()
    {
        await using WebApplication app = await BuildAsync();
        using HttpClient client = app.GetTestClient();

        using HttpResponseMessage index = await client.GetAsync("/studio/");
        index.StatusCode.Should().Be(System.Net.HttpStatusCode.OK);
        index.Headers.CacheControl!.NoStore.Should().BeTrue();
        AssertSecurityHeaders(index);
        index.Headers.GetValues("HPD-Studio-Asset-Identity").Single().Should().MatchRegex("^[0-9a-f]{64}$");

        using HttpResponseMessage route = await client.GetAsync("/studio/gateway/configure");
        route.StatusCode.Should().Be(System.Net.HttpStatusCode.OK);

        using HttpResponseMessage unknown = await client.GetAsync("/studio/not-a-module");
        unknown.StatusCode.Should().Be(System.Net.HttpStatusCode.NotFound);
        using HttpResponseMessage stale = await client.GetAsync("/studio/assets/index-stale000.js");
        stale.StatusCode.Should().Be(System.Net.HttpStatusCode.NotFound);
        using HttpResponseMessage sourceMap = await client.GetAsync("/studio/assets/index.js.map");
        sourceMap.StatusCode.Should().Be(System.Net.HttpStatusCode.NotFound);
        (await client.GetAsync("/studio/index.html")).StatusCode.Should().Be(System.Net.HttpStatusCode.NotFound);
        (await client.GetAsync("/studio/gateway%5Cconfigure")).StatusCode.Should().Be(System.Net.HttpStatusCode.NotFound);
        (await client.GetAsync("/studio/assets%5Cindex-DaviNOrF.js")).StatusCode.Should().Be(System.Net.HttpStatusCode.NotFound);
        (await client.GetAsync("/studio/assets/%2e%2e/index.html")).StatusCode.Should().Be(System.Net.HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Prefix_without_trailing_slash_redirects_to_the_canonical_asset_base()
    {
        await using WebApplication app = await BuildAsync();
        using HttpClient client = app.GetTestClient();

        using HttpResponseMessage response = await client.GetAsync("/studio?source=test");

        response.StatusCode.Should().Be(System.Net.HttpStatusCode.Redirect);
        response.Headers.Location.Should().Be(new Uri("/studio/?source=test", UriKind.Relative));
    }

    [Fact]
    public async Task Runtime_configuration_is_bounded_secret_free_and_not_cached()
    {
        await using WebApplication app = await BuildAsync();
        using HttpClient client = app.GetTestClient();

        using HttpResponseMessage response = await client.GetAsync("/studio/studio-config.js");
        string body = await response.Content.ReadAsStringAsync();

        response.StatusCode.Should().Be(System.Net.HttpStatusCode.OK);
        response.Headers.CacheControl!.NoStore.Should().BeTrue();
        body.Should().Contain("globalThis.HPD_STUDIO_CONFIG");
        body.Should().Contain("assetContractVersion: \"1\"");
        body.Should().MatchRegex("shellContractIdentity: \"[0-9a-f]{64}\"");
        body.Should().Contain("routePrefix: \"/studio\"");
        string lower = body.ToLowerInvariant();
        lower.Should().NotContain("token");
        lower.Should().NotContain("secret");
        lower.Should().NotContain("candidate");

        string shellIdentity = System.Text.RegularExpressions.Regex.Match(
            body, "shellContractIdentity: \\\"([0-9a-f]{64})\\\"").Groups[1].Value;
        string index = await client.GetStringAsync("/studio/");
        string scriptPath = System.Text.RegularExpressions.Regex.Match(index, """assets/[^\"]+\.js""").Value;
        string script = await client.GetStringAsync("/studio/" + scriptPath);
        script.Should().Contain("hpd-shell-contract-v1:" + shellIdentity);
    }

    [Theory]
    [InlineData("", "/management/gateway/v1", "HPD Gateway Studio", "development")]
    [InlineData("/studio", "relative", "HPD Gateway Studio", "development")]
    [InlineData("/studio", "/management/gateway/v1/", "HPD Gateway Studio", "development")]
    [InlineData("/studio", "/management/gateway/v1?query", "HPD Gateway Studio", "development")]
    [InlineData("/studio", "/management/gateway/v1#fragment", "HPD Gateway Studio", "development")]
    [InlineData("/studio", "/management%2fgateway/v1", "HPD Gateway Studio", "development")]
    [InlineData("/studio", "/management%5Cgateway/v1", "HPD Gateway Studio", "development")]
    [InlineData("/studio", "/management/gateway/v1", "", "development")]
    [InlineData("/studio", "/management/gateway/v1", "HPD Gateway Studio", "unknown")]
    public void Hostile_runtime_configuration_fails_before_mapping(
        string routePrefix, string apiBasePath, string title, string mode)
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddHPDAIPlatform().AddGatewayStudioCore();
        using WebApplication app = builder.Build();

        Action map = () => app.MapGatewayStudioCore(options =>
        {
            options.RoutePrefix = routePrefix;
            options.ApiBasePath = apiBasePath;
            options.ProductTitle = title;
            options.Mode = mode;
        });

        map.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public async Task Valid_non_default_api_base_is_frozen_into_runtime_configuration()
    {
        await using WebApplication app = await BuildAsync(new GatewayStudioEndpointOptions
        {
            ApiBasePath = "/custom/gateway-admin",
        });

        string configuration = await app.GetTestClient().GetStringAsync("/studio/studio-config.js");

        configuration.Should().Contain("apiBasePath: \"/custom/gateway-admin\"");
    }

    [Fact]
    public void Runtime_catalog_bounds_are_exact()
    {
        using WebApplication exact = BuildConfigured(static options =>
        {
            for (int index = 0; index < 64; index++) options.Capabilities.Add($"capability.{index:D2}");
        });
        Action exactMap = () => exact.MapHPDAIPlatform();
        exactMap.Should().NotThrow();

        using WebApplication overflow = BuildConfigured(static options =>
        {
            for (int index = 0; index < 65; index++) options.Capabilities.Add($"capability.{index:D2}");
        });
        Action overflowMap = () => overflow.MapHPDAIPlatform();
        overflowMap.Should().Throw<InvalidOperationException>();
    }

    [Theory]
    [InlineData("e\u0301", "gateway", "Gateway", "Gateway Studio", "active")]
    [InlineData("valid", "Gateway", "Gateway", "Gateway Studio", "active")]
    [InlineData("valid", "gateway", "bad\u0001", "Gateway Studio", "active")]
    [InlineData("valid", "gateway", "Gateway", "Gateway Studio", "unknown")]
    public void Malformed_runtime_catalog_entries_fail_before_mapping(
        string capability, string moduleId, string label, string title, string status)
    {
        using WebApplication app = BuildConfigured(options =>
        {
            options.Capabilities.Add(capability);
            options.Modules.Add(new(moduleId, label, title, status));
        });

        Action map = () => app.MapHPDAIPlatform();
        map.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void Duplicate_runtime_catalog_entries_fail_before_mapping()
    {
        using WebApplication app = BuildConfigured(static options =>
        {
            options.Capabilities.Add("gateway");
            options.Capabilities.Add("gateway");
        });
        Action map = () => app.MapHPDAIPlatform();
        map.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void Conflicting_gateway_studio_route_ownership_fails_validation()
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddHPDAIPlatform().AddGatewayStudioCore();
        using WebApplication app = builder.Build();
        app.MapGatewayStudioCore();
        app.MapGatewayStudioCore();

        Action validate = () => app.ValidateHpdGatewayEndpointRoles();

        validate.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public async Task Every_served_kind_has_exact_media_cache_and_security_headers()
    {
        await using WebApplication app = await BuildAsync();
        using HttpClient client = app.GetTestClient();
        string indexBody = await client.GetStringAsync("/studio/");
        string script = System.Text.RegularExpressions.Regex.Match(indexBody, """assets/[^\"]+\.js""").Value;
        string style = System.Text.RegularExpressions.Regex.Match(indexBody, """assets/[^\"]+\.css""").Value;

        foreach ((string path, string mediaType, string cache) in new[]
        {
            ("/studio/", "text/html", "no-store"),
            ("/studio/gateway/diagnose", "text/html", "no-store"),
            ("/studio/studio-config.js", "text/javascript", "no-store"),
            ("/studio/" + script, "text/javascript", "public, max-age=31536000, immutable"),
            ("/studio/" + style, "text/css", "public, max-age=31536000, immutable")
        })
        {
            using HttpResponseMessage response = await client.GetAsync(path);
            response.StatusCode.Should().Be(System.Net.HttpStatusCode.OK, path);
            response.Content.Headers.ContentType!.MediaType.Should().Be(mediaType, path);
            response.Headers.CacheControl!.ToString().Should().Be(cache, path);
            response.Headers.GetValues("HPD-Studio-Asset-Identity").Single().Should().MatchRegex("^[0-9a-f]{64}$");
            AssertSecurityHeaders(response);
        }
    }

    [Theory]
    [InlineData("index.html")]
    [InlineData("other.js")]
    [InlineData("assets\\bundle.js")]
    [InlineData("assets/../bundle.js")]
    [InlineData("assets/_private.js")]
    [InlineData("assets/.private.js")]
    [InlineData("assets//bundle.js")]
    [InlineData("assets/bad\u001f.js")]
    public void Invalid_embedded_asset_paths_fail_closed(string path)
    {
        if (path == "index.html") return; // the one dedicated root resource
        Action validate = () => HPDAIPlatformEndpointRouteBuilderExtensions.ValidateEmbeddedAssetPath(path);
        validate.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void Embedded_asset_path_and_graph_bounds_are_exact()
    {
        string maximumPath = "assets/" + new string('a', 246) + ".js";
        maximumPath.Should().HaveLength(256);
        HPDAIPlatformEndpointRouteBuilderExtensions.ValidateEmbeddedAssetPath(maximumPath);
        Action pathOverflow = () => HPDAIPlatformEndpointRouteBuilderExtensions.ValidateEmbeddedAssetPath(maximumPath + "x");
        pathOverflow.Should().Throw<InvalidOperationException>();

        var maximumCount = Enumerable.Range(0, 256).Select(index => ($"assets/a{index:D3}.js", 1L)).ToArray();
        HPDAIPlatformEndpointRouteBuilderExtensions.ValidateEmbeddedAssetGraph(maximumCount);
        Action countOverflow = () => HPDAIPlatformEndpointRouteBuilderExtensions.ValidateEmbeddedAssetGraph(
            maximumCount.Append(("assets/overflow.js", 1L)));
        countOverflow.Should().Throw<InvalidOperationException>();

        HPDAIPlatformEndpointRouteBuilderExtensions.ValidateEmbeddedAssetGraph(
            Enumerable.Range(0, 4).Select(index => ($"assets/g{index}.js", 8L * 1024 * 1024)));
        Action totalOverflow = () => HPDAIPlatformEndpointRouteBuilderExtensions.ValidateEmbeddedAssetGraph(
            Enumerable.Range(0, 4).Select(index => ($"assets/g{index}.js", 8L * 1024 * 1024)).Append(("assets/x.js", 1L)));
        totalOverflow.Should().Throw<InvalidOperationException>();
        Action itemOverflow = () => HPDAIPlatformEndpointRouteBuilderExtensions.ValidateEmbeddedAssetGraph(
            [("assets/x.js", 8L * 1024 * 1024 + 1)]);
        itemOverflow.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public async Task Listener_role_and_surface_are_enforced_at_request_time()
    {
        await using WebApplication app = await BuildWithListenerEnforcementAsync();
        using HttpClient client = app.GetTestClient();

        (await client.GetAsync("/studio/")).StatusCode.Should().Be(System.Net.HttpStatusCode.OK);
        foreach (string listener in new[] { "data", "foreign", "missing" })
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, "/studio/");
            request.Headers.Add("x-test-listener", listener);
            (await client.SendAsync(request)).StatusCode.Should().Be(System.Net.HttpStatusCode.NotFound, listener);
        }
    }

    [Fact]
    public async Task Caller_option_mutation_after_mapping_cannot_change_routes()
    {
        var options = new GatewayStudioEndpointOptions { RoutePrefix = "/governed-studio" };
        await using WebApplication app = await BuildAsync(options);
        options.RoutePrefix = "/mutated";
        using HttpClient client = app.GetTestClient();

        (await client.GetAsync("/governed-studio/gateway")).StatusCode.Should().Be(System.Net.HttpStatusCode.OK);
        (await client.GetAsync("/mutated/gateway")).StatusCode.Should().Be(System.Net.HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Studio_endpoints_have_one_management_role()
    {
        await using WebApplication app = await BuildAsync();
        Endpoint[] studioEndpoints = ((IEndpointRouteBuilder)app).DataSources.SelectMany(static source => source.Endpoints)
            .Where(static endpoint => endpoint.Metadata.GetMetadata<GatewayEndpointRoleMetadata>()?.EndpointSurfaceId == "gateway-admin-v1")
            .ToArray();
        studioEndpoints.Should().NotBeEmpty();
        foreach (Endpoint endpoint in studioEndpoints)
        {
            GatewayEndpointRoleMetadata[] roles = endpoint.Metadata.GetOrderedMetadata<GatewayEndpointRoleMetadata>().ToArray();
            roles.Should().ContainSingle();
            roles[0].Role.Should().Be(GatewayListenerRole.Management);
            roles[0].EndpointSurfaceId.Should().Be("gateway-admin-v1");
            roles[0].RequireListenerFeature.Should().BeTrue();
        }
    }

    [Theory]
    [InlineData("relative")]
    [InlineData("/studio/../admin")]
    [InlineData("/studio\\admin")]
    public void Invalid_route_prefix_fails_before_serving(string routePrefix)
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddHPDAIPlatform().AddGatewayStudioCore();
        using WebApplication app = builder.Build();

        Action map = () => app.MapGatewayStudioCore(options => options.RoutePrefix = routePrefix);

        map.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public async Task Exact_asset_etag_produces_not_modified_without_a_body()
    {
        await using WebApplication app = await BuildAsync();
        using HttpClient client = app.GetTestClient();
        string index = await client.GetStringAsync("/studio/");
        string assetPath = System.Text.RegularExpressions.Regex.Match(index, """assets/[^\"]+\.js""").Value;
        assetPath.Should().NotBeEmpty();
        using HttpResponseMessage first = await client.GetAsync("/studio/" + assetPath);
        first.StatusCode.Should().Be(System.Net.HttpStatusCode.OK);
        first.Headers.CacheControl!.Public.Should().BeTrue();
        first.Headers.CacheControl.MaxAge.Should().Be(TimeSpan.FromDays(365));

        using var request = new HttpRequestMessage(HttpMethod.Get, "/studio/" + assetPath);
        request.Headers.IfNoneMatch.Add(first.Headers.ETag!);
        using HttpResponseMessage second = await client.SendAsync(request);
        second.StatusCode.Should().Be(System.Net.HttpStatusCode.NotModified);
        (await second.Content.ReadAsByteArrayAsync()).Should().BeEmpty();
    }

    private static async Task<WebApplication> BuildAsync(GatewayStudioEndpointOptions? supplied = null)
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddHPDAIPlatform().AddGatewayStudioCore();
        WebApplication app = builder.Build();
        app.MapGatewayStudioCore(options =>
        {
            options.RoutePrefix = supplied?.RoutePrefix ?? "/studio";
            options.ApiBasePath = supplied?.ApiBasePath ?? "/management/gateway/v1";
            options.ProductTitle = supplied?.ProductTitle ?? "HPD Gateway Studio";
            options.Mode = supplied?.Mode ?? "development";
            options.EndpointSurfaceId = supplied?.EndpointSurfaceId ?? "gateway-admin-v1";
            options.RequireManagementListener = supplied?.RequireManagementListener ?? true;
        });
        await app.StartAsync();
        return app;
    }

    private static WebApplication BuildConfigured(Action<HPDAIPlatformOptions> configure)
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddHPDAIPlatform();
        builder.Services.Configure(configure);
        return builder.Build();
    }

    private static async Task<WebApplication> BuildWithListenerEnforcementAsync()
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddHPDAIPlatform().AddGatewayStudioCore();
        WebApplication app = builder.Build();
        app.UseRouting();
        app.Use((context, next) =>
        {
            string listener = context.Request.Headers["x-test-listener"].SingleOrDefault() ?? "management";
            if (listener != "missing")
            {
                context.Features.Set<IHpdGatewayListenerFeature>(listener switch
                {
                    "data" => new TestListenerFeature(new("data"), GatewayListenerRole.DataPlane, "gateway-data"),
                    "foreign" => new TestListenerFeature(new("management"), GatewayListenerRole.Management, "foreign-admin"),
                    _ => new TestListenerFeature(new("management"), GatewayListenerRole.Management, "gateway-admin-v1")
                });
            }
            return next(context);
        });
        app.UseHpdGatewayListenerRoles();
        app.MapGatewayStudioCore();
        await app.StartAsync();
        return app;
    }

    private sealed record TestListenerFeature(
        ListenerId ListenerId, GatewayListenerRole Role, string EndpointSurfaceId) : IHpdGatewayListenerFeature;

    private static void AssertSecurityHeaders(HttpResponseMessage response)
    {
        response.Headers.GetValues("Content-Security-Policy").Single().Should().Contain("default-src 'none'");
        response.Headers.GetValues("Cross-Origin-Opener-Policy").Single().Should().Be("same-origin");
        response.Headers.GetValues("Cross-Origin-Resource-Policy").Single().Should().Be("same-origin");
        response.Headers.GetValues("Referrer-Policy").Single().Should().Be("no-referrer");
        response.Headers.GetValues("X-Content-Type-Options").Single().Should().Be("nosniff");
        response.Headers.GetValues("X-Frame-Options").Single().Should().Be("DENY");
    }
}
