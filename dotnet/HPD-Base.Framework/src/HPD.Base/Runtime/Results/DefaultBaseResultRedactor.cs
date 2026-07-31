using Microsoft.Extensions.Options;

namespace HPD.Base;

internal sealed class DefaultBaseResultRedactor : IBaseResultRedactor
{
    private readonly HPDBaseRuntimeRedactionOptions _options;

    public DefaultBaseResultRedactor(IOptions<HPDBaseRuntimeOptions> options)
    {
        _options = options.Value.Redaction;
    }

    public OperationResult<T> Redact<T>(OperationResult<T> result, VisibilityLevel view)
    {
        return view == VisibilityLevel.Public && _options.RedactPublicErrors
            ? result with { Error = RedactError(result.Error), Diagnostics = null }
            : result;
    }

    public OperationResult Redact(OperationResult result, VisibilityLevel view)
    {
        return view == VisibilityLevel.Public && _options.RedactPublicErrors
            ? result with { Error = RedactError(result.Error), Diagnostics = null }
            : result;
    }

    private static BaseError? RedactError(BaseError? error)
    {
        if (error is null)
        {
            return null;
        }

        return error with
        {
            Detail = null,
            Hint = null,
            TraceId = null,
            Store = error.Store is null
                ? null
                : error.Store with
                {
                    NativeCode = null,
                    NativeSubcode = null,
                    NativeCategory = null,
                    NativeMessage = null
                }
        };
    }
}
