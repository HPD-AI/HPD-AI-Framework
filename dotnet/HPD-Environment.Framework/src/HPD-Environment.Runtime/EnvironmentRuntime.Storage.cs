using HPD.Environment.Contracts;

namespace HPD.Environment.Runtime;

public interface IEnvironmentRuntimeStorageStateStore
{
    ValueTask<EnvironmentRuntimeStorageState?> LoadAsync(
        CancellationToken cancellationToken = default);
    ValueTask SaveAsync(
        EnvironmentRuntimeStorageState state,
        CancellationToken cancellationToken = default);
}

public sealed record EnvironmentRuntimeStorageState(
    string Schema,
    long Generation,
    IReadOnlyList<ResourceSnapshot<
        StoragePool,
        StoragePoolSpec,
        StoragePoolStatus>> Pools,
    IReadOnlyList<ResourceSnapshot<
        DurableVolume,
        DurableVolumeSpec,
        DurableVolumeStatus>> Volumes,
    IReadOnlyList<ResourceSnapshot<
        StorageReservation,
        StorageReservationSpec,
        StorageReservationStatus>> Reservations,
    IReadOnlyList<ResourceSnapshot<
        VolumeBackup,
        VolumeBackupSpec,
        VolumeBackupStatus>> Backups,
    IReadOnlyList<ResourceSnapshot<
        VolumeRestore,
        VolumeRestoreSpec,
        VolumeRestoreStatus>> Restores)
{
    public const string CurrentSchema =
        "hpd.environment.runtime-storage/v1";
}

public sealed partial class InMemoryEnvironmentRuntime
{
    private readonly Dictionary<string, ResourceSnapshot<StoragePool, StoragePoolSpec, StoragePoolStatus>>
        _storagePools = new(StringComparer.Ordinal);
    private readonly Dictionary<string, ResourceSnapshot<DurableVolume, DurableVolumeSpec, DurableVolumeStatus>>
        _durableVolumes = new(StringComparer.Ordinal);
    private readonly Dictionary<string, ResourceSnapshot<StorageReservation, StorageReservationSpec, StorageReservationStatus>>
        _storageReservations = new(StringComparer.Ordinal);
    private readonly Dictionary<string, ResourceSnapshot<VolumeBackup, VolumeBackupSpec, VolumeBackupStatus>>
        _volumeBackups = new(StringComparer.Ordinal);
    private readonly Dictionary<string, ResourceSnapshot<VolumeRestore, VolumeRestoreSpec, VolumeRestoreStatus>>
        _volumeRestores = new(StringComparer.Ordinal);
    private bool _storageStateLoaded;
    private long _storageStateGeneration;

    public async ValueTask<ResourceSnapshot<StoragePool, StoragePoolSpec, StoragePoolStatus>>
        EnsureStoragePoolAsync(
            StoragePoolSpec spec,
            CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(spec);
        await _reconciliationGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await EnsureStorageStateLoadedAsync(cancellationToken)
                .ConfigureAwait(false);
            string key = spec.StorageClass.ToString();
            _storagePools.TryGetValue(key, out var existing);
            ResourceMetadata<StoragePool> metadata = existing?.Metadata ??
                Metadata<StoragePool>("storage-pool");
            IStoragePoolProvider provider = SelectStorageProvider(
                registry.StoragePoolProviders,
                spec.PreferredProvider,
                "storage pool");
            StoragePoolStatus status = await provider
                .EnsureAsync(metadata, spec, existing?.Status, cancellationToken)
                .ConfigureAwait(false);
            var snapshot = new ResourceSnapshot<StoragePool, StoragePoolSpec, StoragePoolStatus>(
                metadata,
                spec,
                status);
            if (status.ReconciliationOutcome == ResourceReconciliationOutcome.Accepted)
            {
                _storagePools[key] = snapshot;
                await PersistStorageStateAsync(cancellationToken)
                    .ConfigureAwait(false);
            }
            return snapshot;
        }
        finally
        {
            _reconciliationGate.Release();
        }
    }

    public async ValueTask<ResourceSnapshot<StoragePool, StoragePoolSpec, StoragePoolStatus>>
        GetStoragePoolAsync(
            ResourceRef<StoragePool> pool,
            CancellationToken cancellationToken = default)
    {
        await _reconciliationGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await EnsureStorageStateLoadedAsync(cancellationToken)
                .ConfigureAwait(false);
            var owned = Find(_storagePools.Values, pool);
            IStoragePoolProvider provider = ProviderById(
                registry.StoragePoolProviders,
                owned.Status.ProviderHandle?.ProviderId ??
                    throw OwnershipFailure("hpd.environment.storage.provider-missing", "Storage pool provider identity is missing."),
                "storage pool");
            return owned with
            {
                Status = await provider.GetStatusAsync(pool, cancellationToken).ConfigureAwait(false),
            };
        }
        finally
        {
            _reconciliationGate.Release();
        }
    }

    public async ValueTask DeleteStoragePoolAsync(
        ResourceRef<StoragePool> pool,
        CancellationToken cancellationToken = default)
    {
        await _reconciliationGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await EnsureStorageStateLoadedAsync(cancellationToken)
                .ConfigureAwait(false);
            var owned = Find(_storagePools.Values, pool);
            IStoragePoolProvider provider = ProviderById(
                registry.StoragePoolProviders,
                owned.Status.ProviderHandle?.ProviderId ??
                    throw OwnershipFailure("hpd.environment.storage.provider-missing", "Storage pool provider identity is missing."),
                "storage pool");
            await provider.DeleteAsync(pool, cancellationToken).ConfigureAwait(false);
            _storagePools.Remove(owned.Spec.StorageClass.ToString());
            await PersistStorageStateAsync(cancellationToken)
                .ConfigureAwait(false);
        }
        finally
        {
            _reconciliationGate.Release();
        }
    }

    public async ValueTask<ResourceSnapshot<DurableVolume, DurableVolumeSpec, DurableVolumeStatus>>
        EnsureDurableVolumeAsync(
            DurableVolumeSpec spec,
            CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(spec);
        await _reconciliationGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await EnsureStorageStateLoadedAsync(cancellationToken)
                .ConfigureAwait(false);
            _ = Find(_storagePools.Values, spec.Pool);
            _durableVolumes.TryGetValue(spec.LogicalId, out var existing);
            ResourceMetadata<DurableVolume> metadata = existing?.Metadata ??
                Metadata<DurableVolume>("durable-volume");
            ProviderId owner = Find(_storagePools.Values, spec.Pool).Status.ProviderHandle?.ProviderId ??
                throw OwnershipFailure("hpd.environment.storage.provider-missing", "Storage pool provider identity is missing.");
            IDurableVolumeProvider provider = SelectStorageProvider(
                registry.DurableVolumeProviders,
                owner,
                "durable volume");
            DurableVolumeStatus status = await provider
                .EnsureAsync(metadata, spec, existing?.Status, cancellationToken)
                .ConfigureAwait(false);
            var snapshot = new ResourceSnapshot<DurableVolume, DurableVolumeSpec, DurableVolumeStatus>(
                metadata,
                spec,
                status);
            if (status.ReconciliationOutcome == ResourceReconciliationOutcome.Accepted)
            {
                _durableVolumes[spec.LogicalId] = snapshot;
                await PersistStorageStateAsync(cancellationToken)
                    .ConfigureAwait(false);
            }
            return snapshot;
        }
        finally
        {
            _reconciliationGate.Release();
        }
    }

    public async ValueTask<ResourceSnapshot<DurableVolume, DurableVolumeSpec, DurableVolumeStatus>>
        GetDurableVolumeAsync(ResourceRef<DurableVolume> volume, CancellationToken cancellationToken = default)
    {
        await _reconciliationGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await EnsureStorageStateLoadedAsync(cancellationToken)
                .ConfigureAwait(false);
            var owned = Find(_durableVolumes.Values, volume);
            IDurableVolumeProvider provider = ProviderById(
                registry.DurableVolumeProviders,
                owned.Status.ProviderHandle?.ProviderId ??
                    throw OwnershipFailure("hpd.environment.storage.provider-missing", "Durable volume provider identity is missing."),
                "durable volume");
            return owned with
            {
                Status = await provider.GetStatusAsync(volume, cancellationToken).ConfigureAwait(false),
            };
        }
        finally
        {
            _reconciliationGate.Release();
        }
    }

    public async ValueTask DetachDurableVolumeAsync(
        ResourceRef<DurableVolume> volume,
        CancellationToken cancellationToken = default)
    {
        await MutateVolumeAsync(volume, erase: false, cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask EraseDurableVolumeAsync(
        ResourceRef<DurableVolume> volume,
        CancellationToken cancellationToken = default)
    {
        await MutateVolumeAsync(volume, erase: true, cancellationToken).ConfigureAwait(false);
    }

    private async ValueTask MutateVolumeAsync(
        ResourceRef<DurableVolume> volume,
        bool erase,
        CancellationToken cancellationToken)
    {
        await _reconciliationGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await EnsureStorageStateLoadedAsync(cancellationToken)
                .ConfigureAwait(false);
            var owned = Find(_durableVolumes.Values, volume);
            IDurableVolumeProvider provider = ProviderById(
                registry.DurableVolumeProviders,
                owned.Status.ProviderHandle?.ProviderId ??
                    throw OwnershipFailure("hpd.environment.storage.provider-missing", "Durable volume provider identity is missing."),
                "durable volume");
            if (erase)
                await provider.EraseAsync(volume, cancellationToken).ConfigureAwait(false);
            else
                _durableVolumes[owned.Spec.LogicalId] = owned with
                {
                    Status = await provider.DetachAsync(volume, cancellationToken).ConfigureAwait(false),
                };
            if (erase)
                _durableVolumes.Remove(owned.Spec.LogicalId);
            await PersistStorageStateAsync(CancellationToken.None)
                .ConfigureAwait(false);
        }
        finally
        {
            _reconciliationGate.Release();
        }
    }

    public async ValueTask<ResourceSnapshot<StorageReservation, StorageReservationSpec, StorageReservationStatus>>
        EnsureStorageReservationAsync(
            StorageReservationSpec spec,
            CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(spec);
        await _reconciliationGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await EnsureStorageStateLoadedAsync(cancellationToken)
                .ConfigureAwait(false);
            _ = Find(_storagePools.Values, spec.Pool);
            if (_storageReservations.TryGetValue(spec.OperationId, out var existing))
            {
                if (!SameReservationIntent(existing.Spec, spec))
                {
                    throw OwnershipFailure(
                        "hpd.environment.storage.reservation-operation-conflict",
                        $"Operation '{spec.OperationId}' already owns a reservation with different immutable intent.");
                }

                if (existing.Status.ReservationPhase !=
                    StorageReservationPhase.Reserved)
                {
                    throw OwnershipFailure(
                        "hpd.environment.storage.reservation-not-active",
                        $"Operation '{spec.OperationId}' owns a reservation in '{existing.Status.ReservationPhase}' state; it cannot authorize mutation and must be explicitly reconciled.");
                }

                // OperationId is the durable idempotency key. The original
                // expiry is intentionally retained: retrying recovery may
                // adopt a reservation, but it must never extend its lease.
                return existing;
            }
            ResourceMetadata<StorageReservation> metadata = Metadata<StorageReservation>("storage-reservation");
            ProviderId owner = Find(_storagePools.Values, spec.Pool).Status.ProviderHandle?.ProviderId ??
                throw OwnershipFailure("hpd.environment.storage.provider-missing", "Storage pool provider identity is missing.");
            IStorageReservationProvider provider = SelectStorageProvider(
                registry.StorageReservationProviders,
                owner,
                "storage reservation");
            StorageReservationStatus status = await provider
                .ReserveAsync(metadata, spec, observed: null, cancellationToken)
                .ConfigureAwait(false);
            var snapshot = new ResourceSnapshot<StorageReservation, StorageReservationSpec, StorageReservationStatus>(
                metadata,
                spec,
                status);
            _storageReservations[spec.OperationId] = snapshot;
            // Provider reservation is authoritative once ReserveAsync returns.
            // Persist its idempotency key even if the initiating caller is
            // cancelled at this boundary, otherwise recovery cannot adopt it.
            await PersistStorageStateAsync(CancellationToken.None)
                .ConfigureAwait(false);
            return snapshot;
        }
        finally
        {
            _reconciliationGate.Release();
        }
    }

    private static bool SameReservationIntent(
        StorageReservationSpec existing,
        StorageReservationSpec requested) =>
        existing.Pool == requested.Pool &&
        string.Equals(existing.Owner, requested.Owner, StringComparison.Ordinal) &&
        existing.RequestedBytes == requested.RequestedBytes &&
        existing.EstimatedBytes == requested.EstimatedBytes &&
        existing.SafetyMultiplier.Equals(requested.SafetyMultiplier);

    public async ValueTask ReleaseStorageReservationAsync(
        ResourceRef<StorageReservation> reservation,
        CancellationToken cancellationToken = default)
    {
        await _reconciliationGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await EnsureStorageStateLoadedAsync(cancellationToken)
                .ConfigureAwait(false);
            var owned = Find(_storageReservations.Values, reservation);
            IStorageReservationProvider provider = SelectStorageProvider(
                registry.StorageReservationProviders,
                owned.Status.ProviderHandle?.ProviderId,
                "storage reservation");
            await provider.ReleaseAsync(reservation, cancellationToken).ConfigureAwait(false);
            _storageReservations.Remove(owned.Spec.OperationId);
            await PersistStorageStateAsync(CancellationToken.None)
                .ConfigureAwait(false);
        }
        finally
        {
            _reconciliationGate.Release();
        }
    }

    public async ValueTask<ResourceSnapshot<VolumeBackup, VolumeBackupSpec, VolumeBackupStatus>>
        CaptureVolumeBackupAsync(VolumeBackupSpec spec, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(spec);
        await _reconciliationGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await EnsureStorageStateLoadedAsync(cancellationToken)
                .ConfigureAwait(false);
            ResourceSnapshot<
                DurableVolume,
                DurableVolumeSpec,
                DurableVolumeStatus> source =
                Find(_durableVolumes.Values, spec.Volume);
            ValidateBackupSourceEvidence(source, spec);
            _ = Find(_storageReservations.Values, spec.Reservation);
            ResourceMetadata<VolumeBackup> metadata = Metadata<VolumeBackup>("volume-backup");
            ProviderId owner = Find(_durableVolumes.Values, spec.Volume).Status.ProviderHandle?.ProviderId ??
                throw OwnershipFailure("hpd.environment.storage.provider-missing", "Durable volume provider identity is missing.");
            IVolumeBackupProvider provider = SelectStorageProvider(registry.VolumeBackupProviders, owner, "volume backup");
            var snapshot = new ResourceSnapshot<VolumeBackup, VolumeBackupSpec, VolumeBackupStatus>(
                metadata,
                spec,
                new VolumeBackupStatus
                {
                    Phase = ResourcePhase.Pending,
                    BackupPhase = VolumeBackupPhase.Pending,
                    ReconciliationOutcome =
                        ResourceReconciliationOutcome.Accepted,
                    ObservedGeneration = metadata.Generation,
                });
            _volumeBackups[metadata.Id.Value] = snapshot;
            await PersistStorageStateAsync(CancellationToken.None)
                .ConfigureAwait(false);
            try
            {
                VolumeBackupStatus status = await provider
                    .CaptureAsync(
                        metadata,
                        spec,
                        observed: null,
                        cancellationToken)
                    .ConfigureAwait(false);
                snapshot = snapshot with { Status = status };
                _volumeBackups[metadata.Id.Value] = snapshot;
                await PersistStorageStateAsync(CancellationToken.None)
                    .ConfigureAwait(false);
                return snapshot;
            }
            catch
            {
                snapshot = snapshot with
                {
                    Status = snapshot.Status with
                    {
                        Phase = ResourcePhase.Degraded,
                        BackupPhase =
                            VolumeBackupPhase.FailedRetained,
                    },
                };
                _volumeBackups[metadata.Id.Value] = snapshot;
                await PersistStorageStateAsync(CancellationToken.None)
                    .ConfigureAwait(false);
                throw;
            }
        }
        finally
        {
            _reconciliationGate.Release();
        }
    }

    public async ValueTask ExportVolumeBackupAsync(
        ResourceRef<VolumeBackup> backup,
        Stream destination,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(destination);
        if (!destination.CanWrite)
            throw new ArgumentException(
                "The backup destination stream must be writable.",
                nameof(destination));

        await _reconciliationGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await EnsureStorageStateLoadedAsync(cancellationToken)
                .ConfigureAwait(false);
            var owned = Find(_volumeBackups.Values, backup);
            if (owned.Status.BackupPhase != VolumeBackupPhase.Ready ||
                owned.Status.StoredBytes is null ||
                owned.Status.ContentDigest is null)
                throw OwnershipFailure(
                    "hpd.environment.storage.backup-not-ready",
                    "Only a verified ready backup may be exported.");
            IVolumeBackupProvider provider = SelectStorageProvider(
                registry.VolumeBackupProviders,
                owned.Status.ProviderHandle?.ProviderId,
                "volume backup");
            await provider.ExportAsync(backup, destination, cancellationToken)
                .ConfigureAwait(false);
        }
        finally
        {
            _reconciliationGate.Release();
        }
    }

    public async ValueTask<ResourceSnapshot<VolumeBackup, VolumeBackupSpec, VolumeBackupStatus>>
        ImportVolumeBackupAsync(
            VolumeBackupSpec spec,
            VolumeBackupStatus expectedStatus,
            Stream source,
            CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(spec);
        ArgumentNullException.ThrowIfNull(expectedStatus);
        ArgumentNullException.ThrowIfNull(source);
        if (!source.CanRead)
            throw new ArgumentException(
                "The backup source stream must be readable.",
                nameof(source));
        if (expectedStatus.BackupPhase != VolumeBackupPhase.Ready ||
            expectedStatus.StoredBytes is null ||
            expectedStatus.ContentDigest is null ||
            expectedStatus.LogicalBytes is null ||
            expectedStatus.CapturedAt is null)
            throw OwnershipFailure(
                "hpd.environment.storage.backup-import-evidence-invalid",
                "Imported backup evidence must describe a verified ready backup.");

        await _reconciliationGate.WaitAsync(cancellationToken)
            .ConfigureAwait(false);
        try
        {
            await EnsureStorageStateLoadedAsync(cancellationToken)
                .ConfigureAwait(false);
            _ = Find(_storagePools.Values, spec.SourceVolumeSpec.Pool);
            _ = Find(_storageReservations.Values, spec.Reservation);
            ProviderId owner = Find(
                    _storagePools.Values,
                    spec.SourceVolumeSpec.Pool)
                .Status.ProviderHandle?.ProviderId ??
                throw OwnershipFailure(
                    "hpd.environment.storage.provider-missing",
                    "Storage pool provider identity is missing.");
            IVolumeBackupProvider provider = SelectStorageProvider(
                registry.VolumeBackupProviders,
                owner,
                "volume backup");
            ResourceMetadata<VolumeBackup> metadata =
                Metadata<VolumeBackup>("volume-backup");
            var pending = new ResourceSnapshot<VolumeBackup, VolumeBackupSpec, VolumeBackupStatus>(
                metadata,
                spec,
                expectedStatus with
                {
                    Phase = ResourcePhase.Pending,
                    BackupPhase = VolumeBackupPhase.Pending,
                    ObservedGeneration = metadata.Generation,
                });
            _volumeBackups[metadata.Id.Value] = pending;
            await PersistStorageStateAsync(CancellationToken.None)
                .ConfigureAwait(false);
            try
            {
                VolumeBackupStatus status = await provider.ImportAsync(
                        metadata,
                        spec,
                        expectedStatus,
                        source,
                        cancellationToken)
                    .ConfigureAwait(false);
                var imported = pending with { Status = status };
                _volumeBackups[metadata.Id.Value] = imported;
                await PersistStorageStateAsync(CancellationToken.None)
                    .ConfigureAwait(false);
                return imported;
            }
            catch
            {
                _volumeBackups[metadata.Id.Value] = pending with
                {
                    Status = pending.Status with
                    {
                        Phase = ResourcePhase.Degraded,
                        BackupPhase = VolumeBackupPhase.FailedRetained,
                    },
                };
                await PersistStorageStateAsync(CancellationToken.None)
                    .ConfigureAwait(false);
                throw;
            }
        }
        finally
        {
            _reconciliationGate.Release();
        }
    }

    public async ValueTask DeleteVolumeBackupAsync(
        ResourceRef<VolumeBackup> backup,
        CancellationToken cancellationToken = default)
    {
        await _reconciliationGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await EnsureStorageStateLoadedAsync(cancellationToken)
                .ConfigureAwait(false);
            var owned = Find(_volumeBackups.Values, backup);
            IVolumeBackupProvider provider = SelectStorageProvider(
                registry.VolumeBackupProviders,
                owned.Status.ProviderHandle?.ProviderId,
                "volume backup");
            await provider.DeleteAsync(backup, cancellationToken).ConfigureAwait(false);
            _volumeBackups.Remove(owned.Metadata.Id.Value);
            await PersistStorageStateAsync(CancellationToken.None)
                .ConfigureAwait(false);
        }
        finally
        {
            _reconciliationGate.Release();
        }
    }

    public async ValueTask<ResourceSnapshot<VolumeRestore, VolumeRestoreSpec, VolumeRestoreStatus>>
        RestoreVolumeAsync(VolumeRestoreSpec spec, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(spec);
        await _reconciliationGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await EnsureStorageStateLoadedAsync(cancellationToken)
                .ConfigureAwait(false);
            _ = Find(_volumeBackups.Values, spec.Backup);
            _ = Find(_durableVolumes.Values, spec.TargetVolume);
            _ = Find(_storageReservations.Values, spec.Reservation);
            ResourceMetadata<VolumeRestore> metadata = Metadata<VolumeRestore>("volume-restore");
            ProviderId owner = Find(_durableVolumes.Values, spec.TargetVolume).Status.ProviderHandle?.ProviderId ??
                throw OwnershipFailure("hpd.environment.storage.provider-missing", "Durable volume provider identity is missing.");
            IVolumeRestoreProvider provider = SelectStorageProvider(registry.VolumeRestoreProviders, owner, "volume restore");
            ResourceGeneration previousVolumeGeneration =
                Find(_durableVolumes.Values, spec.TargetVolume)
                    .Status.VolumeGeneration;
            var snapshot = new ResourceSnapshot<VolumeRestore, VolumeRestoreSpec, VolumeRestoreStatus>(
                metadata,
                spec,
                new VolumeRestoreStatus
                {
                    Phase = ResourcePhase.Pending,
                    RestorePhase = VolumeRestorePhase.Pending,
                    ReconciliationOutcome =
                        ResourceReconciliationOutcome.Accepted,
                    ObservedGeneration = metadata.Generation,
                    PreviousVolumeGeneration =
                        previousVolumeGeneration,
                });
            _volumeRestores[metadata.Id.Value] = snapshot;
            await PersistStorageStateAsync(CancellationToken.None)
                .ConfigureAwait(false);
            VolumeRestoreStatus status;
            try
            {
                status = await provider
                    .RestoreAsync(
                        metadata,
                        spec,
                        observed: null,
                        cancellationToken)
                    .ConfigureAwait(false);
            }
            catch
            {
                snapshot = snapshot with
                {
                    Status = snapshot.Status with
                    {
                        Phase = ResourcePhase.Degraded,
                        RestorePhase =
                            VolumeRestorePhase.FailedRetained,
                    },
                };
                _volumeRestores[metadata.Id.Value] = snapshot;
                await PersistStorageStateAsync(CancellationToken.None)
                    .ConfigureAwait(false);
                throw;
            }
            snapshot = snapshot with { Status = status };
            _volumeRestores[metadata.Id.Value] = snapshot;
            ResourceSnapshot<DurableVolume, DurableVolumeSpec, DurableVolumeStatus> volume =
                Find(_durableVolumes.Values, spec.TargetVolume);
            _durableVolumes[volume.Spec.LogicalId] = volume with
            {
                Status = await ProviderById(
                        registry.DurableVolumeProviders,
                        volume.Status.ProviderHandle?.ProviderId ??
                            throw OwnershipFailure("hpd.environment.storage.provider-missing", "Durable volume provider identity is missing."),
                        "durable volume")
                    .GetStatusAsync(spec.TargetVolume, cancellationToken)
                    .ConfigureAwait(false),
            };
            await PersistStorageStateAsync(CancellationToken.None)
                .ConfigureAwait(false);
            await provider.FinalizeAsync(
                    new ResourceRef<VolumeRestore>(
                        metadata.Id,
                        metadata.Scope,
                        metadata.Generation),
                    CancellationToken.None)
                .ConfigureAwait(false);
            return snapshot;
        }
        finally
        {
            _reconciliationGate.Release();
        }
    }

    private async ValueTask EnsureStorageStateLoadedAsync(
        CancellationToken cancellationToken)
    {
        if (_storageStateLoaded)
            return;
        if (_storageStateStore is null)
        {
            _storageStateLoaded = true;
            return;
        }

        EnvironmentRuntimeStorageState? state =
            await _storageStateStore.LoadAsync(cancellationToken)
                .ConfigureAwait(false);
        if (state is null)
        {
            _storageStateLoaded = true;
            return;
        }
        if (!string.Equals(
                state.Schema,
                EnvironmentRuntimeStorageState.CurrentSchema,
                StringComparison.Ordinal) ||
            state.Generation <= 0 ||
            state.Pools is null ||
            state.Volumes is null ||
            state.Reservations is null ||
            state.Backups is null ||
            state.Restores is null)
            throw new InvalidOperationException(
                "Environment.Storage.AuthoritativeStateInvalid: the runtime storage authority has an unsupported or malformed schema.");

        foreach (var persisted in state.Pools
                     .OrderBy(
                         static value => value.Spec.StorageClass))
        {
            string key = persisted.Spec.StorageClass.ToString();
            if (_storagePools.ContainsKey(key))
                throw new InvalidOperationException(
                    "Environment.Storage.AuthoritativeStateInvalid: duplicate storage-pool authority exists.");
            ProviderId? providerId =
                persisted.Status.ProviderHandle?.ProviderId ??
                persisted.Spec.PreferredProvider;
            IStoragePoolProvider provider = SelectStorageProvider(
                registry.StoragePoolProviders,
                providerId,
                "storage pool");
            StoragePoolStatus status = await provider.RecoverAsync(
                    persisted.Metadata,
                    persisted.Spec,
                    persisted.Status,
                    cancellationToken)
                .ConfigureAwait(false);
            _storagePools[key] = persisted with { Status = status };
            AdvanceRuntimeGeneration(persisted.Metadata.Generation.Value);
        }

        foreach (var persisted in state.Volumes
                     .OrderBy(
                         static value => value.Spec.LogicalId,
                         StringComparer.Ordinal))
        {
            if (_durableVolumes.ContainsKey(
                    persisted.Spec.LogicalId))
                throw new InvalidOperationException(
                    "Environment.Storage.AuthoritativeStateInvalid: duplicate durable-volume authority exists.");
            ResourceSnapshot<
                StoragePool,
                StoragePoolSpec,
                StoragePoolStatus> pool =
                Find(_storagePools.Values, persisted.Spec.Pool);
            ProviderId providerId =
                pool.Status.ProviderHandle?.ProviderId ??
                throw OwnershipFailure(
                    "hpd.environment.storage.provider-missing",
                    "Storage pool provider identity is missing.");
            IDurableVolumeProvider provider =
                SelectStorageProvider(
                    registry.DurableVolumeProviders,
                    providerId,
                    "durable volume");
            DurableVolumeStatus status = await provider.RecoverAsync(
                    persisted.Metadata,
                    persisted.Spec,
                    persisted.Status,
                    cancellationToken)
                .ConfigureAwait(false);
            _durableVolumes[persisted.Spec.LogicalId] =
                persisted with { Status = status };
            AdvanceRuntimeGeneration(persisted.Metadata.Generation.Value);
        }

        foreach (var persisted in state.Reservations
                     .OrderBy(
                         static value => value.Spec.OperationId,
                         StringComparer.Ordinal))
        {
            if (_storageReservations.ContainsKey(
                    persisted.Spec.OperationId))
                throw new InvalidOperationException(
                    "Environment.Storage.AuthoritativeStateInvalid: duplicate storage-reservation authority exists.");
            ResourceSnapshot<
                StoragePool,
                StoragePoolSpec,
                StoragePoolStatus> pool =
                Find(_storagePools.Values, persisted.Spec.Pool);
            ProviderId providerId =
                pool.Status.ProviderHandle?.ProviderId ??
                throw OwnershipFailure(
                    "hpd.environment.storage.provider-missing",
                    "Storage pool provider identity is missing.");
            IStorageReservationProvider provider =
                SelectStorageProvider(
                    registry.StorageReservationProviders,
                    providerId,
                    "storage reservation");
            StorageReservationStatus status =
                await provider.RecoverAsync(
                        persisted.Metadata,
                        persisted.Spec,
                        persisted.Status,
                        cancellationToken)
                    .ConfigureAwait(false);
            _storageReservations[persisted.Spec.OperationId] =
                persisted with { Status = status };
            AdvanceRuntimeGeneration(persisted.Metadata.Generation.Value);
        }
        foreach (var persisted in state.Backups
                     .OrderBy(
                         static value => value.Metadata.Id.Value,
                         StringComparer.Ordinal))
        {
            if (_volumeBackups.ContainsKey(persisted.Metadata.Id.Value))
                throw new InvalidOperationException(
                    "Environment.Storage.AuthoritativeStateInvalid: duplicate volume-backup authority exists.");
            ResourceSnapshot<
                DurableVolume,
                DurableVolumeSpec,
                DurableVolumeStatus> volume =
                Find(_durableVolumes.Values, persisted.Spec.Volume);
            _ = Find(_storageReservations.Values, persisted.Spec.Reservation);
            ProviderId providerId =
                volume.Status.ProviderHandle?.ProviderId ??
                throw OwnershipFailure(
                    "hpd.environment.storage.provider-missing",
                    "Durable volume provider identity is missing.");
            IVolumeBackupProvider provider =
                SelectStorageProvider(
                    registry.VolumeBackupProviders,
                    providerId,
                    "volume backup");
            VolumeBackupStatus status =
                await provider.RecoverAsync(
                        persisted.Metadata,
                        persisted.Spec,
                        persisted.Status,
                        cancellationToken)
                    .ConfigureAwait(false);
            _volumeBackups[persisted.Metadata.Id.Value] =
                persisted with { Status = status };
            AdvanceRuntimeGeneration(persisted.Metadata.Generation.Value);
        }
        foreach (var persisted in state.Restores
                     .OrderBy(
                         static value => value.Metadata.Id.Value,
                         StringComparer.Ordinal))
        {
            if (_volumeRestores.ContainsKey(persisted.Metadata.Id.Value))
                throw new InvalidOperationException(
                    "Environment.Storage.AuthoritativeStateInvalid: duplicate volume-restore authority exists.");
            ResourceSnapshot<
                DurableVolume,
                DurableVolumeSpec,
                DurableVolumeStatus> volume =
                Find(_durableVolumes.Values, persisted.Spec.TargetVolume);
            _ = Find(_volumeBackups.Values, persisted.Spec.Backup);
            _ = Find(_storageReservations.Values, persisted.Spec.Reservation);
            ProviderId providerId =
                volume.Status.ProviderHandle?.ProviderId ??
                throw OwnershipFailure(
                    "hpd.environment.storage.provider-missing",
                    "Durable volume provider identity is missing.");
            IVolumeRestoreProvider provider =
                SelectStorageProvider(
                    registry.VolumeRestoreProviders,
                    providerId,
                    "volume restore");
            VolumeRestoreStatus status =
                await provider.RecoverAsync(
                        persisted.Metadata,
                        persisted.Spec,
                        persisted.Status,
                        cancellationToken)
                    .ConfigureAwait(false);
            _volumeRestores[persisted.Metadata.Id.Value] =
                persisted with { Status = status };
            ResourceSnapshot<
                DurableVolume,
                DurableVolumeSpec,
                DurableVolumeStatus> refreshedVolume =
                Find(
                    _durableVolumes.Values,
                    persisted.Spec.TargetVolume);
            _durableVolumes[refreshedVolume.Spec.LogicalId] =
                refreshedVolume with
                {
                    Status = await ProviderById(
                            registry.DurableVolumeProviders,
                            providerId,
                            "durable volume")
                        .GetStatusAsync(
                            persisted.Spec.TargetVolume,
                            cancellationToken)
                        .ConfigureAwait(false),
                };
            AdvanceRuntimeGeneration(persisted.Metadata.Generation.Value);
        }
        _storageStateGeneration = state.Generation;
        _storageStateLoaded = true;
        await PersistStorageStateAsync(CancellationToken.None)
            .ConfigureAwait(false);
        foreach (var restore in _volumeRestores.Values)
        {
            ProviderId providerId =
                Find(
                    _durableVolumes.Values,
                    restore.Spec.TargetVolume)
                .Status.ProviderHandle?.ProviderId ??
                throw OwnershipFailure(
                    "hpd.environment.storage.provider-missing",
                    "Durable volume provider identity is missing.");
            await ProviderById(
                    registry.VolumeRestoreProviders,
                    providerId,
                    "volume restore")
                .FinalizeAsync(
                    new ResourceRef<VolumeRestore>(
                        restore.Metadata.Id,
                        restore.Metadata.Scope,
                        restore.Metadata.Generation),
                    CancellationToken.None)
                .ConfigureAwait(false);
        }
    }

    private async ValueTask PersistStorageStateAsync(
        CancellationToken cancellationToken)
    {
        if (_storageStateStore is null)
            return;
        long generation = checked(_storageStateGeneration + 1);
        var state = new EnvironmentRuntimeStorageState(
            EnvironmentRuntimeStorageState.CurrentSchema,
            generation,
            _storagePools.Values
                .OrderBy(
                    static value => value.Spec.StorageClass)
                .ToArray(),
            _durableVolumes.Values
                .OrderBy(
                    static value => value.Spec.LogicalId,
                    StringComparer.Ordinal)
                .ToArray(),
            _storageReservations.Values
                .OrderBy(
                    static value => value.Spec.OperationId,
                    StringComparer.Ordinal)
                .ToArray(),
            _volumeBackups.Values
                .OrderBy(
                    static value => value.Metadata.Id.Value,
                    StringComparer.Ordinal)
                .ToArray(),
            _volumeRestores.Values
                .OrderBy(
                    static value => value.Metadata.Id.Value,
                    StringComparer.Ordinal)
                .ToArray());
        await _storageStateStore.SaveAsync(state, cancellationToken)
            .ConfigureAwait(false);
        _storageStateGeneration = generation;
    }

    private void AdvanceRuntimeGeneration(long observed)
    {
        long current;
        do
        {
            current = Volatile.Read(ref _generation);
            if (current >= observed)
                return;
        }
        while (Interlocked.CompareExchange(
                   ref _generation,
                   observed,
                   current) != current);
    }

    private static TProvider SelectStorageProvider<TProvider>(
        IReadOnlyList<TProvider> providers,
        ProviderId? preferred,
        string family)
        where TProvider : IStorageProvider
    {
        if (preferred is { } providerId)
        {
            return providers.SingleOrDefault(provider => provider.ProviderId.Equals(providerId))
                ?? throw new InvalidOperationException(
                    $"{family} provider '{providerId.Value}' is not registered.");
        }
        if (providers.Count == 1)
            return providers[0];
        throw new InvalidOperationException(
            $"A preferred {family} provider is required when {providers.Count} providers are registered.");
    }

    private static void ValidateBackupSourceEvidence(
        ResourceSnapshot<
            DurableVolume,
            DurableVolumeSpec,
            DurableVolumeStatus> source,
        VolumeBackupSpec backup)
    {
        ProviderOpaqueHandle provider = source.Status.ProviderHandle ??
            throw OwnershipFailure(
                "hpd.environment.storage.provider-missing",
                "Durable volume provider identity is missing.");
        var authoritativeRef = new ResourceRef<DurableVolume>(
            source.Metadata.Id,
            source.Metadata.Scope,
            source.Metadata.Generation);
        if (backup.SourceVolumeResource != authoritativeRef ||
            backup.SourceVolumeGeneration !=
                source.Status.VolumeGeneration ||
            backup.SourceProviderId != provider.ProviderId ||
            backup.SourceProviderGeneration != provider.Generation ||
            backup.SourceProviderRealizationGeneration !=
                source.Status.ProviderRealizationGeneration ||
            !SameDurableVolumeSpec(
                backup.SourceVolumeSpec,
                source.Spec))
            throw OwnershipFailure(
                "hpd.environment.storage.backup-source-mismatch",
                "Backup source identity, generation, provider, or immutable volume declaration does not match authoritative ownership.");
    }

    private static bool SameDurableVolumeSpec(
        DurableVolumeSpec left,
        DurableVolumeSpec right) =>
        string.Equals(left.LogicalId, right.LogicalId,
            StringComparison.Ordinal) &&
        string.Equals(left.OwnerScopeId, right.OwnerScopeId,
            StringComparison.Ordinal) &&
        string.Equals(left.OwnerResourceId, right.OwnerResourceId,
            StringComparison.Ordinal) &&
        string.Equals(left.DeclarationId, right.DeclarationId,
            StringComparison.Ordinal) &&
        left.Pool == right.Pool &&
        left.MinimumBytes == right.MinimumBytes &&
        left.MaximumBytes == right.MaximumBytes &&
        left.Retention == right.Retention &&
        left.BackupEligible == right.BackupEligible &&
        left.Filesystem == right.Filesystem &&
        left.Encryption == right.Encryption &&
        string.Equals(left.CompatibilityDomain,
            right.CompatibilityDomain, StringComparison.Ordinal) &&
        string.Equals(left.Sensitivity, right.Sensitivity,
            StringComparison.Ordinal) &&
        SameProviderExtensions(
            left.ProviderExtensions,
            right.ProviderExtensions);

    private static bool SameProviderExtensions(
        IReadOnlyList<ProviderExtensionData> left,
        IReadOnlyList<ProviderExtensionData> right) =>
        left.Count == right.Count &&
        left.Zip(right).All(static pair =>
            pair.First.ProviderId == pair.Second.ProviderId &&
            pair.First.SchemaId == pair.Second.SchemaId &&
            pair.First.ContentType == pair.Second.ContentType &&
            pair.First.Payload.Span.SequenceEqual(
                pair.Second.Payload.Span));

    private static ResourceSnapshot<TResource, TSpec, TStatus> Find<TResource, TSpec, TStatus>(
        IEnumerable<ResourceSnapshot<TResource, TSpec, TStatus>> snapshots,
        ResourceRef<TResource> reference)
        where TResource : IExecutionResourceMarker
        where TSpec : notnull
        where TStatus : ResourceStatus =>
        snapshots.SingleOrDefault(item =>
            item.Metadata.Id.Equals(reference.Id) &&
            item.Metadata.Scope.Equals(reference.Scope) &&
            (reference.Generation is null ||
             item.Metadata.Generation.Equals(reference.Generation.Value)))
        ?? throw new KeyNotFoundException($"{typeof(TResource).Name} '{reference.Id.Value}' was not found at the requested generation.");
}
