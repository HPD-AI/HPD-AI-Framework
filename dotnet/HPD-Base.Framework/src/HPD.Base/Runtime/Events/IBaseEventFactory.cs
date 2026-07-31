
namespace HPD.Base;

/// <summary>
/// Creates BASE domain events for runtime operations.
/// </summary>
public interface IBaseEventFactory
{
    /// <summary>Creates an event for a committed record mutation.</summary>
    BaseRecordMutationEvent CreateRecordMutationEvent(
        BaseOperationKind operation,
        OperationContext context,
        PrincipalContext principal,
        CollectionDefinition collection,
        RecordEnvelope? before,
        RecordEnvelope? after,
        string[]? changedFields,
        string? committedEventId);
}
