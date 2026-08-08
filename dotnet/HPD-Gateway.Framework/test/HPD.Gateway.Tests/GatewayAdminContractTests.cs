using System.Collections.Immutable;
using System.Security.Claims;
using System.Text.Encodings.Web;
using FluentAssertions;
using HPD.Gateway.Admin;
using HPD.Gateway;
using HPD.Gateway.Management;
using HPD.Gateway.HPDAuth;
using HPD.Auth.ControlPlane;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Threading.RateLimiting;
using Xunit;

namespace HPD.Gateway.Tests;

public sealed class GatewayAdminContractTests
{
    [Fact]
    public void Endpoint_ledger_maps_one_static_capability_and_exact_resource_policy_per_scope()
    {
        WebApplicationBuilder builder = WebApplication.CreateBuilder();
        builder.Services.AddLogging();
        builder.Services.AddHpdGateway(static gateway => gateway.AddCoreFamilies());
        builder.Services.AddHpdGatewayManagement();
        builder.Services.AddAuthentication("test").AddScheme<AuthenticationSchemeOptions, TestAuthenticationHandler>("test", null);
        builder.Services.AddAuthorization(options =>
        {
            foreach (string policy in GatewayAdminCapabilities.All)
                options.AddPolicy(policy, value => value.RequireAssertion(static _ => true));
            options.AddPolicy(GatewayAdminResourcePolicies.Namespace, value => value.RequireAssertion(static _ => true));
            options.AddPolicy(GatewayAdminResourcePolicies.Target, value => value.RequireAssertion(static _ => true));
            options.AddPolicy(GatewayAdminResourcePolicies.Administration, value => value.RequireAssertion(static _ => true));
        });
        builder.Services.AddRateLimiter(options => options.AddFixedWindowLimiter("gateway-management", value =>
        {
            value.PermitLimit = 8;
            value.QueueLimit = 0;
            value.Window = TimeSpan.FromSeconds(1);
        }));
        builder.Services.AddRequestTimeouts(options => options.AddPolicy("gateway-management", TimeSpan.FromSeconds(5)));
        builder.Services.AddSingleton<IGatewayAdminActorProjector, TestActorProjector>();
        builder.Services.AddHpdGatewayAdmin();
        WebApplication application = builder.Build();
        ImmutableDictionary<string, string> policies = GatewayAdminCapabilities.All
            .ToImmutableDictionary(static value => value, static value => value, StringComparer.Ordinal);

        application.MapHpdGatewayAdmin(new GatewayAdminEndpointOptions
        {
            AuthenticationScheme = "test",
            CapabilityPolicies = policies,
        });

        RouteEndpoint[] endpoints = ((IEndpointRouteBuilder)application).DataSources.SelectMany(static source => source.Endpoints)
            .OfType<RouteEndpoint>().Where(static endpoint => endpoint.Metadata.GetMetadata<GatewayAdminEndpointDescriptor>() is not null)
            .ToArray();
        endpoints.Should().HaveCount(GatewayAdminEndpointLedger.V1.Length);
        GatewayAdminEndpointLedger.V1.Should().HaveCount(22);
        GatewayAdminEndpointLedger.V1.Select(static endpoint => (endpoint.Method, endpoint.Pattern))
            .Should().OnlyHaveUniqueItems();
        foreach (RouteEndpoint endpoint in endpoints)
        {
            GatewayAdminEndpointDescriptor descriptor = endpoint.Metadata.GetRequiredMetadata<GatewayAdminEndpointDescriptor>();
            endpoint.Metadata.GetOrderedMetadata<IAuthorizeData>()
                .Count(value => StringComparer.Ordinal.Equals(value.Policy, descriptor.Capability))
                .Should().Be(1);
            descriptor.ResourceKind.HasValue.Should().Be(descriptor.ResourcePolicy is not null);
        }
    }

    [Fact]
    public void Wire_contract_has_no_unbounded_or_reflection_escape_types()
    {
        Type[] dtoTypes =
        [
            typeof(GatewayRevisionRequest), typeof(GatewayActivationRequest), typeof(GatewayCompareRequest),
            typeof(GatewayImportRequest), typeof(GatewayBackupRequest), typeof(GatewayPurgeRequest),
            typeof(GatewayOperationResponse), typeof(GatewayActivationHistoryResponse),
            typeof(GatewayTargetStatusResponse), typeof(GatewayExportResponse),
            typeof(GatewayAdministrativeResponse),
        ];
        dtoTypes.SelectMany(static type => type.GetProperties())
            .Should().NotContain(property => property.PropertyType == typeof(object) ||
                property.PropertyType == typeof(System.Text.Json.JsonElement) ||
                property.PropertyType == typeof(Type) || typeof(Delegate).IsAssignableFrom(property.PropertyType));
        typeof(GatewayAdminEndpointRouteBuilderExtensions).GetMethods()
            .Should().ContainSingle(method => method.Name == nameof(GatewayAdminEndpointRouteBuilderExtensions.MapHpdGatewayAdmin));
    }

    [Fact]
    public void Mapping_rejects_an_incomplete_capability_policy_catalog()
    {
        WebApplicationBuilder builder = WebApplication.CreateBuilder();
        builder.Services.AddLogging();
        builder.Services.AddHpdGateway(static gateway => gateway.AddCoreFamilies());
        builder.Services.AddHpdGatewayManagement();
        builder.Services.AddHpdGatewayAdmin();
        builder.Services.AddSingleton<IGatewayAdminActorProjector, TestActorProjector>();
        WebApplication application = builder.Build();
        Action map = () => application.MapHpdGatewayAdmin(new GatewayAdminEndpointOptions
        {
            CapabilityPolicies = ImmutableDictionary<string, string>.Empty,
        });
        map.Should().Throw<InvalidOperationException>().WithMessage("*exact v1 catalog*");
    }

    [Fact]
    public void HpdAuth_bridge_rejects_split_brain_endpoint_profile_options()
    {
        WebApplicationBuilder builder = WebApplication.CreateBuilder();
        builder.Services.AddLogging();
        builder.Services.AddHpdGateway(static gateway => gateway.AddCoreFamilies());
        builder.Services.AddHpdGatewayManagement();
        builder.Services.AddHpdGatewayAdmin();
        builder.Services.AddHPDControlPlane(options =>
        {
            options.AddProfile("gateway", profile =>
            {
                profile.AuthenticationScheme = "hpd-auth";
                profile.AuthenticationProfile = "gateway";
                profile.ActorIdentifierClaim = ClaimTypes.NameIdentifier;
                profile.RateLimitPolicy = "hpd-rate";
                profile.RequestTimeoutPolicy = "hpd-timeout";
            });
            foreach (string capability in GatewayAdminCapabilities.All)
                options.MapCapability(capability, "hpd-policy");
        });
        builder.Services.AddHpdGatewayAdminHpdAuth("gateway");
        WebApplication application = builder.Build();
        Action map = () => application.MapHpdGatewayAdmin(new GatewayAdminEndpointOptions
        {
            AuthenticationScheme = "different",
            RateLimitPolicy = "hpd-rate",
            RequestTimeoutPolicy = "hpd-timeout",
            CapabilityPolicies = GatewayAdminCapabilities.All.ToImmutableDictionary(
                static capability => capability, static _ => "hpd-policy", StringComparer.Ordinal),
        });
        map.Should().Throw<InvalidOperationException>().WithMessage("*do not match*");
    }

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
