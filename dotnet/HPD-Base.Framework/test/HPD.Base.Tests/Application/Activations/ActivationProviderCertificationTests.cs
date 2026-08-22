using System.Collections.Immutable;
using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using HPD.Base.Testing;

namespace HPD.Base.Tests;

public sealed class ActivationProviderCertificationTests
{
    [Fact]
    public async Task InMemoryRejectsExpiredObservationToken()
    {
        var store = new InMemoryRecordStore();
        byte[] token = new byte[48];
        BinaryPrimitives.WriteInt64BigEndian(token, 0);
        BinaryPrimitives.WriteInt64BigEndian(token.AsSpan(8), 0);

        BaseDueWaitResult result = await store.WaitForDueChangeAsync(
            new BaseDueObservationToken { Value = token.ToImmutableArray() }, DateTimeOffset.UtcNow.AddSeconds(1));

        Assert.Equal(BaseDueWaitOutcome.TokenInvalid, result.Outcome);
    }

    [Fact]
    public void CapabilityRejectsEveryPreviouslyUncheckedBoundary()
    {
        BaseActivationProviderCapability value = BaseActivationCapabilityContract.BuiltIn("tests.activation.capability.boundaries.v1");
        BaseActivationProviderCapability[] invalid =
        [
            value with { MaximumPendingRows = 0 },
            value with { MaximumClaimedRows = 0 },
            value with { MaximumTerminalRows = 0 },
            value with { MaximumTimeZoneBytes = 0 },
            value with { MaximumReadIntervals = 0 },
            value with { MaximumIndexOperations = 0 },
            value with { MaximumPriorityAgingBoost = 0 },
            value with { PriorityAgingInterval = TimeSpan.Zero },
            value with { ObservationTokenLifetime = TimeSpan.Zero },
            value with { ObservationWaitDeadline = TimeSpan.Zero },
            value with { RenewalDeadline = TimeSpan.Zero },
            value with { CommitObservationDeadline = TimeSpan.Zero },
            value with { ReceiptResolutionDeadline = TimeSpan.Zero },
            value with { MaintenanceDeadline = TimeSpan.Zero },
            value with { BackupModes = default },
            value with { RestoreModes = default },
        ];

        Assert.True(BaseActivationCapabilityContract.IsValid(value));
        Assert.All(invalid, static candidate => Assert.False(BaseActivationCapabilityContract.IsValid(candidate)));
    }

    [Fact]
    public void ReceiptBindsCapabilityAndNativeDependencies()
    {
        BaseActivationProviderCapability capability = BaseActivationCapabilityContract.BuiltIn("tests.activation.capability.v1");
        BaseActivationProviderDescriptor descriptor = BaseActivationCertificationReceiptContract.FromSuccessfulReport(
            "tests.activation", "1", capability, ImmutableArray.Create(new byte[32]), "native-a", "native-b");

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
            CertificationReportChecksum = ImmutableArray.Create(Enumerable.Repeat((byte)0xff, 32).ToArray()),
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
    public async Task InMemoryDescriptorBindsExecutedProviderMatrix()
    {
        await using var fixture = new BaseInMemoryActivationCertificationFixture();
        BaseActivationCertificationReport report = await BaseActivationProviderCertification.RunAsync(
            fixture, TimeSpan.FromSeconds(5));

        Assert.True(report.Passed);
        string expectedReport = Convert.ToHexStringLower(fixture.Descriptor.CertificationReportChecksum.AsSpan());
        string actualReport = Convert.ToHexStringLower(report.ReportChecksum.AsSpan());
        Assert.True(expectedReport == actualReport, $"expected={expectedReport}; actual={actualReport}");
        Assert.Equal(Convert.ToHexStringLower(fixture.Descriptor.CertificationReceipt.AsSpan()),
            Convert.ToHexStringLower(report.CertificationReceipt.AsSpan()));
    }

    [Fact]
    public async Task FixturePreparationFailureCannotIssueReceipt()
    {
        await using var fixture = new Fixture(failOrdinal: 7);
        await Assert.ThrowsAsync<InvalidOperationException>(async () => await BaseActivationProviderCertification.RunAsync(
            fixture, TimeSpan.FromSeconds(5)));
    }

    [Fact]
    public void FixtureCannotSelfAttestCaseResults()
    {
        Assert.DoesNotContain(typeof(IBaseActivationCertificationFixture).GetMethods(),
            static method => method.ReturnType == typeof(ValueTask<BaseActivationCertificationCaseResult>));
    }

    private sealed class Fixture(int? failOrdinal = null) : IBaseActivationCertificationFixture
    {
        private readonly InMemoryRecordStore _store = new();
        public BaseActivationProviderDescriptor Descriptor { get; } = BaseActivationCertificationReceiptContract.FromSuccessfulReport(
            "tests.activation.matrix", "1", BaseActivationCapabilityContract.BuiltIn("tests.activation.matrix.capability.v1"),
            ImmutableArray.CreateRange(Convert.FromHexString("b878fb9ed43e42e5eb7528672c447d218b72f837cc404af4e2430317fd58216b")));

        public IBaseActivationProvider Provider => _store;

        public ValueTask PrepareAsync(
            BaseActivationCertificationCaseRequest request, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (request.Ordinal == failOrdinal) throw new InvalidOperationException("fixture preparation failed");
            return ValueTask.CompletedTask;
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
