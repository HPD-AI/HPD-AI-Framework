using Microsoft.Extensions.Options;

namespace HPD.Base;

internal sealed class DefaultFileObjectKeyValidator : IFileObjectKeyValidator
{
    private readonly HPDBaseFilesOptions _options;

    /// <summary>Initializes a new instance.</summary>
    public DefaultFileObjectKeyValidator(IOptions<HPDBaseFilesOptions> options)
    {
        _options = options.Value;
    }

    /// <summary>Executes the normalize operation.</summary>
    public OperationResult<FileObjectKey> Normalize(string? key)
    {
        if (string.IsNullOrWhiteSpace(key))
            return Invalid("empty", "Object key is required.");

        if (key.IndexOf('\0') >= 0 || key.Any(char.IsControl))
            return Invalid("control", "Object key cannot contain control characters.");

        if (key.Contains('\\', StringComparison.Ordinal))
            return Invalid("backslash", "Object key cannot contain backslashes.");

        if (key.StartsWith("/", StringComparison.Ordinal) || key.StartsWith("~/", StringComparison.Ordinal))
            return Invalid("absolute", "Object key cannot be absolute.");

        if (key.Length >= 2 && char.IsLetter(key[0]) && key[1] == ':')
            return Invalid("driveRoot", "Object key cannot contain drive roots.");

        if (Uri.UnescapeDataString(key).Contains("..", StringComparison.Ordinal))
            return Invalid("traversal", "Object key cannot contain path traversal segments.");

        if (key.Contains("//", StringComparison.Ordinal))
            return Invalid("separator", "Object key cannot contain repeated separators.");

        var normalized = string.Join('/', key.Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
        if (normalized.Length == 0)
            return Invalid("empty", "Object key is required.");

        if (normalized.Length > _options.MaxKeyLength)
            return Invalid("length", "Object key is too long.");

        var segments = normalized.Split('/');
        if (segments.Length > _options.MaxKeySegments)
            return Invalid("segments", "Object key has too many path segments.");

        if (segments.Any(static segment => segment is "." or ".." || segment.Length == 0))
            return Invalid("segment", "Object key contains an unsafe segment.");

        return OperationResults.Ok(new FileObjectKey(normalized));
    }

    private static OperationResult<FileObjectKey> Invalid(string reason, string message) =>
        OperationResults.ValidationFailed<FileObjectKey>(new BaseError
        {
            Code = FileDiagnosticIds.InvalidKey,
            Message = message,
            Target = "key",
            Category = ErrorCategory.Validation,
            Validation =
            [
                new ValidationIssue
                {
                    Path = "key",
                    Code = reason,
                    Message = message
                }
            ]
        });
}
