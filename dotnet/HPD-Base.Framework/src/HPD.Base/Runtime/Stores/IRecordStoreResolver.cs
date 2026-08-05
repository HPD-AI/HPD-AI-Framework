
namespace HPD.Base;

/// <summary>Defines the irecord store resolver contract.</summary>
public interface IRecordStoreResolver
{
    /// <summary>Executes the resolve operation.</summary>
    OperationResult<IRecordStore> Resolve(
        CollectionDefinition collection,
        OperationContext operation);
}
