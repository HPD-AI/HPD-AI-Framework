using HPD.Base.Results;
using HPD.Base.Schema;
using HPD.Base.Stores;
using HPD.Base.Runtime;

namespace HPD.Base.Runtime.Stores;

public interface IRecordStoreResolver
{
    OperationResult<IRecordStore> Resolve(
        CollectionDefinition collection,
        OperationContext operation);
}
