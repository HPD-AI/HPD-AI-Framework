using HPD.Base.Records;
using HPD.Base.Results;
using HPD.Base.Runtime.DependencyInjection;
using HPD.Base.Runtime.Results;
using Microsoft.Extensions.DependencyInjection;

namespace HPD.Base.Runtime.Tests.Results;

public sealed class ResultNormalizerTests
{
    [Fact]
    public void FailedStoreResultWithoutErrorGetsSafeError()
    {
        using var provider = Provider();

        var result = provider.GetRequiredService<IBaseResultNormalizer>().NormalizeStoreResult(
            new OperationResult<RecordEnvelope> { Status = OperationStatus.NotFound },
            RuntimeTestData.Operation(BaseOperationKind.Get));

        Assert.Equal(OperationStatus.NotFound, result.Status);
        Assert.NotNull(result.Error);
        Assert.Equal("base.runtime.store.notFound", result.Error.Code);
        Assert.Equal(ErrorCategory.NotFound, result.Error.Category);
    }

    [Fact]
    public void SuccessWithoutValueBecomesStoreError()
    {
        using var provider = Provider();

        var result = provider.GetRequiredService<IBaseResultNormalizer>().NormalizeStoreResult(
            new OperationResult<RecordEnvelope> { Status = OperationStatus.Ok },
            RuntimeTestData.Operation(BaseOperationKind.Get));

        Assert.Equal(OperationStatus.StoreError, result.Status);
        Assert.NotNull(result.Error);
        Assert.Equal("base.runtime.store.nullSuccessValue", result.Error.Code);
    }

    [Fact]
    public void FailedStoreResultClearsLeakedValue()
    {
        using var provider = Provider();
        var leaked = new RecordEnvelope
        {
            CollectionId = "items",
            Id = new RecordId("rec_1"),
            Payload = new RecordPayload { Kind = RecordPayloadKind.FieldMap, Fields = [] },
            Metadata = new RecordMetadata()
        };

        var result = provider.GetRequiredService<IBaseResultNormalizer>().NormalizeStoreResult(
            new OperationResult<RecordEnvelope>
            {
                Status = OperationStatus.StoreError,
                Value = leaked,
                Error = new BaseError { Code = "store", Message = "Store.", Category = ErrorCategory.Store }
            },
            RuntimeTestData.Operation(BaseOperationKind.Get));

        Assert.Null(result.Value);
    }

    private static ServiceProvider Provider()
    {
        var services = new ServiceCollection();
        services.AddHPDBaseRuntime();
        return services.BuildServiceProvider();
    }
}
