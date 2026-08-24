using System.Text;
using System.Text.Json.Serialization;
using HPD.Base;
using HPD.Base.Sqlite;
using HPD.Base.Testing;
using HPD.Payments.Adapters.Sqlite;
using HPD.Payments.Persistence.AtomicDomains;
using HPD.Payments.Persistence.Ports;
using HPD.Payments.Persistence.Receipts;
using HPD.Payments.Primitives.Identity;
using HPD.Payments.Primitives.Time;
using HPD.Payments.Runtime.Base;
using HPD.Payments.Supporting.History;
using HPD.Payments.Supporting.Ownership;

string path = Path.Combine(Path.GetTempPath(), "hpd-payments-09s-" + Guid.NewGuid().ToString("N") + ".db");
try
{
    await using BaseTestHost host = await CreateHost(path);
    PrincipalContext principal = Principal();
    BaseSession session = host.Session(principal, options => options.Audience = HPDBaseEndpointAudience.ControlPlane);
    var persistence = new BaseSqlitePaymentsPersistence(session);
    var port = persistence.CreateOwnerPort(new PaymentsFactJsonCodec<SqliteFact>("l5-09s-fact-v1", SqliteJsonContext.Default.SqliteFact));
    ScopeId scope = ScopeId.Create("tenant-sqlite-certification", "l5-09s", "sqlite");
    SemanticId ownerId = SemanticId.Create(scope, "certification", "owner", "durable");
    OwnerReference initial = new(FrozenAuthority.MeasuredFact, ownerId, OwnerGeneration.Create(1));
    AtomicDomain domain = new(SemanticId.Create(scope, "certification", "domain", "local"), AtomicDomainKind.Local, Revision.Create("topology", 1));
    CanonicalDigestProfileId profile = new("l5-09s", ContractVersion.Create(1, 0), "all", "ordinal", "utc", "canonical", "certification");
    CanonicalDigest Digest(string value) => CanonicalDigest.Sha256(profile, Encoding.UTF8.GetBytes(value));

    var firstRequest = new OwnerAppendRequest<SqliteFact>(initial, Digest("first"), domain, new("first", 10));
    OwnerAppendReceipt<SqliteFact> first = await port.CompareBindAppendAsync(firstRequest);
    Require(first.Disposition == OwnerAppendDisposition.Appended && first.ObservedGeneration.Value == 2, "Initial durable append failed.");

    // Commit succeeds but its acknowledgement is lost; exact retry must converge from the durable receipt.
    var ambiguousRequest = new OwnerAppendRequest<SqliteFact>(first.Owner, Digest("ambiguous"), domain, new("ambiguous", 20));
    host.Faults.MakeNextAtomicCommitIndeterminate();
    OwnerAppendReceipt<SqliteFact> ambiguous = await port.CompareBindAppendAsync(ambiguousRequest);
    Require(ambiguous.Disposition == OwnerAppendDisposition.Indeterminate && ambiguous.ObservedGeneration.Value == 3, "SQLite ambiguity was flattened.");
    Require((await port.CompareBindAppendAsync(ambiguousRequest)).Disposition == OwnerAppendDisposition.Replay, "Durable ambiguous replay did not converge.");

    // Independent contention over a fresh owner conserves one winner.
    OwnerReference raceOwner = new(FrozenAuthority.MeasuredFact, SemanticId.Create(scope, "certification", "owner", "race"), OwnerGeneration.Create(1));
    OwnerAppendReceipt<SqliteFact>[] race = await Task.WhenAll(Enumerable.Range(0, 32).Select(i => port.CompareBindAppendAsync(
        new(raceOwner, Digest("race-" + i), domain, new SqliteFact("race-" + i, i))).AsTask()));
    Require(race.Count(x => x.Disposition == OwnerAppendDisposition.Appended) == 1 && race.Count(x => x.Disposition == OwnerAppendDisposition.Conflict) == 31,
        "SQLite contention did not conserve one append.");

    IHPDBaseAdministration administration = host.GetRequiredService<IHPDBaseAdministration>();
    Require(administration.Capability is { Durable: true, Backup: true, Restore: true }, "Configured SQLite lifecycle capability was understated.");
    var adminPrincipal = new PrincipalContext { AuthenticationState = PrincipalAuthenticationState.System };
    using var artifact = new MemoryStream();
    BaseBackupManifest manifest = (await administration.CreateBackupAsync(artifact,
        new BaseBackupRequest { StoreId = "sqlite", Principal = adminPrincipal })).RequireValue();

    var afterBackupRequest = new OwnerAppendRequest<SqliteFact>(ambiguous.Owner, Digest("after-backup"), domain, new("after-backup", 30));
    Require((await port.CompareBindAppendAsync(afterBackupRequest)).Disposition == OwnerAppendDisposition.Appended, "Post-backup append failed.");
    artifact.Position = 0;
    BaseRestoreResult restored = (await administration.RestoreAsync(artifact, new BaseRestoreRequest
    {
        StoreId = "sqlite", Principal = adminPrincipal,
        ExpectedCurrentStoreIdentityDigest = manifest.StoreIdentityDigest,
        ExpectedArtifactStoreIdentityDigest = manifest.StoreIdentityDigest,
        IdentityMode = BaseRestoreIdentityMode.RequireCurrentStoreIdentity,
        RecoveryImageRetention = BaseRecoveryImageRetention.DeleteAfterSuccessfulRestore,
        ScheduleRestoreDomain = BaseScheduleRestoreDomain.InPlaceRecovery,
        ConfirmDestructiveReplacement = true,
    })).RequireValue();
    Require(restored.RestoreEpoch == manifest.RestoreEpoch + 1, "Restore epoch did not advance.");
    Require((await port.CompareBindAppendAsync(ambiguousRequest)).Disposition == OwnerAppendDisposition.Replay,
        "Pre-backup Payments receipt did not survive restore.");
    Require((await port.CompareBindAppendAsync(afterBackupRequest)).Disposition == OwnerAppendDisposition.Appended,
        "Post-backup mutation was not removed by restore.");

    OwnerHistoryPage<SqliteFact> history = await port.ReadHistoryAsync(new OwnerHistoryRequest(
        new OwnerReference(FrozenAuthority.MeasuredFact, ownerId, OwnerGeneration.Create(4)),
        new HistoricalFrame(HPD.Payments.Supporting.History.HistoricalFrameKind.AsKnownAt,
            NamedTime.Create(TimeKind.Record, DateTimeOffset.UnixEpoch),
            [new HPD.Payments.Supporting.History.OwnerCut(new(FrozenAuthority.MeasuredFact, ownerId, OwnerGeneration.Create(4)))]), 8));
    Require(history.Facts.SequenceEqual([new SqliteFact("first", 10), new SqliteFact("ambiguous", 20), new SqliteFact("after-backup", 30)]),
        "Restored history did not reconstruct the exact Payments lineage.");

    Console.WriteLine("L5-09S independent SQLite certification passed: durable ambiguity/replay, 32-way conservation, backup/restore epoch, receipt recovery, and exact history.");
}
finally
{
    foreach (string suffix in new[] { "", "-shm", "-wal", ".restore-recovery" }) { string candidate = path + suffix; if (File.Exists(candidate)) File.Delete(candidate); }
}

static async ValueTask<BaseTestHost> CreateHost(string path) => await BaseTestHost.CreateAsync(builder =>
{
    builder.ConfigureSchema(options => options.PlanProtectionKey = Enumerable.Repeat((byte)0x81, 32).ToArray());
    builder.ConfigureTokenProtection(options => options.ActiveKey = new BaseOpaqueTokenKey { Id = 1, Key = Enumerable.Repeat((byte)0x82, 32).ToArray(), IssueNotBefore = DateTimeOffset.UnixEpoch });
    builder.UseStore(SqliteStore.Configure(options => { options.StoreId = "sqlite"; options.DataSource = path; options.AdministrationEnabled = true; }));
    AddOperation(builder, "hpd.payments.owner-fact.append");
    AddSource(builder, "hpd.payments.owner-fact.source", PaymentsOwnerFactEvent.Collection.Id);
    AddSource(builder, "hpd.payments.owner-fact-head.source", PaymentsOwnerFactHead.Collection.Id);
    builder.AddPaymentsOwnerFactPersistence();
});

static PrincipalContext Principal() => new() { AuthenticationState = PrincipalAuthenticationState.Service, SubjectKind = AccessSubjectKind.ServicePrincipal,
    SubjectId = "payments-sqlite-certifier", CurrentTenantId = "tenant-sqlite-certification" };
static void AddOperation(HPDBaseBuilder b, string id) => b.AddStaticGrantAuthority(Def(id), Grant(id, new ResourceScope { Kind = ResourceScopeKind.Runtime, TenantId = "tenant-sqlite-certification" }));
static void AddSource(HPDBaseBuilder b, string id, string collection) => b.AddStaticGrantAuthority(Def(id), Grant(collection,
    new ResourceScope { Kind = ResourceScopeKind.Collection, CollectionId = collection, TenantId = "tenant-sqlite-certification" }, id));
static BaseGrantAuthorityDefinition Def(string id) => new() { Id = id, Version = 1, OwningModuleId = "hpd.payments", SourceContractId = "hpd.payments.09s.grants", SourceContractVersion = 1 };
static AccessGrant Grant(string action, ResourceScope scope, string? id = null) => new() { Id = id ?? action, ApplicationId = "hpd.base.application", ModuleId = "hpd.payments",
    Audience = HPDBaseEndpointAudience.ControlPlane, Subject = new AccessSubject { Kind = AccessSubjectKind.ServicePrincipal, Id = "payments-sqlite-certifier", TenantId = "tenant-sqlite-certification" }, Action = action, Scope = scope };
static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
sealed record SqliteFact(string Id, int Amount);
[JsonSerializable(typeof(SqliteFact))] internal sealed partial class SqliteJsonContext : JsonSerializerContext;
