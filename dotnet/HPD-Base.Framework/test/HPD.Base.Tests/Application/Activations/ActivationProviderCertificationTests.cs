using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text;
using HPD.Base.Testing;

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

    [Fact]
    public async Task CompleteMandatoryMatrixIssuesBoundReceipt()
    {
        await using var fixture = new Fixture();
        BaseActivationCertificationReport report = await BaseActivationProviderCertification.RunAsync(
            fixture, TimeSpan.FromSeconds(5));

        Assert.True(report.Passed);
        Assert.Equal(BaseActivationProviderCertification.MandatoryCases, report.Cases.Select(static item => item.Id));
        Assert.Equal(32, report.ReportChecksum.Length);
        Assert.Equal(32, report.CertificationReceipt.Length);
        Assert.True(BaseActivationCertificationReceiptContract.Validate(fixture.Descriptor with
        {
            CertificationReportChecksum = report.ReportChecksum,
            CertificationReceipt = report.CertificationReceipt,
        }));
    }

    [Fact]
    public async Task FailedCaseCannotIssueReceipt()
    {
        await using var fixture = new Fixture(failOrdinal: 7);
        BaseActivationCertificationReport report = await BaseActivationProviderCertification.RunAsync(
            fixture, TimeSpan.FromSeconds(5));

        Assert.False(report.Passed);
        Assert.Empty(report.CertificationReceipt);
        Assert.Equal("base.activation.certification.failed", report.Cases[7].ErrorCode);
    }

    [Fact]
    public async Task SubstitutedCaseIdentityFailsClosed()
    {
        await using var fixture = new Fixture(substituteOrdinal: 2);
        InvalidOperationException error = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await BaseActivationProviderCertification.RunAsync(fixture, TimeSpan.FromSeconds(5)));
        Assert.Equal("base.activation.providerContractInvalid", error.Message);
    }

    private sealed class Fixture(int? failOrdinal = null, int? substituteOrdinal = null) : IBaseActivationCertificationFixture
    {
        public BaseActivationProviderDescriptor Descriptor { get; } = BaseActivationCertificationReceiptContract.BuiltIn(
            "tests.activation.matrix", "1", BaseActivationCapabilityContract.BuiltIn("tests.activation.matrix.capability.v1"));

        public ValueTask<BaseActivationCertificationCaseResult> ExecuteAsync(
            BaseActivationCertificationCaseRequest request, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            bool passed = request.Ordinal != failOrdinal;
            string id = request.Ordinal == substituteOrdinal ? "substituted" : request.Id;
            return ValueTask.FromResult(new BaseActivationCertificationCaseResult
            {
                Id = id, Passed = passed,
                Status = passed ? OperationStatus.Ok : OperationStatus.StoreError,
                ErrorCode = passed ? null : "base.activation.certification.failed",
                EvidenceChecksum = SHA256.HashData(Encoding.UTF8.GetBytes($"{request.Ordinal}:{request.Id}:{passed}"))
                    .ToImmutableArray(),
            });
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
