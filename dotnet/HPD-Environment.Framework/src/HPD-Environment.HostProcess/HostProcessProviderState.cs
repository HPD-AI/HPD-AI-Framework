namespace HPD.Environment.HostProcess;

using HPD.Environment.Contracts;
using HPD.Environment.Runtime;

/// <summary>
/// Owns provider-scoped identities for the lifetime of one Host Process provider.
/// The Environment runtime remains the owner of session ordering and cleanup.
/// </summary>
internal sealed class HostProcessProviderState
{
    public HostProcessProviderState()
        : this(new ProviderResourceLedger(HostProcessProviderDescriptor.ProviderId))
    {
    }

    internal HostProcessProviderState(ProviderResourceLedger ledger)
    {
        Ledger = ledger ?? throw new ArgumentNullException(nameof(ledger));
    }

    public ProviderResourceLedger Ledger { get; }
}
