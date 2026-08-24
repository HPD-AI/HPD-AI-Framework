using System.Diagnostics;
using System.Text;
using System.Text.Json.Serialization;
using HPD.Base;
using HPD.Base.Sqlite;
using HPD.Base.Testing;
using HPD.Payments.Persistence.AtomicDomains;
using HPD.Payments.Persistence.Ports;
using HPD.Payments.Persistence.Receipts;
using HPD.Payments.Profiles.Embedded;
using HPD.Payments.Contracts.HeldPosition.QuotaWallet;
using HPD.Payments.Primitives.Identity;
using HPD.Payments.Primitives.Time;
using HPD.Payments.Runtime.Base;
using HPD.Payments.Runtime.QuotaWallet;
using HPD.Payments.Supporting.History;
using HPD.Payments.Supporting.Ownership;

string[] boundaries =
[
    "append-claim-invoke-verify", "compare-bind-conflict-before-invoke", "cancel-before-send",
    "timeout-after-possible-send", "claim-before-result-append", "send-before-acknowledgement",
    "takeover-before-stale-result", "compatibility-reject-before-invoke", "verified-before-residual-branch",
    "request-before-delete-result", "verified-absence-before-restore",
];

if (args is ["child", string path, string childBoundary, string stage])
{
    if (stage == "before") Process.GetCurrentProcess().Kill();
    await using BaseTestHost childHost = await CreateHost(path);
    IOwnerPersistencePort<BoundaryFact> childPort = Profile(childHost).CreateOwnerPort(Codec());
    OwnerAppendReceipt<BoundaryFact> result = await childPort.CompareBindAppendAsync(Request(childBoundary, stage));
    Require(result.Disposition == OwnerAppendDisposition.Appended, $"Child append failed at {childBoundary}/{stage}.");
    Process.GetCurrentProcess().Kill();
    return;
}

if (args is ["quota-child", string quotaPath])
{
    await using BaseTestHost childHost = await CreateHost(quotaPath);
    QuotaWalletFact fact = IndeterminateQuotaFact();
    OwnerAppendReceipt<QuotaWalletFact> result = await Profile(childHost).CreateOwnerPort(QuotaCodec())
        .CompareBindAppendAsync(QuotaRequest("indeterminate-reservation", fact));
    Require(result.Disposition == OwnerAppendDisposition.Appended, "Child quota reservation append failed.");
    Process.GetCurrentProcess().Kill();
    return;
}

string databasePath = Path.Combine(Path.GetTempPath(), $"hpd-payments-l5-10-{Guid.NewGuid():N}.db");
try
{
    await using (BaseTestHost bootstrap = await CreateHost(databasePath))
        Require(bootstrap.Features.Provider == "sqlite", "L5-10 certification did not use SQLite provider authority.");

    foreach (string boundary in boundaries)
    {
        await RunChild(databasePath, boundary, "before");
        await using (BaseTestHost beforeHost = await CreateHost(databasePath))
            Require(await HasNoFacts(beforeHost, boundary, "before"),
                $"Crash before {boundary} fabricated a durable runtime fact.");

        await RunChild(databasePath, boundary, "after");
        await using BaseTestHost afterHost = await CreateHost(databasePath);
        OwnerHistoryPage<BoundaryFact> recovered = await Read(afterHost, boundary, "after");
        Require(recovered.Facts.SequenceEqual([new BoundaryFact(boundary, "after")]),
            $"Crash after {boundary} did not recover exactly one durable runtime fact.");
        Require((await Profile(afterHost).CreateOwnerPort(Codec()).CompareBindAppendAsync(Request(boundary, "after"))).Disposition
            == OwnerAppendDisposition.Replay, $"Recovered {boundary} identity did not replay exactly.");
    }


    await RunQuotaChild(databasePath);
    await using (BaseTestHost quotaHost = await CreateHost(databasePath))
    {
        QuotaWalletFact expectedQuota = IndeterminateQuotaFact();
        OwnerHistoryPage<QuotaWalletFact> recoveredQuota = await ReadQuota(quotaHost, "indeterminate-reservation");
        Require(recoveredQuota.Facts.SequenceEqual([expectedQuota]),
            "Crash recovery flattened or released the indeterminate quota reservation.");
        Require((await Profile(quotaHost).CreateOwnerPort(QuotaCodec())
            .CompareBindAppendAsync(QuotaRequest("indeterminate-reservation", expectedQuota))).Disposition == OwnerAppendDisposition.Replay,
            "Recovered quota reservation did not replay exactly.");

        QuotaWalletFact walletFact = StaleWalletFact();
        Require((await Profile(quotaHost).CreateOwnerPort(QuotaCodec())
            .CompareBindAppendAsync(QuotaRequest("stale-wallet-generation", walletFact))).Disposition == OwnerAppendDisposition.Appended,
            "Generation-fenced wallet decision was not durably admitted.");
        Require((await ReadQuota(quotaHost, "stale-wallet-generation")).Facts.SequenceEqual([walletFact]),
            "Generation-fenced wallet rejection did not recover exactly.");
    }

    Console.WriteLine($"L5-10/RES-009 physical certification passed: {boundaries.Length} boundaries plus indeterminate quota and generation-fenced wallet recovery on SQLite.");
}
finally
{
    foreach (string suffix in new[] { "", "-shm", "-wal", ".restore-recovery" })
    {
        string candidate = databasePath + suffix;
        if (File.Exists(candidate)) File.Delete(candidate);
    }
}

static async Task RunChild(string path, string boundary, string stage)
{
    string processPath = Environment.ProcessPath ?? throw new InvalidOperationException("Executable path is unavailable.");
    bool frameworkDependent = string.Equals(Path.GetFileNameWithoutExtension(processPath), "dotnet", StringComparison.OrdinalIgnoreCase);
    var start = new ProcessStartInfo(processPath) { UseShellExecute = false, RedirectStandardError = true, RedirectStandardOutput = true };
    if (frameworkDependent) start.ArgumentList.Add(Path.Combine(AppContext.BaseDirectory, "HPD.Payments.Runtime.Persistence.Tests.dll"));
    start.ArgumentList.Add("child"); start.ArgumentList.Add(path); start.ArgumentList.Add(boundary); start.ArgumentList.Add(stage);
    using Process process = Process.Start(start) ?? throw new InvalidOperationException("Runtime H7 child could not start.");
    await process.WaitForExitAsync();
    Require(process.ExitCode != 0, $"Runtime H7 child did not terminate abruptly at {boundary}/{stage}.");
}

static async Task RunQuotaChild(string path)
{
    string processPath = Environment.ProcessPath ?? throw new InvalidOperationException("Executable path is unavailable.");
    bool frameworkDependent = string.Equals(Path.GetFileNameWithoutExtension(processPath), "dotnet", StringComparison.OrdinalIgnoreCase);
    var start = new ProcessStartInfo(processPath) { UseShellExecute = false, RedirectStandardError = true, RedirectStandardOutput = true };
    if (frameworkDependent) start.ArgumentList.Add(Path.Combine(AppContext.BaseDirectory, "HPD.Payments.Runtime.Persistence.Tests.dll"));
    start.ArgumentList.Add("quota-child"); start.ArgumentList.Add(path);
    using Process process = Process.Start(start) ?? throw new InvalidOperationException("RES-009 child could not start.");
    await process.WaitForExitAsync();
    Require(process.ExitCode != 0, "RES-009 child did not terminate abruptly after quota persistence.");
}

static IEmbeddedPaymentsProfile Profile(BaseTestHost host) => EmbeddedPaymentsProfile.Sqlite(Session(host),
    Revision.Create("credential", 1), Revision.Create("configuration", 1));
static PaymentsFactJsonCodec<BoundaryFact> Codec() => new("l5-10-boundary-v1", BoundaryJsonContext.Default.BoundaryFact);
static PaymentsFactJsonCodec<QuotaWalletFact> QuotaCodec() => new("res-009-quota-wallet-v1", BoundaryJsonContext.Default.QuotaWalletFact);

static OwnerAppendRequest<BoundaryFact> Request(string boundary, string stage)
{
    ScopeId scope = ScopeId.Create("tenant-runtime", "l5-10", "h7");
    OwnerReference owner = new(FrozenAuthority.WorkRequirement,
        SemanticId.Create(scope, "runtime", "boundary", boundary + "-" + stage), OwnerGeneration.Create(1));
    AtomicDomain domain = new(SemanticId.Create(scope, "runtime", "domain", "local"), AtomicDomainKind.Local, Revision.Create("topology", 1));
    CanonicalDigestProfileId profile = new("l5-10", ContractVersion.Create(1, 0), "boundary", "ordinal", "utc", "canonical", "runtime");
    return new(owner, CanonicalDigest.Sha256(profile, Encoding.UTF8.GetBytes(boundary + ":" + stage)), domain, new(boundary, stage));
}

static OwnerAppendRequest<QuotaWalletFact> QuotaRequest(string operation, QuotaWalletFact fact)
{
    ScopeId scope = ScopeId.Create("tenant-runtime", "res-009", "quota-wallet");
    OwnerReference owner = new(FrozenAuthority.HeldPosition,
        SemanticId.Create(scope, "quota-wallet", "operation", operation), OwnerGeneration.Create(1));
    AtomicDomain domain = new(SemanticId.Create(scope, "quota-wallet", "domain", "local"), AtomicDomainKind.Local, Revision.Create("topology", 1));
    CanonicalDigestProfileId profile = new("res-009", ContractVersion.Create(1, 0), "quota-wallet", "ordinal", "utc", "canonical", "runtime");
    return new(owner, CanonicalDigest.Sha256(profile, Encoding.UTF8.GetBytes($"{fact.Kind}:{fact.State}:{fact.Quantity}:{fact.Generation}")), domain, fact);
}

static QuotaWalletFact IndeterminateQuotaFact()
{
    ScopeId scope = ScopeId.Create("tenant-runtime", "res-009", "quota-wallet");
    SemanticId operation = SemanticId.Create(scope, "quota-wallet", "operation", "indeterminate-reservation");
    QuotaReservationProtocol reservation = QuotaReservationProtocol.FromAdmission(operation,
        new QuotaAdmissionResult(QuotaAdmissionKind.Indeterminate, 10, 4));
    return new("quota", reservation.State.ToString(), reservation.Quantity, 0);
}

static QuotaWalletFact StaleWalletFact()
{
    ScopeId scope = ScopeId.Create("tenant-runtime", "res-009", "quota-wallet");
    SemanticId lot = SemanticId.Create(scope, "quota-wallet", "lot", "one");
    OwnerGeneration planned = OwnerGeneration.Create(2);
    QuotaAdmissionKind result = WalletPlanAdmission.Admit([new WalletSourceSlice(lot, 4, planned)],
        new Dictionary<SemanticId, OwnerGeneration> { [lot] = OwnerGeneration.Create(3) }, false, false);
    return new("wallet", result.ToString(), 4, planned.Value);
}

static async ValueTask<OwnerHistoryPage<BoundaryFact>> Read(BaseTestHost host, string boundary, string stage)
{
    OwnerAppendRequest<BoundaryFact> request = Request(boundary, stage);
    OwnerReference through = new(request.ExpectedOwner.Authority, request.ExpectedOwner.SubjectId, OwnerGeneration.Create(2));
    var frame = new HistoricalFrame(HPD.Payments.Supporting.History.HistoricalFrameKind.AsKnownAt,
        NamedTime.Create(TimeKind.Record, DateTimeOffset.UnixEpoch), [new HPD.Payments.Supporting.History.OwnerCut(through)]);
    return await Profile(host).CreateOwnerPort(Codec()).ReadHistoryAsync(new(through, frame, 4));
}

static async ValueTask<OwnerHistoryPage<QuotaWalletFact>> ReadQuota(BaseTestHost host, string operation)
{
    OwnerAppendRequest<QuotaWalletFact> request = QuotaRequest(operation,
        operation == "indeterminate-reservation" ? IndeterminateQuotaFact() : StaleWalletFact());
    OwnerReference through = new(request.ExpectedOwner.Authority, request.ExpectedOwner.SubjectId, OwnerGeneration.Create(2));
    var frame = new HistoricalFrame(HPD.Payments.Supporting.History.HistoricalFrameKind.AsKnownAt,
        NamedTime.Create(TimeKind.Record, DateTimeOffset.UnixEpoch), [new HPD.Payments.Supporting.History.OwnerCut(through)]);
    return await Profile(host).CreateOwnerPort(QuotaCodec()).ReadHistoryAsync(new(through, frame, 4));
}

static async ValueTask<bool> HasNoFacts(BaseTestHost host, string boundary, string stage)
{
    try { _ = await Read(host, boundary, stage); return false; }
    catch (KeyNotFoundException) { return true; }
}

static async ValueTask<BaseTestHost> CreateHost(string path) => await BaseTestHost.CreateAsync(builder =>
{
    builder.UseStore(SqliteStore.Configure(options => { options.StoreId = "sqlite"; options.DataSource = path; }));
    AddOperation(builder, "hpd.payments.owner-fact.append");
    AddSource(builder, "hpd.payments.owner-fact.source", PaymentsOwnerFactEvent.Collection.Id);
    AddSource(builder, "hpd.payments.owner-fact-head.source", PaymentsOwnerFactHead.Collection.Id);
    builder.AddPaymentsOwnerFactPersistence();
});

static BaseSession Session(BaseTestHost host) => host.Session(new PrincipalContext
{
    AuthenticationState = PrincipalAuthenticationState.Service, SubjectKind = AccessSubjectKind.ServicePrincipal,
    SubjectId = "payments-runtime-certifier", CurrentTenantId = "tenant-runtime",
}, options => options.Audience = HPDBaseEndpointAudience.ControlPlane);
static void AddOperation(HPDBaseBuilder builder, string id) => builder.AddStaticGrantAuthority(Definition(id), Grant(id,
    new ResourceScope { Kind = ResourceScopeKind.Runtime, TenantId = "tenant-runtime" }));
static void AddSource(HPDBaseBuilder builder, string id, string collection) => builder.AddStaticGrantAuthority(Definition(id), Grant(collection,
    new ResourceScope { Kind = ResourceScopeKind.Collection, CollectionId = collection, TenantId = "tenant-runtime" }, id));
static BaseGrantAuthorityDefinition Definition(string id) => new() { Id = id, Version = 1, OwningModuleId = "hpd.payments",
    SourceContractId = "hpd.payments.l5-10.grants", SourceContractVersion = 1 };
static AccessGrant Grant(string action, ResourceScope scope, string? id = null) => new() { Id = id ?? action,
    ApplicationId = "hpd.base.application", ModuleId = "hpd.payments", Audience = HPDBaseEndpointAudience.ControlPlane,
    Subject = new AccessSubject { Kind = AccessSubjectKind.ServicePrincipal, Id = "payments-runtime-certifier", TenantId = "tenant-runtime" },
    Action = action, Scope = scope };
static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }

internal sealed record BoundaryFact(string Boundary, string Stage);
internal sealed record QuotaWalletFact(string Kind, string State, long Quantity, ulong Generation);
[JsonSerializable(typeof(BoundaryFact))]
[JsonSerializable(typeof(QuotaWalletFact))]
internal sealed partial class BoundaryJsonContext : JsonSerializerContext;
