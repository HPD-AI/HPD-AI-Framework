using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Globalization;
using HPD.Base;
using Microsoft.Data.Sqlite;

namespace HPD.Base.Sqlite;

public sealed partial class SqliteRecordStore
{
    /// <inheritdoc />
    public ValueTask<RecordMutationExecutionResult> ExecuteAsync(IAtomicMutationProcessor processor, RecordMutationExecutionRequest request, CancellationToken cancellationToken = default) =>
        ExecuteAtomicAsync(processor, request, cancellationToken);

    /// <inheritdoc />
    public async ValueTask<OperationResult<BaseSubjectRetirementPublicationPage>> ReadPublicationsAsync(BaseSubjectRetirementPublicationReadRequest request,CancellationToken cancellationToken=default)
    {
        ArgumentNullException.ThrowIfNull(request);if(request.Take is<1 or>256)return RetirementReadFailure<BaseSubjectRetirementPublicationPage>(OperationStatus.ValidationFailed,BaseSubjectRetirementErrorCodes.ContractInvalid,ErrorCategory.Validation);
        await using SqliteConnection connection=await _connections.OpenAsync(cancellationToken).ConfigureAwait(false);long high;await using(SqliteCommand highCommand=connection.CreateCommand()){highCommand.CommandTimeout=TimeoutSeconds();highCommand.CommandText=$"SELECT CAST(value AS INTEGER) FROM {_names.ProviderState} WHERE key='subject_retirement_position';";high=Convert.ToInt64(await highCommand.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false),CultureInfo.InvariantCulture);}
        var rows=ImmutableArray.CreateBuilder<BaseSubjectRetirementPublicationRow>();await using SqliteCommand command=connection.CreateCommand();command.CommandTimeout=TimeoutSeconds();command.CommandText=$"SELECT scope_kind,scope_index_digest,protected_scope_value,payload FROM {_names.SubjectRetirementPublications} WHERE position>$after AND position<=$high ORDER BY position LIMIT $take;";command.Parameters.AddWithValue("$after",request.After?.Value??0);command.Parameters.AddWithValue("$high",high);command.Parameters.AddWithValue("$take",request.Take);await using SqliteDataReader reader=await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);while(await reader.ReadAsync(cancellationToken).ConfigureAwait(false)){BaseSubjectRetirementPublicationFact fact=JsonSerializer.Deserialize((byte[])reader.GetValue(3),HPDBaseJsonSerializerContext.Default.BaseSubjectRetirementPublicationFact)??throw new InvalidDataException(BaseSubjectRetirementErrorCodes.ProviderContractInvalid);BaseProtectedSubjectScope? scope=reader.IsDBNull(0)?null:new(){Kind=(BaseSubjectScopeKind)reader.GetInt32(0),IndexDigest=((byte[])reader.GetValue(1)).ToArray(),ProtectedCanonicalValue=((byte[])reader.GetValue(2)).ToArray()};var row=new BaseSubjectRetirementPublicationRow{Scope=scope,Fact=fact};BaseSubjectRetirementRegistry.ValidatePublication(row);rows.Add(row);}return OperationResults.Ok(new BaseSubjectRetirementPublicationPage{Rows=rows.ToImmutable(),HighWater=high==0?default:new(high)});
    }

    /// <inheritdoc />
    public async ValueTask<OperationResult<BaseSubjectRetirementBarrierPage>> ReadBarriersAsync(BaseSubjectRetirementBarrierReadRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.DeadlineUtc <= _timeProvider.GetUtcNow() || request.Take is < 1 or > 256 || request.MaximumResultBytes is < 1 or > 1_048_576)
            return RetirementReadFailure<BaseSubjectRetirementBarrierPage>(OperationStatus.ValidationFailed, BaseSubjectRetirementErrorCodes.ContractInvalid, ErrorCategory.Validation);
        if (_subjectScopes is null || _subjectScopeProtectionKey is null) return RetirementReadFailure<BaseSubjectRetirementBarrierPage>(OperationStatus.CapabilityUnavailable, BaseSubjectRetirementErrorCodes.ProviderContractInvalid, ErrorCategory.Capability);
        BaseExportedSubjectDefinition? subjectContract = _options.ExportedSubjects.SingleOrDefault(value => value.Id == request.ContractId && value.Version == request.ContractVersion);
        bool exactAuthority=request.ScopeAuthority.Mode==BaseSubjectScopeQueryMode.ExactScope&&subjectContract is not null&&string.Equals(request.ScopeAuthority.InstalledAuthorityDigest,BaseSubjectContractGraph.Checksum(subjectContract),StringComparison.Ordinal);bool allAuthority=request.ScopeAuthority.Mode==BaseSubjectScopeQueryMode.AllAuthorizedScopes&&request.ScopeAuthority.ExactScope is null&&_options.SubjectLifecycleInspectionAuthorities.Any(value=>value.ContractId==request.ContractId&&value.ContractVersion==request.ContractVersion&&value.Digest==request.ScopeAuthority.InstalledAuthorityDigest);if(subjectContract is null||!exactAuthority&&!allAuthority)return RetirementReadFailure<BaseSubjectRetirementBarrierPage>(OperationStatus.CapabilityUnavailable, BaseSubjectRetirementErrorCodes.ProviderContractInvalid, ErrorCategory.Capability);
        await using SqliteConnection connection = await _connections.OpenAsync(cancellationToken).ConfigureAwait(false);
        BaseProtectedSubjectScope? exact = request.ScopeAuthority.Mode == BaseSubjectScopeQueryMode.ExactScope && request.ScopeAuthority.ExactScope is { } canonical
            ? _subjectScopes.Protect(canonical, _subjectScopeProtectionKey.Value) : null;
        if (request.ScopeAuthority.Mode == BaseSubjectScopeQueryMode.ExactScope && exact is null)
            return RetirementReadFailure<BaseSubjectRetirementBarrierPage>(OperationStatus.ValidationFailed, BaseSubjectRetirementErrorCodes.ContractInvalid, ErrorCategory.Validation);
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandTimeout = TimeoutSeconds();
        command.CommandText = $"SELECT scope_kind,scope_index_digest,protected_scope_value,subject_id,authority_epoch,incarnation,tombstone_sequence,required_consumer_set_checksum,created_at,deadline_at,state,generation,barrier_checksum FROM {_names.SubjectRetirementBarriers} WHERE contract_id=$contract AND contract_version=$version"
            + (exact is null ? "" : " AND scope_kind=$scopeKind AND scope_index_digest=$scopeDigest")
            + (request.State is null ? "" : " AND state=$state")
            + (request.After is null ? "" : " AND (scope_kind>$afterScopeKind OR (scope_kind=$afterScopeKind AND (scope_index_digest>$afterScopeDigest OR (scope_index_digest=$afterScopeDigest AND (subject_id>$afterSubject OR (subject_id=$afterSubject AND (authority_epoch>$afterEpoch OR (authority_epoch=$afterEpoch AND incarnation>$afterIncarnation))))))))")
            + " ORDER BY scope_kind,scope_index_digest,subject_id,authority_epoch,incarnation LIMIT $limit;";
        command.Parameters.AddWithValue("$contract", request.ContractId); command.Parameters.AddWithValue("$version", request.ContractVersion);
        if (exact is not null) { command.Parameters.AddWithValue("$scopeKind", (int)exact.Kind); command.Parameters.Add("$scopeDigest", SqliteType.Blob).Value = exact.IndexDigest; }
        if (request.State is not null) command.Parameters.AddWithValue("$state", (int)request.State.Value);
        if(request.After is { } after){command.Parameters.AddWithValue("$afterScopeKind",(int)after.ScopeKind);command.Parameters.Add("$afterScopeDigest",SqliteType.Blob).Value=after.ScopeIndexDigest;command.Parameters.AddWithValue("$afterSubject",after.SubjectId.Value);command.Parameters.Add("$afterEpoch",SqliteType.Blob).Value=after.AuthorityEpoch.ToArray();command.Parameters.Add("$afterIncarnation",SqliteType.Blob).Value=after.Incarnation.ToArray();}
        command.Parameters.AddWithValue("$limit",checked(request.Take+1));
        await using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        var rows=ImmutableArray.CreateBuilder<BaseSubjectRetirementBarrierRow>();BaseSubjectRetirementBarrierKey? last=null;bool more=false;long resultBytes=0;
        while(await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            var key=new BaseSubjectRetirementBarrierKey{ScopeKind=(BaseSubjectScopeKind)reader.GetInt32(0),ScopeIndexDigest=((byte[])reader.GetValue(1)).ToArray(),ContractId=request.ContractId,ContractVersion=request.ContractVersion,SubjectId=BaseSubjectId.Create(reader.GetString(3),subjectContract.SubjectIdKind,subjectContract.MaximumSubjectIdUtf8Bytes),AuthorityEpoch=new((byte[])reader.GetValue(4)),Incarnation=new((byte[])reader.GetValue(5))};
            if(rows.Count==request.Take){more=true;break;}
            var barrier=new BaseSubjectRetirementBarrier{ContractId=request.ContractId,ContractVersion=request.ContractVersion,SubjectId=key.SubjectId,AuthorityEpoch=key.AuthorityEpoch,Incarnation=key.Incarnation,TombstoneSequence=reader.GetInt64(6),RequiredConsumerSetChecksum=reader.GetString(7),CreatedAtUtc=DateTimeOffset.Parse(reader.GetString(8),CultureInfo.InvariantCulture),DeadlineUtc=DateTimeOffset.Parse(reader.GetString(9),CultureInfo.InvariantCulture),State=(BaseSubjectRetirementBarrierState)reader.GetInt32(10),Generation=reader.GetInt64(11),BarrierChecksum=reader.GetString(12)};
            resultBytes=checked(resultBytes+RetirementBarrierBytes(barrier));if(resultBytes>request.MaximumResultBytes)return RetirementReadFailure<BaseSubjectRetirementBarrierPage>(OperationStatus.ValidationFailed,BaseSubjectErrorCodes.BudgetExceeded,ErrorCategory.Validation);
            ImmutableArray<string> inputs=await ReadRetirementAcknowledgementInputsAsync(key,cancellationToken).ConfigureAwait(false);rows.Add(new(){Scope=new(){Kind=key.ScopeKind,IndexDigest=key.ScopeIndexDigest.ToArray(),ProtectedCanonicalValue=((byte[])reader.GetValue(2)).ToArray()},Barrier=barrier,AcknowledgementChecksumInputs=inputs});last=key;
        }
        long generation;await using(SqliteCommand generationCommand=connection.CreateCommand()){generationCommand.CommandTimeout=TimeoutSeconds();generationCommand.CommandText=$"SELECT CAST(value AS INTEGER) FROM {_names.ProviderState} WHERE key='subject_retirement_position';";generation=Convert.ToInt64(await generationCommand.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false),CultureInfo.InvariantCulture);}
        ImmutableArray<BaseReadIntervalEvidence> intervals=BaseSubjectRetirementReadIntervals.Create(request.ContractId,request.ContractVersion,request.State,exact,request.After,last??request.After);BaseReadIntervalEvidence interval=intervals[0];byte[] lower=interval.LowerInclusive;byte[] upper=interval.UpperInclusive;int acknowledgementRows=rows.Sum(static row=>row.AcknowledgementChecksumInputs.Length);long acknowledgementBytes=rows.Sum(static row=>row.AcknowledgementChecksumInputs.Sum(static value=>(long)Encoding.UTF8.GetByteCount(value)));long evidenceBytes=checked(lower.LongLength+upper.LongLength+acknowledgementBytes);
        return OperationResults.Ok(new BaseSubjectRetirementBarrierPage{Barriers=rows.ToImmutable(),Next=more?last:null,CapturedBarrierGeneration=generation,Intervals=intervals,Accounting=new(){BarrierRows=rows.Count,AcknowledgementRows=acknowledgementRows,ResultBytes=resultBytes,EvidenceBytes=evidenceBytes,TransientBytes=checked(resultBytes+evidenceBytes)}});
    }

    /// <inheritdoc />
    async ValueTask<OperationResult<BaseSubjectRetirementInspection>> IBaseSubjectRetirementStore.InspectAsync(BaseSubjectRetirementInspectionRequest request,CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);if(_subjectScopes is null||_subjectScopeProtectionKey is null||request.ScopeAuthority.Mode!=BaseSubjectScopeQueryMode.ExactScope||request.ScopeAuthority.ExactScope is null||request.DeadlineUtc<=_timeProvider.GetUtcNow())return RetirementReadFailure<BaseSubjectRetirementInspection>(OperationStatus.ValidationFailed,BaseSubjectRetirementErrorCodes.ContractInvalid,ErrorCategory.Validation);
        BaseExportedSubjectDefinition? installedContract=_options.ExportedSubjects.SingleOrDefault(value=>value.Id==request.ContractId&&value.Version==request.ContractVersion);if(installedContract is null||!string.Equals(request.ScopeAuthority.InstalledAuthorityDigest,BaseSubjectContractGraph.Checksum(installedContract),StringComparison.Ordinal))return RetirementReadFailure<BaseSubjectRetirementInspection>(OperationStatus.CapabilityUnavailable,BaseSubjectRetirementErrorCodes.ProviderContractInvalid,ErrorCategory.Capability);
        BaseProtectedSubjectScope scope=_subjectScopes.Protect(request.ScopeAuthority.ExactScope,_subjectScopeProtectionKey.Value);await using SqliteConnection connection=await _connections.OpenAsync(cancellationToken).ConfigureAwait(false);
        BaseSubjectRetirementBarrier? barrier=null;await using(SqliteCommand command=connection.CreateCommand()){command.CommandTimeout=TimeoutSeconds();command.CommandText=$"SELECT tombstone_sequence,required_consumer_set_checksum,created_at,deadline_at,state,generation,barrier_checksum FROM {_names.SubjectRetirementBarriers} WHERE scope_kind=$scopeKind AND scope_index_digest=$scopeDigest AND contract_id=$contract AND contract_version=$version AND subject_id=$subject AND authority_epoch=$epoch AND incarnation=$incarnation;";AddRetirementInspectionParameters(command,scope,request);await using SqliteDataReader reader=await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);if(await reader.ReadAsync(cancellationToken).ConfigureAwait(false))barrier=new(){ContractId=request.ContractId,ContractVersion=request.ContractVersion,SubjectId=request.SubjectId,AuthorityEpoch=request.AuthorityEpoch,Incarnation=request.Incarnation,TombstoneSequence=reader.GetInt64(0),RequiredConsumerSetChecksum=reader.GetString(1),CreatedAtUtc=DateTimeOffset.Parse(reader.GetString(2),CultureInfo.InvariantCulture),DeadlineUtc=DateTimeOffset.Parse(reader.GetString(3),CultureInfo.InvariantCulture),State=(BaseSubjectRetirementBarrierState)reader.GetInt32(4),Generation=reader.GetInt64(5),BarrierChecksum=reader.GetString(6)};}
        BaseSubjectRetirementTerminalSummary? terminal=null;if(request.IncludeTerminalSummary){await using SqliteCommand command=connection.CreateCommand();command.CommandTimeout=TimeoutSeconds();command.CommandText=$"SELECT tombstone_sequence,retired_position,purged_at,receipt_checksum FROM {_names.SubjectRetirementTerminals} WHERE scope_kind=$scopeKind AND scope_index_digest=$scopeDigest AND contract_id=$contract AND contract_version=$version AND subject_id=$subject AND authority_epoch=$epoch AND incarnation=$incarnation;";AddRetirementInspectionParameters(command,scope,request);await using SqliteDataReader reader=await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);if(await reader.ReadAsync(cancellationToken).ConfigureAwait(false))terminal=new(){ContractId=request.ContractId,ContractVersion=request.ContractVersion,SubjectId=request.SubjectId,AuthorityEpoch=request.AuthorityEpoch,Incarnation=request.Incarnation,TombstoneSequence=reader.GetInt64(0),RetiredPosition=new(reader.GetInt64(1)),PurgedAtUtc=DateTimeOffset.Parse(reader.GetString(2),CultureInfo.InvariantCulture),TerminalReceiptChecksum=reader.GetString(3)};}
        if(barrier is not null&&terminal is not null)return RetirementReadFailure<BaseSubjectRetirementInspection>(OperationStatus.StoreError,BaseSubjectRetirementErrorCodes.ProviderContractInvalid,ErrorCategory.Store);long bytes=barrier is null?(terminal is null?0:256):RetirementBarrierBytes(barrier);if(bytes>request.MaximumResultBytes)return RetirementReadFailure<BaseSubjectRetirementInspection>(OperationStatus.ValidationFailed,BaseSubjectErrorCodes.BudgetExceeded,ErrorCategory.Validation);ImmutableArray<string> inputs=barrier is null?[]:await ReadRetirementAcknowledgementInputsAsync(new(){ScopeKind=scope.Kind,ScopeIndexDigest=scope.IndexDigest.ToArray(),ContractId=request.ContractId,ContractVersion=request.ContractVersion,SubjectId=request.SubjectId,AuthorityEpoch=request.AuthorityEpoch,Incarnation=request.Incarnation},cancellationToken).ConfigureAwait(false);long evidenceBytes=inputs.Sum(static value=>(long)Encoding.UTF8.GetByteCount(value));return OperationResults.Ok(new BaseSubjectRetirementInspection{Scope=new(){Kind=scope.Kind,IndexDigest=scope.IndexDigest.ToArray(),ProtectedCanonicalValue=scope.ProtectedCanonicalValue.ToArray()},CurrentBarrier=barrier,TerminalSummary=terminal,AcknowledgementChecksumInputs=inputs,Accounting=new(){BarrierRows=barrier is null?0:1,AcknowledgementRows=inputs.Length,ResultBytes=bytes,EvidenceBytes=evidenceBytes,TransientBytes=checked(bytes+evidenceBytes)}});
    }

    private static void AddRetirementInspectionParameters(SqliteCommand command,BaseProtectedSubjectScope scope,BaseSubjectRetirementInspectionRequest request){command.Parameters.AddWithValue("$scopeKind",(int)scope.Kind);command.Parameters.Add("$scopeDigest",SqliteType.Blob).Value=scope.IndexDigest;command.Parameters.AddWithValue("$contract",request.ContractId);command.Parameters.AddWithValue("$version",request.ContractVersion);command.Parameters.AddWithValue("$subject",request.SubjectId.Value);command.Parameters.Add("$epoch",SqliteType.Blob).Value=request.AuthorityEpoch.ToArray();command.Parameters.Add("$incarnation",SqliteType.Blob).Value=request.Incarnation.ToArray();}
    private async ValueTask<ImmutableArray<string>> ReadRetirementAcknowledgementInputsAsync(BaseSubjectRetirementBarrierKey key,CancellationToken cancellationToken){await using SqliteConnection connection=await _connections.OpenAsync(cancellationToken).ConfigureAwait(false);await using SqliteCommand command=connection.CreateCommand();command.CommandTimeout=TimeoutSeconds();command.CommandText=$"SELECT consumer_id,consumer_version,consumer_checksum,through_sequence,disposition,retirement_position FROM {_names.SubjectRetirementAcknowledgements} WHERE scope_kind=$scopeKind AND scope_index_digest=$scopeDigest AND contract_id=$contract AND contract_version=$version AND subject_id=$subject AND authority_epoch=$epoch AND incarnation=$incarnation ORDER BY consumer_id,consumer_version;";command.Parameters.AddWithValue("$scopeKind",(int)key.ScopeKind);command.Parameters.Add("$scopeDigest",SqliteType.Blob).Value=key.ScopeIndexDigest;command.Parameters.AddWithValue("$contract",key.ContractId);command.Parameters.AddWithValue("$version",key.ContractVersion);command.Parameters.AddWithValue("$subject",key.SubjectId.Value);command.Parameters.Add("$epoch",SqliteType.Blob).Value=key.AuthorityEpoch.ToArray();command.Parameters.Add("$incarnation",SqliteType.Blob).Value=key.Incarnation.ToArray();var values=ImmutableArray.CreateBuilder<string>();await using SqliteDataReader reader=await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);while(await reader.ReadAsync(cancellationToken).ConfigureAwait(false))values.Add(BaseSubjectRetirementRegistry.AcknowledgementChecksumInput(reader.GetString(0),reader.GetInt32(1),reader.GetString(2),reader.GetInt64(3),(BaseSubjectAcknowledgementDisposition)reader.GetInt32(4),reader.GetInt64(5)));return values.ToImmutable();}
    private static int CompareRetirementKey(BaseSubjectRetirementBarrierKey left,BaseSubjectRetirementBarrierKey right)=>RetirementKeyBytes(left).AsSpan().SequenceCompareTo(RetirementKeyBytes(right));
    private static byte[] RetirementKeyBytes(BaseSubjectRetirementBarrierKey key)=>Encoding.UTF8.GetBytes($"{(int)key.ScopeKind:D2}\0{Convert.ToHexString(key.ScopeIndexDigest)}\0{key.ContractId}\0{key.ContractVersion:D10}\0{key.SubjectId.Value}\0{key.AuthorityEpoch.ToBase64Url()}\0{key.Incarnation.ToBase64Url()}");
    private static long RetirementBarrierBytes(BaseSubjectRetirementBarrier barrier)=>Encoding.UTF8.GetByteCount($"{barrier.ContractId}\0{barrier.ContractVersion}\0{barrier.SubjectId.Value}\0{barrier.AuthorityEpoch.ToBase64Url()}\0{barrier.Incarnation.ToBase64Url()}\0{barrier.TombstoneSequence}\0{barrier.RequiredConsumerSetChecksum}\0{barrier.CreatedAtUtc.UtcTicks}\0{barrier.DeadlineUtc.UtcTicks}\0{(int)barrier.State}\0{barrier.Generation}\0{barrier.BarrierChecksum}");
    private static OperationResult<T> RetirementReadFailure<T>(OperationStatus status, string code, ErrorCategory category) => new()
    { Status=status, Error=new BaseError { Code=code, Message="The subject retirement barrier is unavailable.", Category=category } };

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
        return OperationResults.Ok(new BaseSubjectLifecycleProviderPage { StoreInstanceId = CurrentStoreInstanceId, RestoreEpoch = restoreEpoch, DeliveryEpoch = deliveryEpoch, CheckpointGeneration = durableGeneration, Scope = protectedScope, Facts = facts.ToImmutable(), EarliestRetained = earliestRetained, HighWater = highWater, Through = through, ProjectionGeneration = request.ProjectionGeneration, Intervals = intervals, Accounting = new BaseSubjectLifecycleReadAccounting { RowsSought = rowsSought, RowsHydrated = facts.Count, ResultBytes = resultBytes, TransientBytes = checked(resultBytes + BaseSubjectCanonicalRetainedWork.MeasureLifecycleIntervals(intervals)) } });
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
        long deliveryEpoch;long scopeProtectionGeneration;string scopeProtectionKeyId;long retirementControlGeneration;
        await using (SqliteCommand delivery = connection.CreateCommand())
        {
            delivery.CommandTimeout = TimeoutSeconds();
            delivery.CommandText = $"SELECT CAST((SELECT value FROM {_names.ProviderState} WHERE key='subject_lifecycle_delivery_epoch') AS INTEGER),COALESCE((SELECT MAX(restore_epoch) FROM {_names.SubjectContracts}),0),CAST((SELECT value FROM {_names.ProviderState} WHERE key='subject_scope_protection_generation') AS INTEGER),(SELECT value FROM {_names.ProviderState} WHERE key='subject_scope_protection_key_id'),CAST((SELECT value FROM {_names.ProviderState} WHERE key='subject_retirement_position') AS INTEGER);";
            await using SqliteDataReader authorityReader = await delivery.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            if (!await authorityReader.ReadAsync(cancellationToken).ConfigureAwait(false) || authorityReader.IsDBNull(0))
                return LifecycleInspectionFailure(BaseSubjectErrorCodes.LifecycleProviderContractInvalid, OperationStatus.CapabilityUnavailable, ErrorCategory.Capability);
            deliveryEpoch = authorityReader.GetInt64(0);
            restoreEpoch = authorityReader.GetInt64(1);
            scopeProtectionGeneration=authorityReader.GetInt64(2);scopeProtectionKeyId=authorityReader.GetString(3);retirementControlGeneration=authorityReader.GetInt64(4);
        }
        return OperationResults.Ok(new BaseSubjectLifecycleProviderInspection { StoreInstanceId = CurrentStoreInstanceId, RestoreEpoch = restoreEpoch, DeliveryEpoch = deliveryEpoch,ScopeProtectionGeneration=scopeProtectionGeneration,ScopeProtectionKeyId=scopeProtectionKeyId,RetirementControlGeneration=retirementControlGeneration, EarliestRetained = null, HighWater = null, Consumers = consumers.ToImmutable(), TerminalReceipt = terminalReceipt, Accounting = new() { RowsSought = consumers.Count, RowsHydrated = consumers.Count, ResultBytes = consumers.Count * 96L, TransientBytes = consumers.Count * 96L } });
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
    public ValueTask<RecordMutationExecutionResult> ExecuteMaintenanceAsync(IBaseSubjectAuthorityMaintenanceProcessor processor, BaseSubjectAuthorityMaintenanceExecutionRequest request, CancellationToken cancellationToken = default) =>
        processor.ExecuteAsync(new SqliteLifecycleMaintenanceSession(this, request), request, cancellationToken);

    private sealed class SqliteLifecycleMaintenanceSession(SqliteRecordStore owner, BaseSubjectAuthorityMaintenanceExecutionRequest authority) : IBaseSubjectAuthorityMaintenanceSession
    {
        private long _retirementExamined;
        private long _retirementChanged;
        private long _publishedBarrierControlGeneration;
        public async ValueTask<OperationResult<BaseSubjectAuthorityMaintenancePageResult>> ExecutePageAsync(
            BaseSubjectAuthorityMaintenancePageRequest page,
            CancellationToken cancellationToken = default)
        {
            SqliteLifecycleMaintenanceRequest request = SqliteLifecycleMaintenanceRequest.From(authority);
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            deadline.CancelAfter(request.OperationTimeout);
            try
            {
                await using IAsyncDisposable generationLease = await owner._schemaGenerationGate.AcquireExclusiveAsync(deadline.Token).ConfigureAwait(false);
                OperationResult<BaseSubjectAuthorityMaintenancePageResult>? stagedPage=await AdvanceOneStagedPageAsync(request,page,deadline.Token).ConfigureAwait(false);
                if(stagedPage is not null)return stagedPage;
                if (request.Kind == BaseSubjectLifecycleMaintenanceKind.RotateScopeProtection)
                    return Page(await ExecuteStagedRotationAsync(request, deadline.Token).ConfigureAwait(false), page.PageOrdinal, authority.Retirement);
                if (request.Kind == BaseSubjectLifecycleMaintenanceKind.RemoveConsumer)
                    return Page(await ExecuteStagedConsumerRemovalAsync(request, deadline.Token).ConfigureAwait(false), page.PageOrdinal, authority.Retirement);
                if (request.Kind == BaseSubjectLifecycleMaintenanceKind.RebuildDeliveryProjection)
                    return Page(await ExecuteStagedDeliveryRebuildAsync(request, deadline.Token).ConfigureAwait(false), page.PageOrdinal, authority.Retirement);
                if (request.Kind == BaseSubjectLifecycleMaintenanceKind.Prune)
                    return Page(await ExecuteStagedPruneAsync(request, deadline.Token).ConfigureAwait(false), page.PageOrdinal, authority.Retirement);
                await using SqliteConnection connection = await owner._connections.OpenAsync(deadline.Token).ConfigureAwait(false);
                if (await owner.LifecycleMaintenanceActiveAsync(connection, deadline.Token).ConfigureAwait(false))
                    return Page(Failure(BaseSubjectErrorCodes.MaintenanceRequired, OperationStatus.CapabilityUnavailable, ErrorCategory.Capability), page.PageOrdinal, authority.Retirement);
                await using SqliteTransaction transaction = (SqliteTransaction)await connection.BeginTransactionAsync(System.Data.IsolationLevel.Serializable, deadline.Token).ConfigureAwait(false);
                OperationResult<BaseSubjectLifecycleMaintenanceResult>? replay = await ReadMaintenanceReceiptAsync(connection, transaction, request, deadline.Token).ConfigureAwait(false);
                if (replay is not null)
                {
                    await transaction.RollbackAsync(CancellationToken.None).ConfigureAwait(false);
                    return Page(replay, page.PageOrdinal, authority.Retirement);
                }
                OperationResult<BaseSubjectLifecycleMaintenanceResult> result = await ExecuteCoreAsync(connection, transaction, request, deadline.Token).ConfigureAwait(false);
                if (!result.IsSuccess())
                {
                    await transaction.RollbackAsync(CancellationToken.None).ConfigureAwait(false);
                    return Page(result, page.PageOrdinal, authority.Retirement);
                }
                await InsertMaintenanceReceiptAsync(connection, transaction, request, result.Value!, deadline.Token).ConfigureAwait(false);
                await transaction.CommitAsync(CancellationToken.None).ConfigureAwait(false);
                if (request.Kind == BaseSubjectLifecycleMaintenanceKind.RotateScopeProtection
                    && byte.TryParse(request.ReplacementScopeProtectionKeyId, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out byte replacement))
                {
                    owner._subjectScopeProtectionKey = replacement;
                    owner._subjectScopeProtectionKeyId = replacement.ToString(System.Globalization.CultureInfo.InvariantCulture);
                }
                return Page(result, page.PageOrdinal, authority.Retirement);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                return Page(Failure(BaseSubjectErrorCodes.Timeout, OperationStatus.StoreError, ErrorCategory.Store), page.PageOrdinal, authority.Retirement);
            }
            catch (SqliteException)
            {
                return Page(Failure(BaseSubjectErrorCodes.LifecycleProviderContractInvalid, OperationStatus.StoreError, ErrorCategory.Store), page.PageOrdinal, authority.Retirement);
            }
            catch (InvalidDataException)
            {
                return Page(Failure(BaseSubjectErrorCodes.LifecycleProviderContractInvalid, OperationStatus.CapabilityUnavailable, ErrorCategory.Capability), page.PageOrdinal, authority.Retirement);
            }
        }

        private async ValueTask<OperationResult<BaseSubjectAuthorityMaintenancePageResult>?> AdvanceOneStagedPageAsync(SqliteLifecycleMaintenanceRequest request,BaseSubjectAuthorityMaintenancePageRequest page,CancellationToken token)
        {
            if(request.Kind is not(BaseSubjectLifecycleMaintenanceKind.RotateScopeProtection or BaseSubjectLifecycleMaintenanceKind.RemoveConsumer or BaseSubjectLifecycleMaintenanceKind.RebuildDeliveryProjection or BaseSubjectLifecycleMaintenanceKind.Prune))return null;
            if(request.Kind==BaseSubjectLifecycleMaintenanceKind.RotateScopeProtection&&owner._options.SubjectRetirementPolicies.Any()&&authority.Retirement is null)throw new InvalidDataException(BaseSubjectRetirementErrorCodes.ProviderContractInvalid);
            await using SqliteConnection connection=await owner._connections.OpenAsync(token).ConfigureAwait(false);
            await using(SqliteTransaction receiptTransaction=(SqliteTransaction)await connection.BeginTransactionAsync(System.Data.IsolationLevel.Serializable,token).ConfigureAwait(false))
            {
                OperationResult<BaseSubjectLifecycleMaintenanceResult>? replay=await ReadMaintenanceReceiptAsync(connection,receiptTransaction,request,token).ConfigureAwait(false);await receiptTransaction.RollbackAsync(CancellationToken.None).ConfigureAwait(false);if(replay is not null)return null;
            }
            switch(request.Kind)
            {
                case BaseSubjectLifecycleMaintenanceKind.Prune:await InitializePruneAsync(connection,request,token).ConfigureAwait(false);break;
                case BaseSubjectLifecycleMaintenanceKind.RemoveConsumer:if(await InitializeConsumerRemovalAsync(connection,request,token).ConfigureAwait(false) is not null)return null;break;
                case BaseSubjectLifecycleMaintenanceKind.RebuildDeliveryProjection:await InitializeDeliveryRebuildAsync(connection,request,token).ConfigureAwait(false);break;
                case BaseSubjectLifecycleMaintenanceKind.RotateScopeProtection:
                    if(owner._tokenProtector is null||!byte.TryParse(request.ReplacementScopeProtectionKeyId,NumberStyles.None,CultureInfo.InvariantCulture,out byte replacement)||replacement==owner._subjectScopeProtectionKey||!owner._tokenProtector.CanIssueWithKey(replacement))return null;
                    if(await InitializeRotationAsync(connection,request,token).ConfigureAwait(false) is not null)return null;break;
            }
            RotationProgress progress=await ReadRotationProgressAsync(connection,null,token).ConfigureAwait(false)??throw new InvalidDataException(BaseSubjectErrorCodes.LifecycleProviderContractInvalid);ValidateProgressForKind(progress,request);
            int terminalDomain=TerminalDomain(request.Kind);if(progress.DomainOrdinal==terminalDomain)return null;
            byte[] expectedContinuation=ProgressKey(progress);if(page.PageOrdinal!=1&&(page.LastCanonicalKey is null||!CryptographicOperations.FixedTimeEquals(page.LastCanonicalKey,expectedContinuation)))throw new InvalidDataException(BaseSubjectErrorCodes.LifecycleProviderContractInvalid);
            bool stagedRows;
            switch(request.Kind)
            {
                case BaseSubjectLifecycleMaintenanceKind.Prune:stagedRows=await ExecutePrunePageAsync(connection,request,progress,token).ConfigureAwait(false);break;
                case BaseSubjectLifecycleMaintenanceKind.RemoveConsumer:stagedRows=await StageConsumerRemovalPageAsync(connection,request,progress,token).ConfigureAwait(false);break;
                case BaseSubjectLifecycleMaintenanceKind.RebuildDeliveryProjection:stagedRows=await StageDeliveryRebuildPageAsync(connection,request,progress,token).ConfigureAwait(false);break;
                case BaseSubjectLifecycleMaintenanceKind.RotateScopeProtection:
                    byte replacement=byte.Parse(request.ReplacementScopeProtectionKeyId!,CultureInfo.InvariantCulture);stagedRows=await StageRotationPageAsync(connection,request,progress,replacement,token).ConfigureAwait(false);break;
                default:throw new InvalidDataException(BaseSubjectErrorCodes.LifecycleProviderContractInvalid);
            }
            if(stagedRows)await owner._administrationOperations.BeforePhaseAsync(request.Kind switch{BaseSubjectLifecycleMaintenanceKind.Prune=>"subjectLifecyclePruneAfterPage",BaseSubjectLifecycleMaintenanceKind.RemoveConsumer=>"subjectLifecycleConsumerRemovalAfterPage",BaseSubjectLifecycleMaintenanceKind.RebuildDeliveryProjection=>"subjectLifecycleDeliveryRebuildAfterPage",BaseSubjectLifecycleMaintenanceKind.RotateScopeProtection=>"subjectLifecycleRotationAfterPage",_=>throw new InvalidDataException(BaseSubjectErrorCodes.LifecycleProviderContractInvalid)},token).ConfigureAwait(false);
            RotationProgress advanced=await ReadRotationProgressAsync(connection,null,token).ConfigureAwait(false)??throw new InvalidDataException(BaseSubjectErrorCodes.LifecycleProviderContractInvalid);ValidateProgressForKind(advanced,request);if(advanced.DomainOrdinal==terminalDomain)return null;
            return OperationResults.Ok(new BaseSubjectAuthorityMaintenancePageResult{PageOrdinal=page.PageOrdinal,HasMore=true,NextCanonicalKey=ProgressKey(advanced),LifecycleExaminedCount=advanced.ExaminedCount,LifecycleChangedCount=advanced.ChangedCount,RetirementExaminedCount=0,RetirementChangedCount=0,CanonicalBytes=advanced.CanonicalBytes,RollingChecksum=advanced.RollingChecksum,LifecycleResult=null,RetirementResult=null});
        }

        private static int TerminalDomain(BaseSubjectLifecycleMaintenanceKind kind)=>kind switch{BaseSubjectLifecycleMaintenanceKind.Prune=>2,BaseSubjectLifecycleMaintenanceKind.RemoveConsumer=>2,BaseSubjectLifecycleMaintenanceKind.RebuildDeliveryProjection=>1,BaseSubjectLifecycleMaintenanceKind.RotateScopeProtection=>RotationDomains.Length,_=>throw new InvalidDataException(BaseSubjectErrorCodes.LifecycleProviderContractInvalid)};
        private static byte[] ProgressKey(RotationProgress value)=>Encoding.UTF8.GetBytes($"{value.DomainOrdinal}\0{value.LastRowId}\0{value.RollingChecksum}");
        private static void ValidateProgressForKind(RotationProgress value,SqliteLifecycleMaintenanceRequest request)
        {
            switch(request.Kind){case BaseSubjectLifecycleMaintenanceKind.Prune:ValidatePruneProgress(value,request);break;case BaseSubjectLifecycleMaintenanceKind.RemoveConsumer:ValidateConsumerRemovalProgress(value,request);break;case BaseSubjectLifecycleMaintenanceKind.RebuildDeliveryProjection:ValidateDeliveryRebuildProgress(value,request);break;case BaseSubjectLifecycleMaintenanceKind.RotateScopeProtection:ValidateRotationProgress(value,request);break;default:throw new InvalidDataException(BaseSubjectErrorCodes.LifecycleProviderContractInvalid);}
        }

        private OperationResult<BaseSubjectAuthorityMaintenancePageResult> Page(OperationResult<BaseSubjectLifecycleMaintenanceResult> result,long ordinal,BaseSubjectRetirementMaintenancePlan? retirement)
        {
            if (!result.IsSuccess() || result.Value is null) return new() { Status = result.Status, Error = result.Error };
            BaseSubjectLifecycleMaintenanceResult value = result.Value;
            return OperationResults.Ok(new BaseSubjectAuthorityMaintenancePageResult
            {
                PageOrdinal = ordinal, HasMore = false, NextCanonicalKey = null,
                LifecycleExaminedCount = value.ExaminedCount, LifecycleChangedCount = value.ChangedCount,
                RetirementExaminedCount = _retirementExamined, RetirementChangedCount = _retirementChanged, CanonicalBytes = value.CanonicalBytes,
                RollingChecksum = value.RollingChecksum,LifecycleResult=value,
                RetirementResult=retirement is null?null:new BaseSubjectRetirementMaintenanceResult{Kind=retirement.Kind,Outcome=value.Duplicate?BaseSubjectRetirementMutationOutcome.Duplicate:BaseSubjectRetirementMutationOutcome.Applied,ExaminedCount=_retirementExamined,ChangedCount=_retirementChanged,CanonicalBytes=value.CanonicalBytes,RollingChecksum=value.RollingChecksum,PublishedBarrierControlGeneration=_publishedBarrierControlGeneration==0?retirement.ExpectedBarrierControlGeneration:_publishedBarrierControlGeneration},
            });
        }

        private static readonly string[] RotationDomains =
        [
            "lifetimes",
            "terminal-lifetimes",
            "lifecycle-facts",
            "delivery-memberships",
            "consumer-checkpoints",
            "retirement-barriers",
            "retirement-acknowledgements",
            "retirement-terminals",
            "retirement-publications",
            "semantic-scopes",
            "semantic-live-slots",
            "semantic-retired-slots",
            "semantic-absence-slots",
            "semantic-retired-floors",
            "semantic-absence-floors",
        ];

        private static readonly string[] ConsumerRemovalDomains = ["retirement-barriers", "delivery-memberships", "consumer-checkpoints"];

        private async ValueTask<OperationResult<BaseSubjectLifecycleMaintenanceResult>> ExecuteStagedPruneAsync(
            SqliteLifecycleMaintenanceRequest request,
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

        private async ValueTask InitializePruneAsync(SqliteConnection connection, SqliteLifecycleMaintenanceRequest request, CancellationToken cancellationToken)
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

        private async ValueTask<bool> ExecutePrunePageAsync(SqliteConnection connection, SqliteLifecycleMaintenanceRequest request, RotationProgress expected, CancellationToken cancellationToken)
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

        private static void ValidatePruneProgress(RotationProgress progress, SqliteLifecycleMaintenanceRequest request)
        {
            if (progress.Kind != BaseSubjectLifecycleMaintenanceKind.Prune || !string.Equals(progress.Scope, request.Identity.Scope, StringComparison.Ordinal) || !string.Equals(progress.Operation, request.Identity.Operation, StringComparison.Ordinal) || !string.Equals(progress.RequestKey, request.Identity.IdempotencyKey, StringComparison.Ordinal) || !CryptographicOperations.FixedTimeEquals(progress.Fingerprint, request.Identity.Fingerprint.ToArray()) || !CryptographicOperations.FixedTimeEquals(progress.PlanChecksum, request.PlanChecksum) || progress.ExpectedStoreGeneration != request.ExpectedStoreGeneration || progress.ExpectedRestoreEpoch != request.ExpectedRestoreEpoch || progress.ExpectedDeliveryEpoch != request.ExpectedDeliveryEpoch || progress.ExpectedScopeGeneration != request.ExpectedScopeProtectionGeneration || !string.Equals(progress.OldKeyId, request.ExpectedScopeProtectionKeyId, StringComparison.Ordinal) || progress.ReplacementKeyId.Length != 0 || progress.DomainOrdinal is < 0 or > 2 || progress.LastRowId < 0 || progress.ExaminedCount != progress.ChangedCount || progress.ChangedCount < 0 || progress.CanonicalBytes < 0 || progress.RollingChecksum.Length != 64) throw new InvalidDataException(BaseSubjectErrorCodes.LifecycleProviderContractInvalid);
        }

        private async ValueTask<RotationEvidence> ValidatePruneStageAsync(SqliteConnection connection, SqliteTransaction transaction, SqliteLifecycleMaintenanceRequest request, CancellationToken cancellationToken)
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
            0 => owner._names.SubjectRetirementBarriers,
            1 => owner._names.SubjectLifecycleMemberships,
            2 => owner._names.SubjectLifecycleCheckpoints,
            _ => throw new InvalidDataException(BaseSubjectErrorCodes.LifecycleProviderContractInvalid),
        };

        private async ValueTask<OperationResult<BaseSubjectLifecycleMaintenanceResult>> ExecuteStagedConsumerRemovalAsync(
            SqliteLifecycleMaintenanceRequest request,
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
            await ValidateRetirementConsumerRemovalAsync(connection, transaction, request, cancellationToken).ConfigureAwait(false);
            RotationEvidence stagedEvidence = await ValidateConsumerRemovalStageAsync(connection, transaction, request, cancellationToken).ConfigureAwait(false);
            if (stagedEvidence.Examined != completed.ExaminedCount || stagedEvidence.Changed != completed.ChangedCount || stagedEvidence.CanonicalBytes != completed.CanonicalBytes || !string.Equals(stagedEvidence.RollingChecksum, completed.RollingChecksum, StringComparison.Ordinal)) throw new InvalidDataException(BaseSubjectErrorCodes.LifecycleProviderContractInvalid);
            long deleted = 0;
            for (int domain = 1; domain < ConsumerRemovalDomains.Length; domain++)
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
            long retirementRows;await using(SqliteCommand count=connection.CreateCommand()){count.Transaction=transaction;count.CommandTimeout=owner.TimeoutSeconds();count.CommandText=$"SELECT COUNT(*) FROM {owner._names.SubjectLifecycleScopeStage} WHERE domain_ordinal=0;";retirementRows=Convert.ToInt64(await count.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false),CultureInfo.InvariantCulture);}
            if (deleted != checked(completed.ChangedCount-retirementRows)) throw new InvalidDataException(BaseSubjectErrorCodes.LifecycleProviderContractInvalid);
            await using (SqliteCommand publish = connection.CreateCommand())
            {
                publish.Transaction = transaction; publish.CommandTimeout = owner.TimeoutSeconds();
                publish.CommandText = $"DELETE FROM {owner._names.SubjectLifecycleConsumers} WHERE consumer_id=$consumer AND consumer_version=$version AND projection_generation=$generation; DELETE FROM {owner._names.SubjectLifecycleScopeStage}; DELETE FROM {owner._names.SubjectLifecycleMaintenance} WHERE singleton=1;";
                publish.Parameters.AddWithValue("$consumer", request.ConsumerId!); publish.Parameters.AddWithValue("$version", request.ConsumerVersion!.Value); publish.Parameters.AddWithValue("$generation", request.ExpectedProjectionGeneration!.Value);
                if (await publish.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false) < 1) throw new InvalidDataException(BaseSubjectErrorCodes.LifecycleProviderContractInvalid);
            }
            await PublishRetirementConsumerRemovalAsync(connection,transaction,request,cancellationToken).ConfigureAwait(false);
            _retirementExamined=retirementRows;_retirementChanged=retirementRows;
            var result = new BaseSubjectLifecycleMaintenanceResult { Kind = request.Kind, ExaminedCount = checked(completed.ExaminedCount + 1), ChangedCount = checked(completed.ChangedCount + 1), CanonicalBytes = completed.CanonicalBytes, RollingChecksum = completed.RollingChecksum, DeliveryEpoch = request.ExpectedDeliveryEpoch, ProjectionGeneration = null, Duplicate = false };
            await InsertMaintenanceReceiptAsync(connection, transaction, request, result, cancellationToken).ConfigureAwait(false);
            await transaction.CommitAsync(CancellationToken.None).ConfigureAwait(false);
            return OperationResults.Ok(result);
        }

        private async ValueTask<OperationResult<BaseSubjectLifecycleMaintenanceResult>?> InitializeConsumerRemovalAsync(SqliteConnection connection, SqliteLifecycleMaintenanceRequest request, CancellationToken cancellationToken)
        {
            await using SqliteTransaction transaction = (SqliteTransaction)await connection.BeginTransactionAsync(System.Data.IsolationLevel.Serializable, cancellationToken).ConfigureAwait(false);
            RotationProgress? existing = await ReadRotationProgressAsync(connection, transaction, cancellationToken).ConfigureAwait(false);
            if (existing is not null) { ValidateConsumerRemovalProgress(existing, request); await transaction.RollbackAsync(CancellationToken.None).ConfigureAwait(false); return null; }
            await ValidateConsumerProjectionAsync(connection, transaction, request, cancellationToken).ConfigureAwait(false);
            await ValidateRetirementConsumerRemovalAsync(connection, transaction, request, cancellationToken).ConfigureAwait(false);
            await using SqliteCommand insert = connection.CreateCommand(); insert.Transaction = transaction; insert.CommandTimeout = owner.TimeoutSeconds();
            insert.CommandText = $"INSERT INTO {owner._names.SubjectLifecycleMaintenance}(singleton,kind,request_scope,request_operation,request_key,fingerprint,plan_checksum,expected_store_generation,expected_restore_epoch,expected_delivery_epoch,expected_scope_generation,old_key_id,replacement_key_id,domain_ordinal,last_rowid,examined_count,changed_count,canonical_bytes,rolling_checksum) VALUES(1,$kind,$scope,$operation,$key,$fingerprint,$plan,$store,$restore,$delivery,$scopeGeneration,$old,'',0,0,0,0,0,$checksum);";
            insert.Parameters.AddWithValue("$kind", (int)request.Kind); insert.Parameters.AddWithValue("$scope", request.Identity.Scope); insert.Parameters.AddWithValue("$operation", request.Identity.Operation); insert.Parameters.AddWithValue("$key", request.Identity.IdempotencyKey); insert.Parameters.Add("$fingerprint", SqliteType.Blob).Value = request.Identity.Fingerprint.ToArray(); insert.Parameters.Add("$plan", SqliteType.Blob).Value = request.PlanChecksum; insert.Parameters.AddWithValue("$store", request.ExpectedStoreGeneration); insert.Parameters.AddWithValue("$restore", request.ExpectedRestoreEpoch); insert.Parameters.AddWithValue("$delivery", request.ExpectedDeliveryEpoch); insert.Parameters.AddWithValue("$scopeGeneration", request.ExpectedScopeProtectionGeneration); insert.Parameters.AddWithValue("$old", request.ExpectedScopeProtectionKeyId); insert.Parameters.AddWithValue("$checksum", EmptyRotationChecksum);
            await insert.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false); await transaction.CommitAsync(CancellationToken.None).ConfigureAwait(false); return null;
        }

        private async ValueTask<bool> StageConsumerRemovalPageAsync(SqliteConnection connection, SqliteLifecycleMaintenanceRequest request, RotationProgress expected, CancellationToken cancellationToken)
        {
            await using SqliteTransaction transaction = (SqliteTransaction)await connection.BeginTransactionAsync(System.Data.IsolationLevel.Serializable, cancellationToken).ConfigureAwait(false);
            RotationProgress current = await ReadRotationProgressAsync(connection, transaction, cancellationToken).ConfigureAwait(false) ?? throw new InvalidDataException(BaseSubjectErrorCodes.LifecycleProviderContractInvalid);
            ValidateConsumerRemovalProgress(current, request); if (current.DomainOrdinal != expected.DomainOrdinal || current.LastRowId != expected.LastRowId) throw new InvalidDataException(BaseSubjectErrorCodes.LifecycleProviderContractInvalid);
            await using SqliteCommand select = connection.CreateCommand(); select.Transaction = transaction; select.CommandTimeout = owner.TimeoutSeconds();
            if (current.DomainOrdinal == 0)
            {
                select.CommandText = $"SELECT b.rowid,b.scope_kind,b.scope_index_digest,b.subject_id,b.authority_epoch,b.incarnation,b.generation,EXISTS(SELECT 1 FROM {owner._names.SubjectRetirementAcknowledgements} a WHERE a.scope_kind=b.scope_kind AND a.scope_index_digest=b.scope_index_digest AND a.contract_id=b.contract_id AND a.contract_version=b.contract_version AND a.subject_id=b.subject_id AND a.authority_epoch=b.authority_epoch AND a.incarnation=b.incarnation AND a.consumer_id=$consumer AND a.consumer_version=$version) FROM {owner._names.SubjectRetirementBarriers} b WHERE b.contract_id=$contract AND b.contract_version=$contractVersion AND b.rowid>$after ORDER BY b.rowid LIMIT $take;";
                select.Parameters.AddWithValue("$contract", request.ContractId!); select.Parameters.AddWithValue("$contractVersion", request.ContractVersion!.Value);
            }
            else select.CommandText = $"SELECT rowid,NULL,NULL,NULL,NULL,NULL,NULL,1 FROM {ConsumerRemovalTable(current.DomainOrdinal)} WHERE consumer_id=$consumer AND consumer_version=$version AND rowid>$after ORDER BY rowid LIMIT $take;";
            select.Parameters.AddWithValue("$consumer", request.ConsumerId!); select.Parameters.AddWithValue("$version", request.ConsumerVersion!.Value); select.Parameters.AddWithValue("$after", current.LastRowId); select.Parameters.AddWithValue("$take", request.PageSize);
            var rows = new List<(long RowId,string Canonical,bool Resolved)>(); await using (SqliteDataReader reader = await select.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false)) while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                long rowId=reader.GetInt64(0);string canonical=current.DomainOrdinal==0
                    ? $"0\0{rowId}\0{reader.GetInt32(1)}\0{Convert.ToHexStringLower((byte[])reader.GetValue(2))}\0{reader.GetString(3)}\0{Convert.ToHexStringLower((byte[])reader.GetValue(4))}\0{Convert.ToHexStringLower((byte[])reader.GetValue(5))}\0{reader.GetInt64(6)}\0{request.ConsumerId}\0{request.ConsumerVersion}"
                    : $"{current.DomainOrdinal}\0{rowId}\0{request.ConsumerId}\0{request.ConsumerVersion}";
                rows.Add((rowId,canonical,reader.GetBoolean(7)));
            }
            long examined = current.ExaminedCount, changed = current.ChangedCount, bytes = current.CanonicalBytes, last = current.LastRowId; byte[] rolling = Convert.FromHexString(current.RollingChecksum);
            foreach (var row in rows)
            {
                if(current.DomainOrdinal==0&&!row.Resolved)throw new InvalidDataException(BaseSubjectRetirementErrorCodes.BarrierPending);
                byte[] canonical = Encoding.UTF8.GetBytes(row.Canonical); byte[] digest = SHA256.HashData(canonical); rolling = SHA256.HashData([.. rolling, .. canonical]); checked { examined++; changed++; bytes += canonical.LongLength; } last = row.RowId;
                await using SqliteCommand stage = connection.CreateCommand(); stage.Transaction = transaction; stage.CommandTimeout = owner.TimeoutSeconds(); stage.CommandText = $"INSERT INTO {owner._names.SubjectLifecycleScopeStage}(domain_ordinal,source_rowid,prior_digest,prior_value,replacement_digest,replacement_value) VALUES($domain,$rowid,$digest,$canonical,$digest,X'');"; stage.Parameters.AddWithValue("$domain", current.DomainOrdinal); stage.Parameters.AddWithValue("$rowid", row.RowId); stage.Parameters.Add("$digest", SqliteType.Blob).Value = digest;stage.Parameters.Add("$canonical",SqliteType.Blob).Value=current.DomainOrdinal==0?canonical:Array.Empty<byte>(); await stage.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            }
            int nextDomain = rows.Count == 0 ? checked(current.DomainOrdinal + 1) : current.DomainOrdinal; long nextLast = rows.Count == 0 ? 0 : last;
            await using SqliteCommand update = connection.CreateCommand(); update.Transaction = transaction; update.CommandTimeout = owner.TimeoutSeconds(); update.CommandText = $"UPDATE {owner._names.SubjectLifecycleMaintenance} SET domain_ordinal=$domain,last_rowid=$last,examined_count=$examined,changed_count=$changed,canonical_bytes=$bytes,rolling_checksum=$checksum WHERE singleton=1 AND domain_ordinal=$expectedDomain AND last_rowid=$expectedLast;"; update.Parameters.AddWithValue("$domain", nextDomain); update.Parameters.AddWithValue("$last", nextLast); update.Parameters.AddWithValue("$examined", examined); update.Parameters.AddWithValue("$changed", changed); update.Parameters.AddWithValue("$bytes", bytes); update.Parameters.AddWithValue("$checksum", Convert.ToHexStringLower(rolling)); update.Parameters.AddWithValue("$expectedDomain", current.DomainOrdinal); update.Parameters.AddWithValue("$expectedLast", current.LastRowId); if (await update.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false) != 1) throw new InvalidDataException(BaseSubjectErrorCodes.LifecycleProviderContractInvalid); await transaction.CommitAsync(CancellationToken.None).ConfigureAwait(false); return rows.Count != 0;
        }

        private async ValueTask ValidateConsumerProjectionAsync(SqliteConnection connection, SqliteTransaction transaction, SqliteLifecycleMaintenanceRequest request, CancellationToken cancellationToken)
        {
            (long restoreEpoch, long deliveryEpoch, long scopeGeneration, string scopeKeyId) = await ReadAuthorityAsync(connection, transaction, cancellationToken).ConfigureAwait(false);
            if (owner._schemaGeneration != request.ExpectedStoreGeneration || owner._schemaGeneration != request.ExpectedSchemaGeneration || restoreEpoch != request.ExpectedRestoreEpoch || deliveryEpoch != request.ExpectedDeliveryEpoch || scopeGeneration != request.ExpectedScopeProtectionGeneration || !string.Equals(scopeKeyId, request.ExpectedScopeProtectionKeyId, StringComparison.Ordinal)) throw new InvalidDataException(BaseSubjectErrorCodes.LifecycleProviderContractInvalid);
            await using SqliteCommand command = connection.CreateCommand(); command.Transaction = transaction; command.CommandTimeout = owner.TimeoutSeconds(); command.CommandText = $"SELECT projection_generation FROM {owner._names.SubjectLifecycleConsumers} WHERE consumer_id=$consumer AND consumer_version=$version AND contract_id=$contract AND contract_version=$contractVersion;"; command.Parameters.AddWithValue("$consumer", request.ConsumerId!); command.Parameters.AddWithValue("$version", request.ConsumerVersion!.Value); command.Parameters.AddWithValue("$contract", request.ContractId!); command.Parameters.AddWithValue("$contractVersion", request.ContractVersion!.Value); object? value = await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false); if (value is null || Convert.ToInt64(value, System.Globalization.CultureInfo.InvariantCulture) != request.ExpectedProjectionGeneration) throw new InvalidDataException(BaseSubjectErrorCodes.LifecycleProviderContractInvalid);
        }

        private async ValueTask ValidateRetirementConsumerRemovalAsync(SqliteConnection connection,SqliteTransaction transaction,SqliteLifecycleMaintenanceRequest request,CancellationToken cancellationToken)
        {
            if(request.Retirement is null)return;
            if(request.Retirement.Kind!=BaseSubjectRetirementMaintenanceKind.RemoveConsumer||request.Retirement.PlanChecksum is not{Length:32})throw new InvalidDataException(BaseSubjectRetirementErrorCodes.ProviderContractInvalid);
            BaseSubjectRetirementPolicy policy=owner._options.SubjectRetirementPolicies.SingleOrDefault(value=>value.ContractId==request.ContractId&&value.ContractVersion==request.ContractVersion)??throw new InvalidDataException(BaseSubjectRetirementErrorCodes.ProviderContractInvalid);
            BaseAcceptedRetirementConsumer accepted=policy.AcceptedConsumers.SingleOrDefault(value=>value.ConsumerId==request.ConsumerId&&value.ConsumerVersion==request.ConsumerVersion)??throw new InvalidDataException(BaseSubjectRetirementErrorCodes.RegistrationConflict);
            await using SqliteCommand pending=connection.CreateCommand();pending.Transaction=transaction;pending.CommandTimeout=owner.TimeoutSeconds();pending.CommandText=$"SELECT EXISTS(SELECT 1 FROM {owner._names.SubjectRetirementBarriers} b WHERE b.contract_id=$contract AND b.contract_version=$version AND b.state IN ($pending,$timedOut,$quarantined) AND NOT EXISTS(SELECT 1 FROM {owner._names.SubjectRetirementAcknowledgements} a WHERE a.scope_kind=b.scope_kind AND a.scope_index_digest=b.scope_index_digest AND a.contract_id=b.contract_id AND a.contract_version=b.contract_version AND a.subject_id=b.subject_id AND a.authority_epoch=b.authority_epoch AND a.incarnation=b.incarnation AND a.consumer_id=$consumer AND a.consumer_version=$consumerVersion));";pending.Parameters.AddWithValue("$contract",policy.ContractId);pending.Parameters.AddWithValue("$version",policy.ContractVersion);pending.Parameters.AddWithValue("$pending",(int)BaseSubjectRetirementBarrierState.Pending);pending.Parameters.AddWithValue("$timedOut",(int)BaseSubjectRetirementBarrierState.TimedOut);pending.Parameters.AddWithValue("$quarantined",(int)BaseSubjectRetirementBarrierState.Quarantined);pending.Parameters.AddWithValue("$consumer",accepted.ConsumerId);pending.Parameters.AddWithValue("$consumerVersion",accepted.ConsumerVersion);if(Convert.ToInt32(await pending.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false),CultureInfo.InvariantCulture)!=0)throw new InvalidDataException(BaseSubjectRetirementErrorCodes.BarrierPending);
        }

        private async ValueTask PublishRetirementConsumerRemovalAsync(SqliteConnection connection,SqliteTransaction transaction,SqliteLifecycleMaintenanceRequest request,CancellationToken cancellationToken)
        {
            if(request.Retirement is null)return;BaseSubjectRetirementPolicy policy=owner._options.SubjectRetirementPolicies.Single(value=>value.ContractId==request.ContractId&&value.ContractVersion==request.ContractVersion);string previous=BaseSubjectRetirementRegistry.AcceptedSetChecksum(policy.AcceptedConsumers);string published=BaseSubjectRetirementRegistry.AcceptedSetChecksum(policy.AcceptedConsumers.Where(value=>value.ConsumerId!=request.ConsumerId||value.ConsumerVersion!=request.ConsumerVersion));long position;
            await using(SqliteCommand advance=connection.CreateCommand()){advance.Transaction=transaction;advance.CommandTimeout=owner.TimeoutSeconds();advance.CommandText=$"UPDATE {owner._names.ProviderState} SET value=CAST(value AS INTEGER)+1 WHERE key='subject_retirement_position' RETURNING CAST(value AS INTEGER);";position=Convert.ToInt64(await advance.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false),CultureInfo.InvariantCulture);}
            _publishedBarrierControlGeneration=position;
            var fact=BaseSubjectRetirementRegistry.SealPublication(new BaseSubjectRetirementPublicationFact{Position=new(position),Kind=BaseSubjectRetirementPublicationKind.ConsumerSetChanged,ConsumerSet=new(){ContractId=policy.ContractId,ContractVersion=policy.ContractVersion,PreviousConsumerSetChecksum=previous,PublishedConsumerSetChecksum=published,PreviousGraphGeneration=request.Retirement.ExpectedGraphGeneration,PublishedGraphGeneration=checked(request.Retirement.ExpectedGraphGeneration+1),RemovedConsumerId=request.ConsumerId}});BaseSubjectRetirementRegistry.ValidatePublication(new(){Scope=null,Fact=fact});byte[] payload=JsonSerializer.SerializeToUtf8Bytes(fact,HPDBaseJsonSerializerContext.Default.BaseSubjectRetirementPublicationFact);
            await using SqliteCommand insert=connection.CreateCommand();insert.Transaction=transaction;insert.CommandTimeout=owner.TimeoutSeconds();insert.CommandText=$"INSERT INTO {owner._names.SubjectRetirementPublications}(position,kind,scope_kind,scope_index_digest,protected_scope_value,payload) VALUES($position,$kind,NULL,NULL,NULL,$payload);";insert.Parameters.AddWithValue("$position",position);insert.Parameters.AddWithValue("$kind",(int)fact.Kind);insert.Parameters.Add("$payload",SqliteType.Blob).Value=payload;if(await insert.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false)!=1)throw new InvalidDataException(BaseSubjectRetirementErrorCodes.ProviderContractInvalid);
        }

        private static void ValidateConsumerRemovalProgress(RotationProgress progress, SqliteLifecycleMaintenanceRequest request)
        {
            if (progress.Kind != BaseSubjectLifecycleMaintenanceKind.RemoveConsumer || !string.Equals(progress.Scope, request.Identity.Scope, StringComparison.Ordinal) || !string.Equals(progress.Operation, request.Identity.Operation, StringComparison.Ordinal) || !string.Equals(progress.RequestKey, request.Identity.IdempotencyKey, StringComparison.Ordinal) || !CryptographicOperations.FixedTimeEquals(progress.Fingerprint, request.Identity.Fingerprint.ToArray()) || !CryptographicOperations.FixedTimeEquals(progress.PlanChecksum, request.PlanChecksum) || progress.ExpectedStoreGeneration != request.ExpectedStoreGeneration || progress.ExpectedRestoreEpoch != request.ExpectedRestoreEpoch || progress.ExpectedDeliveryEpoch != request.ExpectedDeliveryEpoch || progress.ExpectedScopeGeneration != request.ExpectedScopeProtectionGeneration || !string.Equals(progress.OldKeyId, request.ExpectedScopeProtectionKeyId, StringComparison.Ordinal) || progress.ReplacementKeyId.Length != 0 || progress.DomainOrdinal < 0 || progress.DomainOrdinal > ConsumerRemovalDomains.Length || progress.LastRowId < 0 || progress.ExaminedCount < 0 || progress.ChangedCount < 0 || progress.CanonicalBytes < 0 || progress.RollingChecksum.Length != 64) throw new InvalidDataException(BaseSubjectErrorCodes.LifecycleProviderContractInvalid);
        }

        private async ValueTask<RotationEvidence> ValidateConsumerRemovalStageAsync(SqliteConnection connection, SqliteTransaction transaction, SqliteLifecycleMaintenanceRequest request, CancellationToken cancellationToken)
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
                        byte[] canonical = domain==0?row.PriorValue:Encoding.UTF8.GetBytes($"{domain}\0{row.RowId}\0{request.ConsumerId}\0{request.ConsumerVersion}"); byte[] digest = SHA256.HashData(canonical);
                        if ((domain==0?row.PriorValue.Length==0:row.PriorValue.Length!=0) || row.ReplacementValue.Length != 0 || !CryptographicOperations.FixedTimeEquals(row.Prior, digest) || !CryptographicOperations.FixedTimeEquals(row.Replacement, digest)) throw new InvalidDataException(BaseSubjectErrorCodes.LifecycleProviderContractInvalid);
                        await using SqliteCommand exists = connection.CreateCommand(); exists.Transaction = transaction; exists.CommandTimeout = owner.TimeoutSeconds(); exists.CommandText = domain==0
                            ? $"SELECT EXISTS(SELECT 1 FROM {owner._names.SubjectRetirementBarriers} b WHERE b.rowid=$rowid AND b.contract_id=$contract AND b.contract_version=$contractVersion AND EXISTS(SELECT 1 FROM {owner._names.SubjectRetirementAcknowledgements} a WHERE a.scope_kind=b.scope_kind AND a.scope_index_digest=b.scope_index_digest AND a.contract_id=b.contract_id AND a.contract_version=b.contract_version AND a.subject_id=b.subject_id AND a.authority_epoch=b.authority_epoch AND a.incarnation=b.incarnation AND a.consumer_id=$consumer AND a.consumer_version=$version));"
                            : $"SELECT EXISTS(SELECT 1 FROM {ConsumerRemovalTable(domain)} WHERE rowid=$rowid AND consumer_id=$consumer AND consumer_version=$version);"; exists.Parameters.AddWithValue("$rowid", row.RowId); exists.Parameters.AddWithValue("$consumer", request.ConsumerId!); exists.Parameters.AddWithValue("$version", request.ConsumerVersion!.Value);if(domain==0){exists.Parameters.AddWithValue("$contract",request.ContractId!);exists.Parameters.AddWithValue("$contractVersion",request.ContractVersion!.Value);} if (Convert.ToInt32(await exists.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false), System.Globalization.CultureInfo.InvariantCulture) != 1) throw new InvalidDataException(BaseSubjectErrorCodes.LifecycleProviderContractInvalid);
                        rolling = SHA256.HashData([.. rolling, .. canonical]); checked { examined++; changed++; bytes += canonical.LongLength; } after = row.RowId;
                    }
                }
            }
            return new RotationEvidence(examined, changed, bytes, Convert.ToHexStringLower(rolling));
        }

        private async ValueTask<OperationResult<BaseSubjectLifecycleMaintenanceResult>> ExecuteStagedDeliveryRebuildAsync(SqliteLifecycleMaintenanceRequest request, CancellationToken cancellationToken)
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

        private async ValueTask InitializeDeliveryRebuildAsync(SqliteConnection connection, SqliteLifecycleMaintenanceRequest request, CancellationToken cancellationToken)
        {
            await using SqliteTransaction transaction = (SqliteTransaction)await connection.BeginTransactionAsync(System.Data.IsolationLevel.Serializable, cancellationToken).ConfigureAwait(false);
            RotationProgress? existing = await ReadRotationProgressAsync(connection, transaction, cancellationToken).ConfigureAwait(false);
            if (existing is not null) { ValidateDeliveryRebuildProgress(existing, request); await transaction.RollbackAsync(CancellationToken.None).ConfigureAwait(false); return; }
            await ValidateConsumerProjectionAsync(connection, transaction, request, cancellationToken).ConfigureAwait(false);
            if (!owner._options.SubjectLifecycleConsumers.Any(value => value.Id == request.ConsumerId && value.Version == request.ConsumerVersion)) throw new InvalidDataException(BaseSubjectErrorCodes.LifecycleProviderContractInvalid);
            await using SqliteCommand insert = connection.CreateCommand(); insert.Transaction = transaction; insert.CommandTimeout = owner.TimeoutSeconds(); insert.CommandText = $"INSERT INTO {owner._names.SubjectLifecycleMaintenance}(singleton,kind,request_scope,request_operation,request_key,fingerprint,plan_checksum,expected_store_generation,expected_restore_epoch,expected_delivery_epoch,expected_scope_generation,old_key_id,replacement_key_id,domain_ordinal,last_rowid,examined_count,changed_count,canonical_bytes,rolling_checksum) VALUES(1,$kind,$scope,$operation,$key,$fingerprint,$plan,$store,$restore,$delivery,$scopeGeneration,$old,'',0,0,0,0,0,$checksum);"; insert.Parameters.AddWithValue("$kind", (int)request.Kind); insert.Parameters.AddWithValue("$scope", request.Identity.Scope); insert.Parameters.AddWithValue("$operation", request.Identity.Operation); insert.Parameters.AddWithValue("$key", request.Identity.IdempotencyKey); insert.Parameters.Add("$fingerprint", SqliteType.Blob).Value = request.Identity.Fingerprint.ToArray(); insert.Parameters.Add("$plan", SqliteType.Blob).Value = request.PlanChecksum; insert.Parameters.AddWithValue("$store", request.ExpectedStoreGeneration); insert.Parameters.AddWithValue("$restore", request.ExpectedRestoreEpoch); insert.Parameters.AddWithValue("$delivery", request.ExpectedDeliveryEpoch); insert.Parameters.AddWithValue("$scopeGeneration", request.ExpectedScopeProtectionGeneration); insert.Parameters.AddWithValue("$old", request.ExpectedScopeProtectionKeyId); insert.Parameters.AddWithValue("$checksum", EmptyRotationChecksum); await insert.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false); await transaction.CommitAsync(CancellationToken.None).ConfigureAwait(false);
        }

        private async ValueTask<bool> StageDeliveryRebuildPageAsync(SqliteConnection connection, SqliteLifecycleMaintenanceRequest request, RotationProgress expected, CancellationToken cancellationToken)
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

        private static void ValidateDeliveryRebuildProgress(RotationProgress progress, SqliteLifecycleMaintenanceRequest request)
        {
            if (progress.Kind != BaseSubjectLifecycleMaintenanceKind.RebuildDeliveryProjection || !string.Equals(progress.Scope, request.Identity.Scope, StringComparison.Ordinal) || !string.Equals(progress.Operation, request.Identity.Operation, StringComparison.Ordinal) || !string.Equals(progress.RequestKey, request.Identity.IdempotencyKey, StringComparison.Ordinal) || !CryptographicOperations.FixedTimeEquals(progress.Fingerprint, request.Identity.Fingerprint.ToArray()) || !CryptographicOperations.FixedTimeEquals(progress.PlanChecksum, request.PlanChecksum) || progress.ExpectedStoreGeneration != request.ExpectedStoreGeneration || progress.ExpectedRestoreEpoch != request.ExpectedRestoreEpoch || progress.ExpectedDeliveryEpoch != request.ExpectedDeliveryEpoch || progress.ExpectedScopeGeneration != request.ExpectedScopeProtectionGeneration || !string.Equals(progress.OldKeyId, request.ExpectedScopeProtectionKeyId, StringComparison.Ordinal) || progress.ReplacementKeyId.Length != 0 || progress.DomainOrdinal is < 0 or > 1 || progress.LastRowId < 0 || progress.ExaminedCount < progress.ChangedCount || progress.ChangedCount < 0 || progress.CanonicalBytes < 0 || progress.RollingChecksum.Length != 64) throw new InvalidDataException(BaseSubjectErrorCodes.LifecycleProviderContractInvalid);
        }

        private async ValueTask<RotationEvidence> ValidateDeliveryRebuildStageAsync(SqliteConnection connection, SqliteTransaction transaction, SqliteLifecycleMaintenanceRequest request, CancellationToken cancellationToken)
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
            5 => owner._names.SubjectRetirementBarriers,
            6 => owner._names.SubjectRetirementAcknowledgements,
            7 => owner._names.SubjectRetirementTerminals,
            8 => owner._names.SubjectRetirementPublications,
            9 => owner._names.SemanticActivationScopes,
            10 => owner._names.SemanticActivationSlots,
            11 => owner._names.SemanticActivationSlots,
            12 => owner._names.SemanticActivationSlots,
            13 => owner._names.SemanticActivationRecoveryFloors,
            14 => owner._names.SemanticActivationRecoveryFloors,
            _ => throw new InvalidDataException(BaseSubjectErrorCodes.LifecycleProviderContractInvalid),
        };

        private async ValueTask<OperationResult<BaseSubjectLifecycleMaintenanceResult>> ExecuteStagedRotationAsync(
            SqliteLifecycleMaintenanceRequest request,
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
            bool semanticInstalled = owner._options.SemanticActivations.Length != 0;
            if (semanticInstalled)
            {
                long semanticGeneration = await owner.ReadSemanticAuthorityGenerationAsync(connection, null, cancellationToken).ConfigureAwait(false);
                if (request.ExpectedSemanticActivationAuthorityGeneration != semanticGeneration
                    || request.ExpectedSemanticActivationDefinitionSetChecksum.Length != 32
                    || !CryptographicOperations.FixedTimeEquals(request.ExpectedSemanticActivationDefinitionSetChecksum.AsSpan(), owner._options.SemanticActivationDefinitionSetChecksum))
                    return Failure(BaseSubjectErrorCodes.ScopeProtectionRotationConflict, OperationStatus.Conflict, ErrorCategory.Conflict);
            }
            else if (request.ExpectedSemanticActivationAuthorityGeneration is not null
                || !request.ExpectedSemanticActivationDefinitionSetChecksum.IsDefaultOrEmpty)
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

            long? resultingSemanticGeneration = null;
            if (semanticInstalled)
            {
                long currentSemanticGeneration = await owner.ReadSemanticAuthorityGenerationAsync(connection, transaction, cancellationToken).ConfigureAwait(false);
                if (currentSemanticGeneration != request.ExpectedSemanticActivationAuthorityGeneration)
                    return Failure(BaseSubjectErrorCodes.ScopeProtectionRotationConflict, OperationStatus.Conflict, ErrorCategory.Conflict);
                resultingSemanticGeneration = checked(currentSemanticGeneration + 1);
            }
            RotationEvidence evidence = await ValidateAndPublishRotationAsync(
                connection, transaction, replacement, request.ExpectedScopeProtectionKeyId,
                request.ExpectedRestoreEpoch, request.ExpectedSchemaGeneration,
                request.PageSize, resultingSemanticGeneration, cancellationToken).ConfigureAwait(false);
            if (evidence.Examined != completed.ExaminedCount
                || evidence.Changed != completed.ChangedCount
                || evidence.CanonicalBytes != completed.CanonicalBytes
                || !string.Equals(evidence.RollingChecksum, completed.RollingChecksum, StringComparison.Ordinal))
                throw new InvalidDataException(BaseSubjectErrorCodes.LifecycleProviderContractInvalid);

            if(request.Retirement is not null)
            {
                await using SqliteCommand retirementEvidence=connection.CreateCommand();retirementEvidence.Transaction=transaction;retirementEvidence.CommandTimeout=owner.TimeoutSeconds();
                retirementEvidence.CommandText=$"SELECT (SELECT COUNT(*) FROM {owner._names.SubjectLifecycleScopeStage} WHERE domain_ordinal>=5),CAST((SELECT value FROM {owner._names.ProviderState} WHERE key='subject_retirement_position') AS INTEGER);";
                await using SqliteDataReader retirementReader=await retirementEvidence.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);if(!await retirementReader.ReadAsync(cancellationToken).ConfigureAwait(false))throw new InvalidDataException(BaseSubjectRetirementErrorCodes.ProviderContractInvalid);
                _retirementExamined=retirementReader.GetInt64(0);_retirementChanged=_retirementExamined;long currentBarrierControl=retirementReader.GetInt64(1);
                if(currentBarrierControl!=request.Retirement.ExpectedBarrierControlGeneration)throw new InvalidDataException(BaseSubjectRetirementErrorCodes.ProviderContractInvalid);
                _publishedBarrierControlGeneration=checked(currentBarrierControl+1);
            }

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
UPDATE {owner._names.ProviderState} SET value=$semanticGeneration WHERE key='semantic_activation_authority_generation' AND $semanticGeneration IS NOT NULL;
UPDATE {owner._names.ProviderState} SET value=CAST(value AS INTEGER)+1 WHERE key='subject_retirement_position';
DELETE FROM {owner._names.SubjectLifecycleScopeStage};
DELETE FROM {owner._names.SubjectLifecycleMaintenance} WHERE singleton=1;
""";
                publish.Parameters.AddWithValue("$delivery", nextDelivery.ToString(System.Globalization.CultureInfo.InvariantCulture));
                publish.Parameters.AddWithValue("$generation", nextScopeGeneration.ToString(System.Globalization.CultureInfo.InvariantCulture));
                publish.Parameters.AddWithValue("$key", replacement.ToString(System.Globalization.CultureInfo.InvariantCulture));
                publish.Parameters.AddWithValue("$semanticGeneration", (object?)resultingSemanticGeneration?.ToString(CultureInfo.InvariantCulture) ?? DBNull.Value);
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
            SqliteLifecycleMaintenanceRequest request,
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
            SqliteLifecycleMaintenanceRequest request,
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
            select.CommandText = current.DomainOrdinal switch
            {
                9 => $"SELECT rowid,binding_json FROM {table} WHERE rowid>$after ORDER BY rowid LIMIT $take;",
                10 => $"SELECT rowid,authority_json FROM {table} WHERE rowid>$after AND state=1 ORDER BY rowid LIMIT $take;",
                11 => $"SELECT rowid,authority_json FROM {table} WHERE rowid>$after AND state=2 ORDER BY rowid LIMIT $take;",
                12 => $"SELECT rowid,authority_json FROM {table} WHERE rowid>$after AND state=3 ORDER BY rowid LIMIT $take;",
                13 => $"SELECT rowid,authority_json FROM {table} WHERE rowid>$after AND state=2 ORDER BY rowid LIMIT $take;",
                14 => $"SELECT rowid,authority_json FROM {table} WHERE rowid>$after AND state=3 ORDER BY rowid LIMIT $take;",
                _ => $"SELECT rowid,scope_kind,scope_index_digest,protected_scope_value FROM {table} WHERE rowid>$after{(current.DomainOrdinal==8?" AND scope_kind IS NOT NULL":string.Empty)} ORDER BY rowid LIMIT $take;",
            };
            select.Parameters.AddWithValue("$after", current.LastRowId);
            select.Parameters.AddWithValue("$take", request.PageSize);
            var rows = new List<(long RowId, BaseProtectedSubjectScope Scope)>();
            await using (SqliteDataReader reader = await select.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false))
                while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
                {
                    if (current.DomainOrdinal == 9)
                    {
                        BaseSemanticActivationScopeBinding binding = JsonSerializer.Deserialize((byte[])reader.GetValue(1), HPDBaseJsonSerializerContext.Default.BaseSemanticActivationScopeBinding)
                            ?? throw new InvalidDataException(BaseSemanticActivationErrorCodes.Corrupt);
                        rows.Add((reader.GetInt64(0), new BaseProtectedSubjectScope { Kind = binding.Kind, IndexDigest = binding.SeekDigest.ToArray(), ProtectedCanonicalValue = binding.ProtectedCanonicalScope.ToArray() }));
                    }
                    else if (current.DomainOrdinal == 10)
                    {
                        BaseSemanticActivationLiveAuthority live = JsonSerializer.Deserialize((byte[])reader.GetValue(1), HPDBaseJsonSerializerContext.Default.BaseSemanticActivationLiveAuthority)
                            ?? throw new InvalidDataException(BaseSemanticActivationErrorCodes.Corrupt);
                        rows.Add((reader.GetInt64(0), new BaseProtectedSubjectScope { Kind = live.ScopeBinding.Kind, IndexDigest = live.ScopeBinding.SeekDigest.ToArray(), ProtectedCanonicalValue = live.ScopeBinding.ProtectedCanonicalScope.ToArray() }));
                    }
                    else if (current.DomainOrdinal is >= 11 and <= 14)
                    {
                        byte[] authority = (byte[])reader.GetValue(1);
                        byte[] checksum = ReadNegativeAuthorityChecksum(authority, current.DomainOrdinal is 11 or 13);
                        rows.Add((reader.GetInt64(0), new BaseProtectedSubjectScope
                        { Kind = BaseSubjectScopeKind.Global, IndexDigest = checksum, ProtectedCanonicalValue = authority }));
                    }
                    else rows.Add((reader.GetInt64(0), new BaseProtectedSubjectScope { Kind = (BaseSubjectScopeKind)reader.GetInt32(1), IndexDigest = (byte[])reader.GetValue(2), ProtectedCanonicalValue = (byte[])reader.GetValue(3) }));
                }

            long examined = current.ExaminedCount;
            long changed = current.ChangedCount;
            long canonicalBytes = current.CanonicalBytes;
            long lastRowId = current.LastRowId;
            byte[] rolling = Convert.FromHexString(current.RollingChecksum);
            foreach ((long rowId, BaseProtectedSubjectScope prior) in rows)
            {
                if (current.DomainOrdinal == 9)
                {
                    BaseSemanticActivationScopeBinding binding = await ReadSemanticScopeBindingAsync(
                        connection, transaction, rowId, cancellationToken).ConfigureAwait(false);
                    ValidateSemanticScopeBinding(binding, request.ExpectedScopeProtectionKeyId);
                }
                else if (current.DomainOrdinal == 10)
                {
                    BaseSemanticActivationLiveAuthority live = await ReadSemanticLiveAuthorityAsync(
                        connection, transaction, rowId, cancellationToken).ConfigureAwait(false);
                    await ValidateSemanticLiveRotationSourceAsync(connection, transaction, live,
                        request.ExpectedSemanticActivationAuthorityGeneration
                            ?? throw new InvalidDataException(BaseSemanticActivationErrorCodes.Corrupt),
                        request.ExpectedSemanticActivationDefinitionSetChecksum,
                        request.ExpectedRestoreEpoch, request.ExpectedSchemaGeneration,
                        request.ExpectedScopeProtectionKeyId, directoryAlreadyRotated: false,
                        cancellationToken).ConfigureAwait(false);
                }
                else if (current.DomainOrdinal is 13 or 14)
                {
                    await ValidateSemanticRecoveryFloorRotationSourceAsync(connection, transaction, rowId,
                        current.DomainOrdinal == 13,
                        request.ExpectedSemanticActivationAuthorityGeneration
                            ?? throw new InvalidDataException(BaseSemanticActivationErrorCodes.Corrupt),
                        request.ExpectedSemanticActivationDefinitionSetChecksum.ToArray(),
                        request.ExpectedRestoreEpoch, request.ExpectedSchemaGeneration, cancellationToken)
                        .ConfigureAwait(false);
                }
                else if (current.DomainOrdinal is 11 or 12)
                {
                    await ValidateSemanticNegativeSlotRotationSourceAsync(connection, transaction, rowId,
                        current.DomainOrdinal == 11,
                        request.ExpectedSemanticActivationAuthorityGeneration
                            ?? throw new InvalidDataException(BaseSemanticActivationErrorCodes.Corrupt),
                        request.ExpectedSemanticActivationDefinitionSetChecksum.ToArray(),
                        request.ExpectedRestoreEpoch, request.ExpectedSchemaGeneration, cancellationToken)
                        .ConfigureAwait(false);
                }
                BaseProtectedSubjectScope next;
                if (current.DomainOrdinal is >= 11 and <= 14)
                {
                    (byte[] checksum, byte[] authority) = RotateNegativeAuthority(prior.ProtectedCanonicalValue,
                        current.DomainOrdinal is 11 or 13,
                        request.ExpectedSemanticActivationAuthorityGeneration
                            ?? throw new InvalidDataException(BaseSemanticActivationErrorCodes.Corrupt),
                        request.ExpectedSemanticActivationDefinitionSetChecksum);
                    next = new BaseProtectedSubjectScope { Kind = prior.Kind, IndexDigest = checksum, ProtectedCanonicalValue = authority };
                }
                else
                {
                    BaseOwnedSubjectScopeEvidence logical = owner._subjectScopes!.Unprotect(prior)
                        ?? throw new InvalidDataException(BaseSubjectErrorCodes.LifecycleProviderContractInvalid);
                    next = current.DomainOrdinal == 10
                        ? await ReadStagedSemanticScopeProtectionAsync(connection, transaction, rowId, cancellationToken).ConfigureAwait(false)
                        : owner._subjectScopes.Protect(logical, replacement);
                }
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

        private async ValueTask<BaseProtectedSubjectScope> ReadStagedSemanticScopeProtectionAsync(
            SqliteConnection connection, SqliteTransaction transaction, long liveRotationId, CancellationToken cancellationToken)
        {
            await using SqliteCommand command = connection.CreateCommand();
            command.Transaction = transaction; command.CommandTimeout = owner.TimeoutSeconds();
            command.CommandText = $"""
SELECT s.scope_kind,st.replacement_digest,st.replacement_value
FROM {owner._names.SemanticActivationSlots} l
JOIN {owner._names.SemanticActivationScopes} s ON s.binding_id=l.binding_id
JOIN {owner._names.SubjectLifecycleScopeStage} st ON st.domain_ordinal=9 AND st.source_rowid=s.rotation_id
WHERE l.rotation_id=$rotation;
""";
            command.Parameters.AddWithValue("$rotation", liveRotationId);
            await using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
                throw new InvalidDataException(BaseSemanticActivationErrorCodes.Corrupt);
            return new BaseProtectedSubjectScope
            {
                Kind = (BaseSubjectScopeKind)reader.GetInt32(0),
                IndexDigest = (byte[])reader.GetValue(1),
                ProtectedCanonicalValue = (byte[])reader.GetValue(2),
            };
        }

        private static byte[] ReadNegativeAuthorityChecksum(byte[] authority, bool retired) => retired
            ? (JsonSerializer.Deserialize(authority, HPDBaseJsonSerializerContext.Default.BaseSemanticActivationRetirementAuthority)
                ?? throw new InvalidDataException(BaseSemanticActivationErrorCodes.Corrupt)).Checksum.ToArray()
            : (JsonSerializer.Deserialize(authority, HPDBaseJsonSerializerContext.Default.BaseSemanticActivationAbsenceAuthority)
                ?? throw new InvalidDataException(BaseSemanticActivationErrorCodes.Corrupt)).Checksum.ToArray();

        private static (byte[] Checksum, byte[] Authority) RotateNegativeAuthority(
            byte[] authority, bool retired, long expectedGeneration, ImmutableArray<byte> definitionSet)
        {
            long resultingGeneration = checked(expectedGeneration + 1);
            if (retired)
            {
                BaseSemanticActivationRetirementAuthority prior = JsonSerializer.Deserialize(authority,
                    HPDBaseJsonSerializerContext.Default.BaseSemanticActivationRetirementAuthority)
                    ?? throw new InvalidDataException(BaseSemanticActivationErrorCodes.Corrupt);
                ValidateSemanticStoreAuthority(prior.StoreAuthority, expectedGeneration, definitionSet);
                if (!CryptographicOperations.FixedTimeEquals(prior.Checksum.AsSpan(),
                    BaseSemanticActivationEvidenceContract.RetirementChecksum(prior).AsSpan()))
                    throw new InvalidDataException(BaseSemanticActivationErrorCodes.Corrupt);
                BaseSemanticActivationStoreAuthority store = BaseSemanticActivationEvidenceContract.CreateStoreAuthority(
                    prior.StoreAuthority.Requirement with { SemanticAuthorityGeneration = resultingGeneration });
                BaseSemanticActivationRetirementAuthority next = prior with { StoreAuthority = store, Checksum = [] };
                next = next with { Checksum = BaseSemanticActivationEvidenceContract.RetirementChecksum(next) };
                return (next.Checksum.ToArray(), JsonSerializer.SerializeToUtf8Bytes(next,
                    HPDBaseJsonSerializerContext.Default.BaseSemanticActivationRetirementAuthority));
            }
            BaseSemanticActivationAbsenceAuthority priorAbsent = JsonSerializer.Deserialize(authority,
                HPDBaseJsonSerializerContext.Default.BaseSemanticActivationAbsenceAuthority)
                ?? throw new InvalidDataException(BaseSemanticActivationErrorCodes.Corrupt);
            ValidateSemanticStoreAuthority(priorAbsent.StoreAuthority, expectedGeneration, definitionSet);
            if (!CryptographicOperations.FixedTimeEquals(priorAbsent.Checksum.AsSpan(),
                BaseSemanticActivationEvidenceContract.AbsenceChecksum(priorAbsent).AsSpan()))
                throw new InvalidDataException(BaseSemanticActivationErrorCodes.Corrupt);
            BaseSemanticActivationStoreAuthority absentStore = BaseSemanticActivationEvidenceContract.CreateStoreAuthority(
                priorAbsent.StoreAuthority.Requirement with { SemanticAuthorityGeneration = resultingGeneration });
            BaseSemanticActivationAbsenceAuthority nextAbsent = priorAbsent with { StoreAuthority = absentStore, Checksum = [] };
            nextAbsent = nextAbsent with { Checksum = BaseSemanticActivationEvidenceContract.AbsenceChecksum(nextAbsent) };
            return (nextAbsent.Checksum.ToArray(), JsonSerializer.SerializeToUtf8Bytes(nextAbsent,
                HPDBaseJsonSerializerContext.Default.BaseSemanticActivationAbsenceAuthority));
        }

        private static void ValidateSemanticStoreAuthority(BaseSemanticActivationStoreAuthority authority,
            long expectedGeneration, ImmutableArray<byte> definitionSet)
        {
            if (authority.Requirement.SemanticAuthorityGeneration != expectedGeneration
                || definitionSet.Length != 32
                || !CryptographicOperations.FixedTimeEquals(authority.Requirement.DefinitionSetChecksum.AsSpan(), definitionSet.AsSpan())
                || !CryptographicOperations.FixedTimeEquals(authority.Checksum.AsSpan(),
                    BaseSemanticActivationEvidenceContract.StoreAuthorityChecksum(authority.Requirement).AsSpan()))
                throw new InvalidDataException(BaseSemanticActivationErrorCodes.Corrupt);
        }

        private async ValueTask<RotationEvidence> ValidateAndPublishRotationAsync(
            SqliteConnection connection,
            SqliteTransaction transaction,
            byte replacement,
            string expectedProtectionKeyId,
            long expectedRestoreEpoch,
            long expectedSchemaGeneration,
            int pageSize,
            long? resultingSemanticGeneration,
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
                            source.CommandText = domain switch
                            {
                                9 => $"SELECT binding_json FROM {table} WHERE rowid=$rowid;",
                                10 => $"SELECT authority_json FROM {table} WHERE rowid=$rowid AND state=1;",
                                11 or 13 => $"SELECT authority_json FROM {table} WHERE rowid=$rowid AND state=2;",
                                12 or 14 => $"SELECT authority_json FROM {table} WHERE rowid=$rowid AND state=3;",
                                _ => $"SELECT scope_kind,scope_index_digest,protected_scope_value FROM {table} WHERE rowid=$rowid;",
                            };
                            source.Parameters.AddWithValue("$rowid", row.RowId);
                            await using SqliteDataReader reader = await source.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
                            if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
                                throw new InvalidDataException(BaseSubjectErrorCodes.LifecycleProviderContractInvalid);
                            if (domain == 9)
                            {
                                BaseSemanticActivationScopeBinding binding = JsonSerializer.Deserialize((byte[])reader.GetValue(0), HPDBaseJsonSerializerContext.Default.BaseSemanticActivationScopeBinding)
                                    ?? throw new InvalidDataException(BaseSemanticActivationErrorCodes.Corrupt);
                                prior = new BaseProtectedSubjectScope { Kind = binding.Kind, IndexDigest = binding.SeekDigest.ToArray(), ProtectedCanonicalValue = binding.ProtectedCanonicalScope.ToArray() };
                            }
                            else if (domain == 10)
                            {
                                BaseSemanticActivationLiveAuthority live = JsonSerializer.Deserialize((byte[])reader.GetValue(0), HPDBaseJsonSerializerContext.Default.BaseSemanticActivationLiveAuthority)
                                    ?? throw new InvalidDataException(BaseSemanticActivationErrorCodes.Corrupt);
                                prior = new BaseProtectedSubjectScope { Kind = live.ScopeBinding.Kind, IndexDigest = live.ScopeBinding.SeekDigest.ToArray(), ProtectedCanonicalValue = live.ScopeBinding.ProtectedCanonicalScope.ToArray() };
                            }
                            else if (domain is >= 11 and <= 14)
                            {
                                byte[] authority = (byte[])reader.GetValue(0);
                                prior = new BaseProtectedSubjectScope
                                {
                                    Kind = BaseSubjectScopeKind.Global,
                                    IndexDigest = ReadNegativeAuthorityChecksum(authority, domain is 11 or 13),
                                    ProtectedCanonicalValue = authority,
                                };
                            }
                            else prior = new BaseProtectedSubjectScope { Kind = (BaseSubjectScopeKind)reader.GetInt32(0), IndexDigest = (byte[])reader.GetValue(1), ProtectedCanonicalValue = (byte[])reader.GetValue(2) };
                        }
                        BaseSubjectRetirementTerminalReceipt? priorTerminal=domain==7?await ReadRetirementTerminalAsync(connection,transaction,row.RowId,prior,cancellationToken).ConfigureAwait(false):null;
                        if (!CryptographicOperations.FixedTimeEquals(prior.IndexDigest, row.PriorDigest)
                            || !CryptographicOperations.FixedTimeEquals(prior.ProtectedCanonicalValue, row.PriorValue))
                            throw new InvalidDataException(BaseSubjectErrorCodes.LifecycleProviderContractInvalid);
                        var stagedReplacement = new BaseProtectedSubjectScope
                        {
                            Kind = prior.Kind,
                            IndexDigest = row.NextDigest,
                            ProtectedCanonicalValue = row.NextValue,
                        };
                        if (domain is >= 11 and <= 14)
                        {
                            if (resultingSemanticGeneration is null)
                                throw new InvalidDataException(BaseSemanticActivationErrorCodes.Corrupt);
                            if (domain is 13 or 14)
                            {
                                await ValidateSemanticRecoveryFloorRotationSourceAsync(connection, transaction, row.RowId,
                                    domain == 13,
                                    checked(resultingSemanticGeneration.Value - 1),
                                    owner._options.SemanticActivationDefinitionSetChecksum,
                                    expectedRestoreEpoch, expectedSchemaGeneration, cancellationToken,
                                    slotAlreadyRotated: true)
                                    .ConfigureAwait(false);
                            }
                            else if (domain is 11 or 12)
                            {
                                await ValidateSemanticNegativeSlotRotationSourceAsync(connection, transaction, row.RowId,
                                    domain == 11, checked(resultingSemanticGeneration.Value - 1),
                                    owner._options.SemanticActivationDefinitionSetChecksum,
                                    expectedRestoreEpoch, expectedSchemaGeneration, cancellationToken).ConfigureAwait(false);
                            }
                            (byte[] expectedChecksum, byte[] expectedAuthority) = RotateNegativeAuthority(
                                row.PriorValue, domain is 11 or 13, checked(resultingSemanticGeneration.Value - 1),
                                owner._options.SemanticActivationDefinitionSetChecksum.ToImmutableArray());
                            if (!CryptographicOperations.FixedTimeEquals(expectedChecksum, row.NextDigest)
                                || !CryptographicOperations.FixedTimeEquals(expectedAuthority, row.NextValue))
                                throw new InvalidDataException(BaseSemanticActivationErrorCodes.Corrupt);
                        }
                        else
                        {
                            BaseOwnedSubjectScopeEvidence logical = owner._subjectScopes!.Unprotect(prior)
                                ?? throw new InvalidDataException(BaseSubjectErrorCodes.LifecycleProviderContractInvalid);
                            BaseProtectedSubjectScope expectedDigest = owner._subjectScopes.Protect(logical, replacement);
                            BaseOwnedSubjectScopeEvidence replacementLogical = owner._subjectScopes.Unprotect(stagedReplacement)
                                ?? throw new InvalidDataException(BaseSubjectErrorCodes.LifecycleProviderContractInvalid);
                            if (!CryptographicOperations.FixedTimeEquals(expectedDigest.IndexDigest, row.NextDigest)
                                || replacementLogical.Kind != logical.Kind
                                || !string.Equals(replacementLogical.Value, logical.Value, StringComparison.Ordinal))
                                throw new InvalidDataException(BaseSubjectErrorCodes.LifecycleProviderContractInvalid);
                        }
                        byte[] canonical = RotationCanonicalBytes(domain, row.RowId, prior, stagedReplacement);
                        rolling = SHA256.HashData([.. rolling, .. canonical]);
                        checked { examined++; changed++; canonicalBytes += canonical.LongLength; stagedCount++; }
                        await using SqliteCommand update = connection.CreateCommand();
                        update.Transaction = transaction;
                        update.CommandTimeout = owner.TimeoutSeconds();
                        update.CommandText = domain switch
                        {
                            9 => $"UPDATE {table} SET seek_digest=$digest,binding_json=$authority WHERE rowid=$rowid AND seek_digest=$priorDigest;",
                            10 => $"UPDATE {table} SET authority_json=$authority WHERE rowid=$rowid AND state=1 AND authority_json=$priorAuthority;",
                            11 or 13 => $"UPDATE {table} SET authority_json=$authority WHERE rowid=$rowid AND state=2 AND authority_json=$priorAuthority;",
                            12 or 14 => $"UPDATE {table} SET authority_json=$authority WHERE rowid=$rowid AND state=3 AND authority_json=$priorAuthority;",
                            _ => $"UPDATE {table} SET scope_index_digest=$digest,protected_scope_value=$value WHERE rowid=$rowid AND scope_index_digest=$priorDigest AND protected_scope_value=$priorValue;",
                        };
                        update.Parameters.Add("$digest", SqliteType.Blob).Value = row.NextDigest;
                        update.Parameters.Add("$value", SqliteType.Blob).Value = row.NextValue;
                        update.Parameters.AddWithValue("$rowid", row.RowId);
                        update.Parameters.Add("$priorDigest", SqliteType.Blob).Value = row.PriorDigest;
                        update.Parameters.Add("$priorValue", SqliteType.Blob).Value = row.PriorValue;
                        if (domain == 9)
                        {
                            BaseSemanticActivationScopeBinding priorBinding = await ReadSemanticScopeBindingAsync(connection, transaction, row.RowId, cancellationToken).ConfigureAwait(false);
                            ValidateSemanticScopeBinding(priorBinding, expectedProtectionKeyId);
                            BaseSemanticActivationScopeBinding replacementBinding = BaseSemanticActivationEvidenceContract.CreateScopeBinding(
                                priorBinding.Kind, priorBinding.BindingId.AsSpan(), row.NextValue, row.NextDigest,
                                replacement.ToString(CultureInfo.InvariantCulture), replacement);
                            update.Parameters.Add("$authority", SqliteType.Blob).Value = JsonSerializer.SerializeToUtf8Bytes(replacementBinding, HPDBaseJsonSerializerContext.Default.BaseSemanticActivationScopeBinding);
                        }
                        else if (domain == 10)
                        {
                            BaseSemanticActivationLiveAuthority priorLive = await ReadSemanticLiveAuthorityAsync(connection, transaction, row.RowId, cancellationToken).ConfigureAwait(false);
                            await ValidateSemanticLiveRotationSourceAsync(connection, transaction, priorLive,
                                checked(resultingSemanticGeneration!.Value - 1),
                                owner._options.SemanticActivationDefinitionSetChecksum.ToImmutableArray(),
                                expectedRestoreEpoch, expectedSchemaGeneration,
                                expectedProtectionKeyId, directoryAlreadyRotated: true,
                                cancellationToken).ConfigureAwait(false);
                            BaseSemanticActivationScopeBinding replacementBinding = BaseSemanticActivationEvidenceContract.CreateScopeBinding(
                                priorLive.ScopeBinding.Kind, priorLive.ScopeBinding.BindingId.AsSpan(), row.NextValue, row.NextDigest,
                                replacement.ToString(CultureInfo.InvariantCulture), replacement);
                            if (resultingSemanticGeneration is null)
                                throw new InvalidDataException(BaseSemanticActivationErrorCodes.Corrupt);
                            BaseSemanticActivationStoreAuthority storeAuthority = BaseSemanticActivationEvidenceContract.CreateStoreAuthority(
                                priorLive.StoreAuthority.Requirement with { SemanticAuthorityGeneration = resultingSemanticGeneration.Value });
                            BaseSemanticActivationLiveAuthority replacementLive = priorLive with
                            { ScopeBinding = replacementBinding, StoreAuthority = storeAuthority, Checksum = [] };
                            replacementLive = replacementLive with { Checksum = BaseSemanticActivationEvidenceContract.LiveChecksum(replacementLive) };
                            update.Parameters.Add("$authority", SqliteType.Blob).Value = JsonSerializer.SerializeToUtf8Bytes(replacementLive, HPDBaseJsonSerializerContext.Default.BaseSemanticActivationLiveAuthority);
                            update.Parameters.Add("$priorAuthority", SqliteType.Blob).Value = JsonSerializer.SerializeToUtf8Bytes(priorLive, HPDBaseJsonSerializerContext.Default.BaseSemanticActivationLiveAuthority);
                        }
                        else if (domain is >= 11 and <= 14)
                        {
                            update.Parameters.Add("$authority", SqliteType.Blob).Value = row.NextValue;
                            update.Parameters.Add("$priorAuthority", SqliteType.Blob).Value = row.PriorValue;
                        }
                        if (await update.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false) != 1)
                            throw new InvalidDataException(BaseSubjectErrorCodes.LifecycleProviderContractInvalid);
                        if(priorTerminal is not null)
                        {
                            BaseSubjectRetirementTerminalReceipt replacementTerminal=priorTerminal with{Scope=stagedReplacement,ReceiptChecksum=string.Empty};replacementTerminal=replacementTerminal with{ReceiptChecksum=BaseSubjectRetirementRegistry.TerminalChecksum(replacementTerminal)};
                            await using SqliteCommand receiptUpdate=connection.CreateCommand();receiptUpdate.Transaction=transaction;receiptUpdate.CommandTimeout=owner.TimeoutSeconds();receiptUpdate.CommandText=$"UPDATE {owner._names.SubjectRetirementTerminals} SET receipt_checksum=$replacement WHERE rowid=$rowid AND receipt_checksum=$prior;";receiptUpdate.Parameters.AddWithValue("$replacement",replacementTerminal.ReceiptChecksum);receiptUpdate.Parameters.AddWithValue("$rowid",row.RowId);receiptUpdate.Parameters.AddWithValue("$prior",priorTerminal.ReceiptChecksum);if(await receiptUpdate.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false)!=1)throw new InvalidDataException(BaseSubjectRetirementErrorCodes.ProviderContractInvalid);
                        }
                        afterRowId = row.RowId;
                    }
                    if (rows.Count < pageSize) break;
                }

                await using SqliteCommand sourceCount = connection.CreateCommand();
                sourceCount.Transaction = transaction;
                sourceCount.CommandTimeout = owner.TimeoutSeconds();
                string predicate = domain switch
                {
                    8 => " WHERE scope_kind IS NOT NULL",
                    10 => " WHERE state=1",
                    11 or 13 => " WHERE state=2",
                    12 or 14 => " WHERE state=3",
                    _ => string.Empty,
                };
                sourceCount.CommandText = $"SELECT COUNT(*) FROM {table}{predicate};";
                long count = Convert.ToInt64(await sourceCount.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false), System.Globalization.CultureInfo.InvariantCulture);
                if (count != stagedCount)
                    throw new InvalidDataException(BaseSubjectErrorCodes.LifecycleProviderContractInvalid);
            }
            return new RotationEvidence(examined, changed, canonicalBytes, Convert.ToHexStringLower(rolling));
        }

        private async ValueTask<BaseSemanticActivationScopeBinding> ReadSemanticScopeBindingAsync(
            SqliteConnection connection, SqliteTransaction transaction, long rowId, CancellationToken cancellationToken)
        {
            await using SqliteCommand command = connection.CreateCommand();
            command.Transaction = transaction; command.CommandTimeout = owner.TimeoutSeconds();
            command.CommandText = $"SELECT scope_kind,binding_id,seek_digest,binding_json FROM {owner._names.SemanticActivationScopes} WHERE rowid=$rowid;";
            command.Parameters.AddWithValue("$rowid", rowId);
            await using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
                throw new InvalidDataException(BaseSemanticActivationErrorCodes.Corrupt);
            BaseSemanticActivationScopeBinding binding = JsonSerializer.Deserialize((byte[])reader[3],
                HPDBaseJsonSerializerContext.Default.BaseSemanticActivationScopeBinding)
                ?? throw new InvalidDataException(BaseSemanticActivationErrorCodes.Corrupt);
            if (reader.GetInt32(0) != (int)binding.Kind
                || !((byte[])reader[1]).AsSpan().SequenceEqual(binding.BindingId.AsSpan())
                || !((byte[])reader[2]).AsSpan().SequenceEqual(binding.SeekDigest.AsSpan()))
                throw new InvalidDataException(BaseSemanticActivationErrorCodes.Corrupt);
            return binding;
        }

        private async ValueTask<BaseSemanticActivationLiveAuthority> ReadSemanticLiveAuthorityAsync(
            SqliteConnection connection, SqliteTransaction transaction, long rowId, CancellationToken cancellationToken)
        {
            await using SqliteCommand command = connection.CreateCommand();
            command.Transaction = transaction; command.CommandTimeout = owner.TimeoutSeconds();
            command.CommandText = $"SELECT definition_id,binding_id,key_digest,slot_generation,authority_json FROM {owner._names.SemanticActivationSlots} WHERE rowid=$rowid AND state=1;";
            command.Parameters.AddWithValue("$rowid", rowId);
            await using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
                throw new InvalidDataException(BaseSemanticActivationErrorCodes.Corrupt);
            BaseSemanticActivationLiveAuthority live = JsonSerializer.Deserialize((byte[])reader[4],
                HPDBaseJsonSerializerContext.Default.BaseSemanticActivationLiveAuthority)
                ?? throw new InvalidDataException(BaseSemanticActivationErrorCodes.Corrupt);
            Span<byte> key = stackalloc byte[BaseSemanticActivationKeyDigest.Length]; live.KeyDigest.CopyTo(key);
            if (!string.Equals(reader.GetString(0), live.Definition.Id, StringComparison.Ordinal)
                || !((byte[])reader[1]).AsSpan().SequenceEqual(live.ScopeBinding.BindingId.AsSpan())
                || !((byte[])reader[2]).AsSpan().SequenceEqual(key)
                || reader.GetInt64(3) != live.SlotGeneration)
                throw new InvalidDataException(BaseSemanticActivationErrorCodes.Corrupt);
            return live;
        }

        private static void ValidateSemanticScopeBinding(
            BaseSemanticActivationScopeBinding binding, string expectedProtectionKeyId)
        {
            if (binding.BindingId.Length != 32 || binding.SeekDigest.Length != 32
                || binding.ProtectedCanonicalScope.IsDefaultOrEmpty
                || binding.ProtectionKeyVersion < 1
                || !string.Equals(binding.ProtectionKeyId, expectedProtectionKeyId, StringComparison.Ordinal)
                || !CryptographicOperations.FixedTimeEquals(binding.Checksum.AsSpan(),
                    BaseSemanticActivationEvidenceContract.ScopeBindingChecksum(binding).AsSpan()))
                throw new InvalidDataException(BaseSemanticActivationErrorCodes.Corrupt);
        }

        private async ValueTask ValidateSemanticLiveRotationSourceAsync(
            SqliteConnection connection,
            SqliteTransaction transaction,
            BaseSemanticActivationLiveAuthority live,
            long expectedGeneration,
            ImmutableArray<byte> definitionSet,
            long expectedRestoreEpoch,
            long expectedSchemaGeneration,
            string expectedProtectionKeyId,
            bool directoryAlreadyRotated,
            CancellationToken cancellationToken)
        {
            owner.ValidateSemanticStore(live.StoreAuthority, expectedGeneration, definitionSet.ToArray(),
                expectedRestoreEpoch, expectedSchemaGeneration);
            await using (SqliteCommand installed = connection.CreateCommand())
            {
                installed.Transaction = transaction; installed.CommandTimeout = owner.TimeoutSeconds();
                installed.CommandText = $"SELECT COUNT(*) FROM {owner._names.SemanticActivationDefinitions} WHERE definition_id=$id AND definition_version=$version AND definition_checksum=$checksum AND execution_enabled=1;";
                installed.Parameters.AddWithValue("$id", live.Definition.Id);
                installed.Parameters.AddWithValue("$version", live.Definition.Version);
                installed.Parameters.Add("$checksum", SqliteType.Blob).Value = live.Definition.Checksum.ToArray();
                if (Convert.ToInt64(await installed.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false), CultureInfo.InvariantCulture) != 1)
                    throw new InvalidDataException(BaseSemanticActivationErrorCodes.Corrupt);
            }
            ValidateSemanticScopeBinding(live.ScopeBinding, expectedProtectionKeyId);
            if (!CryptographicOperations.FixedTimeEquals(live.Checksum.AsSpan(),
                    BaseSemanticActivationEvidenceContract.LiveChecksum(live).AsSpan()))
                throw new InvalidDataException(BaseSemanticActivationErrorCodes.Corrupt);
            if (directoryAlreadyRotated)
            {
                await using SqliteCommand staged = connection.CreateCommand();
                staged.Transaction = transaction; staged.CommandTimeout = owner.TimeoutSeconds();
                staged.CommandText = $"""
SELECT st.prior_digest,st.prior_value
FROM {owner._names.SemanticActivationScopes} s
JOIN {owner._names.SubjectLifecycleScopeStage} st ON st.domain_ordinal=9 AND st.source_rowid=s.rotation_id
WHERE s.binding_id=$binding;
""";
                staged.Parameters.Add("$binding", SqliteType.Blob).Value = live.ScopeBinding.BindingId.ToArray();
                await using SqliteDataReader reader = await staged.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
                if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false)
                    || !CryptographicOperations.FixedTimeEquals((byte[])reader.GetValue(0), live.ScopeBinding.SeekDigest.AsSpan())
                    || !CryptographicOperations.FixedTimeEquals((byte[])reader.GetValue(1), live.ScopeBinding.ProtectedCanonicalScope.AsSpan()))
                    throw new InvalidDataException(BaseSemanticActivationErrorCodes.Corrupt);
            }
            else
            {
                BaseSemanticActivationScopeBinding directory = await ReadSemanticScopeBindingByIdAsync(
                    connection, transaction, live.ScopeBinding.BindingId, cancellationToken).ConfigureAwait(false);
                if (!ScopeBindingsEqual(directory, live.ScopeBinding))
                    throw new InvalidDataException(BaseSemanticActivationErrorCodes.Corrupt);
            }
            await owner.RequireLiveActivationCorrespondenceAsync(connection, transaction, live, cancellationToken,
                    requireCurrentScopeBinding: !directoryAlreadyRotated)
                .ConfigureAwait(false);
        }

        private async ValueTask<BaseSemanticActivationScopeBinding> ReadSemanticScopeBindingByIdAsync(
            SqliteConnection connection, SqliteTransaction transaction, ImmutableArray<byte> bindingId,
            CancellationToken cancellationToken)
        {
            await using SqliteCommand command = connection.CreateCommand();
            command.Transaction = transaction; command.CommandTimeout = owner.TimeoutSeconds();
            command.CommandText = $"SELECT binding_json FROM {owner._names.SemanticActivationScopes} WHERE binding_id=$binding;";
            command.Parameters.Add("$binding", SqliteType.Blob).Value = bindingId.ToArray();
            return JsonSerializer.Deserialize((byte[])(await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false)
                ?? throw new InvalidDataException(BaseSemanticActivationErrorCodes.Corrupt)),
                HPDBaseJsonSerializerContext.Default.BaseSemanticActivationScopeBinding)
                ?? throw new InvalidDataException(BaseSemanticActivationErrorCodes.Corrupt);
        }

        private async ValueTask<SemanticRecoveryRow> ReadSemanticRecoveryRowByRotationIdAsync(
            SqliteConnection connection, SqliteTransaction transaction, long rotationId,
            CancellationToken cancellationToken)
        {
            await using SqliteCommand command = connection.CreateCommand();
            command.Transaction = transaction; command.CommandTimeout = owner.TimeoutSeconds();
            command.CommandText = $"""
SELECT definition_id,binding_id,key_digest,state,slot_generation,authority_json,
 receipt_scope,receipt_operation,receipt_key,receipt_fingerprint,receipt_structural_digest,
 receipt_result_json,receipt_authority_checksum,receipt_slot_authority_json
FROM {owner._names.SemanticActivationRecoveryFloors} WHERE rotation_id=$rotation;
""";
            command.Parameters.AddWithValue("$rotation", rotationId);
            await using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
                throw new InvalidDataException(BaseSemanticActivationErrorCodes.Corrupt);
            return new SemanticRecoveryRow(reader.GetString(0), (byte[])reader[1], (byte[])reader[2], reader.GetInt32(3),
                reader.GetInt64(4), (byte[])reader[5], reader.IsDBNull(6) ? null : reader.GetString(6),
                reader.IsDBNull(7) ? null : reader.GetString(7), reader.IsDBNull(8) ? null : reader.GetString(8),
                reader.IsDBNull(9) ? null : (byte[])reader[9], reader.IsDBNull(10) ? null : (byte[])reader[10],
                reader.IsDBNull(11) ? null : (byte[])reader[11], reader.IsDBNull(12) ? null : (byte[])reader[12],
                reader.IsDBNull(13) ? null : (byte[])reader[13]);
        }

        private async ValueTask ValidateSemanticNegativeSlotRotationSourceAsync(
            SqliteConnection connection, SqliteTransaction transaction, long rotationId, bool retired,
            long expectedGeneration, byte[] definitionSet, long expectedRestoreEpoch, long expectedSchemaGeneration,
            CancellationToken cancellationToken)
        {
            await using SqliteCommand slot = connection.CreateCommand();
            slot.Transaction = transaction; slot.CommandTimeout = owner.TimeoutSeconds();
            slot.CommandText = $"SELECT definition_id,binding_id,key_digest,state,slot_generation,authority_json FROM {owner._names.SemanticActivationSlots} WHERE rotation_id=$rotation AND state=$state;";
            slot.Parameters.AddWithValue("$rotation", rotationId);
            slot.Parameters.AddWithValue("$state", retired ? 2 : 3);
            await using SqliteDataReader reader = await slot.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
                throw new InvalidDataException(BaseSemanticActivationErrorCodes.Corrupt);
            var current = new SemanticRecoveryRow(reader.GetString(0), (byte[])reader[1], (byte[])reader[2],
                reader.GetInt32(3), reader.GetInt64(4), (byte[])reader[5], null, null, null, null, null, null, null, null);
            await reader.DisposeAsync().ConfigureAwait(false);
            owner.ValidateSemanticRecoveryRow(current, expectedGeneration, definitionSet, expectedRestoreEpoch, expectedSchemaGeneration);
            SemanticRecoveryRow floor = await ReadSemanticRecoveryRowByKeyAsync(connection, transaction,
                current.DefinitionId, current.BindingId, current.KeyDigest, cancellationToken).ConfigureAwait(false);
            if (floor.State != current.State || floor.SlotGeneration != current.SlotGeneration
                || !CryptographicOperations.FixedTimeEquals(floor.AuthorityJson, current.AuthorityJson))
                throw new InvalidDataException(BaseSemanticActivationErrorCodes.Corrupt);
            owner.ValidateSemanticRecoveryRow(floor, expectedGeneration, definitionSet, expectedRestoreEpoch, expectedSchemaGeneration);
            await owner.ValidateSemanticRecoveryDependenciesAsync(connection, transaction, floor, cancellationToken)
                .ConfigureAwait(false);
        }

        private async ValueTask ValidateSemanticRecoveryFloorRotationSourceAsync(
            SqliteConnection connection, SqliteTransaction transaction, long rotationId, bool retired,
            long expectedGeneration, byte[] definitionSet, long expectedRestoreEpoch, long expectedSchemaGeneration,
            CancellationToken cancellationToken, bool slotAlreadyRotated = false)
        {
            SemanticRecoveryRow floor = await ReadSemanticRecoveryRowByRotationIdAsync(
                connection, transaction, rotationId, cancellationToken).ConfigureAwait(false);
            if (floor.State != (retired ? 2 : 3))
                throw new InvalidDataException(BaseSemanticActivationErrorCodes.Corrupt);
            await using SqliteCommand slot = connection.CreateCommand();
            slot.Transaction = transaction; slot.CommandTimeout = owner.TimeoutSeconds();
            slot.CommandText = $"SELECT rotation_id FROM {owner._names.SemanticActivationSlots} WHERE definition_id=$definition AND binding_id=$binding AND key_digest=$key AND state=$state;";
            slot.Parameters.AddWithValue("$definition", floor.DefinitionId);
            slot.Parameters.Add("$binding", SqliteType.Blob).Value = floor.BindingId;
            slot.Parameters.Add("$key", SqliteType.Blob).Value = floor.KeyDigest;
            slot.Parameters.AddWithValue("$state", floor.State);
            object? value = await slot.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
            if (value is null)
                throw new InvalidDataException(BaseSemanticActivationErrorCodes.Corrupt);
            long slotRotationId = Convert.ToInt64(value, CultureInfo.InvariantCulture);
            if (!slotAlreadyRotated)
            {
                await ValidateSemanticNegativeSlotRotationSourceAsync(connection, transaction,
                    slotRotationId, retired, expectedGeneration, definitionSet,
                    expectedRestoreEpoch, expectedSchemaGeneration, cancellationToken).ConfigureAwait(false);
                return;
            }
            owner.ValidateSemanticRecoveryRow(floor, expectedGeneration, definitionSet,
                expectedRestoreEpoch, expectedSchemaGeneration);
            await owner.ValidateSemanticRecoveryDependenciesAsync(connection, transaction, floor, cancellationToken)
                .ConfigureAwait(false);
            (byte[] expectedChecksum, byte[] expectedAuthority) = RotateNegativeAuthority(
                floor.AuthorityJson, retired, expectedGeneration, definitionSet.ToImmutableArray());
            await using SqliteCommand current = connection.CreateCommand();
            current.Transaction = transaction; current.CommandTimeout = owner.TimeoutSeconds();
            current.CommandText = $"SELECT authority_json FROM {owner._names.SemanticActivationSlots} WHERE rotation_id=$rotation;";
            current.Parameters.AddWithValue("$rotation", slotRotationId);
            if (await current.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false) is not byte[] currentAuthority
                || !CryptographicOperations.FixedTimeEquals(currentAuthority, expectedAuthority)
                || !CryptographicOperations.FixedTimeEquals(ReadNegativeAuthorityChecksum(currentAuthority, retired), expectedChecksum))
                throw new InvalidDataException(BaseSemanticActivationErrorCodes.Corrupt);
        }

        private async ValueTask<SemanticRecoveryRow> ReadSemanticRecoveryRowByKeyAsync(
            SqliteConnection connection, SqliteTransaction transaction, string definitionId, byte[] bindingId,
            byte[] keyDigest, CancellationToken cancellationToken)
        {
            await using SqliteCommand command = connection.CreateCommand();
            command.Transaction = transaction; command.CommandTimeout = owner.TimeoutSeconds();
            command.CommandText = $"""
SELECT definition_id,binding_id,key_digest,state,slot_generation,authority_json,
 receipt_scope,receipt_operation,receipt_key,receipt_fingerprint,receipt_structural_digest,
 receipt_result_json,receipt_authority_checksum,receipt_slot_authority_json
FROM {owner._names.SemanticActivationRecoveryFloors}
WHERE definition_id=$definition AND binding_id=$binding AND key_digest=$key;
""";
            command.Parameters.AddWithValue("$definition", definitionId);
            command.Parameters.Add("$binding", SqliteType.Blob).Value = bindingId;
            command.Parameters.Add("$key", SqliteType.Blob).Value = keyDigest;
            await using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
                throw new InvalidDataException(BaseSemanticActivationErrorCodes.Corrupt);
            return new SemanticRecoveryRow(reader.GetString(0), (byte[])reader[1], (byte[])reader[2], reader.GetInt32(3),
                reader.GetInt64(4), (byte[])reader[5], reader.IsDBNull(6) ? null : reader.GetString(6),
                reader.IsDBNull(7) ? null : reader.GetString(7), reader.IsDBNull(8) ? null : reader.GetString(8),
                reader.IsDBNull(9) ? null : (byte[])reader[9], reader.IsDBNull(10) ? null : (byte[])reader[10],
                reader.IsDBNull(11) ? null : (byte[])reader[11], reader.IsDBNull(12) ? null : (byte[])reader[12],
                reader.IsDBNull(13) ? null : (byte[])reader[13]);
        }

        private async ValueTask<BaseSubjectRetirementTerminalReceipt> ReadRetirementTerminalAsync(SqliteConnection connection,SqliteTransaction transaction,long rowId,BaseProtectedSubjectScope scope,CancellationToken token)
        {
            await using SqliteCommand command=connection.CreateCommand();command.Transaction=transaction;command.CommandTimeout=owner.TimeoutSeconds();command.CommandText=$"SELECT contract_id,contract_version,subject_id,authority_epoch,incarnation,tombstone_sequence,authorizing_state,final_barrier_generation,final_barrier_checksum,required_consumer_set_checksum,acknowledgements_blob,retired_position,purged_at,receipt_checksum FROM {owner._names.SubjectRetirementTerminals} WHERE rowid=$rowid;";command.Parameters.AddWithValue("$rowid",rowId);await using SqliteDataReader reader=await command.ExecuteReaderAsync(token).ConfigureAwait(false);if(!await reader.ReadAsync(token).ConfigureAwait(false))throw new InvalidDataException(BaseSubjectRetirementErrorCodes.ProviderContractInvalid);
            BaseExportedSubjectDefinition definition=owner._options.ExportedSubjects.Single(value=>value.Id==reader.GetString(0)&&value.Version==reader.GetInt32(1));ImmutableArray<BaseSubjectTerminalAcknowledgement> acknowledgements=ParseTerminalAcknowledgements((byte[])reader.GetValue(10));var receipt=new BaseSubjectRetirementTerminalReceipt{ContractId=reader.GetString(0),ContractVersion=reader.GetInt32(1),SubjectId=BaseSubjectId.Create(reader.GetString(2),definition.SubjectIdKind,definition.MaximumSubjectIdUtf8Bytes),Scope=scope,AuthorityEpoch=new((byte[])reader.GetValue(3)),Incarnation=new((byte[])reader.GetValue(4)),TombstoneSequence=reader.GetInt64(5),AuthorizingState=(BaseSubjectRetirementBarrierState)reader.GetInt32(6),FinalBarrierGeneration=reader.GetInt64(7),FinalBarrierChecksum=reader.GetString(8),RequiredConsumerSetChecksum=reader.GetString(9),Acknowledgements=acknowledgements,RetiredPosition=new(reader.GetInt64(11)),PurgedAtUtc=DateTimeOffset.Parse(reader.GetString(12),CultureInfo.InvariantCulture),ReceiptChecksum=reader.GetString(13)};if(!string.Equals(BaseSubjectRetirementRegistry.TerminalChecksum(receipt),receipt.ReceiptChecksum,StringComparison.Ordinal))throw new InvalidDataException(BaseSubjectRetirementErrorCodes.ProviderContractInvalid);return receipt;
        }

        private static ImmutableArray<BaseSubjectTerminalAcknowledgement> ParseTerminalAcknowledgements(byte[] bytes)
        {
            if(bytes.Length==0)return [];var builder=ImmutableArray.CreateBuilder<BaseSubjectTerminalAcknowledgement>();foreach(string line in Encoding.UTF8.GetString(bytes).Split('\n',StringSplitOptions.RemoveEmptyEntries)){string[] fields=line.Split('\0');if(fields.Length!=6||!int.TryParse(fields[1],NumberStyles.None,CultureInfo.InvariantCulture,out int version)||!long.TryParse(fields[3],NumberStyles.None,CultureInfo.InvariantCulture,out long sequence)||!int.TryParse(fields[4],NumberStyles.None,CultureInfo.InvariantCulture,out int disposition)||!long.TryParse(fields[5],NumberStyles.None,CultureInfo.InvariantCulture,out long position))throw new InvalidDataException(BaseSubjectRetirementErrorCodes.ProviderContractInvalid);builder.Add(new(){ConsumerId=fields[0],ConsumerVersion=version,ConsumerChecksum=fields[2],ThroughSubjectSequence=sequence,Disposition=(BaseSubjectAcknowledgementDisposition)disposition,AcknowledgedPosition=new(position)});}return builder.MoveToImmutable();
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

        private static void ValidateRotationProgress(RotationProgress progress, SqliteLifecycleMaintenanceRequest request)
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
            SqliteLifecycleMaintenanceRequest request,
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
                || receipt?.Kind != BaseAtomicReceiptResultKind.SubjectLifecycleMaintenance || receipt.SubjectLifecycleMaintenance is null
                || (authority.Retirement is null)!=(receipt.SubjectRetirement is null)
                || receipt.SubjectRetirement is not null && receipt.SubjectRetirement.Operation!=BaseSubjectRetirementReceiptOperation.Maintenance)
                return Failure(BaseMutationRequestErrorCodes.FingerprintConflict, OperationStatus.Conflict, ErrorCategory.Conflict);
            if(receipt.SubjectRetirement?.Maintenance is { } retirement)
            {
                _retirementExamined=retirement.ExaminedCount;_retirementChanged=retirement.ChangedCount;_publishedBarrierControlGeneration=retirement.PublishedBarrierControlGeneration;
            }
            return OperationResults.Ok(receipt.SubjectLifecycleMaintenance with
            {
                RollingChecksum = new string(receipt.SubjectLifecycleMaintenance.RollingChecksum.AsSpan()),
                Duplicate = true,
            });
        }

        private async ValueTask InsertMaintenanceReceiptAsync(
            SqliteConnection connection,
            SqliteTransaction transaction,
            SqliteLifecycleMaintenanceRequest request,
            BaseSubjectLifecycleMaintenanceResult result,
            CancellationToken cancellationToken)
        {
            var receipt = new BaseAtomicReceiptResult
            {
                Kind = BaseAtomicReceiptResultKind.SubjectLifecycleMaintenance,
                Mutations = [],
                SubjectLifecycleMaintenance = result with { RollingChecksum = new string(result.RollingChecksum.AsSpan()), Duplicate = false },
                SubjectRetirement = authority.Retirement is null ? null : new BaseSubjectRetirementReceiptResult
                {
                    Operation=BaseSubjectRetirementReceiptOperation.Maintenance,
                    Maintenance=new BaseSubjectRetirementMaintenanceResult
                    {
                        Kind=authority.Retirement.Kind,Outcome=BaseSubjectRetirementMutationOutcome.Applied,
                        ExaminedCount=_retirementExamined,ChangedCount=_retirementChanged,CanonicalBytes=result.CanonicalBytes,
                        RollingChecksum=new string(result.RollingChecksum.AsSpan()),PublishedBarrierControlGeneration=_publishedBarrierControlGeneration,
                    },
                },
            };
            byte[] bytes = System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(BaseAtomicReceiptWire.From(receipt), HPDBaseJsonSerializerContext.Default.BaseAtomicReceiptWire);
            if (bytes.Length > 16_384) throw new InvalidOperationException(BaseSubjectErrorCodes.LifecycleProviderContractInvalid);
            await using SqliteCommand command = connection.CreateCommand(); command.Transaction = transaction; command.CommandTimeout = owner.TimeoutSeconds();
            command.CommandText = $"INSERT INTO {owner._names.OperationReceipts}(scope,operation,idempotency_key,fingerprint,structural_digest,result_json,result_format_version,schema_generation,store_instance_id,committed_at,expires_at) VALUES($scope,$operation,$key,$fingerprint,$structural,$result,2,$generation,$store,$committed,$expires);";
            command.Parameters.AddWithValue("$scope", request.Identity.Scope); command.Parameters.AddWithValue("$operation", request.Identity.Operation); command.Parameters.AddWithValue("$key", request.Identity.IdempotencyKey); command.Parameters.Add("$fingerprint", SqliteType.Blob).Value=request.Identity.Fingerprint.ToArray(); command.Parameters.Add("$structural", SqliteType.Blob).Value=request.PlanChecksum; command.Parameters.Add("$result", SqliteType.Blob).Value=bytes; command.Parameters.AddWithValue("$generation",owner._schemaGeneration); command.Parameters.AddWithValue("$store",owner.CurrentStoreInstanceId); command.Parameters.AddWithValue("$committed",owner._timeProvider.GetUtcNow().ToString("O",System.Globalization.CultureInfo.InvariantCulture)); command.Parameters.AddWithValue("$expires",owner._timeProvider.GetUtcNow().AddDays(30).ToString("O",System.Globalization.CultureInfo.InvariantCulture));
            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        private async ValueTask<OperationResult<BaseSubjectLifecycleMaintenanceResult>> ExecuteCoreAsync(
            SqliteConnection connection,
            SqliteTransaction transaction,
            SqliteLifecycleMaintenanceRequest request,
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

        private async ValueTask<long?> MarkOvertakenAsync(SqliteConnection connection, SqliteTransaction transaction, SqliteLifecycleMaintenanceRequest request, List<string> changed, CancellationToken cancellationToken)
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

    private sealed record SqliteLifecycleMaintenanceRequest
    {
        public required int FormatVersion { get; init; }
        public required BaseSubjectLifecycleMaintenanceKind Kind { get; init; }
        public string? ContractId { get; init; }
        public int? ContractVersion { get; init; }
        public string? ConsumerId { get; init; }
        public int? ConsumerVersion { get; init; }
        public BaseOwnedSubjectScopeEvidence? Scope { get; init; }
        public BaseSubjectLifecycleOrderingBoundary? RetainedFrom { get; init; }
        public required BaseMutationRequestIdentity Identity { get; init; }
        public required byte[] PlanChecksum { get; init; }
        public required long ExpectedStoreGeneration { get; init; }
        public required long ExpectedSchemaGeneration { get; init; }
        public required long ExpectedRestoreEpoch { get; init; }
        public required long ExpectedDeliveryEpoch { get; init; }
        public long? ExpectedProjectionGeneration { get; init; }
        public required long ExpectedScopeProtectionGeneration { get; init; }
        public required string ExpectedScopeProtectionKeyId { get; init; }
        public string? ReplacementScopeProtectionKeyId { get; init; }
        public long? ExpectedSemanticActivationAuthorityGeneration { get; init; }
        public ImmutableArray<byte> ExpectedSemanticActivationDefinitionSetChecksum { get; init; }
        public byte[]? LastCanonicalKey { get; init; }
        public required int PageSize { get; init; }
        public required TimeSpan OperationTimeout { get; init; }
        public required TimeSpan CommitCompletionTimeout { get; init; }
        public BaseSubjectRetirementMaintenancePlan? Retirement { get; init; }

        public static SqliteLifecycleMaintenanceRequest From(BaseSubjectAuthorityMaintenanceExecutionRequest request) => new()
        {
            FormatVersion = 1,
            Kind = request.Lifecycle.Kind,
            ContractId = request.Lifecycle.ContractId,
            ContractVersion = request.Lifecycle.ContractVersion,
            ConsumerId = request.Lifecycle.ConsumerId,
            ConsumerVersion = request.Lifecycle.ConsumerVersion,
            Scope = request.Lifecycle.Scope,
            RetainedFrom = request.Lifecycle.RetainedFrom,
            Identity = request.Identity,
            PlanChecksum = request.CombinedPlanChecksum.ToArray(),
            ExpectedStoreGeneration = request.ExpectedStoreGeneration,
            ExpectedSchemaGeneration = request.ExpectedSchemaGeneration,
            ExpectedRestoreEpoch = request.ExpectedRestoreEpoch,
            ExpectedDeliveryEpoch = request.Lifecycle.ExpectedDeliveryEpoch ?? 1,
            ExpectedProjectionGeneration = request.Lifecycle.ExpectedProjectionGeneration,
            ExpectedScopeProtectionGeneration = request.ExpectedScopeProtectionGeneration,
            ExpectedScopeProtectionKeyId = request.ExpectedScopeProtectionKeyId,
            ReplacementScopeProtectionKeyId = request.ReplacementScopeProtectionKeyId,
            ExpectedSemanticActivationAuthorityGeneration = request.ExpectedSemanticActivationAuthorityGeneration,
            ExpectedSemanticActivationDefinitionSetChecksum = request.ExpectedSemanticActivationDefinitionSetChecksum,
            LastCanonicalKey = null,
            PageSize = request.PageSize,
            OperationTimeout = request.OperationTimeout,
            CommitCompletionTimeout = request.CommitCompletionTimeout,
            Retirement = request.Retirement,
        };
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
