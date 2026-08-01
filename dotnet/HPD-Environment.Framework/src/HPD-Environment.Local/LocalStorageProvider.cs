namespace HPD.Environment.Local;

using HPD.Environment.Contracts;
using HPD.Environment.Runtime;

internal sealed class LocalStorageProvider :
    IStoragePoolProvider,
    IDurableVolumeProvider,
    IStorageReservationProvider,
    IVolumeBackupProvider,
    IVolumeRestoreProvider,
    IDisposable
{
    private readonly LocalProviderState _state;
    private readonly string _root;
    private readonly string? _engineDataRoot;
    private readonly IStorageBackupKeyProvider? _backupKeys;
    private readonly LocalVolumeIdentityStore _volumeIdentities;
    private readonly LocalRestoreOperationStore _restoreOperations;
    private readonly ILocalDurableVolumeBackend _volumeBackend;
    private readonly object _gate = new();
    private readonly Dictionary<string, (string PoolId, long Bytes)> _reservations =
        new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _volumePaths =
        new(StringComparer.Ordinal);

    private static readonly ProviderResourceShape PoolShape = Shape<StoragePool>(
        "storage-pool", TargetRouteSegmentKind.StoragePool);
    private static readonly ProviderResourceShape VolumeShape = Shape<DurableVolume>(
        "durable-volume", TargetRouteSegmentKind.DurableVolume);
    private static readonly ProviderResourceShape ReservationShape = Shape<StorageReservation>(
        "storage-reservation", TargetRouteSegmentKind.StorageReservation);
    private static readonly ProviderResourceShape BackupShape = Shape<VolumeBackup>(
        "volume-backup", TargetRouteSegmentKind.VolumeBackup);
    private static readonly ProviderResourceShape RestoreShape = Shape<VolumeRestore>(
        "volume-restore", TargetRouteSegmentKind.VolumeRestore);

    public LocalStorageProvider(LocalProviderState state)
    {
        _state = state;
        _backupKeys = state.Options.BackupKeyProvider;
        _root = ProviderStateDirectory.EnsurePrivateRoot(
            state.Options.StorageRoot ??
            Path.Combine(
                state.Options.WorkloadStateRoot ??
                Path.Combine(
                    System.Environment.GetFolderPath(
                        System.Environment.SpecialFolder.LocalApplicationData),
                    "HPD-Environment",
                    "local"),
                "storage"),
            "LocalEnvironment.StorageRootInvalid");
        _engineDataRoot = string.IsNullOrWhiteSpace(
                state.Options.EngineDataRootPath)
            ? null
            : Path.GetFullPath(state.Options.EngineDataRootPath);
        _ = ProviderStateDirectory.EnsurePrivateRoot(
            BackupsRoot,
            "LocalEnvironment.StorageRootInvalid");
        _volumeIdentities = new LocalVolumeIdentityStore(_root);
        _restoreOperations = new LocalRestoreOperationStore(_root);
        _volumeBackend = LocalDurableVolumeBackend.Create(
            state.Options,
            _root);
        _state.RegisterStorageRelease(
            _volumeBackend.ReleaseAll);
    }

    public ProviderId ProviderId =>
        LocalEnvironmentProviderDescriptor.ProviderId;

    private string BackupsRoot => Path.Combine(_root, "backups");

    public void Dispose()
    {
        _volumeBackend.Dispose();
    }

    public ValueTask<StoragePoolStatus> EnsureAsync(
        ResourceMetadata<StoragePool> metadata,
        StoragePoolSpec spec,
        StoragePoolStatus? observed,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        StoragePoolStatus status;
        lock (_gate)
            status = MeasurePool(metadata, spec);
        status = Store(metadata, spec, status, PoolShape);
        return ValueTask.FromResult(status);
    }

    public ValueTask<StoragePoolStatus> GetStatusAsync(
        ResourceRef<StoragePool> pool,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var entry = Get<StoragePool, StoragePoolSpec, StoragePoolStatus>(pool);
        StoragePoolStatus status;
        lock (_gate)
            status = MeasurePool(Metadata(entry.Resource), entry.Spec);
        status = Store(Metadata(entry.Resource), entry.Spec, status, PoolShape);
        return ValueTask.FromResult(status);
    }

    public ValueTask<StoragePoolStatus> RecoverAsync(
        ResourceMetadata<StoragePool> metadata,
        StoragePoolSpec spec,
        StoragePoolStatus persisted,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(persisted);
        StoragePoolStatus measured;
        lock (_gate)
            measured = MeasurePool(metadata, spec);
        return ValueTask.FromResult(
            Store(metadata, spec, measured, PoolShape));
    }

    public async ValueTask ExportAsync(
        ResourceRef<VolumeBackup> backup,
        Stream destination,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(destination);
        if (!destination.CanWrite)
            throw new ArgumentException(
                "Backup destination must be writable.",
                nameof(destination));
        var entry = Get<VolumeBackup, VolumeBackupSpec, VolumeBackupStatus>(backup);
        if (entry.Status.BackupPhase != VolumeBackupPhase.Ready ||
            entry.Status.StoredBytes is not { } expected)
            throw new InvalidOperationException(
                "Environment.Storage.BackupInvalid: only a verified ready backup may be exported.");
        await using var source = new FileStream(
            BackupPath(backup.Id.Value),
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            bufferSize: 64 * 1024,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        if (source.Length != expected.Value)
            throw new InvalidOperationException(
                "Environment.Storage.BackupInvalid: backup size changed before export.");
        await source.CopyToAsync(destination, 64 * 1024, cancellationToken)
            .ConfigureAwait(false);
    }

    public async ValueTask<VolumeBackupStatus> ImportAsync(
        ResourceMetadata<VolumeBackup> metadata,
        VolumeBackupSpec spec,
        VolumeBackupStatus expectedStatus,
        Stream source,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (_backupKeys is null)
            throw new InvalidOperationException(
                "LocalEnvironment.BackupEncryptionAuthorityRequired: backup import requires a configured platform credential resolver.");
        if (expectedStatus.StoredBytes is not { Value: > 0 } stored ||
            expectedStatus.LogicalBytes is not { Value: >= 0 } logical ||
            expectedStatus.ContentDigest is null ||
            expectedStatus.CapturedAt is null)
            throw new InvalidOperationException(
                "Environment.Storage.BackupInvalid: imported backup evidence is incomplete.");
        RequireReservation(spec.Reservation, stored.Value);
        string destination = BackupPath(metadata.Id.Value);
        string staging = destination + ".import-" + Guid.NewGuid().ToString("N");
        try
        {
            await CopyExactImportAsync(source, staging, stored.Value, cancellationToken)
                .ConfigureAwait(false);
            using StorageBackupKeyMaterial key = await _backupKeys.ResolveAsync(
                    spec.EncryptionCredential,
                    metadata.Scope,
                    "volume-backup-import",
                    cancellationToken)
                .ConfigureAwait(false);
            PortableVolumeBackupManifest manifest = PortableVolumeBackupArchive.Validate(
                staging,
                key,
                spec.SourceVolumeSpec.MaximumBytes.Value,
                cancellationToken);
            ValidateImportedManifest(spec, expectedStatus, logical.Value, manifest);
            File.Move(staging, destination);
            var status = expectedStatus with
            {
                Phase = ResourcePhase.Ready,
                BackupPhase = VolumeBackupPhase.Ready,
                ReconciliationOutcome = ResourceReconciliationOutcome.Accepted,
                ObservedGeneration = metadata.Generation,
            };
            return Store(metadata, spec, status, BackupShape);
        }
        catch
        {
            if (File.Exists(staging))
                File.Delete(staging);
            throw;
        }
    }

    public ValueTask DeleteAsync(
        ResourceRef<StoragePool> pool,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
        {
            bool hasVolumes = _volumeIdentities.Any();
            bool hasReservations = _reservations.Values.Any(value =>
                value.PoolId == pool.Id.Value);
            if (hasVolumes || hasReservations)
                throw new InvalidOperationException(
                    "LocalEnvironment.StoragePoolNotEmpty: storage pools cannot be deleted while volumes or reservations remain.");
            _state.Ledger.Remove<StoragePool, StoragePoolSpec, StoragePoolStatus>(pool);
        }
        return ValueTask.CompletedTask;
    }

    public ValueTask<DurableVolumeStatus> EnsureAsync(
        ResourceMetadata<DurableVolume> metadata,
        DurableVolumeSpec spec,
        DurableVolumeStatus? observed,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ValidateComponent(spec.LogicalId, nameof(spec.LogicalId));
        Get<StoragePool, StoragePoolSpec, StoragePoolStatus>(spec.Pool);
        string path;
        LocalVolumeIdentity identity;
        lock (_gate)
        {
            bool existed = _volumeBackend.Exists(
                spec.LogicalId);
            if (_volumeIdentities.Exists(spec.LogicalId))
            {
                identity = _volumeIdentities.ReadAndValidate(
                    metadata,
                    spec,
                    observed?.VolumeGeneration ?? metadata.Generation,
                    observed?.FilesystemIdentity);
                path = _volumeBackend.OpenExisting(
                    spec.LogicalId,
                    spec.MaximumBytes.Value,
                    identity.FilesystemIdentity);
            }
            else if (existed)
            {
                throw new InvalidOperationException(
                    "Environment.Storage.LegacyLayoutRejected: a Local durable-volume directory exists without the required physical identity record.");
            }
            else
            {
                identity = _volumeIdentities.Create(metadata, spec);
                try
                {
                    path = _volumeBackend.Create(
                        spec.LogicalId,
                        spec.MaximumBytes.Value,
                        identity.FilesystemIdentity);
                }
                catch
                {
                    _volumeIdentities.Delete(spec.LogicalId);
                    throw;
                }
            }
            _volumePaths[spec.LogicalId] = path;
        }
        long physicalAllocatedBytes =
            _volumeBackend.MeasurePhysicalAllocatedBytes(
                spec.LogicalId);
        var status = new DurableVolumeStatus
        {
            Phase = ResourcePhase.Ready,
            VolumePhase = DurableVolumePhase.Ready,
            ReconciliationOutcome =
                ResourceReconciliationOutcome.Accepted,
            ObservedGeneration = metadata.Generation,
            VolumeGeneration =
                new ResourceGeneration(identity.VolumeGeneration),
            ProviderRealizationGeneration =
                _state.Ledger.ProviderGeneration,
            LogicalCapacityBytes = spec.MaximumBytes,
            PhysicalAllocatedBytes =
                new ByteSize(physicalAllocatedBytes),
            UsedBytes = new ByteSize(DirectoryBytes(path)),
            FilesystemIdentity = identity.FilesystemIdentity,
            Integrity = VolumeIntegrityState.Clean,
        };
        status = Store(metadata, spec, status, VolumeShape);
        status = status with
        {
            Realization = new DurableVolumeRealization
            {
                EffectiveRuntimePath = path,
                ProviderHandle = status.ProviderHandle ??
                    throw new InvalidOperationException(
                        "Local durable volume has no provider handle."),
                Generation =
                    new ResourceGeneration(identity.VolumeGeneration),
            },
        };
        status = Store(metadata, spec, status, VolumeShape);
        return ValueTask.FromResult(status);
    }

    public ValueTask<DurableVolumeStatus> GetStatusAsync(
        ResourceRef<DurableVolume> volume,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var entry = Get<DurableVolume, DurableVolumeSpec, DurableVolumeStatus>(volume);
        string path = VolumePath(entry.Spec.LogicalId);
        _ = _volumeIdentities.ReadAndValidate(
            Metadata(entry.Resource),
            entry.Spec,
            entry.Status.VolumeGeneration,
            entry.Status.FilesystemIdentity);
        long usedBytes = Directory.Exists(path)
            ? DirectoryBytes(path)
            : 0;
        bool overMaximum =
            usedBytes > entry.Spec.MaximumBytes.Value;
        long physicalAllocatedBytes =
            _volumeBackend.MeasurePhysicalAllocatedBytes(
                entry.Spec.LogicalId);
        DurableVolumeStatus status = entry.Status with
        {
            Phase = Directory.Exists(path)
                ? overMaximum
                    ? ResourcePhase.Degraded
                    : ResourcePhase.Ready
                : ResourcePhase.Failed,
            VolumePhase = Directory.Exists(path)
                ? overMaximum
                    ? DurableVolumePhase.FailedRetained
                    : entry.Status.VolumePhase
                : DurableVolumePhase.FailedRetained,
            PhysicalAllocatedBytes =
                new ByteSize(physicalAllocatedBytes),
            UsedBytes = new ByteSize(usedBytes),
            Diagnostics = overMaximum
                ?
                [
                    new Diagnostic
                    {
                        Code = new DiagnosticCode(
                            "Environment.Storage.AppVolumeLow"),
                        Severity = DiagnosticSeverity.Error,
                        Message =
                            "Durable-volume usage exceeds its declared maximum. New mutation remains blocked until data is exported, erased, or an explicit growth operation succeeds.",
                    },
                ]
                : entry.Status.Diagnostics,
        };
        status = Store(Metadata(entry.Resource), entry.Spec, status, VolumeShape);
        return ValueTask.FromResult(status);
    }

    public ValueTask<DurableVolumeStatus> RecoverAsync(
        ResourceMetadata<DurableVolume> metadata,
        DurableVolumeSpec spec,
        DurableVolumeStatus persisted,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(persisted);
        ValidateComponent(spec.LogicalId, nameof(spec.LogicalId));
        _ = Get<StoragePool, StoragePoolSpec, StoragePoolStatus>(
            spec.Pool);
        IReadOnlyList<LocalRestoreOperation> pendingRestores =
            _restoreOperations.FindForVolume(spec.LogicalId);
        if (pendingRestores.Count > 1)
            throw new InvalidOperationException(
                "Environment.Storage.RestoreIncomplete: multiple restore journals claim one Local durable volume.");
        LocalVolumeIdentity identity = pendingRestores.Count == 0
            ? _volumeIdentities.ReadAndValidate(
                metadata,
                spec,
                persisted.VolumeGeneration,
                persisted.FilesystemIdentity)
            : _volumeIdentities.ReadForPendingRestore(
                metadata,
                spec,
                new ResourceGeneration(
                    pendingRestores[0]
                        .PreviousVolumeGeneration),
                new ResourceGeneration(
                    pendingRestores[0]
                        .RestoredVolumeGeneration),
                persisted.FilesystemIdentity);
        string path = _volumeBackend.OpenExisting(
            spec.LogicalId,
            spec.MaximumBytes.Value,
            identity.FilesystemIdentity);
        lock (_gate)
            _volumePaths[spec.LogicalId] = path;
        long physicalAllocatedBytes =
            _volumeBackend.MeasurePhysicalAllocatedBytes(
                spec.LogicalId);
        long usedBytes = DirectoryBytes(path);
        bool overMaximum =
            usedBytes > spec.MaximumBytes.Value;
        DurableVolumeStatus recovered = Store(
            metadata,
            spec,
            persisted with
            {
                Phase = overMaximum
                    ? ResourcePhase.Degraded
                    : ResourcePhase.Ready,
                VolumePhase = overMaximum
                    ? DurableVolumePhase.FailedRetained
                    : persisted.VolumePhase,
                PhysicalAllocatedBytes =
                    new ByteSize(physicalAllocatedBytes),
                UsedBytes = new ByteSize(usedBytes),
                VolumeGeneration = new ResourceGeneration(
                    identity.VolumeGeneration),
                Integrity = pendingRestores.Count != 0
                    ? VolumeIntegrityState.CheckRequired
                    : persisted.Integrity is
                    VolumeIntegrityState.Unknown
                        ? VolumeIntegrityState.CheckRequired
                        : persisted.Integrity,
                Diagnostics = overMaximum
                    ?
                    [
                        new Diagnostic
                        {
                            Code = new DiagnosticCode(
                                "Environment.Storage.AppVolumeLow"),
                            Severity = DiagnosticSeverity.Error,
                            Message =
                                "Recovered durable-volume usage exceeds its declared maximum.",
                        },
                    ]
                    : persisted.Diagnostics,
                FilesystemIdentity = identity.FilesystemIdentity,
            },
            VolumeShape);
        recovered = recovered with
        {
            Realization = new DurableVolumeRealization
            {
                EffectiveRuntimePath = path,
                ProviderHandle = recovered.ProviderHandle ??
                    throw new InvalidOperationException(
                        "Recovered Local durable volume has no provider handle."),
                Generation = new ResourceGeneration(
                    identity.VolumeGeneration),
            },
        };
        return ValueTask.FromResult(
            Store(metadata, spec, recovered, VolumeShape));
    }

    public ValueTask<DurableVolumeStatus> DetachAsync(
        ResourceRef<DurableVolume> volume,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var entry = Get<DurableVolume, DurableVolumeSpec, DurableVolumeStatus>(volume);
        DurableVolumeStatus status = entry.Status with
        {
            VolumePhase = DurableVolumePhase.DetachedRetained,
            LastCleanUnmountAt = DateTimeOffset.UtcNow,
        };
        status = Store(Metadata(entry.Resource), entry.Spec, status, VolumeShape);
        return ValueTask.FromResult(status);
    }

    public ValueTask EraseAsync(
        ResourceRef<DurableVolume> volume,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var entry = Get<DurableVolume, DurableVolumeSpec, DurableVolumeStatus>(volume);
        lock (_gate)
        {
            _volumeBackend.Erase(
                entry.Spec.LogicalId,
                entry.Status.FilesystemIdentity ??
                throw new InvalidOperationException(
                    "Environment.Storage.IntegrityCheckRequired: the Local durable volume has no accepted filesystem identity."));
            _volumePaths.Remove(entry.Spec.LogicalId);
            _volumeIdentities.Delete(entry.Spec.LogicalId);
            _state.Ledger.Remove<DurableVolume, DurableVolumeSpec, DurableVolumeStatus>(volume);
        }
        return ValueTask.CompletedTask;
    }

    public ValueTask<StorageReservationStatus> ReserveAsync(
        ResourceMetadata<StorageReservation> metadata,
        StorageReservationSpec spec,
        StorageReservationStatus? observed,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (observed is not null)
            throw new InvalidOperationException(
                "LocalEnvironment.StorageReservationObservedStateInvalid: reservation recovery must use the non-mutating recovery contract.");
        var pool = Get<StoragePool, StoragePoolSpec, StoragePoolStatus>(spec.Pool);
        long granted = checked((long)Math.Ceiling(
            Math.Max(
                spec.RequestedBytes.Value,
                spec.EstimatedBytes?.Value ?? 0) *
            spec.SafetyMultiplier));
        lock (_gate)
        {
            StoragePoolStatus measured = MeasurePool(Metadata(pool.Resource), pool.Spec);
            long available = measured.AvailableBytes?.Value ?? 0;
            if (granted <= 0 ||
                spec.ExpiresAt <= DateTimeOffset.UtcNow)
                throw new InvalidOperationException(
                    "LocalEnvironment.StorageReservationInvalid: reservation size and expiry must be valid.");
            if (available - granted < pool.Spec.MinimumFreeBytes.Value)
                throw new InvalidOperationException(
                    "Environment.Storage.AdmissionDenied: the requested reservation would cross the pool admission watermark.");
            _reservations[metadata.Id.Value] =
                (spec.Pool.Id.Value, granted);
        }
        var status = new StorageReservationStatus
        {
            Phase = ResourcePhase.Ready,
            ReservationPhase = StorageReservationPhase.Reserved,
            ReconciliationOutcome =
                ResourceReconciliationOutcome.Accepted,
            ObservedGeneration = metadata.Generation,
            GrantedBytes = new ByteSize(granted),
            ReservedAt = DateTimeOffset.UtcNow,
        };
        status = Store(metadata, spec, status, ReservationShape);
        return ValueTask.FromResult(status);
    }

    public ValueTask<StorageReservationStatus> RecoverAsync(
        ResourceMetadata<StorageReservation> metadata,
        StorageReservationSpec spec,
        StorageReservationStatus persisted,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(persisted);
        _ = Get<StoragePool, StoragePoolSpec, StoragePoolStatus>(
            spec.Pool);
        long granted = persisted.GrantedBytes.Value;
        if (granted <= 0)
            throw new InvalidOperationException(
                "LocalEnvironment.StorageReservationInvalid: persisted reservation size must be positive.");
        bool expired = spec.ExpiresAt <= DateTimeOffset.UtcNow;
        lock (_gate)
            _reservations[metadata.Id.Value] =
                (spec.Pool.Id.Value, granted);
        StorageReservationStatus recovered = persisted with
        {
            Phase = expired
                ? ResourcePhase.Degraded
                : ResourcePhase.Ready,
            ReservationPhase = expired
                ? StorageReservationPhase.Ambiguous
                : StorageReservationPhase.Reserved,
            Diagnostics = expired
                ?
                [
                    new Diagnostic
                    {
                        Code = new DiagnosticCode(
                            "Environment.Storage.ReservationExpiredAmbiguous"),
                        Severity = DiagnosticSeverity.Error,
                        Message =
                            "The reservation expired across runtime reconstruction; its bytes remain fenced until operation activity is disproven.",
                    },
                ]
                : persisted.Diagnostics,
        };
        return ValueTask.FromResult(
            Store(metadata, spec, recovered, ReservationShape));
    }

    public ValueTask<StorageReservationStatus> GetStatusAsync(
        ResourceRef<StorageReservation> reservation,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromResult(
            Get<StorageReservation, StorageReservationSpec, StorageReservationStatus>(reservation).Status);
    }

    public ValueTask ReleaseAsync(
        ResourceRef<StorageReservation> reservation,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
        {
            _reservations.Remove(reservation.Id.Value);
            _state.Ledger.Remove<StorageReservation, StorageReservationSpec, StorageReservationStatus>(reservation);
        }
        return ValueTask.CompletedTask;
    }

    public async ValueTask<VolumeBackupStatus> CaptureAsync(
        ResourceMetadata<VolumeBackup> metadata,
        VolumeBackupSpec spec,
        VolumeBackupStatus? observed,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (observed is not null)
            throw new InvalidOperationException(
                "LocalEnvironment.BackupCaptureObservedStateInvalid: backup recovery must use the non-mutating recovery contract.");
        if (spec.Encryption != StorageEncryptionRequirement.Required)
            throw new InvalidOperationException(
                "LocalEnvironment.BackupEncryptionRequired: portable durable-volume backups must use authenticated encryption.");
        if (_backupKeys is null)
            throw new InvalidOperationException(
                "LocalEnvironment.BackupEncryptionAuthorityRequired: backup encryption requires a configured platform credential resolver.");
        var volume = Get<DurableVolume, DurableVolumeSpec, DurableVolumeStatus>(spec.Volume);
        if (!volume.Spec.BackupEligible)
            throw new InvalidOperationException(
                "LocalEnvironment.BackupNotEligible: the durable volume does not permit backup.");
        long sourceBytes = DirectoryBytes(
            VolumePath(volume.Spec.LogicalId));
        if (sourceBytes > volume.Spec.MaximumBytes.Value)
            throw new InvalidOperationException(
                "Environment.Storage.AppVolumeLow: durable-volume usage exceeds its declared maximum.");
        RequireReservation(
            spec.Reservation,
            minimumBytes: sourceBytes);
        string source = VolumePath(volume.Spec.LogicalId);
        string destination = BackupPath(metadata.Id.Value);
        using StorageBackupKeyMaterial key =
            await _backupKeys.ResolveAsync(
                spec.EncryptionCredential,
                metadata.Scope,
                "volume-backup-capture",
                cancellationToken).ConfigureAwait(false);
        PortableVolumeBackupManifest manifest;
        lock (_gate)
        {
            if (File.Exists(destination))
                throw new InvalidOperationException(
                    "LocalEnvironment.BackupIdentityConflict: immutable backup content already exists.");
            manifest = PortableVolumeBackupArchive.Capture(
                source,
                destination,
                new PortableVolumeBackupManifest
                {
                    BackupId = metadata.Id.Value,
                    OwnerTypeId = spec.OwnerTypeId,
                    OwnerScopeId =
                        spec.OwnerScopeId,
                    OwnerVersion =
                        spec.OwnerVersion,
                    CompatibilityDomain =
                        spec.CompatibilityDomain,
                    LogicalVolumeId =
                        volume.Spec.LogicalId,
                    VolumeGeneration =
                        (ulong)volume.Status
                            .VolumeGeneration.Value,
                    ProviderId = ProviderId.Value,
                    Consistency = spec.Consistency,
                    CreatedAt = DateTimeOffset.UtcNow,
                    LogicalBytes = 0,
                    EntryCount = 0,
                    ContentSha256 = "pending",
                    EncryptionKeyId = "pending",
                },
                key,
                volume.Spec.MaximumBytes.Value,
                cancellationToken);
        }
        var status = new VolumeBackupStatus
        {
            Phase = ResourcePhase.Ready,
            BackupPhase = VolumeBackupPhase.Ready,
            ReconciliationOutcome =
                ResourceReconciliationOutcome.Accepted,
            ObservedGeneration = metadata.Generation,
            ContentDigest = new Digest(
                "sha256",
                manifest.ContentSha256),
            LogicalBytes =
                new ByteSize(manifest.LogicalBytes),
            StoredBytes = new ByteSize(
                new FileInfo(destination).Length),
            CapturedAt = manifest.CreatedAt,
        };
        status = Store(metadata, spec, status, BackupShape);
        return status;
    }

    public ValueTask<VolumeBackupStatus> GetStatusAsync(
        ResourceRef<VolumeBackup> backup,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromResult(
            Get<VolumeBackup, VolumeBackupSpec, VolumeBackupStatus>(backup).Status);
    }

    public async ValueTask<VolumeBackupStatus> RecoverAsync(
        ResourceMetadata<VolumeBackup> metadata,
        VolumeBackupSpec spec,
        VolumeBackupStatus persisted,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var volume =
            Get<DurableVolume, DurableVolumeSpec, DurableVolumeStatus>(
                spec.Volume);
        _ = Get<
            StorageReservation,
            StorageReservationSpec,
            StorageReservationStatus>(spec.Reservation);
        string destination = BackupPath(metadata.Id.Value);
        if (!File.Exists(destination))
        {
            DeleteBackupStaging(metadata.Id.Value);
            if (persisted.BackupPhase is
                VolumeBackupPhase.Ready or
                VolumeBackupPhase.Verifying)
                throw new InvalidOperationException(
                    "Environment.Storage.BackupInvalid: authoritative backup content is missing during recovery.");
            return Store(
                metadata,
                spec,
                persisted with
                {
                    Phase = ResourcePhase.Degraded,
                    BackupPhase =
                        VolumeBackupPhase.FailedRetained,
                },
                BackupShape);
        }
        if (_backupKeys is null)
            throw new InvalidOperationException(
                "LocalEnvironment.BackupEncryptionAuthorityRequired: backup recovery requires a configured platform credential resolver.");
        using StorageBackupKeyMaterial key =
            await _backupKeys.ResolveAsync(
                spec.EncryptionCredential,
                metadata.Scope,
                "volume-backup-recovery",
                cancellationToken).ConfigureAwait(false);
        PortableVolumeBackupManifest manifest =
            PortableVolumeBackupArchive.Validate(
                destination,
                key,
                volume.Spec.MaximumBytes.Value,
                cancellationToken);
        if (!ManifestMatches(
                manifest,
                metadata.Id.Value,
                spec))
            throw new InvalidOperationException(
                "Environment.Storage.BackupInvalid: authoritative backup content failed recovery validation.");
        if (persisted.BackupPhase == VolumeBackupPhase.Ready &&
            (!string.Equals(
                 manifest.ContentSha256,
                 persisted.ContentDigest?.Value,
                 StringComparison.Ordinal) ||
             manifest.LogicalBytes !=
                 persisted.LogicalBytes?.Value))
            throw new InvalidOperationException(
                "Environment.Storage.BackupInvalid: authoritative backup evidence differs from the immutable artifact.");
        DeleteBackupStaging(metadata.Id.Value);
        return Store(
            metadata,
            spec,
            persisted with
            {
                Phase = ResourcePhase.Ready,
                BackupPhase = VolumeBackupPhase.Ready,
                ContentDigest = new Digest(
                    "sha256",
                    manifest.ContentSha256),
                LogicalBytes =
                    new ByteSize(manifest.LogicalBytes),
                StoredBytes = new ByteSize(
                    new FileInfo(destination).Length),
                CapturedAt = manifest.CreatedAt,
            },
            BackupShape);
    }

    public ValueTask DeleteAsync(
        ResourceRef<VolumeBackup> backup,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
        {
            string path = BackupPath(backup.Id.Value);
            if (File.Exists(path))
                File.Delete(path);
            _state.Ledger.Remove<VolumeBackup, VolumeBackupSpec, VolumeBackupStatus>(backup);
        }
        return ValueTask.CompletedTask;
    }

    public async ValueTask<VolumeRestoreStatus> RestoreAsync(
        ResourceMetadata<VolumeRestore> metadata,
        VolumeRestoreSpec spec,
        VolumeRestoreStatus? observed,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (observed is not null)
            throw new InvalidOperationException(
                "LocalEnvironment.RestoreObservedStateInvalid: restore recovery must use the non-mutating recovery contract.");
        var backup = Get<VolumeBackup, VolumeBackupSpec, VolumeBackupStatus>(spec.Backup);
        var target = Get<DurableVolume, DurableVolumeSpec, DurableVolumeStatus>(spec.TargetVolume);
        long backupBytes =
            backup.Status.LogicalBytes?.Value ??
            throw new InvalidOperationException(
                "Environment.Storage.BackupInvalid: backup size evidence is missing.");
        if (backupBytes > target.Spec.MaximumBytes.Value)
            throw new InvalidOperationException(
                "Environment.Storage.AdmissionDenied: backup content exceeds the target durable-volume maximum.");
        long currentBytes = DirectoryBytes(
            VolumePath(target.Spec.LogicalId));
        RequireReservation(
            spec.Reservation,
            minimumBytes: checked(backupBytes + currentBytes));
        if (!string.Equals(
                backup.Spec.CompatibilityDomain,
                spec.ExpectedCompatibilityDomain,
                StringComparison.Ordinal) ||
            !string.Equals(
                target.Spec.CompatibilityDomain,
                spec.ExpectedCompatibilityDomain,
                StringComparison.Ordinal))
            throw new InvalidOperationException(
                "Environment.Storage.RestoreCompatibilityMismatch: backup and target compatibility identities differ.");
        if (_backupKeys is null)
            throw new InvalidOperationException(
                "LocalEnvironment.BackupEncryptionAuthorityRequired: restore requires a configured platform credential resolver.");

        string source = BackupPath(spec.Backup.Id.Value);
        string targetPath = VolumePath(target.Spec.LogicalId);
        string staging = targetPath + ".restore-" + metadata.Id.Value;
        string previous = targetPath + ".previous-" + metadata.Id.Value;
        string expectedDigest =
            backup.Status.ContentDigest?.Value ??
            throw new InvalidOperationException(
                "Environment.Storage.BackupInvalid: backup digest evidence is missing.");
        ResourceGeneration restoredGeneration =
            new(checked(
                target.Status.VolumeGeneration.Value + 1));
        var operation = new LocalRestoreOperation(
            metadata.Id.Value,
            metadata.Scope.Value,
            metadata.Generation.Value,
            spec.Backup.Id.Value,
            spec.Backup.Scope.Value,
            spec.Backup.Generation?.Value ?? 0,
            spec.TargetVolume.Id.Value,
            spec.TargetVolume.Scope.Value,
            spec.TargetVolume.Generation?.Value ?? 0,
            target.Spec.LogicalId,
            target.Status.VolumeGeneration.Value,
            restoredGeneration.Value,
            expectedDigest,
            spec.PreservePreviousGenerationUntilVerified,
            LocalRestoreCheckpoint.Staging);
        using StorageBackupKeyMaterial key =
            await _backupKeys.ResolveAsync(
                backup.Spec.EncryptionCredential,
                metadata.Scope,
                "volume-backup-restore",
                cancellationToken).ConfigureAwait(false);
        PortableVolumeBackupManifest manifest;
        bool selected = false;
        lock (_gate)
        {
            if (Directory.Exists(staging) ||
                Directory.Exists(previous))
                throw new InvalidOperationException(
                    "Environment.Storage.RestoreIncomplete: restore staging already exists for a new operation identity.");
            _restoreOperations.Write(operation);
            try
            {
                manifest =
                    PortableVolumeBackupArchive.RestoreToStaging(
                        source,
                        staging,
                        key,
                        target.Spec.MaximumBytes.Value,
                        cancellationToken);
                if (!string.Equals(
                        manifest.ContentSha256,
                        expectedDigest,
                        StringComparison.Ordinal) ||
                    !ManifestMatches(
                        manifest,
                        spec.Backup.Id.Value,
                        backup.Spec) ||
                    !string.Equals(
                        manifest.LogicalVolumeId,
                        target.Spec.LogicalId,
                        StringComparison.Ordinal))
                    throw new InvalidOperationException(
                        "Environment.Storage.BackupInvalid: restored content digest does not match the backup manifest.");
                operation = operation with
                {
                    Checkpoint = LocalRestoreCheckpoint.Staged,
                };
                _restoreOperations.Write(operation);
                cancellationToken.ThrowIfCancellationRequested();
                if (Directory.Exists(targetPath))
                    Directory.Move(targetPath, previous);
                operation = operation with
                {
                    Checkpoint =
                        LocalRestoreCheckpoint.PreviousMoved,
                };
                _restoreOperations.Write(operation);
                Directory.Move(staging, targetPath);
                selected = true;
                operation = operation with
                {
                    Checkpoint = LocalRestoreCheckpoint.Selected,
                };
                _restoreOperations.Write(operation);
            }
            catch
            {
                if (selected && Directory.Exists(targetPath))
                    Directory.Delete(targetPath, recursive: true);
                if (!Directory.Exists(targetPath) &&
                    Directory.Exists(previous))
                    Directory.Move(previous, targetPath);
                if (Directory.Exists(staging))
                    Directory.Delete(staging, recursive: true);
                _restoreOperations.Delete(metadata.Id.Value);
                throw;
            }
        }
        PortableVolumeContentEvidence selectedEvidence =
            PortableVolumeBackupArchive.MeasureContent(
                targetPath,
                target.Spec.MaximumBytes.Value,
                CancellationToken.None);
        if (!string.Equals(
                selectedEvidence.Sha256,
                expectedDigest,
                StringComparison.Ordinal))
        {
            lock (_gate)
            {
                if (Directory.Exists(targetPath))
                    Directory.Delete(targetPath, recursive: true);
                if (Directory.Exists(previous))
                    Directory.Move(previous, targetPath);
                _restoreOperations.Delete(metadata.Id.Value);
            }
            throw new InvalidOperationException(
                "Environment.Storage.RestoreIncomplete: selected Local restore content failed post-selection verification.");
        }
        LocalVolumeIdentity restoredIdentity =
            _volumeIdentities.AdvanceGeneration(
                Metadata(target.Resource),
                target.Spec,
                target.Status.VolumeGeneration,
                restoredGeneration,
                target.Status.FilesystemIdentity);
        operation = operation with
        {
            Checkpoint =
                LocalRestoreCheckpoint.IdentityAdvanced,
        };
        _restoreOperations.Write(operation);
        _ = Store(
            Metadata(target.Resource),
            target.Spec,
            target.Status with
            {
                VolumeGeneration = restoredGeneration,
                UsedBytes =
                    new ByteSize(manifest.LogicalBytes),
                Integrity = VolumeIntegrityState.Clean,
                VolumePhase = target.Status.VolumePhase,
                LastCleanUnmountAt = null,
                FilesystemIdentity =
                    restoredIdentity.FilesystemIdentity,
            },
            VolumeShape);
        var status = new VolumeRestoreStatus
        {
            Phase = ResourcePhase.Ready,
            RestorePhase = VolumeRestorePhase.Ready,
            ReconciliationOutcome =
                ResourceReconciliationOutcome.Accepted,
            ObservedGeneration = metadata.Generation,
            PreviousVolumeGeneration =
                target.Status.VolumeGeneration,
            RestoredVolumeGeneration = restoredGeneration,
            VerifiedDigest = backup.Status.ContentDigest,
            RestoredAt = DateTimeOffset.UtcNow,
        };
        status = Store(metadata, spec, status, RestoreShape);
        _restoreOperations.Write(operation with
        {
            Checkpoint = LocalRestoreCheckpoint.Verified,
        });
        return status;
    }

    public ValueTask<VolumeRestoreStatus> GetStatusAsync(
        ResourceRef<VolumeRestore> restore,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromResult(
            Get<VolumeRestore, VolumeRestoreSpec, VolumeRestoreStatus>(restore).Status);
    }

    public ValueTask<VolumeRestoreStatus> RecoverAsync(
        ResourceMetadata<VolumeRestore> metadata,
        VolumeRestoreSpec spec,
        VolumeRestoreStatus persisted,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var backup =
            Get<VolumeBackup, VolumeBackupSpec, VolumeBackupStatus>(
                spec.Backup);
        var target =
            Get<DurableVolume, DurableVolumeSpec, DurableVolumeStatus>(
                spec.TargetVolume);
        _ = Get<
            StorageReservation,
            StorageReservationSpec,
            StorageReservationStatus>(spec.Reservation);
        if (!string.Equals(
                backup.Spec.CompatibilityDomain,
                spec.ExpectedCompatibilityDomain,
                StringComparison.Ordinal) ||
            !string.Equals(
                target.Spec.CompatibilityDomain,
                spec.ExpectedCompatibilityDomain,
                StringComparison.Ordinal))
            throw new InvalidOperationException(
                "Environment.Storage.RestoreCompatibilityMismatch: backup and target compatibility identities differ.");
        ResourceGeneration previousGeneration =
            persisted.PreviousVolumeGeneration ??
            throw new InvalidOperationException(
                "Environment.Storage.RestoreIncomplete: authoritative restore state is missing its pre-selection volume generation.");
        string expectedDigest =
            backup.Status.ContentDigest?.Value ??
            throw new InvalidOperationException(
                "Environment.Storage.BackupInvalid: backup digest evidence is missing.");
        string targetPath = VolumePath(target.Spec.LogicalId);
        if (!_restoreOperations.Exists(metadata.Id.Value))
        {
            if (persisted.RestorePhase != VolumeRestorePhase.Ready ||
                !Directory.Exists(targetPath))
                return ValueTask.FromResult(
                    Store(
                        metadata,
                        spec,
                        persisted with
                        {
                            Phase = ResourcePhase.Degraded,
                            RestorePhase =
                                VolumeRestorePhase.FailedRetained,
                        },
                        RestoreShape));
            PortableVolumeContentEvidence completedEvidence =
                PortableVolumeBackupArchive.MeasureContent(
                    targetPath,
                    target.Spec.MaximumBytes.Value,
                    cancellationToken);
            if (!string.Equals(
                    completedEvidence.Sha256,
                    persisted.VerifiedDigest?.Value,
                    StringComparison.Ordinal))
                throw new InvalidOperationException(
                    "Environment.Storage.RestoreIncomplete: finalized restored content failed recovery validation.");
            return ValueTask.FromResult(
                Store(metadata, spec, persisted, RestoreShape));
        }

        LocalRestoreOperation operation =
            _restoreOperations.ReadAndValidate(
                metadata,
                spec,
                target.Spec.LogicalId,
                expectedDigest,
                previousGeneration);
        string staging =
            targetPath + ".restore-" + metadata.Id.Value;
        string previous =
            targetPath + ".previous-" + metadata.Id.Value;
        lock (_gate)
        {
            bool targetExists = Directory.Exists(targetPath);
            bool previousExists = Directory.Exists(previous);
            if (operation.Checkpoint <
                LocalRestoreCheckpoint.Selected)
            {
                if (targetExists && previousExists)
                {
                    PortableVolumeContentEvidence uncertain =
                        PortableVolumeBackupArchive.MeasureContent(
                            targetPath,
                            target.Spec.MaximumBytes.Value,
                            CancellationToken.None);
                    if (string.Equals(
                            uncertain.Sha256,
                            expectedDigest,
                            StringComparison.Ordinal))
                    {
                        operation = operation with
                        {
                            Checkpoint =
                                LocalRestoreCheckpoint.Selected,
                        };
                        _restoreOperations.Write(operation);
                    }
                    else
                    {
                        Directory.Delete(
                            targetPath,
                            recursive: true);
                        Directory.Move(previous, targetPath);
                        DeleteDirectoryIfPresent(staging);
                        _restoreOperations.Delete(
                            metadata.Id.Value);
                        return ValueTask.FromResult(
                            Store(
                                metadata,
                                spec,
                                persisted with
                                {
                                    Phase = ResourcePhase.Degraded,
                                    RestorePhase =
                                        VolumeRestorePhase
                                            .FailedRetained,
                                },
                                RestoreShape));
                    }
                }
                else
                {
                    if (!targetExists && previousExists)
                        Directory.Move(previous, targetPath);
                    DeleteDirectoryIfPresent(staging);
                    _restoreOperations.Delete(
                        metadata.Id.Value);
                    return ValueTask.FromResult(
                        Store(
                            metadata,
                            spec,
                            persisted with
                            {
                                Phase = ResourcePhase.Degraded,
                                RestorePhase =
                                    VolumeRestorePhase
                                        .FailedRetained,
                            },
                            RestoreShape));
                }
            }

            if (!Directory.Exists(targetPath) ||
                !Directory.Exists(previous))
                throw new InvalidOperationException(
                    "Environment.Storage.RestoreIncomplete: selected Local restore content or its retained previous generation is missing.");
            PortableVolumeContentEvidence evidence =
                PortableVolumeBackupArchive.MeasureContent(
                    targetPath,
                    target.Spec.MaximumBytes.Value,
                    CancellationToken.None);
            if (!string.Equals(
                    evidence.Sha256,
                    expectedDigest,
                    StringComparison.Ordinal))
                throw new InvalidOperationException(
                    "Environment.Storage.RestoreIncomplete: selected restored content failed recovery validation.");

            ResourceGeneration restoredGeneration =
                new(operation.RestoredVolumeGeneration);
            LocalVolumeIdentity identity =
                _volumeIdentities.ReadForPendingRestore(
                    Metadata(target.Resource),
                    target.Spec,
                    previousGeneration,
                    restoredGeneration,
                    target.Status.FilesystemIdentity);
            if (identity.VolumeGeneration ==
                previousGeneration.Value)
                identity = _volumeIdentities.AdvanceGeneration(
                    Metadata(target.Resource),
                    target.Spec,
                    previousGeneration,
                    restoredGeneration,
                    target.Status.FilesystemIdentity);
            operation = operation with
            {
                Checkpoint =
                    LocalRestoreCheckpoint.IdentityAdvanced,
            };
            _restoreOperations.Write(operation);
            _ = Store(
                Metadata(target.Resource),
                target.Spec,
                target.Status with
                {
                    VolumeGeneration = restoredGeneration,
                    UsedBytes = new ByteSize(
                        evidence.LogicalBytes),
                    Integrity = VolumeIntegrityState.Clean,
                    FilesystemIdentity =
                        identity.FilesystemIdentity,
                    LastCleanUnmountAt = null,
                },
                VolumeShape);
            _restoreOperations.Write(operation with
            {
                Checkpoint = LocalRestoreCheckpoint.Verified,
            });
            return ValueTask.FromResult(
                Store(
                    metadata,
                    spec,
                    persisted with
                    {
                        Phase = ResourcePhase.Ready,
                        RestorePhase = VolumeRestorePhase.Ready,
                        PreviousVolumeGeneration =
                            previousGeneration,
                        RestoredVolumeGeneration =
                            restoredGeneration,
                        VerifiedDigest =
                            backup.Status.ContentDigest,
                        RestoredAt =
                            persisted.RestoredAt ??
                            DateTimeOffset.UtcNow,
                    },
                    RestoreShape));
        }
    }

    public ValueTask FinalizeAsync(
        ResourceRef<VolumeRestore> restore,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var entry = Get<
            VolumeRestore,
            VolumeRestoreSpec,
            VolumeRestoreStatus>(restore);
        if (entry.Status.RestorePhase !=
            VolumeRestorePhase.Ready)
            return ValueTask.CompletedTask;
        var target = Get<
            DurableVolume,
            DurableVolumeSpec,
            DurableVolumeStatus>(entry.Spec.TargetVolume);
        string targetPath = VolumePath(target.Spec.LogicalId);
        lock (_gate)
        {
            DeleteDirectoryIfPresent(
                targetPath + ".restore-" + restore.Id.Value);
            DeleteDirectoryIfPresent(
                targetPath + ".previous-" + restore.Id.Value);
            _restoreOperations.Delete(restore.Id.Value);
        }
        return ValueTask.CompletedTask;
    }

    private StoragePoolStatus MeasurePool(
        ResourceMetadata<StoragePool> metadata,
        StoragePoolSpec spec)
    {
        if (spec.StorageClass == StorageClass.RuntimeDisposable &&
            _engineDataRoot is null)
        {
            return new StoragePoolStatus
            {
                Phase = ResourcePhase.Degraded,
                PoolPhase = StoragePoolPhase.AdmissionStopped,
                ReconciliationOutcome =
                    ResourceReconciliationOutcome.Accepted,
                ObservedGeneration = metadata.Generation,
                AvailableBytes = null,
                ReservedBytes = new ByteSize(
                    _reservations.Values
                        .Where(value =>
                            value.PoolId == metadata.Id.Value)
                        .Sum(static value => value.Bytes)),
                MeasurementConfidence =
                    StorageMeasurementConfidence.Unknown,
                MeasuredAt = DateTimeOffset.UtcNow,
                Conditions =
                [
                    new Condition(
                        "Environment.Storage.EngineDataRootUnknown",
                        ConditionStatus.True,
                        "ProviderCapacityUnavailable",
                        "The selected Local engine does not expose authoritative remaining data-root capacity through the Docker Engine API; image growth is denied until a provider-specific capacity observer is configured.",
                        DateTimeOffset.UtcNow,
                        metadata.Generation,
                        DiagnosticSeverity.Error),
                ],
            };
        }
        string measuredRoot = spec.StorageClass ==
                StorageClass.RuntimeDisposable
            ? _engineDataRoot!
            : _root;
        if (!Directory.Exists(measuredRoot) ||
            new DirectoryInfo(measuredRoot).LinkTarget is not null)
            throw new InvalidOperationException(
                "LocalEnvironment.EngineDataRootInvalid: the configured storage measurement root must be an existing non-symbolic-link directory.");
        DriveInfo drive = new(Path.GetPathRoot(measuredRoot)!);
        long reserved = _reservations.Values
            .Where(value => value.PoolId == metadata.Id.Value)
            .Sum(static value => value.Bytes);
        long available = Math.Max(0, drive.AvailableFreeSpace - reserved);
        StoragePoolPhase phase =
            available <= spec.EmergencyFreeBytes.Value
                ? StoragePoolPhase.Emergency
                : available <= spec.MinimumFreeBytes.Value
                    ? StoragePoolPhase.AdmissionStopped
                    : available <= spec.WarningFreeBytes.Value
                        ? StoragePoolPhase.Warning
                        : StoragePoolPhase.Ready;
        DirectoryAllocation allocation =
            MeasureDirectoryAllocation(measuredRoot);
        return new StoragePoolStatus
        {
            Phase = phase is StoragePoolPhase.Ready or StoragePoolPhase.Warning
                ? ResourcePhase.Ready
                : ResourcePhase.Degraded,
            PoolPhase = phase,
            ReconciliationOutcome =
                ResourceReconciliationOutcome.Accepted,
            ObservedGeneration = metadata.Generation,
            LogicalCapacityBytes = new ByteSize(drive.TotalSize),
            PhysicalAllocatedBytes = new ByteSize(allocation.Bytes),
            AvailableBytes = new ByteSize(available),
            ReservedBytes = new ByteSize(reserved),
            MeasurementConfidence = allocation.Confidence,
            MeasuredAt = DateTimeOffset.UtcNow,
            Conditions = PoolConditions(
                phase,
                metadata.Generation,
                allocation.Confidence,
                spec.StorageClass),
        };
    }

    private static IReadOnlyList<Condition> PoolConditions(
        StoragePoolPhase phase,
        ResourceGeneration generation,
        StorageMeasurementConfidence confidence,
        StorageClass storageClass)
    {
        var conditions = new List<Condition>(2);
        if (phase is StoragePoolPhase.Warning or
            StoragePoolPhase.AdmissionStopped or
            StoragePoolPhase.Emergency)
        {
            conditions.Add(new Condition(
                storageClass == StorageClass.RuntimeDisposable
                    ? "Environment.Storage.EngineDataRootLow"
                    : "Environment.Storage.HostCapacityLow",
                ConditionStatus.True,
                phase.ToString(),
                storageClass == StorageClass.RuntimeDisposable
                    ? "The Local engine data-root capacity crossed its configured storage watermark."
                    : "Host capacity crossed the configured Local storage watermark.",
                DateTimeOffset.UtcNow,
                generation,
                phase == StoragePoolPhase.Warning
                    ? DiagnosticSeverity.Warning
                    : DiagnosticSeverity.Error));
        }
        if (confidence is StorageMeasurementConfidence.Estimated or
            StorageMeasurementConfidence.Unknown)
        {
            conditions.Add(new Condition(
                "Environment.Storage.SparseAllocationUnknown",
                ConditionStatus.True,
                confidence.ToString(),
                "Physical allocation is not known exactly on this Local host.",
                DateTimeOffset.UtcNow,
                generation,
                DiagnosticSeverity.Warning));
        }
        return conditions;
    }

    private void RequireReservation(
        ResourceRef<StorageReservation> reservation,
        long minimumBytes = 1)
    {
        var entry = Get<StorageReservation, StorageReservationSpec, StorageReservationStatus>(reservation);
        lock (_gate)
        {
            if (!_reservations.ContainsKey(reservation.Id.Value) ||
                entry.Status.ReservationPhase != StorageReservationPhase.Reserved ||
                entry.Spec.ExpiresAt <= DateTimeOffset.UtcNow ||
                entry.Status.GrantedBytes.Value < minimumBytes)
                throw new InvalidOperationException(
                    "Environment.Storage.ReservationExpiredAmbiguous: an active and sufficiently sized reservation is required.");
        }
    }

    private ProviderResourceEntry<TResource, TSpec, TStatus> Get<TResource, TSpec, TStatus>(
        ResourceRef<TResource> resource)
        where TResource : IExecutionResourceMarker, IOperationTargetMarker
        where TSpec : notnull
        where TStatus : ResourceStatus
    {
        var lookup = _state.Ledger.TryGet<TResource, TSpec, TStatus>(resource);
        if (!lookup.Succeeded)
            throw new InvalidOperationException(
                $"{lookup.Diagnostic!.Code.Value}: {lookup.Diagnostic.Message}");
        return lookup.Entry!;
    }

    private TStatus Store<TResource, TSpec, TStatus>(
        ResourceMetadata<TResource> metadata,
        TSpec spec,
        TStatus status,
        ProviderResourceShape shape)
        where TResource : IExecutionResourceMarker, IOperationTargetMarker
        where TSpec : notnull
        where TStatus : ResourceStatus
    {
        var entry = _state.Ledger.Upsert(metadata, spec, status, shape);
        TStatus completed = status switch
        {
            StoragePoolStatus value =>
                (TStatus)(ResourceStatus)(value with
                {
                    ProviderHandle = entry.ProviderHandle,
                }),
            DurableVolumeStatus value =>
                (TStatus)(ResourceStatus)(value with
                {
                    ProviderHandle = entry.ProviderHandle,
                }),
            StorageReservationStatus value =>
                (TStatus)(ResourceStatus)(value with
                {
                    ProviderHandle = entry.ProviderHandle,
                }),
            VolumeBackupStatus value =>
                (TStatus)(ResourceStatus)(value with
                {
                    ProviderHandle = entry.ProviderHandle,
                }),
            VolumeRestoreStatus value =>
                (TStatus)(ResourceStatus)(value with
                {
                    ProviderHandle = entry.ProviderHandle,
                }),
            _ => status,
        };
        _state.Ledger.Upsert(metadata, spec, completed, shape);
        return completed;
    }

    private static ProviderResourceShape Shape<TResource>(
        string kind,
        TargetRouteSegmentKind segment)
        where TResource : IExecutionResourceMarker, IOperationTargetMarker =>
        new(
            new TargetKind(kind),
            segment,
            TargetHandleLifetime.DurableAddress,
            TargetHandleAuthority.Observe |
                TargetHandleAuthority.Read |
                TargetHandleAuthority.Control,
            new SchemaId($"hpd.execution.local.{kind}.handle.v1"));

    private static ResourceMetadata<TResource> Metadata<TResource>(
        ResourceRef<TResource> resource)
        where TResource : IExecutionResourceMarker =>
        new()
        {
            Id = resource.Id,
            Kind = new ResourceKind(typeof(TResource).Name),
            Scope = resource.Scope,
            Generation = resource.Generation ?? new ResourceGeneration(1),
            SchemaVersion = new SchemaVersion("1"),
        };

    private string VolumePath(string logicalId)
    {
        lock (_gate)
        {
            if (_volumePaths.TryGetValue(
                    logicalId,
                    out string? path))
                return path;
        }
        throw new InvalidOperationException(
            "Environment.Storage.IntegrityCheckRequired: the Local durable volume has no active provider realization.");
    }

    private string BackupPath(string backupId)
    {
        ValidateComponent(backupId, nameof(backupId));
        return Path.Combine(
            BackupsRoot,
            backupId + ".hpdbackup");
    }

    private void DeleteBackupStaging(string backupId)
    {
        ValidateComponent(backupId, nameof(backupId));
        string prefix = "." + backupId + ".hpdbackup.";
        foreach (string path in Directory.EnumerateFiles(
                     BackupsRoot,
                     prefix + "*.tmp",
                     SearchOption.TopDirectoryOnly))
            File.Delete(path);
    }

    private static void DeleteDirectoryIfPresent(string path)
    {
        if (Directory.Exists(path))
            Directory.Delete(path, recursive: true);
    }

    private static bool ManifestMatches(
        PortableVolumeBackupManifest manifest,
        string backupId,
        VolumeBackupSpec spec) =>
        string.Equals(
            manifest.BackupId,
            backupId,
            StringComparison.Ordinal) &&
        string.Equals(
            manifest.OwnerTypeId,
            spec.OwnerTypeId,
            StringComparison.Ordinal) &&
        string.Equals(
            manifest.OwnerScopeId,
            spec.OwnerScopeId,
            StringComparison.Ordinal) &&
        string.Equals(
            manifest.OwnerVersion,
            spec.OwnerVersion,
            StringComparison.Ordinal) &&
        string.Equals(
            manifest.CompatibilityDomain,
            spec.CompatibilityDomain,
            StringComparison.Ordinal);

    private static void ValidateComponent(string value, string name)
    {
        if (string.IsNullOrWhiteSpace(value) ||
            value is "." or ".." ||
            value.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 ||
            value.Contains(Path.DirectorySeparatorChar) ||
            value.Contains(Path.AltDirectorySeparatorChar))
            throw new ArgumentException(
                "Storage identity must be a single safe path component.",
                name);
    }

    private static long DirectoryBytes(string root)
    {
        if (!Directory.Exists(root))
            return 0;
        long total = 0;
        WalkTree(
            root,
            file =>
            {
                total = checked(total + file.Length);
            });
        return total;
    }

    private static DirectoryAllocation MeasureDirectoryAllocation(string root)
    {
        if (!Directory.Exists(root))
            return new DirectoryAllocation(
                0,
                StorageMeasurementConfidence.Exact);
        if (!OperatingSystem.IsMacOS() && !OperatingSystem.IsLinux())
            return new DirectoryAllocation(
                DirectoryBytes(root),
                StorageMeasurementConfidence.Estimated);

        var start = new System.Diagnostics.ProcessStartInfo
        {
            FileName = "/usr/bin/du",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        start.ArgumentList.Add("-sk");
        start.ArgumentList.Add(root);
        using var process = new System.Diagnostics.Process
        {
            StartInfo = start,
        };
        if (!process.Start())
            throw new InvalidOperationException(
                "LocalEnvironment.StorageMeasurementFailed: allocated-byte measurement did not start.");
        string output = process.StandardOutput.ReadToEnd();
        _ = process.StandardError.ReadToEnd();
        if (!process.WaitForExit(
                (int)TimeSpan.FromSeconds(5).TotalMilliseconds))
        {
            process.Kill(entireProcessTree: true);
            throw new InvalidOperationException(
                "LocalEnvironment.StorageMeasurementFailed: allocated-byte measurement exceeded its deadline.");
        }
        string first = output.Split(
            [' ', '\t', '\r', '\n'],
            StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? "";
        if (process.ExitCode != 0 ||
            !long.TryParse(
                first,
                System.Globalization.NumberStyles.None,
                System.Globalization.CultureInfo.InvariantCulture,
                out long kibibytes) ||
            kibibytes < 0)
            throw new InvalidOperationException(
                "LocalEnvironment.StorageMeasurementFailed: allocated-byte measurement returned invalid bounded evidence.");
        return new DirectoryAllocation(
            checked(kibibytes * 1024),
            StorageMeasurementConfidence.Exact);
    }

    private readonly record struct DirectoryAllocation(
        long Bytes,
        StorageMeasurementConfidence Confidence);

    private static void WalkTree(
        string root,
        Action<FileInfo> visitFile)
    {
        var rootInfo = new DirectoryInfo(root);
        RejectLinkedEntry(rootInfo);
        Walk(rootInfo);

        void Walk(DirectoryInfo directory)
        {
            foreach (FileSystemInfo entry in directory
                         .EnumerateFileSystemInfos()
                         .OrderBy(
                             static value => value.Name,
                             StringComparer.Ordinal))
            {
                RejectLinkedEntry(entry);
                if (entry is DirectoryInfo child)
                    Walk(child);
                else if (entry is FileInfo file)
                    visitFile(file);
                else
                    throw new InvalidOperationException(
                        "Environment.Storage.IntegrityCheckRequired: durable content contains an unsupported filesystem entry.");
            }
        }
    }

    private static void RejectLinkedEntry(FileSystemInfo entry)
    {
        entry.Refresh();
        if (!entry.Exists ||
            entry.LinkTarget is not null ||
            entry.Attributes.HasFlag(
                FileAttributes.ReparsePoint))
            throw new InvalidOperationException(
                "Environment.Storage.IntegrityCheckRequired: durable content contains a symbolic link or unavailable entry.");
    }

    private static async Task CopyExactImportAsync(
        Stream source,
        string staging,
        long expectedBytes,
        CancellationToken cancellationToken)
    {
        await using var output = new FileStream(
            staging,
            FileMode.CreateNew,
            FileAccess.Write,
            FileShare.None,
            64 * 1024,
            FileOptions.Asynchronous | FileOptions.WriteThrough);
        byte[] buffer = new byte[64 * 1024];
        long remaining = expectedBytes;
        while (remaining > 0)
        {
            int read = await source.ReadAsync(
                    buffer.AsMemory(0, (int)Math.Min(buffer.Length, remaining)),
                    cancellationToken)
                .ConfigureAwait(false);
            if (read == 0)
                throw new InvalidOperationException(
                    "Environment.Storage.BackupInvalid: imported backup ended before its declared length.");
            await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken)
                .ConfigureAwait(false);
            remaining -= read;
        }
        await output.FlushAsync(cancellationToken).ConfigureAwait(false);
        output.Flush(flushToDisk: true);
    }

    private static void ValidateImportedManifest(
        VolumeBackupSpec spec,
        VolumeBackupStatus expectedStatus,
        long logicalBytes,
        PortableVolumeBackupManifest manifest)
    {
        bool matches =
            string.Equals(manifest.OwnerTypeId, spec.OwnerTypeId, StringComparison.Ordinal) &&
            string.Equals(manifest.OwnerScopeId, spec.OwnerScopeId, StringComparison.Ordinal) &&
            string.Equals(manifest.OwnerVersion, spec.OwnerVersion, StringComparison.Ordinal) &&
            string.Equals(manifest.CompatibilityDomain, spec.CompatibilityDomain, StringComparison.Ordinal) &&
            string.Equals(manifest.LogicalVolumeId, spec.SourceVolumeSpec.LogicalId, StringComparison.Ordinal) &&
            manifest.Consistency == spec.Consistency &&
            manifest.LogicalBytes == logicalBytes &&
            manifest.CreatedAt == expectedStatus.CapturedAt &&
            string.Equals(expectedStatus.ContentDigest?.Algorithm, "sha256", StringComparison.OrdinalIgnoreCase) &&
            string.Equals(manifest.ContentSha256, expectedStatus.ContentDigest?.Value, StringComparison.OrdinalIgnoreCase);
        if (!matches)
            throw new InvalidOperationException(
                "Environment.Storage.BackupInvalid: imported backup identity or integrity evidence does not match its manifest.");
    }
}
