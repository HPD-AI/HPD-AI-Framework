
namespace HPD.Base;

/// <summary>Registers exact record-store instances and resolves their owning registrations.</summary>
public interface IRecordStoreRegistry
{
    /// <summary>Adds one store registration.</summary>
    /// <param name="registration">The exact registration and store instance to add.</param>
    void Add(RecordStoreRegistration registration);

    /// <summary>Gets the last store registered with the supplied identifier.</summary>
    /// <param name="storeId">The store identifier.</param>
    /// <returns>The exact store instance, or <see langword="null"/> when absent.</returns>
    IRecordStore? GetStore(string storeId);

    /// <summary>Gets the store that owns a collection.</summary>
    /// <param name="collectionId">The collection identifier.</param>
    /// <returns>The exact store instance, or <see langword="null"/> when absent.</returns>
    IRecordStore? GetStoreForCollection(string collectionId);

    /// <summary>Gets the last registration with the supplied store identifier.</summary>
    /// <param name="storeId">The store identifier.</param>
    /// <returns>The exact registration object, or <see langword="null"/> when absent.</returns>
    RecordStoreRegistration? GetRegistration(string storeId);

    /// <summary>Gets the exact registration that owns a collection.</summary>
    /// <param name="collectionId">The collection identifier.</param>
    /// <returns>The exact registration object, or <see langword="null"/> when absent.</returns>
    RecordStoreRegistration? GetRegistrationForCollection(string collectionId);

    /// <summary>Gets a snapshot of all registrations in registration order.</summary>
    /// <returns>The registered store entries.</returns>
    RecordStoreRegistration[] GetRegistrations();
}
