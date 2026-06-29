using HPD.Base.Results;

namespace HPD.Base.Abstractions.Tests.Results;

public sealed class OperationResultContractTests
{
    [Fact]
    public void OperationResultHasNoSerializableSucceededProperty()
    {
        Assert.Null(typeof(OperationResult).GetProperty("Succeeded"));
        Assert.Null(typeof(OperationResult<>).GetProperty("Succeeded"));
    }
}
