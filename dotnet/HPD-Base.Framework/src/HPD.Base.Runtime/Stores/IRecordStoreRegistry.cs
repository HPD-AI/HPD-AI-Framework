using HPD.Base.Stores;

namespace HPD.Base.Runtime.Stores;

public interface IRecordStoreRegistry
{
    void Add(RecordStoreRegistration registration);
    IRecordStore? GetStore(string storeId);
    IRecordStore? GetStoreForCollection(string collectionId);
    RecordStoreRegistration[] GetRegistrations();
}
