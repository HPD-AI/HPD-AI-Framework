
namespace HPD.Base;

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
        => GetRegistration(storeId)?.Store;

    public RecordStoreRegistration? GetRegistration(string storeId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(storeId);
        lock (_gate)
        {
            return _registrations.LastOrDefault(registration =>
                string.Equals(registration.StoreId, storeId, StringComparison.Ordinal));
        }
    }

    public IRecordStore? GetStoreForCollection(string collectionId)
        => GetRegistrationForCollection(collectionId)?.Store;

    public RecordStoreRegistration? GetRegistrationForCollection(string collectionId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(collectionId);
        lock (_gate)
        {
            return _registrations.LastOrDefault(registration =>
                registration.CollectionIds?.Contains(collectionId, StringComparer.Ordinal) == true)
                ?? _registrations.LastOrDefault(registration => registration.CollectionIds is null or { Length: 0 });
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
