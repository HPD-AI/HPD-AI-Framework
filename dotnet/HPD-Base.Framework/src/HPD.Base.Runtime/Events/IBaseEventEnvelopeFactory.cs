using HPD.Base.Events;
using HPD.Base.Records;
using HPD.Base.Runtime;
using HPD.Base.Schema;

namespace HPD.Base.Runtime.Events;

public interface IBaseEventEnvelopeFactory
{
    BaseEventEnvelope CreateRecordMutationEvent(
        BaseOperationKind operation,
        OperationContext context,
        PrincipalContext principal,
        CollectionDefinition collection,
        RecordEnvelope? before,
        RecordEnvelope? after,
        string[]? changedFields);
}
