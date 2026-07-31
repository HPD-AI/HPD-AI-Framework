
namespace HPD.Base;

internal sealed class DefaultBaseStoreExecutionResolver(
    IRecordStoreRegistry registry) : IBaseStoreExecutionResolver
{
    public OperationResult<BaseResolvedMutationStore> Resolve(
        CollectionDefinition collection,
        BaseRecordMutationKind operation,
        OperationContext context)
    {
        ArgumentNullException.ThrowIfNull(collection);
        _ = context;

        var registration = !string.IsNullOrWhiteSpace(collection.Store?.StoreId)
            ? registry.GetRegistration(collection.Store.StoreId)
            : registry.GetRegistrationForCollection(collection.Id);
        if (registration is null)
        {
            return OperationResults.Unsupported<BaseResolvedMutationStore>(Error(
                "base.runtime.store.missing",
                "No record store is registered for this collection.",
                ErrorCategory.Unsupported));
        }

        if (registration.Store is not IRecordMutationStore mutationStore)
        {
            return OperationResults.Unsupported<BaseResolvedMutationStore>(Error(
                "base.runtime.store.operationUnsupported",
                "The registered store does not implement the mutation execution contract.",
                ErrorCategory.Unsupported));
        }

        var supported = operation switch
        {
            BaseRecordMutationKind.Create => mutationStore.Capabilities.Mutation.Create,
            BaseRecordMutationKind.Patch => mutationStore.Capabilities.Mutation.Patch,
            BaseRecordMutationKind.Replace => mutationStore.Capabilities.Mutation.Replace,
            BaseRecordMutationKind.Delete => mutationStore.Capabilities.Mutation.Delete,
            BaseRecordMutationKind.Upsert => mutationStore.Capabilities.Upsert?.Atomic == true,
            _ => false
        };
        if (!supported)
        {
            return OperationResults.Unsupported<BaseResolvedMutationStore>(Error(
                operation == BaseRecordMutationKind.Upsert
                    ? "base.runtime.upsert.unsupported"
                    : "base.runtime.store.operationUnsupported",
                "The registered store does not support the requested mutation.",
                ErrorCategory.Unsupported));
        }

        return OperationResults.Ok(new BaseResolvedMutationStore
        {
            Registration = registration,
            Store = mutationStore,
            AtomicStore = mutationStore as IAtomicRecordStore
        });
    }

    private static BaseError Error(string code, string message, ErrorCategory category) => new()
    {
        Code = code,
        Message = message,
        Category = category
    };
}
