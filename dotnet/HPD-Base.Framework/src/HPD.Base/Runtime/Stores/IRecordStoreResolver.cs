
namespace HPD.Base;

public interface IRecordStoreResolver
{
    OperationResult<IRecordStore> Resolve(
        CollectionDefinition collection,
        OperationContext operation);
}
