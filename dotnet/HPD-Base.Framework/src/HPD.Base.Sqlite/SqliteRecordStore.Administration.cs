using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
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
    public async ValueTask<OperationResult<BaseBackupManifest>> CreateBackupAsync(
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
        if (!OwnedRegularDatabasePath())
            return AdminUnsupported<BaseBackupManifest>();

        string? staging = RandomSiblingPath("backup");
        IAsyncDisposable? lease = null;
        bool slot = false;
        try
        {
            using var acquisition = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            acquisition.CancelAfter(_options.AdministrationAcquisitionTimeout);
            await _administrationExecutionSlots.WaitAsync(acquisition.Token).ConfigureAwait(false);
            slot = true;
            lease = await _schemaGenerationGate.AcquireExclusiveAsync(acquisition.Token).ConfigureAwait(false);
            await EnsureKeepAliveAsync(acquisition.Token).ConfigureAwait(false);

            Task native = RunNativeBackupAsync(staging);
            try
            {
                await native.WaitAsync(_options.NativeBackupCompletionWait, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) { QuarantineAdministration(native, lease, staging); lease = null; staging = null; slot = false; throw; }
            catch (TimeoutException) { QuarantineAdministration(native, lease, staging); lease = null; staging = null; slot = false; throw new OperationCanceledException(); }

            await ValidateDatabaseFileAsync(staging, cancellationToken).ConfigureAwait(false);
            await using SqliteConnection source = await OpenInitializedAsync(cancellationToken).ConfigureAwait(false);
            BaseBackupManifest manifest = await ReadManifestAsync(source, new FileInfo(staging).Length, cancellationToken).ConfigureAwait(false);

            if (request.ExpectedStoreIdentityDigest is { } expected
                && !FixedHexEquals(expected, manifest.StoreIdentityDigest))
                return AdminConflict<BaseBackupManifest>(BaseAdministrationErrorCodes.ArtifactIdentityMismatch, "The active store identity does not match the request.");

            await WriteEnvelopeAsync(destination, staging, manifest, cancellationToken).ConfigureAwait(false);
            return OperationResults.Ok(manifest);
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
    public async ValueTask<OperationResult<BaseBackupManifest>> ValidateBackupAsync(
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

        string? staging = RandomSiblingPath("validation");
        bool slot = false;
        try
        {
            using var acquisition = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            acquisition.CancelAfter(_options.AdministrationAcquisitionTimeout);
            await _administrationExecutionSlots.WaitAsync(acquisition.Token).ConfigureAwait(false);
            slot = true;
            (BaseBackupManifest Manifest, byte KeyId, byte[] Header, byte[] ManifestBytes, byte[] Digest) artifact =
                await ReadEnvelopeAsync(source, staging!, cancellationToken).ConfigureAwait(false);
            if (request.ExpectedArtifactStoreIdentityDigest is { } expected
                && !FixedHexEquals(expected, artifact.Manifest.StoreIdentityDigest))
                return AdminConflict<BaseBackupManifest>(BaseAdministrationErrorCodes.ArtifactIdentityMismatch, "The artifact store identity does not match the request.");
            string validationPath = staging!;
            Task validation = Task.Run(async () => await ValidateDatabaseFileAsync(validationPath, CancellationToken.None).ConfigureAwait(false), CancellationToken.None);
            try { await validation.WaitAsync(_options.IntegrityCheckTimeout, cancellationToken).ConfigureAwait(false); }
            catch (OperationCanceledException) { QuarantineAdministration(validation, null, staging); staging = null; slot = false; throw; }
            catch (TimeoutException) { QuarantineAdministration(validation, null, staging); staging = null; slot = false; throw new OperationCanceledException(); }
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
    public async ValueTask<OperationResult<BaseRestoreResult>> RestoreAsync(
        Stream source,
        BaseRestoreRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(request);
        if (!AdministrationCapability.Restore || _tokenProtector is null)
            return AdminUnsupported<BaseRestoreResult>();
        if (!source.CanRead || !ValidStoreRequest(request.StoreId)
            || !Enum.IsDefined(request.IdentityMode) || !Enum.IsDefined(request.RecoveryImageRetention))
            return AdminValidation<BaseRestoreResult>(BaseAdministrationErrorCodes.Invalid, "The restore request is invalid.");
        if (!OwnedRegularDatabasePath())
            return AdminUnsupported<BaseRestoreResult>();
        if (!request.ConfirmDestructiveReplacement)
            return AdminValidation<BaseRestoreResult>(BaseAdministrationErrorCodes.RestoreConfirmationRequired, "Destructive replacement was not confirmed.");
        if (!ValidDigest(request.ExpectedCurrentStoreIdentityDigest) || !ValidDigest(request.ExpectedArtifactStoreIdentityDigest))
            return AdminValidation<BaseRestoreResult>(BaseAdministrationErrorCodes.Invalid, "Restore identity digests are invalid.");

        string staging = RandomSiblingPath("restore");
        string activePath = DatabasePath();
        string recovery = activePath + ".recovery." + Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(16));
        bool originalMoved = false;
        bool replacementInstalled = false;
        bool retainRecovery = false;
        bool administrationSlot = false;
        UnixFileMode? unixMode = null;
        FileAttributes? windowsAttributes = null;
        try
        {
            using var slotAcquisition = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            slotAcquisition.CancelAfter(_options.AdministrationAcquisitionTimeout);
            await _administrationExecutionSlots.WaitAsync(slotAcquisition.Token).ConfigureAwait(false);
            administrationSlot = true;
            (BaseBackupManifest manifest, _, _, _, _) = await ReadEnvelopeAsync(source, staging, cancellationToken).ConfigureAwait(false);
            if (!FixedHexEquals(request.ExpectedArtifactStoreIdentityDigest, manifest.StoreIdentityDigest))
                return AdminConflict<BaseRestoreResult>(BaseAdministrationErrorCodes.RestoreIdentityMismatch, "The artifact identity does not match the restore request.");
            await ValidateDatabaseFileAsync(staging, cancellationToken).ConfigureAwait(false);

            using var acquisition = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            acquisition.CancelAfter(_options.AdministrationAcquisitionTimeout);
            await using IAsyncDisposable lease = await _schemaGenerationGate.AcquireExclusiveAsync(acquisition.Token).ConfigureAwait(false);
            (string ActiveIdentity, long PreRestoreEpoch) = await ReadActiveIdentityAsync(acquisition.Token).ConfigureAwait(false);
            if (!FixedHexEquals(request.ExpectedCurrentStoreIdentityDigest, ActiveIdentity)
                || request.IdentityMode == BaseRestoreIdentityMode.RequireCurrentStoreIdentity
                    && !FixedHexEquals(ActiveIdentity, manifest.StoreIdentityDigest))
                return AdminConflict<BaseRestoreResult>(BaseAdministrationErrorCodes.RestoreIdentityMismatch, "Restore identity requirements were not met.");
            if (_quarantinedMutations.Count != 0)
                return AdminStoreError<BaseRestoreResult>(BaseAdministrationErrorCodes.RestoreBusy, "The store has unresolved provider work.");
            if (_quarantinedAdministration.Count != 0)
                return AdminStoreError<BaseRestoreResult>(BaseAdministrationErrorCodes.RestoreBusy, "The store has unresolved administration work.");

            if (OperatingSystem.IsWindows()) windowsAttributes = File.GetAttributes(activePath);
            else unixMode = File.GetUnixFileMode(activePath);

            WriteRestoreMarker("Prepared", staging, recovery, ActiveIdentity, manifest.StoreIdentityDigest);
            await CheckpointWalAsync(cancellationToken).ConfigureAwait(false);
            await CloseKeepAliveForMaintenanceAsync().ConfigureAwait(false);
            using (var anchor = new SqliteConnection(_connections.BuildConnectionString()))
                SqliteConnection.ClearPool(anchor);
            File.Move(activePath, recovery);
            MoveIfPresent(activePath + "-wal", recovery + "-wal");
            MoveIfPresent(activePath + "-shm", recovery + "-shm");
            originalMoved = true;
            WriteRestoreMarker("OriginalRenamed", staging, recovery, ActiveIdentity, manifest.StoreIdentityDigest);
            File.Move(staging, activePath);
            replacementInstalled = true;
            if (!OperatingSystem.IsWindows() && unixMode is { } mode)
            {
                ApplyUnixMode(activePath, mode);
            }
            else if (windowsAttributes is { } attributes)
            {
                File.SetAttributes(activePath, attributes);
            }
            WriteRestoreMarker("ReplacementInstalled", staging, recovery, ActiveIdentity, manifest.StoreIdentityDigest);
            await ValidateDatabaseFileAsync(activePath, cancellationToken).ConfigureAwait(false);

            long epoch = checked(Math.Max(PreRestoreEpoch, manifest.RestoreEpoch) + 1);
            var installedBuilder = new SqliteConnectionStringBuilder { DataSource = activePath, Mode = SqliteOpenMode.ReadWrite, Pooling = false };
            await using (var installed = new SqliteConnection(installedBuilder.ToString()))
            {
                await installed.OpenAsync(cancellationToken).ConfigureAwait(false);
                await using var update = installed.CreateCommand();
                update.CommandText = $"INSERT INTO {_names.ProviderState}(key,value) VALUES ('restore_epoch',$epoch) ON CONFLICT(key) DO UPDATE SET value=excluded.value;";
                update.Parameters.AddWithValue("$epoch", epoch.ToString(System.Globalization.CultureInfo.InvariantCulture));
                await update.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            }
            Volatile.Write(ref _schemaGeneration, manifest.SchemaGeneration);
            WriteRestoreMarker("ReplacementValidated", staging, recovery, ActiveIdentity, manifest.StoreIdentityDigest);
            retainRecovery = request.RecoveryImageRetention == BaseRecoveryImageRetention.RetainUntilHostRemoves;
            if (!retainRecovery) DeleteRecoverySet(recovery);
            WriteRestoreMarker("Completed", staging, recovery, ActiveIdentity, manifest.StoreIdentityDigest);
            DeleteStaging(RestoreMarkerPath());
            await EnsureKeepAliveAsync(CancellationToken.None).ConfigureAwait(false);
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
            await RecoverOriginalAsync(activePath, recovery, originalMoved, replacementInstalled).ConfigureAwait(false);
            return AdminStoreError<BaseRestoreResult>(BaseAdministrationErrorCodes.RestoreTimeout, "Restore exceeded its bounded lifetime.");
        }
        catch (OperationCanceledException)
        {
            await RecoverOriginalAsync(activePath, recovery, originalMoved, replacementInstalled).ConfigureAwait(false);
            throw;
        }
        catch (BackupKeyUnavailableException)
        {
            return AdminValidation<BaseRestoreResult>(BaseAdministrationErrorCodes.ArtifactKeyUnavailable, "The artifact authentication key is unavailable.");
        }
        catch (BackupArtifactTooLargeException)
        {
            return AdminValidation<BaseRestoreResult>(BaseAdministrationErrorCodes.ArtifactTooLarge, "The backup artifact exceeds the configured bound.");
        }
        catch (Exception) when (!cancellationToken.IsCancellationRequested)
        {
            bool recovered = await RecoverOriginalAsync(activePath, recovery, originalMoved, replacementInstalled).ConfigureAwait(false);
            return AdminStoreError<BaseRestoreResult>(
                recovered || !originalMoved ? BaseAdministrationErrorCodes.RestoreFailed : BaseAdministrationErrorCodes.RestoreIndeterminate,
                recovered || !originalMoved ? "Restore failed and the original store was preserved." : "The restored store state is indeterminate and unavailable.");
        }
        finally
        {
            DeleteStaging(staging);
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

    private void QuarantineAdministration(Task work, IAsyncDisposable? lease, string staging)
    {
        long id = Interlocked.Increment(ref _nextQuarantinedAdministrationId);
        Task cleanup = work.ContinueWith(async antecedent =>
        {
            _ = antecedent.Exception;
            try { if (lease is not null) await lease.DisposeAsync().ConfigureAwait(false); }
            finally
            {
                DeleteStaging(staging);
                _administrationExecutionSlots.Release();
                _quarantinedAdministration.TryRemove(id, out _);
            }
        }, CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default).Unwrap();
        _quarantinedAdministration[id] = cleanup;
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
        return new BaseBackupManifest
        {
            EnvelopeVersion = BackupVersion,
            ProviderKind = "sqlite",
            ProviderVersion = _options.StoreVersion,
            NativeSqliteVersion = reader.GetString(5),
            BaseContractVersion = "37",
            StoreIdentityDigest = HexDigest(reader.GetString(0)),
            SchemaBaselineId = reader.GetString(1),
            SchemaChecksum = reader.GetString(2),
            SchemaGeneration = reader.GetInt64(3),
            RestoreEpoch = reader.GetInt64(4),
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

    private async Task WriteEnvelopeAsync(Stream destination, string payloadPath, BaseBackupManifest initial, CancellationToken cancellationToken)
    {
        byte[] digest;
        await using (var payload = new FileStream(payloadPath, FileMode.Open, FileAccess.Read, FileShare.Read, 131072, FileOptions.SequentialScan))
            digest = await SHA256.HashDataAsync(payload, cancellationToken).ConfigureAwait(false);
        BaseBackupManifest manifest = initial with { ProviderPayloadSha256 = Convert.ToHexStringLower(digest) };
        byte[] manifestBytes = JsonSerializer.SerializeToUtf8Bytes(manifest, SqliteAdministrationJsonContext.Default.BaseBackupManifest);
        byte[] header = Header(_tokenProtector!.ActiveKeyId, manifestBytes.Length, manifest.ProviderPayloadLength);
        byte[] authenticated = [.. header, .. manifestBytes, .. digest];
        byte[] tag = _tokenProtector.Authenticate(BackupAuthenticationPurpose, _tokenProtector.ActiveKeyId, authenticated);
        await destination.WriteAsync(header, cancellationToken).ConfigureAwait(false);
        await destination.WriteAsync(manifestBytes, cancellationToken).ConfigureAwait(false);
        await using (var payload = new FileStream(payloadPath, FileMode.Open, FileAccess.Read, FileShare.Read, 131072, FileOptions.SequentialScan))
            await payload.CopyToAsync(destination, 131072, cancellationToken).ConfigureAwait(false);
        await destination.WriteAsync(tag, cancellationToken).ConfigureAwait(false);
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

    private async Task ValidateDatabaseFileAsync(string path, CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(_options.IntegrityCheckTimeout);
        var builder = new SqliteConnectionStringBuilder { DataSource = path, Mode = SqliteOpenMode.ReadOnly, Pooling = false };
        await using var connection = new SqliteConnection(builder.ToString());
        await connection.OpenAsync(timeout.Token).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = $"PRAGMA integrity_check; SELECT COUNT(*) FROM sqlite_schema WHERE type='table' AND name IN ('{_names.Collections}','{_names.ProviderState}','{_names.MutationJournal}','{_names.OperationReceipts}','{_names.SchemaIdentity}','{_names.SchemaBaseline}');";
        command.CommandTimeout = TimeoutSeconds(_options.IntegrityCheckTimeout);
        await using SqliteDataReader reader = await command.ExecuteReaderAsync(timeout.Token).ConfigureAwait(false);
        if (!await reader.ReadAsync(timeout.Token).ConfigureAwait(false) || !string.Equals(reader.GetString(0), "ok", StringComparison.Ordinal)) throw new InvalidDataException();
        if (!await reader.NextResultAsync(timeout.Token).ConfigureAwait(false) || !await reader.ReadAsync(timeout.Token).ConfigureAwait(false) || reader.GetInt64(0) != 6) throw new InvalidDataException();
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
        return (HexDigest(reader.GetString(0)), reader.GetInt64(1));
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
            await CloseKeepAliveForMaintenanceAsync().ConfigureAwait(false);
            if (replacementInstalled && File.Exists(activePath)) DeleteStaging(activePath);
            if (File.Exists(recovery)) File.Move(recovery, activePath);
            MoveIfPresent(recovery + "-wal", activePath + "-wal");
            MoveIfPresent(recovery + "-shm", activePath + "-shm");
            using (var anchor = new SqliteConnection(_connections.BuildConnectionString())) SqliteConnection.ClearPool(anchor);
            await ValidateDatabaseFileAsync(activePath, CancellationToken.None).ConfigureAwait(false);
            await EnsureKeepAliveAsync(CancellationToken.None).ConfigureAwait(false);
            DeleteStaging(RestoreMarkerPath());
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

    private bool OwnedRegularDatabasePath()
    {
        try
        {
            string path = DatabasePath();
            if (!File.Exists(path)) return false;
            FileAttributes attributes = File.GetAttributes(path);
            return (attributes & (FileAttributes.Directory | FileAttributes.ReparsePoint)) == 0
                && new FileInfo(path).LinkTarget is null;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or NotSupportedException)
        {
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
    }

    private void RecoverRestoreMarkerIfPresent()
    {
        if (!IsFileBacked(_options)) return;
        string markerPath = RestoreMarkerPath();
        if (!File.Exists(markerPath)) return;
        SqliteRestoreMarker marker = JsonSerializer.Deserialize(File.ReadAllBytes(markerPath), SqliteAdministrationJsonContext.Default.SqliteRestoreMarker)
            ?? throw new InvalidOperationException("SQLite restore recovery state is invalid.");
        string expected = HexDigest($"{marker.Version}\0{marker.State}\0{marker.StagingName}\0{marker.RecoveryName}\0{marker.CurrentIdentityDigest}\0{marker.ArtifactIdentityDigest}");
        if (marker.Version != 1 || !FixedHexEquals(marker.Checksum, expected)
            || Path.GetFileName(marker.StagingName) != marker.StagingName || Path.GetFileName(marker.RecoveryName) != marker.RecoveryName)
            throw new InvalidOperationException("SQLite restore recovery state is invalid.");
        string directory = Path.GetDirectoryName(DatabasePath())!;
        string staging = Path.Combine(directory, marker.StagingName);
        string recovery = Path.Combine(directory, marker.RecoveryName);
        string active = DatabasePath();
        switch (marker.State)
        {
            case "Prepared":
                DeleteStaging(staging);
                break;
            case "OriginalRenamed":
            case "ReplacementInstalled":
                if (File.Exists(active)) DeleteStaging(active);
                if (!File.Exists(recovery)) throw new InvalidOperationException("SQLite restore recovery image is unavailable.");
                File.Move(recovery, active);
                MoveIfPresent(recovery + "-wal", active + "-wal");
                MoveIfPresent(recovery + "-shm", active + "-shm");
                DeleteStaging(staging);
                break;
            case "ReplacementValidated":
            case "Completed":
                DeleteStaging(staging);
                break;
            default:
                throw new InvalidOperationException("SQLite restore recovery state is invalid.");
        }
        DeleteStaging(markerPath);
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

    private static void DeleteRecoverySet(string recovery)
    {
        DeleteStaging(recovery);
        DeleteStaging(recovery + "-wal");
        DeleteStaging(recovery + "-shm");
    }

    [System.Runtime.Versioning.UnsupportedOSPlatform("windows")]
    private static void ApplyUnixMode(string path, UnixFileMode mode)
    {
        File.SetUnixFileMode(path, mode);
        if (File.GetUnixFileMode(path) != mode) throw new UnauthorizedAccessException();
    }
    private static void DeleteStaging(string path) { try { if (File.Exists(path)) File.Delete(path); } catch (IOException) { } catch (UnauthorizedAccessException) { } }

    private static OperationResult<T> AdminUnsupported<T>() => OperationResults.CapabilityUnavailable<T>(AdminError(BaseAdministrationErrorCodes.CapabilityUnavailable, "SQLite administration is unavailable.", ErrorCategory.Capability));
    private static OperationResult<T> AdminValidation<T>(string code, string message) => OperationResults.ValidationFailed<T>(AdminError(code, message, ErrorCategory.Validation));
    private static OperationResult<T> AdminConflict<T>(string code, string message) => OperationResults.Conflict<T>(AdminError(code, message, ErrorCategory.Conflict));
    private static OperationResult<T> AdminStoreError<T>(string code, string message) => OperationResults.StoreError<T>(AdminError(code, message, ErrorCategory.Store));
    private static BaseError AdminError(string code, string message, ErrorCategory category) => new() { Code = code, Message = message, Category = category, Store = category == ErrorCategory.Store ? new StoreErrorInfo { Retryable = false } : null };
    private sealed class BackupKeyUnavailableException : Exception;
    private sealed class BackupArtifactTooLargeException : Exception;
}
