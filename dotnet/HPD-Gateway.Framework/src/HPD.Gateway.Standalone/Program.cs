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
using Microsoft.IdentityModel.Tokens;

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
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = inputs.Management.JwtAuthority,
            ValidateAudience = true,
            ValidAudience = inputs.Management.JwtAudience,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(Convert.FromHexString(inputs.Management.JwtSigningKeyHex)),
        };
    });
builder.Services.AddAuthorization(options =>
{
    options.AddPolicy(adminPolicy, policy => policy.RequireAuthenticatedUser());
    options.AddPolicy(GatewayAdminResourcePolicies.Namespace, policy => policy
        .RequireAuthenticatedUser()
        .RequireAssertion(OwnsNamespace));
    options.AddPolicy(GatewayAdminResourcePolicies.Target, policy => policy
        .RequireAuthenticatedUser()
        .RequireAssertion(OwnsNamespace));
    options.AddPolicy(GatewayAdminResourcePolicies.Administration, policy => policy
        .RequireAuthenticatedUser()
        .RequireAssertion(OwnsNamespace));
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
builder.Services.AddSingleton(new StandaloneManagedTarget(inputs.InitialCandidate.NamespaceId, inputs.InitialCandidate.TargetNodeId));
builder.Services.AddHostedService<StandaloneManagementInitializer>();
builder.Services.AddHpdGatewayManagement(options =>
{
    options.ManagementAuthorityId = inputs.Management.ManagementAuthorityId;
    options.RequiredDurability = GatewayAuthorityDurability.RestartDurable;
    options.DesiredStateTokenKey = Convert.FromHexString(inputs.Management.DesiredStateTokenKeyHex);
    options.EpochReservationKey = Convert.FromHexString(inputs.Management.EpochReservationKeyHex);
}, hpdBase =>
{
    hpdBase.ConfigureSchema(schema =>
        schema.PlanProtectionKey = Convert.FromHexString(inputs.Management.PlanProtectionKeyHex));
    hpdBase.ConfigureTokenProtection(tokens => tokens.ActiveKey = new BaseOpaqueTokenKey
    {
        Id = 1,
        Key = Convert.FromHexString(inputs.Management.TokenProtectionKeyHex),
        IssueNotBefore = inputs.Management.TokenProtectionIssueNotBeforeUtc,
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
    OpenApiSecurityScheme = "Bearer",
    EndpointSurfaceId = "gateway-admin-v1",
    RequireManagementListener = true,
    CapabilityPolicies = GatewayAdminCapabilities.All.ToImmutableDictionary(
        static capability => capability, static _ => adminPolicy, StringComparer.Ordinal),
});
application.MapOpenApi()
    .WithHpdGatewayEndpointRole(GatewayListenerRole.Management, "gateway-admin-v1", requireListenerFeature: true)
    .RequireAuthorization(adminPolicy);
application.ValidateHpdGatewayEndpointRoles();
await application.RunAsync();

static bool OwnsNamespace(Microsoft.AspNetCore.Authorization.AuthorizationHandlerContext context) =>
    context.Resource is GatewayAdminResource resource &&
    context.User.FindAll("hpd_namespace").Select(static claim => claim.Value)
        .Contains(resource.NamespaceId, StringComparer.Ordinal);

internal sealed class StandaloneManagementInitializer(
    IBaseSchemaManager schemas,
    IGatewayAuthorityRuntime authority,
    IGatewayManagementCommandCoordinator commands,
    StandaloneManagedTarget target) : IHostedService
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
        GatewayManagementCommandResult provisioned = await commands.ProvisionLocalTargetAsync(new(
            target.NamespaceId, target.TargetNodeId, "standalone-initial-provision",
            new GatewayManagementActor("hpd.gateway.standalone", "system", GatewayAdminCapabilities.TargetProvision),
            "standalone-startup"), cancellationToken).ConfigureAwait(false);
        if (!provisioned.IsAccepted)
            throw new InvalidOperationException("The standalone managed target could not be provisioned.");
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}

internal sealed record StandaloneManagedTarget(string NamespaceId, string TargetNodeId);

internal sealed class StandaloneUnencodedInspector : IGatewayRequestInspector
{
    public ValueTask<GatewayInspectionDecision> InspectAsync(
        GatewayInspectionContext context,
        CancellationToken cancellationToken) =>
        ValueTask.FromResult(context.ContentEncoded
            ? GatewayInspectionDecision.Reject("encoded-body-unsupported", 415)
            : GatewayInspectionDecision.Allow());
}
