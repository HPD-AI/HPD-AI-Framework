using HPD.Auth.ControlPlane;
using HPD.Base.AspNetCore;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Options;
using System.Security.Claims;
using System.Text.Encodings.Web;

namespace HPD.Base.Auth.Tests.AspNetCore.Integration;

public sealed class ControlPlaneCompositionTests
{
    [Fact]
    public void MapperBindsEveryEndpointToItsExactL1Capability()
    {
        WebApplicationBuilder builder = WebApplication.CreateBuilder();
        builder.Services.AddAuthorization(options =>
            options.AddPolicy("BaseControlPlane", policy => policy.RequireAuthenticatedUser()));
        builder.Services.AddHPDControlPlane(options =>
        {
            options.AddProfile("base-management", profile =>
            {
                profile.AuthenticationScheme = "management";
                profile.AuthenticationProfile = "management";
                profile.ActorIdentifierClaim = "sub";
            });
            foreach (string capability in Capabilities)
                options.MapCapability(capability, "BaseControlPlane");
        });
        builder.Services.AddHPDBase(hpd => hpd.AddAspNetCore().AddHPDAuth(options =>
            options.RequireHPDAuthServices = false));

        using WebApplication app = builder.Build();
        app.MapHPDBaseControlPlane(new()
        {
            RoutePrefix = "/base",
            Profile = "base-management"
        });

        RouteEndpoint[] endpoints = ((IEndpointRouteBuilder)app).DataSources.SelectMany(static source => source.Endpoints)
            .OfType<RouteEndpoint>()
            .Where(endpoint => endpoint.Metadata.GetMetadata<HPDBaseEndpointDescriptor>() is not null)
            .ToArray();
        endpoints.Should().NotBeEmpty();
        foreach (RouteEndpoint endpoint in endpoints)
        {
            HPDBaseEndpointDescriptor descriptor = endpoint.Metadata.GetRequiredMetadata<HPDBaseEndpointDescriptor>();
            descriptor.Audience.Should().Be(HPDBaseEndpointAudience.ControlPlane);
            endpoint.Metadata.GetOrderedMetadata<ControlPlaneEndpointMetadata>().Should().ContainSingle(metadata => metadata.Profile == "base-management");
            endpoint.Metadata.GetOrderedMetadata<ControlPlaneCapabilityMetadata>().Should().ContainSingle(metadata => metadata.Capability == descriptor.Capability);
            endpoint.Metadata.GetOrderedMetadata<IAuthorizeData>().Should().HaveCountGreaterThanOrEqualTo(2);
        }
    }

    [Fact]
    public void FinalPackageExposesOnlyTheUnifiedBuilderEntryPoint()
    {
        Type[] exported = typeof(HPDBaseAuthBuilderExtensions).Assembly.GetExportedTypes();
        exported.Select(static type => type.Name).Should().NotContain([
            "HPDBaseAuthSubjectProjector",
            "HPDBaseAuthPolicyEvaluator",
            "HPDBaseAuthDescriptorContributor",
            "HPDBaseAuthHealthContributor",
            "HPDBaseAuthUserManagerPrincipalEnricher"
        ]);
        typeof(HPDBaseAuthBuilderExtensions).GetMethods()
            .Should().ContainSingle(method => method.Name == "AddHPDAuth");
    }

    [Fact]
    public async Task AuthorizedRequestUsesL1ActorAndCorrelationComposition()
    {
        WebApplicationBuilder builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Testing" });
        builder.WebHost.UseTestServer();
        builder.Services.AddAuthentication(TestAuthenticationHandler.SchemeName)
            .AddScheme<AuthenticationSchemeOptions, TestAuthenticationHandler>(TestAuthenticationHandler.SchemeName, static _ => { });
        builder.Services.AddAuthorization(options =>
            options.AddPolicy("BaseControlPlane", policy => policy.RequireRole("Admin")));
        builder.Services.AddHPDControlPlane(options =>
        {
            options.AddProfile("base-management", profile =>
            {
                profile.AuthenticationScheme = TestAuthenticationHandler.SchemeName;
                profile.AuthenticationProfile = "test";
                profile.ActorIdentifierClaim = "sub";
            });
            foreach (string capability in Capabilities)
                options.MapCapability(capability, "BaseControlPlane");
        });
        builder.Services.AddHPDBase(hpd => hpd
            .AddAspNetCore()
            .AddHPDAuth(options =>
            {
                options.RequireHPDAuthServices = false;
                options.EnrichFromUserManager = false;
                options.AdminRoleNames = ["Admin"];
                options.AllowAdminBypass = true;
            }));

        await using WebApplication app = builder.Build();
        app.UseHPDControlPlaneCorrelation();
        app.UseAuthentication();
        app.UseAuthorization();
        app.MapHPDBaseControlPlane(new()
        {
            RoutePrefix = "/base",
            Profile = "base-management",
            MapRecords = false,
            MapRegisteredReads = false,
            MapAdministration = true,
            MapPolicyExplain = false
        });
        await app.StartAsync();

        using HttpRequestMessage request = new(HttpMethod.Get, "/base/admin/manifest");
        request.Headers.Add("X-Correlation-ID", "l38-correlation");
        HttpResponseMessage response = await app.GetTestClient().SendAsync(request);
        response.StatusCode.Should().Be(System.Net.HttpStatusCode.OK);
    }

    [Fact]
    public async Task DuplicatePrincipalAuthorityFailsStartup()
    {
        WebApplicationBuilder builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Testing" });
        builder.WebHost.UseTestServer();
        builder.Services.AddAuthentication(TestAuthenticationHandler.SchemeName)
            .AddScheme<AuthenticationSchemeOptions, TestAuthenticationHandler>(TestAuthenticationHandler.SchemeName, static _ => { });
        builder.Services.AddAuthorization(options => options.AddPolicy("BaseControlPlane", policy => policy.RequireAuthenticatedUser()));
        builder.Services.AddHPDControlPlane(options =>
        {
            options.AddProfile("base-management", profile =>
            {
                profile.AuthenticationScheme = TestAuthenticationHandler.SchemeName;
                profile.AuthenticationProfile = "test";
                profile.ActorIdentifierClaim = "sub";
            });
            foreach (string capability in Capabilities) options.MapCapability(capability, "BaseControlPlane");
        });
        builder.Services.AddHPDBase(hpd => hpd.AddAspNetCore().AddHPDAuth(options => options.RequireHPDAuthServices = false));
        builder.Services.AddScoped<IBaseHttpPrincipalMapper, ConflictingPrincipalMapper>();

        await using WebApplication app = builder.Build();
        app.MapHPDBaseControlPlane(new() { RoutePrefix = "/base", Profile = "base-management" });

        Func<Task> action = () => app.StartAsync();
        await action.Should().ThrowAsync<InvalidOperationException>().WithMessage("base.auth.principal.ambiguous");
    }

    private static readonly string[] Capabilities =
    [
        HPDBaseCapabilities.RecordsRead,
        HPDBaseCapabilities.RecordsWrite,
        HPDBaseCapabilities.RecordsDelete,
        HPDBaseCapabilities.RecordsBatchWrite,
        HPDBaseCapabilities.FilesRead,
        HPDBaseCapabilities.FilesWrite,
        HPDBaseCapabilities.FilesDelete,
        HPDBaseCapabilities.RealtimeSubscribe,
        HPDBaseCapabilities.AdministrationMetadataRead,
        HPDBaseCapabilities.AdministrationHealthRead,
        HPDBaseCapabilities.AdministrationDiagnosticsRead,
        HPDBaseCapabilities.AdministrationRecordsRead,
        HPDBaseCapabilities.ActivationQuery,
        HPDBaseCapabilities.ActivationRetry,
        HPDBaseCapabilities.ActivationReconcile,
        HPDBaseCapabilities.ActivationDispose,
        HPDBaseCapabilities.ActivationMaintenanceAdvance,
        HPDBaseCapabilities.ActivationRemovalAdvance,
        HPDBaseCapabilities.ActivationMigrate,
        HPDBaseCapabilities.ActivationRepairExecute,
        HPDBaseCapabilities.ActivationScheduleRead,
        HPDBaseCapabilities.ActivationScheduleMutate,
        HPDBaseCapabilities.SemanticActivationInspect,
        HPDBaseCapabilities.SemanticActivationMaintenance,
        HPDBaseCapabilities.PolicyExplain
    ];

    private sealed class TestAuthenticationHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder)
        : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        internal const string SchemeName = "L38Test";
        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            ClaimsIdentity identity = new([
                new Claim("sub", "admin-1"),
                new Claim("role", "Admin")
            ], SchemeName, "sub", "role");
            return Task.FromResult(AuthenticateResult.Success(
                new AuthenticationTicket(new ClaimsPrincipal(identity), SchemeName)));
        }
    }

    private sealed class ConflictingPrincipalMapper : IBaseHttpPrincipalMapper
    {
        public ValueTask<PrincipalContext> MapAsync(HttpContext httpContext, HPDBaseEndpointDescriptor endpoint, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(new PrincipalContext { AuthenticationState = PrincipalAuthenticationState.Authenticated });
    }
}
