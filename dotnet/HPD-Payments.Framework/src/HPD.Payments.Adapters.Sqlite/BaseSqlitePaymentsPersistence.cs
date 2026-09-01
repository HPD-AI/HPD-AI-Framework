using HPD.Base;
using HPD.Payments.Persistence.Ports;
using HPD.Payments.Runtime.Base;

namespace HPD.Payments.Adapters.Sqlite;

/// <summary>Composes Payments persistence ports over a principal-bound HPD.Base SQLite session.</summary>
/// <remarks>HPD.Base owns the provider, schema, connections, transactions, receipts, recovery, and lifecycle.</remarks>
public sealed class BaseSqlitePaymentsPersistence
{
    private readonly BaseSession _session;

    /// <summary>Creates the provider-neutral Payments translation boundary over an installed Base SQLite graph.</summary>
    public BaseSqlitePaymentsPersistence(BaseSession session)
    {
        _session = session ?? throw new ArgumentNullException(nameof(session));
        Supporting = new BaseSupportingPersistencePort(session);
    }

    /// <summary>Gets the shared supporting relation, continuation, and custody ports.</summary>
    public BaseSupportingPersistencePort Supporting { get; }

    /// <summary>Creates one closed typed owner port using an explicit source-generated fact codec.</summary>
    public IOwnerPersistencePort<TFact> CreateOwnerPort<TFact>(PaymentsFactJsonCodec<TFact> codec) where TFact : notnull =>
        new BaseOwnerPersistencePort<TFact>(_session, codec ?? throw new ArgumentNullException(nameof(codec)));
}
