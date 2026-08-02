
namespace HPD.Base;

internal sealed class DefaultRecordStoreResolver : IRecordStoreResolver
{
    private readonly IRecordStoreRegistry _registry;

    /// <summary>Initializes a new instance.</summary>
    public DefaultRecordStoreResolver(IRecordStoreRegistry registry)
    {
        _registry = registry;
    }

    /// <summary>Executes the resolve operation.</summary>
    public OperationResult<IRecordStore> Resolve(
        CollectionDefinition collection,
        OperationContext operation)
    {
        ArgumentNullException.ThrowIfNull(collection);
        _ = operation;

        var store = !string.IsNullOrWhiteSpace(collection.Store?.StoreId)
            ? _registry.GetStore(collection.Store.StoreId)
            : _registry.GetStoreForCollection(collection.Id);

        return store is null
            ? OperationResults.Unsupported<IRecordStore>(new BaseError
            {
                Code = "base.runtime.store.missing",
                Message = "No record store is registered for this collection.",
                Category = ErrorCategory.Unsupported,
                Target = collection.Id
            })
            : OperationResults.Ok(store);
    }
}
