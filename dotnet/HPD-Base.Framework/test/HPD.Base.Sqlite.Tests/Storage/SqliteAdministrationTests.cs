using System.Buffers.Binary;
using System.Reflection;
using FluentAssertions;
using HPD.Base;
using HPD.Base.Sqlite;
using Microsoft.Extensions.Options;

namespace HPD.Base.Sqlite.Tests.Storage;

public sealed class SqliteAdministrationTests
{
    [Fact]
    public async Task ArtifactValidationDistinguishesRetainedUnknownOversizedAndTruncatedInputs()
    {
        string path = Path.Combine(Path.GetTempPath(), $"hpd-base-admin-{Guid.NewGuid():N}.db");
        byte[] oldKey = Enumerable.Repeat((byte)0x41, 32).ToArray();
        byte[] newKey = Enumerable.Repeat((byte)0x42, 32).ToArray();
        try
        {
            byte[] artifact;
            using BaseOpaqueTokenProtector original = Protector(7, oldKey);
            await using (SqliteRecordStore creator = Store(path, original))
            {
                var destination = new MemoryStream();
                OperationResult<BaseBackupManifest> created = await creator.CreateBackupAsync(destination, BackupRequest());
                created.IsSuccess().Should().BeTrue(created.Error?.Code);
                artifact = destination.ToArray();
            }

            using BaseOpaqueTokenProtector rotated = Protector(
                8,
                newKey,
                new BaseOpaqueTokenKey { Id = 7, Key = oldKey });
            await using SqliteRecordStore validator = Store(path, rotated);

            (await validator.ValidateBackupAsync(new MemoryStream(artifact), ValidationRequest()))
                .IsSuccess().Should().BeTrue("retained keys must validate older artifacts");

            byte[] unknownKey = [.. artifact];
            unknownKey[11] = 99;
            (await validator.ValidateBackupAsync(new MemoryStream(unknownKey), ValidationRequest()))
                .Error!.Code.Should().Be(BaseAdministrationErrorCodes.ArtifactKeyUnavailable);

            byte[] oversized = [.. artifact];
            BinaryPrimitives.WriteUInt64BigEndian(oversized.AsSpan(16, 8), 32UL * 1024 * 1024);
            (await validator.ValidateBackupAsync(new MemoryStream(oversized), ValidationRequest()))
                .Error!.Code.Should().Be(BaseAdministrationErrorCodes.ArtifactTooLarge);

            (await validator.ValidateBackupAsync(new MemoryStream(artifact[..^1]), ValidationRequest()))
                .Error!.Code.Should().Be(BaseAdministrationErrorCodes.ArtifactInvalid);
        }
        finally { Cleanup(path); }
    }

    [Fact]
    public async Task RestoreRejectsMissingConfirmationAndBothIdentityMismatchesBeforeReplacement()
    {
        string path = Path.Combine(Path.GetTempPath(), $"hpd-base-admin-restore-{Guid.NewGuid():N}.db");
        using BaseOpaqueTokenProtector protector = Protector(3, Enumerable.Repeat((byte)0x33, 32).ToArray());
        try
        {
            await using SqliteRecordStore store = Store(path, protector);
            var destination = new MemoryStream();
            OperationResult<BaseBackupManifest> created = await store.CreateBackupAsync(destination, BackupRequest());
            created.IsSuccess().Should().BeTrue(created.Error?.Code);
            BaseBackupManifest manifest = created.Value!;
            byte[] artifact = destination.ToArray();

            BaseRestoreRequest valid = new()
            {
                StoreId = "sqlite",
                Principal = Principal(),
                ExpectedCurrentStoreIdentityDigest = manifest.StoreIdentityDigest,
                ExpectedArtifactStoreIdentityDigest = manifest.StoreIdentityDigest,
                IdentityMode = BaseRestoreIdentityMode.RequireCurrentStoreIdentity,
                RecoveryImageRetention = BaseRecoveryImageRetention.DeleteAfterSuccessfulRestore,
                ConfirmDestructiveReplacement = true,
            };
            BaseError confirmation = (await store.RestoreAsync(new MemoryStream(artifact), valid with { ConfirmDestructiveReplacement = false })).Error!;
            BaseError artifactMismatch = (await store.RestoreAsync(new MemoryStream(artifact), valid with { ExpectedArtifactStoreIdentityDigest = new string('0', 64) })).Error!;
            BaseError currentMismatch = (await store.RestoreAsync(new MemoryStream(artifact), valid with { ExpectedCurrentStoreIdentityDigest = new string('0', 64) })).Error!;

            confirmation.Code.Should().Be(BaseAdministrationErrorCodes.RestoreConfirmationRequired);
            confirmation.RestoreFailureDisposition.Should().Be(BaseRestoreFailureDisposition.RejectedBeforeChange);
            artifactMismatch.Code.Should().Be(BaseAdministrationErrorCodes.RestoreIdentityMismatch);
            artifactMismatch.RestoreFailureDisposition.Should().Be(BaseRestoreFailureDisposition.RejectedBeforeChange);
            currentMismatch.Code.Should().Be(BaseAdministrationErrorCodes.RestoreIdentityMismatch);
            currentMismatch.RestoreFailureDisposition.Should().Be(BaseRestoreFailureDisposition.OriginalPreserved);

            store.RestoreRecoveryPending.Should().BeFalse();
        }
        finally { Cleanup(path); }
    }

    [Fact]
    public async Task StartupRecoversOriginalRenamedCrashStateBeforeOpeningStore()
    {
        string path = Path.Combine(Path.GetTempPath(), $"hpd-base-admin-recovery-{Guid.NewGuid():N}.db");
        string staging = path + ".restore-staging";
        string recovery = path + ".recovery-test";
        using BaseOpaqueTokenProtector protector = Protector(4, Enumerable.Repeat((byte)0x44, 32).ToArray());
        try
        {
            await using (SqliteRecordStore store = Store(path, protector))
            {
                File.Copy(path, staging);
                typeof(SqliteRecordStore)
                    .GetMethod("WriteRestoreMarker", BindingFlags.Instance | BindingFlags.NonPublic)!
                    .Invoke(store, ["OriginalRenamed", staging, recovery, new string('1', 64), new string('2', 64)]);
            }
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            File.Move(path, recovery);

            await using SqliteRecordStore restarted = Store(path, protector);

            File.Exists(path).Should().BeTrue();
            File.Exists(recovery).Should().BeFalse();
            File.Exists(staging).Should().BeFalse();
            File.Exists(path + ".restore-state").Should().BeFalse();
            restarted.RestoreRecoveryPending.Should().BeFalse();
        }
        finally { Cleanup(path); }
    }

    [Fact]
    public async Task NonCooperativeRestoreStagingIsBoundedAndRetainsCapacityUntilCompletion()
    {
        string path = Path.Combine(Path.GetTempPath(), $"hpd-base-admin-staging-{Guid.NewGuid():N}.db");
        using BaseOpaqueTokenProtector protector = Protector(5, Enumerable.Repeat((byte)0x55, 32).ToArray());
        var source = new BlockingReadStream();
        try
        {
            await using SqliteRecordStore store = Store(path, protector, TimeSpan.FromSeconds(1));
            var destination = new MemoryStream();
            BaseBackupManifest manifest = (await store.CreateBackupAsync(destination, BackupRequest())).Value!;
            BaseRestoreRequest request = new()
            {
                StoreId = "sqlite",
                Principal = Principal(),
                ExpectedCurrentStoreIdentityDigest = manifest.StoreIdentityDigest,
                ExpectedArtifactStoreIdentityDigest = manifest.StoreIdentityDigest,
                IdentityMode = BaseRestoreIdentityMode.RequireCurrentStoreIdentity,
                RecoveryImageRetention = BaseRecoveryImageRetention.DeleteAfterSuccessfulRestore,
                ConfirmDestructiveReplacement = true,
            };

            OperationResult<BaseRestoreResult> timedOut = await store.RestoreAsync(source, request);

            timedOut.Error!.Code.Should().Be(BaseAdministrationErrorCodes.RestoreTimeout);
            timedOut.Error.RestoreFailureDisposition.Should().Be(BaseRestoreFailureDisposition.RejectedBeforeChange);
            store.QuarantinedAdministrationCount.Should().Be(1);

            source.Complete();
            SpinWait.SpinUntil(() => store.QuarantinedAdministrationCount == 0, TimeSpan.FromSeconds(2)).Should().BeTrue();
        }
        finally
        {
            source.Complete();
            Cleanup(path);
        }
    }

    private static SqliteRecordStore Store(
        string path,
        BaseOpaqueTokenProtector protector,
        TimeSpan? restoreStagingTimeout = null)
    {
        SqliteRecordStore store = SqliteTestFactory.Create(new HPDBaseSqliteOptions
        {
            StoreId = "sqlite",
            DataSource = path,
            AdministrationEnabled = true,
            RestoreStagingTimeout = restoreStagingTimeout ?? TimeSpan.FromMinutes(10),
            MaxBackupArtifactBytes = 16 * 1024 * 1024,
            Collections = [SqliteTestFactory.Collection()],
        }, tokenProtector: protector);
        using var connection = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={path};Pooling=False");
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT OR IGNORE INTO hpd_base_schema_identity(singleton,store_instance_id) VALUES (1,'administration-test-instance');
            INSERT OR IGNORE INTO hpd_base_schema_baseline(application_id,store_instance_id,baseline_id,checksum,generation,last_plan_id,applied_at)
            VALUES ('administration-test','administration-test-instance','baseline-1','checksum-1',1,'plan-1','2026-08-03T00:00:00Z');
            """;
        command.ExecuteNonQuery();
        return store;
    }

    private static BaseOpaqueTokenProtector Protector(
        byte id,
        byte[] key,
        params BaseOpaqueTokenKey[] retained) => new(Options.Create(new HPDBaseTokenProtectionOptions
        {
            ActiveKey = new BaseOpaqueTokenKey { Id = id, Key = key },
            DecryptionKeys = retained,
        }));

    private static BaseBackupRequest BackupRequest() => new() { StoreId = "sqlite", Principal = Principal() };
    private static BaseBackupValidationRequest ValidationRequest() => new() { StoreId = "sqlite", Principal = Principal() };
    private static PrincipalContext Principal() => new() { AuthenticationState = PrincipalAuthenticationState.System };

    private static void Cleanup(string path)
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        string directory = Path.GetDirectoryName(path)!;
        string name = Path.GetFileName(path);
        foreach (string candidate in Directory.GetFiles(directory).Where(file => Path.GetFileName(file).Contains(name, StringComparison.Ordinal)))
            File.Delete(candidate);
    }

    private sealed class BlockingReadStream : Stream
    {
        private readonly TaskCompletionSource<int> _read = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public void Complete() => _read.TrySetResult(0);
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) => new(_read.Task);
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override void Flush() { }
    }
}
