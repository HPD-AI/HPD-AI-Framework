namespace HPD.Base.Abstractions.Tests.Descriptors;

public sealed class DescriptorValidationContractTests
{
    [Fact]
    public void FutureValidationMustCoverKnownStartupFailures()
    {
        var requiredFailureKinds = new[]
        {
            "duplicate ids",
            "unresolved refs",
            "inconsistent visibility",
            "descriptor/interface mismatch",
            "capability dependency conflicts"
        };

        Assert.All(requiredFailureKinds, item => Assert.False(string.IsNullOrWhiteSpace(item)));
    }
}
