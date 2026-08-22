using HPD.Base.Testing;
using Microsoft.Extensions.Options;
using System.Buffers.Binary;
using System.Collections.Immutable;

namespace HPD.Base.Sqlite.Tests.Storage;

public sealed class SqliteActivationCertificationTests
{
    [Fact]
    public async Task ProviderRejectsExpiredObservationToken()
    {
        await using SqliteRecordStore store = SqliteTestFactory.Create();
        byte[] token = new byte[56];
        BinaryPrimitives.WriteInt64BigEndian(token, 0);
        BinaryPrimitives.WriteInt64BigEndian(token.AsSpan(8), 0);
        BinaryPrimitives.WriteInt64BigEndian(token.AsSpan(16), 0);

        BaseDueWaitResult result = await store.WaitForDueChangeAsync(
            new BaseDueObservationToken { Value = token.ToImmutableArray() }, DateTimeOffset.UtcNow.AddSeconds(1));

        Assert.Equal(BaseDueWaitOutcome.TokenInvalid, result.Outcome);
    }

    [Fact]
    public async Task DescriptorDeclaresWholeStoreRecoveryModes()
    {
        await using var fixture = new Fixture(durable: true);
        BaseActivationProviderDescriptor descriptor = fixture.Descriptor;
        Assert.True(descriptor.Capability.BackupModes.SequenceEqual([BaseActivationBackupMode.WholeStoreAtomic]));
        Assert.True(descriptor.Capability.RestoreModes.SequenceEqual(
            [BaseActivationRestoreMode.InPlaceRecovery, BaseActivationRestoreMode.NewDisasterDomain]));
    }

    [Fact]
    public async Task DescriptorBindsExecutedProviderMatrix()
    {
        await using var fixture = new Fixture();
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
    public async Task DurableDescriptorBindsExecutedProviderMatrix()
    {
        await using var fixture = new Fixture(durable: true);
        BaseActivationCertificationReport report = await BaseActivationProviderCertification.RunAsync(
            fixture, TimeSpan.FromSeconds(5));

        Assert.True(report.Passed);
        string expectedReport = Convert.ToHexStringLower(fixture.Descriptor.CertificationReportChecksum.AsSpan());
        string actualReport = Convert.ToHexStringLower(report.ReportChecksum.AsSpan());
        Assert.True(expectedReport == actualReport, $"expected={expectedReport}; actual={actualReport}");
    }

    private sealed class Fixture : IBaseActivationCertificationFixture
    {
        private readonly string? _path;
        private readonly BaseOpaqueTokenProtector? _protector;
        private readonly SqliteRecordStore _store;

        public Fixture(bool durable = false)
        {
            if (!durable)
            {
                _store = SqliteTestFactory.Create();
                return;
            }
            _path = Path.Combine(Path.GetTempPath(), $"hpd-base-activation-certification-{Guid.NewGuid():N}.db");
            _protector = new BaseOpaqueTokenProtector(Options.Create(new HPDBaseTokenProtectionOptions
            {
                ActiveKey = new BaseOpaqueTokenKey
                {
                    Id = 1, Key = Enumerable.Repeat((byte)0x51, 32).ToArray(), IssueNotBefore = DateTimeOffset.UnixEpoch,
                },
            }));
            _store = SqliteTestFactory.Create(new HPDBaseSqliteOptions
            {
                StoreId = "activation-certification", DataSource = _path, AdministrationEnabled = true,
                Collections = [SqliteTestFactory.Collection()],
            }, tokenProtector: _protector);
        }

        public BaseActivationProviderDescriptor Descriptor => ((IBaseActivationProvider)_store).Descriptor;

        public IBaseActivationProvider Provider => _store;

        public ValueTask PrepareAsync(
            BaseActivationCertificationCaseRequest request,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(request);
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.CompletedTask;
        }

        public async ValueTask DisposeAsync()
        {
            await _store.DisposeAsync();
            _protector?.Dispose();
            if (_path is not null)
                foreach (string suffix in new[] { "", "-wal", "-shm" })
                    if (File.Exists(_path + suffix)) File.Delete(_path + suffix);
        }
    }
}
