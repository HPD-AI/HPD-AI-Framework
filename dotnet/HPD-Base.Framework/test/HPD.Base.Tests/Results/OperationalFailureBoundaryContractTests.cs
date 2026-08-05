using HPD.Base;

namespace HPD.Base.Tests.Abstractions.Results;

public sealed class OperationalFailureBoundaryContractTests
{
    [Fact]
    public void ExpectedOperationalFailuresHaveResultStatuses()
    {
        OperationStatus[] expectedFailureStatuses =
        [
            OperationStatus.NotFound,
            OperationStatus.Conflict,
            OperationStatus.ValidationFailed,
            OperationStatus.PolicyDenied,
            OperationStatus.Unauthorized,
            OperationStatus.Unsupported,
            OperationStatus.CapabilityUnavailable,
            OperationStatus.StoreError
        ];

        Assert.All(expectedFailureStatuses, status => Assert.True(Enum.IsDefined(status)));
    }
}
