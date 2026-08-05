
namespace HPD.Base;

/// <summary>Defines the ibase query validator contract.</summary>
public interface IBaseQueryValidator
{
    /// <summary>Executes the validate async operation.</summary>
    ValueTask<OperationResult<ValidatedRecordQuery>> ValidateAsync(
        CollectionDefinition collection,
        RecordQuery query,
        QueryCapability capability,
        BaseQueryValidationUsage usage,
        OperationContext operation,
        CancellationToken cancellationToken = default);
}

/// <summary>Defines the base query validation usage contract.</summary>
public enum BaseQueryValidationUsage
{
    /// <summary>Identifies external query.</summary>
ExternalQuery,
    /// <summary>Identifies policy constraint.</summary>
PolicyConstraint,
    /// <summary>Identifies policy write check.</summary>
PolicyWriteCheck,
    /// <summary>Identifies include filter.</summary>
IncludeFilter,
    /// <summary>Identifies stream.</summary>
Stream
}

/// <summary>Represents a validated record query.</summary>
public sealed class ValidatedRecordQuery
{
    /// <summary>Initializes a new instance.</summary>
    public ValidatedRecordQuery(RecordQuery query)
    {
        Query = query ?? throw new ArgumentNullException(nameof(query));
    }

    /// <summary>Gets the query.</summary>
    public RecordQuery Query { get; }
}
