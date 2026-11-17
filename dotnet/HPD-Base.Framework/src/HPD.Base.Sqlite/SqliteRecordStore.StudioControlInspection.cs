using System.Collections.Immutable;
using System.Globalization;
using System.Text.Json;
using Microsoft.Data.Sqlite;

namespace HPD.Base.Sqlite;

public sealed partial class SqliteRecordStore
{
    /// <inheritdoc />
    public async ValueTask<OperationResult<StudioControlInspectionPage>> ReadStudioControlFactsAsync(
        StudioControlInspectionRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (!StudioControlInspectionContract.IsValid(request) || !ValidInspectionIdentity(request))
            return InspectionFailure("base.studio.control.invalid", ErrorCategory.Validation);

        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(request.Limits.Deadline);
        try
        {
            await using SqliteConnection connection = await _connections.OpenAsync(deadline.Token).ConfigureAwait(false);
            InspectedFacts items = request.Kind switch
            {
                StudioControlFactKind.AtomicReceipt => await ReadAtomicReceiptFactsAsync(connection, request, deadline.Token).ConfigureAwait(false),
                StudioControlFactKind.ActivationReceipt => await ReadActivationReceiptFactsAsync(connection, request, deadline.Token).ConfigureAwait(false),
                StudioControlFactKind.Activation => await ReadActivationFactsAsync(connection, request, deadline.Token).ConfigureAwait(false),
                StudioControlFactKind.Schedule => await ReadScheduleFactsAsync(connection, request, deadline.Token).ConfigureAwait(false),
                StudioControlFactKind.Occurrence => await ReadOccurrenceFactsAsync(connection, request, deadline.Token).ConfigureAwait(false),
                StudioControlFactKind.Executor => await ReadExecutorFactsAsync(connection, request, deadline.Token).ConfigureAwait(false),
                StudioControlFactKind.Effect => await ReadEffectFactsAsync(connection, request, deadline.Token).ConfigureAwait(false),
                StudioControlFactKind.Quarantine => await ReadQuarantineFactsAsync(connection, request, deadline.Token).ConfigureAwait(false),
                StudioControlFactKind.SubjectContract => await ReadSubjectContractFactsAsync(connection, request, deadline.Token).ConfigureAwait(false),
                StudioControlFactKind.Subject => await ReadSubjectFactsAsync(connection, request, deadline.Token).ConfigureAwait(false),
                StudioControlFactKind.LifecycleConsumer => await ReadLifecycleConsumerFactsAsync(connection, request, deadline.Token).ConfigureAwait(false),
                StudioControlFactKind.LifecycleCheckpoint => await ReadLifecycleCheckpointFactsAsync(connection, request, deadline.Token).ConfigureAwait(false),
                StudioControlFactKind.RetirementBarrier => await ReadRetirementBarrierFactsAsync(connection, request, deadline.Token).ConfigureAwait(false),
                _ => throw new InvalidOperationException("The control fact kind is unsupported."),
            };
            long rowsRead = items.RowsRead;
            bool more = items.Count > request.Take;
            if (more) items.RemoveAt(items.Count - 1);
            ImmutableArray<StudioControlFact> frozen = [.. items];
            long bytes = frozen.Sum(StudioControlInspectionContract.Measure);
            if (rowsRead > request.Limits.MaximumRowsRead || bytes > request.Limits.MaximumEvidenceBytes ||
                bytes > request.Limits.MaximumTransientBytes)
                return InspectionFailure("base.studio.control.budgetExceeded", ErrorCategory.Validation);
            string? next = more && frozen.Length != 0 ? frozen[^1].Identity : null;
            return OperationResults.Ok(new StudioControlInspectionPage
            {
                Items = frozen, NextIdentity = next, RowsRead = rowsRead, EvidenceBytes = bytes, TransientBytes = bytes,
                PageChecksum = StudioControlInspectionContract.PageChecksum(frozen, next, rowsRead, bytes, bytes),
            });
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return InspectionFailure("base.studio.control.deadline", ErrorCategory.Store);
        }
        catch (InvalidDataException)
        {
            return InspectionFailure("base.studio.control.corruptEvidence", ErrorCategory.Store);
        }
        catch (SqliteException)
        {
            return InspectionFailure("base.studio.control.unavailable", ErrorCategory.Store);
        }
    }

    private async ValueTask<InspectedFacts> ReadLifecycleConsumerFactsAsync(SqliteConnection connection,
        StudioControlInspectionRequest request, CancellationToken cancellationToken)
    {
        long deliveryEpoch;
        await using (SqliteCommand epoch = connection.CreateCommand())
        { epoch.CommandText = $"SELECT CAST(value AS INTEGER) FROM {_names.ProviderState} WHERE key='subject_lifecycle_delivery_epoch';";
          deliveryEpoch = Convert.ToInt64(await epoch.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false), CultureInfo.InvariantCulture); }
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = $"SELECT consumer_id,consumer_version,consumer_checksum,contract_id,contract_version,projection_generation,published_graph_generation FROM {_names.SubjectLifecycleConsumers} WHERE state=0 ORDER BY consumer_id,consumer_version LIMIT $take;";
        command.Parameters.AddWithValue("$take", request.Limits.MaximumRowsRead + 1); var facts = new InspectedFacts();
        await using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false)) { facts.RowsRead++; string id=reader.GetString(0); int version=reader.GetInt32(1);
            facts.Add(WithChecksum(new StudioLifecycleConsumerFact { Identity=StudioControlInspectionContract.LifecycleConsumerIdentity(id,version),ConsumerId=id,ConsumerVersion=version,
                ConsumerChecksum=reader.GetString(2),ContractId=reader.GetString(3),ContractVersion=reader.GetInt32(4),ProjectionGeneration=reader.GetInt64(5),PublishedGraphGeneration=reader.GetInt64(6),DeliveryEpoch=deliveryEpoch,FactChecksum=[] })); }
        return CanonicalPage(facts,request);
    }

    private async ValueTask<InspectedFacts> ReadLifecycleCheckpointFactsAsync(SqliteConnection connection,
        StudioControlInspectionRequest request, CancellationToken cancellationToken)
    {
        await using SqliteCommand command=connection.CreateCommand(); command.CommandText=$"SELECT consumer_id,consumer_version,contract_id,contract_version,projection_generation,scope_index_digest,through_position,through_subject_id,through_authority_epoch,through_incarnation,through_sequence,checkpoint_generation,state FROM {_names.SubjectLifecycleCheckpoints} ORDER BY consumer_id,consumer_version,scope_kind,scope_index_digest LIMIT $take;";
        command.Parameters.AddWithValue("$take",request.Limits.MaximumRowsRead+1);var facts=new InspectedFacts();await using SqliteDataReader reader=await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while(await reader.ReadAsync(cancellationToken).ConfigureAwait(false)){facts.RowsRead++;string consumer=reader.GetString(0);int version=reader.GetInt32(1);string scope=Convert.ToHexString((byte[])reader[5]).ToLowerInvariant();string through=reader.IsDBNull(6)?"none":$"{reader.GetInt64(6)}:{reader.GetString(7)}:{new BaseSubjectAuthorityEpoch((byte[])reader[8]).ToBase64Url()}:{new BaseSubjectIncarnation((byte[])reader[9]).ToBase64Url()}:{reader.GetInt64(10)}";
            facts.Add(WithChecksum(new StudioLifecycleCheckpointFact{Identity=StudioControlInspectionContract.LifecycleCheckpointIdentity(consumer,version,scope),ConsumerId=consumer,ConsumerVersion=version,ContractId=reader.GetString(2),ContractVersion=reader.GetInt32(3),ProjectionGeneration=reader.GetInt64(4),ProtectedScopeIdentity=scope,ThroughBoundary=through,CheckpointGeneration=reader.GetInt64(11),Overtaken=reader.GetInt32(12)!=0,FactChecksum=[]}));}
        return CanonicalPage(facts,request);
    }

    private async ValueTask<InspectedFacts> ReadRetirementBarrierFactsAsync(SqliteConnection connection,
        StudioControlInspectionRequest request, CancellationToken cancellationToken)
    {
        await using SqliteCommand command=connection.CreateCommand();command.CommandText=$"SELECT contract_id,contract_version,subject_id,authority_epoch,incarnation,tombstone_sequence,required_consumer_set_checksum,deadline_at,state,generation,barrier_checksum FROM {_names.SubjectRetirementBarriers} ORDER BY contract_id,contract_version,subject_id,authority_epoch,incarnation LIMIT $take;";
        command.Parameters.AddWithValue("$take",request.Limits.MaximumRowsRead+1);var facts=new InspectedFacts();await using SqliteDataReader reader=await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while(await reader.ReadAsync(cancellationToken).ConfigureAwait(false)){facts.RowsRead++;string contract=reader.GetString(0);int version=reader.GetInt32(1);string subject=reader.GetString(2);string epoch=new BaseSubjectAuthorityEpoch((byte[])reader[3]).ToBase64Url();string incarnation=new BaseSubjectIncarnation((byte[])reader[4]).ToBase64Url();
            facts.Add(WithChecksum(new StudioRetirementBarrierFact{Identity=StudioControlInspectionContract.RetirementBarrierIdentity(contract,version,subject,epoch,incarnation),ContractId=contract,ContractVersion=version,ProtectedSubjectIdentity=subject,AuthorityEpoch=epoch,Incarnation=incarnation,TombstoneSequence=reader.GetInt64(5),RequiredConsumerSetChecksum=reader.GetString(6),DeadlineUtc=ParseUtc(reader.GetString(7)),State=(BaseSubjectRetirementBarrierState)reader.GetInt32(8),Generation=reader.GetInt64(9),BarrierChecksum=reader.GetString(10),FactChecksum=[]}));}
        return CanonicalPage(facts,request);
    }

    private async ValueTask<InspectedFacts> ReadQuarantineFactsAsync(SqliteConnection connection,
        StudioControlInspectionRequest request, CancellationToken cancellationToken)
    {
        await Task.CompletedTask.ConfigureAwait(false);
        return new InspectedFacts();
    }

    private async ValueTask<InspectedFacts> ReadAtomicReceiptFactsAsync(SqliteConnection connection,
        StudioControlInspectionRequest request, CancellationToken cancellationToken)
    {
        (string Scope, string Operation, string Key)? exact = request.Identity is null ? null : DecodeAtomic(request.Identity);
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = $"SELECT scope,operation,idempotency_key,fingerprint,structural_digest,result_json,expires_at FROM {_names.OperationReceipts} " +
            (exact is not null ? "WHERE scope=$scope AND operation=$operation AND idempotency_key=$key " : "") +
            "ORDER BY scope,operation,idempotency_key LIMIT $take;";
        BindTriple(command, exact); command.Parameters.AddWithValue("$take", exact is null ? request.Limits.MaximumRowsRead + 1 : 1);
        var facts = new InspectedFacts();
        await using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            facts.RowsRead++;
            string identity = StudioControlInspectionContract.AtomicIdentity(reader.GetString(0), reader.GetString(1), reader.GetString(2));
            byte[] fingerprint = Blob32(reader, 3); byte[] structural = Blob32(reader, 4); byte[] result = (byte[])reader[5];
            BaseAtomicReceiptWire? wire = JsonSerializer.Deserialize(result, HPDBaseJsonSerializerContext.Default.BaseAtomicReceiptWire);
            if (wire is null) throw new InvalidDataException();
            DateTimeOffset expires = ParseUtc(reader.GetString(6));
            BaseAtomicReceiptResultKind kind = wire.Materialize().Kind;
            facts.Add(WithChecksum(new StudioAtomicReceiptFact { Identity = identity, ResultKind = kind, ExpiresAtUtc = expires,
                RequestFingerprint = [.. fingerprint], StructuralDigest = [.. structural], FactChecksum = [] }));
        }
        return CanonicalPage(facts, request);
    }

    private async ValueTask<InspectedFacts> ReadActivationReceiptFactsAsync(SqliteConnection connection,
        StudioControlInspectionRequest request, CancellationToken cancellationToken)
    {
        await using SqliteCommand command = connection.CreateCommand();
        string owner = request.SubjectKind is null ? "" : "activation_id=$subjectIdentity";
        string boundary = request.Identity is not null ? "receipt_key=$identity" : request.AfterIdentity is not null ? "receipt_key>$after" : "";
        string predicate = owner.Length == 0 && boundary.Length == 0 ? "" : "WHERE " + owner + (owner.Length > 0 && boundary.Length > 0 ? " AND " : "") + boundary + " ";
        command.CommandText = $"SELECT receipt_key,operation_kind,fingerprint,result_checksum,activation_id FROM {_names.ActivationInstanceReceipts} " + predicate +
            "ORDER BY receipt_key LIMIT $take;";
        BindIdentity(command, request); if (request.SubjectKind is not null) command.Parameters.AddWithValue("$subjectIdentity", request.SubjectIdentity!);
        command.Parameters.AddWithValue("$take", request.Identity is null ? request.Take + 1 : 1);
        var facts = new InspectedFacts();
        await using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        { facts.RowsRead++;
            facts.Add(WithChecksum(new StudioActivationReceiptFact { Identity = reader.GetString(0), TransitionKind = reader.GetString(1),
                RequestFingerprint = [.. Blob32(reader, 2)], ResultDigest = [.. Blob32(reader, 3)],
                ActivationId = reader.IsDBNull(4) ? null : reader.GetString(4), FactChecksum = [] })); }
        return facts;
    }

    private async ValueTask<InspectedFacts> ReadActivationFactsAsync(SqliteConnection connection,
        StudioControlInspectionRequest request, CancellationToken cancellationToken)
    {
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = $"SELECT a.activation_id,a.definition_id,a.definition_version,a.state,a.generation,a.attempt_number,a.claim_epoch,a.effective_due_at,a.occurrence_id," +
            $"EXISTS(SELECT 1 FROM {_names.ActivationEffects} e WHERE e.activation_id=a.activation_id) FROM {_names.Activations} a " + Predicate("a.activation_id", request) + " ORDER BY a.activation_id LIMIT $take;";
        BindIdentity(command, request); command.Parameters.AddWithValue("$take", request.Identity is null ? request.Take + 1 : 1);
        var facts = new InspectedFacts(); await using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        { facts.RowsRead++;
            facts.Add(WithChecksum(new StudioActivationFact { Identity = reader.GetString(0), DefinitionId = reader.GetString(1), DefinitionVersion = reader.GetInt32(2),
                State = (BaseActivationState)reader.GetInt32(3), Generation = reader.GetInt64(4), AttemptNumber = reader.GetInt32(5), ClaimEpoch = reader.GetInt64(6),
                EffectiveDueAt = reader.GetInt64(7), OccurrenceId = reader.IsDBNull(8) ? null : reader.GetString(8), HasEffect = reader.GetInt64(9) != 0, FactChecksum = [] })); }
        return facts;
    }

    private async ValueTask<InspectedFacts> ReadScheduleFactsAsync(SqliteConnection connection,
        StudioControlInspectionRequest request, CancellationToken cancellationToken)
    {
        (string Id, int Version)? exact = request.Identity is null ? null : DecodeSchedule(request.Identity);
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = $"SELECT schedule_id,schedule_version,definition_generation,enabled,schedule_epoch,next_nominal FROM {_names.ActivationSchedules} " +
            (exact is not null ? "WHERE schedule_id=$id AND schedule_version=$version " : "") +
            "ORDER BY schedule_id,schedule_version LIMIT $take;";
        BindVersioned(command, exact); command.Parameters.AddWithValue("$take", exact is null ? request.Limits.MaximumRowsRead + 1 : 1);
        var facts = new InspectedFacts(); await using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        { facts.RowsRead++;
            facts.Add(WithChecksum(new StudioScheduleFact { Identity = StudioControlInspectionContract.ScheduleIdentity(reader.GetString(0), reader.GetInt32(1)),
                Version = reader.GetInt32(1), DefinitionGeneration = reader.GetInt64(2), Enabled = reader.GetInt64(3) != 0,
                ScheduleEpoch = reader.GetInt64(4), NextNominal = reader.IsDBNull(5) ? null : reader.GetInt64(5), FactChecksum = [] })); }
        return CanonicalPage(facts, request);
    }

    private async ValueTask<InspectedFacts> ReadOccurrenceFactsAsync(SqliteConnection connection,
        StudioControlInspectionRequest request, CancellationToken cancellationToken)
    {
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = $"SELECT occurrence_id,schedule_id,schedule_epoch,nominal_at,effective_at,fact_json FROM {_names.ActivationOccurrences} " + Predicate("occurrence_id", request) + " ORDER BY occurrence_id LIMIT $take;";
        BindIdentity(command, request); command.Parameters.AddWithValue("$take", request.Identity is null ? request.Take + 1 : 1);
        var facts = new InspectedFacts(); await using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            facts.RowsRead++;
            BaseScheduleOccurrenceFact? fact = JsonSerializer.Deserialize((byte[])reader[5], HPDBaseJsonSerializerContext.Default.BaseScheduleOccurrenceFact);
            if (fact is null) throw new InvalidDataException();
            (string disposition, string? activation) = fact.Disposition switch
            {
                BaseOccurrenceMaterialized value => ("materialized", value.ActivationId), BaseOccurrenceSkippedMisfire => ("skippedMisfire", null),
                BaseOccurrenceSkippedOverlap => ("skippedOverlap", null), BaseOccurrenceCancelled => ("cancelled", null),
                BaseOccurrenceSuppressedByReplacement => ("suppressedByReplacement", null), BaseOccurrenceSuppressedByRestoreFloor => ("suppressedByRestoreFloor", null),
                _ => throw new InvalidDataException(),
            };
            facts.Add(WithChecksum(new StudioOccurrenceFact { Identity = reader.GetString(0), ScheduleId = reader.GetString(1), ScheduleEpoch = reader.GetInt64(2),
                NominalAt = reader.GetInt64(3), EffectiveAt = reader.GetInt64(4), Disposition = disposition, ActivationId = activation, FactChecksum = [] }));
        }
        return facts;
    }

    private async ValueTask<InspectedFacts> ReadExecutorFactsAsync(SqliteConnection connection,
        StudioControlInspectionRequest request, CancellationToken cancellationToken)
    {
        (string App, string Host, string Process)? exact = request.Identity is null ? null : DecodeExecutor(request.Identity);
        if (exact is { } exactValue && !StringComparer.Ordinal.Equals(exactValue.App, request.ApplicationId))
            throw new InvalidDataException();
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = $"SELECT application_id,host_id,process_incarnation_id,executor_generation,heartbeat_revision,heartbeat_expires_at,retired FROM {_names.Executors} WHERE application_id=$application " +
            (exact is not null ? "AND host_id=$host AND process_incarnation_id=$process " : "") +
            "ORDER BY host_id,process_incarnation_id LIMIT $take;";
        command.Parameters.AddWithValue("$application", request.ApplicationId); BindExecutor(command, exact);
        command.Parameters.AddWithValue("$take", exact is null ? request.Limits.MaximumRowsRead + 1 : 1);
        var facts = new InspectedFacts(); await using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        { facts.RowsRead++;
            facts.Add(WithChecksum(new StudioExecutorFact { Identity = StudioControlInspectionContract.ExecutorIdentity(reader.GetString(0), reader.GetString(1), reader.GetString(2)), HostId = reader.GetString(1),
                ProcessIncarnationId = reader.GetString(2), ExecutorGeneration = reader.GetInt64(3), HeartbeatRevision = reader.GetInt64(4), HeartbeatExpiresAt = reader.GetInt64(5),
                Retired = reader.GetInt64(6) != 0, FactChecksum = [] })); }
        return CanonicalPage(facts, request);
    }

    private async ValueTask<InspectedFacts> ReadEffectFactsAsync(SqliteConnection connection,
        StudioControlInspectionRequest request, CancellationToken cancellationToken)
    {
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = $"SELECT activation_id,claim_attempt,effect_start_generation,executor_generation,heartbeat_revision,heartbeat_expires_at FROM {_names.ActivationEffects} " +
            Predicate("activation_id", request) + " ORDER BY activation_id LIMIT $take;";
        BindIdentity(command, request); command.Parameters.AddWithValue("$take", request.Identity is null ? request.Take + 1 : 1);
        var facts = new InspectedFacts(); await using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        { facts.RowsRead++;
            facts.Add(WithChecksum(new StudioEffectFact { Identity = reader.GetString(0), ActivationId = reader.GetString(0), AttemptNumber = reader.GetInt32(1), EffectStartGeneration = reader.GetInt64(2),
                ExecutorGeneration = reader.GetInt64(3), HeartbeatRevision = reader.GetInt64(4), HeartbeatExpiresAt = reader.GetInt64(5), FactChecksum = [] })); }
        return facts;
    }

    private async ValueTask<InspectedFacts> ReadSubjectContractFactsAsync(SqliteConnection connection,
        StudioControlInspectionRequest request, CancellationToken cancellationToken)
    {
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = $"SELECT contract_id,contract_version,contract_checksum,authority_epoch,restore_epoch,state_generation,publication_kind,publication_position FROM {_names.SubjectContracts} ORDER BY contract_id,contract_version LIMIT $take;";
        command.Parameters.AddWithValue("$take", request.Limits.MaximumRowsRead + 1);
        var facts = new InspectedFacts(); await using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        { facts.RowsRead++; var value = new StudioSubjectContractFact { Identity = StudioControlInspectionContract.SubjectContractIdentity(reader.GetString(0), reader.GetInt32(1)),
            ContractId = reader.GetString(0), ContractVersion = reader.GetInt32(1), ContractChecksum = reader.GetString(2), AuthorityEpoch = [.. (byte[])reader[3]],
            RestoreEpoch = reader.GetInt64(4), StateGeneration = reader.GetInt64(5), PublicationKind = (BaseSubjectAuthorityPublicationKind)reader.GetInt32(6),
            PublicationPosition = reader.GetInt64(7), FactChecksum = [] }; facts.Add(WithChecksum(value)); }
        return SubjectPage(facts, request);
    }

    private async ValueTask<InspectedFacts> ReadSubjectFactsAsync(SqliteConnection connection,
        StudioControlInspectionRequest request, CancellationToken cancellationToken)
    {
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = $"SELECT contract_id,contract_version,subject_id,incarnation,created_journal_position FROM {_names.SubjectLifetimes} ORDER BY contract_id,contract_version,subject_id LIMIT $take;";
        command.Parameters.AddWithValue("$take", request.Limits.MaximumRowsRead + 1);
        var facts = new InspectedFacts(); await using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        { facts.RowsRead++; var value = new StudioSubjectFact { Identity = StudioControlInspectionContract.SubjectIdentity(reader.GetString(0), reader.GetInt32(1), reader.GetString(2)),
            ContractId = reader.GetString(0), ContractVersion = reader.GetInt32(1), SubjectId = reader.GetString(2), Incarnation = [.. (byte[])reader[3]],
            CreatedJournalPosition = reader.GetInt64(4), FactChecksum = [] }; facts.Add(WithChecksum(value)); }
        return SubjectPage(facts, request);
    }

    private static InspectedFacts SubjectPage(InspectedFacts facts, StudioControlInspectionRequest request)
    {
        long rows = facts.RowsRead; StudioControlFact[] selected = facts.Where(value => request.Identity is null || StringComparer.Ordinal.Equals(value.Identity, request.Identity))
            .Where(value => request.AfterIdentity is null || StringComparer.Ordinal.Compare(value.Identity, request.AfterIdentity) > 0)
            .OrderBy(static value => value.Identity, StringComparer.Ordinal).Take(request.Take + 1).ToArray();
        facts.Clear(); facts.AddRange(selected); facts.RowsRead = rows; return facts;
    }

    private static T WithChecksum<T>(T fact) where T : StudioControlFact => (T)(fact with { FactChecksum = StudioControlInspectionContract.FactChecksum(fact) });
    private static bool ValidInspectionIdentity(StudioControlInspectionRequest request)
        => StudioControlInspectionContract.IsValid(request);
    private static string Predicate(string column, StudioControlInspectionRequest request) => request.Identity is not null ? $"WHERE {column}=$identity" : request.AfterIdentity is not null ? $"WHERE {column}>$after" : "";
    private static void BindIdentity(SqliteCommand command, StudioControlInspectionRequest request) { if (request.Identity is not null) command.Parameters.AddWithValue("$identity", request.Identity); else if (request.AfterIdentity is not null) command.Parameters.AddWithValue("$after", request.AfterIdentity); }
    private static (string Scope, string Operation, string Key) DecodeAtomic(string value) =>
        StudioControlInspectionContract.TryDecodeAtomicIdentity(value, out string scope, out string operation, out string key)
            ? (scope, operation, key) : throw new InvalidDataException();
    private static (string Id, int Version) DecodeSchedule(string value) =>
        StudioControlInspectionContract.TryDecodeScheduleIdentity(value, out string id, out int version)
            ? (id, version) : throw new InvalidDataException();
    private static (string App, string Host, string Process) DecodeExecutor(string value) =>
        StudioControlInspectionContract.TryDecodeExecutorIdentity(value, out string app, out string host, out string process)
            ? (app, host, process) : throw new InvalidDataException();
    private static void BindTriple(SqliteCommand command, (string Scope, string Operation, string Key)? value) { if (value is null) return; command.Parameters.AddWithValue("$scope", value.Value.Scope); command.Parameters.AddWithValue("$operation", value.Value.Operation); command.Parameters.AddWithValue("$key", value.Value.Key); }
    private static void BindVersioned(SqliteCommand command, (string Id, int Version)? value) { if (value is null) return; command.Parameters.AddWithValue("$id", value.Value.Id); command.Parameters.AddWithValue("$version", value.Value.Version); }
    private static void BindExecutor(SqliteCommand command, (string App, string Host, string Process)? value) { if (value is null) return; command.Parameters.AddWithValue("$host", value.Value.Host); command.Parameters.AddWithValue("$process", value.Value.Process); }
    private static byte[] Blob32(SqliteDataReader reader, int ordinal) { byte[] value = (byte[])reader[ordinal]; return value.Length == 32 ? value : throw new InvalidDataException(); }
    private static DateTimeOffset ParseUtc(string value) => DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out DateTimeOffset parsed) ? parsed.ToUniversalTime() : throw new InvalidDataException();
    private static InspectedFacts CanonicalPage(InspectedFacts facts, StudioControlInspectionRequest request)
    {
        long rows = facts.RowsRead;
        StudioControlFact[] selected = facts
            .Where(value => request.AfterIdentity is null || StringComparer.Ordinal.Compare(value.Identity, request.AfterIdentity) > 0)
            .OrderBy(static value => value.Identity, StringComparer.Ordinal).Take(checked(request.Take + 1)).ToArray();
        facts.Clear(); facts.AddRange(selected); facts.RowsRead = rows; return facts;
    }
    private sealed class InspectedFacts : List<StudioControlFact> { internal long RowsRead { get; set; } }
    private static OperationResult<StudioControlInspectionPage> InspectionFailure(string code, ErrorCategory category) => category switch
    { ErrorCategory.Store => OperationResults.StoreError<StudioControlInspectionPage>(InspectionError(code, category)), _ => OperationResults.ValidationFailed<StudioControlInspectionPage>(InspectionError(code, category)) };
    private static BaseError InspectionError(string code, ErrorCategory category) => new() { Code = code, Message = "The control-plane inspection could not be completed.", Category = category };
}
