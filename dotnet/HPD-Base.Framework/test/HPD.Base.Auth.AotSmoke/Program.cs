using System.Security.Claims;
using HPD.Base;
using HPD.Base.Auth;
using HPD.Base.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System.Security.Cryptography;
using System.Text.Json;

string dataSource = Path.Combine(Path.GetTempPath(), "hpd-base-auth-aot-" + Guid.NewGuid().ToString("N") + ".db");
try
{
var services = new ServiceCollection();
services.AddLogging();
services.AddHPDBaseHPDAuth(options =>
{
    options.RequireHPDAuthServices = false;
    options.CollectionRules =
    [
        new HPDAuthBaseCollectionRule
        {
            CollectionId = "items",
            ReadRoles = ["Reader", "Writer"],
            WriteRoles = ["Writer"],
            RequireTenantMatch = false,
        }
    ];
});
BaseCollection<JsonElement> items = BaseCollection<JsonElement>.Create(
    new CollectionDefinition
    {
        Id = "items",
        Name = "items",
        Kind = BaseCollectionKinds.Document,
        SchemaMode = SchemaMode.Loose,
        UnknownFields = UnknownFieldPolicy.Preserve,
        MutationMode = BaseCollectionMutationMode.Mutable,
    },
    HPDBaseJsonSerializerContext.Default.JsonElement,
    static _ => { });
services.AddHPDBase(builder => builder
    .ConfigureSchema(options =>
    {
        options.ApplicationId = "hpd.base.auth.aot";
        options.PlanProtectionKey = Enumerable.Repeat((byte)0x73, 32).ToArray();
    })
    .ConfigureTokenProtection(options => options.ActiveKey = new BaseOpaqueTokenKey
    {
        Id = 3,
        Key = Enumerable.Repeat((byte)0x74, 32).ToArray(),
    })
    .AddCollection(items)
    .UseSqlite(options =>
    {
        options.StoreId = "auth.sqlite";
        options.DataSource = dataSource;
        options.AdministrationEnabled = true;
    }));

await using var provider = services.BuildServiceProvider();
IBaseSchemaManager schemas = provider.GetRequiredService<IBaseSchemaManager>();
BaseSchemaPlan schemaPlan = (await schemas.PlanAsync(new BaseSchemaPlanRequest { StoreId = "auth.sqlite" })).Value!;
Require((await schemas.ApplyAsync(new BaseSchemaApplyRequest { ProtectedArtifact = schemaPlan.ProtectedArtifact })).IsSuccess(), "Schema apply failed.");
Require((await provider.GetRequiredService<IHPDBaseApplication>().InitializeAsync()).IsSuccess(), "BASE initialization failed.");
var mapper = provider.GetRequiredService<HPDAuthBaseSubjectMapper>();
var principal = mapper.Map(new ClaimsPrincipal(new ClaimsIdentity(
[
    new Claim("sub", "user-1"),
    new Claim("role", "Reader"),
    new Claim("instance_id", "tenant-1")
], "HPD")));

var evaluator = provider.GetRequiredService<IPolicyEvaluator>();
var decision = await evaluator.EvaluateAsync(new PolicyEvaluationRequest
{
    Principal = principal,
    Operation = new OperationContext
    {
        Operation = BaseOperationKind.List,
        CollectionId = "items",
        Now = DateTimeOffset.UnixEpoch
    },
    Collection = new CollectionDefinition
    {
        Id = "items",
        Name = "items",
        Kind = BaseCollectionKinds.Document,
        SchemaMode = SchemaMode.Loose,
        UnknownFields = UnknownFieldPolicy.Preserve
    },
    Resource = new PolicyResource { Kind = PolicyResourceKind.Query }
});

Require(decision.Effect == PolicyEffect.Allow, "HPD.Auth adapter policy did not allow the smoke principal.");

PrincipalContext writer = mapper.Map(new ClaimsPrincipal(new ClaimsIdentity(
[
    new Claim("sub", "writer-1"),
    new Claim("role", "Writer"),
], "HPD")));
PrincipalContext denied = mapper.Map(new ClaimsPrincipal(new ClaimsIdentity(
[
    new Claim("sub", "denied-1"),
], "HPD")));
PrincipalContext admin = mapper.Map(new ClaimsPrincipal(new ClaimsIdentity(
[
    new Claim("sub", "admin-1"),
    new Claim("role", "Admin"),
], "HPD")));
BaseMutationRequestIdentity requestIdentity = BaseMutationRequestIdentity.Create(
    "auth-aot", "create-item", "request-1",
    BaseMutationRequestFingerprint.Create(SHA256.HashData("auth-aot-request"u8)));
BaseBatchBuilder first = provider.GetRequiredService<IBaseSessionFactory>().For(writer).Atomic(requestIdentity);
first.Create(items, new RecordId("item-1"), Json("value"));
Require((await first.CommitAsync()) is BaseSuccess<BaseBatchResult>, "Authorized atomic request failed.");

BaseBatchBuilder deniedReplay = provider.GetRequiredService<IBaseSessionFactory>().For(denied).Atomic(requestIdentity);
deniedReplay.Create(items, new RecordId("item-1"), Json("value"));
BaseResult<BaseBatchResult> deniedReceipt = await deniedReplay.CommitAsync();
Require(
    deniedReceipt is BaseFailure<BaseBatchResult> { Status: OperationStatus.PolicyDenied },
    "Current HPD.Auth policy did not gate duplicate receipt disclosure.");

IHPDBaseAdministration administration = provider.GetRequiredService<IHPDBaseApplication>().Administration;
BaseResult<BaseBackupManifest> deniedBackup = await administration.CreateBackupAsync(
    new MemoryStream(),
    new BaseBackupRequest { StoreId = "auth.sqlite", Principal = denied });
Require(
    deniedBackup is BaseFailure<BaseBackupManifest> { Status: OperationStatus.PolicyDenied },
    "HPD.Auth did not deny non-admin backup.");
var artifact = new MemoryStream();
BaseResult<BaseBackupManifest> adminBackup = await administration.CreateBackupAsync(
    artifact,
    new BaseBackupRequest { StoreId = "auth.sqlite", Principal = admin });
Require(adminBackup is BaseSuccess<BaseBackupManifest>, "HPD.Auth did not authorize admin backup.");

static JsonElement Json(string value)
{
    using JsonDocument document = JsonDocument.Parse($$"""{"value":"{{value}}"}""");
    return document.RootElement.Clone();
}

static void Require(bool condition, string message)
{
    if (!condition)
        throw new InvalidOperationException(message);
}
}
finally
{
    Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
    string directory = Path.GetDirectoryName(dataSource)!;
    string name = Path.GetFileName(dataSource);
    foreach (string file in Directory.GetFiles(directory).Where(file => Path.GetFileName(file).StartsWith(name, StringComparison.Ordinal)))
        File.Delete(file);
}
