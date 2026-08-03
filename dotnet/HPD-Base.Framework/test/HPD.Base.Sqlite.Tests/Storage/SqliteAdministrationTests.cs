using System.Buffers.Binary;
using System.Reflection;
using FluentAssertions;
using HPD.Base;
using HPD.Base.Sqlite;
using Microsoft.Extensions.Options;
using System.Security.AccessControl;

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
    public async Task SuccessfulRestorePreservesProviderOwnedFileSecurityPolicy()
    {
        string path = Path.Combine(Path.GetTempPath(), $"hpd-base-admin-permissions-{Guid.NewGuid():N}.db");
        using BaseOpaqueTokenProtector protector = Protector(12, Enumerable.Repeat((byte)0x12, 32).ToArray());
        try
        {
            await using SqliteRecordStore store = Store(path, protector);
            UnixFileMode? unixMode = null;
            byte[]? windowsDescriptor = null;
            if (OperatingSystem.IsWindows())
            {
                windowsDescriptor = new FileInfo(path)
                    .GetAccessControl(AccessControlSections.All)
                    .GetSecurityDescriptorBinaryForm();
            }
            else
            {
                unixMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
                File.SetUnixFileMode(path, unixMode.Value);
            }
            var destination = new MemoryStream();
            BaseBackupManifest manifest = (await store.CreateBackupAsync(destination, BackupRequest())).Value!;
            destination.Position = 0;

            OperationResult<BaseRestoreResult> restored = await store.RestoreAsync(
                destination,
                new BaseRestoreRequest
                {
                    StoreId = "sqlite",
                    Principal = Principal(),
                    ExpectedCurrentStoreIdentityDigest = manifest.StoreIdentityDigest,
                    ExpectedArtifactStoreIdentityDigest = manifest.StoreIdentityDigest,
                    IdentityMode = BaseRestoreIdentityMode.RequireCurrentStoreIdentity,
                    RecoveryImageRetention = BaseRecoveryImageRetention.DeleteAfterSuccessfulRestore,
                    ConfirmDestructiveReplacement = true,
                });

            restored.IsSuccess().Should().BeTrue(restored.Error?.Code);
            if (OperatingSystem.IsWindows())
            {
                new FileInfo(path).GetAccessControl(AccessControlSections.All)
                    .GetSecurityDescriptorBinaryForm().Should().Equal(windowsDescriptor!);
            }
            else
            {
                File.GetUnixFileMode(path).Should().Be(unixMode);
            }
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

    [Theory]
    [InlineData("Prepared")]
    [InlineData("ReplacementInstalled")]
    [InlineData("ReplacementValidated")]
    [InlineData("Completed")]
    public async Task StartupResolvesEveryOtherDurableRestoreMarkerState(string state)
    {
        string path = Path.Combine(Path.GetTempPath(), $"hpd-base-admin-marker-{state}-{Guid.NewGuid():N}.db");
        string staging = path + ".restore-staging";
        string recovery = path + ".recovery-test";
        using BaseOpaqueTokenProtector protector = Protector(10, Enumerable.Repeat((byte)0x10, 32).ToArray());
        try
        {
            await using (SqliteRecordStore store = Store(path, protector))
            {
                File.Copy(path, staging);
                if (state is "ReplacementInstalled" or "ReplacementValidated" or "Completed")
                    File.Copy(path, recovery);
                typeof(SqliteRecordStore)
                    .GetMethod("WriteRestoreMarker", BindingFlags.Instance | BindingFlags.NonPublic)!
                    .Invoke(store, [state, staging, recovery, new string('1', 64), new string('2', 64)]);
            }
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();

            await using SqliteRecordStore restarted = Store(path, protector);

            File.Exists(path).Should().BeTrue();
            File.Exists(staging).Should().BeFalse();
            File.Exists(path + ".restore-state").Should().BeFalse();
            restarted.RestoreRecoveryPending.Should().BeFalse();
            if (state == "ReplacementInstalled")
                File.Exists(recovery).Should().BeFalse("the preserved original must be restored");
            else if (state is "ReplacementValidated" or "Completed")
                File.Exists(recovery).Should().BeTrue("the completed marker cannot override the requested recovery retention");
        }
        finally { Cleanup(path); }
    }

    [Fact]
    public async Task InvalidRestoreMarkerFailsClosedBeforeAnyDatabaseOpen()
    {
        string path = Path.Combine(Path.GetTempPath(), $"hpd-base-admin-invalid-marker-{Guid.NewGuid():N}.db");
        using BaseOpaqueTokenProtector protector = Protector(11, Enumerable.Repeat((byte)0x11, 32).ToArray());
        try
        {
            SqliteRecordStore initialized = Store(path, protector);
            await initialized.DisposeAsync();
            File.WriteAllText(path + ".restore-state", "not-authenticated-restore-state");

            Action restart = () => _ = Store(path, protector);

            restart.Should().Throw<InvalidOperationException>()
                .WithMessage("*restore recovery state is invalid*");
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

    [Fact]
    public async Task NonCooperativePostInstallValidationKeepsRestoreMaintenanceClosedUntilCompletion()
    {
        string path = Path.Combine(Path.GetTempPath(), $"hpd-base-admin-post-install-{Guid.NewGuid():N}.db");
        using BaseOpaqueTokenProtector protector = Protector(6, Enumerable.Repeat((byte)0x66, 32).ToArray());
        var operations = new BlockingAdministrationOperations();
        try
        {
            await using SqliteRecordStore store = Store(
                path,
                protector,
                TimeSpan.FromSeconds(1),
                operations);
            var destination = new MemoryStream();
            BaseBackupManifest manifest = (await store.CreateBackupAsync(destination, BackupRequest())).Value!;
            destination.Position = 0;

            OperationResult<BaseRestoreResult> indeterminate = await store.RestoreAsync(
                destination,
                new BaseRestoreRequest
                {
                    StoreId = "sqlite",
                    Principal = Principal(),
                    ExpectedCurrentStoreIdentityDigest = manifest.StoreIdentityDigest,
                    ExpectedArtifactStoreIdentityDigest = manifest.StoreIdentityDigest,
                    IdentityMode = BaseRestoreIdentityMode.RequireCurrentStoreIdentity,
                    RecoveryImageRetention = BaseRecoveryImageRetention.DeleteAfterSuccessfulRestore,
                    ConfirmDestructiveReplacement = true,
                });

            indeterminate.Error!.Code.Should().Be(BaseAdministrationErrorCodes.RestoreIndeterminate);
            indeterminate.Error.RestoreFailureDisposition.Should().Be(BaseRestoreFailureDisposition.IndeterminateUnavailable);
            store.QuarantinedAdministrationCount.Should().Be(1);
            store.RestoreRecoveryPending.Should().BeTrue();

            operations.Release();
            SpinWait.SpinUntil(
                () => store.QuarantinedAdministrationCount == 0 && !store.RestoreRecoveryPending,
                TimeSpan.FromSeconds(5)).Should().BeTrue();
        }
        finally
        {
            operations.Release();
            Cleanup(path);
        }
    }

    [Fact]
    public async Task FailedPostInstallValidationRestoresAndReopensTheVerifiedOriginal()
    {
        string path = Path.Combine(Path.GetTempPath(), $"hpd-base-admin-post-install-failure-{Guid.NewGuid():N}.db");
        using BaseOpaqueTokenProtector protector = Protector(13, Enumerable.Repeat((byte)0x13, 32).ToArray());
        var operations = new FailingAdministrationOperations("postInstallValidation");
        try
        {
            await using SqliteRecordStore store = Store(path, protector, administrationOperations: operations);
            var destination = new MemoryStream();
            BaseBackupManifest manifest = (await store.CreateBackupAsync(destination, BackupRequest())).Value!;
            destination.Position = 0;

            using (var connection = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={path};Pooling=False"))
            {
                connection.Open();
                using var command = connection.CreateCommand();
                command.CommandText = "INSERT INTO hpd_base_provider_state(key,value) VALUES ('original_sentinel','preserved');";
                command.ExecuteNonQuery();
            }

            OperationResult<BaseRestoreResult> failed = await store.RestoreAsync(
                destination,
                new BaseRestoreRequest
                {
                    StoreId = "sqlite",
                    Principal = Principal(),
                    ExpectedCurrentStoreIdentityDigest = manifest.StoreIdentityDigest,
                    ExpectedArtifactStoreIdentityDigest = manifest.StoreIdentityDigest,
                    IdentityMode = BaseRestoreIdentityMode.RequireCurrentStoreIdentity,
                    RecoveryImageRetention = BaseRecoveryImageRetention.DeleteAfterSuccessfulRestore,
                    ConfirmDestructiveReplacement = true,
                });

            failed.Error!.Code.Should().Be(BaseAdministrationErrorCodes.RestoreFailed);
            failed.Error.RestoreFailureDisposition.Should().Be(BaseRestoreFailureDisposition.RecoveryRestoredOriginal);
            store.RestoreRecoveryPending.Should().BeFalse();

            using var verified = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={path};Pooling=False");
            verified.Open();
            using var read = verified.CreateCommand();
            read.CommandText = "SELECT value FROM hpd_base_provider_state WHERE key='original_sentinel';";
            read.ExecuteScalar().Should().Be("preserved");
        }
        finally { Cleanup(path); }
    }

    [Fact]
    public async Task UnrecoverablePostInstallFailureLeavesProviderClosedAndUnhealthy()
    {
        string path = Path.Combine(Path.GetTempPath(), $"hpd-base-admin-unrecoverable-{Guid.NewGuid():N}.db");
        using BaseOpaqueTokenProtector protector = Protector(14, Enumerable.Repeat((byte)0x14, 32).ToArray());
        HPDBaseSqliteOptions options = OptionsFor(path);
        try
        {
            await using SqliteRecordStore store = Store(path, protector);
            typeof(SqliteRecordStore)
                .GetMethod("WriteRestoreMarker", BindingFlags.Instance | BindingFlags.NonPublic)!
                .Invoke(store,
                ["ReplacementInstalled", path + ".restore-staging", path + ".missing-recovery", new string('1', 64), new string('2', 64)]);
            store.RestoreRecoveryPending.Should().BeTrue();

            Func<Task> ordinaryOpen = async () => await store.GetMutationJournalBoundsAsync();
            await ordinaryOpen.Should().ThrowAsync<InvalidOperationException>()
                .WithMessage("*restore recovery is incomplete*");

            HealthDescriptor health = (await new SqliteHealthContributor(Options.Create(options), store).GetHealthAsync()).Single();
            health.Status.Should().Be(HealthStatus.Unhealthy);
            health.Summary.Should().Be("SQLite restore recovery is incomplete and the store is unavailable.");
        }
        finally { Cleanup(path); }
    }

    private static SqliteRecordStore Store(
        string path,
        BaseOpaqueTokenProtector protector,
        TimeSpan? restoreStagingTimeout = null,
        ISqliteAdministrationOperationController? administrationOperations = null)
    {
        HPDBaseSqliteOptions options = OptionsFor(path, restoreStagingTimeout);
        SqliteRecordStore store = SqliteTestFactory.Create(options, administrationOperations: administrationOperations, tokenProtector: protector);
        InitializeAuthorityMetadata(path);
        return store;
    }

    private static HPDBaseSqliteOptions OptionsFor(string path, TimeSpan? restoreStagingTimeout = null) => new()
    {
        StoreId = "sqlite",
        DataSource = path,
        AdministrationEnabled = true,
        RestoreStagingTimeout = restoreStagingTimeout ?? TimeSpan.FromMinutes(10),
        IntegrityCheckTimeout = restoreStagingTimeout ?? TimeSpan.FromMinutes(5),
        AdministrationAcquisitionTimeout = restoreStagingTimeout ?? TimeSpan.FromSeconds(30),
        MaxBackupArtifactBytes = 16 * 1024 * 1024,
        Collections = [SqliteTestFactory.Collection()],
    };

    private static void InitializeAuthorityMetadata(string path)
    {
        using var connection = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={path};Pooling=False");
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT OR IGNORE INTO hpd_base_schema_identity(singleton,store_instance_id) VALUES (1,'administration-test-instance');
            INSERT OR IGNORE INTO hpd_base_schema_baseline(application_id,store_instance_id,baseline_id,checksum,generation,last_plan_id,applied_at)
            VALUES ('administration-test','administration-test-instance','baseline-1','checksum-1',1,'plan-1','2026-08-03T00:00:00Z');
            """;
        command.ExecuteNonQuery();
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

    private sealed class BlockingAdministrationOperations : ISqliteAdministrationOperationController
    {
        private readonly TaskCompletionSource _release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public void Release() => _release.TrySetResult();
        public ValueTask BeforePhaseAsync(string phase, CancellationToken cancellationToken) =>
            string.Equals(phase, "postInstallValidation", StringComparison.Ordinal)
                ? new ValueTask(_release.Task)
                : ValueTask.CompletedTask;
    }

    private sealed class FailingAdministrationOperations(string phase) : ISqliteAdministrationOperationController
    {
        public ValueTask BeforePhaseAsync(string currentPhase, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return string.Equals(currentPhase, phase, StringComparison.Ordinal)
                ? ValueTask.FromException(new InvalidOperationException("Injected administration failure."))
                : ValueTask.CompletedTask;
        }
    }

}
