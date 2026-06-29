using HPD.Base.Results;

namespace HPD.Base.Abstractions.Tests.Results;

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
