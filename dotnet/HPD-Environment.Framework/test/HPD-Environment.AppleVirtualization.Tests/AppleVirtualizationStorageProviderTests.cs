namespace HPD.Environment.AppleVirtualization.Tests;

using FluentAssertions;
using HPD.Environment.AppleVirtualization.Protocol;
using HPD.Environment.AppleVirtualization.State;
using HPD.Environment.AppleVirtualization.Storage;
using HPD.Environment.Contracts;
using HPD.Environment.Runtime;
using Xunit;

public sealed class AppleVirtualizationStorageProviderTests
{
    [Fact]
    public async Task Pool_projects_guest_capacity_and_sparse_allocation_conditions()
    {
        var ledger = ReadyHostLedger();
        var helper = new FakeAppleVirtualizationHelperClient();
        helper.EnqueueResponse(StorageResponse(
            hostId: "host",
            action: AppleVirtualizationStorageAction.MeasurePool));
        var provider =
            new AppleVirtualizationStorageProvider(ledger, helper);

        StoragePoolStatus status = await provider.EnsureAsync(
            Metadata<StoragePool>("pool"),
            PoolSpec() with
            {
                WarningFreeBytes =
                    new ByteSize(long.MaxValue),
            },
            observed: null);

        status.PoolPhase.Should().Be(StoragePoolPhase.Warning);
        status.Conditions.Should().Contain(condition =>
            condition.Type ==
                "Environment.Storage.GuestFilesystemLow" &&
            condition.Status == ConditionStatus.True);
        status.Conditions.Should().Contain(condition =>
            condition.Type ==
                "Environment.Storage.SparseAllocationUnknown" &&
            condition.Status == ConditionStatus.True);
    }

    [Fact]
    public async Task Runtime_pool_measurement_is_class_bound_and_reports_engine_watermark()
    {
        var ledger = ReadyHostLedger();
        var helper = new FakeAppleVirtualizationHelperClient();
        helper.EnqueueResponse(StorageResponse(
            hostId: "host",
            action: AppleVirtualizationStorageAction.MeasurePool));
        var provider =
            new AppleVirtualizationStorageProvider(ledger, helper);

        StoragePoolStatus status = await provider.EnsureAsync(
            Metadata<StoragePool>("runtime-pool"),
            PoolSpec() with
            {
                StorageClass = StorageClass.RuntimeDisposable,
                WarningFreeBytes = new ByteSize(long.MaxValue),
            },
            observed: null);

        helper.Requests.Last().StorageRequest!.StorageClass
            .Should().Be(StorageClass.RuntimeDisposable);
        status.Conditions.Should().Contain(condition =>
            condition.Type ==
                "Environment.Storage.EngineDataRootLow");
        status.Conditions.Should().NotContain(condition =>
            condition.Type ==
                "Environment.Storage.GuestFilesystemLow");
    }

    [Fact]
    public async Task Volume_recovery_observes_without_recreating_and_pool_deletion_is_fenced()
    {
        var ledger = ReadyHostLedger();
        var helper = new FakeAppleVirtualizationHelperClient();
        var provider =
            new AppleVirtualizationStorageProvider(ledger, helper);
        ResourceRef<StoragePool> pool = Ref<StoragePool>("pool");
        ResourceMetadata<DurableVolume> volumeMetadata =
            Metadata<DurableVolume>("volume");
        ResourceRef<DurableVolume> volume =
            Ref<DurableVolume>("volume");

        await provider.EnsureAsync(
            Metadata<StoragePool>("pool"),
            PoolSpec(),
            observed: null);
        DurableVolumeStatus created = await provider.EnsureAsync(
            volumeMetadata,
            VolumeSpec(pool),
            observed: null);

        var reconstructed =
            new AppleVirtualizationStorageProvider(ledger, helper);
        await reconstructed.RecoverAsync(
            Metadata<StoragePool>("pool"),
            PoolSpec(),
            await provider.GetStatusAsync(pool));
        DurableVolumeStatus recovered =
            await reconstructed.RecoverAsync(
                volumeMetadata,
                VolumeSpec(pool),
                created);

        recovered.VolumeGeneration.Should()
            .Be(created.VolumeGeneration);
        helper.Requests
            .Where(request =>
                request.Operation ==
                AppleVirtualizationHelperOperation.Storage)
            .Select(request => request.StorageRequest!.Action)
            .TakeLast(2)
            .Should().Equal(
                AppleVirtualizationStorageAction.MeasurePool,
                AppleVirtualizationStorageAction.ObserveVolume);
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => reconstructed.DeleteAsync(pool).AsTask());

        await reconstructed.EraseAsync(volume);
        await reconstructed.DeleteAsync(pool);
    }

    [Fact]
    public async Task Storage_response_identity_is_validated_at_the_provider_boundary()
    {
        var ledger = ReadyHostLedger();
        var helper = new FakeAppleVirtualizationHelperClient();
        helper.EnqueueResponse(StorageResponse(
            hostId: "different-host",
            action: AppleVirtualizationStorageAction.MeasurePool));
        var provider =
            new AppleVirtualizationStorageProvider(ledger, helper);

        InvalidOperationException error =
            await Assert.ThrowsAsync<InvalidOperationException>(
                () => provider.EnsureAsync(
                        Metadata<StoragePool>("pool"),
                        PoolSpec(),
                        observed: null)
                    .AsTask());

        error.Message.Should().Contain(
            "StorageResponseIdentityMismatch");
    }

    [Fact]
    public async Task Storage_requires_exactly_one_ready_runtime_host()
    {
        var noHostProvider =
            new AppleVirtualizationStorageProvider(
                new AppleVirtualizationProviderStateLedger(),
                new FakeAppleVirtualizationHelperClient());

        InvalidOperationException unavailable =
            await Assert.ThrowsAsync<InvalidOperationException>(
                () => noHostProvider.EnsureAsync(
                        Metadata<StoragePool>("pool"),
                        PoolSpec(),
                        observed: null)
                    .AsTask());
        unavailable.Message.Should().Contain(
            "StorageHostUnavailable");

        var multiple = ReadyHostLedger("host-a", "host-b");
        var ambiguousProvider =
            new AppleVirtualizationStorageProvider(
                multiple,
                new FakeAppleVirtualizationHelperClient());
        InvalidOperationException ambiguous =
            await Assert.ThrowsAsync<InvalidOperationException>(
                () => ambiguousProvider.EnsureAsync(
                        Metadata<StoragePool>("pool"),
                        PoolSpec(),
                        observed: null)
                    .AsTask());
        ambiguous.Message.Should().Contain(
            "StorageHostAmbiguous");
    }

    [Fact]
    public async Task Reservations_are_accounted_and_duplicate_identity_is_rejected()
    {
        var ledger = ReadyHostLedger();
        var helper = new FakeAppleVirtualizationHelperClient();
        var provider =
            new AppleVirtualizationStorageProvider(ledger, helper);
        ResourceRef<StoragePool> pool = Ref<StoragePool>("pool");
        await provider.EnsureAsync(
            Metadata<StoragePool>("pool"),
            PoolSpec(),
            observed: null);
        ResourceMetadata<StorageReservation> metadata =
            Metadata<StorageReservation>("reservation");
        StorageReservationSpec spec = new()
        {
            Pool = pool,
            OperationId = "pull-1",
            Owner = "io.penpot.penpot",
            RequestedBytes = new ByteSize(4096),
            ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(5),
        };

        await provider.ReserveAsync(metadata, spec, observed: null);
        InvalidOperationException duplicate =
            await Assert.ThrowsAsync<InvalidOperationException>(
                () => provider.ReserveAsync(
                        metadata,
                        spec,
                        observed: null)
                    .AsTask());

        duplicate.Message.Should().Contain(
            "StorageReservationDuplicate");
        StoragePoolStatus status =
            await provider.GetStatusAsync(pool);
        status.ReservedBytes.Should().Be(new ByteSize(4096));
    }

    [Fact]
    public async Task Volume_observation_fails_closed_for_identity_replacement_and_quota_overrun()
    {
        var ledger = ReadyHostLedger();
        var helper = new FakeAppleVirtualizationHelperClient();
        var provider =
            new AppleVirtualizationStorageProvider(ledger, helper);
        ResourceRef<StoragePool> pool = Ref<StoragePool>("pool");
        ResourceRef<DurableVolume> volume =
            Ref<DurableVolume>("volume");
        await provider.EnsureAsync(
            Metadata<StoragePool>("pool"),
            PoolSpec(),
            observed: null);
        DurableVolumeStatus created = await provider.EnsureAsync(
            Metadata<DurableVolume>("volume"),
            VolumeSpec(pool),
            observed: null);

        helper.EnqueueResponse(
            StorageResponse(
                hostId: "host",
                action:
                    AppleVirtualizationStorageAction.ObserveVolume,
                logicalId: "penpot-data",
                filesystemIdentity:
                    "guest-app-data:replacement:project:7",
                usedBytes: new ByteSize(1)));
        DurableVolumeStatus replaced =
            await provider.GetStatusAsync(volume);
        replaced.Phase.Should().Be(ResourcePhase.Degraded);
        replaced.VolumePhase.Should()
            .Be(DurableVolumePhase.FailedRetained);
        replaced.Integrity.Should()
            .Be(VolumeIntegrityState.CheckRequired);
        replaced.Diagnostics.Should().ContainSingle(
            diagnostic =>
                diagnostic.Code.Value ==
                "Environment.Storage.IntegrityCheckRequired");

        helper.EnqueueResponse(
            StorageResponse(
                hostId: "host",
                action:
                    AppleVirtualizationStorageAction.ObserveVolume,
                logicalId: "penpot-data",
                filesystemIdentity:
                    created.FilesystemIdentity,
                usedBytes: new ByteSize(1024 * 1024 + 1)));
        DurableVolumeStatus overQuota =
            await provider.GetStatusAsync(volume);
        overQuota.Phase.Should().Be(ResourcePhase.Degraded);
        overQuota.Diagnostics.Should().ContainSingle(
            diagnostic =>
                diagnostic.Code.Value ==
                "Environment.Storage.AppVolumeLow");
    }

    [Fact]
    public async Task Backup_streams_guest_payload_into_authenticated_host_artifact()
    {
        string root = Path.Combine(
            Path.GetTempPath(),
            "apple-storage-stream-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            byte[] keyBytes = Enumerable.Range(0, 32)
                .Select(static value => (byte)value)
                .ToArray();
            var keys = new TestBackupKeyProvider(keyBytes);
            var helper = new FakeAppleVirtualizationHelperClient();
            var provider = new AppleVirtualizationStorageProvider(
                ReadyHostLedger(),
                helper,
                new AppleVirtualizationProviderOptions
                {
                    StateRoot = root,
                    BackupKeyProvider = keys,
                });
            ResourceRef<StoragePool> pool = Ref<StoragePool>("pool");
            await provider.EnsureAsync(
                Metadata<StoragePool>("pool"),
                PoolSpec(),
                observed: null);
            await provider.EnsureAsync(
                Metadata<DurableVolume>("volume"),
                VolumeSpec(pool),
                observed: null);
            ResourceRef<StorageReservation> reservation =
                Ref<StorageReservation>("reservation");
            await provider.ReserveAsync(
                Metadata<StorageReservation>("reservation"),
                new StorageReservationSpec
                {
                    Pool = pool,
                    OperationId = "backup-a",
                    Owner = "installation",
                    RequestedBytes = new ByteSize(1024 * 1024),
                    ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(5),
                },
                observed: null);

            string source = Path.Combine(root, "source");
            Directory.CreateDirectory(source);
            File.WriteAllText(Path.Combine(source, "data.txt"), "guest-data");
            string seedArtifact = Path.Combine(root, "seed.hpdbackup");
            using (var key = new StorageBackupKeyMaterial("test-key", keyBytes))
            {
                _ = PortableVolumeBackupArchive.Capture(
                    source,
                    seedArtifact,
                    Manifest("backup"),
                    key,
                    1024 * 1024);
            }
            var payload = new MemoryStream();
            PortableVolumeBackupManifest seed;
            using (var key = new StorageBackupKeyMaterial("test-key", keyBytes))
            {
                seed = await PortableVolumeBackupArchive.StreamValidatedPayloadAsync(
                    seedArtifact,
                    key,
                    1024 * 1024,
                    (chunk, _) =>
                    {
                        payload.Write(chunk.Span);
                        return ValueTask.CompletedTask;
                    });
            }
            helper.EnqueueResponse(TransferResponse(
                AppleVirtualizationStorageAction.BeginBackup,
                "backup",
                seed,
                payload.Length));
            helper.EnqueueResponse(TransferResponse(
                AppleVirtualizationStorageAction.ReadBackupChunk,
                "backup",
                seed,
                payload.Length,
                chunk: payload.ToArray(),
                offset: 0));
            helper.EnqueueResponse(TransferResponse(
                AppleVirtualizationStorageAction.EndBackup,
                "backup",
                seed,
                payload.Length,
                completed: true));

            var backupSpec = new VolumeBackupSpec
            {
                BackupSetId = "backup-set",
                Volume = Ref<DurableVolume>("volume"),
                SourceVolumeResource = Ref<DurableVolume>("volume"),
                SourceVolumeSpec = VolumeSpec(pool),
                SourceVolumeGeneration = new(1),
                SourceProviderId = AppleVirtualizationProviderDescriptor.ProviderId,
                SourceProviderGeneration = 1,
                SourceProviderRealizationGeneration = 1,
                OwnerTypeId = "io.penpot.penpot",
                OwnerScopeId = "installation",
                OwnerVersion = "revision",
                CompatibilityDomain = "penpot-v1",
                Consistency = VolumeBackupConsistency.Stopped,
                Reservation = reservation,
                EncryptionCredential = new CredentialRef("test-key"),
            };
            VolumeBackupStatus captured = await provider.CaptureAsync(
                Metadata<VolumeBackup>("backup"),
                backupSpec,
                observed: null);

            captured.BackupPhase.Should().Be(VolumeBackupPhase.Ready);
            captured.ContentDigest!.Value.Value.Should().Be(seed.ContentSha256);
            string artifact = Path.Combine(root, "backups", "backup.hpdbackup");
            File.Exists(artifact).Should().BeTrue();
            using var validateKey =
                new StorageBackupKeyMaterial("test-key", keyBytes);
            PortableVolumeBackupArchive.Validate(
                    artifact,
                    validateKey,
                    1024 * 1024)
                .ContentSha256.Should().Be(seed.ContentSha256);
            await using (var exported = new MemoryStream())
            {
                await provider.ExportAsync(
                    Ref<VolumeBackup>("backup"),
                    exported);
                exported.Position = 0;
                VolumeBackupStatus imported = await provider.ImportAsync(
                    Metadata<VolumeBackup>("imported-backup"),
                    backupSpec,
                    captured,
                    exported);
                imported.ContentDigest.Should().Be(captured.ContentDigest);
                exported.Position.Should().Be(exported.Length);
            }
            helper.Requests
                .Where(static request =>
                    request.StorageRequest?.OperationId == "backup")
                .Select(static request => request.StorageRequest!.Action)
                .Should().Equal(
                    AppleVirtualizationStorageAction.BeginBackup,
                    AppleVirtualizationStorageAction.ReadBackupChunk,
                    AppleVirtualizationStorageAction.EndBackup);

            helper.EnqueueResponse(TransferResponse(
                AppleVirtualizationStorageAction.BeginRestore,
                "restore",
                seed,
                payload.Length));
            helper.EnqueueResponse(TransferResponse(
                AppleVirtualizationStorageAction.WriteRestoreChunk,
                "restore",
                seed,
                payload.Length));
            helper.EnqueueResponse(TransferResponse(
                AppleVirtualizationStorageAction.WriteRestoreChunk,
                "restore",
                seed,
                payload.Length));
            helper.EnqueueResponse(TransferResponse(
                AppleVirtualizationStorageAction.CommitRestore,
                "restore",
                seed,
                payload.Length,
                completed: true));
            VolumeRestoreStatus restored = await provider.RestoreAsync(
                Metadata<VolumeRestore>("restore"),
                new VolumeRestoreSpec
                {
                    Backup = Ref<VolumeBackup>("backup"),
                    TargetVolume = Ref<DurableVolume>("volume"),
                    Reservation = reservation,
                    ExpectedCompatibilityDomain = "penpot-v1",
                },
                observed: null);
            restored.RestorePhase.Should().Be(VolumeRestorePhase.Ready);
            restored.PreviousVolumeGeneration.Should()
                .Be(new ResourceGeneration(1));
            restored.RestoredVolumeGeneration.Should()
                .Be(new ResourceGeneration(2));
            restored.VerifiedDigest!.Value.Value.Should()
                .Be(seed.ContentSha256);
            helper.EnqueueResponse(TransferResponse(
                AppleVirtualizationStorageAction.AbortRestore,
                "restore",
                seed,
                payload.Length,
                completed: true));
            await provider.FinalizeAsync(Ref<VolumeRestore>("restore"));
            helper.Requests
                .Where(static request =>
                    request.StorageRequest?.OperationId == "restore")
                .Select(static request => request.StorageRequest!.Action)
                .Should().Equal(
                    AppleVirtualizationStorageAction.BeginRestore,
                    AppleVirtualizationStorageAction.WriteRestoreChunk,
                    AppleVirtualizationStorageAction.WriteRestoreChunk,
                    AppleVirtualizationStorageAction.CommitRestore,
                    AppleVirtualizationStorageAction.AbortRestore);
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task Recovery_proves_selected_restore_generation_after_backend_reconstruction()
    {
        string root = Path.Combine(
            Path.GetTempPath(),
            "apple-storage-recovery-" + Guid.NewGuid().ToString("N"));
        try
        {
            byte[] keyBytes = Enumerable.Range(0, 32)
                .Select(static value => (byte)value)
                .ToArray();
            var keys = new TestBackupKeyProvider(keyBytes);
            var initialHelper = new FakeAppleVirtualizationHelperClient();
            var initial = new AppleVirtualizationStorageProvider(
                ReadyHostLedger(),
                initialHelper,
                new AppleVirtualizationProviderOptions
                {
                    StateRoot = root,
                    BackupKeyProvider = keys,
                });
            ResourceMetadata<StoragePool> poolMetadata =
                Metadata<StoragePool>("pool");
            ResourceRef<StoragePool> pool = Ref<StoragePool>("pool");
            ResourceMetadata<DurableVolume> volumeMetadata =
                Metadata<DurableVolume>("volume");
            ResourceRef<DurableVolume> volume =
                Ref<DurableVolume>("volume");
            StoragePoolStatus poolStatus =
                await initial.EnsureAsync(
                    poolMetadata,
                    PoolSpec(),
                    observed: null);
            DurableVolumeStatus volumeStatus =
                await initial.EnsureAsync(
                    volumeMetadata,
                    VolumeSpec(pool),
                    observed: null);
            ResourceMetadata<StorageReservation> reservationMetadata =
                Metadata<StorageReservation>("reservation");
            ResourceRef<StorageReservation> reservation =
                Ref<StorageReservation>("reservation");
            StorageReservationSpec reservationSpec = new()
            {
                Pool = pool,
                OperationId = "restore-crash",
                Owner = "installation",
                RequestedBytes = new ByteSize(1024 * 1024),
                ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(5),
            };
            StorageReservationStatus reservationStatus =
                await initial.ReserveAsync(
                    reservationMetadata,
                    reservationSpec,
                    observed: null);
            ResourceMetadata<VolumeBackup> backupMetadata =
                Metadata<VolumeBackup>("backup");
            ResourceRef<VolumeBackup> backup = Ref<VolumeBackup>("backup");
            VolumeBackupSpec backupSpec = new()
            {
                BackupSetId = "backup-set",
                Volume = volume,
                SourceVolumeResource = volume,
                SourceVolumeSpec = VolumeSpec(pool),
                SourceVolumeGeneration = new(1),
                SourceProviderId = AppleVirtualizationProviderDescriptor.ProviderId,
                SourceProviderGeneration = 1,
                SourceProviderRealizationGeneration = 1,
                OwnerTypeId = "io.penpot.penpot",
                OwnerScopeId = "installation",
                OwnerVersion = "revision",
                CompatibilityDomain = "penpot-v1",
                Consistency = VolumeBackupConsistency.Stopped,
                Reservation = reservation,
                EncryptionCredential = new CredentialRef("test-key"),
            };
            string source = Path.Combine(root, "source");
            Directory.CreateDirectory(source);
            await File.WriteAllTextAsync(
                Path.Combine(source, "value.txt"),
                "selected");
            PortableVolumeBackupManifest manifest;
            string artifact = Path.Combine(
                root,
                "backups",
                "backup.hpdbackup");
            using (var key =
                   new StorageBackupKeyMaterial("test-key", keyBytes))
            {
                manifest = PortableVolumeBackupArchive.Capture(
                    source,
                    artifact,
                    Manifest("backup"),
                    key,
                    1024 * 1024);
            }
            VolumeBackupStatus backupStatus =
                await initial.RecoverAsync(
                    backupMetadata,
                    backupSpec,
                    new VolumeBackupStatus
                    {
                        Phase = ResourcePhase.Ready,
                        BackupPhase = VolumeBackupPhase.Ready,
                        ObservedGeneration = backupMetadata.Generation,
                        ContentDigest = new Digest(
                            "sha256",
                            manifest.ContentSha256),
                        LogicalBytes =
                            new ByteSize(manifest.LogicalBytes),
                        StoredBytes =
                            new ByteSize(new FileInfo(artifact).Length),
                    });

            var helper = new FakeAppleVirtualizationHelperClient();
            var reconstructed =
                new AppleVirtualizationStorageProvider(
                    ReadyHostLedger(),
                    helper,
                    new AppleVirtualizationProviderOptions
                    {
                        StateRoot = root,
                        BackupKeyProvider = keys,
                    });
            _ = await reconstructed.RecoverAsync(
                poolMetadata,
                PoolSpec(),
                poolStatus);
            helper.EnqueueResponse(StorageResponse(
                hostId: "host",
                action: AppleVirtualizationStorageAction.ObserveVolume,
                logicalId: "penpot-data",
                filesystemIdentity: volumeStatus.FilesystemIdentity,
                usedBytes: new ByteSize(manifest.LogicalBytes),
                volumeGeneration: 2));
            DurableVolumeStatus recoveredVolume =
                await reconstructed.RecoverAsync(
                    volumeMetadata,
                    VolumeSpec(pool),
                    volumeStatus);
            _ = await reconstructed.RecoverAsync(
                reservationMetadata,
                reservationSpec,
                reservationStatus);
            _ = await reconstructed.RecoverAsync(
                backupMetadata,
                backupSpec,
                backupStatus);
            ResourceMetadata<VolumeRestore> restoreMetadata =
                Metadata<VolumeRestore>("restore-crash");
            VolumeRestoreSpec restoreSpec = new()
            {
                Backup = backup,
                TargetVolume = volume,
                Reservation = reservation,
                ExpectedCompatibilityDomain = "penpot-v1",
            };
            helper.EnqueueResponse(TransferResponse(
                AppleVirtualizationStorageAction.CommitRestore,
                "restore-crash",
                manifest,
                encodedBytes: 0,
                completed: true));
            VolumeRestoreStatus restored =
                await reconstructed.RecoverAsync(
                    restoreMetadata,
                    restoreSpec,
                    new VolumeRestoreStatus
                    {
                        Phase = ResourcePhase.Pending,
                        RestorePhase = VolumeRestorePhase.Pending,
                        ObservedGeneration =
                            restoreMetadata.Generation,
                        PreviousVolumeGeneration =
                            new ResourceGeneration(1),
                    });

            recoveredVolume.VolumeGeneration.Should()
                .Be(new ResourceGeneration(2));
            recoveredVolume.Realization!.Generation.Should()
                .Be(new ResourceGeneration(2));
            restored.RestorePhase.Should()
                .Be(VolumeRestorePhase.Ready);
            restored.RestoredVolumeGeneration.Should()
                .Be(new ResourceGeneration(2));
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }

    private static PortableVolumeBackupManifest Manifest(string backupId) =>
        new()
        {
            BackupId = backupId,
            OwnerTypeId = "io.penpot.penpot",
            OwnerScopeId = "installation",
            OwnerVersion = "revision",
            CompatibilityDomain = "penpot-v1",
            LogicalVolumeId = "penpot-data",
            VolumeGeneration = 1,
            ProviderId = "hpd.execution.apple-virtualization",
            Consistency = VolumeBackupConsistency.Stopped,
            CreatedAt = DateTimeOffset.UtcNow,
            LogicalBytes = 0,
            EntryCount = 0,
            ContentSha256 = "pending",
            EncryptionKeyId = "pending",
        };

    private static AppleVirtualizationHelperEnvelope TransferResponse(
        AppleVirtualizationStorageAction action,
        string operationId,
        PortableVolumeBackupManifest manifest,
        long encodedBytes,
        byte[]? chunk = null,
        long? offset = null,
        bool completed = false)
    {
        AppleVirtualizationHelperEnvelope request =
            AppleVirtualizationHelperEnvelope.Request(
                AppleVirtualizationHelperOperation.Storage,
                "transfer",
                1,
                AppleVirtualizationHelperProtocol.StorageRequestSchema);
        return request.ToResponse(2) with
        {
            PayloadSchema = AppleVirtualizationHelperProtocol.StorageResponseSchema,
            StorageResponse = new AppleVirtualizationStorageResponse
            {
                HostId = "host",
                ProviderGeneration = 1,
                HostStartGeneration = 3,
                Action = action,
                LogicalVolumeId = "penpot-data",
                OperationId = operationId,
                Offset = offset,
                ChunkBase64 = chunk is null
                    ? null
                    : Convert.ToBase64String(chunk),
                Completed = completed,
                EncodedPayloadBytes = encodedBytes,
                LogicalBytes = manifest.LogicalBytes,
                EntryCount = manifest.EntryCount,
                ContentSha256 = manifest.ContentSha256,
                VolumeGeneration =
                    action == AppleVirtualizationStorageAction.CommitRestore
                        ? 2UL
                        : 1UL,
                Exists = true,
            },
        };
    }

    private sealed class TestBackupKeyProvider(byte[] key) :
        IStorageBackupKeyProvider
    {
        public ValueTask<StorageBackupKeyMaterial> ResolveAsync(
            CredentialRef credential,
            ResourceScope scope,
            string purpose,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            _ = credential;
            _ = scope;
            _ = purpose;
            return ValueTask.FromResult(
                new StorageBackupKeyMaterial("test-key", key));
        }
    }

    private static AppleVirtualizationProviderStateLedger
        ReadyHostLedger(params string[] hostIds)
    {
        if (hostIds.Length == 0)
            hostIds = ["host"];
        var ledger = new AppleVirtualizationProviderStateLedger();
        foreach (string hostId in hostIds)
        {
            ResourceMetadata<RuntimeHost> metadata =
                Metadata<RuntimeHost>(hostId);
            ledger.UpsertRuntimeHost(
                metadata,
                new RuntimeHostStatus
                {
                    Phase = ResourcePhase.Ready,
                    HostPhase = RuntimeHostPhase.Ready,
                    ObservedGeneration = metadata.Generation,
                    Generations = new RuntimeHostGenerationStatus
                    {
                        HostStartGeneration =
                            new RuntimeHostStartGeneration(3),
                    },
                    GuestControl = new GuestControlStatus(
                        Expected: true,
                        Installed: true,
                        Reachable: true,
                        Transport: ProviderTransportKind.Vsock),
                    Readiness =
                        new RuntimeHostReadinessStatus(Ready: true),
                });
        }
        return ledger;
    }

    private static StoragePoolSpec PoolSpec() => new()
    {
        StorageClass = StorageClass.AppDurable,
        MinimumFreeBytes = new ByteSize(1),
        WarningFreeBytes = new ByteSize(2),
        EmergencyFreeBytes = new ByteSize(0),
    };

    private static DurableVolumeSpec VolumeSpec(
        ResourceRef<StoragePool> pool) =>
        new()
        {
            LogicalId = "penpot-data",
            OwnerScopeId = "installation",
            OwnerResourceId = "backend",
            DeclarationId = "data",
            Pool = pool,
            MinimumBytes = new ByteSize(1),
            MaximumBytes = new ByteSize(1024 * 1024),
            BackupEligible = true,
            CompatibilityDomain = "penpot-v1",
        };

    private static AppleVirtualizationHelperEnvelope StorageResponse(
        string hostId,
        AppleVirtualizationStorageAction action,
        string? logicalId = null,
        string? filesystemIdentity = null,
        ByteSize? usedBytes = null,
        ulong? volumeGeneration = null)
    {
        AppleVirtualizationHelperEnvelope request =
            AppleVirtualizationHelperEnvelope.Request(
                AppleVirtualizationHelperOperation.Storage,
                "test",
                1,
                AppleVirtualizationHelperProtocol.StorageRequestSchema);
        return request.ToResponse(2) with
        {
            PayloadSchema =
                AppleVirtualizationHelperProtocol.StorageResponseSchema,
            StorageResponse = new AppleVirtualizationStorageResponse
            {
                HostId = hostId,
                ProviderGeneration = 1,
                HostStartGeneration = 3,
                Action = action,
                LogicalVolumeId = logicalId,
                Exists = logicalId is not null,
                EffectiveRuntimePath = logicalId is null
                    ? null
                    : "/var/lib/hpdos/app-data/volumes/" +
                        logicalId,
                FilesystemIdentity = filesystemIdentity,
                VolumeGeneration = logicalId is null
                    ? null
                    : volumeGeneration ?? 1UL,
                LogicalCapacityBytes =
                    new ByteSize(32L * 1024 * 1024 * 1024),
                UsedBytes = usedBytes,
                AvailableBytes =
                    new ByteSize(24L * 1024 * 1024 * 1024),
            },
        };
    }

    private static ResourceMetadata<T> Metadata<T>(string id)
        where T : IExecutionResourceMarker =>
        new()
        {
            Id = new ResourceId<T>(id),
            Kind = new ResourceKind(typeof(T).Name),
            Scope = new ResourceScope("apple-storage-tests"),
            Generation = new ResourceGeneration(1),
            SchemaVersion = new SchemaVersion("1"),
        };

    private static ResourceRef<T> Ref<T>(string id)
        where T : IExecutionResourceMarker =>
        new(
            new ResourceId<T>(id),
            new ResourceScope("apple-storage-tests"),
            new ResourceGeneration(1));
}
