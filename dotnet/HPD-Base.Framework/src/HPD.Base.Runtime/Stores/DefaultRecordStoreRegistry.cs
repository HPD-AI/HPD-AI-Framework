using HPD.Base.Stores;

namespace HPD.Base.Runtime.Stores;

internal sealed class DefaultRecordStoreRegistry : IRecordStoreRegistry
{
    private readonly object _gate = new();
    private readonly List<RecordStoreRegistration> _registrations = [];

    public void Add(RecordStoreRegistration registration)
    {
        ArgumentNullException.ThrowIfNull(registration);
        ArgumentException.ThrowIfNullOrWhiteSpace(registration.StoreId);
        ArgumentNullException.ThrowIfNull(registration.Store);

        lock (_gate)
        {
            _registrations.Add(registration);
        }
    }

    public IRecordStore? GetStore(string storeId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(storeId);
        lock (_gate)
        {
            return _registrations.LastOrDefault(registration =>
                string.Equals(registration.StoreId, storeId, StringComparison.Ordinal))?.Store;
        }
    }

    public IRecordStore? GetStoreForCollection(string collectionId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(collectionId);
        lock (_gate)
        {
            return _registrations.LastOrDefault(registration =>
                registration.CollectionIds?.Contains(collectionId, StringComparer.Ordinal) == true)?.Store
                ?? _registrations.LastOrDefault(registration => registration.CollectionIds is null or { Length: 0 })?.Store;
        }
    }

    public RecordStoreRegistration[] GetRegistrations()
    {
        lock (_gate)
        {
            return _registrations.ToArray();
        }
    }
}
