
namespace HPD.Base;

internal interface IBaseStoreExecutionResolver
{
    OperationResult<BaseResolvedMutationStore> Resolve(
        CollectionDefinition collection,
        BaseRecordMutationKind operation,
        OperationContext context);
}

internal sealed record BaseResolvedMutationStore
{
    public required RecordStoreRegistration Registration { get; init; }
    public required IRecordMutationStore Store { get; init; }
    public IAtomicRecordStore? AtomicStore { get; init; }
}
