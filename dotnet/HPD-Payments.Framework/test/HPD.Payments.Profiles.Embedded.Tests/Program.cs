using HPD.Base;
using HPD.Base.Sqlite;
using HPD.Base.Testing;
using HPD.Payments.Connectors.Simulator.Core;
using HPD.Payments.Connectors.Simulator.Scenarios;
using HPD.Payments.Contracts.ExternalEffect;
using HPD.Payments.Profiles.Embedded;
using HPD.Payments.Primitives.Identity;
using HPD.Payments.Runtime.Base;

Revision credential = Revision.Create("credential", 1);
Revision configuration = Revision.Create("configuration", 1);

await using (BaseTestHost memoryHost = await BaseTestHost.CreateAsync(RegisterPayments))
{
    Require(memoryHost.Features.Provider == "inmemory", "InMemory graph did not retain provider authority.");
    IEmbeddedPaymentsProfile profile = EmbeddedPaymentsProfile.InMemory(Session(memoryHost), credential, configuration);
    VerifyClosedProfile(profile, EmbeddedPersistenceProvider.InMemory);
}

string databasePath = Path.Combine(Path.GetTempPath(), $"hpd-payments-embedded-{Guid.NewGuid():N}.db");
try
{
    await using BaseTestHost sqliteHost = await BaseTestHost.CreateAsync(builder =>
    {
        builder.UseStore(SqliteStore.Configure(options =>
        {
            options.StoreId = "sqlite";
            options.DataSource = databasePath;
            options.AdministrationEnabled = true;
        }));
        RegisterPayments(builder);
    });
    Require(sqliteHost.Features.Provider == "sqlite", "SQLite graph did not retain provider authority.");
    IEmbeddedPaymentsProfile profile = EmbeddedPaymentsProfile.Sqlite(Session(sqliteHost), credential, configuration);
    VerifyClosedProfile(profile, EmbeddedPersistenceProvider.Sqlite);
}
finally
{
    File.Delete(databasePath);
    File.Delete(databasePath + "-shm");
    File.Delete(databasePath + "-wal");
}

static void RegisterPayments(HPDBaseBuilder builder)
{
    builder.AddPaymentsModuleMutations();
    builder.AddPaymentsOwnerFactPersistence();
    builder.AddPaymentsSupportingPersistence();
}

static BaseSession Session(BaseTestHost host) => host.Session(new PrincipalContext
{
    AuthenticationState = PrincipalAuthenticationState.Service,
    SubjectKind = AccessSubjectKind.ServicePrincipal,
    SubjectId = "payments-embedded",
    CurrentTenantId = "tenant-one",
}, options => options.Audience = HPDBaseEndpointAudience.ControlPlane);

static void VerifyClosedProfile(IEmbeddedPaymentsProfile profile, EmbeddedPersistenceProvider expectedProvider)
{
    Require(profile.Provider == expectedProvider, "The profile changed the explicit provider selection.");
    Require(profile.Supporting is not null, "The supporting persistence surface is missing.");
    SimulatorResult uncertain = profile.Simulator.Execute(
        new SimulatorRequest("operation-one", Revision.Create("credential", 1), Revision.Create("configuration", 1)),
        BootstrapScenarios.PossibleDispatch(),
        new SimulatorVirtualTime(new DateTimeOffset(2030, 1, 1, 0, 0, 0, TimeSpan.Zero)));
    Require(uncertain.State == ExternalEffectState.PossibleDispatch,
        "The embedded connector flattened post-send uncertainty.");
}

static void Require(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
}
