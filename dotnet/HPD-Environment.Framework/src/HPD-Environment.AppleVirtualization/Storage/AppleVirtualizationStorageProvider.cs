namespace HPD.Environment.AppleVirtualization.Storage;

using HPD.Environment.AppleVirtualization.Protocol;
using HPD.Environment.AppleVirtualization.State;
using HPD.Environment.Contracts;
using HPD.Environment.Runtime;

internal sealed class AppleVirtualizationStorageProvider :
    IStoragePoolProvider,
    IDurableVolumeProvider,
    IStorageReservationProvider,
    IVolumeBackupProvider,
    IVolumeRestoreProvider
{
    private const int MaximumRawChunkBytes = 48 * 1024;
    private readonly AppleVirtualizationProviderStateLedger _hosts;
    private readonly IAppleVirtualizationHelperClient _helper;
    private readonly ProviderResourceLedger _resources;
    private readonly IStorageBackupKeyProvider? _backupKeys;
    private readonly string _backupsRoot;
    private readonly object _gate = new();
    private readonly Dictionary<string, (string PoolId, long Bytes)> _reservations =
        new(StringComparer.Ordinal);
    private readonly HashSet<string> _volumes =
        new(StringComparer.Ordinal);
    private long _requestSequence;

    private static readonly ProviderResourceShape PoolShape =
        Shape<StoragePool>(
            "storage-pool",
            TargetRouteSegmentKind.StoragePool);
    private static readonly ProviderResourceShape VolumeShape =
        Shape<DurableVolume>(
            "durable-volume",
            TargetRouteSegmentKind.DurableVolume);
    private static readonly ProviderResourceShape ReservationShape =
        Shape<StorageReservation>(
            "storage-reservation",
            TargetRouteSegmentKind.StorageReservation);
    private static readonly ProviderResourceShape BackupShape =
        Shape<VolumeBackup>(
            "volume-backup",
            TargetRouteSegmentKind.VolumeBackup);
    private static readonly ProviderResourceShape RestoreShape =
        Shape<VolumeRestore>(
            "volume-restore",
            TargetRouteSegmentKind.VolumeRestore);

    public AppleVirtualizationStorageProvider(
        AppleVirtualizationProviderStateLedger hosts,
        IAppleVirtualizationHelperClient helper,
        AppleVirtualizationProviderOptions? options = null)
    {
        options ??= new AppleVirtualizationProviderOptions();
        _hosts = hosts;
        _helper = helper;
        _backupKeys = options.BackupKeyProvider;
        string stateRoot = options.StateRoot ?? Path.Combine(
            System.Environment.GetFolderPath(
                System.Environment.SpecialFolder.LocalApplicationData),
            "HPD-Environment",
            "apple-virtualization");
        _backupsRoot = ProviderStateDirectory.EnsurePrivateRoot(
            options.BackupRoot ?? Path.Combine(stateRoot, "backups"),
            "AppleVirtualization.BackupRootInvalid");
        _resources = new ProviderResourceLedger(
            AppleVirtualizationProviderDescriptor.ProviderId,
            hosts.ProviderGeneration);
    }

    public ProviderId ProviderId =>
        AppleVirtualizationProviderDescriptor.ProviderId;

    public async ValueTask<StoragePoolStatus> EnsureAsync(
        ResourceMetadata<StoragePool> metadata,
        StoragePoolSpec spec,
        StoragePoolStatus? observed,
        CancellationToken cancellationToken = default)
    {
        if (spec.StorageClass is not (
                StorageClass.AppDurable or
                StorageClass.RuntimeDisposable))
            throw new InvalidOperationException(
                "AppleVirtualization.StorageClassUnsupported: Apple storage pools support only app-durable and runtime-disposable storage.");
        AppleVirtualizationStorageResponse response =
            await SendAsync(
                AppleVirtualizationStorageAction.MeasurePool,
                logicalId: null,
                maximumBytes: null,
                cancellationToken,
                storageClass: spec.StorageClass).ConfigureAwait(false);
        long reserved;
        lock (_gate)
            reserved = _reservations.Values
                .Where(value => value.PoolId == metadata.Id.Value)
                .Sum(static value => value.Bytes);
        long available = Math.Max(
            0,
            (response.AvailableBytes?.Value ?? 0) - reserved);
        StoragePoolPhase poolPhase =
            available <= spec.EmergencyFreeBytes.Value
                ? StoragePoolPhase.Emergency
                : available <= spec.MinimumFreeBytes.Value
                    ? StoragePoolPhase.AdmissionStopped
                    : available <= spec.WarningFreeBytes.Value
                        ? StoragePoolPhase.Warning
                        : StoragePoolPhase.Ready;
        return Store(
            metadata,
            spec,
            new StoragePoolStatus
            {
                Phase = poolPhase is
                    StoragePoolPhase.Ready or
                    StoragePoolPhase.Warning
                        ? ResourcePhase.Ready
                        : ResourcePhase.Degraded,
                PoolPhase = poolPhase,
                ReconciliationOutcome =
                    ResourceReconciliationOutcome.Accepted,
                ObservedGeneration = metadata.Generation,
                LogicalCapacityBytes =
                    response.LogicalCapacityBytes,
                PhysicalAllocatedBytes =
                    response.PhysicalAllocatedBytes,
                AvailableBytes = new ByteSize(available),
                ReservedBytes = new ByteSize(reserved),
                MeasurementConfidence =
                    response.MeasurementConfidence,
                MeasuredAt = DateTimeOffset.UtcNow,
                Conditions = PoolConditions(
                    response.Conditions,
                    poolPhase,
                    metadata.Generation,
                    response.MeasurementConfidence,
                    spec.StorageClass),
                Diagnostics = response.Diagnostics,
            },
            PoolShape);
    }

    public async ValueTask<StoragePoolStatus> GetStatusAsync(
        ResourceRef<StoragePool> pool,
        CancellationToken cancellationToken = default)
    {
        var entry = Get<
            StoragePool,
            StoragePoolSpec,
            StoragePoolStatus>(pool);
        return await EnsureAsync(
            Metadata(entry.Resource),
            entry.Spec,
            entry.Status,
            cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask<StoragePoolStatus> RecoverAsync(
        ResourceMetadata<StoragePool> metadata,
        StoragePoolSpec spec,
        StoragePoolStatus persisted,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(persisted);
        if (spec.StorageClass is not (
                StorageClass.AppDurable or
                StorageClass.RuntimeDisposable))
            throw new InvalidOperationException(
                "AppleVirtualization.StorageClassUnsupported: Apple storage pools support only app-durable and runtime-disposable storage.");
        AppleVirtualizationStorageResponse response =
            await SendAsync(
                AppleVirtualizationStorageAction.MeasurePool,
                logicalId: null,
                maximumBytes: null,
                cancellationToken,
                storageClass: spec.StorageClass).ConfigureAwait(false);
        long reserved;
        lock (_gate)
            reserved = _reservations.Values
                .Where(value => value.PoolId == metadata.Id.Value)
                .Sum(static value => value.Bytes);
        long available = Math.Max(
            0,
            (response.AvailableBytes?.Value ?? 0) - reserved);
        StoragePoolPhase poolPhase =
            available <= spec.EmergencyFreeBytes.Value
                ? StoragePoolPhase.Emergency
                : available <= spec.MinimumFreeBytes.Value
                    ? StoragePoolPhase.AdmissionStopped
                    : available <= spec.WarningFreeBytes.Value
                        ? StoragePoolPhase.Warning
                        : StoragePoolPhase.Ready;
        return Store(
            metadata,
            spec,
            persisted with
            {
                Phase = poolPhase is
                    StoragePoolPhase.Ready or
                    StoragePoolPhase.Warning
                        ? ResourcePhase.Ready
                        : ResourcePhase.Degraded,
                PoolPhase = poolPhase,
                LogicalCapacityBytes =
                    response.LogicalCapacityBytes,
                PhysicalAllocatedBytes =
                    response.PhysicalAllocatedBytes,
                AvailableBytes = new ByteSize(available),
                ReservedBytes = new ByteSize(reserved),
                MeasurementConfidence =
                    response.MeasurementConfidence,
                MeasuredAt = DateTimeOffset.UtcNow,
                Conditions = PoolConditions(
                    response.Conditions,
                    poolPhase,
                    metadata.Generation,
                    response.MeasurementConfidence,
                    spec.StorageClass),
                Diagnostics = response.Diagnostics,
            },
            PoolShape);
    }

    private static IReadOnlyList<Condition> PoolConditions(
        IReadOnlyList<Condition> observed,
        StoragePoolPhase phase,
        ResourceGeneration generation,
        StorageMeasurementConfidence confidence,
        StorageClass storageClass)
    {
        var conditions = new List<Condition>(observed);
        if (phase is StoragePoolPhase.Warning or
            StoragePoolPhase.AdmissionStopped or
            StoragePoolPhase.Emergency)
        {
            conditions.Add(new Condition(
                storageClass == StorageClass.RuntimeDisposable
                    ? "Environment.Storage.EngineDataRootLow"
                    : "Environment.Storage.GuestFilesystemLow",
                ConditionStatus.True,
                phase.ToString(),
                storageClass == StorageClass.RuntimeDisposable
                    ? "The Apple guest engine data root crossed its configured storage watermark."
                    : "The Apple App-data guest filesystem crossed its configured storage watermark.",
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
                "Physical allocation for the Apple App-data disk is not known exactly.",
                DateTimeOffset.UtcNow,
                generation,
                DiagnosticSeverity.Warning));
        }
        return conditions;
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
                "AppleVirtualization.BackupEncryptionAuthorityRequired: backup import requires a configured platform credential resolver.");
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
            if (_reservations.Values.Any(value =>
                    value.PoolId == pool.Id.Value))
                throw new InvalidOperationException(
                    "AppleVirtualization.StoragePoolNotEmpty: active reservations prevent pool deletion.");
            if (_volumes.Count != 0)
                throw new InvalidOperationException(
                    "AppleVirtualization.StoragePoolNotEmpty: durable volumes prevent pool deletion.");
            _resources.Remove<
                StoragePool,
                StoragePoolSpec,
                StoragePoolStatus>(pool);
        }
        return ValueTask.CompletedTask;
    }

    public async ValueTask<DurableVolumeStatus> EnsureAsync(
        ResourceMetadata<DurableVolume> metadata,
        DurableVolumeSpec spec,
        DurableVolumeStatus? observed,
        CancellationToken cancellationToken = default)
    {
        _ = Get<
            StoragePool,
            StoragePoolSpec,
            StoragePoolStatus>(spec.Pool);
        AppleVirtualizationStorageResponse response =
            await SendAsync(
                AppleVirtualizationStorageAction.EnsureVolume,
                spec.LogicalId,
                spec.MaximumBytes,
                cancellationToken,
                spec,
                metadata.Generation).ConfigureAwait(false);
        if (!response.Exists ||
            string.IsNullOrWhiteSpace(response.EffectiveRuntimePath))
            throw new InvalidOperationException(
                "AppleVirtualization.StorageVolumeMissing: guest storage did not realize the requested durable volume.");
        RequireVolumeGeneration(
            response,
            metadata.Generation,
            "volume creation");
        ValidateVolumeObservation(
            response,
            spec,
            observed?.FilesystemIdentity);
        DurableVolumeStatus status = Store(
            metadata,
            spec,
            new DurableVolumeStatus
            {
                Phase = ResourcePhase.Ready,
                VolumePhase = response.Attached
                    ? DurableVolumePhase.Attached
                    : DurableVolumePhase.Ready,
                ReconciliationOutcome =
                    ResourceReconciliationOutcome.Accepted,
                ObservedGeneration = metadata.Generation,
                VolumeGeneration = metadata.Generation,
                ProviderRealizationGeneration =
                    _hosts.ProviderGeneration,
                LogicalCapacityBytes = spec.MaximumBytes,
                PhysicalAllocatedBytes =
                    response.PhysicalAllocatedBytes,
                UsedBytes = response.UsedBytes,
                FilesystemIdentity =
                    response.FilesystemIdentity,
                Integrity = VolumeIntegrityState.Clean,
                Conditions = response.Conditions,
                Diagnostics = response.Diagnostics,
            },
            VolumeShape);
        lock (_gate)
            _volumes.Add(metadata.Id.Value);
        status = status with
        {
            Realization = new DurableVolumeRealization
            {
                EffectiveRuntimePath =
                    response.EffectiveRuntimePath,
                ProviderHandle = status.ProviderHandle ??
                    throw new InvalidOperationException(
                        "Apple durable volume has no provider handle."),
                Generation = metadata.Generation,
            },
        };
        return Store(metadata, spec, status, VolumeShape);
    }

    public async ValueTask<DurableVolumeStatus> GetStatusAsync(
        ResourceRef<DurableVolume> volume,
        CancellationToken cancellationToken = default)
    {
        var entry = Get<
            DurableVolume,
            DurableVolumeSpec,
            DurableVolumeStatus>(volume);
        AppleVirtualizationStorageResponse response =
            await SendAsync(
                AppleVirtualizationStorageAction.ObserveVolume,
                entry.Spec.LogicalId,
                entry.Spec.MaximumBytes,
                cancellationToken,
                entry.Spec,
                entry.Status.VolumeGeneration).ConfigureAwait(false);
        RequireVolumeGeneration(
            response,
            entry.Status.VolumeGeneration,
            "volume observation");
        bool identityMatches =
            HasMatchingFilesystemIdentity(
                response,
                entry.Status.FilesystemIdentity);
        bool withinQuota =
            response.UsedBytes is null ||
            response.UsedBytes.Value.Value <=
                entry.Spec.MaximumBytes.Value;
        IReadOnlyList<Diagnostic> diagnostics =
            !identityMatches
                ?
                [
                    .. response.Diagnostics,
                    new Diagnostic
                    {
                        Code = new DiagnosticCode(
                            "Environment.Storage.IntegrityCheckRequired"),
                        Severity = DiagnosticSeverity.Error,
                        Message =
                            "The observed Apple durable volume no longer has its accepted filesystem and project identity.",
                    },
                ]
                : !withinQuota
                    ?
                    [
                        .. response.Diagnostics,
                        new Diagnostic
                        {
                            Code = new DiagnosticCode(
                                "Environment.Storage.AppVolumeLow"),
                            Severity =
                                DiagnosticSeverity.Error,
                            Message =
                                "The observed Apple durable volume exceeds its accepted maximum capacity.",
                        },
                    ]
                    : response.Diagnostics;
        bool healthy =
            response.Exists &&
            identityMatches &&
            withinQuota;
        DurableVolumeStatus status = entry.Status with
        {
            Phase = healthy
                ? ResourcePhase.Ready
                : ResourcePhase.Degraded,
            VolumePhase = healthy
                ? entry.Status.VolumePhase
                : DurableVolumePhase.FailedRetained,
            PhysicalAllocatedBytes =
                response.PhysicalAllocatedBytes,
            UsedBytes = response.UsedBytes,
            Integrity = healthy
                ? entry.Status.Integrity
                : VolumeIntegrityState.CheckRequired,
            Conditions = response.Conditions,
            Diagnostics = diagnostics,
        };
        return Store(
            Metadata(entry.Resource),
            entry.Spec,
            status,
            VolumeShape);
    }

    public async ValueTask<DurableVolumeStatus> RecoverAsync(
        ResourceMetadata<DurableVolume> metadata,
        DurableVolumeSpec spec,
        DurableVolumeStatus persisted,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(persisted);
        _ = Get<
            StoragePool,
            StoragePoolSpec,
            StoragePoolStatus>(spec.Pool);
        AppleVirtualizationStorageResponse response =
            await SendAsync(
                AppleVirtualizationStorageAction.ObserveVolume,
                spec.LogicalId,
                spec.MaximumBytes,
                cancellationToken,
                spec,
                persisted.VolumeGeneration).ConfigureAwait(false);
        if (!response.Exists ||
            string.IsNullOrWhiteSpace(
                response.EffectiveRuntimePath))
            throw new InvalidOperationException(
                "Environment.Storage.IntegrityCheckRequired: authoritative Apple durable-volume content is missing during recovery.");
        ValidateVolumeObservation(
            response,
            spec,
            persisted.FilesystemIdentity);
        ResourceGeneration recoveredGeneration =
            response.VolumeGeneration is null
                ? throw new InvalidOperationException(
                    "Environment.Storage.IntegrityCheckRequired: Apple durable-volume recovery returned no generation evidence.")
                : new ResourceGeneration(
                    checked((long)response.VolumeGeneration.Value));
        if (recoveredGeneration.Value !=
                persisted.VolumeGeneration.Value &&
            recoveredGeneration.Value !=
                checked(persisted.VolumeGeneration.Value + 1))
            throw new InvalidOperationException(
                "Environment.Storage.IntegrityCheckRequired: Apple durable-volume recovery observed an unjournaled generation.");
        DurableVolumeStatus recovered = Store(
            metadata,
            spec,
            persisted with
            {
                Phase = ResourcePhase.Ready,
                VolumePhase = response.Attached
                    ? DurableVolumePhase.Attached
                    : DurableVolumePhase.Ready,
                VolumeGeneration = recoveredGeneration,
                PhysicalAllocatedBytes =
                    response.PhysicalAllocatedBytes,
                UsedBytes = response.UsedBytes,
                FilesystemIdentity =
                    response.FilesystemIdentity,
                Integrity = persisted.Integrity is
                    VolumeIntegrityState.Unknown
                        ? VolumeIntegrityState.CheckRequired
                        : persisted.Integrity,
                Conditions = response.Conditions,
                Diagnostics = response.Diagnostics,
            },
            VolumeShape);
        lock (_gate)
            _volumes.Add(metadata.Id.Value);
        recovered = recovered with
        {
            Realization = new DurableVolumeRealization
            {
                EffectiveRuntimePath =
                    response.EffectiveRuntimePath,
                ProviderHandle = recovered.ProviderHandle ??
                    throw new InvalidOperationException(
                        "Recovered Apple durable volume has no provider handle."),
                Generation = recoveredGeneration,
            },
        };
        return Store(
            metadata,
            spec,
            recovered,
            VolumeShape);
    }

    public async ValueTask<DurableVolumeStatus> DetachAsync(
        ResourceRef<DurableVolume> volume,
        CancellationToken cancellationToken = default)
    {
        var entry = Get<
            DurableVolume,
            DurableVolumeSpec,
            DurableVolumeStatus>(volume);
        _ = await SendAsync(
            AppleVirtualizationStorageAction.DetachVolume,
            entry.Spec.LogicalId,
            entry.Spec.MaximumBytes,
            cancellationToken,
            entry.Spec,
            entry.Status.VolumeGeneration).ConfigureAwait(false);
        return Store(
            Metadata(entry.Resource),
            entry.Spec,
            entry.Status with
            {
                VolumePhase =
                    DurableVolumePhase.DetachedRetained,
                LastCleanUnmountAt = DateTimeOffset.UtcNow,
            },
            VolumeShape);
    }

    public async ValueTask EraseAsync(
        ResourceRef<DurableVolume> volume,
        CancellationToken cancellationToken = default)
    {
        var entry = Get<
            DurableVolume,
            DurableVolumeSpec,
            DurableVolumeStatus>(volume);
        AppleVirtualizationStorageResponse response =
            await SendAsync(
                AppleVirtualizationStorageAction.EraseVolume,
                entry.Spec.LogicalId,
                entry.Spec.MaximumBytes,
                cancellationToken,
                entry.Spec,
                entry.Status.VolumeGeneration).ConfigureAwait(false);
        if (response.Exists)
            throw new InvalidOperationException(
                "AppleVirtualization.StorageEraseUnproven: guest storage still observes the volume after erase.");
        lock (_gate)
            _volumes.Remove(volume.Id.Value);
        _resources.Remove<
            DurableVolume,
            DurableVolumeSpec,
            DurableVolumeStatus>(volume);
    }

    public async ValueTask<StorageReservationStatus> ReserveAsync(
        ResourceMetadata<StorageReservation> metadata,
        StorageReservationSpec spec,
        StorageReservationStatus? observed,
        CancellationToken cancellationToken = default)
    {
        if (observed is not null)
            throw new InvalidOperationException(
                "AppleVirtualization.StorageReservationObservedStateInvalid: reservation recovery must use the non-mutating recovery contract.");
        var pool = Get<
            StoragePool,
            StoragePoolSpec,
            StoragePoolStatus>(spec.Pool);
        long granted = checked((long)Math.Ceiling(
            Math.Max(
                spec.RequestedBytes.Value,
                spec.EstimatedBytes?.Value ?? 0) *
            spec.SafetyMultiplier));
        StoragePoolStatus measured =
            await GetStatusAsync(
                spec.Pool,
                cancellationToken).ConfigureAwait(false);
        lock (_gate)
        {
            if (_reservations.ContainsKey(metadata.Id.Value))
                throw new InvalidOperationException(
                    "AppleVirtualization.StorageReservationDuplicate: the reservation identity is already active.");
            long physicalAvailable = checked(
                (measured.AvailableBytes?.Value ?? 0) +
                measured.ReservedBytes.Value);
            long available = Math.Max(
                0,
                physicalAvailable - _reservations.Values
                    .Where(value => value.PoolId == spec.Pool.Id.Value)
                    .Sum(static value => value.Bytes));
            if (granted <= 0 ||
                spec.ExpiresAt <= DateTimeOffset.UtcNow)
                throw new InvalidOperationException(
                    "AppleVirtualization.StorageReservationInvalid: size and expiry must be valid.");
            if (available - granted <
                pool.Spec.MinimumFreeBytes.Value)
                throw new InvalidOperationException(
                    "Environment.Storage.AdmissionDenied: the reservation would cross the guest App-data admission watermark.");
            _reservations[metadata.Id.Value] =
                (spec.Pool.Id.Value, granted);
        }
        return Store(
            metadata,
            spec,
            new StorageReservationStatus
            {
                Phase = ResourcePhase.Ready,
                ReservationPhase = StorageReservationPhase.Reserved,
                ReconciliationOutcome =
                    ResourceReconciliationOutcome.Accepted,
                ObservedGeneration = metadata.Generation,
                GrantedBytes = new ByteSize(granted),
                ReservedAt = DateTimeOffset.UtcNow,
            },
            ReservationShape);
    }

    public ValueTask<StorageReservationStatus> RecoverAsync(
        ResourceMetadata<StorageReservation> metadata,
        StorageReservationSpec spec,
        StorageReservationStatus persisted,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(persisted);
        _ = Get<
            StoragePool,
            StoragePoolSpec,
            StoragePoolStatus>(spec.Pool);
        long granted = persisted.GrantedBytes.Value;
        if (granted <= 0)
            throw new InvalidOperationException(
                "AppleVirtualization.StorageReservationInvalid: persisted reservation size must be positive.");
        bool expired = spec.ExpiresAt <= DateTimeOffset.UtcNow;
        lock (_gate)
        {
            if (_reservations.ContainsKey(metadata.Id.Value))
                throw new InvalidOperationException(
                    "AppleVirtualization.StorageReservationDuplicate: the recovered reservation identity is already active.");
            _reservations[metadata.Id.Value] =
                (spec.Pool.Id.Value, granted);
        }
        return ValueTask.FromResult(
            Store(
                metadata,
                spec,
                persisted with
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
                },
                ReservationShape));
    }

    public ValueTask<StorageReservationStatus> GetStatusAsync(
        ResourceRef<StorageReservation> reservation,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromResult(
            Get<
                StorageReservation,
                StorageReservationSpec,
                StorageReservationStatus>(reservation).Status);
    }

    public ValueTask ReleaseAsync(
        ResourceRef<StorageReservation> reservation,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
        {
            _reservations.Remove(reservation.Id.Value);
            _resources.Remove<
                StorageReservation,
                StorageReservationSpec,
                StorageReservationStatus>(reservation);
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
                "AppleVirtualization.BackupCaptureObservedStateInvalid: backup recovery must use the non-mutating recovery contract.");
        if (spec.Encryption != StorageEncryptionRequirement.Required ||
            _backupKeys is null)
            throw new InvalidOperationException(
                "AppleVirtualization.BackupEncryptionAuthorityRequired: portable durable-volume backups require authenticated encryption and a platform credential resolver.");
        var volume = Get<DurableVolume, DurableVolumeSpec, DurableVolumeStatus>(spec.Volume);
        if (!volume.Spec.BackupEligible)
            throw new InvalidOperationException(
                "AppleVirtualization.BackupNotEligible: the durable volume does not permit backup.");
        RequireReservation(spec.Reservation, 1);
        string operationId = metadata.Id.Value;
        AppleVirtualizationStorageResponse begin = await SendAsync(
            AppleVirtualizationStorageAction.BeginBackup,
            volume.Spec.LogicalId,
            volume.Spec.MaximumBytes,
            cancellationToken,
            volume.Spec,
            volume.Status.VolumeGeneration,
            operationId: operationId).ConfigureAwait(false);
        if (begin.EncodedPayloadBytes is null ||
            begin.LogicalBytes is null ||
            begin.EntryCount is null ||
            string.IsNullOrWhiteSpace(begin.ContentSha256))
            throw new InvalidOperationException(
                "Environment.Storage.BackupInvalid: guest backup preparation returned incomplete evidence.");
        RequireReservation(spec.Reservation, begin.LogicalBytes.Value);
        string destination = BackupPath(metadata.Id.Value);
        using StorageBackupKeyMaterial key = await _backupKeys.ResolveAsync(
            spec.EncryptionCredential,
            metadata.Scope,
            "volume-backup-capture",
            cancellationToken).ConfigureAwait(false);
        PortableVolumeBackupManifest? capturedManifest = null;
        bool cleanupConfirmed = false;
        try
        {
            capturedManifest = await
                PortableVolumeBackupArchive.CaptureEncodedPayloadAsync(
                    destination,
                    new PortableVolumeBackupManifest
                    {
                        BackupId = metadata.Id.Value,
                        OwnerTypeId = spec.OwnerTypeId,
                        OwnerScopeId = spec.OwnerScopeId,
                        OwnerVersion = spec.OwnerVersion,
                        CompatibilityDomain = spec.CompatibilityDomain,
                        LogicalVolumeId = volume.Spec.LogicalId,
                        VolumeGeneration = (ulong)volume.Status.VolumeGeneration.Value,
                        ProviderId = ProviderId.Value,
                        Consistency = spec.Consistency,
                        CreatedAt = DateTimeOffset.UtcNow,
                        LogicalBytes = begin.LogicalBytes.Value,
                        EntryCount = begin.EntryCount.Value,
                        ContentSha256 = begin.ContentSha256,
                        EncryptionKeyId = "pending",
                    },
                    key,
                    begin.EncodedPayloadBytes.Value,
                    ReadBackupPayloadAsync(
                        operationId,
                        volume,
                        begin.EncodedPayloadBytes.Value,
                        cancellationToken),
                    volume.Spec.MaximumBytes.Value,
                    cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            try
            {
                _ = await SendAsync(
                    AppleVirtualizationStorageAction.EndBackup,
                    volume.Spec.LogicalId,
                    volume.Spec.MaximumBytes,
                    CancellationToken.None,
                    volume.Spec,
                    volume.Status.VolumeGeneration,
                    operationId: operationId).ConfigureAwait(false);
                cleanupConfirmed = true;
            }
            catch
            {
                // The immutable artifact remains valid. Recovery retries this
                // idempotent cleanup by the same operation identity.
            }
        }
        PortableVolumeBackupManifest manifest =
            capturedManifest ??
            throw new InvalidOperationException(
                "Environment.Storage.BackupInvalid: capture completed without an immutable manifest.");
        var status = new VolumeBackupStatus
        {
            Phase = ResourcePhase.Ready,
            BackupPhase = VolumeBackupPhase.Ready,
            ReconciliationOutcome =
                ResourceReconciliationOutcome.Accepted,
            ObservedGeneration = metadata.Generation,
            ContentDigest =
                new Digest("sha256", manifest.ContentSha256),
            LogicalBytes = new ByteSize(manifest.LogicalBytes),
            StoredBytes =
                new ByteSize(new FileInfo(destination).Length),
            CapturedAt = manifest.CreatedAt,
            Diagnostics = cleanupConfirmed
                ? []
                :
                [
                    new Diagnostic
                    {
                        Code = new DiagnosticCode(
                            "Environment.Storage.TemporaryCleanupPending"),
                        Severity = DiagnosticSeverity.Warning,
                        Message =
                            "The backup artifact is verified, but guest temporary cleanup could not be confirmed and will be retried during recovery.",
                    },
                ],
        };
        return Store(metadata, spec, status, BackupShape);
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
        var volume = Get<DurableVolume, DurableVolumeSpec, DurableVolumeStatus>(spec.Volume);
        _ = Get<StorageReservation, StorageReservationSpec, StorageReservationStatus>(spec.Reservation);
        string artifact = BackupPath(metadata.Id.Value);
        if (!File.Exists(artifact))
        {
            if (persisted.BackupPhase is VolumeBackupPhase.Ready or VolumeBackupPhase.Verifying)
                throw new InvalidOperationException(
                    "Environment.Storage.BackupInvalid: authoritative Apple backup content is missing during recovery.");
            return Store(metadata, spec, persisted with
            {
                Phase = ResourcePhase.Degraded,
                BackupPhase = VolumeBackupPhase.FailedRetained,
            }, BackupShape);
        }
        if (_backupKeys is null)
            throw new InvalidOperationException(
                "AppleVirtualization.BackupEncryptionAuthorityRequired: backup recovery requires a platform credential resolver.");
        using StorageBackupKeyMaterial key = await _backupKeys.ResolveAsync(
            spec.EncryptionCredential,
            metadata.Scope,
            "volume-backup-recovery",
            cancellationToken).ConfigureAwait(false);
        PortableVolumeBackupManifest manifest = PortableVolumeBackupArchive.Validate(
            artifact,
            key,
            volume.Spec.MaximumBytes.Value,
            cancellationToken);
        if (!ManifestMatches(manifest, metadata.Id.Value, spec) ||
            (persisted.ContentDigest is not null &&
             !string.Equals(persisted.ContentDigest.Value.Value, manifest.ContentSha256, StringComparison.Ordinal)))
            throw new InvalidOperationException(
                "Environment.Storage.BackupInvalid: immutable Apple backup evidence does not match durable authority.");
        bool cleanupConfirmed = false;
        try
        {
            _ = await SendAsync(
                AppleVirtualizationStorageAction.EndBackup,
                volume.Spec.LogicalId,
                volume.Spec.MaximumBytes,
                CancellationToken.None,
                volume.Spec,
                volume.Status.VolumeGeneration,
                operationId: metadata.Id.Value).ConfigureAwait(false);
            cleanupConfirmed = true;
        }
        catch
        {
            // Keep the cleanup checkpoint visible and retryable. The guest
            // bounds operation-temporary storage independently.
        }
        return Store(metadata, spec, persisted with
        {
            Phase = ResourcePhase.Ready,
            BackupPhase = VolumeBackupPhase.Ready,
            ContentDigest = new Digest("sha256", manifest.ContentSha256),
            LogicalBytes = new ByteSize(manifest.LogicalBytes),
            StoredBytes = new ByteSize(new FileInfo(artifact).Length),
            CapturedAt = manifest.CreatedAt,
            Diagnostics = cleanupConfirmed
                ? []
                :
                [
                    new Diagnostic
                    {
                        Code = new DiagnosticCode(
                            "Environment.Storage.TemporaryCleanupPending"),
                        Severity = DiagnosticSeverity.Warning,
                        Message =
                            "The recovered backup is valid, but guest temporary cleanup remains unconfirmed.",
                    },
                ],
        }, BackupShape);
    }

    public ValueTask DeleteAsync(
        ResourceRef<VolumeBackup> backup,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        string artifact = BackupPath(backup.Id.Value);
        if (File.Exists(artifact))
            File.Delete(artifact);
        _resources.Remove<VolumeBackup, VolumeBackupSpec, VolumeBackupStatus>(backup);
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
                "AppleVirtualization.RestoreObservedStateInvalid: restore recovery must use the non-mutating recovery contract.");
        var backup = Get<VolumeBackup, VolumeBackupSpec, VolumeBackupStatus>(spec.Backup);
        var target = Get<DurableVolume, DurableVolumeSpec, DurableVolumeStatus>(spec.TargetVolume);
        long logicalBytes = backup.Status.LogicalBytes?.Value ??
            throw new InvalidOperationException("Environment.Storage.BackupInvalid: backup size evidence is missing.");
        long currentBytes = target.Status.UsedBytes?.Value ?? 0;
        RequireReservation(spec.Reservation, checked(logicalBytes + currentBytes));
        RequireRestoreCompatibility(backup.Spec, target.Spec, spec);
        if (_backupKeys is null)
            throw new InvalidOperationException(
                "AppleVirtualization.BackupEncryptionAuthorityRequired: restore requires a platform credential resolver.");
        string digest = backup.Status.ContentDigest?.Value ??
            throw new InvalidOperationException("Environment.Storage.BackupInvalid: backup digest evidence is missing.");
        string operationId = metadata.Id.Value;
        ResourceGeneration restoredGeneration = new(checked(target.Status.VolumeGeneration.Value + 1));
        using StorageBackupKeyMaterial key = await _backupKeys.ResolveAsync(
            backup.Spec.EncryptionCredential,
            metadata.Scope,
            "volume-backup-restore",
            cancellationToken).ConfigureAwait(false);
        _ = await SendAsync(
            AppleVirtualizationStorageAction.BeginRestore,
            target.Spec.LogicalId,
            target.Spec.MaximumBytes,
            cancellationToken,
            target.Spec,
            target.Status.VolumeGeneration,
            operationId: operationId,
            expectedContentSha256: digest,
            expectedLogicalBytes: logicalBytes).ConfigureAwait(false);
        try
        {
            long offset = 0;
            PortableVolumeBackupManifest manifest = await
                PortableVolumeBackupArchive.StreamValidatedPayloadAsync(
                    BackupPath(spec.Backup.Id.Value),
                    key,
                    target.Spec.MaximumBytes.Value,
                    async (chunk, token) =>
                    {
                        for (int start = 0; start < chunk.Length; start += MaximumRawChunkBytes)
                        {
                            ReadOnlyMemory<byte> part = chunk.Slice(
                                start,
                                Math.Min(MaximumRawChunkBytes, chunk.Length - start));
                            _ = await SendAsync(
                                AppleVirtualizationStorageAction.WriteRestoreChunk,
                                target.Spec.LogicalId,
                                target.Spec.MaximumBytes,
                                token,
                                target.Spec,
                                target.Status.VolumeGeneration,
                                operationId: operationId,
                                offset: offset,
                                chunkBase64: Convert.ToBase64String(part.Span)).ConfigureAwait(false);
                            offset += part.Length;
                        }
                    },
                    cancellationToken).ConfigureAwait(false);
            AppleVirtualizationStorageResponse committed = await SendAsync(
                AppleVirtualizationStorageAction.CommitRestore,
                target.Spec.LogicalId,
                target.Spec.MaximumBytes,
                CancellationToken.None,
                target.Spec,
                target.Status.VolumeGeneration,
                operationId: operationId,
                expectedContentSha256: digest,
                expectedEncodedPayloadBytes: offset,
                expectedLogicalBytes: manifest.LogicalBytes,
                expectedEntryCount: manifest.EntryCount).ConfigureAwait(false);
            if (!committed.Completed ||
                !string.Equals(committed.ContentSha256, digest, StringComparison.Ordinal) ||
                committed.VolumeGeneration !=
                    (ulong)restoredGeneration.Value)
                throw new InvalidOperationException(
                    "Environment.Storage.RestoreIncomplete: guest restore selection did not return matching digest and generation postcondition evidence.");
            _ = Store(Metadata(target.Resource), target.Spec, target.Status with
            {
                VolumeGeneration = restoredGeneration,
                UsedBytes = new ByteSize(manifest.LogicalBytes),
                Integrity = VolumeIntegrityState.Clean,
            }, VolumeShape);
            return Store(metadata, spec, new VolumeRestoreStatus
            {
                Phase = ResourcePhase.Ready,
                RestorePhase = VolumeRestorePhase.Ready,
                ReconciliationOutcome = ResourceReconciliationOutcome.Accepted,
                ObservedGeneration = metadata.Generation,
                PreviousVolumeGeneration = target.Status.VolumeGeneration,
                RestoredVolumeGeneration = restoredGeneration,
                VerifiedDigest = new Digest("sha256", digest),
                RestoredAt = DateTimeOffset.UtcNow,
            }, RestoreShape);
        }
        catch
        {
            try
            {
                _ = await SendAsync(
                    AppleVirtualizationStorageAction.AbortRestore,
                    target.Spec.LogicalId,
                    target.Spec.MaximumBytes,
                    CancellationToken.None,
                    target.Spec,
                    target.Status.VolumeGeneration,
                    operationId: operationId).ConfigureAwait(false);
            }
            catch { }
            throw;
        }
    }

    public ValueTask<VolumeRestoreStatus> GetStatusAsync(
        ResourceRef<VolumeRestore> restore,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromResult(
            Get<VolumeRestore, VolumeRestoreSpec, VolumeRestoreStatus>(restore).Status);
    }

    public async ValueTask<VolumeRestoreStatus> RecoverAsync(
        ResourceMetadata<VolumeRestore> metadata,
        VolumeRestoreSpec spec,
        VolumeRestoreStatus persisted,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var backup = Get<VolumeBackup, VolumeBackupSpec, VolumeBackupStatus>(spec.Backup);
        var target = Get<DurableVolume, DurableVolumeSpec, DurableVolumeStatus>(spec.TargetVolume);
        RequireRestoreCompatibility(backup.Spec, target.Spec, spec);
        string digest = backup.Status.ContentDigest?.Value ??
            throw new InvalidOperationException("Environment.Storage.BackupInvalid: backup digest evidence is missing.");
        ResourceGeneration previousGeneration =
            persisted.PreviousVolumeGeneration ??
            throw new InvalidOperationException(
                "Environment.Storage.RestoreIncomplete: persisted restore state has no previous generation.");
        ResourceGeneration expectedRestoredGeneration =
            new(checked(previousGeneration.Value + 1));
        AppleVirtualizationStorageResponse response = await SendAsync(
            AppleVirtualizationStorageAction.CommitRestore,
            target.Spec.LogicalId,
            target.Spec.MaximumBytes,
            cancellationToken,
            target.Spec,
            previousGeneration,
            operationId: metadata.Id.Value,
            expectedContentSha256: digest,
            expectedLogicalBytes: backup.Status.LogicalBytes?.Value).ConfigureAwait(false);
        if (!response.Completed ||
            !string.Equals(response.ContentSha256, digest, StringComparison.Ordinal) ||
            response.VolumeGeneration !=
                (ulong)expectedRestoredGeneration.Value)
            throw new InvalidOperationException(
                "Environment.Storage.RestoreIncomplete: interrupted Apple restore could not prove selected content and generation.");
        _ = Store(
            Metadata(target.Resource),
            target.Spec,
            target.Status with
            {
                VolumeGeneration = expectedRestoredGeneration,
                Integrity = VolumeIntegrityState.Clean,
            },
            VolumeShape);
        return Store(metadata, spec, persisted with
        {
            Phase = ResourcePhase.Ready,
            RestorePhase = VolumeRestorePhase.Ready,
            RestoredVolumeGeneration =
                expectedRestoredGeneration,
            VerifiedDigest = new Digest("sha256", digest),
            RestoredAt = persisted.RestoredAt ?? DateTimeOffset.UtcNow,
        }, RestoreShape);
    }

    public async ValueTask FinalizeAsync(
        ResourceRef<VolumeRestore> restore,
        CancellationToken cancellationToken = default)
    {
        var entry = Get<VolumeRestore, VolumeRestoreSpec, VolumeRestoreStatus>(restore);
        var target = Get<DurableVolume, DurableVolumeSpec, DurableVolumeStatus>(entry.Spec.TargetVolume);
        _ = await SendAsync(
            AppleVirtualizationStorageAction.AbortRestore,
            target.Spec.LogicalId,
            target.Spec.MaximumBytes,
            cancellationToken,
            target.Spec,
            entry.Status.PreviousVolumeGeneration ?? target.Status.VolumeGeneration,
            operationId: restore.Id.Value).ConfigureAwait(false);
    }

    private async ValueTask<AppleVirtualizationStorageResponse>
        SendAsync(
            AppleVirtualizationStorageAction action,
            string? logicalId,
            ByteSize? maximumBytes,
            CancellationToken cancellationToken,
            DurableVolumeSpec? volumeSpec = null,
            ResourceGeneration? volumeGeneration = null,
            string? operationId = null,
            long? offset = null,
            int? maximumChunkBytes = null,
            string? chunkBase64 = null,
            string? expectedContentSha256 = null,
            long? expectedEncodedPayloadBytes = null,
            long? expectedLogicalBytes = null,
            int? expectedEntryCount = null,
            StorageClass? storageClass = null)
    {
        IReadOnlyList<AppleVirtualizationLedgerEntry<
            RuntimeHost,
            RuntimeHostStatus>> readyHosts =
            _hosts.GetReadyRuntimeHosts();
        if (readyHosts.Count == 0)
            throw new InvalidOperationException(
                "AppleVirtualization.StorageHostUnavailable: one ready RuntimeHost is required.");
        if (readyHosts.Count != 1)
            throw new InvalidOperationException(
                "AppleVirtualization.StorageHostAmbiguous: more than one ready RuntimeHost exists.");
        var host = readyHosts[0];
        ulong hostStartGeneration = (ulong)Math.Max(
            0,
            host.Status.Generations
                .HostStartGeneration?.Value ??
            0);
        long sequence =
            Interlocked.Increment(ref _requestSequence);
        AppleVirtualizationHelperEnvelope response =
            await _helper.SendAsync(
                AppleVirtualizationHelperEnvelope.Request(
                    AppleVirtualizationHelperOperation.Storage,
                    $"storage-{sequence}",
                    sequence,
                    AppleVirtualizationHelperProtocol.StorageRequestSchema) with
                {
                    ResourceId = host.Resource.Id.Value,
                    ResourceScope = host.Resource.Scope,
                    ResourceGeneration =
                        host.Resource.Generation,
                    ProviderHandle = host.ProviderHandle,
                    ProviderGeneration =
                        _hosts.ProviderGeneration,
                    StorageRequest =
                        new AppleVirtualizationStorageRequest
                        {
                            HostId = host.Resource.Id.Value,
                            ProviderGeneration =
                                _hosts.ProviderGeneration,
                            HostStartGeneration =
                                hostStartGeneration,
                            Action = action,
                            StorageClass = storageClass,
                            LogicalVolumeId = logicalId,
                            MaximumBytes = maximumBytes,
                            OwnerScopeId =
                                volumeSpec?.OwnerScopeId,
                            OwnerResourceId =
                                volumeSpec?.OwnerResourceId,
                            DeclarationId =
                                volumeSpec?.DeclarationId,
                            CompatibilityDomain =
                                volumeSpec?.CompatibilityDomain,
                            VolumeGeneration =
                                volumeGeneration is null
                                    ? null
                                    : (ulong)volumeGeneration.Value.Value,
                            OperationId = operationId,
                            Offset = offset,
                            MaximumChunkBytes = maximumChunkBytes,
                            ChunkBase64 = chunkBase64,
                            ExpectedContentSha256 = expectedContentSha256,
                            ExpectedEncodedPayloadBytes = expectedEncodedPayloadBytes,
                            ExpectedLogicalBytes = expectedLogicalBytes,
                            ExpectedEntryCount = expectedEntryCount,
                        },
                },
                cancellationToken).ConfigureAwait(false);
        if (response.ResponseStatus ==
                AppleVirtualizationHelperResponseStatus.Error ||
            response.StorageResponse is null)
            throw new InvalidOperationException(
                $"{response.Error?.Code ?? "AppleVirtualization.StorageResponseMissing"}: {response.Error?.Message ?? "The helper returned no storage response."}");
        AppleVirtualizationStorageResponse storage =
            response.StorageResponse;
        if (!string.Equals(
                storage.HostId,
                host.Resource.Id.Value,
                StringComparison.Ordinal) ||
            storage.ProviderGeneration !=
                _hosts.ProviderGeneration ||
            storage.HostStartGeneration !=
                hostStartGeneration ||
            storage.Action != action ||
            !string.Equals(
                storage.LogicalVolumeId,
                logicalId,
                StringComparison.Ordinal) ||
            !string.Equals(
                storage.OperationId,
                operationId,
                StringComparison.Ordinal))
            throw new InvalidOperationException(
                "AppleVirtualization.StorageResponseIdentityMismatch: the helper response does not belong to the requested host, generation, action, and logical volume.");
        return storage;
    }

    private static void RequireVolumeGeneration(
        AppleVirtualizationStorageResponse response,
        ResourceGeneration expected,
        string operation)
    {
        if (response.VolumeGeneration != (ulong)expected.Value)
            throw new InvalidOperationException(
                $"Environment.Storage.IntegrityCheckRequired: Apple {operation} returned missing or mismatched volume-generation evidence.");
    }

    private async IAsyncEnumerable<ReadOnlyMemory<byte>> ReadBackupPayloadAsync(
        string operationId,
        ProviderResourceEntry<DurableVolume, DurableVolumeSpec, DurableVolumeStatus> volume,
        long encodedPayloadBytes,
        [System.Runtime.CompilerServices.EnumeratorCancellation]
        CancellationToken cancellationToken)
    {
        long offset = 0;
        while (offset < encodedPayloadBytes)
        {
            AppleVirtualizationStorageResponse response = await SendAsync(
                AppleVirtualizationStorageAction.ReadBackupChunk,
                volume.Spec.LogicalId,
                volume.Spec.MaximumBytes,
                cancellationToken,
                volume.Spec,
                volume.Status.VolumeGeneration,
                operationId: operationId,
                offset: offset,
                maximumChunkBytes: MaximumRawChunkBytes).ConfigureAwait(false);
            byte[] chunk;
            try
            {
                chunk = Convert.FromBase64String(response.ChunkBase64 ?? string.Empty);
            }
            catch (FormatException exception)
            {
                throw new InvalidOperationException(
                    "Environment.Storage.BackupInvalid: guest backup chunk is not canonical Base64.",
                    exception);
            }
            if (response.Offset != offset ||
                chunk.Length == 0 ||
                chunk.Length > MaximumRawChunkBytes ||
                offset + chunk.Length > encodedPayloadBytes)
                throw new InvalidOperationException(
                    "Environment.Storage.BackupInvalid: guest backup chunk violates the accepted offset or size bounds.");
            offset += chunk.Length;
            yield return chunk;
        }
    }

    private void RequireReservation(
        ResourceRef<StorageReservation> reservation,
        long minimumBytes)
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

    private static void RequireRestoreCompatibility(
        VolumeBackupSpec backup,
        DurableVolumeSpec target,
        VolumeRestoreSpec restore)
    {
        if (!string.Equals(backup.CompatibilityDomain, restore.ExpectedCompatibilityDomain, StringComparison.Ordinal) ||
            !string.Equals(target.CompatibilityDomain, restore.ExpectedCompatibilityDomain, StringComparison.Ordinal))
            throw new InvalidOperationException(
                "Environment.Storage.RestoreCompatibilityMismatch: backup and target compatibility identities differ.");
    }

    private string BackupPath(string backupId)
    {
        if (string.IsNullOrWhiteSpace(backupId) ||
            backupId is "." or ".." ||
            backupId.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 ||
            backupId.Contains(Path.DirectorySeparatorChar) ||
            backupId.Contains(Path.AltDirectorySeparatorChar))
            throw new InvalidOperationException(
                "Environment.Storage.BackupInvalid: backup identity is not a safe path component.");
        return Path.Combine(_backupsRoot, backupId + ".hpdbackup");
    }

    private static bool ManifestMatches(
        PortableVolumeBackupManifest manifest,
        string backupId,
        VolumeBackupSpec spec) =>
        string.Equals(manifest.BackupId, backupId, StringComparison.Ordinal) &&
        string.Equals(manifest.OwnerTypeId, spec.OwnerTypeId, StringComparison.Ordinal) &&
        string.Equals(manifest.OwnerScopeId, spec.OwnerScopeId, StringComparison.Ordinal) &&
        string.Equals(manifest.OwnerVersion, spec.OwnerVersion, StringComparison.Ordinal) &&
        string.Equals(manifest.CompatibilityDomain, spec.CompatibilityDomain, StringComparison.Ordinal);

    private static void ValidateVolumeObservation(
        AppleVirtualizationStorageResponse response,
        DurableVolumeSpec spec,
        string? expectedFilesystemIdentity)
    {
        if (string.IsNullOrWhiteSpace(
                response.FilesystemIdentity))
            throw new InvalidOperationException(
                "Environment.Storage.IntegrityCheckRequired: Apple durable-volume storage returned no filesystem identity.");
        if (expectedFilesystemIdentity is not null &&
            !HasMatchingFilesystemIdentity(
                response,
                expectedFilesystemIdentity))
            throw new InvalidOperationException(
                "Environment.Storage.IntegrityCheckRequired: Apple durable-volume filesystem or project identity changed.");
        if (response.UsedBytes is not null &&
            response.UsedBytes.Value.Value >
                spec.MaximumBytes.Value)
            throw new InvalidOperationException(
                "Environment.Storage.AppVolumeLow: Apple durable-volume usage exceeds its accepted maximum capacity.");
    }

    private static bool HasMatchingFilesystemIdentity(
        AppleVirtualizationStorageResponse response,
        string? expectedFilesystemIdentity) =>
        !string.IsNullOrWhiteSpace(expectedFilesystemIdentity) &&
        string.Equals(
            response.FilesystemIdentity,
            expectedFilesystemIdentity,
            StringComparison.Ordinal);

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

    private ProviderResourceEntry<TResource, TSpec, TStatus>
        Get<TResource, TSpec, TStatus>(
            ResourceRef<TResource> resource)
        where TResource :
            IExecutionResourceMarker,
            IOperationTargetMarker
        where TSpec : notnull
        where TStatus : ResourceStatus
    {
        var lookup =
            _resources.TryGet<TResource, TSpec, TStatus>(
                resource);
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
        where TResource :
            IExecutionResourceMarker,
            IOperationTargetMarker
        where TSpec : notnull
        where TStatus : ResourceStatus
    {
        var entry =
            _resources.Upsert(metadata, spec, status, shape);
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
        _resources.Upsert(metadata, spec, completed, shape);
        return completed;
    }

    private static ProviderResourceShape Shape<TResource>(
        string kind,
        TargetRouteSegmentKind segment)
        where TResource :
            IExecutionResourceMarker,
            IOperationTargetMarker =>
        new(
            new TargetKind(kind),
            segment,
            TargetHandleLifetime.DurableAddress,
            TargetHandleAuthority.Observe |
                TargetHandleAuthority.Read |
                TargetHandleAuthority.Control,
            new SchemaId(
                $"hpd.execution.apple-virtualization.{kind}.handle.v1"));

    private static ResourceMetadata<TResource>
        Metadata<TResource>(
            ResourceRef<TResource> resource)
        where TResource : IExecutionResourceMarker =>
        new()
        {
            Id = resource.Id,
            Kind = new ResourceKind(typeof(TResource).Name),
            Scope = resource.Scope,
            Generation =
                resource.Generation ??
                new ResourceGeneration(1),
            SchemaVersion = new SchemaVersion("1"),
        };
}
