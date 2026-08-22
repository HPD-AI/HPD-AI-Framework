
namespace HPD.Base;

internal sealed class DefaultRecordStoreRegistry :
    IRecordStoreRegistry,
    IRecordStoreRegistrationEditor
{
    private readonly object _gate = new();
    private readonly List<RecordStoreRegistration> _registrations = [];

    /// <summary>Executes the add operation.</summary>
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

    /// <summary>Executes the get store operation.</summary>
    public IRecordStore? GetStore(string storeId)
        => GetRegistration(storeId)?.Store;

    /// <summary>Executes the get registration operation.</summary>
    public RecordStoreRegistration? GetRegistration(string storeId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(storeId);
        lock (_gate)
        {
            return _registrations.LastOrDefault(registration =>
                string.Equals(registration.StoreId, storeId, StringComparison.Ordinal));
        }
    }

    /// <summary>Executes the get store for collection operation.</summary>
    public IRecordStore? GetStoreForCollection(string collectionId)
        => GetRegistrationForCollection(collectionId)?.Store;

    /// <summary>Executes the get registration for collection operation.</summary>
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

    /// <summary>Executes the get registrations operation.</summary>
    public RecordStoreRegistration[] GetRegistrations()
    {
        lock (_gate)
        {
            return _registrations.ToArray();
        }
    }

    /// <inheritdoc />
    public void Replace(
        RecordStoreRegistration expected,
        RecordStoreRegistration replacement)
    {
        ArgumentNullException.ThrowIfNull(expected);
        ArgumentNullException.ThrowIfNull(replacement);
        if (!string.Equals(expected.StoreId, replacement.StoreId, StringComparison.Ordinal))
            throw new ArgumentException(
                "A replacement registration must retain the same store identifier.",
                nameof(replacement));

        lock (_gate)
        {
            int index = _registrations.FindLastIndex(registration =>
                ReferenceEquals(registration, expected));
            if (index < 0)
                throw new InvalidOperationException(
                    "The record-store registration changed before it could be decorated.");
            _registrations[index] = replacement;
        }
    }
}
