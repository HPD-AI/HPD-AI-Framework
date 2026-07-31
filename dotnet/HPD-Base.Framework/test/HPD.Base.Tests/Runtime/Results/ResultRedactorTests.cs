using HPD.Base;
using Microsoft.Extensions.DependencyInjection;

namespace HPD.Base.Tests.Results;

public sealed class ResultRedactorTests
{
    [Fact]
    public void PublicResultRedactionRemovesUnsafeErrorDetails()
    {
        var services = new ServiceCollection();
        services.AddHPDBaseRuntime();
        using var provider = services.BuildServiceProvider();
        var redactor = provider.GetRequiredService<IBaseResultRedactor>();

        var result = new OperationResult<string>
        {
            Status = OperationStatus.StoreError,
            Error = new BaseError
            {
                Code = "store",
                Message = "Store failed.",
                Detail = "connection string detail",
                Hint = "restart db01",
                TraceId = "trace",
                Category = ErrorCategory.Store,
                Store = new StoreErrorInfo
                {
                    StoreId = "primary",
                    NativeCode = "57P01",
                    NativeSubcode = "57P01",
                    NativeCategory = "admin_shutdown",
                    NativeMessage = "terminating connection due to administrator command"
                }
            },
            Diagnostics = new OperationDiagnostics { TraceId = "trace" }
        };

        var redacted = redactor.Redact(result, VisibilityLevel.Public);

        Assert.Null(redacted.Error!.Detail);
        Assert.Null(redacted.Error.Hint);
        Assert.Null(redacted.Error.TraceId);
        Assert.Null(redacted.Error.Store!.NativeCode);
        Assert.Null(redacted.Error.Store.NativeSubcode);
        Assert.Null(redacted.Error.Store.NativeMessage);
        Assert.Null(redacted.Diagnostics);
    }
}
