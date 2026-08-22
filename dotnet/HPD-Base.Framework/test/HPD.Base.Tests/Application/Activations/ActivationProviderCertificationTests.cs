using System.Collections.Immutable;

namespace HPD.Base.Tests;

public sealed class ActivationProviderCertificationTests
{
    [Fact]
    public void ReceiptBindsCapabilityAndNativeDependencies()
    {
        BaseActivationProviderCapability capability = BaseActivationCapabilityContract.BuiltIn("tests.activation.capability.v1");
        BaseActivationProviderDescriptor descriptor = BaseActivationCertificationReceiptContract.BuiltIn(
            "tests.activation", "1", capability, "native-a", "native-b");

        Assert.True(BaseActivationCertificationReceiptContract.Validate(descriptor));
        Assert.False(BaseActivationCertificationReceiptContract.Validate(descriptor with
        {
            Capability = capability with { MaximumDueCandidates = capability.MaximumDueCandidates - 1 },
        }));
        Assert.False(BaseActivationCertificationReceiptContract.Validate(descriptor with
        {
            NativeDependencyReceipts = ImmutableArray.Create("native-a", "native-c"),
        }));
        Assert.False(BaseActivationCertificationReceiptContract.Validate(descriptor with
        {
            CertificationReportChecksum = ImmutableArray.Create(new byte[32]),
        }));
        Assert.False(BaseActivationCertificationReceiptContract.Validate(descriptor with
        {
            CertificationReceipt = ImmutableArray.Create(new byte[32]),
        }));
    }
}
