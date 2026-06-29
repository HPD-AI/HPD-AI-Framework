using HPD.Base.Results;

namespace HPD.Base.Abstractions.Tests.Stores.Conformance;

public static class RecordStoreConformanceAssertions
{
    public static void AssertFailedResultHasError<T>(OperationResult<T> result)
    {
        if (result.Status is OperationStatus.Ok or OperationStatus.Created or OperationStatus.Updated or OperationStatus.Deleted or OperationStatus.NoContent)
        {
            return;
        }

        Assert.NotNull(result.Error);
    }
}
