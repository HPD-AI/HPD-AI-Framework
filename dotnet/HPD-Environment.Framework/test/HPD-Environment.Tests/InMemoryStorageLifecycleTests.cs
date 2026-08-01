using HPD.Environment.Contracts;
using HPD.Environment.Runtime;

namespace HPD.Environment.Tests;

public sealed class InMemoryStorageLifecycleTests
{
    [Fact]
    public async Task Reservation_is_atomic_and_rejects_overcommit()
    {
        var provider = new InMemoryEnvironmentProvider();
        ResourceRef<StoragePool> pool = await CreatePoolAsync(provider, 10_000);

        StorageReservationStatus first = await provider.ReserveAsync(
            Metadata<StorageReservation>("reservation-1"),
            Reservation(pool, "operation-1", 6_000),
            observed: null);

        Assert.Equal(StorageReservationPhase.Reserved, first.ReservationPhase);
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            provider.ReserveAsync(
                Metadata<StorageReservation>("reservation-2"),
                Reservation(pool, "operation-2", 5_000),
                observed: null).AsTask());

        await provider.ReleaseAsync(Ref<StorageReservation>("reservation-1"));
        StorageReservationStatus second = await provider.ReserveAsync(
            Metadata<StorageReservation>("reservation-2"),
            Reservation(pool, "operation-2", 5_000),
            observed: null);

        Assert.Equal(StorageReservationPhase.Reserved, second.ReservationPhase);
    }

    [Fact]
    public async Task Detach_retain_and_erase_are_distinct_terminal_states()
    {
        var provider = new InMemoryEnvironmentProvider();
        ResourceRef<StoragePool> pool = await CreatePoolAsync(provider, 10_000);
        ResourceRef<DurableVolume> volume = await CreateVolumeAsync(provider, pool);

        DurableVolumeStatus detached = await provider.DetachAsync(volume);
        Assert.Equal(DurableVolumePhase.DetachedRetained, detached.VolumePhase);
        Assert.NotNull(detached.ProviderHandle);

        await provider.EraseAsync(volume);
        DurableVolumeStatus erased = await provider.GetStatusAsync(volume);
        Assert.Equal(DurableVolumePhase.Erased, erased.VolumePhase);
        Assert.Equal(ResourcePhase.Deleted, erased.Phase);
        Assert.Null(erased.ProviderHandle);
    }

    [Fact]
    public async Task Verified_backup_and_active_reservation_advance_restore_generation()
    {
        var provider = new InMemoryEnvironmentProvider();
        ResourceRef<StoragePool> pool = await CreatePoolAsync(provider, 20_000);
        ResourceRef<DurableVolume> volume = await CreateVolumeAsync(provider, pool);
        ResourceRef<StorageReservation> reservation = Ref<StorageReservation>("reservation");
        await provider.ReserveAsync(
            Metadata<StorageReservation>("reservation"),
            Reservation(pool, "backup-and-restore", 5_000),
            observed: null);

        ResourceRef<VolumeBackup> backup = Ref<VolumeBackup>("backup");
        VolumeBackupStatus captured = await provider.CaptureAsync(
            Metadata<VolumeBackup>("backup"),
            new VolumeBackupSpec
            {
                BackupSetId = "backup-set",
                Volume = volume,
                SourceVolumeResource = volume,
                SourceVolumeSpec = VolumeSpec(pool),
                SourceVolumeGeneration = new(1),
                SourceProviderId = InMemoryEnvironmentProvider.InMemoryProviderId,
                SourceProviderGeneration = 1,
                SourceProviderRealizationGeneration = 1,
                OwnerTypeId = "io.penpot.penpot",
                OwnerScopeId = "installation",
                OwnerVersion = "revision-1",
                CompatibilityDomain = "penpot-data-v1",
                Consistency = VolumeBackupConsistency.Stopped,
                Reservation = reservation,
                EncryptionCredential = new("test-backup-key"),
            },
            observed: null);

        Assert.Equal(VolumeBackupPhase.Ready, captured.BackupPhase);
        VolumeRestoreStatus restored = await provider.RestoreAsync(
            Metadata<VolumeRestore>("restore"),
            new VolumeRestoreSpec
            {
                Backup = backup,
                TargetVolume = volume,
                Reservation = reservation,
                ExpectedCompatibilityDomain = "penpot-data-v1",
            },
            observed: null);

        Assert.Equal(VolumeRestorePhase.Ready, restored.RestorePhase);
        Assert.Equal(2, restored.RestoredVolumeGeneration?.Value);
        Assert.Equal(
            VolumeIntegrityState.Verified,
            (await provider.GetStatusAsync(volume)).Integrity);
    }

    [Fact]
    public async Task Runtime_facade_owns_pool_volume_and_reservation_generations()
    {
        var registry = new EnvironmentProviderRegistry();
        registry.RegisterModule(new InMemoryEnvironmentProviderModule());
        IEnvironmentRuntime runtime = new InMemoryEnvironmentRuntime(registry);

        ResourceSnapshot<StoragePool, StoragePoolSpec, StoragePoolStatus> pool =
            await runtime.EnsureStoragePoolAsync(new StoragePoolSpec
            {
                StorageClass = StorageClass.AppDurable,
                PreferredProvider = InMemoryEnvironmentProvider.InMemoryProviderId,
                QuotaBytes = new ByteSize(10_000),
                WarningFreeBytes = new ByteSize(2_000),
                MinimumFreeBytes = new ByteSize(1_000),
                EmergencyFreeBytes = new ByteSize(500),
            });
        ResourceSnapshot<DurableVolume, DurableVolumeSpec, DurableVolumeStatus> volume =
            await runtime.EnsureDurableVolumeAsync(new DurableVolumeSpec
            {
                LogicalId = "installation/backend/data",
                OwnerScopeId = "installation",
                OwnerResourceId = "backend",
                DeclarationId = "data",
                Pool = new ResourceRef<StoragePool>(
                    pool.Metadata.Id,
                    pool.Metadata.Scope,
                    pool.Metadata.Generation),
                MinimumBytes = new ByteSize(1_000),
                MaximumBytes = new ByteSize(10_000),
                BackupEligible = true,
                CompatibilityDomain = "penpot-data-v1",
            });
        ResourceSnapshot<StorageReservation, StorageReservationSpec, StorageReservationStatus>
            reservation = await runtime.EnsureStorageReservationAsync(new StorageReservationSpec
            {
                Pool = volume.Spec.Pool,
                OperationId = "operation",
                Owner = "io.penpot.penpot",
                RequestedBytes = new ByteSize(2_000),
                ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(5),
            });

        Assert.Equal(StoragePoolPhase.Ready, pool.Status.PoolPhase);
        Assert.Equal(DurableVolumePhase.Ready, volume.Status.VolumePhase);
        Assert.Equal(StorageReservationPhase.Reserved, reservation.Status.ReservationPhase);
        await runtime.ReleaseStorageReservationAsync(new ResourceRef<StorageReservation>(
            reservation.Metadata.Id,
            reservation.Metadata.Scope,
            reservation.Metadata.Generation));
    }

    [Fact]
    public async Task Reservation_operation_is_idempotent_without_extending_expiry()
    {
        var registry = new EnvironmentProviderRegistry();
        registry.RegisterModule(new InMemoryEnvironmentProviderModule());
        IEnvironmentRuntime runtime = new InMemoryEnvironmentRuntime(registry);
        ResourceSnapshot<StoragePool, StoragePoolSpec, StoragePoolStatus> pool =
            await runtime.EnsureStoragePoolAsync(new StoragePoolSpec
            {
                StorageClass = StorageClass.RuntimeDisposable,
                PreferredProvider = InMemoryEnvironmentProvider.InMemoryProviderId,
                QuotaBytes = new ByteSize(10_000),
                WarningFreeBytes = new ByteSize(2_000),
                MinimumFreeBytes = new ByteSize(1_000),
                EmergencyFreeBytes = new ByteSize(500),
            });
        var poolRef = new ResourceRef<StoragePool>(
            pool.Metadata.Id,
            pool.Metadata.Scope,
            pool.Metadata.Generation);
        DateTimeOffset originalExpiry = DateTimeOffset.UtcNow.AddMinutes(5);
        var intent = new StorageReservationSpec
        {
            Pool = poolRef,
            OperationId = "pull-images",
            Owner = "io.penpot.penpot",
            RequestedBytes = new ByteSize(2_000),
            EstimatedBytes = new ByteSize(1_500),
            SafetyMultiplier = 1.15,
            ExpiresAt = originalExpiry,
        };

        var created = await runtime.EnsureStorageReservationAsync(intent);
        var adopted = await runtime.EnsureStorageReservationAsync(
            intent with { ExpiresAt = originalExpiry.AddHours(1) });

        Assert.Equal(created.Metadata, adopted.Metadata);
        Assert.Equal(originalExpiry, adopted.Spec.ExpiresAt);
        RuntimeResourceOwnershipException conflict =
            await Assert.ThrowsAsync<RuntimeResourceOwnershipException>(
            () => runtime.EnsureStorageReservationAsync(
                intent with { RequestedBytes = new ByteSize(2_001) }).AsTask());
        Assert.Equal(
            "hpd.environment.storage.reservation-operation-conflict",
            conflict.Diagnostic.Code.Value);
    }

    [Fact]
    public async Task Backup_capture_rejects_caller_forged_source_generation()
    {
        var registry = new EnvironmentProviderRegistry();
        registry.RegisterModule(new InMemoryEnvironmentProviderModule());
        IEnvironmentRuntime runtime = new InMemoryEnvironmentRuntime(registry);
        var pool = await runtime.EnsureStoragePoolAsync(new StoragePoolSpec
        {
            StorageClass = StorageClass.AppDurable,
            PreferredProvider = InMemoryEnvironmentProvider.InMemoryProviderId,
            QuotaBytes = new ByteSize(10_000),
            WarningFreeBytes = new ByteSize(2_000),
            MinimumFreeBytes = new ByteSize(1_000),
            EmergencyFreeBytes = new ByteSize(500),
        });
        var poolRef = new ResourceRef<StoragePool>(
            pool.Metadata.Id,
            pool.Metadata.Scope,
            pool.Metadata.Generation);
        var volume = await runtime.EnsureDurableVolumeAsync(
            VolumeSpec(poolRef));
        var volumeRef = new ResourceRef<DurableVolume>(
            volume.Metadata.Id,
            volume.Metadata.Scope,
            volume.Metadata.Generation);
        var reservation = await runtime.EnsureStorageReservationAsync(
            Reservation(poolRef, "forged-source", 2_000));
        var reservationRef = new ResourceRef<StorageReservation>(
            reservation.Metadata.Id,
            reservation.Metadata.Scope,
            reservation.Metadata.Generation);
        ProviderOpaqueHandle provider = volume.Status.ProviderHandle ??
            throw new InvalidOperationException(
                "The test volume has no provider identity.");

        RuntimeResourceOwnershipException error =
            await Assert.ThrowsAsync<RuntimeResourceOwnershipException>(
                () => runtime.CaptureVolumeBackupAsync(new VolumeBackupSpec
                {
                    BackupSetId = "backup-set",
                    Volume = volumeRef,
                    SourceVolumeResource = volumeRef,
                    SourceVolumeSpec = volume.Spec,
                    SourceVolumeGeneration = new ResourceGeneration(999),
                    SourceProviderId = provider.ProviderId,
                    SourceProviderGeneration = provider.Generation,
                    SourceProviderRealizationGeneration =
                        volume.Status.ProviderRealizationGeneration,
                    OwnerTypeId = "io.penpot.penpot",
                    OwnerScopeId = "installation",
                    OwnerVersion = "revision",
                    CompatibilityDomain = "penpot-data-v1",
                    Consistency = VolumeBackupConsistency.Stopped,
                    Reservation = reservationRef,
                    EncryptionCredential = new("test-backup-key"),
                }).AsTask());

        Assert.Equal(
            "hpd.environment.storage.backup-source-mismatch",
            error.Diagnostic.Code.Value);
    }

    [Fact]
    public async Task Reconstructed_runtime_adopts_reservation_created_before_owner_checkpoint()
    {
        var store = new TestStorageStateStore();
        var firstRegistry = new EnvironmentProviderRegistry();
        firstRegistry.RegisterModule(new InMemoryEnvironmentProviderModule());
        IEnvironmentRuntime first = new InMemoryEnvironmentRuntime(
            firstRegistry,
            storageStateStore: store);
        ResourceSnapshot<StoragePool, StoragePoolSpec, StoragePoolStatus> pool =
            await first.EnsureStoragePoolAsync(new StoragePoolSpec
            {
                StorageClass = StorageClass.RuntimeDisposable,
                PreferredProvider = InMemoryEnvironmentProvider.InMemoryProviderId,
                QuotaBytes = new ByteSize(10_000),
                WarningFreeBytes = new ByteSize(2_000),
                MinimumFreeBytes = new ByteSize(1_000),
                EmergencyFreeBytes = new ByteSize(500),
            });
        var intent = new StorageReservationSpec
        {
            Pool = new ResourceRef<StoragePool>(
                pool.Metadata.Id,
                pool.Metadata.Scope,
                pool.Metadata.Generation),
            OperationId = "install:immutable-images",
            Owner = "installation",
            RequestedBytes = new ByteSize(2_000),
            EstimatedBytes = new ByteSize(1_500),
            SafetyMultiplier = 1.15,
            ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(30),
        };
        var created = await first.EnsureStorageReservationAsync(intent);

        var recoveredRegistry = new EnvironmentProviderRegistry();
        recoveredRegistry.RegisterModule(
            new InMemoryEnvironmentProviderModule());
        IEnvironmentRuntime recovered = new InMemoryEnvironmentRuntime(
            recoveredRegistry,
            storageStateStore: store);
        var adopted = await recovered.EnsureStorageReservationAsync(
            intent with { ExpiresAt = intent.ExpiresAt.AddMinutes(30) });

        Assert.Equal(created.Metadata, adopted.Metadata);
        Assert.Equal(created.Spec, adopted.Spec);
        Assert.Equal(
            StorageReservationPhase.Reserved,
            adopted.Status.ReservationPhase);
    }

    [Fact]
    public async Task Expired_reservation_is_recovered_as_ambiguous_and_cannot_authorize_mutation()
    {
        var registry = new EnvironmentProviderRegistry();
        registry.RegisterModule(new InMemoryEnvironmentProviderModule());
        var store = new TestStorageStateStore();
        IEnvironmentRuntime first = new InMemoryEnvironmentRuntime(
            registry,
            storageStateStore: store);
        var pool = await first.EnsureStoragePoolAsync(new StoragePoolSpec
        {
            StorageClass = StorageClass.AppDurable,
            PreferredProvider =
                InMemoryEnvironmentProvider.InMemoryProviderId,
            QuotaBytes = new ByteSize(20_000),
            WarningFreeBytes = new ByteSize(2_000),
            MinimumFreeBytes = new ByteSize(1_000),
            EmergencyFreeBytes = new ByteSize(500),
        });
        var volume = await first.EnsureDurableVolumeAsync(
            VolumeSpec(new ResourceRef<StoragePool>(
                pool.Metadata.Id,
                pool.Metadata.Scope,
                pool.Metadata.Generation)));
        var reservation = await first.EnsureStorageReservationAsync(
            Reservation(
                volume.Spec.Pool,
                "expired-backup",
                2_000));
        EnvironmentRuntimeStorageState persisted =
            Assert.IsType<EnvironmentRuntimeStorageState>(store.State);
        store.Replace(persisted with
        {
            Reservations =
            [
                reservation with
                {
                    Spec = reservation.Spec with
                    {
                        ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(-1),
                    },
                },
            ],
        });
        IEnvironmentRuntime recovered = new InMemoryEnvironmentRuntime(
            registry,
            storageStateStore: store);

        RuntimeResourceOwnershipException conflict =
            await Assert.ThrowsAsync<RuntimeResourceOwnershipException>(
                () => recovered.EnsureStorageReservationAsync(
                    reservation.Spec with
                    {
                        ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(5),
                    }).AsTask());

        Assert.Equal(
            "hpd.environment.storage.reservation-not-active",
            conflict.Diagnostic.Code.Value);
        StorageReservationStatus status = Assert.Single(
            store.State!.Reservations).Status;
        Assert.Equal(
            StorageReservationPhase.Ambiguous,
            status.ReservationPhase);
    }

    [Fact]
    public async Task Runtime_reconstruction_recovers_authoritative_storage_identity()
    {
        var registry = new EnvironmentProviderRegistry();
        registry.RegisterModule(new InMemoryEnvironmentProviderModule());
        var store = new TestStorageStateStore();
        IEnvironmentRuntime first =
            new InMemoryEnvironmentRuntime(
                registry,
                storageStateStore: store);
        ResourceSnapshot<StoragePool, StoragePoolSpec, StoragePoolStatus> pool =
            await first.EnsureStoragePoolAsync(new StoragePoolSpec
            {
                StorageClass = StorageClass.AppDurable,
                PreferredProvider =
                    InMemoryEnvironmentProvider.InMemoryProviderId,
                QuotaBytes = new ByteSize(10_000),
                WarningFreeBytes = new ByteSize(2_000),
                MinimumFreeBytes = new ByteSize(1_000),
                EmergencyFreeBytes = new ByteSize(500),
            });
        ResourceSnapshot<DurableVolume, DurableVolumeSpec, DurableVolumeStatus>
            volume = await first.EnsureDurableVolumeAsync(
                new DurableVolumeSpec
                {
                    LogicalId = "installation/backend/data",
                    OwnerScopeId = "installation",
                    OwnerResourceId = "backend",
                    DeclarationId = "data",
                    Pool = new ResourceRef<StoragePool>(
                        pool.Metadata.Id,
                        pool.Metadata.Scope,
                        pool.Metadata.Generation),
                    MinimumBytes = new ByteSize(1_000),
                    MaximumBytes = new ByteSize(10_000),
                    BackupEligible = true,
                    CompatibilityDomain =
                        "penpot-data-v1",
                });

        IEnvironmentRuntime reconstructed =
            new InMemoryEnvironmentRuntime(
                registry,
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
            DurableVolumePhase.Ready,
            recovered.Status.VolumePhase);
        Assert.NotNull(store.State);
        Assert.Equal(
            EnvironmentRuntimeStorageState.CurrentSchema,
            store.State!.Schema);
    }

    private static async Task<ResourceRef<StoragePool>> CreatePoolAsync(
        InMemoryEnvironmentProvider provider,
        long capacity)
    {
        await provider.EnsureAsync(
            Metadata<StoragePool>("pool"),
            new StoragePoolSpec
            {
                StorageClass = StorageClass.AppDurable,
                QuotaBytes = new ByteSize(capacity),
                WarningFreeBytes = new ByteSize(2_000),
                MinimumFreeBytes = new ByteSize(1_000),
                EmergencyFreeBytes = new ByteSize(500),
            },
            observed: null);
        return Ref<StoragePool>("pool");
    }

    private static async Task<ResourceRef<DurableVolume>> CreateVolumeAsync(
        InMemoryEnvironmentProvider provider,
        ResourceRef<StoragePool> pool)
    {
        await provider.EnsureAsync(
            Metadata<DurableVolume>("volume"),
            VolumeSpec(pool),
            observed: null);
        return Ref<DurableVolume>("volume");
    }

    private static DurableVolumeSpec VolumeSpec(
        ResourceRef<StoragePool> pool) => new()
    {
        LogicalId = "installation/backend/data",
        OwnerScopeId = "installation",
        OwnerResourceId = "backend",
        DeclarationId = "data",
        Pool = pool,
        MinimumBytes = new ByteSize(1_000),
        MaximumBytes = new ByteSize(10_000),
        Retention = DurableVolumeRetention.RetainOnRemove,
        BackupEligible = true,
        CompatibilityDomain = "penpot-data-v1",
    };

    private static StorageReservationSpec Reservation(
        ResourceRef<StoragePool> pool,
        string operation,
        long bytes) =>
        new()
        {
            Pool = pool,
            OperationId = operation,
            Owner = "test",
            RequestedBytes = new ByteSize(bytes),
            ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(5),
        };

    private static ResourceMetadata<T> Metadata<T>(string id)
        where T : IExecutionResourceMarker =>
        new()
        {
            Id = new ResourceId<T>(id),
            Kind = new ResourceKind(typeof(T).Name),
            Scope = new ResourceScope("test"),
            Generation = new ResourceGeneration(1),
            SchemaVersion = new SchemaVersion("1"),
            CreatedAt = DateTimeOffset.UtcNow,
        };

    private static ResourceRef<T> Ref<T>(string id)
        where T : IExecutionResourceMarker =>
        new(new ResourceId<T>(id), new ResourceScope("test"), new ResourceGeneration(1));

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
