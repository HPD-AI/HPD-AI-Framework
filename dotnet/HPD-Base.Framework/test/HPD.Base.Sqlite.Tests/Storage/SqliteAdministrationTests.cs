using System.Buffers.Binary;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using FluentAssertions;
using HPD.Base;
using HPD.Base.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using System.Security.AccessControl;

namespace HPD.Base.Sqlite.Tests.Storage;

public sealed class SqliteAdministrationTests
{
    [Fact]
    public async Task ArtifactValidationDistinguishesRetainedUnknownOversizedAndTruncatedInputs()
    {
        string path = Path.Combine(AdministrationTempDirectory(), $"hpd-base-admin-{Guid.NewGuid():N}.db");
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
                new BaseOpaqueTokenKey { Id = 7, Key = oldKey, IssueNotBefore = DateTimeOffset.UnixEpoch });
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
    public async Task AuthenticatedManifestFactsMustMatchTheStagedDatabase()
    {
        string path = Path.Combine(AdministrationTempDirectory(), $"hpd-base-admin-manifest-binding-{Guid.NewGuid():N}.db");
        using BaseOpaqueTokenProtector protector = Protector(17, Enumerable.Repeat((byte)0x17, 32).ToArray());
        try
        {
            await using SqliteRecordStore store = Store(path, protector);
            var destination = new MemoryStream();
            BaseBackupManifest manifest = (await store.CreateBackupAsync(destination, BackupRequest())).Value!;
            byte[] altered = RewriteAuthenticatedArtifact(
                destination.ToArray(),
                protector,
                value => value with { SchemaGeneration = value.SchemaGeneration + 1 });

            (await store.ValidateBackupAsync(new MemoryStream(altered), ValidationRequest()))
                .Error!.Code.Should().Be(BaseAdministrationErrorCodes.ArtifactInvalid);

            OperationResult<BaseRestoreResult> restore = await store.RestoreAsync(
                new MemoryStream(altered),
                RestoreRequest(manifest));
            restore.Error!.Code.Should().Be(BaseAdministrationErrorCodes.RestoreIdentityMismatch);
            restore.Error.RestoreFailureDisposition.Should().Be(BaseRestoreFailureDisposition.RejectedBeforeChange);
        }
        finally { Cleanup(path); }
    }

    [Theory]
    [InlineData("hpd_base_schema_assets")]
    [InlineData("hpd_base_schema_history")]
    [InlineData("hpd_base_schema_lease")]
    [InlineData("hpd_base_operation_receipts")]
    [InlineData("hpd_base_mutation_journal")]
    public async Task AuthenticatedArtifactWithIncompleteProviderSchemaIsInvalid(string table)
    {
        string path = Path.Combine(AdministrationTempDirectory(), $"hpd-base-admin-schema-validation-{Guid.NewGuid():N}.db");
        using BaseOpaqueTokenProtector protector = Protector(18, Enumerable.Repeat((byte)0x18, 32).ToArray());
        try
        {
            await using SqliteRecordStore store = Store(path, protector);
            var destination = new MemoryStream();
            (await store.CreateBackupAsync(destination, BackupRequest())).IsSuccess().Should().BeTrue();
            byte[] altered = RewriteAuthenticatedArtifact(
                destination.ToArray(),
                protector,
                manifest => manifest,
                payloadPath => ExecuteSql(payloadPath, $"DROP TABLE {table};"));

            (await store.ValidateBackupAsync(new MemoryStream(altered), ValidationRequest()))
                .Error!.Code.Should().Be(BaseAdministrationErrorCodes.ArtifactInvalid);
        }
        finally { Cleanup(path); }
    }

    [Fact]
    public async Task AuthenticatedArtifactWithMalformedReceiptShapeIsInvalid()
    {
        string path = Path.Combine(AdministrationTempDirectory(), $"hpd-base-admin-malformed-receipts-{Guid.NewGuid():N}.db");
        using BaseOpaqueTokenProtector protector = Protector(22, Enumerable.Repeat((byte)0x22, 32).ToArray());
        try
        {
            await using SqliteRecordStore store = Store(path, protector);
            var destination = new MemoryStream();
            (await store.CreateBackupAsync(destination, BackupRequest())).IsSuccess().Should().BeTrue();
            byte[] altered = RewriteAuthenticatedArtifact(
                destination.ToArray(),
                protector,
                manifest => manifest,
                payloadPath => ExecuteSql(payloadPath, """
                    ALTER TABLE hpd_base_operation_receipts RENAME TO hpd_base_operation_receipts_old;
                    CREATE TABLE hpd_base_operation_receipts (
                      scope TEXT NOT NULL, operation TEXT NOT NULL, idempotency_key TEXT NOT NULL,
                      fingerprint TEXT NOT NULL, structural_digest BLOB NOT NULL, result_json BLOB NOT NULL,
                      result_format_version INTEGER NOT NULL, schema_generation INTEGER NOT NULL,
                      store_instance_id TEXT NOT NULL, committed_at TEXT NOT NULL, expires_at TEXT NOT NULL,
                      PRIMARY KEY(scope, operation, idempotency_key)
                    ) WITHOUT ROWID;
                    DROP TABLE hpd_base_operation_receipts_old;
                    """));

            (await store.ValidateBackupAsync(new MemoryStream(altered), ValidationRequest()))
                .Error!.Code.Should().Be(BaseAdministrationErrorCodes.ArtifactInvalid);
        }
        finally { Cleanup(path); }
    }

    [Fact]
    public async Task RestoreRejectsMissingConfirmationAndBothIdentityMismatchesBeforeReplacement()
    {
        string path = Path.Combine(AdministrationTempDirectory(), $"hpd-base-admin-restore-{Guid.NewGuid():N}.db");
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
        string path = Path.Combine(AdministrationTempDirectory(), $"hpd-base-admin-permissions-{Guid.NewGuid():N}.db");
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
    public async Task AdministrationRejectsSymlinkedParentDirectory()
    {
        if (OperatingSystem.IsWindows()) return;
        string root = Path.Combine(AdministrationTempDirectory(), $"hpd-base-admin-real-{Guid.NewGuid():N}");
        string alias = root + "-alias";
        Directory.CreateDirectory(root);
        Directory.CreateSymbolicLink(alias, root);
        string path = Path.Combine(alias, "store.db");
        using BaseOpaqueTokenProtector protector = Protector(19, Enumerable.Repeat((byte)0x19, 32).ToArray());
        try
        {
            await using SqliteRecordStore store = Store(path, protector);
            OperationResult<BaseBackupManifest> result = await store.CreateBackupAsync(new MemoryStream(), BackupRequest());
            result.Error!.Code.Should().Be(BaseAdministrationErrorCodes.CapabilityUnavailable);
            File.Exists(Path.Combine(root, "store.db")).Should().BeTrue();
            File.Exists(path + ".restore-state").Should().BeFalse();
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            if (Directory.Exists(alias)) Directory.Delete(alias);
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task RestoreRejectsDirectoryIdentitySwapBeforeMaintenanceBegins()
    {
        if (OperatingSystem.IsWindows()) return;
        string root = Path.Combine(AdministrationTempDirectory(), $"hpd-base-admin-path-swap-{Guid.NewGuid():N}");
        string moved = root + "-original";
        Directory.CreateDirectory(root);
        string path = Path.Combine(root, "store.db");
        using BaseOpaqueTokenProtector protector = Protector(20, Enumerable.Repeat((byte)0x20, 32).ToArray());
        byte[] artifact;
        BaseBackupManifest manifest;
        try
        {
            await using (SqliteRecordStore creator = Store(path, protector))
            {
                var destination = new MemoryStream();
                manifest = (await creator.CreateBackupAsync(destination, BackupRequest())).Value!;
                artifact = destination.ToArray();
            }

            var swap = new CallbackAdministrationOperations("beforeCheckpointPathValidation", () =>
            {
                Directory.Move(root, moved);
                Directory.CreateDirectory(root);
            });
            await using SqliteRecordStore store = Store(path, protector, administrationOperations: swap);
            OperationResult<BaseRestoreResult> result = await store.RestoreAsync(new MemoryStream(artifact), RestoreRequest(manifest));

            result.IsSuccess().Should().BeFalse();
            result.Error!.RestoreFailureDisposition.Should().Be(BaseRestoreFailureDisposition.OriginalPreserved);
            File.Exists(Path.Combine(moved, "store.db")).Should().BeTrue();
            File.Exists(Path.Combine(root, "store.db.restore-state")).Should().BeFalse();
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
            if (Directory.Exists(moved)) Directory.Delete(moved, recursive: true);
        }
    }

    [Theory]
    [InlineData("completedMarker")]
    [InlineData("recoveryDatabase")]
    [InlineData("recoveryWal")]
    [InlineData("recoveryShm")]
    public async Task RestoreNeverReportsSuccessWhenRequiredFinalizationDeletionFails(string failure)
    {
        string path = Path.Combine(AdministrationTempDirectory(), $"hpd-base-admin-cleanup-{failure}-{Guid.NewGuid():N}.db");
        using BaseOpaqueTokenProtector protector = Protector(23, Enumerable.Repeat((byte)0x23, 32).ToArray());
        byte[] artifact;
        BaseBackupManifest manifest;
        try
        {
            await using (SqliteRecordStore creator = Store(path, protector))
            {
                var destination = new MemoryStream();
                manifest = (await creator.CreateBackupAsync(destination, BackupRequest())).Value!;
                artifact = destination.ToArray();
            }

            await using SqliteRecordStore store = Store(
                path,
                protector,
                administrationOperations: new DeletionFailureAdministrationOperations(failure, path));
            OperationResult<BaseRestoreResult> result = await store.RestoreAsync(new MemoryStream(artifact), RestoreRequest(manifest));

            result.IsSuccess().Should().BeFalse();
            if (failure == "recoveryDatabase")
            {
                result.Error!.RestoreFailureDisposition.Should().Be(BaseRestoreFailureDisposition.RecoveryRestoredOriginal);
                store.RestoreRecoveryPending.Should().BeFalse();
            }
            else
            {
                result.Error!.Code.Should().Be(BaseAdministrationErrorCodes.RestoreIndeterminate);
                result.Error.RestoreFailureDisposition.Should().Be(BaseRestoreFailureDisposition.IndeterminateUnavailable);
                store.RestoreRecoveryPending.Should().BeTrue();
            }
        }
        finally { Cleanup(path); }
    }

    [Theory]
    [InlineData("startupMarker")]
    [InlineData("startupStaging")]
    public async Task StartupCleanupFailureRetainsEvidenceAndConstructsIndeterminateProvider(string failure)
    {
        string path = Path.Combine(AdministrationTempDirectory(), $"hpd-base-admin-startup-cleanup-{failure}-{Guid.NewGuid():N}.db");
        string staging = path + ".restore-staging";
        string recovery = path + ".recovery-test";
        using BaseOpaqueTokenProtector protector = Protector(24, Enumerable.Repeat((byte)0x24, 32).ToArray());
        HPDBaseSqliteOptions options = OptionsFor(path);
        try
        {
            await using (SqliteRecordStore initialized = Store(path, protector))
            {
                if (failure == "startupStaging") BackupCopy(path, staging);
                typeof(SqliteRecordStore)
                    .GetMethod("WriteRestoreMarker", BindingFlags.Instance | BindingFlags.NonPublic)!
                    .Invoke(initialized, ["Prepared", staging, recovery, new string('1', 64), new string('2', 64)]);
            }
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();

            await using SqliteRecordStore restarted = SqliteTestFactory.Create(
                options,
                administrationOperations: new DeletionFailureAdministrationOperations(failure, path),
                tokenProtector: protector,
                initializeSchema: false);

            restarted.RestoreRecoveryIndeterminate.Should().BeTrue();
            File.Exists(path + ".restore-state").Should().BeTrue();
            if (failure == "startupStaging") File.Exists(staging).Should().BeTrue();
            HealthDescriptor health = (await new SqliteHealthContributor(Options.Create(options), restarted).GetHealthAsync()).Single();
            health.Status.Should().Be(HealthStatus.Unhealthy);
        }
        finally { Cleanup(path); }
    }

    [Fact]
    public async Task StartupRecoversOriginalRenamedCrashStateBeforeOpeningStore()
    {
        string path = Path.Combine(AdministrationTempDirectory(), $"hpd-base-admin-recovery-{Guid.NewGuid():N}.db");
        string staging = path + ".restore-staging";
        string recovery = path + ".recovery-test";
        using BaseOpaqueTokenProtector protector = Protector(4, Enumerable.Repeat((byte)0x44, 32).ToArray());
        try
        {
            await using (SqliteRecordStore store = Store(path, protector))
            {
                BackupCopy(path, staging);
                typeof(SqliteRecordStore)
                    .GetMethod("WriteRestoreMarker", BindingFlags.Instance | BindingFlags.NonPublic)!
                    .Invoke(store, ["OriginalRenamed", staging, recovery, new string('1', 64), new string('2', 64)]);
            }
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            File.Move(path, recovery);

            await using SqliteRecordStore restarted = Store(path, protector);

            restarted.RestoreRecoveryIndeterminate.Should().BeFalse();
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
        string path = Path.Combine(AdministrationTempDirectory(), $"hpd-base-admin-marker-{state}-{Guid.NewGuid():N}.db");
        string staging = path + ".restore-staging";
        string recovery = path + ".recovery-test";
        using BaseOpaqueTokenProtector protector = Protector(10, Enumerable.Repeat((byte)0x10, 32).ToArray());
        try
        {
            await using (SqliteRecordStore store = Store(path, protector))
            {
                BackupCopy(path, staging);
                if (state is "ReplacementInstalled" or "ReplacementValidated" or "Completed")
                    BackupCopy(path, recovery);
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
    public async Task InvalidRestoreMarkerConstructsMaintenanceClosedProviderWithHealthAndDiagnostics()
    {
        string path = Path.Combine(AdministrationTempDirectory(), $"hpd-base-admin-invalid-marker-{Guid.NewGuid():N}.db");
        using BaseOpaqueTokenProtector protector = Protector(11, Enumerable.Repeat((byte)0x11, 32).ToArray());
        try
        {
            SqliteRecordStore initialized = Store(path, protector);
            await initialized.DisposeAsync();
            File.WriteAllText(path + ".restore-state", "not-authenticated-restore-state");

            HPDBaseSqliteOptions options = OptionsFor(path);
            await using SqliteRecordStore restarted = SqliteTestFactory.Create(
                options,
                tokenProtector: protector,
                initializeSchema: false);

            restarted.RestoreRecoveryIndeterminate.Should().BeTrue();
            File.Exists(path + ".restore-state").Should().BeTrue("invalid recovery evidence must be retained");

            Func<Task> ordinaryOpen = async () => await restarted.GetMutationJournalBoundsAsync();
            await ordinaryOpen.Should().ThrowAsync<InvalidOperationException>()
                .WithMessage("*restore outcome is indeterminate*");

            HealthDescriptor health = (await new SqliteHealthContributor(Options.Create(options), restarted).GetHealthAsync()).Single();
            health.Status.Should().Be(HealthStatus.Unhealthy);
            health.Summary.Should().Be("SQLite restore outcome is indeterminate and the store is maintenance-closed.");

            DiagnosticDescriptor[] diagnostics = await new SqliteDiagnosticContributor(
                Options.Create(options),
                restarted,
                NullLogger<SqliteDiagnosticContributor>.Instance).GetDiagnosticsAsync();
            diagnostics.Should().ContainSingle(item => item.Code == "base.sqlite.restore.indeterminate");
        }
        finally { Cleanup(path); }
    }

    [Theory]
    [InlineData("checksumMismatch")]
    [InlineData("missingRecovery")]
    [InlineData("invalidRecovery")]
    public async Task InvalidStartupRecoveryStatesRetainEvidenceAndExposeUnhealthyProvider(string failure)
    {
        string path = Path.Combine(AdministrationTempDirectory(), $"hpd-base-admin-invalid-recovery-{failure}-{Guid.NewGuid():N}.db");
        string staging = path + ".restore-staging";
        string recovery = path + ".recovery-test";
        using BaseOpaqueTokenProtector protector = Protector(21, Enumerable.Repeat((byte)0x21, 32).ToArray());
        HPDBaseSqliteOptions options = OptionsFor(path);
        try
        {
            await using (SqliteRecordStore initialized = Store(path, protector))
            {
                typeof(SqliteRecordStore)
                    .GetMethod("WriteRestoreMarker", BindingFlags.Instance | BindingFlags.NonPublic)!
                    .Invoke(initialized, [failure == "checksumMismatch" ? "Prepared" : "ReplacementInstalled", staging, recovery, new string('1', 64), new string('2', 64)]);
            }
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();

            if (failure == "checksumMismatch")
            {
                SqliteRestoreMarker marker = JsonSerializer.Deserialize(
                    File.ReadAllBytes(path + ".restore-state"),
                    SqliteAdministrationJsonContext.Default.SqliteRestoreMarker)!;
                File.WriteAllBytes(
                    path + ".restore-state",
                    JsonSerializer.SerializeToUtf8Bytes(marker with { Checksum = new string('0', 64) }, SqliteAdministrationJsonContext.Default.SqliteRestoreMarker));
            }
            else if (failure == "invalidRecovery")
            {
                File.WriteAllText(recovery, "not-a-sqlite-database");
            }

            await using SqliteRecordStore restarted = SqliteTestFactory.Create(options, tokenProtector: protector, initializeSchema: false);
            restarted.RestoreRecoveryIndeterminate.Should().BeTrue();
            File.Exists(path + ".restore-state").Should().BeTrue();
            File.Exists(path).Should().BeTrue("failed recovery must not destroy the active database");
            if (failure == "invalidRecovery") File.Exists(recovery).Should().BeTrue();

            HealthDescriptor health = (await new SqliteHealthContributor(Options.Create(options), restarted).GetHealthAsync()).Single();
            health.Status.Should().Be(HealthStatus.Unhealthy);
            Func<Task> ordinaryOpen = async () => await restarted.GetMutationJournalBoundsAsync();
            await ordinaryOpen.Should().ThrowAsync<InvalidOperationException>();
        }
        finally { Cleanup(path); }
    }

    [Fact]
    public async Task NonCooperativeRestoreStagingIsBoundedAndRetainsCapacityUntilCompletion()
    {
        string path = Path.Combine(AdministrationTempDirectory(), $"hpd-base-admin-staging-{Guid.NewGuid():N}.db");
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
        string path = Path.Combine(AdministrationTempDirectory(), $"hpd-base-admin-post-install-{Guid.NewGuid():N}.db");
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
        string path = Path.Combine(AdministrationTempDirectory(), $"hpd-base-admin-post-install-failure-{Guid.NewGuid():N}.db");
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
        string path = Path.Combine(AdministrationTempDirectory(), $"hpd-base-admin-unrecoverable-{Guid.NewGuid():N}.db");
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

    private static void BackupCopy(string sourcePath, string destinationPath)
    {
        using var source = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={sourcePath};Pooling=False");
        using var destination = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={destinationPath};Mode=ReadWriteCreate;Pooling=False");
        source.Open();
        destination.Open();
        source.BackupDatabase(destination);
    }

    private static BaseOpaqueTokenProtector Protector(
        byte id,
        byte[] key,
        params BaseOpaqueTokenKey[] retained) => new(Options.Create(new HPDBaseTokenProtectionOptions
        {
            ActiveKey = new BaseOpaqueTokenKey { Id = id, Key = key, IssueNotBefore = DateTimeOffset.UnixEpoch },
            DecryptionKeys = retained,
        }));

    private static BaseBackupRequest BackupRequest() => new() { StoreId = "sqlite", Principal = Principal() };
    private static BaseBackupValidationRequest ValidationRequest() => new() { StoreId = "sqlite", Principal = Principal() };
    private static PrincipalContext Principal() => new() { AuthenticationState = PrincipalAuthenticationState.System };

    private static BaseRestoreRequest RestoreRequest(BaseBackupManifest manifest) => new()
    {
        StoreId = "sqlite",
        Principal = Principal(),
        ExpectedCurrentStoreIdentityDigest = manifest.StoreIdentityDigest,
        ExpectedArtifactStoreIdentityDigest = manifest.StoreIdentityDigest,
        IdentityMode = BaseRestoreIdentityMode.RequireCurrentStoreIdentity,
        RecoveryImageRetention = BaseRecoveryImageRetention.DeleteAfterSuccessfulRestore,
        ConfirmDestructiveReplacement = true,
    };

    private static byte[] RewriteAuthenticatedArtifact(
        byte[] artifact,
        BaseOpaqueTokenProtector protector,
        Func<BaseBackupManifest, BaseBackupManifest> rewriteManifest,
        Action<string>? rewritePayload = null)
    {
        int manifestLength = checked((int)BinaryPrimitives.ReadUInt32BigEndian(artifact.AsSpan(12, 4)));
        long payloadLength = checked((long)BinaryPrimitives.ReadUInt64BigEndian(artifact.AsSpan(16, 8)));
        BaseBackupManifest manifest = JsonSerializer.Deserialize(
            artifact.AsSpan(24, manifestLength),
            SqliteAdministrationJsonContext.Default.BaseBackupManifest)!;
        byte[] payload = artifact.AsSpan(24 + manifestLength, checked((int)payloadLength)).ToArray();
        if (rewritePayload is not null)
        {
            string temporary = Path.Combine(AdministrationTempDirectory(), $"hpd-base-artifact-rewrite-{Guid.NewGuid():N}.db");
            try
            {
                File.WriteAllBytes(temporary, payload);
                rewritePayload(temporary);
                payload = File.ReadAllBytes(temporary);
            }
            finally { Cleanup(temporary); }
        }

        byte[] digest = SHA256.HashData(payload);
        manifest = rewriteManifest(manifest) with
        {
            ProviderPayloadLength = payload.LongLength,
            ProviderPayloadSha256 = Convert.ToHexStringLower(digest),
        };
        byte[] manifestBytes = JsonSerializer.SerializeToUtf8Bytes(manifest, SqliteAdministrationJsonContext.Default.BaseBackupManifest);
        byte[] header = new byte[24];
        "HPDBAK01"u8.CopyTo(header);
        BinaryPrimitives.WriteUInt16BigEndian(header.AsSpan(8, 2), 1);
        header[10] = 1;
        header[11] = protector.ActiveKeyId;
        BinaryPrimitives.WriteUInt32BigEndian(header.AsSpan(12, 4), checked((uint)manifestBytes.Length));
        BinaryPrimitives.WriteUInt64BigEndian(header.AsSpan(16, 8), checked((ulong)payload.LongLength));
        byte[] authenticated = [.. header, .. manifestBytes, .. digest];
        byte[] tag = protector.Authenticate("hpd.base.backup.manifest.v1", protector.ActiveKeyId, authenticated);
        return [.. header, .. manifestBytes, .. payload, .. tag];
    }

    private static void ExecuteSql(string path, string sql)
    {
        using var connection = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={path};Pooling=False");
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }

    private static string AdministrationTempDirectory()
    {
        string path = Path.GetFullPath(Path.GetTempPath());
        return OperatingSystem.IsMacOS() && path.StartsWith("/var/", StringComparison.Ordinal)
            ? "/private" + path
            : path;
    }

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
        public void DeleteFile(string path) => File.Delete(path);
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
        public void DeleteFile(string path) => File.Delete(path);
    }

    private sealed class CallbackAdministrationOperations(string phase, Action callback) : ISqliteAdministrationOperationController
    {
        private int _invoked;

        public ValueTask BeforePhaseAsync(string currentPhase, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (string.Equals(currentPhase, phase, StringComparison.Ordinal)
                && Interlocked.Exchange(ref _invoked, 1) == 0)
                callback();
            return ValueTask.CompletedTask;
        }
        public void DeleteFile(string path) => File.Delete(path);
    }

    private sealed class DeletionFailureAdministrationOperations(string failure, string databasePath) : ISqliteAdministrationOperationController
    {
        public ValueTask BeforePhaseAsync(string phase, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (phase == "beforeRecoverySetDeletion" && failure is "recoveryWal" or "recoveryShm")
            {
                string extension = failure == "recoveryWal" ? "-wal" : "-shm";
                string recovery = Directory.GetFiles(Path.GetDirectoryName(databasePath)!, Path.GetFileName(databasePath) + ".recovery.*")
                    .Single(path => path.Contains(".recovery.", StringComparison.Ordinal)
                        && !path.EndsWith("-wal", StringComparison.Ordinal)
                        && !path.EndsWith("-shm", StringComparison.Ordinal));
                File.WriteAllBytes(recovery + extension, [0x01]);
            }
            return ValueTask.CompletedTask;
        }

        public void DeleteFile(string path)
        {
            bool fail = failure switch
            {
                "completedMarker" or "startupMarker" => path.EndsWith(".restore-state", StringComparison.Ordinal),
                "recoveryDatabase" => path.Contains(".recovery.", StringComparison.Ordinal)
                    && !path.EndsWith("-wal", StringComparison.Ordinal)
                    && !path.EndsWith("-shm", StringComparison.Ordinal),
                "recoveryWal" => path.EndsWith("-wal", StringComparison.Ordinal),
                "recoveryShm" => path.EndsWith("-shm", StringComparison.Ordinal),
                "startupStaging" => path.EndsWith(".restore-staging", StringComparison.Ordinal),
                _ => false,
            };
            if (fail) throw new IOException("Injected deletion failure.");
            File.Delete(path);
        }
    }

}
