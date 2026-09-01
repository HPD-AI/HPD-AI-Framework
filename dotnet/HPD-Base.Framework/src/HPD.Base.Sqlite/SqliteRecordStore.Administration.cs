using System.Buffers.Binary;
using System.Collections.Immutable;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Security.AccessControl;
using Microsoft.Data.Sqlite;

namespace HPD.Base.Sqlite;

public sealed partial class SqliteRecordStore
{
    private static ReadOnlySpan<byte> BackupMagic => "HPDBAK01"u8;
    private const ushort BackupVersion = 1;
    private const byte BackupAuthenticationHmacSha256 = 1;
    private const int BackupHeaderLength = 24;
    private const int BackupTagLength = 32;
    private const string BackupAuthenticationPurpose = "hpd.base.backup.manifest.v1";

    /// <inheritdoc />
    public ValueTask<OperationResult<BaseBackupManifest>> CreateBackupAsync(
        Stream destination,
        BaseBackupRequest request,
        CancellationToken cancellationToken = default) =>
        HPDBaseSqliteTelemetry.TraceAdministrationAsync(
            "backup",
            _options.StoreId,
            () => CreateBackupCoreAsync(destination, request, cancellationToken));

    private async ValueTask<OperationResult<BaseBackupManifest>> CreateBackupCoreAsync(
        Stream destination,
        BaseBackupRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(destination);
        ArgumentNullException.ThrowIfNull(request);
        if (!AdministrationCapability.Backup || _tokenProtector is null)
            return AdminUnsupported<BaseBackupManifest>();
        if (!destination.CanWrite || !ValidStoreRequest(request.StoreId))
            return AdminValidation<BaseBackupManifest>(BaseAdministrationErrorCodes.Invalid, "The backup request is invalid.");
        if (!TryCaptureAdministrationPath(out SqliteAdministrationPathGuard pathGuard))
            return AdminUnsupported<BaseBackupManifest>();

        string? staging = RandomSiblingPath("backup");
        IAsyncDisposable? lease = null;
        bool slot = false;
        try
        {
            pathGuard.ValidateSibling(staging, mustExist: false);
            using var acquisition = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            acquisition.CancelAfter(_options.AdministrationAcquisitionTimeout);
            await _administrationExecutionSlots.WaitAsync(acquisition.Token).ConfigureAwait(false);
            slot = true;
            lease = await _schemaGenerationGate.AcquireExclusiveAsync(acquisition.Token).ConfigureAwait(false);
            pathGuard.RevalidateActive();
            await EnsureKeepAliveAsync(acquisition.Token).ConfigureAwait(false);

            Task native = RunNativeBackupAsync(staging);
            try
            {
                await native.WaitAsync(_options.NativeBackupCompletionWait, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) { QuarantineAdministration(native, lease, staging, "backup"); lease = null; staging = null; slot = false; throw; }
            catch (TimeoutException) { QuarantineAdministration(native, lease, staging, "backup"); lease = null; staging = null; slot = false; throw new OperationCanceledException(); }

            pathGuard.ValidateSibling(staging, mustExist: true);
            SqliteBackupDatabaseFacts stagedFacts = await ValidateDatabaseFileAsync(staging, null, cancellationToken).ConfigureAwait(false);
            if (RestoreRecoveryIndeterminate || RestoreRecoveryPending)
                throw new InvalidOperationException("HPD.BASE SQLite restore recovery is incomplete; backup authority is unavailable.");
            await using var stagedSource = new SqliteConnection(new SqliteConnectionStringBuilder
            {
                DataSource = staging,
                Mode = SqliteOpenMode.ReadOnly,
                Pooling = false,
            }.ToString());
            await stagedSource.OpenAsync(cancellationToken).ConfigureAwait(false);
            BaseBackupManifest manifest = await ReadManifestAsync(
                stagedSource, new FileInfo(staging).Length, cancellationToken).ConfigureAwait(false);
            EnsureManifestMatchesDatabase(manifest, stagedFacts);

            if (request.ExpectedStoreIdentityDigest is { } expected
                && !FixedHexEquals(expected, manifest.StoreIdentityDigest))
                return AdminConflict<BaseBackupManifest>(BaseAdministrationErrorCodes.ArtifactIdentityMismatch, "The active store identity does not match the request.");

            (BaseBackupManifest authenticatedManifest, byte[] artifactSha256) = await WriteEnvelopeAsync(
                destination, staging, manifest, cancellationToken).ConfigureAwait(false);
            await using SqliteConnection source = await OpenSubjectMaintenanceAsync(cancellationToken).ConfigureAwait(false);
            await PublishActivationBackupCoverageCheckpointAsync(
                source, authenticatedManifest, artifactSha256, cancellationToken).ConfigureAwait(false);
            return OperationResults.Ok(authenticatedManifest);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return AdminStoreError<BaseBackupManifest>(BaseAdministrationErrorCodes.BackupTimeout, "The backup exceeded its bounded lifetime.");
        }
        catch (OperationCanceledException) { throw; }
        catch (SqliteException exception) when (exception.SqliteErrorCode is 5 or 6)
        {
            return AdminStoreError<BaseBackupManifest>(BaseAdministrationErrorCodes.BackupBusy, "The SQLite store remained busy during backup.");
        }
        catch (SemanticRecoveryProofException)
        {
            return AdminStoreError<BaseBackupManifest>(BaseSemanticActivationErrorCodes.RecoveryProofInvalid,
                "Semantic activation recovery authority is invalid.");
        }
        catch (Exception) when (!cancellationToken.IsCancellationRequested)
        {
            return AdminStoreError<BaseBackupManifest>(BaseAdministrationErrorCodes.BackupFailed, "The backup failed before producing a confirmed artifact.");
        }
        finally
        {
            if (lease is not null) await lease.DisposeAsync().ConfigureAwait(false);
            if (slot) _administrationExecutionSlots.Release();
            if (staging is not null) DeleteStaging(staging);
        }
    }

    /// <inheritdoc />
    public ValueTask<OperationResult<BaseBackupManifest>> ValidateBackupAsync(
        Stream source,
        BaseBackupValidationRequest request,
        CancellationToken cancellationToken = default) =>
        HPDBaseSqliteTelemetry.TraceAdministrationAsync(
            "validation",
            _options.StoreId,
            () => ValidateBackupCoreAsync(source, request, cancellationToken));

    private async ValueTask<OperationResult<BaseBackupManifest>> ValidateBackupCoreAsync(
        Stream source,
        BaseBackupValidationRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(request);
        if (!AdministrationCapability.Validate || _tokenProtector is null)
            return AdminUnsupported<BaseBackupManifest>();
        if (!source.CanRead || !ValidStoreRequest(request.StoreId))
            return AdminValidation<BaseBackupManifest>(BaseAdministrationErrorCodes.Invalid, "The backup-validation request is invalid.");

        if (!TryCaptureAdministrationPath(out SqliteAdministrationPathGuard pathGuard))
            return AdminUnsupported<BaseBackupManifest>();
        string? staging = RandomSiblingPath("validation");
        bool slot = false;
        try
        {
            pathGuard.ValidateSibling(staging, mustExist: false);
            using var acquisition = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            acquisition.CancelAfter(_options.AdministrationAcquisitionTimeout);
            await _administrationExecutionSlots.WaitAsync(acquisition.Token).ConfigureAwait(false);
            slot = true;
            (BaseBackupManifest Manifest, byte KeyId, byte[] Header, byte[] ManifestBytes, byte[] Digest) artifact =
                await ReadEnvelopeAsync(source, staging!, cancellationToken).ConfigureAwait(false);
            pathGuard.RevalidateActive();
            pathGuard.ValidateSibling(staging!, mustExist: true);
            if (request.ExpectedArtifactStoreIdentityDigest is { } expected
                && !FixedHexEquals(expected, artifact.Manifest.StoreIdentityDigest))
                return AdminConflict<BaseBackupManifest>(BaseAdministrationErrorCodes.ArtifactIdentityMismatch, "The artifact store identity does not match the request.");
            string validationPath = staging!;
            Task validation = Task.Run(async () => await ValidateDatabaseFileAsync(validationPath, artifact.Manifest, CancellationToken.None).ConfigureAwait(false), CancellationToken.None);
            try { await validation.WaitAsync(_options.IntegrityCheckTimeout, cancellationToken).ConfigureAwait(false); }
            catch (OperationCanceledException) { QuarantineAdministration(validation, null, staging, "validation"); staging = null; slot = false; throw; }
            catch (TimeoutException) { QuarantineAdministration(validation, null, staging, "validation"); staging = null; slot = false; throw new OperationCanceledException(); }
            return OperationResults.Ok(artifact.Manifest);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return AdminStoreError<BaseBackupManifest>(BaseAdministrationErrorCodes.ValidationTimeout, "Artifact validation exceeded its bounded lifetime.");
        }
        catch (OperationCanceledException) { throw; }
        catch (BackupKeyUnavailableException)
        {
            return AdminValidation<BaseBackupManifest>(BaseAdministrationErrorCodes.ArtifactKeyUnavailable, "The artifact authentication key is unavailable.");
        }
        catch (BackupArtifactTooLargeException)
        {
            return AdminValidation<BaseBackupManifest>(BaseAdministrationErrorCodes.ArtifactTooLarge, "The backup artifact exceeds the configured bound.");
        }
        catch (Exception) when (!cancellationToken.IsCancellationRequested)
        {
            return AdminValidation<BaseBackupManifest>(BaseAdministrationErrorCodes.ArtifactInvalid, "The backup artifact is invalid.");
        }
        finally
        {
            if (slot) _administrationExecutionSlots.Release();
            if (staging is not null) DeleteStaging(staging);
        }
    }

    /// <inheritdoc />
    public ValueTask<OperationResult<BaseRestoreResult>> RestoreAsync(
        Stream source,
        BaseRestoreRequest request,
        CancellationToken cancellationToken = default) =>
        HPDBaseSqliteTelemetry.TraceAdministrationAsync(
            "restore",
            _options.StoreId,
            () => RestoreCallerBoundedAsync(source, request, cancellationToken));

    private async ValueTask<OperationResult<BaseRestoreResult>> RestoreCallerBoundedAsync(
        Stream source,
        BaseRestoreRequest request,
        CancellationToken cancellationToken)
    {
        Task<OperationResult<BaseRestoreResult>> work = Task.Run(
            async () => await RestoreCoreAsync(source, request, CancellationToken.None).ConfigureAwait(false),
            CancellationToken.None);
        TimeSpan bound = _options.RestoreStagingTimeout
            + _options.IntegrityCheckTimeout + _options.IntegrityCheckTimeout
            + _options.AdministrationAcquisitionTimeout + _options.AdministrationAcquisitionTimeout;
        try
        {
            return await work.WaitAsync(bound, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!work.IsCompleted)
        {
            TrackAdministrationCompletion(work, "restore");
            throw;
        }
        catch (TimeoutException)
        {
            TrackAdministrationCompletion(work, "restore");
            return RestoreStoreError(
                _options.SemanticActivationOwnerGeneration > 0
                    ? BaseSemanticActivationErrorCodes.MaintenanceTimeout
                    : BaseAdministrationErrorCodes.RestoreIndeterminate,
                "Restore completion is indeterminate and the store remains under maintenance.",
                BaseRestoreFailureDisposition.IndeterminateUnavailable);
        }
    }

    private async ValueTask<OperationResult<BaseRestoreResult>> RestoreCoreAsync(
        Stream source,
        BaseRestoreRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(request);
        if (!AdministrationCapability.Restore || _tokenProtector is null)
            return AdminUnsupported<BaseRestoreResult>();
        if (!source.CanRead || !ValidStoreRequest(request.StoreId)
            || !Enum.IsDefined(request.IdentityMode) || !Enum.IsDefined(request.RecoveryImageRetention)
            || !Enum.IsDefined(request.ScheduleRestoreDomain)
            || request.ScheduleRestoreDomain == BaseScheduleRestoreDomain.InPlaceRecovery && request.ScheduleRecoveryManifest is not null
            || request.ScheduleRestoreDomain == BaseScheduleRestoreDomain.NewDisasterDomain && request.ScheduleRecoveryManifest is null)
            return RestoreValidation(BaseAdministrationErrorCodes.Invalid, "The restore request is invalid.", BaseRestoreFailureDisposition.RejectedBeforeChange);
        if (!TryCaptureAdministrationPath(out SqliteAdministrationPathGuard pathGuard))
            return AdminUnsupported<BaseRestoreResult>();
        if (!request.ConfirmDestructiveReplacement)
            return RestoreValidation(BaseAdministrationErrorCodes.RestoreConfirmationRequired, "Destructive replacement was not confirmed.", BaseRestoreFailureDisposition.RejectedBeforeChange);
        if (!ValidDigest(request.ExpectedCurrentStoreIdentityDigest) || !ValidDigest(request.ExpectedArtifactStoreIdentityDigest))
            return RestoreValidation(BaseAdministrationErrorCodes.Invalid, "Restore identity digests are invalid.", BaseRestoreFailureDisposition.RejectedBeforeChange);

        string? staging = RandomSiblingPath("restore");
        string activePath = pathGuard.DatabasePath;
        string recovery = activePath + ".recovery." + Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(16));
        bool originalMoved = false;
        bool replacementInstalled = false;
        bool retainRecovery = false;
        bool administrationSlot = false;
        IReadOnlyDictionary<string, long> preRestoreSubjectGenerations = new Dictionary<string, long>(StringComparer.Ordinal);
        long preRestoreLifecycleDeliveryEpoch = 1;
        ImmutableArray<BaseScheduleRecoveryFloor> preRestoreScheduleFloors = [];
        long preRestoreActivationGeneration = 0;
        SemanticRecoverySnapshot? preRestoreSemanticRecovery = null;
        ImmutableArray<string> consumedScheduleRecoveryNonces = [];
        ImmutableArray<BaseScheduleRecoveryFloor> selectedScheduleRecoveryFloors = [];
        string? consumedScheduleRecoveryNonce = null;
        RestoreFilePolicy? filePolicy = null;
        try
        {
            pathGuard.ValidateSibling(staging, mustExist: false);
            pathGuard.ValidateSibling(recovery, mustExist: false);
            pathGuard.ValidateSibling(RestoreMarkerPath(), mustExist: false);
            using var slotAcquisition = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            slotAcquisition.CancelAfter(_options.AdministrationAcquisitionTimeout);
            await _administrationExecutionSlots.WaitAsync(slotAcquisition.Token).ConfigureAwait(false);
            administrationSlot = true;
            string stagingPath = staging!;
            Task<(BaseBackupManifest Manifest, byte KeyId, byte[] Header, byte[] ManifestBytes, byte[] Digest)> stagingWork =
                Task.Run(async () => await ReadEnvelopeAsync(source, stagingPath, CancellationToken.None).ConfigureAwait(false), CancellationToken.None);
            (BaseBackupManifest Manifest, byte KeyId, byte[] Header, byte[] ManifestBytes, byte[] Digest) artifact;
            try
            {
                artifact = await stagingWork.WaitAsync(_options.RestoreStagingTimeout, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                QuarantineAdministration(stagingWork, null, stagingPath, "restoreStaging");
                staging = null;
                administrationSlot = false;
                throw;
            }
            catch (TimeoutException)
            {
                QuarantineAdministration(stagingWork, null, stagingPath, "restoreStaging");
                staging = null;
                administrationSlot = false;
                return RestoreStoreError(
                    _options.SemanticActivationOwnerGeneration > 0
                        ? BaseSemanticActivationErrorCodes.MaintenanceTimeout
                        : BaseAdministrationErrorCodes.RestoreTimeout,
                    "Restore artifact staging exceeded its bounded lifetime.",
                    BaseRestoreFailureDisposition.RejectedBeforeChange);
            }
            BaseBackupManifest manifest = artifact.Manifest;
            if (request.SemanticRecoveryAuthority is { } semanticRecoveryAuthority
                && (!BaseSemanticRecoveryAuthorityContract.RestoreAuthorityIsValid(
                        semanticRecoveryAuthority.Definition, semanticRecoveryAuthority)
                    || semanticRecoveryAuthority.Definition.LogicalStoreId != request.StoreId
                    || semanticRecoveryAuthority.AcceptedNow != request.RecoveryAcceptedNow
                    || semanticRecoveryAuthority.PageCount < 0
                    || semanticRecoveryAuthority.PageCount > semanticRecoveryAuthority.Limits.MaximumPages
                    || semanticRecoveryAuthority.CanonicalBytes < 0
                    || semanticRecoveryAuthority.TransientBytes != semanticRecoveryAuthority.CanonicalBytes
                    || semanticRecoveryAuthority.TransientBytes > semanticRecoveryAuthority.Limits.MaximumTransientBytes
                    || semanticRecoveryAuthority.ArtifactSequence != manifest.SemanticTerminalPublicationSequence
                    || !CryptographicOperations.FixedTimeEquals(semanticRecoveryAuthority.ArtifactOrderedChecksum.AsSpan(),
                        manifest.SemanticTerminalPublicationChecksum.AsSpan())
                    || !CryptographicOperations.FixedTimeEquals(
                        BaseSemanticRecoveryAuthorityContract.RestoreAuthorityChecksum(semanticRecoveryAuthority).AsSpan(),
                        semanticRecoveryAuthority.Checksum.AsSpan())))
                return RestoreValidation(BaseSemanticActivationErrorCodes.RecoveryProofInvalid,
                    "Semantic activation recovery proof is invalid.", BaseRestoreFailureDisposition.OriginalPreserved);
            pathGuard.RevalidateActive();
            pathGuard.ValidateSibling(stagingPath, mustExist: true);
            if (!FixedHexEquals(request.ExpectedArtifactStoreIdentityDigest, manifest.StoreIdentityDigest))
                return RestoreConflict(BaseAdministrationErrorCodes.RestoreIdentityMismatch, "The artifact identity does not match the restore request.", BaseRestoreFailureDisposition.RejectedBeforeChange);
            Task validationWork = Task.Run(
                async () => await ValidateDatabaseFileAsync(stagingPath, manifest, CancellationToken.None).ConfigureAwait(false),
                CancellationToken.None);
            try
            {
                await validationWork.WaitAsync(_options.IntegrityCheckTimeout, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                QuarantineAdministration(validationWork, null, stagingPath, "restoreValidation");
                staging = null;
                administrationSlot = false;
                throw;
            }
            catch (TimeoutException)
            {
                QuarantineAdministration(validationWork, null, stagingPath, "restoreValidation");
                staging = null;
                administrationSlot = false;
                return RestoreStoreError(
                    BaseAdministrationErrorCodes.RestoreTimeout,
                    "Restore artifact validation exceeded its bounded lifetime.",
                    BaseRestoreFailureDisposition.RejectedBeforeChange);
            }

            using var acquisition = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            acquisition.CancelAfter(_options.AdministrationAcquisitionTimeout);
            await using IAsyncDisposable lease = await _schemaGenerationGate.AcquireExclusiveAsync(acquisition.Token).ConfigureAwait(false);
            Volatile.Write(ref _restoreInstallationActive, 1);
            pathGuard.RevalidateActive();
            (string ActiveIdentity, long PreRestoreEpoch) = await ReadActiveIdentityAsync(acquisition.Token).ConfigureAwait(false);
            preRestoreSubjectGenerations = await ReadSubjectStateGenerationsAsync(acquisition.Token).ConfigureAwait(false);
            preRestoreLifecycleDeliveryEpoch = await ReadSubjectLifecycleDeliveryEpochAsync(acquisition.Token).ConfigureAwait(false);
            await using (SqliteConnection active = await _connections.OpenAsync(acquisition.Token).ConfigureAwait(false))
            {
                preRestoreScheduleFloors = await CaptureScheduleRecoveryFloorsAsync(active, acquisition.Token).ConfigureAwait(false);
                (preRestoreActivationGeneration, _) = await ReadActivationAuthorityAsync(active, null, acquisition.Token).ConfigureAwait(false);
                preRestoreSemanticRecovery = await CaptureSemanticRecoverySnapshotAsync(active, acquisition.Token).ConfigureAwait(false);
                consumedScheduleRecoveryNonces = await ReadConsumedRecoveryNoncesAsync(active, acquisition.Token).ConfigureAwait(false);
            }
            if (!Enum.IsDefined(request.ScheduleRestoreDomain)
                || request.ScheduleRestoreDomain == BaseScheduleRestoreDomain.InPlaceRecovery && request.ScheduleRecoveryManifest is not null
                || request.ScheduleRestoreDomain == BaseScheduleRestoreDomain.NewDisasterDomain && request.ScheduleRecoveryManifest is null)
                return RestoreValidation(BaseAdministrationErrorCodes.Invalid, "The restore request is invalid.", BaseRestoreFailureDisposition.OriginalPreserved);
            if (request.ScheduleRestoreDomain == BaseScheduleRestoreDomain.InPlaceRecovery)
            {
                selectedScheduleRecoveryFloors = preRestoreScheduleFloors;
            }
            else
            {
                BaseScheduleRecoveryManifest manifestAuthority = request.ScheduleRecoveryManifest!;
                await using var artifactConnection = new SqliteConnection(new SqliteConnectionStringBuilder
                { DataSource = stagingPath, Mode = SqliteOpenMode.ReadOnly, Pooling = false }.ToString());
                await artifactConnection.OpenAsync(acquisition.Token).ConfigureAwait(false);
                ImmutableArray<BaseScheduleRecoveryFloor> artifactFloors = await CaptureScheduleRecoveryFloorsAsync(
                    artifactConnection, acquisition.Token).ConfigureAwait(false);
                ImmutableArray<ImmutableArray<byte>> expectedKeys = artifactFloors
                    .Select(static floor => floor.ProtectedScheduleKeyDigest).ToImmutableArray();
                byte[] artifactChecksum;
                try { artifactChecksum = Convert.FromHexString(manifest.ProviderPayloadSha256); }
                catch (FormatException) { return RestoreValidation(BaseAdministrationErrorCodes.ArtifactInvalid, "The backup artifact is invalid.", BaseRestoreFailureDisposition.OriginalPreserved); }
                bool validRecovery = request.RecoveryApplicationId is not null
                    && manifestAuthority.SourceStoreInstanceId == manifest.StoreIdentityDigest
                    && manifestAuthority.SourceRestoreEpoch == manifest.RestoreEpoch
                    && BaseScheduleRecoveryManifestContract.Validate(manifestAuthority, new BaseScheduleRecoveryManifestValidation
                    {
                        ApplicationId = request.RecoveryApplicationId, LogicalStoreId = request.StoreId,
                        BackupArtifactId = manifest.ProviderPayloadSha256,
                        BackupArtifactChecksum = artifactChecksum.ToImmutableArray(),
                        AcceptedNow = request.RecoveryAcceptedNow, ExpectedScheduleKeyDigests = expectedKeys,
                    }, request.RecoveryVerificationKeys);
                string nonce = Convert.ToHexStringLower(manifestAuthority.Nonce.AsSpan());
                if (!validRecovery || consumedScheduleRecoveryNonces.Contains(nonce, StringComparer.Ordinal))
                    return RestoreValidation(BaseAdministrationErrorCodes.ArtifactInvalid, "The schedule recovery authority is invalid.", BaseRestoreFailureDisposition.OriginalPreserved);
                selectedScheduleRecoveryFloors = manifestAuthority.Floors.Select(static floor => floor with
                {
                    ProtectedScheduleKeyDigest = floor.ProtectedScheduleKeyDigest.ToArray().ToImmutableArray(),
                    OccurrenceChecksum = floor.OccurrenceChecksum.ToArray().ToImmutableArray(),
                    LatestActivationLineageChecksum = floor.LatestActivationLineageChecksum.ToArray().ToImmutableArray(),
                }).ToImmutableArray();
                consumedScheduleRecoveryNonce = nonce;
            }
            if (!FixedHexEquals(request.ExpectedCurrentStoreIdentityDigest, ActiveIdentity)
                || request.IdentityMode == BaseRestoreIdentityMode.RequireCurrentStoreIdentity
                    && !FixedHexEquals(ActiveIdentity, manifest.StoreIdentityDigest))
                return RestoreConflict(BaseAdministrationErrorCodes.RestoreIdentityMismatch, "Restore identity requirements were not met.", BaseRestoreFailureDisposition.OriginalPreserved);
            if (_quarantinedMutations.Count != 0)
                return RestoreStoreError(BaseAdministrationErrorCodes.RestoreBusy, "The store has unresolved provider work.", BaseRestoreFailureDisposition.OriginalPreserved);
            if (_quarantinedAdministration.Count != 0)
                return RestoreStoreError(BaseAdministrationErrorCodes.RestoreBusy, "The store has unresolved administration work.", BaseRestoreFailureDisposition.OriginalPreserved);

            filePolicy = CaptureFilePolicy(activePath);

            await _administrationOperations.BeforePhaseAsync("beforeCheckpointPathValidation", cancellationToken).ConfigureAwait(false);
            pathGuard.RevalidateActive();
            await CheckpointWalAsync(cancellationToken).ConfigureAwait(false);
            pathGuard.RevalidateActive();
            await CloseKeepAliveForMaintenanceAsync().ConfigureAwait(false);
            using (var anchor = new SqliteConnection(_connections.BuildConnectionString()))
                SqliteConnection.ClearPool(anchor);
            pathGuard.RevalidateActive();
            pathGuard.ValidateSibling(stagingPath, mustExist: true);
            pathGuard.ValidateSibling(recovery, mustExist: false);
            pathGuard.ValidateSibling(RestoreMarkerPath(), mustExist: false);
            WriteRestoreMarker("Prepared", stagingPath, recovery, ActiveIdentity, manifest.StoreIdentityDigest);
            await _administrationOperations.BeforePhaseAsync("beforeOriginalMovePathValidation", cancellationToken).ConfigureAwait(false);
            pathGuard.RevalidateActive();
            File.Move(activePath, recovery);
            MoveIfPresent(activePath + "-wal", recovery + "-wal");
            MoveIfPresent(activePath + "-shm", recovery + "-shm");
            originalMoved = true;
            pathGuard.ValidateSibling(recovery, mustExist: true, expectedDatabaseIdentity: true);
            pathGuard.ValidateSibling(stagingPath, mustExist: true);
            WriteRestoreMarker("OriginalRenamed", staging, recovery, ActiveIdentity, manifest.StoreIdentityDigest);
            await _administrationOperations.BeforePhaseAsync("beforeReplacementInstallPathValidation", cancellationToken).ConfigureAwait(false);
            pathGuard.RevalidateDirectory();
            pathGuard.ValidateSibling(recovery, mustExist: true, expectedDatabaseIdentity: true);
            pathGuard.ValidateSibling(stagingPath, mustExist: true);
            File.Move(stagingPath, activePath);
            staging = null;
            replacementInstalled = true;
            pathGuard.ValidateReplacementActive();
            pathGuard.ValidateSibling(recovery, mustExist: true, expectedDatabaseIdentity: true);
            ApplyFilePolicy(activePath, filePolicy);
            pathGuard.ValidateReplacementActive();
            WriteRestoreMarker("ReplacementInstalled", stagingPath, recovery, ActiveIdentity, manifest.StoreIdentityDigest);
            await _administrationOperations.BeforePhaseAsync("postInstallValidation", CancellationToken.None).ConfigureAwait(false);
            await ValidateDatabaseFileAsync(activePath, manifest, cancellationToken).ConfigureAwait(false);

            long epoch = checked(Math.Max(PreRestoreEpoch, manifest.RestoreEpoch) + 1);
            string installedStoreInstanceId;
            await using (SqliteConnection installed = await _connections.OpenAsync(cancellationToken).ConfigureAwait(false))
            {
                await TransformRestoredSubjectAuthoritiesAsync(
                    installed,
                    epoch,
                    preRestoreSubjectGenerations,
                    preRestoreLifecycleDeliveryEpoch,
                    cancellationToken).ConfigureAwait(false);
                await TransformRestoredActivationAuthoritiesAsync(installed, manifest.RestoreEpoch, epoch, manifest.SchemaGeneration,
                    preRestoreActivationGeneration, selectedScheduleRecoveryFloors,
                    preRestoreSemanticRecovery, request.SemanticRecoveryAuthority, recovery,
                    consumedScheduleRecoveryNonces, consumedScheduleRecoveryNonce, cancellationToken).ConfigureAwait(false);
                installedStoreInstanceId = await ReadStoreInstanceIdAsync(installed, cancellationToken).ConfigureAwait(false)
                    ?? throw new InvalidDataException(BaseSemanticActivationErrorCodes.Corrupt);
            }
            Volatile.Write(ref _schemaGeneration, manifest.SchemaGeneration);
            WriteRestoreMarker("ReplacementValidated", stagingPath, recovery, ActiveIdentity, manifest.StoreIdentityDigest);
            retainRecovery = request.RecoveryImageRetention == BaseRecoveryImageRetention.RetainUntilHostRemoves;
            if (!retainRecovery)
            {
                await _administrationOperations.BeforePhaseAsync("beforeRecoverySetDeletion", CancellationToken.None).ConfigureAwait(false);
                DeleteRecoverySetStrict(recovery);
            }
            else
            {
                pathGuard.ValidateSibling(recovery, mustExist: true, expectedDatabaseIdentity: true);
            }
            WriteRestoreMarker("Completed", stagingPath, recovery, ActiveIdentity, manifest.StoreIdentityDigest);
            await _administrationOperations.BeforePhaseAsync("beforeCompletedMarkerDeletion", CancellationToken.None).ConfigureAwait(false);
            DeleteRequiredFile(RestoreMarkerPath());
            SqliteAdministrationDurability.FlushDirectory(Path.GetDirectoryName(activePath)!);
            await EnsureKeepAliveAsync(CancellationToken.None).ConfigureAwait(false);
            _semanticCertificationOwner?.Rebind(CurrentStoreInstanceId, installedStoreInstanceId);
            Volatile.Write(ref _currentStoreInstanceId, installedStoreInstanceId);
            Volatile.Write(ref _storeInstanceIdentityLoaded, 1);
            return OperationResults.Ok(new BaseRestoreResult
            {
                StoreId = _options.StoreId,
                Status = BaseRestoreStatus.Restored,
                InstalledStoreIdentityDigest = manifest.StoreIdentityDigest,
                RestoreEpoch = epoch,
                RecoveryImageRetained = retainRecovery,
            });
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            bool recovered = await RecoverOriginalAsync(activePath, recovery, originalMoved, replacementInstalled).ConfigureAwait(false);
            return RestoreStoreError(
                recovered ? BaseAdministrationErrorCodes.RestoreTimeout : BaseAdministrationErrorCodes.RestoreIndeterminate,
                recovered ? "Restore exceeded its bounded lifetime." : "The restored store state is indeterminate and unavailable.",
                !originalMoved ? BaseRestoreFailureDisposition.OriginalPreserved
                    : recovered ? BaseRestoreFailureDisposition.RecoveryRestoredOriginal
                    : BaseRestoreFailureDisposition.IndeterminateUnavailable);
        }
        catch (OperationCanceledException)
        {
            _ = await RecoverOriginalAsync(activePath, recovery, originalMoved, replacementInstalled).ConfigureAwait(false);
            throw;
        }
        catch (BackupKeyUnavailableException)
        {
            return RestoreValidation(BaseAdministrationErrorCodes.ArtifactKeyUnavailable, "The artifact authentication key is unavailable.", BaseRestoreFailureDisposition.RejectedBeforeChange);
        }
        catch (BackupArtifactTooLargeException)
        {
            return RestoreValidation(BaseAdministrationErrorCodes.ArtifactTooLarge, "The backup artifact exceeds the configured bound.", BaseRestoreFailureDisposition.RejectedBeforeChange);
        }
        catch (BackupManifestMismatchException)
        {
            bool recovered = await RecoverOriginalAsync(activePath, recovery, originalMoved, replacementInstalled).ConfigureAwait(false);
            return !originalMoved || recovered
                ? RestoreConflict(
                    BaseAdministrationErrorCodes.RestoreIdentityMismatch,
                    "The authenticated manifest does not match the staged database.",
                    !originalMoved ? BaseRestoreFailureDisposition.RejectedBeforeChange : BaseRestoreFailureDisposition.RecoveryRestoredOriginal)
                : RestoreStoreError(
                    BaseAdministrationErrorCodes.RestoreIndeterminate,
                    "The restored store state is indeterminate and unavailable.",
                    BaseRestoreFailureDisposition.IndeterminateUnavailable);
        }
        catch (InvalidDataException exception) when (
            _options.SemanticActivationOwnerGeneration > 0
            && exception.Message == BaseSemanticActivationErrorCodes.MaintenanceIndeterminate)
        {
            _ = await RecoverOriginalAsync(activePath, recovery, originalMoved, replacementInstalled).ConfigureAwait(false);
            return RestoreStoreError(BaseSemanticActivationErrorCodes.MaintenanceIndeterminate,
                "Semantic activation restore publication was interrupted.",
                originalMoved ? BaseRestoreFailureDisposition.IndeterminateUnavailable : BaseRestoreFailureDisposition.OriginalPreserved);
        }
        catch (InvalidDataException exception) when (
            _options.SemanticActivationOwnerGeneration > 0
            && exception.Message is BaseSemanticActivationErrorCodes.Corrupt
                or BaseSemanticActivationErrorCodes.RecoveryProofInvalid)
        {
            bool recovered = await RecoverOriginalAsync(activePath, recovery, originalMoved, replacementInstalled).ConfigureAwait(false);
            return RestoreStoreError(BaseSemanticActivationErrorCodes.RecoveryProofInvalid,
                "Semantic activation recovery authority is invalid.",
                !originalMoved ? BaseRestoreFailureDisposition.OriginalPreserved
                    : recovered ? BaseRestoreFailureDisposition.RecoveryRestoredOriginal
                    : BaseRestoreFailureDisposition.IndeterminateUnavailable);
        }
        catch (Exception) when (!cancellationToken.IsCancellationRequested)
        {
            bool recovered = await RecoverOriginalAsync(activePath, recovery, originalMoved, replacementInstalled).ConfigureAwait(false);
            return RestoreStoreError(
                recovered || !originalMoved ? BaseAdministrationErrorCodes.RestoreFailed : BaseAdministrationErrorCodes.RestoreIndeterminate,
                recovered || !originalMoved ? "Restore failed and the original store was preserved." : "The restored store state is indeterminate and unavailable.",
                !originalMoved ? BaseRestoreFailureDisposition.OriginalPreserved
                    : recovered ? BaseRestoreFailureDisposition.RecoveryRestoredOriginal
                    : BaseRestoreFailureDisposition.IndeterminateUnavailable);
        }
        finally
        {
            Volatile.Write(ref _restoreInstallationActive, 0);
            if (staging is not null) DeleteStaging(staging);
            if (!retainRecovery && !replacementInstalled) DeleteRecoverySet(recovery);
            if (administrationSlot) _administrationExecutionSlots.Release();
        }
    }

    private Task RunNativeBackupAsync(string staging)
    {
        return Task.Run(() =>
        {
            int[] delays = [50, 200, 800];
            for (int attempt = 0; ; attempt++)
            {
                DeleteStaging(staging);
                try
                {
                    using var source = new SqliteConnection(_connections.BuildConnectionString());
                    source.Open();
                    var builder = new SqliteConnectionStringBuilder { DataSource = staging, Mode = SqliteOpenMode.ReadWriteCreate, Pooling = false };
                    using var target = new SqliteConnection(builder.ToString());
                    target.Open();
                    source.BackupDatabase(target);
                    return;
                }
                catch (SqliteException exception) when (exception.SqliteErrorCode is 5 or 6 && attempt < delays.Length)
                {
                    Thread.Sleep(delays[attempt]);
                }
            }
        }, CancellationToken.None);
    }

    private void QuarantineAdministration(Task work, IAsyncDisposable? lease, string staging, string operationKind)
    {
        HPDBaseSqliteLog.AdministrationQuarantined(_logger, operationKind);
        bool semanticActivation = _options.SemanticActivationOwnerGeneration > 0;
        if (semanticActivation)
        {
            Interlocked.Increment(ref _semanticMutationActive);
            Interlocked.Increment(ref _semanticMutationRetained);
            Interlocked.Increment(ref _semanticMutationQuarantined);
        }
        long id = Interlocked.Increment(ref _nextQuarantinedAdministrationId);
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _quarantinedAdministration[id] = completion.Task;
        _ = work.ContinueWith(async antecedent =>
        {
            _ = antecedent.Exception;
            try
            {
                if (lease is not null) await lease.DisposeAsync().ConfigureAwait(false);
                DeleteStaging(staging);
                if (File.Exists(staging))
                    throw new IOException("SQLite administration staging cleanup could not be confirmed.");
                _administrationExecutionSlots.Release();
                _quarantinedAdministration.TryRemove(id, out _);
                if (semanticActivation)
                {
                    Interlocked.Decrement(ref _semanticMutationActive);
                    Interlocked.Decrement(ref _semanticMutationRetained);
                    Interlocked.Decrement(ref _semanticMutationQuarantined);
                    Interlocked.Increment(ref _semanticMutationReleased);
                    Interlocked.Increment(ref _semanticRejectedLateCompletions);
                }
                completion.TrySetResult();
            }
            catch (Exception exception)
            {
                // Failed cleanup deliberately retains the resource root, quarantine entry,
                // and capacity slot. This prevents unbounded admission after cleanup failure.
                completion.TrySetException(exception);
            }
        }, CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default).Unwrap();
    }

    private void TrackAdministrationCompletion(Task work, string operationKind)
    {
        HPDBaseSqliteLog.AdministrationQuarantined(_logger, operationKind);
        long id = Interlocked.Increment(ref _nextQuarantinedAdministrationId);
        _quarantinedAdministration[id] = work;
        _ = work.ContinueWith(
            completed =>
            {
                _ = completed.Exception;
                _quarantinedAdministration.TryRemove(id, out _);
            },
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
    }

    private async ValueTask<BaseBackupManifest> ReadManifestAsync(
        SqliteConnection connection,
        long payloadLength,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = $"SELECT i.store_instance_id, b.baseline_id, b.checksum, b.generation, COALESCE((SELECT CAST(value AS INTEGER) FROM {_names.ProviderState} WHERE key='restore_epoch'),0), sqlite_version() FROM {_names.SchemaIdentity} i JOIN {_names.SchemaBaseline} b ON b.store_instance_id=i.store_instance_id LIMIT 1;";
        command.CommandTimeout = TimeoutSeconds(_options.IntegrityCheckTimeout);
        await using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            throw new InvalidDataException("Required schema metadata is unavailable.");
        string storeInstanceId = reader.GetString(0);
        string baselineId = reader.GetString(1);
        string schemaChecksum = reader.GetString(2);
        long schemaGeneration = reader.GetInt64(3);
        long restoreEpoch = reader.GetInt64(4);
        string sqliteVersion = reader.GetString(5);
        await reader.DisposeAsync().ConfigureAwait(false);
        if (_options.SemanticActivationOwnerGeneration > 0)
        {
            try
            {
                _ = await CaptureSemanticRecoverySnapshotAsync(connection, cancellationToken).ConfigureAwait(false);
                await RequireArtifactNegativeCorrespondenceAsync(connection, null, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception exception) when (exception is InvalidDataException or InvalidOperationException
                or JsonException or FormatException or OverflowException)
            {
                throw new SemanticRecoveryProofException(exception);
            }
        }
        (long semanticSequence, ImmutableArray<byte> semanticChecksum) =
            await ReadSemanticTerminalPublicationAuthorityAsync(connection, cancellationToken).ConfigureAwait(false);
        BaseActivationInstanceReceiptChainState activationReceiptChain =
            await ReadBackupActivationReceiptChainAsync(connection, cancellationToken).ConfigureAwait(false);
        return new BaseBackupManifest
        {
            ActivationInstanceReceiptSequence = activationReceiptChain.CurrentSequence,
            ActivationInstanceReceiptOrderedChecksum = activationReceiptChain.OrderedChecksum,
            SemanticTerminalPublicationSequence = semanticSequence,
            SemanticTerminalPublicationChecksum = semanticChecksum,
            EnvelopeVersion = BackupVersion,
            ProviderKind = "sqlite",
            ProviderVersion = _options.StoreVersion,
            NativeSqliteVersion = sqliteVersion,
            BaseContractVersion = "37",
            StoreIdentityDigest = HexDigest(storeInstanceId),
            SchemaBaselineId = baselineId,
            SchemaChecksum = schemaChecksum,
            SchemaGeneration = schemaGeneration,
            RestoreEpoch = restoreEpoch,
            CreatedAt = _timeProvider.GetUtcNow(),
            ProviderPayloadLength = payloadLength,
            ProviderPayloadSha256 = string.Empty,
            LogicalPartitions = ["records", "schema", "receipts", "journal", "history"],
            ReceiptFormatVersion = 1,
            JournalFormatVersion = 1,
            CollectionHistoryFormatVersion = 1,
            PayloadEncryptedAtRest = false,
            ExternalKeyReferenceKind = null,
        };
    }

    private async ValueTask<BaseActivationInstanceReceiptChainState> ReadBackupActivationReceiptChainAsync(
        SqliteConnection connection,
        CancellationToken cancellationToken)
    {
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandTimeout = TimeoutSeconds(_options.IntegrityCheckTimeout);
        command.CommandText = $"SELECT (SELECT value FROM {_names.ProviderState} WHERE key='activation_instance_receipt_chain_format'),(SELECT value FROM {_names.ProviderState} WHERE key='activation_instance_receipt_chain_sequence'),(SELECT value FROM {_names.ProviderState} WHERE key='activation_instance_receipt_chain_ordered_checksum'),(SELECT value FROM {_names.ProviderState} WHERE key='activation_instance_receipt_chain_generation'),(SELECT value FROM {_names.ProviderState} WHERE key='activation_instance_receipt_chain_checksum');";
        await using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            throw new InvalidDataException("base.activation.receiptCorrupt");
        BaseActivationInstanceReceiptChainState state;
        try
        {
            state = new BaseActivationInstanceReceiptChainState
            {
                FormatVersion = int.Parse(reader.GetString(0), CultureInfo.InvariantCulture),
                CurrentSequence = long.Parse(reader.GetString(1), CultureInfo.InvariantCulture),
                OrderedChecksum = Convert.FromHexString(reader.GetString(2)).ToImmutableArray(),
                Generation = long.Parse(reader.GetString(3), CultureInfo.InvariantCulture),
                Checksum = Convert.FromHexString(reader.GetString(4)).ToImmutableArray(),
            };
        }
        catch (Exception exception) when (exception is FormatException or OverflowException or InvalidCastException)
        {
            throw new InvalidDataException("base.activation.receiptCorrupt", exception);
        }
        if (!BaseActivationInstanceReceiptChainContract.IsValid(state))
            throw new InvalidDataException("base.activation.receiptCorrupt");
        return state;
    }

    private async ValueTask PublishActivationBackupCoverageCheckpointAsync(
        SqliteConnection connection,
        BaseBackupManifest manifest,
        ReadOnlyMemory<byte> artifactSha256,
        CancellationToken cancellationToken)
    {
        await using SqliteTransaction transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        BaseActivationInstanceReceiptChainState chain = await ReadInstanceReceiptChainAsync(
            connection, transaction, cancellationToken).ConfigureAwait(false);
        if (chain.CurrentSequence != manifest.ActivationInstanceReceiptSequence
            || !CryptographicOperations.FixedTimeEquals(
                chain.OrderedChecksum.AsSpan(), manifest.ActivationInstanceReceiptOrderedChecksum.AsSpan()))
            throw new InvalidDataException("base.activation.backupCoverageConflict");

        string storeInstanceId;
        long restoreEpoch;
        await using (SqliteCommand authority = connection.CreateCommand())
        {
            authority.Transaction = transaction;
            authority.CommandText = $"SELECT i.store_instance_id,COALESCE((SELECT CAST(value AS INTEGER) FROM {_names.ProviderState} WHERE key='restore_epoch'),0) FROM {_names.SchemaIdentity} i WHERE i.singleton=1;";
            await using SqliteDataReader reader = await authority.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
                throw new InvalidDataException("base.activation.backupCoverageConflict");
            storeInstanceId = reader.GetString(0);
            restoreEpoch = reader.GetInt64(1);
        }

        string artifactId = Convert.ToHexStringLower(artifactSha256.Span);
        long generation;
        await using (SqliteCommand generationCommand = connection.CreateCommand())
        {
            generationCommand.Transaction = transaction;
            generationCommand.CommandText = $"SELECT COALESCE(MAX(checkpoint_generation),0)+1 FROM {_names.ActivationBackupCoverageCheckpoints};";
            generation = Convert.ToInt64(await generationCommand.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false), CultureInfo.InvariantCulture);
        }
        long committedAt = _timeProvider.GetUtcNow().ToUnixTimeMilliseconds();
        BaseActivationBackupCoverageCheckpoint checkpoint = BaseActivationBackupCoverageCheckpointContract.Create(
            artifactId, artifactSha256.Span, _options.SemanticActivationApplicationId,
            _options.StoreId, storeInstanceId, restoreEpoch, chain.CurrentSequence,
            chain.OrderedChecksum.AsSpan(), generation, committedAt);
        await using SqliteCommand write = connection.CreateCommand();
        write.Transaction = transaction;
        write.CommandText = $"INSERT INTO {_names.ActivationBackupCoverageCheckpoints}(artifact_id,artifact_sha256,application_id,logical_store_id,store_instance_id,restore_epoch,receipt_sequence,receipt_ordered_checksum,checkpoint_generation,committed_at,checkpoint_checksum) VALUES($artifact,$sha,$application,$logical,$instance,$restore,$sequence,$ordered,$generation,$committed,$checksum) ON CONFLICT(artifact_id) DO NOTHING;";
        write.Parameters.AddWithValue("$artifact", checkpoint.ArtifactId);
        write.Parameters.Add("$sha", SqliteType.Blob).Value = checkpoint.ArtifactSha256.ToArray();
        write.Parameters.AddWithValue("$application", checkpoint.ApplicationId);
        write.Parameters.AddWithValue("$logical", checkpoint.LogicalStoreId);
        write.Parameters.AddWithValue("$instance", checkpoint.StoreInstanceId);
        write.Parameters.AddWithValue("$restore", checkpoint.RestoreEpoch);
        write.Parameters.AddWithValue("$sequence", checkpoint.ReceiptSequence);
        write.Parameters.Add("$ordered", SqliteType.Blob).Value = checkpoint.ReceiptOrderedChecksum.ToArray();
        write.Parameters.AddWithValue("$generation", checkpoint.Generation);
        write.Parameters.AddWithValue("$committed", checkpoint.CommittedAt);
        write.Parameters.Add("$checksum", SqliteType.Blob).Value = checkpoint.Checksum.ToArray();
        if (await write.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false) != 1)
            throw new InvalidDataException("base.activation.backupCoverageConflict");
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
    }

    private sealed class SemanticRecoveryProofException(Exception innerException)
        : Exception(BaseSemanticActivationErrorCodes.RecoveryProofInvalid, innerException);

    private async ValueTask<(long Sequence, ImmutableArray<byte> Checksum)> ReadSemanticTerminalPublicationAuthorityAsync(
        SqliteConnection connection, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandTimeout = TimeoutSeconds(_options.IntegrityCheckTimeout);
        command.CommandText = $"SELECT key,value FROM {_names.ProviderState} WHERE key IN ('semantic_terminal_publication_sequence','semantic_terminal_publication_checksum') ORDER BY key;";
        long? sequence = null;
        ImmutableArray<byte> checksum = [];
        await using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            if (reader.GetString(0) == "semantic_terminal_publication_sequence")
                sequence = long.Parse(reader.GetString(1), System.Globalization.CultureInfo.InvariantCulture);
            else checksum = Convert.FromHexString(reader.GetString(1)).ToImmutableArray();
        }
        if (sequence is null or < 0 || checksum.Length != 32
            || sequence == 0 && !CryptographicOperations.FixedTimeEquals(
                checksum.AsSpan(), BaseSemanticRecoveryAuthorityContract.EmptyPublicationSetChecksum().AsSpan()))
            throw new InvalidDataException(BaseSemanticActivationErrorCodes.ProviderContractInvalid);
        return (sequence.Value, checksum);
    }

    private async Task<(BaseBackupManifest Manifest, byte[] ArtifactSha256)> WriteEnvelopeAsync(
        Stream destination,
        string payloadPath,
        BaseBackupManifest initial,
        CancellationToken cancellationToken)
    {
        byte[] digest;
        await using (var payload = new FileStream(payloadPath, FileMode.Open, FileAccess.Read, FileShare.Read, 131072, FileOptions.SequentialScan))
            digest = await SHA256.HashDataAsync(payload, cancellationToken).ConfigureAwait(false);
        BaseBackupManifest manifest = initial with { ProviderPayloadSha256 = Convert.ToHexStringLower(digest) };
        byte[] manifestBytes = JsonSerializer.SerializeToUtf8Bytes(manifest, SqliteAdministrationJsonContext.Default.BaseBackupManifest);
        byte[] header = Header(_tokenProtector!.ActiveKeyId, manifestBytes.Length, manifest.ProviderPayloadLength);
        byte[] authenticated = [.. header, .. manifestBytes, .. digest];
        byte[] tag = _tokenProtector.Authenticate(BackupAuthenticationPurpose, _tokenProtector.ActiveKeyId, authenticated);
        using IncrementalHash artifactHash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        await destination.WriteAsync(header, cancellationToken).ConfigureAwait(false);
        artifactHash.AppendData(header);
        await destination.WriteAsync(manifestBytes, cancellationToken).ConfigureAwait(false);
        artifactHash.AppendData(manifestBytes);
        await using (var payload = new FileStream(payloadPath, FileMode.Open, FileAccess.Read, FileShare.Read, 131072, FileOptions.SequentialScan))
        {
            byte[] buffer = new byte[131072];
            int read;
            while ((read = await payload.ReadAsync(buffer, cancellationToken).ConfigureAwait(false)) != 0)
            {
                await destination.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
                artifactHash.AppendData(buffer.AsSpan(0, read));
            }
        }
        await destination.WriteAsync(tag, cancellationToken).ConfigureAwait(false);
        artifactHash.AppendData(tag);
        return (manifest, artifactHash.GetHashAndReset());
    }

    private async Task<(BaseBackupManifest Manifest, byte KeyId, byte[] Header, byte[] ManifestBytes, byte[] Digest)> ReadEnvelopeAsync(
        Stream source,
        string payloadPath,
        CancellationToken cancellationToken)
    {
        byte[] header = new byte[BackupHeaderLength];
        await ReadExactAsync(source, header, cancellationToken).ConfigureAwait(false);
        if (!header.AsSpan(0, 8).SequenceEqual(BackupMagic)
            || BinaryPrimitives.ReadUInt16BigEndian(header.AsSpan(8, 2)) != BackupVersion
            || header[10] != BackupAuthenticationHmacSha256)
            throw new InvalidDataException();
        byte keyId = header[11];
        if (_tokenProtector?.HasKey(keyId) != true) throw new BackupKeyUnavailableException();
        int manifestLength = checked((int)BinaryPrimitives.ReadUInt32BigEndian(header.AsSpan(12, 4)));
        long payloadLength = checked((long)BinaryPrimitives.ReadUInt64BigEndian(header.AsSpan(16, 8)));
        if (payloadLength > _options.MaxBackupArtifactBytes)
            throw new BackupArtifactTooLargeException();
        if (manifestLength is < 2 or > 1_048_576 || payloadLength < 1)
            throw new InvalidDataException();
        byte[] manifestBytes = new byte[manifestLength];
        await ReadExactAsync(source, manifestBytes, cancellationToken).ConfigureAwait(false);
        BaseBackupManifest manifest = JsonSerializer.Deserialize(manifestBytes, SqliteAdministrationJsonContext.Default.BaseBackupManifest)
            ?? throw new InvalidDataException();
        byte[] canonicalManifest = JsonSerializer.SerializeToUtf8Bytes(manifest, SqliteAdministrationJsonContext.Default.BaseBackupManifest);
        if (!manifestBytes.AsSpan().SequenceEqual(canonicalManifest)) throw new InvalidDataException();
        if (manifest.EnvelopeVersion != BackupVersion || manifest.ProviderPayloadLength != payloadLength || manifest.ProviderKind != "sqlite")
            throw new InvalidDataException();
        if (manifest.BaseContractVersion != "37" || manifest.ReceiptFormatVersion != 1
            || manifest.JournalFormatVersion != 1 || manifest.CollectionHistoryFormatVersion != 1
            || !ValidDigest(manifest.StoreIdentityDigest) || !ValidDigest(manifest.ProviderPayloadSha256)
            || manifest.ActivationInstanceReceiptSequence < 0
            || manifest.ActivationInstanceReceiptOrderedChecksum.Length != 32
            || manifest.ActivationInstanceReceiptSequence == 0 && !CryptographicOperations.FixedTimeEquals(
                manifest.ActivationInstanceReceiptOrderedChecksum.AsSpan(), BaseActivationInstanceReceiptChainContract.ZeroOrderedChecksum.AsSpan())
            || manifest.SemanticTerminalPublicationSequence < 0 || manifest.SemanticTerminalPublicationChecksum.Length != 32
            || manifest.SemanticTerminalPublicationSequence == 0 && !CryptographicOperations.FixedTimeEquals(
                manifest.SemanticTerminalPublicationChecksum.AsSpan(), BaseSemanticRecoveryAuthorityContract.EmptyPublicationSetChecksum().AsSpan())
            || manifest.PayloadEncryptedAtRest || manifest.ExternalKeyReferenceKind is not null)
            throw new InvalidDataException();
        byte[] digest;
        await using (var payload = new FileStream(payloadPath, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None, 131072, FileOptions.SequentialScan))
        {
            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            byte[] buffer = new byte[131072];
            long remaining = payloadLength;
            while (remaining > 0)
            {
                int read = await source.ReadAsync(buffer.AsMemory(0, (int)Math.Min(buffer.Length, remaining)), cancellationToken).ConfigureAwait(false);
                if (read == 0) throw new EndOfStreamException();
                await payload.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
                hash.AppendData(buffer, 0, read);
                remaining -= read;
            }
            await payload.FlushAsync(cancellationToken).ConfigureAwait(false);
            payload.Flush(flushToDisk: true);
            digest = hash.GetHashAndReset();
        }
        byte[] tag = new byte[BackupTagLength];
        await ReadExactAsync(source, tag, cancellationToken).ConfigureAwait(false);
        if (!FixedHexEquals(manifest.ProviderPayloadSha256, Convert.ToHexStringLower(digest))) throw new InvalidDataException();
        byte[] authenticated = [.. header, .. manifestBytes, .. digest];
        byte[] expectedTag = _tokenProtector.Authenticate(BackupAuthenticationPurpose, keyId, authenticated);
        if (!CryptographicOperations.FixedTimeEquals(tag, expectedTag)) throw new InvalidDataException();
        return (manifest, keyId, header, manifestBytes, digest);
    }

    private async Task<SqliteBackupDatabaseFacts> ValidateDatabaseFileAsync(
        string path,
        BaseBackupManifest? manifest,
        CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(_options.IntegrityCheckTimeout);
        var builder = new SqliteConnectionStringBuilder { DataSource = path, Mode = SqliteOpenMode.ReadOnly, Pooling = false };
        await using var connection = new SqliteConnection(builder.ToString());
        await connection.OpenAsync(timeout.Token).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA integrity_check;";
        command.CommandTimeout = TimeoutSeconds(_options.IntegrityCheckTimeout);
        object? integrity = await command.ExecuteScalarAsync(timeout.Token).ConfigureAwait(false);
        if (!string.Equals(integrity as string, "ok", StringComparison.Ordinal))
            throw new InvalidDataException();

        string[] missing = await _schema.GetMissingSchemaPartsAsync(connection, timeout.Token).ConfigureAwait(false);
        if (missing.Length != 0)
            throw new InvalidDataException("The provider schema is incomplete: " + string.Join(",", missing));

        await using var factsCommand = connection.CreateCommand();
        factsCommand.CommandText = $"""
            SELECT i.store_instance_id,
                   b.baseline_id,
                   b.checksum,
                   b.generation,
                   COALESCE((SELECT CAST(value AS INTEGER) FROM {_names.ProviderState} WHERE key='restore_epoch'), -1),
                   sqlite_version(),
                   COALESCE((SELECT CAST(value AS INTEGER) FROM {_names.ProviderState} WHERE key='activation_instance_receipt_chain_sequence'), -1),
                   COALESCE((SELECT value FROM {_names.ProviderState} WHERE key='activation_instance_receipt_chain_ordered_checksum'), '')
            FROM {_names.SchemaIdentity} i
            JOIN {_names.SchemaBaseline} b ON b.store_instance_id=i.store_instance_id;
            """;
        factsCommand.CommandTimeout = TimeoutSeconds(_options.IntegrityCheckTimeout);
        await using SqliteDataReader reader = await factsCommand.ExecuteReaderAsync(timeout.Token).ConfigureAwait(false);
        if (!await reader.ReadAsync(timeout.Token).ConfigureAwait(false))
            throw new InvalidDataException();
        var facts = new SqliteBackupDatabaseFacts(
            HexDigest(reader.GetString(0)),
            reader.GetString(1),
            reader.GetString(2),
            reader.GetInt64(3),
            reader.GetInt64(4),
            reader.GetString(5),
            reader.GetInt64(6),
            reader.GetString(7));
        if (facts.SchemaGeneration < 0 || facts.RestoreEpoch < 0 || facts.ActivationInstanceReceiptSequence < 0
            || !ValidDigest(facts.ActivationInstanceReceiptOrderedChecksum)
            || facts.ActivationInstanceReceiptSequence == 0 && !FixedHexEquals(
                facts.ActivationInstanceReceiptOrderedChecksum,
                Convert.ToHexStringLower(BaseActivationInstanceReceiptChainContract.ZeroOrderedChecksum.AsSpan()))
            || await reader.ReadAsync(timeout.Token).ConfigureAwait(false))
            throw new InvalidDataException();
        if (manifest is not null)
            EnsureManifestMatchesDatabase(manifest, facts);
        return facts;
    }

    private static void EnsureManifestMatchesDatabase(BaseBackupManifest manifest, SqliteBackupDatabaseFacts facts)
    {
        if (!FixedHexEquals(manifest.StoreIdentityDigest, facts.StoreIdentityDigest)
            || !string.Equals(manifest.SchemaBaselineId, facts.SchemaBaselineId, StringComparison.Ordinal)
            || !string.Equals(manifest.SchemaChecksum, facts.SchemaChecksum, StringComparison.Ordinal)
            || manifest.SchemaGeneration != facts.SchemaGeneration
            || manifest.RestoreEpoch != facts.RestoreEpoch
            || manifest.ActivationInstanceReceiptSequence != facts.ActivationInstanceReceiptSequence
            || !FixedHexEquals(Convert.ToHexStringLower(manifest.ActivationInstanceReceiptOrderedChecksum.AsSpan()),
                facts.ActivationInstanceReceiptOrderedChecksum)
            || !string.Equals(manifest.NativeSqliteVersion, facts.NativeSqliteVersion, StringComparison.Ordinal))
            throw new BackupManifestMismatchException();
    }

    private string RandomSiblingPath(string kind)
    {
        var builder = new SqliteConnectionStringBuilder(_connections.BuildConnectionString());
        string full = Path.GetFullPath(builder.DataSource);
        return Path.Combine(Path.GetDirectoryName(full)!, $".{Path.GetFileName(full)}.{kind}.{Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(16))}");
    }

    private string DatabasePath()
    {
        var builder = new SqliteConnectionStringBuilder(_connections.BuildConnectionString());
        return Path.GetFullPath(builder.DataSource);
    }

    private async ValueTask<(string IdentityDigest, long RestoreEpoch)> ReadActiveIdentityAsync(CancellationToken cancellationToken)
    {
        await using SqliteConnection connection = await OpenInitializedAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = $"SELECT i.store_instance_id, COALESCE((SELECT CAST(value AS INTEGER) FROM {_names.ProviderState} WHERE key='restore_epoch'),0) FROM {_names.SchemaIdentity} i LIMIT 1;";
        await using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false)) throw new InvalidDataException();
        string storeInstanceId = reader.GetString(0);
        Volatile.Write(ref _currentStoreInstanceId, storeInstanceId);
        Volatile.Write(ref _storeInstanceIdentityLoaded, 1);
        return (HexDigest(storeInstanceId), reader.GetInt64(1));
    }

    private async ValueTask CloseKeepAliveForMaintenanceAsync()
    {
        await _keepAliveGate.WaitAsync(CancellationToken.None).ConfigureAwait(false);
        try
        {
            if (_keepAliveConnection is not null)
            {
                await _keepAliveConnection.DisposeAsync().ConfigureAwait(false);
                _keepAliveConnection = null;
            }
        }
        finally { _keepAliveGate.Release(); }
    }

    private async ValueTask<bool> RecoverOriginalAsync(string activePath, string recovery, bool originalMoved, bool replacementInstalled)
    {
        if (!originalMoved) return true;
        try
        {
            if (!File.Exists(recovery))
                return false;
            await CloseKeepAliveForMaintenanceAsync().ConfigureAwait(false);
            if (replacementInstalled)
            {
                DeleteRequiredFile(activePath + "-shm");
                DeleteRequiredFile(activePath + "-wal");
                DeleteRequiredFile(activePath);
            }
            if (File.Exists(recovery)) File.Move(recovery, activePath);
            MoveIfPresent(recovery + "-wal", activePath + "-wal");
            MoveIfPresent(recovery + "-shm", activePath + "-shm");
            using (var anchor = new SqliteConnection(_connections.BuildConnectionString())) SqliteConnection.ClearPool(anchor);
            await ValidateDatabaseFileAsync(activePath, null, CancellationToken.None).ConfigureAwait(false);
            await EnsureKeepAliveAsync(CancellationToken.None).ConfigureAwait(false);
            DeleteRequiredFile(RestoreMarkerPath());
            return true;
        }
        catch { return false; }
    }

    private static byte[] Header(byte keyId, int manifestLength, long payloadLength)
    {
        byte[] header = new byte[BackupHeaderLength];
        BackupMagic.CopyTo(header);
        BinaryPrimitives.WriteUInt16BigEndian(header.AsSpan(8, 2), BackupVersion);
        header[10] = BackupAuthenticationHmacSha256;
        header[11] = keyId;
        BinaryPrimitives.WriteUInt32BigEndian(header.AsSpan(12, 4), checked((uint)manifestLength));
        BinaryPrimitives.WriteUInt64BigEndian(header.AsSpan(16, 8), checked((ulong)payloadLength));
        return header;
    }

    private static async Task ReadExactAsync(Stream source, Memory<byte> destination, CancellationToken cancellationToken)
    {
        int offset = 0;
        while (offset < destination.Length)
        {
            int read = await source.ReadAsync(destination[offset..], cancellationToken).ConfigureAwait(false);
            if (read == 0) throw new EndOfStreamException();
            offset += read;
        }
    }

    private bool ValidStoreRequest(string storeId) =>
        string.Equals(storeId, _options.StoreId, StringComparison.Ordinal);

    private bool TryCaptureAdministrationPath(out SqliteAdministrationPathGuard guard)
    {
        try
        {
            guard = SqliteAdministrationPathGuard.Capture(DatabasePath());
            return true;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or NotSupportedException or PlatformNotSupportedException)
        {
            guard = null!;
            return false;
        }
    }

    private static string HexDigest(string value) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
    private static bool FixedHexEquals(string first, string second)
    {
        if (first.Length != 64 || second.Length != 64) return false;
        try { return CryptographicOperations.FixedTimeEquals(Convert.FromHexString(first), Convert.FromHexString(second)); }
        catch (FormatException) { return false; }
    }
    private static bool ValidDigest(string value) => value is { Length: 64 } && value.All(Uri.IsHexDigit);

    private string RestoreMarkerPath() => DatabasePath() + ".restore-state";

    private void WriteRestoreMarker(string state, string staging, string recovery, string currentIdentity, string artifactIdentity)
    {
        string stagingName = Path.GetFileName(staging);
        string recoveryName = Path.GetFileName(recovery);
        string checksum = HexDigest($"1\0{state}\0{stagingName}\0{recoveryName}\0{currentIdentity}\0{artifactIdentity}");
        var marker = new SqliteRestoreMarker
        {
            Version = 1, State = state, StagingName = stagingName, RecoveryName = recoveryName,
            CurrentIdentityDigest = currentIdentity, ArtifactIdentityDigest = artifactIdentity, Checksum = checksum,
        };
        byte[] bytes = JsonSerializer.SerializeToUtf8Bytes(marker, SqliteAdministrationJsonContext.Default.SqliteRestoreMarker);
        string markerPath = RestoreMarkerPath();
        string temporary = markerPath + ".tmp." + Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(8));
        using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
        {
            stream.Write(bytes);
            stream.Flush(flushToDisk: true);
        }
        File.Move(temporary, markerPath, overwrite: true);
        SqliteAdministrationDurability.FlushDirectory(Path.GetDirectoryName(markerPath)!);
    }

    private void RecoverRestoreMarkerIfPresent()
    {
        if (!IsFileBacked(_options)) return;
        string markerPath = RestoreMarkerPath();
        if (!File.Exists(markerPath)) return;
        try
        {
            RecoverRestoreMarkerCore(markerPath);
            Volatile.Write(ref _restoreRecoveryIndeterminate, 0);
        }
        catch (Exception exception) when (exception is InvalidOperationException or InvalidDataException or IOException or UnauthorizedAccessException or JsonException or SqliteException)
        {
            // Recovery evidence is deliberately retained. The provider remains
            // constructible for health/diagnostics but every ordinary open is closed.
            Volatile.Write(ref _restoreRecoveryIndeterminate, 1);
        }
    }

    private void RecoverRestoreMarkerCore(string markerPath)
    {
        SqliteAdministrationPathGuard pathGuard = SqliteAdministrationPathGuard.Capture(DatabasePath(), activeRequired: false);
        pathGuard.ValidateSibling(markerPath, mustExist: true);
        SqliteRestoreMarker marker;
        try
        {
            marker = JsonSerializer.Deserialize(
                File.ReadAllBytes(markerPath),
                SqliteAdministrationJsonContext.Default.SqliteRestoreMarker)
                ?? throw new InvalidOperationException("SQLite restore recovery state is invalid.");
        }
        catch (Exception exception) when (exception is JsonException or IOException or UnauthorizedAccessException)
        {
            throw new InvalidOperationException("SQLite restore recovery state is invalid.");
        }
        string expected = HexDigest($"{marker.Version}\0{marker.State}\0{marker.StagingName}\0{marker.RecoveryName}\0{marker.CurrentIdentityDigest}\0{marker.ArtifactIdentityDigest}");
        if (marker.Version != 1 || !FixedHexEquals(marker.Checksum, expected)
            || Path.GetFileName(marker.StagingName) != marker.StagingName || Path.GetFileName(marker.RecoveryName) != marker.RecoveryName)
            throw new InvalidOperationException("SQLite restore recovery state is invalid.");
        string directory = Path.GetDirectoryName(DatabasePath())!;
        string staging = Path.Combine(directory, marker.StagingName);
        string recovery = Path.Combine(directory, marker.RecoveryName);
        string active = DatabasePath();
        pathGuard.ValidateSibling(staging, mustExist: File.Exists(staging));
        pathGuard.ValidateSibling(recovery, mustExist: File.Exists(recovery));
        pathGuard.RevalidateDirectory();
        switch (marker.State)
        {
            case "Prepared":
                DeleteRequiredFile(staging);
                break;
            case "OriginalRenamed":
            case "ReplacementInstalled":
                if (!File.Exists(recovery)) throw new InvalidOperationException("SQLite restore recovery image is unavailable.");
                pathGuard.ValidateSibling(recovery, mustExist: true);
                ValidateDatabaseFileAsync(recovery, null, CancellationToken.None).GetAwaiter().GetResult();
                if (File.Exists(active)) DeleteRequiredFile(active);
                pathGuard.RevalidateDirectory();
                File.Copy(recovery, active, overwrite: false);
                CopyIfPresent(recovery + "-wal", active + "-wal");
                CopyIfPresent(recovery + "-shm", active + "-shm");
                ValidateDatabaseFileAsync(active, null, CancellationToken.None).GetAwaiter().GetResult();
                DeleteRecoverySetStrict(recovery);
                DeleteRequiredFile(staging);
                break;
            case "ReplacementValidated":
            case "Completed":
                DeleteRequiredFile(staging);
                break;
            default:
                throw new InvalidOperationException("SQLite restore recovery state is invalid.");
        }
        DeleteRequiredFile(markerPath);
    }

    private async ValueTask CheckpointWalAsync(CancellationToken cancellationToken)
    {
        await using SqliteConnection connection = await OpenInitializedAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA wal_checkpoint(TRUNCATE);";
        command.CommandTimeout = TimeoutSeconds(_options.AdministrationAcquisitionTimeout);
        await using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false) || reader.GetInt32(0) != 0)
            throw new InvalidOperationException("SQLite WAL checkpoint did not complete.");
    }

    private static void MoveIfPresent(string source, string destination)
    {
        if (File.Exists(source)) File.Move(source, destination);
    }

    private static void CopyIfPresent(string source, string destination)
    {
        if (File.Exists(source)) File.Copy(source, destination, overwrite: false);
    }

    private static void DeleteRecoverySet(string recovery)
    {
        DeleteStaging(recovery);
        DeleteStaging(recovery + "-wal");
        DeleteStaging(recovery + "-shm");
    }

    private void DeleteRecoverySetStrict(string recovery)
    {
        DeleteRequiredFile(recovery);
        DeleteRequiredFile(recovery + "-wal");
        DeleteRequiredFile(recovery + "-shm");
    }

    private void DeleteRequiredFile(string path)
    {
        if (!File.Exists(path)) return;
        _administrationOperations.DeleteFile(path);
        if (File.Exists(path))
            throw new IOException("SQLite administration cleanup could not be confirmed.");
    }

    [System.Runtime.Versioning.UnsupportedOSPlatform("windows")]
    private static void ApplyUnixMode(string path, UnixFileMode mode)
    {
        File.SetUnixFileMode(path, mode);
        if (File.GetUnixFileMode(path) != mode) throw new UnauthorizedAccessException();
    }

    private static RestoreFilePolicy CaptureFilePolicy(string path)
    {
        if (!OperatingSystem.IsWindows())
            return new RestoreFilePolicy(File.GetUnixFileMode(path), null, null);
        FileInfo file = new(path);
        byte[] descriptor = file.GetAccessControl(AccessControlSections.All).GetSecurityDescriptorBinaryForm();
        return new RestoreFilePolicy(null, file.Attributes, descriptor);
    }

    private static void ApplyFilePolicy(string path, RestoreFilePolicy policy)
    {
        if (!OperatingSystem.IsWindows())
        {
            ApplyUnixMode(path, policy.UnixMode!.Value);
            return;
        }
        var security = new FileSecurity();
        security.SetSecurityDescriptorBinaryForm(policy.WindowsSecurityDescriptor!);
        var file = new FileInfo(path);
        file.SetAccessControl(security);
        file.Attributes = policy.WindowsAttributes!.Value;
        byte[] installed = file.GetAccessControl(AccessControlSections.All).GetSecurityDescriptorBinaryForm();
        if (!installed.AsSpan().SequenceEqual(policy.WindowsSecurityDescriptor))
            throw new UnauthorizedAccessException("SQLite restore file security policy could not be preserved.");
    }

    private sealed record RestoreFilePolicy(
        UnixFileMode? UnixMode,
        FileAttributes? WindowsAttributes,
        byte[]? WindowsSecurityDescriptor);
    private static void DeleteStaging(string path) { try { if (File.Exists(path)) File.Delete(path); } catch (IOException) { } catch (UnauthorizedAccessException) { } }

    private static OperationResult<T> AdminUnsupported<T>() => OperationResults.CapabilityUnavailable<T>(AdminError(BaseAdministrationErrorCodes.CapabilityUnavailable, "SQLite administration is unavailable.", ErrorCategory.Capability));
    private static OperationResult<T> AdminValidation<T>(string code, string message) => OperationResults.ValidationFailed<T>(AdminError(code, message, ErrorCategory.Validation));
    private static OperationResult<T> AdminConflict<T>(string code, string message) => OperationResults.Conflict<T>(AdminError(code, message, ErrorCategory.Conflict));
    private static OperationResult<T> AdminStoreError<T>(string code, string message) => OperationResults.StoreError<T>(AdminError(code, message, ErrorCategory.Store));
    private static OperationResult<BaseRestoreResult> RestoreValidation(string code, string message, BaseRestoreFailureDisposition disposition) =>
        OperationResults.ValidationFailed<BaseRestoreResult>(AdminError(code, message, ErrorCategory.Validation, disposition));
    private static OperationResult<BaseRestoreResult> RestoreConflict(string code, string message, BaseRestoreFailureDisposition disposition) =>
        OperationResults.Conflict<BaseRestoreResult>(AdminError(code, message, ErrorCategory.Conflict, disposition));
    private static OperationResult<BaseRestoreResult> RestoreStoreError(string code, string message, BaseRestoreFailureDisposition disposition) =>
        OperationResults.StoreError<BaseRestoreResult>(AdminError(code, message, ErrorCategory.Store, disposition));
    private static BaseError AdminError(
        string code,
        string message,
        ErrorCategory category,
        BaseRestoreFailureDisposition? restoreFailureDisposition = null) => new()
        {
            Code = code,
            Message = message,
            Category = category,
            Store = category == ErrorCategory.Store ? new StoreErrorInfo { Retryable = false } : null,
            RestoreFailureDisposition = restoreFailureDisposition,
        };
    private sealed class BackupKeyUnavailableException : Exception;
    private sealed class BackupArtifactTooLargeException : Exception;
    private sealed class BackupManifestMismatchException : Exception;
    private sealed record SqliteBackupDatabaseFacts(
        string StoreIdentityDigest,
        string SchemaBaselineId,
        string SchemaChecksum,
        long SchemaGeneration,
        long RestoreEpoch,
        string NativeSqliteVersion,
        long ActivationInstanceReceiptSequence,
        string ActivationInstanceReceiptOrderedChecksum);
}
