using HPD.Base;

namespace HPD.Base.Sqlite;

internal static class SqliteValidation
{
    /// <summary>Executes the validate collection ID operation.</summary>
    public static OperationResult<T>? ValidateCollectionId<T>(string? collectionId) =>
        IsValidIdText(collectionId) ? null : Validation<T>(SqliteErrorCodes.InvalidCollectionId, "Collection id must be non-empty and contain no control characters.", "collection.id");

    /// <summary>Executes the validate record ID operation.</summary>
    public static OperationResult<T>? ValidateRecordId<T>(string? recordId) =>
        IsValidIdText(recordId) ? null : Validation<T>(SqliteErrorCodes.InvalidRecordId, "Record id must be non-empty and contain no control characters.", "id");

    /// <summary>Executes the validate field name operation.</summary>
    public static OperationResult<T>? ValidateFieldName<T>(string? fieldName) =>
        IsValidFieldName(fieldName) ? null : Validation<T>(SqliteErrorCodes.InvalidField, "Field names must be non-empty, top-level names and contain no control characters.", fieldName);

    /// <summary>Executes the is valid ID text operation.</summary>
    public static bool IsValidIdText(string? value) => !string.IsNullOrWhiteSpace(value) && !value.Any(char.IsControl);

    /// <summary>Executes the is valid schema prefix operation.</summary>
    public static bool IsValidSchemaPrefix(string? value) =>
        !string.IsNullOrWhiteSpace(value) && value.All(ch => char.IsAsciiLetterOrDigit(ch) || ch == '_');

    private static bool IsValidFieldName(string? value) =>
        !string.IsNullOrEmpty(value) && !value.Contains('.') && !value.Any(char.IsControl);

    private static OperationResult<T> Validation<T>(string code, string message, string? target = null) =>
        OperationResults.ValidationFailed<T>(new BaseError { Code = code, Message = message, Category = ErrorCategory.Validation, Target = target });
}
