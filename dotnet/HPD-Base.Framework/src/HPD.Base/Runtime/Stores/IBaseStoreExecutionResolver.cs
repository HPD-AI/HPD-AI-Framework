
namespace HPD.Base;

internal interface IBaseStoreExecutionResolver
{
    /// <summary>Executes the resolve operation.</summary>
    OperationResult<BaseResolvedMutationStore> Resolve(
        CollectionDefinition collection,
        BaseRecordMutationKind operation,
        OperationContext context);
}

internal sealed record BaseResolvedMutationStore
{
    /// <summary>Gets or sets the registration.</summary>
    public required RecordStoreRegistration Registration { get; init; }
    /// <summary>Gets or sets the store.</summary>
    public required IRecordMutationStore Store { get; init; }
    /// <summary>Gets or sets the atomic store.</summary>
    public IAtomicRecordStore? AtomicStore { get; init; }
}
