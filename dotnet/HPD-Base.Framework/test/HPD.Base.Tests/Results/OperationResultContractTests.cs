using HPD.Base;

namespace HPD.Base.Tests.Abstractions.Results;

public sealed class OperationResultContractTests
{
    [Fact]
    public void OperationResultHasNoSerializableSucceededProperty()
    {
        Assert.Null(typeof(OperationResult).GetProperty("Succeeded"));
        Assert.Null(typeof(OperationResult<>).GetProperty("Succeeded"));
    }
}
