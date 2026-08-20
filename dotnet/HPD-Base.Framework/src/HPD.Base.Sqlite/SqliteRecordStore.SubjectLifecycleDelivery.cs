using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text;
using HPD.Base;
using Microsoft.Data.Sqlite;

namespace HPD.Base.Sqlite;

public sealed partial class SqliteRecordStore
{
    /// <inheritdoc />
    public ValueTask<RecordMutationExecutionResult> AdvanceCheckpointAsync(
        IAtomicMutationProcessor processor,
        RecordMutationExecutionRequest execution,
        CancellationToken cancellationToken = default) =>
        ExecuteAtomicAsync(processor, execution, cancellationToken);

    /// <inheritdoc />
    public async ValueTask<OperationResult<BaseSubjectLifecycleProviderPage>> ReadAsync(
        BaseSubjectLifecycleProviderReadRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.Take is < 1 or > 256 || request.MaximumResultBytes is < 1 or > 1_048_576 || request.DeadlineUtc <= _timeProvider.GetUtcNow())
            return LifecycleReadFailure(BaseSubjectErrorCodes.LifecycleContractInvalid, OperationStatus.ValidationFailed, ErrorCategory.Validation);
        BaseExportedSubjectDefinition? contract = _options.ExportedSubjects.SingleOrDefault(value => value.Id == request.ContractId && value.Version == request.ContractVersion);
        BaseSubjectLifecycleConsumerDefinition? consumer = _options.SubjectLifecycleConsumers.SingleOrDefault(value => value.Id == request.ConsumerId && value.Version == request.ConsumerVersion);
        if (contract is null || consumer is null || !string.Equals(BaseSubjectContractGraph.Checksum(contract), request.ContractChecksum, StringComparison.Ordinal)
            || !string.Equals(BaseSubjectLifecycleRegistry.Checksum(BaseSubjectLifecycleRegistry.Normalize(consumer), BaseSubjectContractGraph.Checksum(contract)), request.ConsumerChecksum, StringComparison.Ordinal)
            || _subjectScopes is null)
            return LifecycleReadFailure(BaseSubjectErrorCodes.LifecycleProviderContractInvalid, OperationStatus.CapabilityUnavailable, ErrorCategory.Capability);
        BaseProtectedSubjectScope protectedScope = _subjectScopes.Protect(request.Scope, _subjectScopeProtectionKey!.Value);

        await using IAsyncDisposable generationLease = await _schemaGenerationGate.AcquireSharedAsync(cancellationToken).ConfigureAwait(false);
        if (RestoreRecoveryIndeterminate || RestoreRecoveryPending)
            return LifecycleReadFailure(BaseSubjectErrorCodes.MaintenanceRequired, OperationStatus.CapabilityUnavailable, ErrorCategory.Capability);
        await using SqliteConnection connection = await _connections.OpenAsync(cancellationToken).ConfigureAwait(false);
        if (await LifecycleMaintenanceActiveAsync(connection, cancellationToken).ConfigureAwait(false))
            return LifecycleReadFailure(BaseSubjectErrorCodes.MaintenanceRequired, OperationStatus.CapabilityUnavailable, ErrorCategory.Capability);
        long installedProjectionGeneration;
        await using (SqliteCommand installed = connection.CreateCommand())
        {
            installed.CommandTimeout = TimeoutSeconds();
            installed.CommandText = $"SELECT consumer_checksum,contract_id,contract_version,projection_generation,state FROM {_names.SubjectLifecycleConsumers} WHERE consumer_id=$consumer AND consumer_version=$version;";
            installed.Parameters.AddWithValue("$consumer", request.ConsumerId); installed.Parameters.AddWithValue("$version", request.ConsumerVersion);
            await using SqliteDataReader installedReader = await installed.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            if (!await installedReader.ReadAsync(cancellationToken).ConfigureAwait(false)
                || !string.Equals(installedReader.GetString(0), request.ConsumerChecksum, StringComparison.Ordinal)
                || !string.Equals(installedReader.GetString(1), request.ContractId, StringComparison.Ordinal)
                || installedReader.GetInt32(2) != request.ContractVersion || installedReader.GetInt32(4) != 0)
                return LifecycleReadFailure(BaseSubjectErrorCodes.LifecycleProviderContractInvalid, OperationStatus.CapabilityUnavailable, ErrorCategory.Capability);
            installedProjectionGeneration = installedReader.GetInt64(3);
        }

        BaseSubjectLifecycleOrderingBoundary? durableThrough = null; long durableGeneration = 0;
        await using (SqliteCommand durable = connection.CreateCommand())
        {
            durable.CommandTimeout = TimeoutSeconds(); durable.CommandText = $"SELECT through_position,through_subject_id,through_authority_epoch,through_incarnation,through_sequence,checkpoint_generation,protected_scope_value,state FROM {_names.SubjectLifecycleCheckpoints} WHERE consumer_id=$consumer AND consumer_version=$version AND scope_kind=$scopeKind AND scope_index_digest=$scopeDigest;";
            durable.Parameters.AddWithValue("$consumer", request.ConsumerId); durable.Parameters.AddWithValue("$version", request.ConsumerVersion); AddScopeQuery(durable, protectedScope);
            await using SqliteDataReader checkpointReader = await durable.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            if (await checkpointReader.ReadAsync(cancellationToken).ConfigureAwait(false)) { if (!_subjectScopes.Matches(new BaseProtectedSubjectScope { Kind = request.Scope.Kind, IndexDigest = protectedScope.IndexDigest, ProtectedCanonicalValue = (byte[])checkpointReader.GetValue(6) }, request.Scope)) return LifecycleReadFailure(BaseSubjectErrorCodes.LifecycleProviderContractInvalid, OperationStatus.CapabilityUnavailable, ErrorCategory.Capability); if (checkpointReader.GetInt32(7) != 0) return LifecycleReadFailure(BaseSubjectErrorCodes.CursorOvertaken, OperationStatus.Conflict, ErrorCategory.Conflict); durableGeneration = checkpointReader.GetInt64(5); if (!checkpointReader.IsDBNull(0)) durableThrough = new() { CommitPosition = new(checkpointReader.GetInt64(0)), SubjectId = BaseSubjectId.Create(checkpointReader.GetString(1), contract.SubjectIdKind), AuthorityEpoch = new((byte[])checkpointReader.GetValue(2)), Incarnation = new((byte[])checkpointReader.GetValue(3)), SubjectSequence = checkpointReader.GetInt64(4) }; }
        }
        if (installedProjectionGeneration != request.ProjectionGeneration)
            return LifecycleReadFailure(BaseSubjectErrorCodes.LifecycleProviderContractInvalid, OperationStatus.CapabilityUnavailable, ErrorCategory.Capability);
        BaseSubjectLifecycleOrderingBoundary? earliestRetained = await ReadLifecycleMembershipBoundaryAsync(connection, request, protectedScope, contract, descending: false, cancellationToken).ConfigureAwait(false);
        BaseSubjectLifecycleOrderingBoundary? highWater = await ReadLifecycleMembershipBoundaryAsync(connection, request, protectedScope, contract, descending: true, cancellationToken).ConfigureAwait(false);
        BaseSubjectLifecycleOrderingBoundary? effectiveAfter = request.After is null ? durableThrough
            : durableThrough is null || CompareLifecycleBoundary(request.After, durableThrough) >= 0 ? request.After : durableThrough;
        if (effectiveAfter is not null && earliestRetained is not null && CompareLifecycleBoundary(effectiveAfter, earliestRetained) < 0)
            return LifecycleReadFailure(BaseSubjectErrorCodes.CursorOvertaken, OperationStatus.Conflict, ErrorCategory.Conflict);
        await using SqliteCommand command = connection.CreateCommand(); command.CommandTimeout = TimeoutSeconds();
        var sql = new StringBuilder($"SELECT f.commit_position,f.subject_id,f.authority_epoch,f.incarnation,f.subject_sequence,f.contract_state_generation,f.delivery_epoch,f.fact_kind,f.previous_state,f.current_state,m.matched_state,m.protected_scope_value FROM {_names.SubjectLifecycleMemberships} m INNER JOIN {_names.SubjectLifecycleFacts} f ON f.commit_position=m.commit_position AND f.subject_id=m.subject_id AND f.authority_epoch=m.authority_epoch AND f.incarnation=m.incarnation AND f.subject_sequence=m.subject_sequence WHERE m.consumer_id=$consumer AND m.consumer_version=$consumerVersion AND m.consumer_checksum=$consumerChecksum AND m.contract_id=$contract AND m.contract_version=$contractVersion AND m.projection_generation=$projection AND m.scope_kind=$scopeKind AND m.scope_index_digest=$scopeDigest");
        command.Parameters.AddWithValue("$consumer", request.ConsumerId); command.Parameters.AddWithValue("$consumerVersion", request.ConsumerVersion); command.Parameters.AddWithValue("$consumerChecksum", request.ConsumerChecksum); command.Parameters.AddWithValue("$contract", request.ContractId); command.Parameters.AddWithValue("$contractVersion", request.ContractVersion); command.Parameters.AddWithValue("$projection", request.ProjectionGeneration); AddScopeQuery(command, protectedScope);
        if (effectiveAfter is { } after)
        {
            sql.Append(" AND (m.commit_position>$afterPosition OR (m.commit_position=$afterPosition AND (m.subject_id>$afterSubject OR (m.subject_id=$afterSubject AND (m.authority_epoch>$afterEpoch OR (m.authority_epoch=$afterEpoch AND (m.incarnation>$afterIncarnation OR (m.incarnation=$afterIncarnation AND m.subject_sequence>$afterSequence))))))))");
            command.Parameters.AddWithValue("$afterPosition", after.CommitPosition.Value); command.Parameters.AddWithValue("$afterSubject", after.SubjectId.Value); command.Parameters.Add("$afterEpoch", SqliteType.Blob).Value = after.AuthorityEpoch.ToArray(); command.Parameters.Add("$afterIncarnation", SqliteType.Blob).Value = after.Incarnation.ToArray(); command.Parameters.AddWithValue("$afterSequence", after.SubjectSequence);
        }
        sql.Append(" ORDER BY m.commit_position,m.subject_id COLLATE BINARY,m.authority_epoch,m.incarnation,m.subject_sequence LIMIT $take;"); command.Parameters.AddWithValue("$take", request.Take); command.CommandText = sql.ToString();
        var facts = ImmutableArray.CreateBuilder<BaseSubjectLifecycleProviderFact>(); long resultBytes = 8; int rowsSought = 0;
        await using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            rowsSought++;
            if (!_subjectScopes.Matches(new BaseProtectedSubjectScope { Kind = request.Scope.Kind, IndexDigest = protectedScope.IndexDigest, ProtectedCanonicalValue = (byte[])reader.GetValue(11) }, request.Scope)) return LifecycleReadFailure(BaseSubjectErrorCodes.LifecycleProviderContractInvalid, OperationStatus.CapabilityUnavailable, ErrorCategory.Capability);
            var boundary = new BaseSubjectLifecycleOrderingBoundary { CommitPosition = new(reader.GetInt64(0)), SubjectId = BaseSubjectId.Create(reader.GetString(1), contract.SubjectIdKind), AuthorityEpoch = new((byte[])reader.GetValue(2)), Incarnation = new((byte[])reader.GetValue(3)), SubjectSequence = reader.GetInt64(4) };
            BaseSubjectLifecycleFactKind kind = (BaseSubjectLifecycleFactKind)reader.GetInt32(7); BaseSubjectLifecycleState? previous = reader.IsDBNull(8) ? null : (BaseSubjectLifecycleState)reader.GetInt32(8); BaseSubjectLifecycleState? current = reader.IsDBNull(9) ? null : (BaseSubjectLifecycleState)reader.GetInt32(9);
            var fact = new BaseSubjectLifecycleFact { CommitPosition = boundary.CommitPosition, ContractId = request.ContractId, ContractVersion = request.ContractVersion, SubjectId = boundary.SubjectId, AuthorityEpoch = boundary.AuthorityEpoch, Incarnation = boundary.Incarnation, SubjectSequence = boundary.SubjectSequence, ContractStateGeneration = reader.GetInt64(5), DeliveryEpoch = reader.GetInt64(6), Kind = kind,
                Created = kind == BaseSubjectLifecycleFactKind.Created ? new() { CurrentState = current!.Value } : null,
                Transitioned = kind == BaseSubjectLifecycleFactKind.Transitioned ? new() { PreviousState = previous!.Value, CurrentState = current!.Value } : null,
                Retired = kind == BaseSubjectLifecycleFactKind.Retired ? new() { PreviousState = previous!.Value } : null };
            var providerFact = new BaseSubjectLifecycleProviderFact { Boundary = boundary, Scope = protectedScope, Fact = fact, ConsumerId = request.ConsumerId, ConsumerVersion = request.ConsumerVersion, ConsumerChecksum = request.ConsumerChecksum, ProjectionGeneration = request.ProjectionGeneration, MatchedObservedState = (BaseSubjectLifecycleState)reader.GetInt32(10) };
            long bytes = checked(8L + BaseSubjectCanonicalRetainedWork.MeasureLifecycleProviderFact(providerFact));
            if (checked(resultBytes + bytes) > request.MaximumResultBytes)
            {
                if (facts.Count == 0) return LifecycleReadFailure(BaseSubjectErrorCodes.LifecycleCapacityExceeded, OperationStatus.StoreError, ErrorCategory.Store);
                break;
            }
            resultBytes += bytes;
            facts.Add(providerFact);
        }
        BaseSubjectLifecycleOrderingBoundary? through = facts.Count == 0 ? null : facts[^1].Boundary;
        long restoreEpoch; await using (SqliteCommand authority = connection.CreateCommand()) { authority.CommandTimeout = TimeoutSeconds(); authority.CommandText = $"SELECT restore_epoch FROM {_names.SubjectContracts} WHERE contract_id=$contract AND contract_version=$version;"; authority.Parameters.AddWithValue("$contract", request.ContractId); authority.Parameters.AddWithValue("$version", request.ContractVersion); restoreEpoch = Convert.ToInt64(await authority.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false), System.Globalization.CultureInfo.InvariantCulture); }
        long deliveryEpoch;
        await using (SqliteCommand delivery = connection.CreateCommand())
        {
            delivery.CommandTimeout = TimeoutSeconds();
            delivery.CommandText = $"SELECT CAST(value AS INTEGER) FROM {_names.ProviderState} WHERE key='subject_lifecycle_delivery_epoch';";
            object? raw = await delivery.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
            if (raw is null || raw is DBNull)
                return LifecycleReadFailure(BaseSubjectErrorCodes.LifecycleProviderContractInvalid, OperationStatus.CapabilityUnavailable, ErrorCategory.Capability);
            deliveryEpoch = Convert.ToInt64(raw, System.Globalization.CultureInfo.InvariantCulture);
        }
        ImmutableArray<BaseReadIntervalEvidence> intervals = BaseSubjectLifecycleReadIntervals.Create(request, protectedScope, through);
        return OperationResults.Ok(new BaseSubjectLifecycleProviderPage { StoreInstanceId = _options.StoreId, RestoreEpoch = restoreEpoch, DeliveryEpoch = deliveryEpoch, CheckpointGeneration = durableGeneration, Scope = protectedScope, Facts = facts.ToImmutable(), EarliestRetained = earliestRetained, HighWater = highWater, Through = through, ProjectionGeneration = request.ProjectionGeneration, Intervals = intervals, Accounting = new BaseSubjectLifecycleReadAccounting { RowsSought = rowsSought, RowsHydrated = facts.Count, ResultBytes = resultBytes, TransientBytes = checked(resultBytes + BaseSubjectCanonicalRetainedWork.MeasureLifecycleIntervals(intervals)) } });
    }

    private async ValueTask<BaseSubjectLifecycleOrderingBoundary?> ReadLifecycleMembershipBoundaryAsync(
        SqliteConnection connection,
        BaseSubjectLifecycleProviderReadRequest request,
        BaseProtectedSubjectScope scope,
        BaseExportedSubjectDefinition contract,
        bool descending,
        CancellationToken cancellationToken)
    {
        string direction = descending ? "DESC" : "ASC";
        await using SqliteCommand command = connection.CreateCommand(); command.CommandTimeout = TimeoutSeconds();
        command.CommandText = $"SELECT commit_position,subject_id,authority_epoch,incarnation,subject_sequence FROM {_names.SubjectLifecycleMemberships} WHERE consumer_id=$consumer AND consumer_version=$consumerVersion AND consumer_checksum=$consumerChecksum AND contract_id=$contract AND contract_version=$contractVersion AND projection_generation=$projection AND scope_kind=$scopeKind AND scope_index_digest=$scopeDigest ORDER BY commit_position {direction},subject_id COLLATE BINARY {direction},authority_epoch {direction},incarnation {direction},subject_sequence {direction} LIMIT 1;";
        command.Parameters.AddWithValue("$consumer", request.ConsumerId); command.Parameters.AddWithValue("$consumerVersion", request.ConsumerVersion); command.Parameters.AddWithValue("$consumerChecksum", request.ConsumerChecksum); command.Parameters.AddWithValue("$contract", request.ContractId); command.Parameters.AddWithValue("$contractVersion", request.ContractVersion); command.Parameters.AddWithValue("$projection", request.ProjectionGeneration); AddScopeQuery(command, scope);
        await using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        return await reader.ReadAsync(cancellationToken).ConfigureAwait(false)
            ? new BaseSubjectLifecycleOrderingBoundary { CommitPosition = new(reader.GetInt64(0)), SubjectId = BaseSubjectId.Create(reader.GetString(1), contract.SubjectIdKind, contract.MaximumSubjectIdUtf8Bytes), AuthorityEpoch = new((byte[])reader.GetValue(2)), Incarnation = new((byte[])reader.GetValue(3)), SubjectSequence = reader.GetInt64(4) }
            : null;
    }

    /// <inheritdoc />
    public async ValueTask<OperationResult<BaseSubjectLifecycleProviderReconciliationPage>> ReconcileAsync(
        BaseSubjectLifecycleProviderReconciliationRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (!BaseSubjectLifecycleProviderCapabilities.BuiltIn.ReconciliationSupported)
            return LifecycleReconciliationFailure(BaseSubjectErrorCodes.LifecycleReconciliationUnavailable, OperationStatus.CapabilityUnavailable, ErrorCategory.Capability);
        if (request.Take is < 1 or > 256 || request.MaximumResultBytes is < 1 or > 1_048_576 || request.DeadlineUtc <= _timeProvider.GetUtcNow() || _subjectScopes is null)
            return LifecycleReconciliationFailure(BaseSubjectErrorCodes.LifecycleContractInvalid, OperationStatus.ValidationFailed, ErrorCategory.Validation);
        BaseExportedSubjectDefinition? contract = _options.ExportedSubjects.SingleOrDefault(value => value.Id == request.ContractId && value.Version == request.ContractVersion);
        if (contract is null || !string.Equals(BaseSubjectContractGraph.Checksum(contract), request.ContractChecksum, StringComparison.Ordinal))
            return LifecycleReconciliationFailure(BaseSubjectErrorCodes.LifecycleProviderContractInvalid, OperationStatus.CapabilityUnavailable, ErrorCategory.Capability);
        BaseProtectedSubjectScope protectedScope = _subjectScopes.Protect(request.Scope, _subjectScopeProtectionKey!.Value);
        await using IAsyncDisposable generationLease = await _schemaGenerationGate.AcquireSharedAsync(cancellationToken).ConfigureAwait(false);
        if (RestoreRecoveryIndeterminate || RestoreRecoveryPending)
            return LifecycleReconciliationFailure(BaseSubjectErrorCodes.MaintenanceRequired, OperationStatus.CapabilityUnavailable, ErrorCategory.Capability);
        await using SqliteConnection connection = await _connections.OpenAsync(cancellationToken).ConfigureAwait(false);
        if (await LifecycleMaintenanceActiveAsync(connection, cancellationToken).ConfigureAwait(false))
            return LifecycleReconciliationFailure(BaseSubjectErrorCodes.MaintenanceRequired, OperationStatus.CapabilityUnavailable, ErrorCategory.Capability);
        BaseSubjectAuthorityEpoch epoch;
        await using (SqliteCommand authority = connection.CreateCommand())
        {
            authority.CommandTimeout = TimeoutSeconds(); authority.CommandText = $"SELECT authority_epoch FROM {_names.SubjectContracts} WHERE contract_id=$contract AND contract_version=$version;";
            authority.Parameters.AddWithValue("$contract", request.ContractId); authority.Parameters.AddWithValue("$version", request.ContractVersion);
            object? raw = await authority.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
            if (raw is not byte[] bytes) return LifecycleReconciliationFailure(BaseSubjectErrorCodes.LifecycleProviderContractInvalid, OperationStatus.CapabilityUnavailable, ErrorCategory.Capability);
            epoch = new(bytes);
        }
        await using SqliteCommand command = connection.CreateCommand(); command.CommandTimeout = TimeoutSeconds();
        command.CommandText = $"SELECT subject_id,incarnation,lifecycle_state,subject_sequence,protected_scope_value FROM {_names.SubjectLifetimes} WHERE scope_kind=$scopeKind AND scope_index_digest=$scopeDigest AND contract_id=$contract AND contract_version=$version AND ($after IS NULL OR subject_id>$after) ORDER BY subject_id COLLATE BINARY LIMIT $take;";
        AddScopeQuery(command, protectedScope);
        command.Parameters.AddWithValue("$contract", request.ContractId); command.Parameters.AddWithValue("$version", request.ContractVersion);
        command.Parameters.AddWithValue("$after", request.AfterSubjectId is null ? DBNull.Value : request.AfterSubjectId.Value);
        command.Parameters.AddWithValue("$take", request.Take);
        var subjects = ImmutableArray.CreateBuilder<BaseCurrentSubjectLifecycle>(); long resultBytes = 0;
        await using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            if (!_subjectScopes.Matches(new BaseProtectedSubjectScope { Kind = request.Scope.Kind, IndexDigest = protectedScope.IndexDigest, ProtectedCanonicalValue = (byte[])reader.GetValue(4) }, request.Scope)) return LifecycleReconciliationFailure(BaseSubjectErrorCodes.LifecycleProviderContractInvalid, OperationStatus.CapabilityUnavailable, ErrorCategory.Capability);
            BaseSubjectId id = BaseSubjectId.Create(reader.GetString(0), contract.SubjectIdKind, contract.MaximumSubjectIdUtf8Bytes);
            long bytes = checked(96L + Encoding.UTF8.GetByteCount(id.Value)); if (checked(resultBytes + bytes) > request.MaximumResultBytes) break; resultBytes += bytes;
            subjects.Add(new BaseCurrentSubjectLifecycle { SubjectId = id, AuthorityEpoch = epoch, Incarnation = new((byte[])reader.GetValue(1)), State = (BaseSubjectLifecycleState)reader.GetInt32(2), SubjectSequence = reader.GetInt64(3) });
        }
        BaseSubjectLifecycleOrderingBoundary? highWater = null;
        await using (SqliteCommand high = connection.CreateCommand())
        {
            high.CommandTimeout = TimeoutSeconds(); high.CommandText = $"SELECT commit_position,subject_id,authority_epoch,incarnation,subject_sequence FROM {_names.SubjectLifecycleFacts} WHERE scope_kind=$scopeKind AND scope_index_digest=$scopeDigest AND contract_id=$contract AND contract_version=$version ORDER BY commit_position DESC,subject_id DESC,authority_epoch DESC,incarnation DESC,subject_sequence DESC LIMIT 1;";
            AddScopeQuery(high, protectedScope); high.Parameters.AddWithValue("$contract", request.ContractId); high.Parameters.AddWithValue("$version", request.ContractVersion);
            await using SqliteDataReader highReader = await high.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            if (await highReader.ReadAsync(cancellationToken).ConfigureAwait(false)) highWater = new() { CommitPosition = new(highReader.GetInt64(0)), SubjectId = BaseSubjectId.Create(highReader.GetString(1), contract.SubjectIdKind, contract.MaximumSubjectIdUtf8Bytes), AuthorityEpoch = new((byte[])highReader.GetValue(2)), Incarnation = new((byte[])highReader.GetValue(3)), SubjectSequence = highReader.GetInt64(4) };
        }
        return OperationResults.Ok(new BaseSubjectLifecycleProviderReconciliationPage { Scope = protectedScope, Subjects = subjects.ToImmutable(), NextSubjectId = subjects.Count == request.Take ? subjects[^1].SubjectId : null, CapturedHighWater = highWater, ProjectionGeneration = request.ProjectionGeneration, Intervals = [], Accounting = new() { RowsSought = subjects.Count, RowsHydrated = subjects.Count, ResultBytes = resultBytes, TransientBytes = resultBytes } });
    }

    /// <inheritdoc />
    public async ValueTask<OperationResult<BaseSubjectLifecycleProviderInspection>> InspectAsync(
        BaseSubjectLifecycleProviderInspectionRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.DeadlineUtc <= _timeProvider.GetUtcNow()) return LifecycleInspectionFailure(BaseSubjectErrorCodes.LifecycleContractInvalid, OperationStatus.ValidationFailed, ErrorCategory.Validation);
        if (request.ScopeAuthority.Mode == BaseSubjectScopeQueryMode.AllAuthorizedScopes
            && (request.ScopeAuthority.ExactScope is not null || request.IncludeTerminalReceipt || request.SubjectId is not null
                || !_options.SubjectLifecycleInspectionAuthorities.Any(value =>
                    value.ContractId == request.ContractId && value.ContractVersion == request.ContractVersion
                    && string.Equals(value.Digest, request.ScopeAuthority.InstalledAuthorityDigest, StringComparison.Ordinal))))
            return LifecycleInspectionFailure(BaseSubjectErrorCodes.LifecycleUnauthorized, OperationStatus.PolicyDenied, ErrorCategory.Authorization);
        BaseOwnedSubjectScopeEvidence? scope = request.ScopeAuthority.Mode == BaseSubjectScopeQueryMode.ExactScope ? request.ScopeAuthority.ExactScope : null;
        if (request.ScopeAuthority.Mode == BaseSubjectScopeQueryMode.ExactScope && scope is null) return LifecycleInspectionFailure(BaseSubjectErrorCodes.LifecycleContractInvalid, OperationStatus.ValidationFailed, ErrorCategory.Validation);
        if (request.ScopeAuthority.Mode == BaseSubjectScopeQueryMode.ExactScope && !ExactInspectionAuthorityMatches(request)) return LifecycleInspectionFailure(BaseSubjectErrorCodes.LifecycleUnauthorized, OperationStatus.PolicyDenied, ErrorCategory.Authorization);
        BaseProtectedSubjectScope? protectedScope = scope is null ? null : _subjectScopes?.Protect(scope, _subjectScopeProtectionKey!.Value);
        await using IAsyncDisposable generationLease = await _schemaGenerationGate.AcquireSharedAsync(cancellationToken).ConfigureAwait(false);
        if (RestoreRecoveryIndeterminate || RestoreRecoveryPending)
            return LifecycleInspectionFailure(BaseSubjectErrorCodes.MaintenanceRequired, OperationStatus.CapabilityUnavailable, ErrorCategory.Capability);
        await using SqliteConnection connection = await _connections.OpenAsync(cancellationToken).ConfigureAwait(false);
        if (await LifecycleMaintenanceActiveAsync(connection, cancellationToken).ConfigureAwait(false))
            return LifecycleInspectionFailure(BaseSubjectErrorCodes.MaintenanceRequired, OperationStatus.CapabilityUnavailable, ErrorCategory.Capability);
        var consumers = ImmutableArray.CreateBuilder<BaseSubjectLifecycleConsumerInspection>();
        long restoreEpoch;
        await using (SqliteCommand command = connection.CreateCommand())
        {
            command.CommandTimeout = TimeoutSeconds(); var sql = new StringBuilder($"SELECT p.consumer_id,p.consumer_version,p.projection_generation,p.cutoff_position,p.cutoff_subject_id,p.cutoff_authority_epoch,p.cutoff_incarnation,p.cutoff_sequence,p.published_graph_generation,c.through_position,c.through_subject_id,c.through_authority_epoch,c.through_incarnation,c.through_sequence,COALESCE(c.checkpoint_generation,0),COALESCE(c.state,0) FROM {_names.SubjectLifecycleConsumers} p LEFT JOIN {_names.SubjectLifecycleCheckpoints} c ON c.consumer_id=p.consumer_id AND c.consumer_version=p.consumer_version");
            command.Parameters.AddWithValue("$contract", request.ContractId); command.Parameters.AddWithValue("$version", request.ContractVersion);
            if (protectedScope is not null) { sql.Append(" AND c.scope_kind=$scopeKind AND c.scope_index_digest=$scopeDigest"); AddScopeQuery(command, protectedScope); }
            else sql.Append(" AND 0=1");
            sql.Append(" WHERE p.contract_id=$contract AND p.contract_version=$version");
            if (request.ConsumerId is not null) { sql.Append(" AND p.consumer_id=$consumer"); command.Parameters.AddWithValue("$consumer", request.ConsumerId); }
            command.CommandText = sql.ToString(); await using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                BaseSubjectLifecycleOrderingBoundary? cutoff = reader.IsDBNull(4) ? null : new() { CommitPosition = new(reader.GetInt64(3)), SubjectId = BaseSubjectId.Create(reader.GetString(4), BaseSubjectIdKind.OrdinalString), AuthorityEpoch = new((byte[])reader.GetValue(5)), Incarnation = new((byte[])reader.GetValue(6)), SubjectSequence = reader.GetInt64(7) };
                BaseSubjectLifecycleOrderingBoundary? through = reader.IsDBNull(9) ? null : new() { CommitPosition = new(reader.GetInt64(9)), SubjectId = BaseSubjectId.Create(reader.GetString(10), BaseSubjectIdKind.OrdinalString), AuthorityEpoch = new((byte[])reader.GetValue(11)), Incarnation = new((byte[])reader.GetValue(12)), SubjectSequence = reader.GetInt64(13) };
                consumers.Add(new() { ConsumerId = reader.GetString(0), ConsumerVersion = reader.GetInt32(1), ProjectionGeneration = reader.GetInt64(2), InstallationCutoff = cutoff, PublishedGraphGeneration = reader.GetInt64(8), Through = through, CheckpointGeneration = reader.GetInt64(14), Overtaken = reader.GetInt32(15) != 0 });
            }
        }
        BaseSubjectTerminalLifetimeReceipt? terminalReceipt = null;
        if (request.IncludeTerminalReceipt && request.SubjectId is BaseSubjectId requestedSubjectId && scope is not null && protectedScope is not null && _subjectScopes is not null)
        {
            await using SqliteCommand terminal = connection.CreateCommand(); terminal.CommandTimeout = TimeoutSeconds();
            terminal.CommandText = $"SELECT retired_authority_epoch,retired_incarnation,retired_lifetime_generation,retired_subject_sequence,retired_position,contract_state_generation,restore_epoch,receipt_checksum,protected_scope_value FROM {_names.SubjectTerminalLifetimes} WHERE scope_kind=$scopeKind AND scope_index_digest=$scopeDigest AND contract_id=$contract AND contract_version=$version AND subject_id=$subject;";
            AddScopeQuery(terminal, protectedScope); terminal.Parameters.AddWithValue("$contract", request.ContractId); terminal.Parameters.AddWithValue("$version", request.ContractVersion); terminal.Parameters.AddWithValue("$subject", requestedSubjectId.Value);
            await using SqliteDataReader reader = await terminal.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            if (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                if (!_subjectScopes.Matches(new BaseProtectedSubjectScope { Kind = scope.Kind, IndexDigest = protectedScope.IndexDigest, ProtectedCanonicalValue = (byte[])reader.GetValue(8) }, scope)) return LifecycleInspectionFailure(BaseSubjectErrorCodes.LifecycleProviderContractInvalid, OperationStatus.CapabilityUnavailable, ErrorCategory.Capability);
                terminalReceipt = new BaseSubjectTerminalLifetimeReceipt { ContractId = request.ContractId, ContractVersion = request.ContractVersion, SubjectId = requestedSubjectId, Scope = protectedScope, RetiredAuthorityEpoch = new((byte[])reader.GetValue(0)), RetiredIncarnation = new((byte[])reader.GetValue(1)), RetiredLifetimeGeneration = reader.GetInt64(2), RetiredSubjectSequence = reader.GetInt64(3), RetiredPosition = new(reader.GetInt64(4)), ContractStateGeneration = reader.GetInt64(5), RestoreEpoch = reader.GetInt64(6), ReceiptChecksum = reader.GetString(7) };
                if (!BaseSubjectTerminalIntegrity.Verify(terminalReceipt, scope)) return LifecycleInspectionFailure(BaseSubjectErrorCodes.LifecycleProviderContractInvalid, OperationStatus.CapabilityUnavailable, ErrorCategory.Capability);
            }
        }
        long deliveryEpoch;
        await using (SqliteCommand delivery = connection.CreateCommand())
        {
            delivery.CommandTimeout = TimeoutSeconds();
            delivery.CommandText = $"SELECT CAST((SELECT value FROM {_names.ProviderState} WHERE key='subject_lifecycle_delivery_epoch') AS INTEGER),COALESCE((SELECT MAX(restore_epoch) FROM {_names.SubjectContracts}),0);";
            await using SqliteDataReader authorityReader = await delivery.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            if (!await authorityReader.ReadAsync(cancellationToken).ConfigureAwait(false) || authorityReader.IsDBNull(0))
                return LifecycleInspectionFailure(BaseSubjectErrorCodes.LifecycleProviderContractInvalid, OperationStatus.CapabilityUnavailable, ErrorCategory.Capability);
            deliveryEpoch = authorityReader.GetInt64(0);
            restoreEpoch = authorityReader.GetInt64(1);
        }
        return OperationResults.Ok(new BaseSubjectLifecycleProviderInspection { StoreInstanceId = _options.StoreId, RestoreEpoch = restoreEpoch, DeliveryEpoch = deliveryEpoch, EarliestRetained = null, HighWater = null, Consumers = consumers.ToImmutable(), TerminalReceipt = terminalReceipt, Accounting = new() { RowsSought = consumers.Count, RowsHydrated = consumers.Count, ResultBytes = consumers.Count * 96L, TransientBytes = consumers.Count * 96L } });
    }

    private bool ExactInspectionAuthorityMatches(BaseSubjectLifecycleProviderInspectionRequest request)
    {
        BaseExportedSubjectDefinition? contract = _options.ExportedSubjects.SingleOrDefault(value => value.Id == request.ContractId && value.Version == request.ContractVersion);
        if (contract is null) return false;
        string expected = BaseSubjectContractGraph.Checksum(contract);
        if (request.ConsumerId is not null)
        {
            BaseSubjectLifecycleConsumerDefinition? consumer = _options.SubjectLifecycleConsumers.SingleOrDefault(value => value.Id == request.ConsumerId && value.ContractId == request.ContractId && value.ContractVersion == request.ContractVersion);
            if (consumer is null) return false;
            expected = BaseSubjectLifecycleRegistry.Checksum(BaseSubjectLifecycleRegistry.Normalize(consumer), expected);
        }
        return string.Equals(expected, request.ScopeAuthority.InstalledAuthorityDigest, StringComparison.Ordinal);
    }

    /// <inheritdoc />
    public ValueTask<RecordMutationExecutionResult> ExecuteMaintenanceAsync(IBaseSubjectLifecycleMaintenanceProcessor processor, BaseSubjectLifecycleMaintenanceExecutionRequest request, CancellationToken cancellationToken = default) =>
        processor.ExecuteAsync(new SqliteLifecycleMaintenanceSession(this), request, cancellationToken);

    private sealed class SqliteLifecycleMaintenanceSession(SqliteRecordStore owner) : IBaseSubjectLifecycleMaintenanceSession
    {
        public async ValueTask<OperationResult<BaseSubjectLifecycleMaintenanceResult>> ExecuteAsync(
            BaseSubjectLifecycleMaintenanceExecutionRequest request,
            CancellationToken cancellationToken = default)
        {
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            deadline.CancelAfter(request.OperationTimeout);
            try
            {
                await using IAsyncDisposable generationLease = await owner._schemaGenerationGate.AcquireExclusiveAsync(deadline.Token).ConfigureAwait(false);
                if (request.Kind == BaseSubjectLifecycleMaintenanceKind.RotateScopeProtection)
                    return await ExecuteStagedRotationAsync(request, deadline.Token).ConfigureAwait(false);
                if (request.Kind == BaseSubjectLifecycleMaintenanceKind.RemoveConsumer)
                    return await ExecuteStagedConsumerRemovalAsync(request, deadline.Token).ConfigureAwait(false);
                if (request.Kind == BaseSubjectLifecycleMaintenanceKind.RebuildDeliveryProjection)
                    return await ExecuteStagedDeliveryRebuildAsync(request, deadline.Token).ConfigureAwait(false);
                if (request.Kind == BaseSubjectLifecycleMaintenanceKind.Prune)
                    return await ExecuteStagedPruneAsync(request, deadline.Token).ConfigureAwait(false);
                await using SqliteConnection connection = await owner._connections.OpenAsync(deadline.Token).ConfigureAwait(false);
                if (await owner.LifecycleMaintenanceActiveAsync(connection, deadline.Token).ConfigureAwait(false))
                    return Failure(BaseSubjectErrorCodes.MaintenanceRequired, OperationStatus.CapabilityUnavailable, ErrorCategory.Capability);
                await using SqliteTransaction transaction = (SqliteTransaction)await connection.BeginTransactionAsync(System.Data.IsolationLevel.Serializable, deadline.Token).ConfigureAwait(false);
                OperationResult<BaseSubjectLifecycleMaintenanceResult>? replay = await ReadMaintenanceReceiptAsync(connection, transaction, request, deadline.Token).ConfigureAwait(false);
                if (replay is not null)
                {
                    await transaction.RollbackAsync(CancellationToken.None).ConfigureAwait(false);
                    return replay;
                }
                OperationResult<BaseSubjectLifecycleMaintenanceResult> result = await ExecuteCoreAsync(connection, transaction, request, deadline.Token).ConfigureAwait(false);
                if (!result.IsSuccess())
                {
                    await transaction.RollbackAsync(CancellationToken.None).ConfigureAwait(false);
                    return result;
                }
                await InsertMaintenanceReceiptAsync(connection, transaction, request, result.Value!, deadline.Token).ConfigureAwait(false);
                await transaction.CommitAsync(CancellationToken.None).ConfigureAwait(false);
                if (request.Kind == BaseSubjectLifecycleMaintenanceKind.RotateScopeProtection
                    && byte.TryParse(request.ReplacementScopeProtectionKeyId, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out byte replacement))
                {
                    owner._subjectScopeProtectionKey = replacement;
                    owner._subjectScopeProtectionKeyId = replacement.ToString(System.Globalization.CultureInfo.InvariantCulture);
                }
                return result;
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                return Failure(BaseSubjectErrorCodes.Timeout, OperationStatus.StoreError, ErrorCategory.Store);
            }
            catch (SqliteException)
            {
                return Failure(BaseSubjectErrorCodes.LifecycleProviderContractInvalid, OperationStatus.StoreError, ErrorCategory.Store);
            }
            catch (InvalidDataException)
            {
                return Failure(BaseSubjectErrorCodes.LifecycleProviderContractInvalid, OperationStatus.CapabilityUnavailable, ErrorCategory.Capability);
            }
        }

        private static readonly string[] RotationDomains =
        [
            "lifetimes",
            "terminal-lifetimes",
            "lifecycle-facts",
            "delivery-memberships",
            "consumer-checkpoints",
        ];

        private static readonly string[] ConsumerRemovalDomains = ["delivery-memberships", "consumer-checkpoints"];

        private async ValueTask<OperationResult<BaseSubjectLifecycleMaintenanceResult>> ExecuteStagedPruneAsync(
            BaseSubjectLifecycleMaintenanceExecutionRequest request,
            CancellationToken cancellationToken)
        {
            await using SqliteConnection connection = await owner._connections.OpenAsync(cancellationToken).ConfigureAwait(false);
            await using (SqliteTransaction receiptTransaction = (SqliteTransaction)await connection.BeginTransactionAsync(System.Data.IsolationLevel.Serializable, cancellationToken).ConfigureAwait(false))
            {
                OperationResult<BaseSubjectLifecycleMaintenanceResult>? replay = await ReadMaintenanceReceiptAsync(connection, receiptTransaction, request, cancellationToken).ConfigureAwait(false);
                await receiptTransaction.RollbackAsync(CancellationToken.None).ConfigureAwait(false);
                if (replay is not null) return replay;
            }
            await InitializePruneAsync(connection, request, cancellationToken).ConfigureAwait(false);
            while (true)
            {
                RotationProgress progress = await ReadRotationProgressAsync(connection, null, cancellationToken).ConfigureAwait(false)
                    ?? throw new InvalidDataException(BaseSubjectErrorCodes.LifecycleProviderContractInvalid);
                ValidatePruneProgress(progress, request);
                if (progress.DomainOrdinal == 2) break;
                if (await ExecutePrunePageAsync(connection, request, progress, cancellationToken).ConfigureAwait(false))
                    await owner._administrationOperations.BeforePhaseAsync("subjectLifecyclePruneAfterPage", cancellationToken).ConfigureAwait(false);
            }
            await using SqliteTransaction transaction = (SqliteTransaction)await connection.BeginTransactionAsync(System.Data.IsolationLevel.Serializable, cancellationToken).ConfigureAwait(false);
            RotationProgress completed = await ReadRotationProgressAsync(connection, transaction, cancellationToken).ConfigureAwait(false)
                ?? throw new InvalidDataException(BaseSubjectErrorCodes.LifecycleProviderContractInvalid);
            ValidatePruneProgress(completed, request);
            if (completed.DomainOrdinal != 2) throw new InvalidDataException(BaseSubjectErrorCodes.LifecycleProviderContractInvalid);
            RotationEvidence evidence = await ValidatePruneStageAsync(connection, transaction, request, cancellationToken).ConfigureAwait(false);
            if (evidence.Examined != completed.ExaminedCount || evidence.Changed != completed.ChangedCount
                || evidence.CanonicalBytes != completed.CanonicalBytes || !string.Equals(evidence.RollingChecksum, completed.RollingChecksum, StringComparison.Ordinal))
                throw new InvalidDataException(BaseSubjectErrorCodes.LifecycleProviderContractInvalid);
            await using (SqliteCommand clear = connection.CreateCommand())
            {
                clear.Transaction = transaction; clear.CommandTimeout = owner.TimeoutSeconds();
                clear.CommandText = $"DELETE FROM {owner._names.SubjectLifecycleScopeStage}; DELETE FROM {owner._names.SubjectLifecycleMaintenance} WHERE singleton=1;";
                await clear.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            }
            var result = new BaseSubjectLifecycleMaintenanceResult
            {
                Kind = request.Kind, ExaminedCount = completed.ExaminedCount, ChangedCount = completed.ChangedCount,
                CanonicalBytes = completed.CanonicalBytes, RollingChecksum = completed.RollingChecksum,
                DeliveryEpoch = request.ExpectedDeliveryEpoch, ProjectionGeneration = null, Duplicate = false,
            };
            await InsertMaintenanceReceiptAsync(connection, transaction, request, result, cancellationToken).ConfigureAwait(false);
            await transaction.CommitAsync(CancellationToken.None).ConfigureAwait(false);
            return OperationResults.Ok(result);
        }

        private async ValueTask InitializePruneAsync(SqliteConnection connection, BaseSubjectLifecycleMaintenanceExecutionRequest request, CancellationToken cancellationToken)
        {
            await using SqliteTransaction transaction = (SqliteTransaction)await connection.BeginTransactionAsync(System.Data.IsolationLevel.Serializable, cancellationToken).ConfigureAwait(false);
            RotationProgress? existing = await ReadRotationProgressAsync(connection, transaction, cancellationToken).ConfigureAwait(false);
            if (existing is not null) { ValidatePruneProgress(existing, request); await transaction.RollbackAsync(CancellationToken.None).ConfigureAwait(false); return; }
            (long restoreEpoch, long deliveryEpoch, long scopeGeneration, string scopeKeyId) = await ReadAuthorityAsync(connection, transaction, cancellationToken).ConfigureAwait(false);
            if (owner._schemaGeneration != request.ExpectedStoreGeneration || owner._schemaGeneration != request.ExpectedSchemaGeneration
                || restoreEpoch != request.ExpectedRestoreEpoch || deliveryEpoch != request.ExpectedDeliveryEpoch
                || scopeGeneration != request.ExpectedScopeProtectionGeneration || !string.Equals(scopeKeyId, request.ExpectedScopeProtectionKeyId, StringComparison.Ordinal))
                throw new InvalidDataException(BaseSubjectErrorCodes.LifecycleProviderContractInvalid);
            await using SqliteCommand insert = connection.CreateCommand(); insert.Transaction = transaction; insert.CommandTimeout = owner.TimeoutSeconds();
            insert.CommandText = $"INSERT INTO {owner._names.SubjectLifecycleMaintenance}(singleton,kind,request_scope,request_operation,request_key,fingerprint,plan_checksum,expected_store_generation,expected_restore_epoch,expected_delivery_epoch,expected_scope_generation,old_key_id,replacement_key_id,domain_ordinal,last_rowid,examined_count,changed_count,canonical_bytes,rolling_checksum) VALUES(1,$kind,$scope,$operation,$key,$fingerprint,$plan,$store,$restore,$delivery,$scopeGeneration,$old,'',0,0,0,0,0,$checksum);";
            insert.Parameters.AddWithValue("$kind", (int)request.Kind); insert.Parameters.AddWithValue("$scope", request.Identity.Scope); insert.Parameters.AddWithValue("$operation", request.Identity.Operation); insert.Parameters.AddWithValue("$key", request.Identity.IdempotencyKey); insert.Parameters.Add("$fingerprint", SqliteType.Blob).Value = request.Identity.Fingerprint.ToArray(); insert.Parameters.Add("$plan", SqliteType.Blob).Value = request.PlanChecksum; insert.Parameters.AddWithValue("$store", request.ExpectedStoreGeneration); insert.Parameters.AddWithValue("$restore", request.ExpectedRestoreEpoch); insert.Parameters.AddWithValue("$delivery", request.ExpectedDeliveryEpoch); insert.Parameters.AddWithValue("$scopeGeneration", request.ExpectedScopeProtectionGeneration); insert.Parameters.AddWithValue("$old", request.ExpectedScopeProtectionKeyId); insert.Parameters.AddWithValue("$checksum", EmptyRotationChecksum);
            await insert.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false); await transaction.CommitAsync(CancellationToken.None).ConfigureAwait(false);
        }

        private async ValueTask<bool> ExecutePrunePageAsync(SqliteConnection connection, BaseSubjectLifecycleMaintenanceExecutionRequest request, RotationProgress expected, CancellationToken cancellationToken)
        {
            await using SqliteTransaction transaction = (SqliteTransaction)await connection.BeginTransactionAsync(System.Data.IsolationLevel.Serializable, cancellationToken).ConfigureAwait(false);
            RotationProgress current = await ReadRotationProgressAsync(connection, transaction, cancellationToken).ConfigureAwait(false) ?? throw new InvalidDataException(BaseSubjectErrorCodes.LifecycleProviderContractInvalid);
            ValidatePruneProgress(current, request);
            if (current.DomainOrdinal != expected.DomainOrdinal || current.LastRowId != expected.LastRowId) throw new InvalidDataException(BaseSubjectErrorCodes.LifecycleProviderContractInvalid);
            BaseSubjectLifecycleOrderingBoundary retained = request.RetainedFrom!;
            await using SqliteCommand select = connection.CreateCommand(); select.Transaction = transaction; select.CommandTimeout = owner.TimeoutSeconds();
            if (current.DomainOrdinal == 0)
            {
                select.CommandText = $"SELECT m.rowid,m.consumer_id,m.consumer_version,m.commit_position,m.subject_id,m.authority_epoch,m.incarnation,m.subject_sequence FROM {owner._names.SubjectLifecycleMemberships} m LEFT JOIN {owner._names.SubjectLifecycleCheckpoints} c ON c.consumer_id=m.consumer_id AND c.consumer_version=m.consumer_version AND c.scope_kind=m.scope_kind AND c.scope_index_digest=m.scope_index_digest WHERE m.rowid>$after AND m.contract_id=$contract AND m.contract_version=$version AND (m.commit_position,m.subject_id,m.authority_epoch,m.incarnation,m.subject_sequence)<($position,$subject,$epoch,$incarnation,$sequence) AND (c.state=1 OR (c.through_position,c.through_subject_id,c.through_authority_epoch,c.through_incarnation,c.through_sequence)>=(m.commit_position,m.subject_id,m.authority_epoch,m.incarnation,m.subject_sequence)) ORDER BY m.rowid LIMIT $take;";
            }
            else
            {
                select.CommandText = $"SELECT f.rowid,'',0,f.commit_position,f.subject_id,f.authority_epoch,f.incarnation,f.subject_sequence FROM {owner._names.SubjectLifecycleFacts} f LEFT JOIN {owner._names.SubjectLifecycleMemberships} m ON m.scope_kind=f.scope_kind AND m.scope_index_digest=f.scope_index_digest AND m.contract_id=f.contract_id AND m.contract_version=f.contract_version AND m.commit_position=f.commit_position AND m.subject_id=f.subject_id AND m.authority_epoch=f.authority_epoch AND m.incarnation=f.incarnation AND m.subject_sequence=f.subject_sequence LEFT JOIN {owner._names.SubjectTerminalLifetimes} t ON t.scope_kind=f.scope_kind AND t.scope_index_digest=f.scope_index_digest AND t.contract_id=f.contract_id AND t.contract_version=f.contract_version AND t.subject_id=f.subject_id AND t.retired_authority_epoch=f.authority_epoch AND t.retired_incarnation=f.incarnation AND t.retired_subject_sequence=f.subject_sequence AND t.retired_position=f.commit_position WHERE f.rowid>$after AND f.contract_id=$contract AND f.contract_version=$version AND (f.commit_position,f.subject_id,f.authority_epoch,f.incarnation,f.subject_sequence)<($position,$subject,$epoch,$incarnation,$sequence) AND m.consumer_id IS NULL AND t.subject_id IS NULL ORDER BY f.rowid LIMIT $take;";
            }
            select.Parameters.AddWithValue("$after", current.LastRowId); select.Parameters.AddWithValue("$contract", request.ContractId!); select.Parameters.AddWithValue("$version", request.ContractVersion!.Value); select.Parameters.AddWithValue("$position", retained.CommitPosition.Value); select.Parameters.AddWithValue("$subject", retained.SubjectId.Value); select.Parameters.Add("$epoch", SqliteType.Blob).Value = retained.AuthorityEpoch.ToArray(); select.Parameters.Add("$incarnation", SqliteType.Blob).Value = retained.Incarnation.ToArray(); select.Parameters.AddWithValue("$sequence", retained.SubjectSequence); select.Parameters.AddWithValue("$take", request.PageSize);
            var rows = new List<(long RowId,string Consumer,int Version,long Position,string Subject,byte[] Epoch,byte[] Incarnation,long Sequence)>();
            await using (SqliteDataReader reader = await select.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false)) while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false)) rows.Add((reader.GetInt64(0),reader.GetString(1),reader.GetInt32(2),reader.GetInt64(3),reader.GetString(4),(byte[])reader.GetValue(5),(byte[])reader.GetValue(6),reader.GetInt64(7)));
            long examined = current.ExaminedCount, changed = current.ChangedCount, bytes = current.CanonicalBytes, last = current.LastRowId; byte[] rolling = Convert.FromHexString(current.RollingChecksum);
            foreach (var row in rows)
            {
                byte[] canonical = Encoding.UTF8.GetBytes($"{current.DomainOrdinal}\0{row.RowId}\0{row.Consumer}\0{row.Version}\0{row.Position}\0{row.Subject}\0{Convert.ToHexStringLower(row.Epoch)}\0{Convert.ToHexStringLower(row.Incarnation)}\0{row.Sequence}"); byte[] digest = SHA256.HashData(canonical);
                await using SqliteCommand stage = connection.CreateCommand(); stage.Transaction = transaction; stage.CommandTimeout = owner.TimeoutSeconds(); stage.CommandText = $"INSERT INTO {owner._names.SubjectLifecycleScopeStage}(domain_ordinal,source_rowid,prior_digest,prior_value,replacement_digest,replacement_value) VALUES($domain,$rowid,$digest,$canonical,$digest,X'');"; stage.Parameters.AddWithValue("$domain", current.DomainOrdinal); stage.Parameters.AddWithValue("$rowid", row.RowId); stage.Parameters.Add("$digest", SqliteType.Blob).Value = digest; stage.Parameters.Add("$canonical", SqliteType.Blob).Value = canonical; await stage.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
                await using SqliteCommand delete = connection.CreateCommand(); delete.Transaction = transaction; delete.CommandTimeout = owner.TimeoutSeconds(); delete.CommandText = $"DELETE FROM {(current.DomainOrdinal == 0 ? owner._names.SubjectLifecycleMemberships : owner._names.SubjectLifecycleFacts)} WHERE rowid=$rowid;"; delete.Parameters.AddWithValue("$rowid", row.RowId); if (await delete.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false) != 1) throw new InvalidDataException(BaseSubjectErrorCodes.LifecycleProviderContractInvalid);
                rolling = SHA256.HashData([.. rolling, .. canonical]); checked { examined++; changed++; bytes += canonical.LongLength; } last = row.RowId;
            }
            int nextDomain = rows.Count == 0 ? checked(current.DomainOrdinal + 1) : current.DomainOrdinal; long nextLast = rows.Count == 0 ? 0 : last;
            await using SqliteCommand update = connection.CreateCommand(); update.Transaction = transaction; update.CommandTimeout = owner.TimeoutSeconds(); update.CommandText = $"UPDATE {owner._names.SubjectLifecycleMaintenance} SET domain_ordinal=$domain,last_rowid=$last,examined_count=$examined,changed_count=$changed,canonical_bytes=$bytes,rolling_checksum=$checksum WHERE singleton=1 AND domain_ordinal=$expectedDomain AND last_rowid=$expectedLast;"; update.Parameters.AddWithValue("$domain", nextDomain); update.Parameters.AddWithValue("$last", nextLast); update.Parameters.AddWithValue("$examined", examined); update.Parameters.AddWithValue("$changed", changed); update.Parameters.AddWithValue("$bytes", bytes); update.Parameters.AddWithValue("$checksum", Convert.ToHexStringLower(rolling)); update.Parameters.AddWithValue("$expectedDomain", current.DomainOrdinal); update.Parameters.AddWithValue("$expectedLast", current.LastRowId); if (await update.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false) != 1) throw new InvalidDataException(BaseSubjectErrorCodes.LifecycleProviderContractInvalid);
            await transaction.CommitAsync(CancellationToken.None).ConfigureAwait(false); return rows.Count != 0;
        }

        private static void ValidatePruneProgress(RotationProgress progress, BaseSubjectLifecycleMaintenanceExecutionRequest request)
        {
            if (progress.Kind != BaseSubjectLifecycleMaintenanceKind.Prune || !string.Equals(progress.Scope, request.Identity.Scope, StringComparison.Ordinal) || !string.Equals(progress.Operation, request.Identity.Operation, StringComparison.Ordinal) || !string.Equals(progress.RequestKey, request.Identity.IdempotencyKey, StringComparison.Ordinal) || !CryptographicOperations.FixedTimeEquals(progress.Fingerprint, request.Identity.Fingerprint.ToArray()) || !CryptographicOperations.FixedTimeEquals(progress.PlanChecksum, request.PlanChecksum) || progress.ExpectedStoreGeneration != request.ExpectedStoreGeneration || progress.ExpectedRestoreEpoch != request.ExpectedRestoreEpoch || progress.ExpectedDeliveryEpoch != request.ExpectedDeliveryEpoch || progress.ExpectedScopeGeneration != request.ExpectedScopeProtectionGeneration || !string.Equals(progress.OldKeyId, request.ExpectedScopeProtectionKeyId, StringComparison.Ordinal) || progress.ReplacementKeyId.Length != 0 || progress.DomainOrdinal is < 0 or > 2 || progress.LastRowId < 0 || progress.ExaminedCount != progress.ChangedCount || progress.ChangedCount < 0 || progress.CanonicalBytes < 0 || progress.RollingChecksum.Length != 64) throw new InvalidDataException(BaseSubjectErrorCodes.LifecycleProviderContractInvalid);
        }

        private async ValueTask<RotationEvidence> ValidatePruneStageAsync(SqliteConnection connection, SqliteTransaction transaction, BaseSubjectLifecycleMaintenanceExecutionRequest request, CancellationToken cancellationToken)
        {
            long count = 0, bytes = 0; byte[] rolling = SHA256.HashData([]);
            for (int domain = 0; domain < 2; domain++)
            {
                long after = 0;
                while (true)
                {
                    await using SqliteCommand select = connection.CreateCommand(); select.Transaction = transaction; select.CommandTimeout = owner.TimeoutSeconds(); select.CommandText = $"SELECT source_rowid,prior_digest,prior_value,replacement_digest,replacement_value FROM {owner._names.SubjectLifecycleScopeStage} WHERE domain_ordinal=$domain AND source_rowid>$after ORDER BY source_rowid LIMIT $take;"; select.Parameters.AddWithValue("$domain", domain); select.Parameters.AddWithValue("$after", after); select.Parameters.AddWithValue("$take", request.PageSize);
                    var rows = new List<(long RowId,byte[] Digest,byte[] Canonical,byte[] Replacement,byte[] ReplacementValue)>(); await using (SqliteDataReader reader = await select.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false)) while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false)) rows.Add((reader.GetInt64(0),(byte[])reader.GetValue(1),(byte[])reader.GetValue(2),(byte[])reader.GetValue(3),(byte[])reader.GetValue(4)));
                    if (rows.Count == 0) break;
                    foreach (var row in rows) { byte[] digest = SHA256.HashData(row.Canonical); if (!CryptographicOperations.FixedTimeEquals(digest, row.Digest) || !CryptographicOperations.FixedTimeEquals(digest, row.Replacement) || row.ReplacementValue.Length != 0) throw new InvalidDataException(BaseSubjectErrorCodes.LifecycleProviderContractInvalid); rolling = SHA256.HashData([.. rolling, .. row.Canonical]); checked { count++; bytes += row.Canonical.LongLength; } after = row.RowId; }
                }
            }
            return new(count, count, bytes, Convert.ToHexStringLower(rolling));
        }

        private string ConsumerRemovalTable(int ordinal) => ordinal switch
        {
            0 => owner._names.SubjectLifecycleMemberships,
            1 => owner._names.SubjectLifecycleCheckpoints,
            _ => throw new InvalidDataException(BaseSubjectErrorCodes.LifecycleProviderContractInvalid),
        };

        private async ValueTask<OperationResult<BaseSubjectLifecycleMaintenanceResult>> ExecuteStagedConsumerRemovalAsync(
            BaseSubjectLifecycleMaintenanceExecutionRequest request,
            CancellationToken cancellationToken)
        {
            await using SqliteConnection connection = await owner._connections.OpenAsync(cancellationToken).ConfigureAwait(false);
            await using (SqliteTransaction receiptTransaction = (SqliteTransaction)await connection.BeginTransactionAsync(System.Data.IsolationLevel.Serializable, cancellationToken).ConfigureAwait(false))
            {
                OperationResult<BaseSubjectLifecycleMaintenanceResult>? replay = await ReadMaintenanceReceiptAsync(connection, receiptTransaction, request, cancellationToken).ConfigureAwait(false);
                await receiptTransaction.RollbackAsync(CancellationToken.None).ConfigureAwait(false);
                if (replay is not null) return replay;
            }
            OperationResult<BaseSubjectLifecycleMaintenanceResult>? initialization = await InitializeConsumerRemovalAsync(connection, request, cancellationToken).ConfigureAwait(false);
            if (initialization is not null) return initialization;
            while (true)
            {
                RotationProgress progress = await ReadRotationProgressAsync(connection, null, cancellationToken).ConfigureAwait(false)
                    ?? throw new InvalidDataException(BaseSubjectErrorCodes.LifecycleProviderContractInvalid);
                ValidateConsumerRemovalProgress(progress, request);
                if (progress.DomainOrdinal == ConsumerRemovalDomains.Length) break;
                if (await StageConsumerRemovalPageAsync(connection, request, progress, cancellationToken).ConfigureAwait(false))
                    await owner._administrationOperations.BeforePhaseAsync("subjectLifecycleConsumerRemovalAfterPage", cancellationToken).ConfigureAwait(false);
            }

            await using SqliteTransaction transaction = (SqliteTransaction)await connection.BeginTransactionAsync(System.Data.IsolationLevel.Serializable, cancellationToken).ConfigureAwait(false);
            RotationProgress completed = await ReadRotationProgressAsync(connection, transaction, cancellationToken).ConfigureAwait(false)
                ?? throw new InvalidDataException(BaseSubjectErrorCodes.LifecycleProviderContractInvalid);
            ValidateConsumerRemovalProgress(completed, request);
            if (completed.DomainOrdinal != ConsumerRemovalDomains.Length) throw new InvalidDataException(BaseSubjectErrorCodes.LifecycleProviderContractInvalid);
            await ValidateConsumerProjectionAsync(connection, transaction, request, cancellationToken).ConfigureAwait(false);
            RotationEvidence stagedEvidence = await ValidateConsumerRemovalStageAsync(connection, transaction, request, cancellationToken).ConfigureAwait(false);
            if (stagedEvidence.Examined != completed.ExaminedCount || stagedEvidence.Changed != completed.ChangedCount || stagedEvidence.CanonicalBytes != completed.CanonicalBytes || !string.Equals(stagedEvidence.RollingChecksum, completed.RollingChecksum, StringComparison.Ordinal)) throw new InvalidDataException(BaseSubjectErrorCodes.LifecycleProviderContractInvalid);
            long deleted = 0;
            for (int domain = 0; domain < ConsumerRemovalDomains.Length; domain++)
            {
                long after = 0;
                while (true)
                {
                    await using SqliteCommand staged = connection.CreateCommand(); staged.Transaction = transaction; staged.CommandTimeout = owner.TimeoutSeconds();
                    staged.CommandText = $"SELECT source_rowid FROM {owner._names.SubjectLifecycleScopeStage} WHERE domain_ordinal=$domain AND source_rowid>$after ORDER BY source_rowid LIMIT $take;";
                    staged.Parameters.AddWithValue("$domain", domain); staged.Parameters.AddWithValue("$after", after); staged.Parameters.AddWithValue("$take", request.PageSize);
                    var rowIds = new List<long>(); await using (SqliteDataReader reader = await staged.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false)) while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false)) rowIds.Add(reader.GetInt64(0));
                    if (rowIds.Count == 0) break;
                    foreach (long rowId in rowIds)
                    {
                        await using SqliteCommand delete = connection.CreateCommand(); delete.Transaction = transaction; delete.CommandTimeout = owner.TimeoutSeconds();
                        delete.CommandText = $"DELETE FROM {ConsumerRemovalTable(domain)} WHERE rowid=$rowid AND consumer_id=$consumer AND consumer_version=$version;";
                        delete.Parameters.AddWithValue("$rowid", rowId); delete.Parameters.AddWithValue("$consumer", request.ConsumerId!); delete.Parameters.AddWithValue("$version", request.ConsumerVersion!.Value);
                        if (await delete.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false) != 1) throw new InvalidDataException(BaseSubjectErrorCodes.LifecycleProviderContractInvalid);
                        checked { deleted++; } after = rowId;
                    }
                }
            }
            if (deleted != completed.ChangedCount) throw new InvalidDataException(BaseSubjectErrorCodes.LifecycleProviderContractInvalid);
            await using (SqliteCommand publish = connection.CreateCommand())
            {
                publish.Transaction = transaction; publish.CommandTimeout = owner.TimeoutSeconds();
                publish.CommandText = $"DELETE FROM {owner._names.SubjectLifecycleConsumers} WHERE consumer_id=$consumer AND consumer_version=$version AND projection_generation=$generation; DELETE FROM {owner._names.SubjectLifecycleScopeStage}; DELETE FROM {owner._names.SubjectLifecycleMaintenance} WHERE singleton=1;";
                publish.Parameters.AddWithValue("$consumer", request.ConsumerId!); publish.Parameters.AddWithValue("$version", request.ConsumerVersion!.Value); publish.Parameters.AddWithValue("$generation", request.ExpectedProjectionGeneration!.Value);
                if (await publish.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false) < 1) throw new InvalidDataException(BaseSubjectErrorCodes.LifecycleProviderContractInvalid);
            }
            var result = new BaseSubjectLifecycleMaintenanceResult { Kind = request.Kind, ExaminedCount = checked(completed.ExaminedCount + 1), ChangedCount = checked(completed.ChangedCount + 1), CanonicalBytes = completed.CanonicalBytes, RollingChecksum = completed.RollingChecksum, DeliveryEpoch = request.ExpectedDeliveryEpoch, ProjectionGeneration = null, Duplicate = false };
            await InsertMaintenanceReceiptAsync(connection, transaction, request, result, cancellationToken).ConfigureAwait(false);
            await transaction.CommitAsync(CancellationToken.None).ConfigureAwait(false);
            return OperationResults.Ok(result);
        }

        private async ValueTask<OperationResult<BaseSubjectLifecycleMaintenanceResult>?> InitializeConsumerRemovalAsync(SqliteConnection connection, BaseSubjectLifecycleMaintenanceExecutionRequest request, CancellationToken cancellationToken)
        {
            await using SqliteTransaction transaction = (SqliteTransaction)await connection.BeginTransactionAsync(System.Data.IsolationLevel.Serializable, cancellationToken).ConfigureAwait(false);
            RotationProgress? existing = await ReadRotationProgressAsync(connection, transaction, cancellationToken).ConfigureAwait(false);
            if (existing is not null) { ValidateConsumerRemovalProgress(existing, request); await transaction.RollbackAsync(CancellationToken.None).ConfigureAwait(false); return null; }
            await ValidateConsumerProjectionAsync(connection, transaction, request, cancellationToken).ConfigureAwait(false);
            await using SqliteCommand insert = connection.CreateCommand(); insert.Transaction = transaction; insert.CommandTimeout = owner.TimeoutSeconds();
            insert.CommandText = $"INSERT INTO {owner._names.SubjectLifecycleMaintenance}(singleton,kind,request_scope,request_operation,request_key,fingerprint,plan_checksum,expected_store_generation,expected_restore_epoch,expected_delivery_epoch,expected_scope_generation,old_key_id,replacement_key_id,domain_ordinal,last_rowid,examined_count,changed_count,canonical_bytes,rolling_checksum) VALUES(1,$kind,$scope,$operation,$key,$fingerprint,$plan,$store,$restore,$delivery,$scopeGeneration,$old,'',0,0,0,0,0,$checksum);";
            insert.Parameters.AddWithValue("$kind", (int)request.Kind); insert.Parameters.AddWithValue("$scope", request.Identity.Scope); insert.Parameters.AddWithValue("$operation", request.Identity.Operation); insert.Parameters.AddWithValue("$key", request.Identity.IdempotencyKey); insert.Parameters.Add("$fingerprint", SqliteType.Blob).Value = request.Identity.Fingerprint.ToArray(); insert.Parameters.Add("$plan", SqliteType.Blob).Value = request.PlanChecksum; insert.Parameters.AddWithValue("$store", request.ExpectedStoreGeneration); insert.Parameters.AddWithValue("$restore", request.ExpectedRestoreEpoch); insert.Parameters.AddWithValue("$delivery", request.ExpectedDeliveryEpoch); insert.Parameters.AddWithValue("$scopeGeneration", request.ExpectedScopeProtectionGeneration); insert.Parameters.AddWithValue("$old", request.ExpectedScopeProtectionKeyId); insert.Parameters.AddWithValue("$checksum", EmptyRotationChecksum);
            await insert.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false); await transaction.CommitAsync(CancellationToken.None).ConfigureAwait(false); return null;
        }

        private async ValueTask<bool> StageConsumerRemovalPageAsync(SqliteConnection connection, BaseSubjectLifecycleMaintenanceExecutionRequest request, RotationProgress expected, CancellationToken cancellationToken)
        {
            await using SqliteTransaction transaction = (SqliteTransaction)await connection.BeginTransactionAsync(System.Data.IsolationLevel.Serializable, cancellationToken).ConfigureAwait(false);
            RotationProgress current = await ReadRotationProgressAsync(connection, transaction, cancellationToken).ConfigureAwait(false) ?? throw new InvalidDataException(BaseSubjectErrorCodes.LifecycleProviderContractInvalid);
            ValidateConsumerRemovalProgress(current, request); if (current.DomainOrdinal != expected.DomainOrdinal || current.LastRowId != expected.LastRowId) throw new InvalidDataException(BaseSubjectErrorCodes.LifecycleProviderContractInvalid);
            await using SqliteCommand select = connection.CreateCommand(); select.Transaction = transaction; select.CommandTimeout = owner.TimeoutSeconds(); select.CommandText = $"SELECT rowid FROM {ConsumerRemovalTable(current.DomainOrdinal)} WHERE consumer_id=$consumer AND consumer_version=$version AND rowid>$after ORDER BY rowid LIMIT $take;"; select.Parameters.AddWithValue("$consumer", request.ConsumerId!); select.Parameters.AddWithValue("$version", request.ConsumerVersion!.Value); select.Parameters.AddWithValue("$after", current.LastRowId); select.Parameters.AddWithValue("$take", request.PageSize);
            var rows = new List<long>(); await using (SqliteDataReader reader = await select.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false)) while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false)) rows.Add(reader.GetInt64(0));
            long examined = current.ExaminedCount, changed = current.ChangedCount, bytes = current.CanonicalBytes, last = current.LastRowId; byte[] rolling = Convert.FromHexString(current.RollingChecksum);
            foreach (long rowId in rows)
            {
                byte[] canonical = Encoding.UTF8.GetBytes($"{current.DomainOrdinal}\0{rowId}\0{request.ConsumerId}\0{request.ConsumerVersion}"); byte[] digest = SHA256.HashData(canonical); rolling = SHA256.HashData([.. rolling, .. canonical]); checked { examined++; changed++; bytes += canonical.LongLength; } last = rowId;
                await using SqliteCommand stage = connection.CreateCommand(); stage.Transaction = transaction; stage.CommandTimeout = owner.TimeoutSeconds(); stage.CommandText = $"INSERT INTO {owner._names.SubjectLifecycleScopeStage}(domain_ordinal,source_rowid,prior_digest,prior_value,replacement_digest,replacement_value) VALUES($domain,$rowid,$digest,X'',$digest,X'');"; stage.Parameters.AddWithValue("$domain", current.DomainOrdinal); stage.Parameters.AddWithValue("$rowid", rowId); stage.Parameters.Add("$digest", SqliteType.Blob).Value = digest; await stage.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            }
            int nextDomain = rows.Count == 0 ? checked(current.DomainOrdinal + 1) : current.DomainOrdinal; long nextLast = rows.Count == 0 ? 0 : last;
            await using SqliteCommand update = connection.CreateCommand(); update.Transaction = transaction; update.CommandTimeout = owner.TimeoutSeconds(); update.CommandText = $"UPDATE {owner._names.SubjectLifecycleMaintenance} SET domain_ordinal=$domain,last_rowid=$last,examined_count=$examined,changed_count=$changed,canonical_bytes=$bytes,rolling_checksum=$checksum WHERE singleton=1 AND domain_ordinal=$expectedDomain AND last_rowid=$expectedLast;"; update.Parameters.AddWithValue("$domain", nextDomain); update.Parameters.AddWithValue("$last", nextLast); update.Parameters.AddWithValue("$examined", examined); update.Parameters.AddWithValue("$changed", changed); update.Parameters.AddWithValue("$bytes", bytes); update.Parameters.AddWithValue("$checksum", Convert.ToHexStringLower(rolling)); update.Parameters.AddWithValue("$expectedDomain", current.DomainOrdinal); update.Parameters.AddWithValue("$expectedLast", current.LastRowId); if (await update.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false) != 1) throw new InvalidDataException(BaseSubjectErrorCodes.LifecycleProviderContractInvalid); await transaction.CommitAsync(CancellationToken.None).ConfigureAwait(false); return rows.Count != 0;
        }

        private async ValueTask ValidateConsumerProjectionAsync(SqliteConnection connection, SqliteTransaction transaction, BaseSubjectLifecycleMaintenanceExecutionRequest request, CancellationToken cancellationToken)
        {
            (long restoreEpoch, long deliveryEpoch, long scopeGeneration, string scopeKeyId) = await ReadAuthorityAsync(connection, transaction, cancellationToken).ConfigureAwait(false);
            if (owner._schemaGeneration != request.ExpectedStoreGeneration || owner._schemaGeneration != request.ExpectedSchemaGeneration || restoreEpoch != request.ExpectedRestoreEpoch || deliveryEpoch != request.ExpectedDeliveryEpoch || scopeGeneration != request.ExpectedScopeProtectionGeneration || !string.Equals(scopeKeyId, request.ExpectedScopeProtectionKeyId, StringComparison.Ordinal)) throw new InvalidDataException(BaseSubjectErrorCodes.LifecycleProviderContractInvalid);
            await using SqliteCommand command = connection.CreateCommand(); command.Transaction = transaction; command.CommandTimeout = owner.TimeoutSeconds(); command.CommandText = $"SELECT projection_generation FROM {owner._names.SubjectLifecycleConsumers} WHERE consumer_id=$consumer AND consumer_version=$version AND contract_id=$contract AND contract_version=$contractVersion;"; command.Parameters.AddWithValue("$consumer", request.ConsumerId!); command.Parameters.AddWithValue("$version", request.ConsumerVersion!.Value); command.Parameters.AddWithValue("$contract", request.ContractId!); command.Parameters.AddWithValue("$contractVersion", request.ContractVersion!.Value); object? value = await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false); if (value is null || Convert.ToInt64(value, System.Globalization.CultureInfo.InvariantCulture) != request.ExpectedProjectionGeneration) throw new InvalidDataException(BaseSubjectErrorCodes.LifecycleProviderContractInvalid);
        }

        private static void ValidateConsumerRemovalProgress(RotationProgress progress, BaseSubjectLifecycleMaintenanceExecutionRequest request)
        {
            if (progress.Kind != BaseSubjectLifecycleMaintenanceKind.RemoveConsumer || !string.Equals(progress.Scope, request.Identity.Scope, StringComparison.Ordinal) || !string.Equals(progress.Operation, request.Identity.Operation, StringComparison.Ordinal) || !string.Equals(progress.RequestKey, request.Identity.IdempotencyKey, StringComparison.Ordinal) || !CryptographicOperations.FixedTimeEquals(progress.Fingerprint, request.Identity.Fingerprint.ToArray()) || !CryptographicOperations.FixedTimeEquals(progress.PlanChecksum, request.PlanChecksum) || progress.ExpectedStoreGeneration != request.ExpectedStoreGeneration || progress.ExpectedRestoreEpoch != request.ExpectedRestoreEpoch || progress.ExpectedDeliveryEpoch != request.ExpectedDeliveryEpoch || progress.ExpectedScopeGeneration != request.ExpectedScopeProtectionGeneration || !string.Equals(progress.OldKeyId, request.ExpectedScopeProtectionKeyId, StringComparison.Ordinal) || progress.ReplacementKeyId.Length != 0 || progress.DomainOrdinal < 0 || progress.DomainOrdinal > ConsumerRemovalDomains.Length || progress.LastRowId < 0 || progress.ExaminedCount < 0 || progress.ChangedCount < 0 || progress.CanonicalBytes < 0 || progress.RollingChecksum.Length != 64) throw new InvalidDataException(BaseSubjectErrorCodes.LifecycleProviderContractInvalid);
        }

        private async ValueTask<RotationEvidence> ValidateConsumerRemovalStageAsync(SqliteConnection connection, SqliteTransaction transaction, BaseSubjectLifecycleMaintenanceExecutionRequest request, CancellationToken cancellationToken)
        {
            long examined = 0, changed = 0, bytes = 0; byte[] rolling = SHA256.HashData([]);
            for (int domain = 0; domain < ConsumerRemovalDomains.Length; domain++)
            {
                long after = 0;
                while (true)
                {
                    await using SqliteCommand command = connection.CreateCommand(); command.Transaction = transaction; command.CommandTimeout = owner.TimeoutSeconds(); command.CommandText = $"SELECT source_rowid,prior_digest,prior_value,replacement_digest,replacement_value FROM {owner._names.SubjectLifecycleScopeStage} WHERE domain_ordinal=$domain AND source_rowid>$after ORDER BY source_rowid LIMIT $take;"; command.Parameters.AddWithValue("$domain", domain); command.Parameters.AddWithValue("$after", after); command.Parameters.AddWithValue("$take", request.PageSize);
                    var rows = new List<(long RowId,byte[] Prior,byte[] PriorValue,byte[] Replacement,byte[] ReplacementValue)>(); await using (SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false)) while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false)) rows.Add((reader.GetInt64(0),(byte[])reader.GetValue(1),(byte[])reader.GetValue(2),(byte[])reader.GetValue(3),(byte[])reader.GetValue(4)));
                    if (rows.Count == 0) break;
                    foreach (var row in rows)
                    {
                        byte[] canonical = Encoding.UTF8.GetBytes($"{domain}\0{row.RowId}\0{request.ConsumerId}\0{request.ConsumerVersion}"); byte[] digest = SHA256.HashData(canonical);
                        if (row.PriorValue.Length != 0 || row.ReplacementValue.Length != 0 || !CryptographicOperations.FixedTimeEquals(row.Prior, digest) || !CryptographicOperations.FixedTimeEquals(row.Replacement, digest)) throw new InvalidDataException(BaseSubjectErrorCodes.LifecycleProviderContractInvalid);
                        await using SqliteCommand exists = connection.CreateCommand(); exists.Transaction = transaction; exists.CommandTimeout = owner.TimeoutSeconds(); exists.CommandText = $"SELECT EXISTS(SELECT 1 FROM {ConsumerRemovalTable(domain)} WHERE rowid=$rowid AND consumer_id=$consumer AND consumer_version=$version);"; exists.Parameters.AddWithValue("$rowid", row.RowId); exists.Parameters.AddWithValue("$consumer", request.ConsumerId!); exists.Parameters.AddWithValue("$version", request.ConsumerVersion!.Value); if (Convert.ToInt32(await exists.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false), System.Globalization.CultureInfo.InvariantCulture) != 1) throw new InvalidDataException(BaseSubjectErrorCodes.LifecycleProviderContractInvalid);
                        rolling = SHA256.HashData([.. rolling, .. canonical]); checked { examined++; changed++; bytes += canonical.LongLength; } after = row.RowId;
                    }
                }
            }
            return new RotationEvidence(examined, changed, bytes, Convert.ToHexStringLower(rolling));
        }

        private async ValueTask<OperationResult<BaseSubjectLifecycleMaintenanceResult>> ExecuteStagedDeliveryRebuildAsync(BaseSubjectLifecycleMaintenanceExecutionRequest request, CancellationToken cancellationToken)
        {
            await using SqliteConnection connection = await owner._connections.OpenAsync(cancellationToken).ConfigureAwait(false);
            await using (SqliteTransaction receiptTransaction = (SqliteTransaction)await connection.BeginTransactionAsync(System.Data.IsolationLevel.Serializable, cancellationToken).ConfigureAwait(false))
            {
                OperationResult<BaseSubjectLifecycleMaintenanceResult>? replay = await ReadMaintenanceReceiptAsync(connection, receiptTransaction, request, cancellationToken).ConfigureAwait(false);
                await receiptTransaction.RollbackAsync(CancellationToken.None).ConfigureAwait(false); if (replay is not null) return replay;
            }
            await InitializeDeliveryRebuildAsync(connection, request, cancellationToken).ConfigureAwait(false);
            while (true)
            {
                RotationProgress progress = await ReadRotationProgressAsync(connection, null, cancellationToken).ConfigureAwait(false) ?? throw new InvalidDataException(BaseSubjectErrorCodes.LifecycleProviderContractInvalid);
                ValidateDeliveryRebuildProgress(progress, request); if (progress.DomainOrdinal == 1) break;
                if (await StageDeliveryRebuildPageAsync(connection, request, progress, cancellationToken).ConfigureAwait(false))
                    await owner._administrationOperations.BeforePhaseAsync("subjectLifecycleDeliveryRebuildAfterPage", cancellationToken).ConfigureAwait(false);
            }
            await using SqliteTransaction transaction = (SqliteTransaction)await connection.BeginTransactionAsync(System.Data.IsolationLevel.Serializable, cancellationToken).ConfigureAwait(false);
            RotationProgress completed = await ReadRotationProgressAsync(connection, transaction, cancellationToken).ConfigureAwait(false) ?? throw new InvalidDataException(BaseSubjectErrorCodes.LifecycleProviderContractInvalid);
            ValidateDeliveryRebuildProgress(completed, request); await ValidateConsumerProjectionAsync(connection, transaction, request, cancellationToken).ConfigureAwait(false);
            long replacementGeneration = checked(request.ExpectedProjectionGeneration!.Value + 1);
            RotationEvidence evidence = await ValidateDeliveryRebuildStageAsync(connection, transaction, request, cancellationToken).ConfigureAwait(false);
            if (evidence.Examined != completed.ExaminedCount || evidence.Changed != completed.ChangedCount || evidence.CanonicalBytes != completed.CanonicalBytes || !string.Equals(evidence.RollingChecksum, completed.RollingChecksum, StringComparison.Ordinal)) throw new InvalidDataException(BaseSubjectErrorCodes.LifecycleProviderContractInvalid);
            await using (SqliteCommand publish = connection.CreateCommand())
            {
                publish.Transaction = transaction; publish.CommandTimeout = owner.TimeoutSeconds(); publish.CommandText = $"""
DELETE FROM {owner._names.SubjectLifecycleMemberships} WHERE consumer_id=$consumer AND consumer_version=$version;
INSERT INTO {owner._names.SubjectLifecycleMemberships}(consumer_id,consumer_version,consumer_checksum,contract_id,contract_version,projection_generation,matched_state,scope_kind,scope_index_digest,protected_scope_value,commit_position,subject_id,authority_epoch,incarnation,subject_sequence)
SELECT consumer_id,consumer_version,consumer_checksum,contract_id,contract_version,projection_generation,matched_state,scope_kind,scope_index_digest,protected_scope_value,commit_position,subject_id,authority_epoch,incarnation,subject_sequence FROM {owner._names.SubjectLifecycleMembershipStage} ORDER BY source_rowid;
UPDATE {owner._names.SubjectLifecycleConsumers} SET projection_generation=$generation WHERE consumer_id=$consumer AND consumer_version=$version AND projection_generation=$expected;
UPDATE {owner._names.SubjectLifecycleCheckpoints} SET projection_generation=$generation,checkpoint_generation=checkpoint_generation+1,state=0,overtaken_at=NULL WHERE consumer_id=$consumer AND consumer_version=$version;
DELETE FROM {owner._names.SubjectLifecycleMembershipStage};
DELETE FROM {owner._names.SubjectLifecycleMaintenance} WHERE singleton=1;
"""; publish.Parameters.AddWithValue("$consumer", request.ConsumerId!); publish.Parameters.AddWithValue("$version", request.ConsumerVersion!.Value); publish.Parameters.AddWithValue("$generation", replacementGeneration); publish.Parameters.AddWithValue("$expected", request.ExpectedProjectionGeneration.Value); await publish.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            }
            var result = new BaseSubjectLifecycleMaintenanceResult { Kind = request.Kind, ExaminedCount = checked(completed.ExaminedCount + 1), ChangedCount = checked(completed.ChangedCount + 1), CanonicalBytes = completed.CanonicalBytes, RollingChecksum = completed.RollingChecksum, DeliveryEpoch = request.ExpectedDeliveryEpoch, ProjectionGeneration = replacementGeneration, Duplicate = false };
            await InsertMaintenanceReceiptAsync(connection, transaction, request, result, cancellationToken).ConfigureAwait(false); await transaction.CommitAsync(CancellationToken.None).ConfigureAwait(false); return OperationResults.Ok(result);
        }

        private async ValueTask InitializeDeliveryRebuildAsync(SqliteConnection connection, BaseSubjectLifecycleMaintenanceExecutionRequest request, CancellationToken cancellationToken)
        {
            await using SqliteTransaction transaction = (SqliteTransaction)await connection.BeginTransactionAsync(System.Data.IsolationLevel.Serializable, cancellationToken).ConfigureAwait(false);
            RotationProgress? existing = await ReadRotationProgressAsync(connection, transaction, cancellationToken).ConfigureAwait(false);
            if (existing is not null) { ValidateDeliveryRebuildProgress(existing, request); await transaction.RollbackAsync(CancellationToken.None).ConfigureAwait(false); return; }
            await ValidateConsumerProjectionAsync(connection, transaction, request, cancellationToken).ConfigureAwait(false);
            if (!owner._options.SubjectLifecycleConsumers.Any(value => value.Id == request.ConsumerId && value.Version == request.ConsumerVersion)) throw new InvalidDataException(BaseSubjectErrorCodes.LifecycleProviderContractInvalid);
            await using SqliteCommand insert = connection.CreateCommand(); insert.Transaction = transaction; insert.CommandTimeout = owner.TimeoutSeconds(); insert.CommandText = $"INSERT INTO {owner._names.SubjectLifecycleMaintenance}(singleton,kind,request_scope,request_operation,request_key,fingerprint,plan_checksum,expected_store_generation,expected_restore_epoch,expected_delivery_epoch,expected_scope_generation,old_key_id,replacement_key_id,domain_ordinal,last_rowid,examined_count,changed_count,canonical_bytes,rolling_checksum) VALUES(1,$kind,$scope,$operation,$key,$fingerprint,$plan,$store,$restore,$delivery,$scopeGeneration,$old,'',0,0,0,0,0,$checksum);"; insert.Parameters.AddWithValue("$kind", (int)request.Kind); insert.Parameters.AddWithValue("$scope", request.Identity.Scope); insert.Parameters.AddWithValue("$operation", request.Identity.Operation); insert.Parameters.AddWithValue("$key", request.Identity.IdempotencyKey); insert.Parameters.Add("$fingerprint", SqliteType.Blob).Value = request.Identity.Fingerprint.ToArray(); insert.Parameters.Add("$plan", SqliteType.Blob).Value = request.PlanChecksum; insert.Parameters.AddWithValue("$store", request.ExpectedStoreGeneration); insert.Parameters.AddWithValue("$restore", request.ExpectedRestoreEpoch); insert.Parameters.AddWithValue("$delivery", request.ExpectedDeliveryEpoch); insert.Parameters.AddWithValue("$scopeGeneration", request.ExpectedScopeProtectionGeneration); insert.Parameters.AddWithValue("$old", request.ExpectedScopeProtectionKeyId); insert.Parameters.AddWithValue("$checksum", EmptyRotationChecksum); await insert.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false); await transaction.CommitAsync(CancellationToken.None).ConfigureAwait(false);
        }

        private async ValueTask<bool> StageDeliveryRebuildPageAsync(SqliteConnection connection, BaseSubjectLifecycleMaintenanceExecutionRequest request, RotationProgress expected, CancellationToken cancellationToken)
        {
            BaseSubjectLifecycleConsumerDefinition definition = owner._options.SubjectLifecycleConsumers.Single(value => value.Id == request.ConsumerId && value.Version == request.ConsumerVersion);
            await using SqliteTransaction transaction = (SqliteTransaction)await connection.BeginTransactionAsync(System.Data.IsolationLevel.Serializable, cancellationToken).ConfigureAwait(false); RotationProgress current = await ReadRotationProgressAsync(connection, transaction, cancellationToken).ConfigureAwait(false) ?? throw new InvalidDataException(BaseSubjectErrorCodes.LifecycleProviderContractInvalid); ValidateDeliveryRebuildProgress(current, request); if (current.LastRowId != expected.LastRowId) throw new InvalidDataException(BaseSubjectErrorCodes.LifecycleProviderContractInvalid);
            string checksum; long cutoff; await using (SqliteCommand consumer = connection.CreateCommand()) { consumer.Transaction = transaction; consumer.CommandTimeout = owner.TimeoutSeconds(); consumer.CommandText = $"SELECT consumer_checksum,cutoff_position FROM {owner._names.SubjectLifecycleConsumers} WHERE consumer_id=$consumer AND consumer_version=$version;"; consumer.Parameters.AddWithValue("$consumer", request.ConsumerId!); consumer.Parameters.AddWithValue("$version", request.ConsumerVersion!.Value); await using SqliteDataReader reader = await consumer.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false); if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false)) throw new InvalidDataException(BaseSubjectErrorCodes.LifecycleProviderContractInvalid); checksum = reader.GetString(0); cutoff = reader.GetInt64(1); }
            await using SqliteCommand select = connection.CreateCommand(); select.Transaction = transaction; select.CommandTimeout = owner.TimeoutSeconds(); select.CommandText = $"SELECT rowid,COALESCE(current_state,3),scope_kind,scope_index_digest,protected_scope_value,commit_position,subject_id,authority_epoch,incarnation,subject_sequence FROM {owner._names.SubjectLifecycleFacts} WHERE contract_id=$contract AND contract_version=$version AND commit_position>$cutoff AND rowid>$after ORDER BY rowid LIMIT $take;"; select.Parameters.AddWithValue("$contract", request.ContractId!); select.Parameters.AddWithValue("$version", request.ContractVersion!.Value); select.Parameters.AddWithValue("$cutoff", cutoff); select.Parameters.AddWithValue("$after", current.LastRowId); select.Parameters.AddWithValue("$take", request.PageSize);
            var rows = new List<(long RowId,int State,int ScopeKind,byte[] Digest,byte[] Value,long Position,string Subject,byte[] Epoch,byte[] Incarnation,long Sequence)>(); await using (SqliteDataReader reader = await select.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false)) while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false)) rows.Add((reader.GetInt64(0),reader.GetInt32(1),reader.GetInt32(2),(byte[])reader.GetValue(3),(byte[])reader.GetValue(4),reader.GetInt64(5),reader.GetString(6),(byte[])reader.GetValue(7),(byte[])reader.GetValue(8),reader.GetInt64(9)));
            long examined=current.ExaminedCount,changed=current.ChangedCount,bytes=current.CanonicalBytes,last=current.LastRowId; byte[] rolling=Convert.FromHexString(current.RollingChecksum); long generation=checked(request.ExpectedProjectionGeneration!.Value+1);
            foreach (var row in rows) { checked { examined++; } last=row.RowId; if (!definition.ObservedStates.Contains((BaseSubjectLifecycleState)row.State)) continue; byte[] canonical=Encoding.UTF8.GetBytes($"{row.RowId}\0{row.Position}\0{row.Subject}\0{row.State}"); rolling=SHA256.HashData([..rolling,..canonical]); checked { changed++; bytes+=canonical.LongLength; } await using SqliteCommand stage=connection.CreateCommand(); stage.Transaction=transaction;stage.CommandTimeout=owner.TimeoutSeconds();stage.CommandText=$"INSERT INTO {owner._names.SubjectLifecycleMembershipStage}(source_rowid,consumer_id,consumer_version,consumer_checksum,contract_id,contract_version,projection_generation,matched_state,scope_kind,scope_index_digest,protected_scope_value,commit_position,subject_id,authority_epoch,incarnation,subject_sequence) VALUES($rowid,$consumer,$consumerVersion,$checksum,$contract,$contractVersion,$generation,$state,$scopeKind,$digest,$value,$position,$subject,$epoch,$incarnation,$sequence);";stage.Parameters.AddWithValue("$rowid",row.RowId);stage.Parameters.AddWithValue("$consumer",request.ConsumerId!);stage.Parameters.AddWithValue("$consumerVersion",request.ConsumerVersion!.Value);stage.Parameters.AddWithValue("$checksum",checksum);stage.Parameters.AddWithValue("$contract",request.ContractId!);stage.Parameters.AddWithValue("$contractVersion",request.ContractVersion!.Value);stage.Parameters.AddWithValue("$generation",generation);stage.Parameters.AddWithValue("$state",row.State);stage.Parameters.AddWithValue("$scopeKind",row.ScopeKind);stage.Parameters.Add("$digest",SqliteType.Blob).Value=row.Digest;stage.Parameters.Add("$value",SqliteType.Blob).Value=row.Value;stage.Parameters.AddWithValue("$position",row.Position);stage.Parameters.AddWithValue("$subject",row.Subject);stage.Parameters.Add("$epoch",SqliteType.Blob).Value=row.Epoch;stage.Parameters.Add("$incarnation",SqliteType.Blob).Value=row.Incarnation;stage.Parameters.AddWithValue("$sequence",row.Sequence);await stage.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false); }
            int domain=rows.Count==0?1:0;long nextLast=rows.Count==0?0:last;await using SqliteCommand update=connection.CreateCommand();update.Transaction=transaction;update.CommandTimeout=owner.TimeoutSeconds();update.CommandText=$"UPDATE {owner._names.SubjectLifecycleMaintenance} SET domain_ordinal=$domain,last_rowid=$last,examined_count=$examined,changed_count=$changed,canonical_bytes=$bytes,rolling_checksum=$checksum WHERE singleton=1 AND last_rowid=$expectedLast;";update.Parameters.AddWithValue("$domain",domain);update.Parameters.AddWithValue("$last",nextLast);update.Parameters.AddWithValue("$examined",examined);update.Parameters.AddWithValue("$changed",changed);update.Parameters.AddWithValue("$bytes",bytes);update.Parameters.AddWithValue("$checksum",Convert.ToHexStringLower(rolling));update.Parameters.AddWithValue("$expectedLast",current.LastRowId);if(await update.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false)!=1)throw new InvalidDataException(BaseSubjectErrorCodes.LifecycleProviderContractInvalid);await transaction.CommitAsync(CancellationToken.None).ConfigureAwait(false);return rows.Count!=0;
        }

        private static void ValidateDeliveryRebuildProgress(RotationProgress progress, BaseSubjectLifecycleMaintenanceExecutionRequest request)
        {
            if (progress.Kind != BaseSubjectLifecycleMaintenanceKind.RebuildDeliveryProjection || !string.Equals(progress.Scope, request.Identity.Scope, StringComparison.Ordinal) || !string.Equals(progress.Operation, request.Identity.Operation, StringComparison.Ordinal) || !string.Equals(progress.RequestKey, request.Identity.IdempotencyKey, StringComparison.Ordinal) || !CryptographicOperations.FixedTimeEquals(progress.Fingerprint, request.Identity.Fingerprint.ToArray()) || !CryptographicOperations.FixedTimeEquals(progress.PlanChecksum, request.PlanChecksum) || progress.ExpectedStoreGeneration != request.ExpectedStoreGeneration || progress.ExpectedRestoreEpoch != request.ExpectedRestoreEpoch || progress.ExpectedDeliveryEpoch != request.ExpectedDeliveryEpoch || progress.ExpectedScopeGeneration != request.ExpectedScopeProtectionGeneration || !string.Equals(progress.OldKeyId, request.ExpectedScopeProtectionKeyId, StringComparison.Ordinal) || progress.ReplacementKeyId.Length != 0 || progress.DomainOrdinal is < 0 or > 1 || progress.LastRowId < 0 || progress.ExaminedCount < progress.ChangedCount || progress.ChangedCount < 0 || progress.CanonicalBytes < 0 || progress.RollingChecksum.Length != 64) throw new InvalidDataException(BaseSubjectErrorCodes.LifecycleProviderContractInvalid);
        }

        private async ValueTask<RotationEvidence> ValidateDeliveryRebuildStageAsync(SqliteConnection connection, SqliteTransaction transaction, BaseSubjectLifecycleMaintenanceExecutionRequest request, CancellationToken cancellationToken)
        {
            long cutoff; await using (SqliteCommand consumer = connection.CreateCommand()) { consumer.Transaction = transaction; consumer.CommandTimeout = owner.TimeoutSeconds(); consumer.CommandText = $"SELECT cutoff_position FROM {owner._names.SubjectLifecycleConsumers} WHERE consumer_id=$consumer AND consumer_version=$version;"; consumer.Parameters.AddWithValue("$consumer", request.ConsumerId!); consumer.Parameters.AddWithValue("$version", request.ConsumerVersion!.Value); object? value = await consumer.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false); if (value is null) throw new InvalidDataException(BaseSubjectErrorCodes.LifecycleProviderContractInvalid); cutoff = Convert.ToInt64(value, System.Globalization.CultureInfo.InvariantCulture); }
            long examined; await using (SqliteCommand count = connection.CreateCommand()) { count.Transaction = transaction; count.CommandTimeout = owner.TimeoutSeconds(); count.CommandText = $"SELECT COUNT(*) FROM {owner._names.SubjectLifecycleFacts} WHERE contract_id=$contract AND contract_version=$version AND commit_position>$cutoff;"; count.Parameters.AddWithValue("$contract", request.ContractId!); count.Parameters.AddWithValue("$version", request.ContractVersion!.Value); count.Parameters.AddWithValue("$cutoff", cutoff); examined = Convert.ToInt64(await count.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false), System.Globalization.CultureInfo.InvariantCulture); }
            long changed = 0, bytes = 0, after = 0; byte[] rolling = SHA256.HashData([]);
            while (true)
            {
                await using SqliteCommand rows = connection.CreateCommand(); rows.Transaction = transaction; rows.CommandTimeout = owner.TimeoutSeconds(); rows.CommandText = $"""
SELECT s.source_rowid,s.commit_position,s.subject_id,s.matched_state,
       s.consumer_id=$consumer AND s.consumer_version=$consumerVersion AND s.consumer_checksum=(SELECT consumer_checksum FROM {owner._names.SubjectLifecycleConsumers} WHERE consumer_id=$consumer AND consumer_version=$consumerVersion)
       AND s.contract_id=$contract AND s.contract_version=$contractVersion
       AND s.projection_generation=$generation AND s.matched_state=COALESCE(f.current_state,3)
       AND s.scope_kind=f.scope_kind AND s.scope_index_digest=f.scope_index_digest AND s.protected_scope_value=f.protected_scope_value
       AND s.commit_position=f.commit_position AND s.subject_id=f.subject_id AND s.authority_epoch=f.authority_epoch AND s.incarnation=f.incarnation AND s.subject_sequence=f.subject_sequence
FROM {owner._names.SubjectLifecycleMembershipStage} s
LEFT JOIN {owner._names.SubjectLifecycleFacts} f ON f.rowid=s.source_rowid
WHERE s.source_rowid>$after ORDER BY s.source_rowid LIMIT $take;
"""; rows.Parameters.AddWithValue("$after", after); rows.Parameters.AddWithValue("$take", request.PageSize); rows.Parameters.AddWithValue("$consumer", request.ConsumerId!); rows.Parameters.AddWithValue("$consumerVersion", request.ConsumerVersion!.Value); rows.Parameters.AddWithValue("$contract", request.ContractId!); rows.Parameters.AddWithValue("$contractVersion", request.ContractVersion!.Value); rows.Parameters.AddWithValue("$generation", checked(request.ExpectedProjectionGeneration!.Value + 1));
                var page = new List<(long RowId,long Position,string Subject,int State,bool Matches)>(); await using (SqliteDataReader reader = await rows.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false)) while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false)) page.Add((reader.GetInt64(0),reader.GetInt64(1),reader.GetString(2),reader.GetInt32(3),!reader.IsDBNull(4)&&reader.GetBoolean(4)));
                if (page.Count == 0) break;
                foreach (var row in page) { if (!row.Matches) throw new InvalidDataException(BaseSubjectErrorCodes.LifecycleProviderContractInvalid); byte[] canonical=Encoding.UTF8.GetBytes($"{row.RowId}\0{row.Position}\0{row.Subject}\0{row.State}"); rolling=SHA256.HashData([..rolling,..canonical]); checked { changed++; bytes+=canonical.LongLength; } after=row.RowId; }
            }
            return new RotationEvidence(examined, changed, bytes, Convert.ToHexStringLower(rolling));
        }

        private string RotationTable(int ordinal) => ordinal switch
        {
            0 => owner._names.SubjectLifetimes,
            1 => owner._names.SubjectTerminalLifetimes,
            2 => owner._names.SubjectLifecycleFacts,
            3 => owner._names.SubjectLifecycleMemberships,
            4 => owner._names.SubjectLifecycleCheckpoints,
            _ => throw new InvalidDataException(BaseSubjectErrorCodes.LifecycleProviderContractInvalid),
        };

        private async ValueTask<OperationResult<BaseSubjectLifecycleMaintenanceResult>> ExecuteStagedRotationAsync(
            BaseSubjectLifecycleMaintenanceExecutionRequest request,
            CancellationToken cancellationToken)
        {
            await using SqliteConnection connection = await owner._connections.OpenAsync(cancellationToken).ConfigureAwait(false);
            await using (SqliteTransaction receiptTransaction = (SqliteTransaction)await connection.BeginTransactionAsync(System.Data.IsolationLevel.Serializable, cancellationToken).ConfigureAwait(false))
            {
                OperationResult<BaseSubjectLifecycleMaintenanceResult>? replay = await ReadMaintenanceReceiptAsync(connection, receiptTransaction, request, cancellationToken).ConfigureAwait(false);
                await receiptTransaction.RollbackAsync(CancellationToken.None).ConfigureAwait(false);
                if (replay is not null)
                    return replay;
            }
            if (owner._tokenProtector is null || owner._subjectScopes is null
                || !byte.TryParse(request.ReplacementScopeProtectionKeyId, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out byte replacement)
                || replacement == owner._subjectScopeProtectionKey
                || !owner._tokenProtector.CanIssueWithKey(replacement))
                return Failure(BaseSubjectErrorCodes.ScopeProtectionRotationConflict, OperationStatus.Conflict, ErrorCategory.Conflict);
            OperationResult<BaseSubjectLifecycleMaintenanceResult>? initialization = await InitializeRotationAsync(connection, request, cancellationToken).ConfigureAwait(false);
            if (initialization is not null)
                return initialization;

            while (true)
            {
                RotationProgress progress = await ReadRotationProgressAsync(connection, null, cancellationToken).ConfigureAwait(false)
                    ?? throw new InvalidDataException(BaseSubjectErrorCodes.LifecycleProviderContractInvalid);
                ValidateRotationProgress(progress, request);
                if (progress.DomainOrdinal == RotationDomains.Length)
                    break;
                if (await StageRotationPageAsync(connection, request, progress, replacement, cancellationToken).ConfigureAwait(false))
                    await owner._administrationOperations.BeforePhaseAsync("subjectLifecycleRotationAfterPage", cancellationToken).ConfigureAwait(false);
            }

            await using SqliteTransaction transaction = (SqliteTransaction)await connection.BeginTransactionAsync(System.Data.IsolationLevel.Serializable, cancellationToken).ConfigureAwait(false);
            RotationProgress completed = await ReadRotationProgressAsync(connection, transaction, cancellationToken).ConfigureAwait(false)
                ?? throw new InvalidDataException(BaseSubjectErrorCodes.LifecycleProviderContractInvalid);
            ValidateRotationProgress(completed, request);
            if (completed.DomainOrdinal != RotationDomains.Length)
                throw new InvalidDataException(BaseSubjectErrorCodes.LifecycleProviderContractInvalid);

            (long restoreEpoch, long deliveryEpoch, long scopeGeneration, string scopeKeyId) = await ReadAuthorityAsync(connection, transaction, cancellationToken).ConfigureAwait(false);
            if (owner._schemaGeneration != request.ExpectedStoreGeneration || owner._schemaGeneration != request.ExpectedSchemaGeneration
                || restoreEpoch != request.ExpectedRestoreEpoch
                || deliveryEpoch != request.ExpectedDeliveryEpoch
                || scopeGeneration != request.ExpectedScopeProtectionGeneration
                || !string.Equals(scopeKeyId, request.ExpectedScopeProtectionKeyId, StringComparison.Ordinal))
                return Failure(BaseSubjectErrorCodes.ScopeProtectionRotationConflict, OperationStatus.Conflict, ErrorCategory.Conflict);

            RotationEvidence evidence = await ValidateAndPublishRotationAsync(
                connection, transaction, replacement, request.PageSize, cancellationToken).ConfigureAwait(false);
            if (evidence.Examined != completed.ExaminedCount
                || evidence.Changed != completed.ChangedCount
                || evidence.CanonicalBytes != completed.CanonicalBytes
                || !string.Equals(evidence.RollingChecksum, completed.RollingChecksum, StringComparison.Ordinal))
                throw new InvalidDataException(BaseSubjectErrorCodes.LifecycleProviderContractInvalid);

            long nextDelivery = checked(deliveryEpoch + 1);
            long nextScopeGeneration = checked(scopeGeneration + 1);
            await using (SqliteCommand publish = connection.CreateCommand())
            {
                publish.Transaction = transaction;
                publish.CommandTimeout = owner.TimeoutSeconds();
                publish.CommandText = $"""
UPDATE {owner._names.SubjectLifecycleConsumers} SET projection_generation=projection_generation+1,published_graph_generation=published_graph_generation+1;
UPDATE {owner._names.SubjectLifecycleMemberships} SET projection_generation=projection_generation+1;
UPDATE {owner._names.SubjectLifecycleCheckpoints} SET projection_generation=projection_generation+1,checkpoint_generation=checkpoint_generation+1;
UPDATE {owner._names.ProviderState} SET value=$delivery WHERE key='subject_lifecycle_delivery_epoch';
UPDATE {owner._names.ProviderState} SET value=$generation WHERE key='subject_scope_protection_generation';
UPDATE {owner._names.ProviderState} SET value=$key WHERE key='subject_scope_protection_key_id';
DELETE FROM {owner._names.SubjectLifecycleScopeStage};
DELETE FROM {owner._names.SubjectLifecycleMaintenance} WHERE singleton=1;
""";
                publish.Parameters.AddWithValue("$delivery", nextDelivery.ToString(System.Globalization.CultureInfo.InvariantCulture));
                publish.Parameters.AddWithValue("$generation", nextScopeGeneration.ToString(System.Globalization.CultureInfo.InvariantCulture));
                publish.Parameters.AddWithValue("$key", replacement.ToString(System.Globalization.CultureInfo.InvariantCulture));
                await publish.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            }

            long projectionGeneration = await ReadMaximumProjectionGenerationAsync(connection, transaction, cancellationToken).ConfigureAwait(false);
            var result = new BaseSubjectLifecycleMaintenanceResult
            {
                Kind = request.Kind,
                ExaminedCount = checked(evidence.Examined + 1),
                ChangedCount = checked(evidence.Changed + 1),
                CanonicalBytes = evidence.CanonicalBytes,
                RollingChecksum = evidence.RollingChecksum,
                DeliveryEpoch = nextDelivery,
                ProjectionGeneration = projectionGeneration,
                Duplicate = false,
            };
            await InsertMaintenanceReceiptAsync(connection, transaction, request, result, cancellationToken).ConfigureAwait(false);
            await owner._administrationOperations.BeforePhaseAsync("subjectLifecycleRotationBeforePublicationCommit", cancellationToken).ConfigureAwait(false);
            await transaction.CommitAsync(CancellationToken.None).ConfigureAwait(false);
            owner._subjectScopeProtectionKey = replacement;
            owner._subjectScopeProtectionKeyId = replacement.ToString(System.Globalization.CultureInfo.InvariantCulture);
            return OperationResults.Ok(result);
        }

        private async ValueTask<OperationResult<BaseSubjectLifecycleMaintenanceResult>?> InitializeRotationAsync(
            SqliteConnection connection,
            BaseSubjectLifecycleMaintenanceExecutionRequest request,
            CancellationToken cancellationToken)
        {
            await using SqliteTransaction transaction = (SqliteTransaction)await connection.BeginTransactionAsync(System.Data.IsolationLevel.Serializable, cancellationToken).ConfigureAwait(false);
            OperationResult<BaseSubjectLifecycleMaintenanceResult>? replay = await ReadMaintenanceReceiptAsync(connection, transaction, request, cancellationToken).ConfigureAwait(false);
            if (replay is not null)
            {
                await transaction.RollbackAsync(CancellationToken.None).ConfigureAwait(false);
                return replay;
            }

            RotationProgress? existing = await ReadRotationProgressAsync(connection, transaction, cancellationToken).ConfigureAwait(false);
            if (existing is not null)
            {
                ValidateRotationProgress(existing, request);
                await transaction.RollbackAsync(CancellationToken.None).ConfigureAwait(false);
                return null;
            }

            (long restoreEpoch, long deliveryEpoch, long scopeGeneration, string scopeKeyId) = await ReadAuthorityAsync(connection, transaction, cancellationToken).ConfigureAwait(false);
            if (owner._schemaGeneration != request.ExpectedStoreGeneration || owner._schemaGeneration != request.ExpectedSchemaGeneration
                || restoreEpoch != request.ExpectedRestoreEpoch
                || deliveryEpoch != request.ExpectedDeliveryEpoch
                || scopeGeneration != request.ExpectedScopeProtectionGeneration
                || !string.Equals(scopeKeyId, request.ExpectedScopeProtectionKeyId, StringComparison.Ordinal))
            {
                await transaction.RollbackAsync(CancellationToken.None).ConfigureAwait(false);
                return Failure(BaseSubjectErrorCodes.ScopeProtectionRotationConflict, OperationStatus.Conflict, ErrorCategory.Conflict);
            }

            await using SqliteCommand insert = connection.CreateCommand();
            insert.Transaction = transaction;
            insert.CommandTimeout = owner.TimeoutSeconds();
            insert.CommandText = $"INSERT INTO {owner._names.SubjectLifecycleMaintenance}(singleton,kind,request_scope,request_operation,request_key,fingerprint,plan_checksum,expected_store_generation,expected_restore_epoch,expected_delivery_epoch,expected_scope_generation,old_key_id,replacement_key_id,domain_ordinal,last_rowid,examined_count,changed_count,canonical_bytes,rolling_checksum) VALUES(1,$kind,$scope,$operation,$key,$fingerprint,$plan,$store,$restore,$delivery,$scopeGeneration,$old,$replacement,0,0,0,0,0,$checksum);";
            insert.Parameters.AddWithValue("$kind", (int)request.Kind);
            insert.Parameters.AddWithValue("$scope", request.Identity.Scope);
            insert.Parameters.AddWithValue("$operation", request.Identity.Operation);
            insert.Parameters.AddWithValue("$key", request.Identity.IdempotencyKey);
            insert.Parameters.Add("$fingerprint", SqliteType.Blob).Value = request.Identity.Fingerprint.ToArray();
            insert.Parameters.Add("$plan", SqliteType.Blob).Value = request.PlanChecksum;
            insert.Parameters.AddWithValue("$store", request.ExpectedStoreGeneration);
            insert.Parameters.AddWithValue("$restore", request.ExpectedRestoreEpoch);
            insert.Parameters.AddWithValue("$delivery", request.ExpectedDeliveryEpoch);
            insert.Parameters.AddWithValue("$scopeGeneration", request.ExpectedScopeProtectionGeneration);
            insert.Parameters.AddWithValue("$old", request.ExpectedScopeProtectionKeyId);
            insert.Parameters.AddWithValue("$replacement", request.ReplacementScopeProtectionKeyId!);
            insert.Parameters.AddWithValue("$checksum", EmptyRotationChecksum);
            await insert.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            await transaction.CommitAsync(CancellationToken.None).ConfigureAwait(false);
            return null;
        }

        private async ValueTask<bool> StageRotationPageAsync(
            SqliteConnection connection,
            BaseSubjectLifecycleMaintenanceExecutionRequest request,
            RotationProgress expected,
            byte replacement,
            CancellationToken cancellationToken)
        {
            await using SqliteTransaction transaction = (SqliteTransaction)await connection.BeginTransactionAsync(System.Data.IsolationLevel.Serializable, cancellationToken).ConfigureAwait(false);
            RotationProgress current = await ReadRotationProgressAsync(connection, transaction, cancellationToken).ConfigureAwait(false)
                ?? throw new InvalidDataException(BaseSubjectErrorCodes.LifecycleProviderContractInvalid);
            ValidateRotationProgress(current, request);
            if (current.DomainOrdinal != expected.DomainOrdinal || current.LastRowId != expected.LastRowId)
                throw new InvalidDataException(BaseSubjectErrorCodes.LifecycleProviderContractInvalid);

            string table = RotationTable(current.DomainOrdinal);
            await using SqliteCommand select = connection.CreateCommand();
            select.Transaction = transaction;
            select.CommandTimeout = owner.TimeoutSeconds();
            select.CommandText = $"SELECT rowid,scope_kind,scope_index_digest,protected_scope_value FROM {table} WHERE rowid>$after ORDER BY rowid LIMIT $take;";
            select.Parameters.AddWithValue("$after", current.LastRowId);
            select.Parameters.AddWithValue("$take", request.PageSize);
            var rows = new List<(long RowId, BaseProtectedSubjectScope Scope)>();
            await using (SqliteDataReader reader = await select.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false))
                while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
                    rows.Add((reader.GetInt64(0), new BaseProtectedSubjectScope { Kind = (BaseSubjectScopeKind)reader.GetInt32(1), IndexDigest = (byte[])reader.GetValue(2), ProtectedCanonicalValue = (byte[])reader.GetValue(3) }));

            long examined = current.ExaminedCount;
            long changed = current.ChangedCount;
            long canonicalBytes = current.CanonicalBytes;
            long lastRowId = current.LastRowId;
            byte[] rolling = Convert.FromHexString(current.RollingChecksum);
            foreach ((long rowId, BaseProtectedSubjectScope prior) in rows)
            {
                BaseOwnedSubjectScopeEvidence logical = owner._subjectScopes!.Unprotect(prior)
                    ?? throw new InvalidDataException(BaseSubjectErrorCodes.LifecycleProviderContractInvalid);
                BaseProtectedSubjectScope next = owner._subjectScopes.Protect(logical, replacement);
                byte[] canonical = RotationCanonicalBytes(current.DomainOrdinal, rowId, prior, next);
                rolling = SHA256.HashData([.. rolling, .. canonical]);
                checked { examined++; changed++; canonicalBytes += canonical.LongLength; }
                lastRowId = rowId;
                await using SqliteCommand stage = connection.CreateCommand();
                stage.Transaction = transaction;
                stage.CommandTimeout = owner.TimeoutSeconds();
                stage.CommandText = $"INSERT INTO {owner._names.SubjectLifecycleScopeStage}(domain_ordinal,source_rowid,prior_digest,prior_value,replacement_digest,replacement_value) VALUES($domain,$rowid,$priorDigest,$priorValue,$replacementDigest,$replacementValue);";
                stage.Parameters.AddWithValue("$domain", current.DomainOrdinal);
                stage.Parameters.AddWithValue("$rowid", rowId);
                stage.Parameters.Add("$priorDigest", SqliteType.Blob).Value = prior.IndexDigest;
                stage.Parameters.Add("$priorValue", SqliteType.Blob).Value = prior.ProtectedCanonicalValue;
                stage.Parameters.Add("$replacementDigest", SqliteType.Blob).Value = next.IndexDigest;
                stage.Parameters.Add("$replacementValue", SqliteType.Blob).Value = next.ProtectedCanonicalValue;
                await stage.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            }

            int nextDomain = rows.Count == 0 ? checked(current.DomainOrdinal + 1) : current.DomainOrdinal;
            long nextLast = rows.Count == 0 ? 0 : lastRowId;
            await using SqliteCommand update = connection.CreateCommand();
            update.Transaction = transaction;
            update.CommandTimeout = owner.TimeoutSeconds();
            update.CommandText = $"UPDATE {owner._names.SubjectLifecycleMaintenance} SET domain_ordinal=$domain,last_rowid=$last,examined_count=$examined,changed_count=$changed,canonical_bytes=$bytes,rolling_checksum=$checksum WHERE singleton=1 AND domain_ordinal=$expectedDomain AND last_rowid=$expectedLast;";
            update.Parameters.AddWithValue("$domain", nextDomain);
            update.Parameters.AddWithValue("$last", nextLast);
            update.Parameters.AddWithValue("$examined", examined);
            update.Parameters.AddWithValue("$changed", changed);
            update.Parameters.AddWithValue("$bytes", canonicalBytes);
            update.Parameters.AddWithValue("$checksum", Convert.ToHexStringLower(rolling));
            update.Parameters.AddWithValue("$expectedDomain", current.DomainOrdinal);
            update.Parameters.AddWithValue("$expectedLast", current.LastRowId);
            if (await update.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false) != 1)
                throw new InvalidDataException(BaseSubjectErrorCodes.LifecycleProviderContractInvalid);
            await transaction.CommitAsync(CancellationToken.None).ConfigureAwait(false);
            return rows.Count != 0;
        }

        private async ValueTask<RotationEvidence> ValidateAndPublishRotationAsync(
            SqliteConnection connection,
            SqliteTransaction transaction,
            byte replacement,
            int pageSize,
            CancellationToken cancellationToken)
        {
            long examined = 0;
            long changed = 0;
            long canonicalBytes = 0;
            byte[] rolling = Convert.FromHexString(EmptyRotationChecksum);
            for (int domain = 0; domain < RotationDomains.Length; domain++)
            {
                string table = RotationTable(domain);
                long afterRowId = 0;
                long stagedCount = 0;
                while (true)
                {
                    await using SqliteCommand staged = connection.CreateCommand();
                    staged.Transaction = transaction;
                    staged.CommandTimeout = owner.TimeoutSeconds();
                    staged.CommandText = $"SELECT source_rowid,prior_digest,prior_value,replacement_digest,replacement_value FROM {owner._names.SubjectLifecycleScopeStage} WHERE domain_ordinal=$domain AND source_rowid>$after ORDER BY source_rowid LIMIT $take;";
                    staged.Parameters.AddWithValue("$domain", domain);
                    staged.Parameters.AddWithValue("$after", afterRowId);
                    staged.Parameters.AddWithValue("$take", pageSize);
                    var rows = new List<(long RowId, byte[] PriorDigest, byte[] PriorValue, byte[] NextDigest, byte[] NextValue)>(pageSize);
                    await using (SqliteDataReader reader = await staged.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false))
                        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
                            rows.Add((reader.GetInt64(0), (byte[])reader.GetValue(1), (byte[])reader.GetValue(2), (byte[])reader.GetValue(3), (byte[])reader.GetValue(4)));
                    if (rows.Count == 0) break;

                    foreach (var row in rows)
                    {
                        BaseProtectedSubjectScope prior;
                        await using (SqliteCommand source = connection.CreateCommand())
                        {
                            source.Transaction = transaction;
                            source.CommandTimeout = owner.TimeoutSeconds();
                            source.CommandText = $"SELECT scope_kind,scope_index_digest,protected_scope_value FROM {table} WHERE rowid=$rowid;";
                            source.Parameters.AddWithValue("$rowid", row.RowId);
                            await using SqliteDataReader reader = await source.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
                            if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
                                throw new InvalidDataException(BaseSubjectErrorCodes.LifecycleProviderContractInvalid);
                            prior = new BaseProtectedSubjectScope { Kind = (BaseSubjectScopeKind)reader.GetInt32(0), IndexDigest = (byte[])reader.GetValue(1), ProtectedCanonicalValue = (byte[])reader.GetValue(2) };
                        }
                        if (!CryptographicOperations.FixedTimeEquals(prior.IndexDigest, row.PriorDigest)
                            || !CryptographicOperations.FixedTimeEquals(prior.ProtectedCanonicalValue, row.PriorValue))
                            throw new InvalidDataException(BaseSubjectErrorCodes.LifecycleProviderContractInvalid);
                        BaseOwnedSubjectScopeEvidence logical = owner._subjectScopes!.Unprotect(prior)
                            ?? throw new InvalidDataException(BaseSubjectErrorCodes.LifecycleProviderContractInvalid);
                        BaseProtectedSubjectScope expectedDigest = owner._subjectScopes.Protect(logical, replacement);
                        var stagedReplacement = new BaseProtectedSubjectScope
                        {
                            Kind = prior.Kind,
                            IndexDigest = row.NextDigest,
                            ProtectedCanonicalValue = row.NextValue,
                        };
                        BaseOwnedSubjectScopeEvidence replacementLogical = owner._subjectScopes.Unprotect(stagedReplacement)
                            ?? throw new InvalidDataException(BaseSubjectErrorCodes.LifecycleProviderContractInvalid);
                        if (!CryptographicOperations.FixedTimeEquals(expectedDigest.IndexDigest, row.NextDigest)
                            || replacementLogical.Kind != logical.Kind
                            || !string.Equals(replacementLogical.Value, logical.Value, StringComparison.Ordinal))
                            throw new InvalidDataException(BaseSubjectErrorCodes.LifecycleProviderContractInvalid);
                        byte[] canonical = RotationCanonicalBytes(domain, row.RowId, prior, stagedReplacement);
                        rolling = SHA256.HashData([.. rolling, .. canonical]);
                        checked { examined++; changed++; canonicalBytes += canonical.LongLength; stagedCount++; }
                        await using SqliteCommand update = connection.CreateCommand();
                        update.Transaction = transaction;
                        update.CommandTimeout = owner.TimeoutSeconds();
                        update.CommandText = $"UPDATE {table} SET scope_index_digest=$digest,protected_scope_value=$value WHERE rowid=$rowid AND scope_index_digest=$priorDigest AND protected_scope_value=$priorValue;";
                        update.Parameters.Add("$digest", SqliteType.Blob).Value = row.NextDigest;
                        update.Parameters.Add("$value", SqliteType.Blob).Value = row.NextValue;
                        update.Parameters.AddWithValue("$rowid", row.RowId);
                        update.Parameters.Add("$priorDigest", SqliteType.Blob).Value = row.PriorDigest;
                        update.Parameters.Add("$priorValue", SqliteType.Blob).Value = row.PriorValue;
                        if (await update.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false) != 1)
                            throw new InvalidDataException(BaseSubjectErrorCodes.LifecycleProviderContractInvalid);
                        afterRowId = row.RowId;
                    }
                    if (rows.Count < pageSize) break;
                }

                await using SqliteCommand sourceCount = connection.CreateCommand();
                sourceCount.Transaction = transaction;
                sourceCount.CommandTimeout = owner.TimeoutSeconds();
                sourceCount.CommandText = $"SELECT COUNT(*) FROM {table};";
                long count = Convert.ToInt64(await sourceCount.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false), System.Globalization.CultureInfo.InvariantCulture);
                if (count != stagedCount)
                    throw new InvalidDataException(BaseSubjectErrorCodes.LifecycleProviderContractInvalid);
            }
            return new RotationEvidence(examined, changed, canonicalBytes, Convert.ToHexStringLower(rolling));
        }

        private async ValueTask<RotationProgress?> ReadRotationProgressAsync(
            SqliteConnection connection,
            SqliteTransaction? transaction,
            CancellationToken cancellationToken)
        {
            await using SqliteCommand command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandTimeout = owner.TimeoutSeconds();
            command.CommandText = $"SELECT kind,request_scope,request_operation,request_key,fingerprint,plan_checksum,expected_store_generation,expected_restore_epoch,expected_delivery_epoch,expected_scope_generation,old_key_id,replacement_key_id,domain_ordinal,last_rowid,examined_count,changed_count,canonical_bytes,rolling_checksum FROM {owner._names.SubjectLifecycleMaintenance} WHERE singleton=1;";
            await using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
                return null;
            return new RotationProgress((BaseSubjectLifecycleMaintenanceKind)reader.GetInt32(0), reader.GetString(1), reader.GetString(2), reader.GetString(3), (byte[])reader.GetValue(4), (byte[])reader.GetValue(5), reader.GetInt64(6), reader.GetInt64(7), reader.GetInt64(8), reader.GetInt64(9), reader.GetString(10), reader.GetString(11), reader.GetInt32(12), reader.GetInt64(13), reader.GetInt64(14), reader.GetInt64(15), reader.GetInt64(16), reader.GetString(17));
        }

        private static void ValidateRotationProgress(RotationProgress progress, BaseSubjectLifecycleMaintenanceExecutionRequest request)
        {
            if (progress.Kind != request.Kind
                || !string.Equals(progress.Scope, request.Identity.Scope, StringComparison.Ordinal)
                || !string.Equals(progress.Operation, request.Identity.Operation, StringComparison.Ordinal)
                || !string.Equals(progress.RequestKey, request.Identity.IdempotencyKey, StringComparison.Ordinal)
                || !CryptographicOperations.FixedTimeEquals(progress.Fingerprint, request.Identity.Fingerprint.ToArray())
                || !CryptographicOperations.FixedTimeEquals(progress.PlanChecksum, request.PlanChecksum)
                || progress.ExpectedStoreGeneration != request.ExpectedStoreGeneration
                || progress.ExpectedRestoreEpoch != request.ExpectedRestoreEpoch
                || progress.ExpectedDeliveryEpoch != request.ExpectedDeliveryEpoch
                || progress.ExpectedScopeGeneration != request.ExpectedScopeProtectionGeneration
                || !string.Equals(progress.OldKeyId, request.ExpectedScopeProtectionKeyId, StringComparison.Ordinal)
                || !string.Equals(progress.ReplacementKeyId, request.ReplacementScopeProtectionKeyId, StringComparison.Ordinal)
                || progress.DomainOrdinal < 0 || progress.DomainOrdinal > RotationDomains.Length
                || progress.LastRowId < 0 || progress.ExaminedCount < 0 || progress.ChangedCount < 0 || progress.CanonicalBytes < 0
                || progress.RollingChecksum.Length != 64)
                throw new InvalidDataException(BaseSubjectErrorCodes.LifecycleProviderContractInvalid);
        }

        private static byte[] RotationCanonicalBytes(int domain, long rowId, BaseProtectedSubjectScope prior, BaseProtectedSubjectScope next)
        {
            using var stream = new MemoryStream();
            using var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true);
            writer.Write(domain);
            writer.Write(rowId);
            writer.Write((int)prior.Kind);
            writer.Write(prior.IndexDigest.Length); writer.Write(prior.IndexDigest);
            writer.Write(prior.ProtectedCanonicalValue.Length); writer.Write(prior.ProtectedCanonicalValue);
            writer.Write(next.IndexDigest.Length); writer.Write(next.IndexDigest);
            writer.Write(next.ProtectedCanonicalValue.Length); writer.Write(next.ProtectedCanonicalValue);
            writer.Flush();
            return stream.ToArray();
        }

        private static readonly string EmptyRotationChecksum = Convert.ToHexStringLower(SHA256.HashData([]));

        private sealed record RotationProgress(
            BaseSubjectLifecycleMaintenanceKind Kind,
            string Scope,
            string Operation,
            string RequestKey,
            byte[] Fingerprint,
            byte[] PlanChecksum,
            long ExpectedStoreGeneration,
            long ExpectedRestoreEpoch,
            long ExpectedDeliveryEpoch,
            long ExpectedScopeGeneration,
            string OldKeyId,
            string ReplacementKeyId,
            int DomainOrdinal,
            long LastRowId,
            long ExaminedCount,
            long ChangedCount,
            long CanonicalBytes,
            string RollingChecksum);

        private sealed record RotationEvidence(long Examined, long Changed, long CanonicalBytes, string RollingChecksum);

        private async ValueTask<OperationResult<BaseSubjectLifecycleMaintenanceResult>?> ReadMaintenanceReceiptAsync(
            SqliteConnection connection,
            SqliteTransaction transaction,
            BaseSubjectLifecycleMaintenanceExecutionRequest request,
            CancellationToken cancellationToken)
        {
            await using SqliteCommand command = connection.CreateCommand(); command.Transaction = transaction; command.CommandTimeout = owner.TimeoutSeconds();
            command.CommandText = $"SELECT fingerprint,structural_digest,result_json,expires_at FROM {owner._names.OperationReceipts} WHERE scope=$scope AND operation=$operation AND idempotency_key=$key;";
            command.Parameters.AddWithValue("$scope", request.Identity.Scope); command.Parameters.AddWithValue("$operation", request.Identity.Operation); command.Parameters.AddWithValue("$key", request.Identity.IdempotencyKey);
            await using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false)) return null;
            byte[] fingerprint = (byte[])reader.GetValue(0); byte[] structural = (byte[])reader.GetValue(1); byte[] bytes = (byte[])reader.GetValue(2);
            DateTimeOffset expires = DateTimeOffset.Parse(reader.GetString(3), System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.RoundtripKind);
            if (expires <= owner._timeProvider.GetUtcNow()) return null;
            BaseAtomicReceiptWire? wire = System.Text.Json.JsonSerializer.Deserialize(bytes, HPDBaseJsonSerializerContext.Default.BaseAtomicReceiptWire);
            BaseAtomicReceiptResult? receipt = wire?.Materialize();
            if (!CryptographicOperations.FixedTimeEquals(fingerprint, request.Identity.Fingerprint.ToArray())
                || !CryptographicOperations.FixedTimeEquals(structural, request.PlanChecksum)
                || receipt?.Kind != BaseAtomicReceiptResultKind.SubjectLifecycleMaintenance || receipt.SubjectLifecycleMaintenance is null)
                return Failure(BaseMutationRequestErrorCodes.FingerprintConflict, OperationStatus.Conflict, ErrorCategory.Conflict);
            return OperationResults.Ok(receipt.SubjectLifecycleMaintenance with
            {
                RollingChecksum = new string(receipt.SubjectLifecycleMaintenance.RollingChecksum.AsSpan()),
                Duplicate = true,
            });
        }

        private async ValueTask InsertMaintenanceReceiptAsync(
            SqliteConnection connection,
            SqliteTransaction transaction,
            BaseSubjectLifecycleMaintenanceExecutionRequest request,
            BaseSubjectLifecycleMaintenanceResult result,
            CancellationToken cancellationToken)
        {
            var receipt = new BaseAtomicReceiptResult
            {
                Kind = BaseAtomicReceiptResultKind.SubjectLifecycleMaintenance,
                Mutations = [],
                SubjectLifecycleMaintenance = result with { RollingChecksum = new string(result.RollingChecksum.AsSpan()), Duplicate = false },
            };
            byte[] bytes = System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(BaseAtomicReceiptWire.From(receipt), HPDBaseJsonSerializerContext.Default.BaseAtomicReceiptWire);
            if (bytes.Length > 16_384) throw new InvalidOperationException(BaseSubjectErrorCodes.LifecycleProviderContractInvalid);
            await using SqliteCommand command = connection.CreateCommand(); command.Transaction = transaction; command.CommandTimeout = owner.TimeoutSeconds();
            command.CommandText = $"INSERT INTO {owner._names.OperationReceipts}(scope,operation,idempotency_key,fingerprint,structural_digest,result_json,result_format_version,schema_generation,store_instance_id,committed_at,expires_at) VALUES($scope,$operation,$key,$fingerprint,$structural,$result,2,$generation,$store,$committed,$expires);";
            command.Parameters.AddWithValue("$scope", request.Identity.Scope); command.Parameters.AddWithValue("$operation", request.Identity.Operation); command.Parameters.AddWithValue("$key", request.Identity.IdempotencyKey); command.Parameters.Add("$fingerprint", SqliteType.Blob).Value=request.Identity.Fingerprint.ToArray(); command.Parameters.Add("$structural", SqliteType.Blob).Value=request.PlanChecksum; command.Parameters.Add("$result", SqliteType.Blob).Value=bytes; command.Parameters.AddWithValue("$generation",owner._schemaGeneration); command.Parameters.AddWithValue("$store",owner._options.StoreId); command.Parameters.AddWithValue("$committed",owner._timeProvider.GetUtcNow().ToString("O",System.Globalization.CultureInfo.InvariantCulture)); command.Parameters.AddWithValue("$expires",owner._timeProvider.GetUtcNow().AddDays(30).ToString("O",System.Globalization.CultureInfo.InvariantCulture));
            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        private async ValueTask<OperationResult<BaseSubjectLifecycleMaintenanceResult>> ExecuteCoreAsync(
            SqliteConnection connection,
            SqliteTransaction transaction,
            BaseSubjectLifecycleMaintenanceExecutionRequest request,
            CancellationToken cancellationToken)
        {
            (long restoreEpoch, long deliveryEpoch, long scopeGeneration, string scopeKeyId) = await ReadAuthorityAsync(connection, transaction, cancellationToken).ConfigureAwait(false);
            if (owner._schemaGeneration != request.ExpectedStoreGeneration || owner._schemaGeneration != request.ExpectedSchemaGeneration || restoreEpoch != request.ExpectedRestoreEpoch
                || deliveryEpoch != request.ExpectedDeliveryEpoch || request.ExpectedScopeProtectionGeneration != scopeGeneration
                || !string.Equals(scopeKeyId, request.ExpectedScopeProtectionKeyId, StringComparison.Ordinal)
                || !string.Equals(owner._subjectScopeProtectionKeyId, scopeKeyId, StringComparison.Ordinal))
                return Failure(BaseSubjectErrorCodes.ScopeProtectionRotationConflict, OperationStatus.Conflict, ErrorCategory.Conflict);
            if (request.ExpectedProjectionGeneration is long expectedProjection)
            {
                await using SqliteCommand projection = connection.CreateCommand();
                projection.Transaction = transaction;
                projection.CommandTimeout = owner.TimeoutSeconds();
                projection.CommandText = $"SELECT projection_generation FROM {owner._names.SubjectLifecycleConsumers} WHERE consumer_id=$consumer AND consumer_version=$version;";
                projection.Parameters.AddWithValue("$consumer", request.ConsumerId!);
                projection.Parameters.AddWithValue("$version", request.ConsumerVersion!.Value);
                object? actual = await projection.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
                if (actual is null || Convert.ToInt64(actual, System.Globalization.CultureInfo.InvariantCulture) != expectedProjection)
                    return Failure(BaseSubjectErrorCodes.LifecycleRegistrationConflict, OperationStatus.Conflict, ErrorCategory.Conflict);
            }

            var changed = new List<string>();
            long examined = 0;
            long? projectionGeneration = null;
            switch (request.Kind)
            {
                case BaseSubjectLifecycleMaintenanceKind.MarkCheckpointOvertaken:
                    projectionGeneration = await MarkOvertakenAsync(connection, transaction, request, changed, cancellationToken).ConfigureAwait(false);
                    examined = 1;
                    if (projectionGeneration is null)
                        return Failure(BaseSubjectErrorCodes.LifecycleRegistrationConflict, OperationStatus.Conflict, ErrorCategory.Conflict);
                    break;
                default:
                    return Failure(BaseSubjectErrorCodes.LifecycleProviderContractInvalid, OperationStatus.CapabilityUnavailable, ErrorCategory.Capability);
            }

            byte[] canonical = Encoding.UTF8.GetBytes(string.Join('\n', changed.Order(StringComparer.Ordinal)));
            return OperationResults.Ok(new BaseSubjectLifecycleMaintenanceResult
            {
                Kind = request.Kind,
                ExaminedCount = examined,
                ChangedCount = changed.Count,
                CanonicalBytes = canonical.LongLength,
                RollingChecksum = Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(canonical)),
                DeliveryEpoch = deliveryEpoch,
                ProjectionGeneration = projectionGeneration,
                Duplicate = false,
            });
        }

        private async ValueTask<(long RestoreEpoch, long DeliveryEpoch, long ScopeGeneration, string ScopeKeyId)> ReadAuthorityAsync(SqliteConnection connection, SqliteTransaction transaction, CancellationToken cancellationToken)
        {
            await using SqliteCommand command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandTimeout = owner.TimeoutSeconds();
            command.CommandText = $"SELECT COALESCE((SELECT MAX(restore_epoch) FROM {owner._names.SubjectContracts}),0), CAST((SELECT value FROM {owner._names.ProviderState} WHERE key='subject_lifecycle_delivery_epoch') AS INTEGER),CAST((SELECT value FROM {owner._names.ProviderState} WHERE key='subject_scope_protection_generation') AS INTEGER),(SELECT value FROM {owner._names.ProviderState} WHERE key='subject_scope_protection_key_id');";
            await using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false) || reader.IsDBNull(1) || reader.IsDBNull(2) || reader.IsDBNull(3))
                throw new SqliteException("Lifecycle authority is missing.", 1);
            return (reader.GetInt64(0), reader.GetInt64(1), reader.GetInt64(2), reader.GetString(3));
        }

        private async ValueTask<long?> MarkOvertakenAsync(SqliteConnection connection, SqliteTransaction transaction, BaseSubjectLifecycleMaintenanceExecutionRequest request, List<string> changed, CancellationToken cancellationToken)
        {
            if (owner._subjectScopes is null) return null;
            BaseProtectedSubjectScope scope = owner._subjectScopes.Protect(request.Scope!, owner._subjectScopeProtectionKey!.Value);
            BaseExportedSubjectDefinition? contract = owner._options.ExportedSubjects.SingleOrDefault(value => value.Id == request.ContractId && value.Version == request.ContractVersion);
            if (contract is null || request.RetainedFrom is null) return null;
            await using SqliteCommand read = connection.CreateCommand(); read.Transaction = transaction; read.CommandTimeout = owner.TimeoutSeconds();
            read.CommandText = $"""
SELECT p.projection_generation,p.consumer_checksum,p.contract_id,p.contract_version,
       p.cutoff_position,p.cutoff_subject_id,p.cutoff_authority_epoch,p.cutoff_incarnation,p.cutoff_sequence,
       p.installed_at,p.maximum_checkpoint_lag_ticks,
       c.through_position,c.through_subject_id,c.through_authority_epoch,c.through_incarnation,c.through_sequence,
       c.checkpoint_generation,c.advanced_at,c.state,c.protected_scope_value
FROM {owner._names.SubjectLifecycleConsumers} p
LEFT JOIN {owner._names.SubjectLifecycleCheckpoints} c
  ON c.consumer_id=p.consumer_id AND c.consumer_version=p.consumer_version
 AND c.scope_kind=$scopeKind AND c.scope_index_digest=$scopeDigest
WHERE p.consumer_id=$consumer AND p.consumer_version=$version AND p.state=0;
""";
            read.Parameters.AddWithValue("$consumer", request.ConsumerId!); read.Parameters.AddWithValue("$version", request.ConsumerVersion!.Value); AddScopeQuery(read, scope);
            long projectionGeneration; string checksum; string contractId; int contractVersion; BaseSubjectLifecycleOrderingBoundary? cutoff; DateTimeOffset installedAt; TimeSpan maximumLag;
            BaseSubjectLifecycleOrderingBoundary? through; long? checkpointGeneration; DateTimeOffset? advancedAt; int? state;
            await using (SqliteDataReader reader = await read.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false))
            {
                if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false)) return null;
                projectionGeneration=reader.GetInt64(0); checksum=reader.GetString(1); contractId=reader.GetString(2); contractVersion=reader.GetInt32(3);
                cutoff=reader.IsDBNull(5)?null:new(){CommitPosition=new(reader.GetInt64(4)),SubjectId=BaseSubjectId.Create(reader.GetString(5),contract.SubjectIdKind),AuthorityEpoch=new((byte[])reader.GetValue(6)),Incarnation=new((byte[])reader.GetValue(7)),SubjectSequence=reader.GetInt64(8)};
                installedAt=DateTimeOffset.Parse(reader.GetString(9),System.Globalization.CultureInfo.InvariantCulture,System.Globalization.DateTimeStyles.RoundtripKind);
                maximumLag=TimeSpan.FromTicks(reader.GetInt64(10));
                through=reader.IsDBNull(12)?null:new(){CommitPosition=new(reader.GetInt64(11)),SubjectId=BaseSubjectId.Create(reader.GetString(12),contract.SubjectIdKind),AuthorityEpoch=new((byte[])reader.GetValue(13)),Incarnation=new((byte[])reader.GetValue(14)),SubjectSequence=reader.GetInt64(15)};
                checkpointGeneration=reader.IsDBNull(16)?null:reader.GetInt64(16); advancedAt=reader.IsDBNull(17)?null:DateTimeOffset.Parse(reader.GetString(17),System.Globalization.CultureInfo.InvariantCulture,System.Globalization.DateTimeStyles.RoundtripKind); state=reader.IsDBNull(18)?null:reader.GetInt32(18);
                if (!reader.IsDBNull(19) && !owner._subjectScopes.Matches(new BaseProtectedSubjectScope{Kind=request.Scope!.Kind,IndexDigest=scope.IndexDigest,ProtectedCanonicalValue=(byte[])reader.GetValue(19)},request.Scope)) return null;
            }
            if (state is not null and not 0) return null;
            BaseSubjectLifecycleOrderingBoundary? effective = checkpointGeneration is null ? cutoff : through;
            if (effective is not null && CompareLifecycleBoundary(effective, request.RetainedFrom) >= 0) return null;
            await using (SqliteCommand retained = connection.CreateCommand())
            {
                retained.Transaction = transaction; retained.CommandTimeout = owner.TimeoutSeconds();
                retained.CommandText = $"SELECT EXISTS(SELECT 1 FROM {owner._names.SubjectLifecycleFacts} WHERE contract_id=$contract AND contract_version=$contractVersion AND scope_kind=$scopeKind AND scope_index_digest=$scopeDigest AND commit_position=$position AND subject_id=$subject AND authority_epoch=$epoch AND incarnation=$incarnation AND subject_sequence=$sequence);";
                retained.Parameters.AddWithValue("$contract", contractId); retained.Parameters.AddWithValue("$contractVersion", contractVersion); AddScopeQuery(retained, scope);
                retained.Parameters.AddWithValue("$position", request.RetainedFrom.CommitPosition.Value); retained.Parameters.AddWithValue("$subject", request.RetainedFrom.SubjectId.Value);
                retained.Parameters.Add("$epoch", SqliteType.Blob).Value = request.RetainedFrom.AuthorityEpoch.ToArray(); retained.Parameters.Add("$incarnation", SqliteType.Blob).Value = request.RetainedFrom.Incarnation.ToArray(); retained.Parameters.AddWithValue("$sequence", request.RetainedFrom.SubjectSequence);
                if (Convert.ToInt32(await retained.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false), System.Globalization.CultureInfo.InvariantCulture) == 0) return null;
            }
            DateTimeOffset authorityTime = advancedAt ?? installedAt;
            DateTimeOffset now = owner._timeProvider.GetUtcNow();
            if (now < authorityTime.Add(maximumLag)) return null;
            if (checkpointGeneration is null)
            {
                await using SqliteCommand insert = connection.CreateCommand(); insert.Transaction=transaction; insert.CommandTimeout=owner.TimeoutSeconds();
                insert.CommandText=$"INSERT INTO {owner._names.SubjectLifecycleCheckpoints}(consumer_id,consumer_version,consumer_checksum,contract_id,contract_version,projection_generation,scope_kind,scope_index_digest,protected_scope_value,through_position,through_subject_id,through_authority_epoch,through_incarnation,through_sequence,checkpoint_generation,advanced_at,overtaken_at,state) VALUES($consumer,$version,$checksum,$contract,$contractVersion,$projection,$scopeKind,$scopeDigest,$scopeValue,$position,$subject,$epoch,$incarnation,$sequence,1,$advanced,$overtaken,1);";
                insert.Parameters.AddWithValue("$consumer",request.ConsumerId!);insert.Parameters.AddWithValue("$version",request.ConsumerVersion.Value);insert.Parameters.AddWithValue("$checksum",checksum);insert.Parameters.AddWithValue("$contract",contractId);insert.Parameters.AddWithValue("$contractVersion",contractVersion);insert.Parameters.AddWithValue("$projection",projectionGeneration);insert.Parameters.AddWithValue("$scopeKind",(int)scope.Kind);insert.Parameters.Add("$scopeDigest",SqliteType.Blob).Value=scope.IndexDigest;insert.Parameters.Add("$scopeValue",SqliteType.Blob).Value=scope.ProtectedCanonicalValue;
                insert.Parameters.AddWithValue("$position",cutoff is null?DBNull.Value:cutoff.CommitPosition.Value);insert.Parameters.AddWithValue("$subject",cutoff is null?DBNull.Value:cutoff.SubjectId.Value);insert.Parameters.Add("$epoch",SqliteType.Blob).Value=cutoff is null?DBNull.Value:cutoff.AuthorityEpoch.ToArray();insert.Parameters.Add("$incarnation",SqliteType.Blob).Value=cutoff is null?DBNull.Value:cutoff.Incarnation.ToArray();insert.Parameters.AddWithValue("$sequence",cutoff is null?DBNull.Value:cutoff.SubjectSequence);insert.Parameters.AddWithValue("$advanced",installedAt.ToString("O",System.Globalization.CultureInfo.InvariantCulture));insert.Parameters.AddWithValue("$overtaken",now.ToString("O",System.Globalization.CultureInfo.InvariantCulture));
                if(await insert.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false)!=1)return null;
            }
            else
            {
                await using SqliteCommand update=connection.CreateCommand();update.Transaction=transaction;update.CommandTimeout=owner.TimeoutSeconds();update.CommandText=$"UPDATE {owner._names.SubjectLifecycleCheckpoints} SET state=1,overtaken_at=$at,checkpoint_generation=checkpoint_generation+1 WHERE consumer_id=$consumer AND consumer_version=$version AND scope_kind=$scopeKind AND scope_index_digest=$scopeDigest AND checkpoint_generation=$generation AND state=0;";update.Parameters.AddWithValue("$at",now.ToString("O",System.Globalization.CultureInfo.InvariantCulture));update.Parameters.AddWithValue("$consumer",request.ConsumerId!);update.Parameters.AddWithValue("$version",request.ConsumerVersion.Value);update.Parameters.AddWithValue("$generation",checkpointGeneration.Value);AddScopeQuery(update,scope);if(await update.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false)!=1)return null;
            }
            changed.Add($"checkpoint\0{request.ConsumerId}\0{request.ConsumerVersion}\0{request.RetainedFrom.CommitPosition.Value}"); return projectionGeneration;
        }

        private async ValueTask<long> ReadMaximumProjectionGenerationAsync(SqliteConnection connection, SqliteTransaction transaction, CancellationToken cancellationToken)
        {
            await using SqliteCommand command = connection.CreateCommand(); command.Transaction = transaction; command.CommandTimeout = owner.TimeoutSeconds();
            command.CommandText = $"SELECT COALESCE(MAX(projection_generation),1) FROM {owner._names.SubjectLifecycleConsumers};";
            return Convert.ToInt64(await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false), System.Globalization.CultureInfo.InvariantCulture);
        }

        private static OperationResult<BaseSubjectLifecycleMaintenanceResult> Failure(string code, OperationStatus status, ErrorCategory category) => new() { Status=status, Error=new BaseError { Code=code, Category=category, Message="The subject lifecycle maintenance operation failed." } };
    }

    private static OperationResult<BaseSubjectLifecycleProviderPage> LifecycleReadFailure(string code, OperationStatus status, ErrorCategory category) => new() { Status = status, Error = new BaseError { Code = code, Category = category, Message = "The subject lifecycle provider operation failed." } };
    private static OperationResult<BaseSubjectLifecycleProviderReconciliationPage> LifecycleReconciliationFailure(string code, OperationStatus status, ErrorCategory category) => new() { Status = status, Error = new BaseError { Code = code, Category = category, Message = "The subject lifecycle reconciliation operation failed." } };
    private static OperationResult<BaseSubjectLifecycleProviderInspection> LifecycleInspectionFailure(string code, OperationStatus status, ErrorCategory category) => new() { Status = status, Error = new BaseError { Code = code, Category = category, Message = "The subject lifecycle inspection operation failed." } };
    private static void AddScopeQuery(SqliteCommand command, BaseProtectedSubjectScope scope)
    {
        command.Parameters.AddWithValue("$scopeKind", (int)scope.Kind);
        command.Parameters.Add("$scopeDigest", SqliteType.Blob).Value = scope.IndexDigest;
    }
    private async ValueTask<bool> LifecycleMaintenanceActiveAsync(SqliteConnection connection, CancellationToken cancellationToken)
    {
        await using SqliteCommand command = connection.CreateCommand(); command.CommandTimeout = TimeoutSeconds();
        command.CommandText = $"SELECT EXISTS(SELECT 1 FROM {_names.SubjectLifecycleMaintenance} WHERE singleton=1);";
        return Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false), System.Globalization.CultureInfo.InvariantCulture) != 0;
    }
    private static void AddScopeWrite(SqliteCommand command, BaseProtectedSubjectScope scope)
    {
        AddScopeQuery(command, scope);
        command.Parameters.Add("$scopeCiphertext", SqliteType.Blob).Value = scope.ProtectedCanonicalValue;
    }
    private static int CompareLifecycleBoundary(BaseSubjectLifecycleOrderingBoundary a, BaseSubjectLifecycleOrderingBoundary b) { int c = a.CommitPosition.Value.CompareTo(b.CommitPosition.Value); if (c != 0) return c; c = string.CompareOrdinal(a.SubjectId.Value, b.SubjectId.Value); if (c != 0) return c; c = a.AuthorityEpoch.ToArray().AsSpan().SequenceCompareTo(b.AuthorityEpoch.ToArray()); if (c != 0) return c; c = a.Incarnation.ToArray().AsSpan().SequenceCompareTo(b.Incarnation.ToArray()); return c != 0 ? c : a.SubjectSequence.CompareTo(b.SubjectSequence); }
}
