using System.Net;
using System.Collections.Immutable;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using HPD.Auth.ControlPlane;
using HPD.Base;
using HPD.Gateway;
using HPD.Gateway.Admission.Redis;
using HPD.Gateway.ControlPlane;
using HPD.Gateway.ControlPlane.HPDAuth;
using HPD.Gateway.ControlPlane.Sqlite;
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
    gateway.EnableCoreDeclarations();
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
    gateway.AddTrafficAdmission(admission => ConfigureAdmission(admission, inputs.RedisAdmission));
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
builder.Services.AddHpdGatewayControlPlane(controlPlane => controlPlane
    .UseSqlite(sqlite =>
    {
        sqlite.DataSource = inputs.Management.DatabasePath;
        sqlite.PlanProtectionKey = Convert.FromHexString(inputs.Management.PlanProtectionKeyHex);
        sqlite.TokenProtectionKey = Convert.FromHexString(inputs.Management.TokenProtectionKeyHex);
        sqlite.DesiredStateTokenKey = Convert.FromHexString(inputs.Management.DesiredStateTokenKeyHex);
        sqlite.EpochReservationKey = Convert.FromHexString(inputs.Management.EpochReservationKeyHex);
    })
    .AddAdminApi(options =>
    {
        options.AuthenticationScheme = JwtBearerDefaults.AuthenticationScheme;
        options.AuthorizationPolicy = adminPolicy;
        options.RateLimitPolicy = adminProfile;
        options.RequestTimeoutPolicy = adminProfile;
        options.OpenApiSecurityScheme = "Bearer";
        options.EndpointSurfaceId = "gateway-admin-v1";
        options.RequireManagementListener = true;
        options.CapabilityPolicies = GatewayAdminCapabilities.All.ToImmutableDictionary(
            static capability => capability, static _ => adminPolicy, StringComparer.Ordinal);
    })
    .AddStudio()
    .AddHpdAuth(adminProfile));
builder.Services.AddSingleton(new StandaloneManagedTarget(inputs.InitialCandidate.NamespaceId, inputs.InitialCandidate.TargetNodeId));
builder.Services.AddHostedService<StandaloneManagementInitializer>();

await using var application = builder.Build();
application.UseRouting();
application.UseHpdGatewayListenerRoles();
application.UseHPDControlPlaneCorrelation();
application.UseRequestTimeouts();
application.UseAuthentication();
application.UseRateLimiter();
application.UseAuthorization();
application.MapHpdGateway();
application.MapHpdGatewayControlPlane();
application.ValidateHpdGatewayEndpointRoles();
await application.RunAsync();

static void ConfigureAdmission(
    GatewayTrafficAdmissionRegistryBuilder admission,
    GatewayStandaloneRedisAdmissionInputs? redis)
{
    admission.AddPartitionProjector("standalone-subject", ProjectorIdentity("subject", ClaimTypes.NameIdentifier),
        new StandaloneClaimPartitionProjector(ClaimTypes.NameIdentifier));
    admission.AddPartitionProjector("standalone-tenant", ProjectorIdentity("tenant", "hpd_namespace"),
        new StandaloneClaimPartitionProjector("hpd_namespace"));
    admission.AddPartitionProjector("standalone-consumer", ProjectorIdentity("consumer", "hpd_consumer"),
        new StandaloneClaimPartitionProjector("hpd_consumer"));
    foreach ((TrafficAdmissionPartitionKind partition, string suffix, string? projector) in BuiltInPartitions())
    {
        void Configure(GatewayLocalAdmissionOptions options)
        {
            options.Partition = partition;
            options.PartitionProjector = projector;
        }
        admission.AddLocalFixedWindow($"local-fixed-{suffix}", Configure);
        admission.AddLocalSlidingWindow($"local-sliding-{suffix}", Configure);
        admission.AddLocalTokenBucket($"local-token-{suffix}", Configure);
        admission.AddLocalConcurrency($"local-concurrency-{suffix}", Configure);
    }
    if (redis is null) return;
    admission.UseRedis("redis", options =>
    {
        options.AuthorityId = redis.AuthorityId;
        options.Configuration = redis.Configuration;
        options.KeyPrefix = redis.KeyPrefix;
        options.Database = redis.Database;
        options.OperationTimeout = redis.OperationTimeout;
        options.MaximumConcurrentInvocations = redis.MaximumConcurrentInvocations;
    });
    foreach ((TrafficAdmissionPartitionKind partition, string suffix, string? projector) in BuiltInPartitions())
    {
        void Configure(GatewaySharedAdmissionProfileOptions options)
        {
            options.Partition = partition;
            options.PartitionProjector = projector;
        }
        admission.AddSharedFixedWindow($"shared-fixed-{suffix}", "redis", Configure);
        admission.AddSharedSlidingWindow($"shared-sliding-{suffix}", "redis", Configure);
        admission.AddSharedTokenBucket($"shared-token-{suffix}", "redis", Configure);
    }
}

static ContentHash ProjectorIdentity(string kind, string claim) => new(
    "sha-256",
    Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes($"hpd.gateway.standalone.partition/v1|{kind}|{claim}"))));

static (TrafficAdmissionPartitionKind Partition, string Suffix, string? Projector)[] BuiltInPartitions() =>
[
    (TrafficAdmissionPartitionKind.Global, "global", null),
    (TrafficAdmissionPartitionKind.Route, "route", null),
    (TrafficAdmissionPartitionKind.SourceIp, "source-ip", null),
    (TrafficAdmissionPartitionKind.AuthenticatedSubject, "subject", "standalone-subject"),
    (TrafficAdmissionPartitionKind.Tenant, "tenant", "standalone-tenant"),
    (TrafficAdmissionPartitionKind.Consumer, "consumer", "standalone-consumer")
];

static bool OwnsNamespace(Microsoft.AspNetCore.Authorization.AuthorizationHandlerContext context) =>
    context.Resource is GatewayAdminResource resource &&
    context.User.FindAll("hpd_namespace").Select(static claim => claim.Value)
        .Contains(resource.NamespaceId, StringComparer.Ordinal);

internal sealed class StandaloneManagementInitializer(
    IGatewayManagementCommandCoordinator commands,
    StandaloneManagedTarget target) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
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

internal sealed class StandaloneClaimPartitionProjector(string claimType) : IGatewayAdmissionPartitionProjector
{
    public ValueTask<GatewayAdmissionPartitionResult> ProjectAsync(
        GatewayAdmissionPartitionContext context,
        CancellationToken cancellationToken)
    {
        if (cancellationToken.IsCancellationRequested)
            return ValueTask.FromResult(GatewayAdmissionPartitionResult.Failed(GatewayAdmissionPartitionFailure.Canceled));
        string[] values = context.Principal.FindAll(claimType).Select(static claim => claim.Value).Distinct(StringComparer.Ordinal).ToArray();
        return ValueTask.FromResult(values.Length == 1
            ? GatewayAdmissionPartitionResult.Success(values[0])
            : GatewayAdmissionPartitionResult.Failed(GatewayAdmissionPartitionFailure.Unavailable));
    }
}
