using System.Net.Http.Json;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text.Encodings.Web;
using System.Text.Json;
using HPD.Auth.ControlPlane;
using HPD.Base;
using HPD.Base.AspNetCore;
using HPD.Base.Auth;
using HPD.Base.Sqlite;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.Options;

string dataSource = Path.Combine(Path.GetTempPath(), "hpd-base-auth-aot-" + Guid.NewGuid().ToString("N") + ".db");
try
{
    string[] capabilities =
    [
        HPDBaseCapabilities.RecordsRead, HPDBaseCapabilities.RecordsWrite,
        HPDBaseCapabilities.RecordsDelete, HPDBaseCapabilities.RecordsBatchWrite,
        HPDBaseCapabilities.FilesRead, HPDBaseCapabilities.FilesWrite,
        HPDBaseCapabilities.FilesDelete, HPDBaseCapabilities.RealtimeSubscribe,
        HPDBaseCapabilities.AdministrationMetadataRead, HPDBaseCapabilities.AdministrationHealthRead,
        HPDBaseCapabilities.AdministrationDiagnosticsRead, HPDBaseCapabilities.AdministrationRecordsRead,
        HPDBaseCapabilities.PolicyExplain,
    ];
    WebApplicationBuilder builder = WebApplication.CreateSlimBuilder(args);
    builder.Services.AddAuthentication(SmokeAuthenticationHandler.SchemeName)
        .AddScheme<AuthenticationSchemeOptions, SmokeAuthenticationHandler>(SmokeAuthenticationHandler.SchemeName, static _ => { });
    builder.Services.AddAuthorization(options =>
        options.AddPolicy("BaseControlPlane", policy => policy.RequireRole("Admin")));
    builder.Services.AddHPDControlPlane(options =>
    {
        options.AddProfile("base-management", profile =>
        {
            profile.AuthenticationScheme = SmokeAuthenticationHandler.SchemeName;
            profile.AuthenticationProfile = "aot";
            profile.ActorIdentifierClaim = "sub";
        });
        foreach (string capability in capabilities)
            options.MapCapability(capability, "BaseControlPlane");
    });

    BaseCollection<JsonElement> items = BaseCollection<JsonElement>.Create(
        new CollectionDefinition
        {
            Id = "items", Name = "items", Kind = BaseCollectionKinds.Document,
            SchemaMode = SchemaMode.Loose, UnknownFields = UnknownFieldPolicy.Preserve,
            MutationMode = BaseCollectionMutationMode.Mutable,
        },
        HPDBaseJsonSerializerContext.Default.JsonElement,
        static _ => { });
    builder.Services.AddHPDBase(hpd => hpd
        .ConfigureSchema(options =>
        {
            options.ApplicationId = "hpd.base.auth.aot";
            options.PlanProtectionKey = Enumerable.Repeat((byte)0x73, 32).ToArray();
        })
        .ConfigureTokenProtection(options => options.ActiveKey = new BaseOpaqueTokenKey
        {
            Id = 3, Key = Enumerable.Repeat((byte)0x74, 32).ToArray(), IssueNotBefore = DateTimeOffset.UnixEpoch,
        })
        .AddCollection(items)
        .AddAspNetCore()
        .AddHPDAuth(options =>
        {
            options.RequireHPDAuthServices = false;
            options.EnrichFromUserManager = false;
            options.AdminRoleNames = ["Admin"];
            options.AllowAdminBypass = true;
            options.CollectionRules = [new HPDBaseAuthCollectionRule
            {
                CollectionId = "items", ReadRoles = ["Reader", "Writer"],
                WriteRoles = ["Writer"], RequireTenantMatch = false,
            }];
        })
        .UseStore(SqliteStore.Configure(options =>
        {
            options.StoreId = "auth.sqlite";
            options.DataSource = dataSource;
            options.AdministrationEnabled = true;
        })));

    await using WebApplication app = builder.Build();
    app.UseHPDControlPlaneCorrelation();
    app.UseAuthentication();
    app.UseAuthorization();
    app.MapHPDBaseControlPlane(new()
    {
        RoutePrefix = "/base", Profile = "base-management",
        MapRecords = false, MapRegisteredReads = false,
        MapAdministration = true, MapPolicyExplain = false,
    });

    IBaseSchemaManager schemas = app.Services.GetRequiredService<IBaseSchemaManager>();
    BaseSchemaPlan plan = (await schemas.PlanAsync(new BaseSchemaPlanRequest { StoreId = "auth.sqlite" })).Value!;
    Require((await schemas.ApplyAsync(new BaseSchemaApplyRequest { ProtectedArtifact = plan.ProtectedArtifact })).IsSuccess(), "Schema apply failed.");
    app.Urls.Add("http://127.0.0.1:0");
    await app.StartAsync();
    string address = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single();
    using HttpClient client = new() { BaseAddress = new Uri(address) };
    using HttpRequestMessage management = new(HttpMethod.Get, "/base/admin/manifest");
    management.Headers.Add("X-Correlation-ID", "aot-correlation");
    Require((await client.SendAsync(management)).IsSuccessStatusCode, "Combined HPD.Auth control-plane request failed.");

    PrincipalContext writer = Principal("writer-1", "Writer");
    PrincipalContext denied = Principal("denied-1");
    BaseMutationRequestIdentity identity = BaseMutationRequestIdentity.Create(
        "auth-aot", "create-item", "request-1",
        BaseMutationRequestFingerprint.Create(SHA256.HashData("auth-aot-request"u8)));
    BaseBatchBuilder first = app.Services.GetRequiredService<IBaseSessionFactory>().For(writer).Atomic(identity);
    first.Create(items, RecordId.Create("item-1"), Json("value"));
    Require((await first.CommitAsync()) is BaseSuccess<BaseBatchResult>, "Authorized atomic request failed.");
    BaseBatchBuilder replay = app.Services.GetRequiredService<IBaseSessionFactory>().For(denied).Atomic(identity);
    replay.Create(items, RecordId.Create("item-1"), Json("value"));
    Require((await replay.CommitAsync()) is BaseFailure<BaseBatchResult> { Status: OperationStatus.PolicyDenied },
        "Current policy did not gate duplicate receipt disclosure.");
    Require(!JsonSerializer.IsReflectionEnabledByDefault, "JSON reflection fallback must be disabled.");
    await app.StopAsync();
}
finally
{
    Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
    string directory = Path.GetDirectoryName(dataSource)!;
    string name = Path.GetFileName(dataSource);
    foreach (string file in Directory.GetFiles(directory).Where(file => Path.GetFileName(file).StartsWith(name, StringComparison.Ordinal)))
        File.Delete(file);
}

static JsonElement Json(string value)
{
    using JsonDocument document = JsonDocument.Parse($$"""{"value":"{{value}}"}""");
    return document.RootElement.Clone();
}

static PrincipalContext Principal(string id, string? role = null) => new()
{
    AuthenticationState = PrincipalAuthenticationState.Authenticated,
    SubjectKind = AccessSubjectKind.User,
    SubjectId = id,
    Roles = role is null ? null : [role],
    Subjects = role is null
        ? [new AccessSubject { Kind = AccessSubjectKind.User, Id = id, Source = "hpd-auth" }]
        : [new AccessSubject { Kind = AccessSubjectKind.User, Id = id, Source = "hpd-auth" },
           new AccessSubject { Kind = AccessSubjectKind.Role, Id = role, Source = "hpd-auth" }],
    AuthSource = "hpd-auth",
};

static void Require(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
}

sealed class SmokeAuthenticationHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    internal const string SchemeName = "AotControlPlane";
    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        ClaimsIdentity identity = new([new Claim("sub", "admin-1"), new Claim("role", "Admin")], SchemeName, "sub", "role");
        return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identity), SchemeName)));
    }
}
