
namespace HPD.Base;

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
