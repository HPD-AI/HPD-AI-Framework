
namespace HPD.Base;

internal static class InMemoryValidation
{
    /// <summary>Executes the validate collection ID operation.</summary>
    public static OperationResult<T>? ValidateCollectionId<T>(string? collectionId)
    {
        if (IsValidIdText(collectionId))
        {
            return null;
        }

        return OperationResults.ValidationFailed<T>(new BaseError
        {
            Code = InMemoryErrorCodes.InvalidCollectionId,
            Message = "Collection id must be non-empty and contain no control characters.",
            Category = ErrorCategory.Validation,
            Target = "collection.id"
        });
    }

    /// <summary>Executes the validate record ID operation.</summary>
    public static OperationResult<T>? ValidateRecordId<T>(string? recordId)
    {
        if (IsValidIdText(recordId))
        {
            return null;
        }

        return OperationResults.ValidationFailed<T>(new BaseError
        {
            Code = InMemoryErrorCodes.InvalidRecordId,
            Message = "Record id must be non-empty and contain no control characters.",
            Category = ErrorCategory.Validation,
            Target = "id"
        });
    }

    /// <summary>Executes the validate field name operation.</summary>
    public static OperationResult<T>? ValidateFieldName<T>(string? fieldName)
    {
        if (IsValidFieldName(fieldName))
        {
            return null;
        }

        return OperationResults.ValidationFailed<T>(new BaseError
        {
            Code = InMemoryErrorCodes.InvalidField,
            Message = "Field names must be non-empty and contain no control characters.",
            Category = ErrorCategory.Validation,
            Target = fieldName
        });
    }

    /// <summary>Executes the is valid ID text operation.</summary>
    public static bool IsValidIdText(string? value) =>
        !string.IsNullOrWhiteSpace(value) && !value.Any(char.IsControl);

    private static bool IsValidFieldName(string? value) =>
        !string.IsNullOrEmpty(value) && !value.Any(char.IsControl);
}
