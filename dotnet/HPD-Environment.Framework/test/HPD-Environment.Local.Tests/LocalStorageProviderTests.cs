using HPD.Environment.Contracts;
using HPD.Environment.Runtime;

namespace HPD.Environment.Local.Tests;

public sealed class LocalStorageProviderTests
{
    [Fact]
    public async Task Runtime_pool_fails_closed_without_authoritative_engine_root()
    {
        string root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        try
        {
            IStoragePoolProvider pools =
                CreateRegistry(root).StoragePoolProviders.Single();
            StoragePoolStatus status = await pools.EnsureAsync(
                Metadata<StoragePool>("runtime-pool"),
                PoolSpec() with
                {
                    StorageClass = StorageClass.RuntimeDisposable,
                },
                null);

            Assert.Equal(StoragePoolPhase.AdmissionStopped, status.PoolPhase);
            Assert.Null(status.AvailableBytes);
            Assert.Equal(
                StorageMeasurementConfidence.Unknown,
                status.MeasurementConfidence);
            Assert.Contains(status.Conditions, static condition =>
                condition.Type ==
                    "Environment.Storage.EngineDataRootUnknown");
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task Runtime_pool_uses_explicit_direct_host_engine_root()
    {
        string root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        string engineRoot = Path.Combine(root, "engine-data");
        try
        {
            Directory.CreateDirectory(engineRoot);
            var registry = new EnvironmentProviderRegistry();
            registry.RegisterModule(new LocalEnvironmentProviderModule(
                new LocalEnvironmentProviderOptions
                {
                    WorkloadStateRoot = Path.Combine(root, "state"),
                    StorageRoot = Path.Combine(root, "storage"),
                    EngineDataRootPath = engineRoot,
                    EngineSocketPath = "/test/docker.sock",
                    DurableVolumeBackend =
                        LocalDurableVolumeBackendKind.TestDirectory,
                }));
            StoragePoolStatus status = await registry.StoragePoolProviders
                .Single().EnsureAsync(
                    Metadata<StoragePool>("runtime-pool"),
                    PoolSpec() with
                    {
                        StorageClass = StorageClass.RuntimeDisposable,
                    },
                    null);

            Assert.NotNull(status.AvailableBytes);
            Assert.DoesNotContain(status.Conditions, static condition =>
                condition.Type ==
                    "Environment.Storage.EngineDataRootUnknown");
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task Reservation_accounting_is_scoped_to_its_exact_pool()
    {
        string root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        string engineRoot = Path.Combine(root, "engine-data");
        try
        {
            Directory.CreateDirectory(engineRoot);
            var registry = new EnvironmentProviderRegistry();
            registry.RegisterModule(new LocalEnvironmentProviderModule(
                new LocalEnvironmentProviderOptions
                {
                    WorkloadStateRoot = Path.Combine(root, "state"),
                    StorageRoot = Path.Combine(root, "storage"),
                    EngineDataRootPath = engineRoot,
                    EngineSocketPath = "/test/docker.sock",
                    DurableVolumeBackend =
                        LocalDurableVolumeBackendKind.TestDirectory,
                }));
            IStoragePoolProvider pools =
                registry.StoragePoolProviders.Single();
            IStorageReservationProvider reservations =
                registry.StorageReservationProviders.Single();
            ResourceRef<StoragePool> durablePool =
                Ref<StoragePool>("durable-pool");
            ResourceRef<StoragePool> runtimePool =
                Ref<StoragePool>("runtime-pool");
            await pools.EnsureAsync(
                Metadata<StoragePool>("durable-pool"),
                PoolSpec(),
                observed: null);
            await pools.EnsureAsync(
                Metadata<StoragePool>("runtime-pool"),
                PoolSpec() with
                {
                    StorageClass = StorageClass.RuntimeDisposable,
                },
                observed: null);

            await reservations.ReserveAsync(
                Metadata<StorageReservation>("runtime-reservation"),
                new StorageReservationSpec
                {
                    Pool = runtimePool,
                    OperationId = "pull-images",
                    Owner = "io.penpot.penpot",
                    RequestedBytes = new ByteSize(4096),
                    ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(5),
                },
                observed: null);

            Assert.Equal(
                new ByteSize(4096),
                (await pools.GetStatusAsync(runtimePool)).ReservedBytes);
            Assert.Equal(
                new ByteSize(0),
                (await pools.GetStatusAsync(durablePool)).ReservedBytes);
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task Pool_projects_host_capacity_watermark_condition()
    {
        string root = Path.Combine(
            Path.GetTempPath(),
            "hpd-local-storage-condition-tests",
            Guid.NewGuid().ToString("N"));
        try
        {
            EnvironmentProviderRegistry registry =
                CreateRegistry(root);
            IStoragePoolProvider pools =
                registry.StoragePoolProviders.Single();

            StoragePoolStatus status = await pools.EnsureAsync(
                Metadata<StoragePool>("pool"),
                PoolSpec() with
                {
                    WarningFreeBytes =
                        new ByteSize(long.MaxValue),
                },
                observed: null);

            Assert.Equal(StoragePoolPhase.Warning, status.PoolPhase);
            Assert.Contains(
                status.Conditions,
                static condition =>
                    condition.Type ==
                    "Environment.Storage.HostCapacityLow" &&
                    condition.Status == ConditionStatus.True);
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task Mac_platform_backend_enforces_the_volume_ceiling()
    {
        if (!OperatingSystem.IsMacOS())
            return;

        string root = Path.Combine(
            Path.GetTempPath(),
            "hpd-local-storage-quota-tests",
            Guid.NewGuid().ToString("N"));
        ResourceRef<DurableVolume>? volume = null;
        IDurableVolumeProvider? volumes = null;
        IDurableVolumeProvider? cleanupVolumes = null;
        bool erased = false;
        try
        {
            var registry = new EnvironmentProviderRegistry();
            registry.RegisterModule(
                new LocalEnvironmentProviderModule(
                    new LocalEnvironmentProviderOptions
                    {
                        StorageRoot = root,
                        EngineSocketPath =
                            "/test/docker.sock",
                        DurableVolumeBackend =
                            LocalDurableVolumeBackendKind
                                .PlatformHardQuota,
                    }));
            IStoragePoolProvider pools =
                registry.StoragePoolProviders.Single();
            volumes =
                registry.DurableVolumeProviders.Single();
            ResourceRef<StoragePool> pool =
                Ref<StoragePool>("pool");
            await pools.EnsureAsync(
                Metadata<StoragePool>("pool"),
                PoolSpec(),
                null);
            ResourceRef<DurableVolume> requestedVolume =
                Ref<DurableVolume>("volume");
            DurableVolumeStatus status =
                await volumes.EnsureAsync(
                    Metadata<DurableVolume>("volume"),
                    VolumeSpec(pool) with
                    {
                        MaximumBytes =
                            new ByteSize(32L * 1024 * 1024),
                    },
                    null);
            volume = requestedVolume;
            string path =
                status.Realization!.EffectiveRuntimePath;
            Assert.True(Directory.Exists(path));

            long declaredMaximum = 32L * 1024 * 1024;
            var drive = new DriveInfo(path);
            Assert.InRange(
                drive.TotalSize,
                1,
                declaredMaximum);
            Assert.NotNull(status.PhysicalAllocatedBytes);
            Assert.InRange(
                status.PhysicalAllocatedBytes!.Value.Value,
                1,
                declaredMaximum);

            var competingRegistry =
                new EnvironmentProviderRegistry();
            competingRegistry.RegisterModule(
                new LocalEnvironmentProviderModule(
                    new LocalEnvironmentProviderOptions
                    {
                        StorageRoot = root,
                        EngineSocketPath =
                            "/test/docker.sock",
                        DurableVolumeBackend =
                            LocalDurableVolumeBackendKind
                                .PlatformHardQuota,
                    }));
            IStoragePoolProvider competingPools =
                competingRegistry.StoragePoolProviders.Single();
            IDurableVolumeProvider competingVolumes =
                competingRegistry.DurableVolumeProviders.Single();
            await competingPools.EnsureAsync(
                Metadata<StoragePool>("pool"),
                PoolSpec(),
                null);
            InvalidOperationException ownership =
                await Assert.ThrowsAsync<InvalidOperationException>(
                    () => competingVolumes.EnsureAsync(
                            Metadata<DurableVolume>("volume"),
                            VolumeSpec(pool) with
                            {
                                MaximumBytes =
                                    new ByteSize(
                                        declaredMaximum),
                            },
                            status)
                        .AsTask());
            Assert.Contains(
                "owns the durable volume",
                ownership.Message,
                StringComparison.Ordinal);

            IRuntimeHostProvider hosts =
                registry.RuntimeHostProviders.Single();
            RuntimeHostStatus host = await hosts.EnsureAsync(
                Metadata<RuntimeHost>("host"),
                new RuntimeHostSpec
                {
                    Platform =
                        LocalEnvironmentProviderDescriptor
                            .CurrentPlatform(),
                },
                null);
            await hosts.StopAsync(
                host.Handle!.Value,
                StopPolicy.Default);

            DurableVolumeStatus reopened =
                await competingVolumes.EnsureAsync(
                    Metadata<DurableVolume>("volume"),
                    VolumeSpec(pool) with
                    {
                        MaximumBytes =
                            new ByteSize(declaredMaximum),
                    },
                    status);
            Assert.Equal(
                status.FilesystemIdentity,
                reopened.FilesystemIdentity);
            cleanupVolumes = competingVolumes;
        }
        finally
        {
            if (volume is not null &&
                (cleanupVolumes ?? volumes) is { } owner)
            {
                await owner.EraseAsync(volume.Value);
                erased = true;
            }
            if (erased && Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task Durable_data_survives_detach_and_backup_restore_is_staged()
    {
        string root = Path.Combine(
            Path.GetTempPath(),
            "hpd-local-storage-tests",
            Guid.NewGuid().ToString("N"));
        try
        {
            var registry = new EnvironmentProviderRegistry();
            registry.RegisterModule(new LocalEnvironmentProviderModule(
                new LocalEnvironmentProviderOptions
                {
                    WorkloadStateRoot = Path.Combine(root, "state"),
                    StorageRoot = Path.Combine(root, "storage"),
                    EngineSocketPath = "/test/docker.sock",
                    DurableVolumeBackend =
                        LocalDurableVolumeBackendKind
                            .TestDirectory,
                    BackupKeyProvider =
                        TestBackupKeyProvider.Instance,
                }));
            IStoragePoolProvider pools = registry.StoragePoolProviders.Single();
            IDurableVolumeProvider volumes = registry.DurableVolumeProviders.Single();
            IStorageReservationProvider reservations =
                registry.StorageReservationProviders.Single();
            IVolumeBackupProvider backups = registry.VolumeBackupProviders.Single();
            IVolumeRestoreProvider restores = registry.VolumeRestoreProviders.Single();

            ResourceRef<StoragePool> pool = Ref<StoragePool>("pool");
            await pools.EnsureAsync(
                Metadata<StoragePool>("pool"),
                new StoragePoolSpec
                {
                    StorageClass = StorageClass.AppDurable,
                    MinimumFreeBytes = new ByteSize(1),
                    WarningFreeBytes = new ByteSize(2),
                    EmergencyFreeBytes = new ByteSize(0),
                },
                observed: null);
            ResourceRef<DurableVolume> volume = Ref<DurableVolume>("volume");
            await volumes.EnsureAsync(
                Metadata<DurableVolume>("volume"),
                new DurableVolumeSpec
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
                },
                observed: null);
            string content = Path.Combine(
                root,
                "storage",
                "volumes",
                "penpot-data",
                "value.txt");
            await File.WriteAllTextAsync(content, "before");

            DurableVolumeStatus detached = await volumes.DetachAsync(volume);
            Assert.Equal(DurableVolumePhase.DetachedRetained, detached.VolumePhase);
            Assert.Equal("before", await File.ReadAllTextAsync(content));

            ResourceRef<StorageReservation> reservation =
                Ref<StorageReservation>("reservation");
            await reservations.ReserveAsync(
                Metadata<StorageReservation>("reservation"),
                new StorageReservationSpec
                {
                    Pool = pool,
                    OperationId = "backup-restore",
                    Owner = "io.penpot.penpot",
                    RequestedBytes = new ByteSize(4096),
                    ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(5),
                },
                observed: null);
            ResourceRef<VolumeBackup> backup = Ref<VolumeBackup>("backup");
            var backupSpec = new VolumeBackupSpec
            {
                BackupSetId = "backup-set",
                Volume = volume,
                SourceVolumeResource = volume,
                SourceVolumeSpec = VolumeSpec(pool),
                SourceVolumeGeneration = new(1),
                SourceProviderId = LocalEnvironmentProviderDescriptor.ProviderId,
                SourceProviderGeneration = 1,
                SourceProviderRealizationGeneration = 1,
                OwnerTypeId = "io.penpot.penpot",
                OwnerScopeId = "installation",
                OwnerVersion = "revision",
                CompatibilityDomain = "penpot-v1",
                Consistency = VolumeBackupConsistency.Stopped,
                Reservation = reservation,
                Encryption = StorageEncryptionRequirement.Required,
                EncryptionCredential = new("test-backup-key"),
            };
            VolumeBackupStatus captured = await backups.CaptureAsync(
                Metadata<VolumeBackup>("backup"),
                backupSpec,
                observed: null);
            Assert.Equal(VolumeBackupPhase.Ready, captured.BackupPhase);
            await using (var exported = new MemoryStream())
            {
                await backups.ExportAsync(backup, exported);
                Assert.Equal(captured.StoredBytes?.Value, exported.Length);
                exported.Position = 0;
                VolumeBackupStatus imported = await backups.ImportAsync(
                    Metadata<VolumeBackup>("imported-backup"),
                    backupSpec,
                    captured,
                    exported);
                Assert.Equal(VolumeBackupPhase.Ready, imported.BackupPhase);
                Assert.Equal(captured.ContentDigest, imported.ContentDigest);
                Assert.Equal(exported.Length, exported.Position);
            }
            await File.WriteAllTextAsync(content, "after");

            VolumeRestoreStatus restored = await restores.RestoreAsync(
                Metadata<VolumeRestore>("restore"),
                new VolumeRestoreSpec
                {
                    Backup = backup,
                    TargetVolume = volume,
                    Reservation = reservation,
                    ExpectedCompatibilityDomain = "penpot-v1",
                },
                observed: null);
            Assert.Equal(VolumeRestorePhase.Ready, restored.RestorePhase);
            Assert.Equal("before", await File.ReadAllTextAsync(content));
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task Required_backup_encryption_fails_without_credential_authority()
    {
        string root = Path.Combine(
            Path.GetTempPath(),
            "hpd-local-storage-tests",
            Guid.NewGuid().ToString("N"));
        try
        {
            var registry = new EnvironmentProviderRegistry();
            registry.RegisterModule(new LocalEnvironmentProviderModule(
                new LocalEnvironmentProviderOptions
                {
                    StorageRoot = root,
                    EngineSocketPath = "/test/docker.sock",
                    DurableVolumeBackend =
                        LocalDurableVolumeBackendKind
                            .TestDirectory,
                }));
            IStoragePoolProvider pools = registry.StoragePoolProviders.Single();
            IDurableVolumeProvider volumes = registry.DurableVolumeProviders.Single();
            IStorageReservationProvider reservations =
                registry.StorageReservationProviders.Single();
            IVolumeBackupProvider backups = registry.VolumeBackupProviders.Single();
            ResourceRef<StoragePool> pool = Ref<StoragePool>("pool");
            await pools.EnsureAsync(
                Metadata<StoragePool>("pool"),
                PoolSpec(),
                null);
            ResourceRef<DurableVolume> volume = Ref<DurableVolume>("volume");
            await volumes.EnsureAsync(
                Metadata<DurableVolume>("volume"),
                VolumeSpec(pool),
                null);
            ResourceRef<StorageReservation> reservation =
                Ref<StorageReservation>("reservation");
            await reservations.ReserveAsync(
                Metadata<StorageReservation>("reservation"),
                ReservationSpec(pool),
                null);

            InvalidOperationException error =
                await Assert.ThrowsAsync<InvalidOperationException>(() =>
                    backups.CaptureAsync(
                        Metadata<VolumeBackup>("backup"),
                        new VolumeBackupSpec
                        {
                            BackupSetId = "backup-set",
                            Volume = volume,
                            SourceVolumeResource = volume,
                            SourceVolumeSpec = VolumeSpec(pool),
                            SourceVolumeGeneration = new(1),
                            SourceProviderId = LocalEnvironmentProviderDescriptor.ProviderId,
                            SourceProviderGeneration = 1,
                            SourceProviderRealizationGeneration = 1,
                            OwnerTypeId = "io.penpot.penpot",
                            OwnerScopeId = "installation",
                            OwnerVersion = "revision",
                            CompatibilityDomain = "penpot-v1",
                            Consistency = VolumeBackupConsistency.Stopped,
                            Reservation = reservation,
                            EncryptionCredential = new("test-backup-key"),
                        },
                        null).AsTask());
            Assert.Contains(
                "BackupEncryptionAuthorityRequired",
                error.Message,
                StringComparison.Ordinal);
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task Provider_and_runtime_reconstruction_preserve_volume_identity()
    {
        string root = Path.Combine(
            Path.GetTempPath(),
            "hpd-local-storage-tests",
            Guid.NewGuid().ToString("N"));
        try
        {
            var store = new TestStorageStateStore();
            EnvironmentProviderRegistry firstRegistry =
                CreateRegistry(root);
            IEnvironmentRuntime first =
                new InMemoryEnvironmentRuntime(
                    firstRegistry,
                    storageStateStore: store);
            ResourceSnapshot<
                StoragePool,
                StoragePoolSpec,
                StoragePoolStatus> pool =
                await first.EnsureStoragePoolAsync(
                    PoolSpec() with
                    {
                        PreferredProvider =
                            LocalEnvironmentProviderDescriptor
                                .ProviderId,
                    });
            ResourceSnapshot<
                DurableVolume,
                DurableVolumeSpec,
                DurableVolumeStatus> volume =
                await first.EnsureDurableVolumeAsync(
                    VolumeSpec(new ResourceRef<StoragePool>(
                        pool.Metadata.Id,
                        pool.Metadata.Scope,
                        pool.Metadata.Generation)));
            string content = Path.Combine(
                root,
                "storage",
                "volumes",
                "penpot-data",
                "identity.txt");
            await File.WriteAllTextAsync(content, "persistent");
            ResourceSnapshot<
                StorageReservation,
                StorageReservationSpec,
                StorageReservationStatus> reservation =
                await first.EnsureStorageReservationAsync(
                    ReservationSpec(
                        new ResourceRef<StoragePool>(
                            pool.Metadata.Id,
                            pool.Metadata.Scope,
                            pool.Metadata.Generation)));
            ResourceSnapshot<
                VolumeBackup,
                VolumeBackupSpec,
                VolumeBackupStatus> backup =
                await first.CaptureVolumeBackupAsync(
                    new VolumeBackupSpec
                    {
                        BackupSetId = "backup-set",
                        Volume = new ResourceRef<DurableVolume>(
                            volume.Metadata.Id,
                            volume.Metadata.Scope,
                            volume.Metadata.Generation),
                        SourceVolumeResource = new ResourceRef<DurableVolume>(
                            volume.Metadata.Id,
                            volume.Metadata.Scope,
                            volume.Metadata.Generation),
                        SourceVolumeSpec = volume.Spec,
                        SourceVolumeGeneration = volume.Status.VolumeGeneration,
                        SourceProviderId = LocalEnvironmentProviderDescriptor.ProviderId,
                        SourceProviderGeneration = 1,
                        SourceProviderRealizationGeneration =
                            volume.Status.ProviderRealizationGeneration,
                        OwnerTypeId = "io.penpot.penpot",
                        OwnerScopeId = "installation",
                        OwnerVersion = "revision",
                        CompatibilityDomain = "penpot-v1",
                        Consistency =
                            VolumeBackupConsistency.Stopped,
                        Reservation =
                            new ResourceRef<StorageReservation>(
                                reservation.Metadata.Id,
                                reservation.Metadata.Scope,
                                reservation.Metadata.Generation),
                        Encryption =
                            StorageEncryptionRequirement.Required,
                        EncryptionCredential =
                            new("test-backup-key"),
                    });
            await File.WriteAllTextAsync(content, "changed");
            await first.RestoreVolumeAsync(
                new VolumeRestoreSpec
                {
                    Backup = new ResourceRef<VolumeBackup>(
                        backup.Metadata.Id,
                        backup.Metadata.Scope,
                        backup.Metadata.Generation),
                    TargetVolume =
                        new ResourceRef<DurableVolume>(
                            volume.Metadata.Id,
                            volume.Metadata.Scope,
                            volume.Metadata.Generation),
                    Reservation =
                        new ResourceRef<StorageReservation>(
                            reservation.Metadata.Id,
                            reservation.Metadata.Scope,
                            reservation.Metadata.Generation),
                    ExpectedCompatibilityDomain = "penpot-v1",
                });
            Assert.Single(store.State!.Backups);
            Assert.Single(store.State.Restores);

            EnvironmentProviderRegistry secondRegistry =
                CreateRegistry(root);
            IEnvironmentRuntime reconstructed =
                new InMemoryEnvironmentRuntime(
                    secondRegistry,
                    storageStateStore: store);
            ResourceSnapshot<
                DurableVolume,
                DurableVolumeSpec,
                DurableVolumeStatus> recovered =
                await reconstructed.GetDurableVolumeAsync(
                    new ResourceRef<DurableVolume>(
                        volume.Metadata.Id,
                        volume.Metadata.Scope,
                        volume.Metadata.Generation));

            Assert.Equal(volume.Metadata, recovered.Metadata);
            Assert.Equal(volume.Spec, recovered.Spec);
            Assert.Equal(
                "persistent",
                await File.ReadAllTextAsync(content));
            Assert.Single(store.State!.Backups);
            Assert.Single(store.State.Restores);
            Assert.Equal(
                DurableVolumePhase.Ready,
                recovered.Status.VolumePhase);
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task Runtime_reconstruction_does_not_recreate_missing_volume()
    {
        string root = Path.Combine(
            Path.GetTempPath(),
            "hpd-local-storage-tests",
            Guid.NewGuid().ToString("N"));
        try
        {
            var store = new TestStorageStateStore();
            IEnvironmentRuntime first =
                new InMemoryEnvironmentRuntime(
                    CreateRegistry(root),
                    storageStateStore: store);
            ResourceSnapshot<
                StoragePool,
                StoragePoolSpec,
                StoragePoolStatus> pool =
                await first.EnsureStoragePoolAsync(
                    PoolSpec() with
                    {
                        PreferredProvider =
                            LocalEnvironmentProviderDescriptor
                                .ProviderId,
                    });
            ResourceSnapshot<
                DurableVolume,
                DurableVolumeSpec,
                DurableVolumeStatus> volume =
                await first.EnsureDurableVolumeAsync(
                    VolumeSpec(new ResourceRef<StoragePool>(
                        pool.Metadata.Id,
                        pool.Metadata.Scope,
                        pool.Metadata.Generation)));
            string volumePath = Path.Combine(
                root,
                "storage",
                "volumes",
                "penpot-data");
            Directory.Delete(volumePath, recursive: true);

            IEnvironmentRuntime reconstructed =
                new InMemoryEnvironmentRuntime(
                    CreateRegistry(root),
                    storageStateStore: store);
            InvalidOperationException error =
                await Assert.ThrowsAsync<InvalidOperationException>(
                    () => reconstructed.GetDurableVolumeAsync(
                            new ResourceRef<DurableVolume>(
                                volume.Metadata.Id,
                                volume.Metadata.Scope,
                                volume.Metadata.Generation))
                        .AsTask());

            Assert.Contains(
                "IntegrityCheckRequired",
                error.Message,
                StringComparison.Ordinal);
            Assert.False(Directory.Exists(volumePath));
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task Runtime_reconstruction_rejects_missing_physical_volume_identity()
    {
        string root = Path.Combine(
            Path.GetTempPath(),
            "hpd-local-storage-tests",
            Guid.NewGuid().ToString("N"));
        try
        {
            var store = new TestStorageStateStore();
            IEnvironmentRuntime first =
                new InMemoryEnvironmentRuntime(
                    CreateRegistry(root),
                    storageStateStore: store);
            ResourceSnapshot<
                StoragePool,
                StoragePoolSpec,
                StoragePoolStatus> pool =
                await first.EnsureStoragePoolAsync(
                    PoolSpec() with
                    {
                        PreferredProvider =
                            LocalEnvironmentProviderDescriptor
                                .ProviderId,
                    });
            ResourceSnapshot<
                DurableVolume,
                DurableVolumeSpec,
                DurableVolumeStatus> volume =
                await first.EnsureDurableVolumeAsync(
                    VolumeSpec(new ResourceRef<StoragePool>(
                        pool.Metadata.Id,
                        pool.Metadata.Scope,
                        pool.Metadata.Generation)));
            string identityPath = Path.Combine(
                root,
                "storage",
                "volume-state",
                "penpot-data.identity");
            File.Delete(identityPath);

            IEnvironmentRuntime reconstructed =
                new InMemoryEnvironmentRuntime(
                    CreateRegistry(root),
                    storageStateStore: store);
            InvalidOperationException error =
                await Assert.ThrowsAsync<InvalidOperationException>(
                    () => reconstructed.GetDurableVolumeAsync(
                            new ResourceRef<DurableVolume>(
                                volume.Metadata.Id,
                                volume.Metadata.Scope,
                                volume.Metadata.Generation))
                        .AsTask());

            Assert.Contains(
                "IntegrityCheckRequired",
                error.Message,
                StringComparison.Ordinal);
            Assert.True(Directory.Exists(Path.Combine(
                root,
                "storage",
                "volumes",
                "penpot-data")));
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task Runtime_reconstruction_completes_selected_restore_and_finalizes_previous_data()
    {
        string root = Path.Combine(
            Path.GetTempPath(),
            "hpd-local-storage-tests",
            Guid.NewGuid().ToString("N"));
        try
        {
            var store = new TestStorageStateStore();
            IEnvironmentRuntime first =
                new InMemoryEnvironmentRuntime(
                    CreateRegistry(root),
                    storageStateStore: store);
            ResourceSnapshot<StoragePool, StoragePoolSpec, StoragePoolStatus> pool =
                await first.EnsureStoragePoolAsync(
                    PoolSpec() with
                    {
                        PreferredProvider =
                            LocalEnvironmentProviderDescriptor.ProviderId,
                    });
            var poolRef = new ResourceRef<StoragePool>(
                pool.Metadata.Id,
                pool.Metadata.Scope,
                pool.Metadata.Generation);
            ResourceSnapshot<DurableVolume, DurableVolumeSpec, DurableVolumeStatus> volume =
                await first.EnsureDurableVolumeAsync(VolumeSpec(poolRef));
            string targetPath = Path.Combine(
                root,
                "storage",
                "volumes",
                "penpot-data");
            await File.WriteAllTextAsync(
                Path.Combine(targetPath, "value.txt"),
                "persistent");
            ResourceSnapshot<StorageReservation, StorageReservationSpec, StorageReservationStatus> reservation =
                await first.EnsureStorageReservationAsync(
                    ReservationSpec(poolRef));
            var volumeRef = new ResourceRef<DurableVolume>(
                volume.Metadata.Id,
                volume.Metadata.Scope,
                volume.Metadata.Generation);
            var reservationRef = new ResourceRef<StorageReservation>(
                reservation.Metadata.Id,
                reservation.Metadata.Scope,
                reservation.Metadata.Generation);
            ResourceSnapshot<VolumeBackup, VolumeBackupSpec, VolumeBackupStatus> backup =
                await first.CaptureVolumeBackupAsync(
                    new VolumeBackupSpec
                    {
                        BackupSetId = "backup-set",
                        Volume = volumeRef,
                        SourceVolumeResource = volumeRef,
                        SourceVolumeSpec = volume.Spec,
                        SourceVolumeGeneration = volume.Status.VolumeGeneration,
                        SourceProviderId = LocalEnvironmentProviderDescriptor.ProviderId,
                        SourceProviderGeneration = 1,
                        SourceProviderRealizationGeneration =
                            volume.Status.ProviderRealizationGeneration,
                        OwnerTypeId = "io.penpot.penpot",
                        OwnerScopeId = "installation",
                        OwnerVersion = "revision",
                        CompatibilityDomain = "penpot-v1",
                        Consistency = VolumeBackupConsistency.Stopped,
                        Reservation = reservationRef,
                        Encryption = StorageEncryptionRequirement.Required,
                        EncryptionCredential = new("test-backup-key"),
                    });
            await File.WriteAllTextAsync(
                Path.Combine(targetPath, "value.txt"),
                "changed");

            ResourceMetadata<VolumeRestore> restoreMetadata =
                Metadata<VolumeRestore>("restore-crash");
            var backupRef = new ResourceRef<VolumeBackup>(
                backup.Metadata.Id,
                backup.Metadata.Scope,
                backup.Metadata.Generation);
            var restoreSpec = new VolumeRestoreSpec
            {
                Backup = backupRef,
                TargetVolume = volumeRef,
                Reservation = reservationRef,
                ExpectedCompatibilityDomain = "penpot-v1",
            };
            ResourceGeneration previousGeneration =
                volume.Status.VolumeGeneration;
            ResourceGeneration restoredGeneration = new(
                previousGeneration.Value + 1);
            string previousPath =
                targetPath + ".previous-restore-crash";
            Directory.Move(targetPath, previousPath);
            Directory.CreateDirectory(targetPath);
            await File.WriteAllTextAsync(
                Path.Combine(targetPath, "value.txt"),
                "persistent");
            new LocalRestoreOperationStore(
                Path.Combine(root, "storage"))
                .Write(new LocalRestoreOperation(
                    restoreMetadata.Id.Value,
                    restoreMetadata.Scope.Value,
                    restoreMetadata.Generation.Value,
                    backupRef.Id.Value,
                    backupRef.Scope.Value,
                    backupRef.Generation!.Value.Value,
                    volumeRef.Id.Value,
                    volumeRef.Scope.Value,
                    volumeRef.Generation!.Value.Value,
                    volume.Spec.LogicalId,
                    previousGeneration.Value,
                    restoredGeneration.Value,
                    backup.Status.ContentDigest!.Value.Value,
                    PreservePrevious: true,
                    LocalRestoreCheckpoint.Selected));
            EnvironmentRuntimeStorageState authority = store.State!;
            store.Replace(authority with
            {
                Generation = authority.Generation + 1,
                Restores =
                [
                    .. authority.Restores,
                    new ResourceSnapshot<VolumeRestore, VolumeRestoreSpec, VolumeRestoreStatus>(
                        restoreMetadata,
                        restoreSpec,
                        new VolumeRestoreStatus
                        {
                            Phase = ResourcePhase.Pending,
                            RestorePhase = VolumeRestorePhase.Pending,
                            ObservedGeneration =
                                restoreMetadata.Generation,
                            PreviousVolumeGeneration =
                                previousGeneration,
                        }),
                ],
            });

            IEnvironmentRuntime reconstructed =
                new InMemoryEnvironmentRuntime(
                    CreateRegistry(root),
                    storageStateStore: store);
            ResourceSnapshot<DurableVolume, DurableVolumeSpec, DurableVolumeStatus> recovered =
                await reconstructed.GetDurableVolumeAsync(volumeRef);

            Assert.Equal(
                restoredGeneration,
                recovered.Status.VolumeGeneration);
            Assert.Equal(
                "persistent",
                await File.ReadAllTextAsync(
                    Path.Combine(targetPath, "value.txt")));
            Assert.False(Directory.Exists(previousPath));
            Assert.False(File.Exists(Path.Combine(
                root,
                "storage",
                "restore-operations",
                "restore-crash.restore")));
            Assert.Equal(
                VolumeRestorePhase.Ready,
                Assert.Single(store.State!.Restores)
                    .Status.RestorePhase);
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task Volume_limit_is_observed_and_backup_rejects_linked_content()
    {
        if (OperatingSystem.IsWindows())
            return;
        string root = Path.Combine(
            Path.GetTempPath(),
            "hpd-local-storage-tests",
            Guid.NewGuid().ToString("N"));
        try
        {
            EnvironmentProviderRegistry registry =
                CreateRegistry(root);
            IStoragePoolProvider pools =
                registry.StoragePoolProviders.Single();
            IDurableVolumeProvider volumes =
                registry.DurableVolumeProviders.Single();
            IStorageReservationProvider reservations =
                registry.StorageReservationProviders.Single();
            IVolumeBackupProvider backups =
                registry.VolumeBackupProviders.Single();
            ResourceRef<StoragePool> pool =
                Ref<StoragePool>("pool");
            await pools.EnsureAsync(
                Metadata<StoragePool>("pool"),
                PoolSpec(),
                null);
            ResourceRef<DurableVolume> volume =
                Ref<DurableVolume>("volume");
            await volumes.EnsureAsync(
                Metadata<DurableVolume>("volume"),
                VolumeSpec(pool) with
                {
                    MaximumBytes = new ByteSize(4),
                },
                null);
            string volumePath = Path.Combine(
                root,
                "storage",
                "volumes",
                "penpot-data");
            string content = Path.Combine(volumePath, "value");
            await File.WriteAllBytesAsync(content, [1, 2, 3, 4, 5]);

            DurableVolumeStatus overLimit =
                await volumes.GetStatusAsync(volume);
            Assert.Equal(
                ResourcePhase.Degraded,
                overLimit.Phase);
            Assert.Equal(
                DurableVolumePhase.FailedRetained,
                overLimit.VolumePhase);
            Assert.Contains(
                overLimit.Diagnostics,
                static value =>
                    value.Code.Value ==
                    "Environment.Storage.AppVolumeLow");

            File.Delete(content);
            string outside = Path.Combine(root, "outside");
            await File.WriteAllTextAsync(outside, "secret");
            File.CreateSymbolicLink(content, outside);
            ResourceRef<StorageReservation> reservation =
                Ref<StorageReservation>("reservation");
            await reservations.ReserveAsync(
                Metadata<StorageReservation>("reservation"),
                ReservationSpec(pool),
                null);

            InvalidOperationException error =
                await Assert.ThrowsAsync<InvalidOperationException>(
                    () => backups.CaptureAsync(
                            Metadata<VolumeBackup>("backup"),
                            new VolumeBackupSpec
                            {
                                BackupSetId = "backup-set",
                                Volume = volume,
                                SourceVolumeResource = volume,
                                SourceVolumeSpec = VolumeSpec(pool),
                                SourceVolumeGeneration = new(1),
                                SourceProviderId = LocalEnvironmentProviderDescriptor.ProviderId,
                                SourceProviderGeneration = 1,
                                SourceProviderRealizationGeneration = 1,
                                OwnerTypeId = "io.penpot.penpot",
                                OwnerScopeId =
                                    "installation",
                                OwnerVersion = "revision",
                                CompatibilityDomain =
                                    "penpot-v1",
                                Consistency =
                                    VolumeBackupConsistency
                                        .Stopped,
                                Reservation = reservation,
                                Encryption =
                                    StorageEncryptionRequirement
                                        .Required,
                                EncryptionCredential =
                                    new("test-backup-key"),
                            },
                            null)
                        .AsTask());
            Assert.Contains(
                "symbolic link",
                error.Message,
                StringComparison.Ordinal);
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }

    private static EnvironmentProviderRegistry CreateRegistry(
        string root)
    {
        var registry = new EnvironmentProviderRegistry();
        registry.RegisterModule(new LocalEnvironmentProviderModule(
            new LocalEnvironmentProviderOptions
            {
                WorkloadStateRoot = Path.Combine(root, "state"),
                StorageRoot = Path.Combine(root, "storage"),
                EngineSocketPath = "/test/docker.sock",
                DurableVolumeBackend =
                    LocalDurableVolumeBackendKind.TestDirectory,
                BackupKeyProvider =
                    TestBackupKeyProvider.Instance,
            }));
        return registry;
    }

    private static StoragePoolSpec PoolSpec() => new()
    {
        StorageClass = StorageClass.AppDurable,
        MinimumFreeBytes = new ByteSize(1),
        WarningFreeBytes = new ByteSize(2),
        EmergencyFreeBytes = new ByteSize(0),
    };

    private sealed class TestBackupKeyProvider :
        IStorageBackupKeyProvider
    {
        public static TestBackupKeyProvider Instance { get; } =
            new();

        public ValueTask<StorageBackupKeyMaterial> ResolveAsync(
            CredentialRef credential,
            ResourceScope scope,
            string purpose,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            _ = scope;
            _ = purpose;
            return ValueTask.FromResult(
                new StorageBackupKeyMaterial(
                    credential.Value,
                    Enumerable.Range(0, 32)
                        .Select(static value => (byte)value)
                        .ToArray()));
        }
    }

    private static DurableVolumeSpec VolumeSpec(ResourceRef<StoragePool> pool) =>
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

    private static StorageReservationSpec ReservationSpec(
        ResourceRef<StoragePool> pool) =>
        new()
        {
            Pool = pool,
            OperationId = "backup",
            Owner = "io.penpot.penpot",
            RequestedBytes = new ByteSize(4096),
            ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(5),
        };

    private static ResourceMetadata<T> Metadata<T>(string id)
        where T : IExecutionResourceMarker =>
        new()
        {
            Id = new ResourceId<T>(id),
            Kind = new ResourceKind(typeof(T).Name),
            Scope = new ResourceScope("local-storage-tests"),
            Generation = new ResourceGeneration(1),
            SchemaVersion = new SchemaVersion("1"),
        };

    private static ResourceRef<T> Ref<T>(string id)
        where T : IExecutionResourceMarker =>
        new(
            new ResourceId<T>(id),
            new ResourceScope("local-storage-tests"),
            new ResourceGeneration(1));

    private sealed class TestStorageStateStore :
        IEnvironmentRuntimeStorageStateStore
    {
        public EnvironmentRuntimeStorageState? State { get; private set; }

        public void Replace(EnvironmentRuntimeStorageState state) =>
            State = state;

        public ValueTask<EnvironmentRuntimeStorageState?> LoadAsync(
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult(State);
        }

        public ValueTask SaveAsync(
            EnvironmentRuntimeStorageState state,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            State = state;
            return ValueTask.CompletedTask;
        }
    }
}
