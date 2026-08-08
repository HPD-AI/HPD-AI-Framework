using System.Net;
using System.Collections.Immutable;
using System.Security.Claims;
using HPD.Auth.ControlPlane;
using HPD.Base;
using HPD.Base.Sqlite;
using HPD.Gateway;
using HPD.Gateway.Admin;
using HPD.Gateway.HPDAuth;
using HPD.Gateway.Hosting;
using HPD.Gateway.Inspection;
using HPD.Gateway.Management;
using HPD.Gateway.OutputCaching;
using HPD.Gateway.Resilience;
using HPD.Gateway.Standalone;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.RateLimiting;
using System.Threading.RateLimiting;

if (args.Length != 1)
    throw new InvalidOperationException("Usage: HPD.Gateway.Standalone <absolute-bootstrap-json-path>");

var inputs = GatewayStandaloneBootstrapReader.Read(args[0]);
var builder = WebApplication.CreateSlimBuilder(args);
builder.UseHpdGatewayHost(inputs.Host, certificates =>
{
    foreach (var (reference, source) in inputs.Certificates)
        certificates.Add(reference, source);
});
builder.Services.AddHpdGateway(gateway =>
{
    gateway.AddCoreFamilies();
    gateway.AddRequestInspection(
        inspectors => inspectors.Add("standalone-unencoded", new StandaloneUnencodedInspector()));
    gateway.ProtectCredentialHeaders("x-api-key");
    gateway.AddUpstreamResilience(profiles => profiles.Add(new GatewayResilienceProfile
    {
        Name = "standalone-safe",
        Version = 1,
        Retry = new GatewayResponseRetryProfile
        {
            StatusCodes = [HttpStatusCode.ServiceUnavailable],
            MaximumRetryAttempts = 1
        }
    }));
    gateway.AddOutputCaching(profiles => profiles.Add(new GatewayOutputCacheProfile
    {
        Name = "standalone-cache",
        Version = 1,
        Expiration = TimeSpan.FromMinutes(1)
    }));
    gateway.UseInitialCandidate(inputs.InitialCandidate);
});
const string adminProfile = "gateway-management";
const string adminPolicy = "gateway-management-access";
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.Authority = inputs.Management.JwtAuthority;
        options.Audience = inputs.Management.JwtAudience;
        options.RequireHttpsMetadata = true;
    });
builder.Services.AddAuthorization(options =>
{
    options.AddPolicy(adminPolicy, policy => policy.RequireAuthenticatedUser());
    options.AddPolicy(GatewayAdminResourcePolicies.Namespace, policy => policy.RequireAuthenticatedUser());
    options.AddPolicy(GatewayAdminResourcePolicies.Target, policy => policy.RequireAuthenticatedUser());
    options.AddPolicy(GatewayAdminResourcePolicies.Administration, policy => policy.RequireAuthenticatedUser());
});
builder.Services.AddRateLimiter(options => options.AddFixedWindowLimiter(adminProfile, limiter =>
{
    limiter.PermitLimit = 128;
    limiter.QueueLimit = 0;
    limiter.Window = TimeSpan.FromSeconds(1);
    limiter.AutoReplenishment = true;
}));
builder.Services.AddRequestTimeouts(options => options.AddPolicy(adminProfile, TimeSpan.FromSeconds(30)));
builder.Services.AddHPDControlPlane(options =>
{
    options.AddProfile(adminProfile, profile =>
    {
        profile.AuthenticationScheme = JwtBearerDefaults.AuthenticationScheme;
        profile.AuthenticationProfile = adminProfile;
        profile.ActorIdentifierClaim = ClaimTypes.NameIdentifier;
        profile.TenantClaim = "hpd_namespace";
        profile.RateLimitPolicy = adminProfile;
        profile.RequestTimeoutPolicy = adminProfile;
        profile.OpenApiSecurityScheme = "Bearer";
    });
    foreach (string capability in GatewayAdminCapabilities.All)
        options.MapCapability(capability, adminPolicy);
});
builder.Services.AddHPDControlPlaneOpenApi("hpd-gateway-v1");
builder.Services.AddHpdGatewayAdmin();
builder.Services.AddHpdGatewayAdminHpdAuth(adminProfile);
builder.Services.AddHostedService<StandaloneManagementInitializer>();
builder.Services.AddHpdGatewayManagement(options =>
{
    options.ManagementAuthorityId = inputs.Management.ManagementAuthorityId;
    options.RequiredDurability = GatewayAuthorityDurability.RestartDurable;
    options.DesiredStateTokenKey = Convert.FromHexString(inputs.Management.DesiredStateTokenKeyHex);
}, hpdBase =>
{
    hpdBase.ConfigureSchema(schema =>
        schema.PlanProtectionKey = Convert.FromHexString(inputs.Management.PlanProtectionKeyHex));
    hpdBase.ConfigureTokenProtection(tokens => tokens.ActiveKey = new BaseOpaqueTokenKey
    {
        Id = 1,
        Key = Convert.FromHexString(inputs.Management.TokenProtectionKeyHex),
    });
    hpdBase.UseSqlite(sqlite =>
    {
        sqlite.StoreId = "gateway-management";
        sqlite.DataSource = inputs.Management.DatabasePath;
        sqlite.AdministrationEnabled = true;
        sqlite.AllowClientRequestedIds = true;
    });
});

await using var application = builder.Build();
application.UseRouting();
application.UseHpdGatewayListenerRoles();
application.UseHPDControlPlaneCorrelation();
application.UseRequestTimeouts();
application.UseAuthentication();
application.UseRateLimiter();
application.UseAuthorization();
application.MapHpdGateway();
application.MapHpdGatewayAdmin(new GatewayAdminEndpointOptions
{
    AuthenticationScheme = JwtBearerDefaults.AuthenticationScheme,
    RateLimitPolicy = adminProfile,
    RequestTimeoutPolicy = adminProfile,
    EndpointSurfaceId = "gateway-admin-v1",
    RequireManagementListener = true,
    CapabilityPolicies = GatewayAdminCapabilities.All.ToImmutableDictionary(
        static capability => capability, static _ => adminPolicy, StringComparer.Ordinal),
});
await application.RunAsync();

internal sealed class StandaloneManagementInitializer(
    IBaseSchemaManager schemas,
    IGatewayAuthorityRuntime authority) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        BaseSchemaPlan plan = (await schemas.PlanAsync(new BaseSchemaPlanRequest
        {
            StoreId = "gateway-management",
        }, cancellationToken).ConfigureAwait(false)).Value
            ?? throw new InvalidOperationException("The Gateway management schema plan is unavailable.");
        OperationResult<BaseSchemaApplyResult> applied = await schemas.ApplyAsync(new BaseSchemaApplyRequest
        {
            ProtectedArtifact = plan.ProtectedArtifact,
        }, cancellationToken).ConfigureAwait(false);
        if (!applied.IsSuccess())
            throw new InvalidOperationException("The Gateway management schema could not be applied.");
        await authority.InitializeAsync(cancellationToken).ConfigureAwait(false);
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}

internal sealed class StandaloneUnencodedInspector : IGatewayRequestInspector
{
    public ValueTask<GatewayInspectionDecision> InspectAsync(
        GatewayInspectionContext context,
        CancellationToken cancellationToken) =>
        ValueTask.FromResult(context.ContentEncoded
            ? GatewayInspectionDecision.Reject("encoded-body-unsupported", 415)
            : GatewayInspectionDecision.Allow());
}
