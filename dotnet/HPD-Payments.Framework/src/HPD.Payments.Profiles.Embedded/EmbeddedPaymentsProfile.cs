using HPD.Base;
using HPD.Payments.Adapters.InMemory;
using HPD.Payments.Adapters.Sqlite;
using HPD.Payments.Connectors.Simulator.Core;
using HPD.Payments.Persistence.Ports;
using HPD.Payments.Primitives.Identity;
using HPD.Payments.Runtime.Base;

namespace HPD.Payments.Profiles.Embedded;

/// <summary>Identifies the explicitly selected HPD.Base provider bridge in an embedded profile.</summary>
public enum EmbeddedPersistenceProvider
{
    /// <summary>No provider was selected. A profile is never created in this state.</summary>
    Unspecified = 0,
    /// <summary>The accepted HPD.Base InMemory bridge.</summary>
    InMemory = 1,
    /// <summary>The accepted HPD.Base SQLite bridge.</summary>
    Sqlite = 2,
}

/// <summary>Exposes the closed, provider-neutral surface of an embedded Payments composition.</summary>
public interface IEmbeddedPaymentsProfile
{
    /// <summary>Gets the explicitly selected persistence provider.</summary>
    EmbeddedPersistenceProvider Provider { get; }
    /// <summary>Gets the shared supporting persistence ports.</summary>
    BaseSupportingPersistencePort Supporting { get; }
    /// <summary>Gets the deterministic connector simulator.</summary>
    SimulatorEngine Simulator { get; }
    /// <summary>Creates a typed owner port from an explicit source-generated fact codec.</summary>
    IOwnerPersistencePort<TFact> CreateOwnerPort<TFact>(PaymentsFactJsonCodec<TFact> codec) where TFact : notnull;
}

/// <summary>Creates closed embedded Payments compositions over accepted HPD.Base bridges.</summary>
public static class EmbeddedPaymentsProfile
{
    /// <summary>Creates an embedded composition over the accepted HPD.Base InMemory bridge.</summary>
    public static IEmbeddedPaymentsProfile InMemory(
        BaseSession session,
        Revision credentialRevision,
        Revision configurationRevision) =>
        new InMemoryProfile(new BaseInMemoryPaymentsPersistence(session), NewSimulator(credentialRevision, configurationRevision));

    /// <summary>Creates an embedded composition over the accepted HPD.Base SQLite bridge.</summary>
    public static IEmbeddedPaymentsProfile Sqlite(
        BaseSession session,
        Revision credentialRevision,
        Revision configurationRevision) =>
        new SqliteProfile(new BaseSqlitePaymentsPersistence(session), NewSimulator(credentialRevision, configurationRevision));

    private static SimulatorEngine NewSimulator(Revision credentialRevision, Revision configurationRevision) =>
        new(credentialRevision, configurationRevision);

    private sealed class InMemoryProfile(BaseInMemoryPaymentsPersistence persistence, SimulatorEngine simulator)
        : IEmbeddedPaymentsProfile
    {
        public EmbeddedPersistenceProvider Provider => EmbeddedPersistenceProvider.InMemory;
        public BaseSupportingPersistencePort Supporting => persistence.Supporting;
        public SimulatorEngine Simulator => simulator;
        public IOwnerPersistencePort<TFact> CreateOwnerPort<TFact>(PaymentsFactJsonCodec<TFact> codec) where TFact : notnull =>
            persistence.CreateOwnerPort(codec);
    }

    private sealed class SqliteProfile(BaseSqlitePaymentsPersistence persistence, SimulatorEngine simulator)
        : IEmbeddedPaymentsProfile
    {
        public EmbeddedPersistenceProvider Provider => EmbeddedPersistenceProvider.Sqlite;
        public BaseSupportingPersistencePort Supporting => persistence.Supporting;
        public SimulatorEngine Simulator => simulator;
        public IOwnerPersistencePort<TFact> CreateOwnerPort<TFact>(PaymentsFactJsonCodec<TFact> codec) where TFact : notnull =>
            persistence.CreateOwnerPort(codec);
    }
}
