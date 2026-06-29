using HPD.Base.Query;
using HPD.Base.Results;
using HPD.Base.Schema;

namespace HPD.Base.Runtime.Query;

public interface IBaseQueryValidator
{
    ValueTask<OperationResult<ValidatedRecordQuery>> ValidateAsync(
        CollectionDefinition collection,
        RecordQuery query,
        QueryCapability capability,
        BaseQueryValidationUsage usage,
        OperationContext operation,
        CancellationToken cancellationToken = default);
}

public enum BaseQueryValidationUsage
{
    ExternalQuery,
    PolicyConstraint,
    PolicyWriteCheck,
    IncludeFilter,
    Stream
}

public sealed class ValidatedRecordQuery
{
    public ValidatedRecordQuery(RecordQuery query)
    {
        Query = query ?? throw new ArgumentNullException(nameof(query));
    }

    public RecordQuery Query { get; }
}
