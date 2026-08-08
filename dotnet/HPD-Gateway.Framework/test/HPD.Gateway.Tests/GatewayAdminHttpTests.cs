using System.Collections.Immutable;
using System.Net;
using System.Security.Claims;
using System.Text.Encodings.Web;
using FluentAssertions;
using HPD.Gateway.Abstractions;
using HPD.Gateway.Admin;
using HPD.Gateway.Hosting;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Threading.RateLimiting;
using Xunit;

namespace HPD.Gateway.Tests;

public sealed class GatewayAdminHttpTests
{
    [Fact]
    public async Task Capability_endpoint_requires_exact_management_listener_identity()
    {
        await using WebApplication application = Build(resourceAllowed: true);
        await application.StartAsync();
        HttpClient client = application.GetTestClient();

        HttpResponseMessage accepted = await client.GetAsync("/management/gateway/v1/capabilities");
        accepted.StatusCode.Should().Be(HttpStatusCode.OK);

        using var wrong = new HttpRequestMessage(HttpMethod.Get, "/management/gateway/v1/capabilities");
        wrong.Headers.Add("x-test-listener", "data");
        HttpResponseMessage rejected = await client.SendAsync(wrong);
        rejected.StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await rejected.Content.ReadAsByteArrayAsync()).Should().BeEmpty();
    }

    [Fact]
    public async Task Resource_denial_is_safe_not_found_before_body_or_authority_resolution()
    {
        await using WebApplication application = Build(resourceAllowed: false);
        await application.StartAsync();
        using var request = new HttpRequestMessage(HttpMethod.Post,
            "/management/gateway/v1/namespaces/ns/targets/node:provision");
        request.Headers.Add("Idempotency-Key", "key");
        request.Content = new StringContent("not-json");
        HttpResponseMessage response = await application.GetTestClient().SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        string body = await response.Content.ReadAsStringAsync();
        body.Should().Contain("gateway.admin.resource.notFound");
        body.ToLowerInvariant().Should().NotContain("policy");
    }

    private static WebApplication Build(bool resourceAllowed)
    {
        WebApplicationBuilder builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddAuthentication("test")
            .AddScheme<AuthenticationSchemeOptions, TestAuthenticationHandler>("test", null);
        builder.Services.AddAuthorization(options =>
        {
            foreach (string capability in GatewayAdminCapabilities.All)
                options.AddPolicy(capability, policy => policy.RequireAuthenticatedUser());
            options.AddPolicy(GatewayAdminResourcePolicies.Namespace, policy => policy.RequireAssertion(_ => resourceAllowed));
            options.AddPolicy(GatewayAdminResourcePolicies.Target, policy => policy.RequireAssertion(_ => resourceAllowed));
            options.AddPolicy(GatewayAdminResourcePolicies.Administration, policy => policy.RequireAssertion(_ => resourceAllowed));
        });
        builder.Services.AddRateLimiter(options => options.AddFixedWindowLimiter("gateway-management", limiter =>
        {
            limiter.PermitLimit = 16; limiter.QueueLimit = 0; limiter.Window = TimeSpan.FromSeconds(1);
        }));
        builder.Services.AddRequestTimeouts(options => options.AddPolicy("gateway-management", TimeSpan.FromSeconds(5)));
        builder.Services.AddSingleton<IGatewayAdminActorProjector, TestActorProjector>();
        builder.Services.AddHpdGatewayAdmin();
        WebApplication app = builder.Build();
        app.UseRouting();
        app.Use((context, next) =>
        {
            bool data = context.Request.Headers.ContainsKey("x-test-listener");
            context.Features.Set<IHpdGatewayListenerFeature>(new TestListenerFeature(
                new("management"), data ? GatewayListenerRole.DataPlane : GatewayListenerRole.Management,
                data ? "gateway-data" : "gateway-admin-v1"));
            return next(context);
        });
        app.UseHpdGatewayListenerRoles();
        app.UseRequestTimeouts();
        app.UseAuthentication();
        app.UseRateLimiter();
        app.UseAuthorization();
        app.MapHpdGatewayAdmin(new GatewayAdminEndpointOptions
        {
            AuthenticationScheme = "test",
            CapabilityPolicies = GatewayAdminCapabilities.All.ToImmutableDictionary(
                static capability => capability, static capability => capability, StringComparer.Ordinal),
        });
        return app;
    }

    private sealed record TestListenerFeature(
        ListenerId ListenerId, GatewayListenerRole Role, string EndpointSurfaceId) : IHpdGatewayListenerFeature;

    private sealed class TestActorProjector : IGatewayAdminActorProjector
    {
        public ValueTask<GatewayAdminRequestAttribution> ProjectAsync(
            HttpContext context, string capability, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(new GatewayAdminRequestAttribution("actor", "test", capability, "correlation"));
    }

    private sealed class TestAuthenticationHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder) : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            var identity = new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, "actor")], Scheme.Name);
            return Task.FromResult(AuthenticateResult.Success(
                new AuthenticationTicket(new ClaimsPrincipal(identity), Scheme.Name)));
        }
    }
}
