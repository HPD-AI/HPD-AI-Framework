using HPD.Base;
using HPD.Payments.Persistence.Ports;
using HPD.Payments.Runtime.Base;

namespace HPD.Payments.Adapters.InMemory;

/// <summary>Composes Payments persistence ports over a principal-bound HPD.Base InMemory session.</summary>
/// <remarks>The adapter owns no records, locks, transactions, snapshots, generations, or recovery machinery.</remarks>
public sealed class BaseInMemoryPaymentsPersistence
{
    private readonly BaseSession _session;

    /// <summary>Creates the permanent provider-neutral translation boundary over the installed Base InMemory graph.</summary>
    public BaseInMemoryPaymentsPersistence(BaseSession session)
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
