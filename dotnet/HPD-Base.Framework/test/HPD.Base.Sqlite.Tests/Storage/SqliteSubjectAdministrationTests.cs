using System.Text.Json;
using FluentAssertions;
using HPD.Base;
using HPD.Base.Sqlite;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace HPD.Base.Sqlite.Tests.Storage;

public sealed class SqliteSubjectAdministrationTests
{
    [Fact]
    public async Task Lifecycle_prune_persists_verified_bounded_progress_and_resumes()
    {
        string path = Path.Combine(Path.GetTempPath(), $"hpd-base-lifecycle-prune-{Guid.NewGuid():N}.db");
        const string checksum = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
        using var protector = new BaseOpaqueTokenProtector(Options.Create(new HPDBaseTokenProtectionOptions { ActiveKey = new BaseOpaqueTokenKey { Id = 31, Key = Enumerable.Repeat((byte)0x31, 32).ToArray(), IssueNotBefore = DateTimeOffset.UnixEpoch } }));
        var interruption = new OneShotPhaseInterruption("subjectLifecyclePruneAfterPage");
        try
        {
            await using SqliteRecordStore store = SqliteTestFactory.Create(new HPDBaseSqliteOptions { StoreId = $"lifecycle-prune-{Guid.NewGuid():N}", DataSource = path, EnableWal = false, Collections = [ConsumerCollection(checksum)], ExportedSubjects = [SubjectDefinition(checksum)] }, tokenProtector: protector, administrationOperations: interruption);
            await using (var connection = new SqliteConnection($"Data Source={path};Pooling=False"))
            {
                await connection.OpenAsync(); await using SqliteCommand seed = connection.CreateCommand(); seed.CommandText = """
WITH RECURSIVE rows(value) AS (VALUES(1) UNION ALL SELECT value+1 FROM rows WHERE value<300)
INSERT INTO hpd_base_subject_lifecycle_facts(commit_position,contract_id,contract_version,subject_id,authority_epoch,incarnation,subject_sequence,contract_state_generation,delivery_epoch,fact_kind,previous_state,current_state,scope_kind,scope_index_digest,protected_scope_value)
SELECT value,'example.subject',1,printf('subject-%03d',value),$epoch,$incarnation,1,1,1,0,NULL,0,0,$digest,X'' FROM rows;
"""; seed.Parameters.Add("$digest", SqliteType.Blob).Value = new byte[32]; seed.Parameters.Add("$epoch", SqliteType.Blob).Value = new byte[16]; seed.Parameters.Add("$incarnation", SqliteType.Blob).Value = Incarnation(1); await seed.ExecuteNonQueryAsync();
            }
            var request = new BaseSubjectLifecycleMaintenanceExecutionRequest
            {
                FormatVersion = 1, Kind = BaseSubjectLifecycleMaintenanceKind.Prune, ContractId = "example.subject", ContractVersion = 1,
                RetainedFrom = new BaseSubjectLifecycleOrderingBoundary { CommitPosition = new BaseMutationJournalPosition(301), SubjectId = BaseSubjectId.Create("terminal", BaseSubjectIdKind.OrdinalString), AuthorityEpoch = new BaseSubjectAuthorityEpoch(new byte[16]), Incarnation = new BaseSubjectIncarnation(Incarnation(1)), SubjectSequence = 1 },
                Identity = BaseMutationRequestIdentity.Create("control-plane", "prune-lifecycle", "prune-lifecycle-1", BaseMutationRequestFingerprint.Create(System.Security.Cryptography.SHA256.HashData("prune-lifecycle"u8))),
                PlanChecksum = new byte[32], ExpectedStoreGeneration = store.VectorSchemaGeneration, ExpectedSchemaGeneration = store.VectorSchemaGeneration, ExpectedRestoreEpoch = 0, ExpectedDeliveryEpoch = 1, ExpectedScopeProtectionGeneration = 1, ExpectedScopeProtectionKeyId = "31", PageSize = 256, OperationTimeout = TimeSpan.FromSeconds(5), CommitCompletionTimeout = TimeSpan.FromSeconds(5),
            };
            request = request with { PlanChecksum = BaseSubjectLifecycleMaintenanceProcessor.PlanChecksum(request) };
            await FluentActions.Awaiting(async () => await store.ExecuteMaintenanceAsync(new BaseSubjectLifecycleMaintenanceProcessor(), request)).Should().ThrowAsync<IOException>();
            await using (var progress = new SqliteConnection($"Data Source={path};Pooling=False")) { await progress.OpenAsync(); await using SqliteCommand count = progress.CreateCommand(); count.CommandText = "SELECT (SELECT changed_count FROM hpd_base_subject_lifecycle_maintenance),(SELECT COUNT(*) FROM hpd_base_subject_lifecycle_scope_stage),(SELECT COUNT(*) FROM hpd_base_subject_lifecycle_facts);"; await using SqliteDataReader reader = await count.ExecuteReaderAsync(); (await reader.ReadAsync()).Should().BeTrue(); reader.GetInt64(0).Should().Be(256); reader.GetInt64(1).Should().Be(256); reader.GetInt64(2).Should().Be(44); }
            var processor = new BaseSubjectLifecycleMaintenanceProcessor(); RecordMutationExecutionResult resumed = await store.ExecuteMaintenanceAsync(processor, request); resumed.Outcome.Should().Be(RecordMutationExecutionOutcome.Committed, resumed.Error?.Code); processor.Result!.ChangedCount.Should().Be(300); processor.Result.ExaminedCount.Should().Be(300);
            await using var verify = new SqliteConnection($"Data Source={path};Pooling=False"); await verify.OpenAsync(); await using SqliteCommand final = verify.CreateCommand(); final.CommandText = "SELECT (SELECT COUNT(*) FROM hpd_base_subject_lifecycle_facts),(SELECT COUNT(*) FROM hpd_base_subject_lifecycle_maintenance),(SELECT COUNT(*) FROM hpd_base_subject_lifecycle_scope_stage);"; await using SqliteDataReader finalReader = await final.ExecuteReaderAsync(); (await finalReader.ReadAsync()).Should().BeTrue(); finalReader.GetInt64(0).Should().Be(0); finalReader.GetInt64(1).Should().Be(0); finalReader.GetInt64(2).Should().Be(0);
        }
        finally { SqliteConnection.ClearAllPools(); foreach (string candidate in new[] { path, path + "-wal", path + "-shm" }) if (File.Exists(candidate)) File.Delete(candidate); }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Lifecycle_delivery_rebuild_stages_bounded_pages_and_publishes_one_generation(bool corruptStage)
    {
        string path = Path.Combine(Path.GetTempPath(), $"hpd-base-lifecycle-rebuild-{Guid.NewGuid():N}.db"); const string checksum = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
        using var protector = new BaseOpaqueTokenProtector(Options.Create(new HPDBaseTokenProtectionOptions { ActiveKey = new BaseOpaqueTokenKey { Id = 31, Key = Enumerable.Repeat((byte)0x31, 32).ToArray(), IssueNotBefore = DateTimeOffset.UnixEpoch } }));
        var consumer = new BaseSubjectLifecycleConsumerDefinition { Id = "consumer.rebuild", Version = 1, OwningModuleId = "consumer.module", Audience = BaseSubjectLifecycleConsumerAudience.Service, ContractId = "example.subject", ContractVersion = 1, ObservedStates = [BaseSubjectLifecycleState.Active], DeliveryGrantId = "consumer.rebuild.read", Limits = new BaseSubjectLifecycleConsumerLimits { MaximumFactsPerPage = 256, MaximumResultBytes = 1_048_576, MaximumCheckpointLag = TimeSpan.FromDays(1), ReadTimeout = TimeSpan.FromSeconds(5) } };
        var interruption = new OneShotPhaseInterruption("subjectLifecycleDeliveryRebuildAfterPage");
        try
        {
            await using SqliteRecordStore store = SqliteTestFactory.Create(new HPDBaseSqliteOptions { StoreId = $"lifecycle-rebuild-{Guid.NewGuid():N}", DataSource = path, EnableWal = false, Collections = [ConsumerCollection(checksum)], ExportedSubjects = [SubjectDefinition(checksum)], SubjectLifecycleConsumers = [consumer] }, tokenProtector: protector, administrationOperations: interruption);
            await using (var connection = new SqliteConnection($"Data Source={path};Pooling=False"))
            {
                await connection.OpenAsync(); await using SqliteCommand seed = connection.CreateCommand(); seed.CommandText = """
INSERT INTO hpd_base_subject_lifecycle_consumers(consumer_id,consumer_version,consumer_checksum,contract_id,contract_version,projection_generation,cutoff_position,published_graph_generation,state) VALUES('consumer.rebuild',1,$checksum,'example.subject',1,1,0,1,0);
WITH RECURSIVE rows(value) AS (VALUES(1) UNION ALL SELECT value+1 FROM rows WHERE value<300)
INSERT INTO hpd_base_subject_lifecycle_facts(commit_position,contract_id,contract_version,subject_id,authority_epoch,incarnation,subject_sequence,contract_state_generation,delivery_epoch,fact_kind,previous_state,current_state,scope_kind,scope_index_digest,protected_scope_value)
SELECT value,'example.subject',1,printf('subject-%03d',value),$epoch,$incarnation,1,1,1,0,NULL,0,0,$digest,X'' FROM rows;
"""; seed.Parameters.AddWithValue("$checksum", BaseSubjectLifecycleRegistry.Checksum(BaseSubjectLifecycleRegistry.Normalize(consumer), checksum)); seed.Parameters.Add("$digest", SqliteType.Blob).Value = new byte[32]; seed.Parameters.Add("$epoch", SqliteType.Blob).Value = new byte[16]; seed.Parameters.Add("$incarnation", SqliteType.Blob).Value = Incarnation(1); await seed.ExecuteNonQueryAsync();
            }
            var request = new BaseSubjectLifecycleMaintenanceExecutionRequest { FormatVersion = 1, Kind = BaseSubjectLifecycleMaintenanceKind.RebuildDeliveryProjection, ContractId = "example.subject", ContractVersion = 1, ConsumerId = consumer.Id, ConsumerVersion = consumer.Version, ExpectedProjectionGeneration = 1, Identity = BaseMutationRequestIdentity.Create("control-plane", "rebuild-delivery", "rebuild-delivery-1", BaseMutationRequestFingerprint.Create(System.Security.Cryptography.SHA256.HashData("rebuild-delivery"u8))), PlanChecksum = new byte[32], ExpectedStoreGeneration = store.VectorSchemaGeneration, ExpectedSchemaGeneration = store.VectorSchemaGeneration, ExpectedRestoreEpoch = 0, ExpectedDeliveryEpoch = 1, ExpectedScopeProtectionGeneration = 1, ExpectedScopeProtectionKeyId = "31", PageSize = 256, OperationTimeout = TimeSpan.FromSeconds(5), CommitCompletionTimeout = TimeSpan.FromSeconds(5) };
            request = request with { PlanChecksum = BaseSubjectLifecycleMaintenanceProcessor.PlanChecksum(request) };
            await FluentActions.Awaiting(async () => await store.ExecuteMaintenanceAsync(new BaseSubjectLifecycleMaintenanceProcessor(), request)).Should().ThrowAsync<IOException>();
            if (corruptStage)
            {
                await using var corrupt = new SqliteConnection($"Data Source={path};Pooling=False"); await corrupt.OpenAsync(); await using SqliteCommand command = corrupt.CreateCommand(); command.CommandText = "UPDATE hpd_base_subject_lifecycle_membership_stage SET subject_id='corrupt' WHERE source_rowid=(SELECT MIN(source_rowid) FROM hpd_base_subject_lifecycle_membership_stage);"; (await command.ExecuteNonQueryAsync()).Should().Be(1);
                RecordMutationExecutionResult rejected = await store.ExecuteMaintenanceAsync(new BaseSubjectLifecycleMaintenanceProcessor(), request); rejected.Outcome.Should().Be(RecordMutationExecutionOutcome.RollbackConfirmed); rejected.Error!.Code.Should().Be(BaseSubjectErrorCodes.LifecycleProviderContractInvalid);
                await using SqliteCommand retained = corrupt.CreateCommand(); retained.CommandText = "SELECT (SELECT COUNT(*) FROM hpd_base_subject_lifecycle_maintenance),(SELECT COUNT(*) FROM hpd_base_subject_lifecycle_membership_stage);"; await using SqliteDataReader retainedReader = await retained.ExecuteReaderAsync(); (await retainedReader.ReadAsync()).Should().BeTrue(); retainedReader.GetInt64(0).Should().Be(1); retainedReader.GetInt64(1).Should().BeGreaterThan(0); return;
            }
            var processor = new BaseSubjectLifecycleMaintenanceProcessor(); RecordMutationExecutionResult resumed = await store.ExecuteMaintenanceAsync(processor, request); resumed.Outcome.Should().Be(RecordMutationExecutionOutcome.Committed, resumed.Error?.Code); processor.Result!.ProjectionGeneration.Should().Be(2); processor.Result.ExaminedCount.Should().Be(301); processor.Result.ChangedCount.Should().Be(301);
            await using var verify = new SqliteConnection($"Data Source={path};Pooling=False"); await verify.OpenAsync(); await using SqliteCommand count = verify.CreateCommand(); count.CommandText = "SELECT (SELECT projection_generation FROM hpd_base_subject_lifecycle_consumers WHERE consumer_id='consumer.rebuild'),(SELECT COUNT(*) FROM hpd_base_subject_lifecycle_memberships WHERE consumer_id='consumer.rebuild' AND projection_generation=2),(SELECT COUNT(*) FROM hpd_base_subject_lifecycle_membership_stage);"; await using SqliteDataReader reader = await count.ExecuteReaderAsync(); (await reader.ReadAsync()).Should().BeTrue(); reader.GetInt64(0).Should().Be(2); reader.GetInt64(1).Should().Be(300); reader.GetInt64(2).Should().Be(0);
        }
        finally { SqliteConnection.ClearAllPools(); foreach (string candidate in new[] { path, path + "-wal", path + "-shm" }) if (File.Exists(candidate)) File.Delete(candidate); }
    }

    [Fact]
    public async Task Lifecycle_consumer_removal_uses_bounded_verified_pages_and_resumes()
    {
        string path = Path.Combine(Path.GetTempPath(), $"hpd-base-lifecycle-remove-{Guid.NewGuid():N}.db");
        const string checksum = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
        using var protector = new BaseOpaqueTokenProtector(Options.Create(new HPDBaseTokenProtectionOptions
        {
            ActiveKey = new BaseOpaqueTokenKey { Id = 31, Key = Enumerable.Repeat((byte)0x31, 32).ToArray(), IssueNotBefore = DateTimeOffset.UnixEpoch },
        }));
        var interruption = new OneShotPhaseInterruption("subjectLifecycleConsumerRemovalAfterPage");
        try
        {
            await using SqliteRecordStore store = SqliteTestFactory.Create(new HPDBaseSqliteOptions
            {
                StoreId = $"lifecycle-remove-{Guid.NewGuid():N}", DataSource = path, EnableWal = false,
                Collections = [ConsumerCollection(checksum)], ExportedSubjects = [SubjectDefinition(checksum)],
            }, tokenProtector: protector, administrationOperations: interruption);
            await using (var connection = new SqliteConnection($"Data Source={path};Pooling=False"))
            {
                await connection.OpenAsync();
                await using SqliteCommand seed = connection.CreateCommand();
                seed.CommandText = """
INSERT INTO hpd_base_subject_lifecycle_consumers(consumer_id,consumer_version,consumer_checksum,contract_id,contract_version,projection_generation,cutoff_position,published_graph_generation,state)
VALUES('consumer.remove',1,$checksum,'example.subject',1,1,0,1,0);
WITH RECURSIVE rows(value) AS (VALUES(1) UNION ALL SELECT value+1 FROM rows WHERE value<300)
INSERT INTO hpd_base_subject_lifecycle_memberships(consumer_id,consumer_version,consumer_checksum,contract_id,contract_version,projection_generation,matched_state,scope_kind,scope_index_digest,protected_scope_value,commit_position,subject_id,authority_epoch,incarnation,subject_sequence)
SELECT 'consumer.remove',1,$checksum,'example.subject',1,1,0,0,$digest,X'',value,printf('subject-%03d',value),$epoch,$incarnation,1 FROM rows;
""";
                seed.Parameters.AddWithValue("$checksum", checksum);
                seed.Parameters.Add("$digest", SqliteType.Blob).Value = new byte[32];
                seed.Parameters.Add("$epoch", SqliteType.Blob).Value = new byte[16];
                seed.Parameters.Add("$incarnation", SqliteType.Blob).Value = Incarnation(1);
                await seed.ExecuteNonQueryAsync();
            }
            var request = new BaseSubjectLifecycleMaintenanceExecutionRequest
            {
                FormatVersion = 1, Kind = BaseSubjectLifecycleMaintenanceKind.RemoveConsumer,
                ContractId = "example.subject", ContractVersion = 1, ConsumerId = "consumer.remove", ConsumerVersion = 1,
                ExpectedProjectionGeneration = 1,
                Identity = BaseMutationRequestIdentity.Create("control-plane", "remove-consumer", "remove-consumer-1", BaseMutationRequestFingerprint.Create(System.Security.Cryptography.SHA256.HashData("remove-consumer"u8))),
                PlanChecksum = new byte[32], ExpectedStoreGeneration = store.VectorSchemaGeneration, ExpectedSchemaGeneration = store.VectorSchemaGeneration,
                ExpectedRestoreEpoch = 0, ExpectedDeliveryEpoch = 1, ExpectedScopeProtectionGeneration = 1, ExpectedScopeProtectionKeyId = "31",
                PageSize = 256, OperationTimeout = TimeSpan.FromSeconds(5), CommitCompletionTimeout = TimeSpan.FromSeconds(5),
            };
            request = request with { PlanChecksum = BaseSubjectLifecycleMaintenanceProcessor.PlanChecksum(request) };
            await FluentActions.Awaiting(async () => await store.ExecuteMaintenanceAsync(new BaseSubjectLifecycleMaintenanceProcessor(), request)).Should().ThrowAsync<IOException>();
            var processor = new BaseSubjectLifecycleMaintenanceProcessor();
            RecordMutationExecutionResult resumed = await store.ExecuteMaintenanceAsync(processor, request);
            resumed.Outcome.Should().Be(RecordMutationExecutionOutcome.Committed, resumed.Error?.Code);
            processor.Result!.ExaminedCount.Should().Be(301);
            processor.Result.ChangedCount.Should().Be(301);
            await using var verify = new SqliteConnection($"Data Source={path};Pooling=False"); await verify.OpenAsync();
            await using SqliteCommand count = verify.CreateCommand(); count.CommandText = "SELECT (SELECT COUNT(*) FROM hpd_base_subject_lifecycle_consumers WHERE consumer_id='consumer.remove'),(SELECT COUNT(*) FROM hpd_base_subject_lifecycle_memberships WHERE consumer_id='consumer.remove'),(SELECT COUNT(*) FROM hpd_base_subject_lifecycle_maintenance),(SELECT COUNT(*) FROM hpd_base_subject_lifecycle_scope_stage);";
            await using SqliteDataReader reader = await count.ExecuteReaderAsync(); (await reader.ReadAsync()).Should().BeTrue();
            reader.GetInt64(0).Should().Be(0); reader.GetInt64(1).Should().Be(0); reader.GetInt64(2).Should().Be(0); reader.GetInt64(3).Should().Be(0);
            await reader.DisposeAsync(); await verify.DisposeAsync(); await store.DisposeAsync();

            await using SqliteRecordStore reopened = SqliteTestFactory.Create(new HPDBaseSqliteOptions
            {
                StoreId = $"lifecycle-remove-reopened-{Guid.NewGuid():N}", DataSource = path, EnableWal = false,
                Collections = [ConsumerCollection(checksum)], ExportedSubjects = [SubjectDefinition(checksum)],
            }, tokenProtector: protector);
            OperationResult<BaseSubjectLifecycleProviderPage> stale = await reopened.ReadAsync(new()
            {
                ApplicationId = "test.application", ContractId = "example.subject", ContractVersion = 1,
                ContractChecksum = checksum, ConsumerId = "consumer.remove", ConsumerVersion = 1,
                ConsumerChecksum = checksum, ProjectionGeneration = 1,
                Scope = new BaseOwnedSubjectScopeEvidence { Kind = BaseSubjectScopeKind.Global },
                Take = 1, MaximumResultBytes = 4096, DeadlineUtc = DateTimeOffset.UtcNow.AddSeconds(5),
            });
            stale.IsSuccess().Should().BeFalse();
            stale.Error!.Code.Should().Be(BaseSubjectErrorCodes.LifecycleProviderContractInvalid);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            foreach (string candidate in new[] { path, path + "-wal", path + "-shm" }) if (File.Exists(candidate)) File.Delete(candidate);
        }
    }

    [Fact]
    public async Task Lifecycle_scope_protection_rotation_rekeys_current_indexes_and_publishes_fresh_authority()
    {
        string path = Path.Combine(Path.GetTempPath(), $"hpd-base-subject-scope-rotation-{Guid.NewGuid():N}.db");
        const string checksum = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
        using var protector = new BaseOpaqueTokenProtector(Options.Create(new HPDBaseTokenProtectionOptions
        {
            ActiveKey = new BaseOpaqueTokenKey { Id = 31, Key = Enumerable.Repeat((byte)0x31, 32).ToArray(), IssueNotBefore = DateTimeOffset.UnixEpoch },
            DecryptionKeys = [new BaseOpaqueTokenKey { Id = 32, Key = Enumerable.Repeat((byte)0x32, 32).ToArray(), IssueNotBefore = DateTimeOffset.UnixEpoch }],
        }));
        var options = new HPDBaseSqliteOptions
        {
            StoreId = $"subject-scope-rotation-{Guid.NewGuid():N}", DataSource = path, EnableWal = false,
            Collections = [ConsumerCollection(checksum)], ExportedSubjects = [SubjectDefinition(checksum)],
        };
        try
        {
            await using SqliteRecordStore store = SqliteTestFactory.Create(options, tokenProtector: protector);
            var scopes = new BaseSubjectScopeProtector(protector);
            var logicalScope = new BaseOwnedSubjectScopeEvidence { Kind = BaseSubjectScopeKind.Tenant, Value = "tenant-a" };
            BaseProtectedSubjectScope prior = scopes.Protect(logicalScope, 31);
            await using (var connection = new SqliteConnection($"Data Source={path};Pooling=False"))
            {
                await connection.OpenAsync();
                await using SqliteCommand insert = connection.CreateCommand();
                insert.CommandText = "WITH RECURSIVE rows(value) AS (VALUES(1) UNION ALL SELECT value+1 FROM rows WHERE value<300) INSERT INTO hpd_base_subject_lifecycle_facts(commit_position,contract_id,contract_version,subject_id,authority_epoch,incarnation,subject_sequence,contract_state_generation,delivery_epoch,fact_kind,previous_state,current_state,scope_kind,scope_index_digest,protected_scope_value) SELECT value+1,'example.subject',1,printf('subject-%03d',value),$epoch,$incarnation,1,1,1,0,NULL,0,1,$digest,$value FROM rows;";
                insert.Parameters.Add("$epoch", SqliteType.Blob).Value = Enumerable.Repeat((byte)4, 16).ToArray();
                insert.Parameters.Add("$incarnation", SqliteType.Blob).Value = Incarnation(7);
                insert.Parameters.Add("$digest", SqliteType.Blob).Value = prior.IndexDigest;
                insert.Parameters.Add("$value", SqliteType.Blob).Value = prior.ProtectedCanonicalValue;
                await insert.ExecuteNonQueryAsync();
            }
            byte[] fingerprint = System.Security.Cryptography.SHA256.HashData("scope-rotation"u8);
            BaseMutationRequestIdentity identity = BaseMutationRequestIdentity.Create("control-plane", "rotate-subject-scope-protection", "scope-rotation-1", BaseMutationRequestFingerprint.Create(fingerprint));
            var request = new BaseSubjectLifecycleMaintenanceExecutionRequest
            {
                FormatVersion = 1,
                Kind = BaseSubjectLifecycleMaintenanceKind.RotateScopeProtection, Identity = identity, PlanChecksum = new byte[32],
                ExpectedStoreGeneration = store.VectorSchemaGeneration, ExpectedSchemaGeneration = store.VectorSchemaGeneration, ExpectedRestoreEpoch = 0, ExpectedDeliveryEpoch = 1,
                ExpectedScopeProtectionGeneration = 1, ExpectedScopeProtectionKeyId = "31", ReplacementScopeProtectionKeyId = "32",
                PageSize = 256, OperationTimeout = TimeSpan.FromSeconds(5), CommitCompletionTimeout = TimeSpan.FromSeconds(5),
            };
            request = request with { PlanChecksum = BaseSubjectLifecycleMaintenanceProcessor.PlanChecksum(request) };
            var processor = new BaseSubjectLifecycleMaintenanceProcessor();
            RecordMutationExecutionResult execution = await store.ExecuteMaintenanceAsync(processor, request);
            execution.Outcome.Should().Be(RecordMutationExecutionOutcome.Committed, execution.Error?.Code ?? execution.Processing?.Error?.Code);
            processor.Result!.DeliveryEpoch.Should().Be(2);
            var duplicateProcessor = new BaseSubjectLifecycleMaintenanceProcessor();
            RecordMutationExecutionResult duplicate = await store.ExecuteMaintenanceAsync(duplicateProcessor, request);
            duplicate.Outcome.Should().Be(RecordMutationExecutionOutcome.Committed);
            duplicateProcessor.Result!.Duplicate.Should().BeTrue();
            duplicateProcessor.Result.DeliveryEpoch.Should().Be(2);
            BaseProtectedSubjectScope expected = scopes.Protect(logicalScope, 32);
            await using (var connection = new SqliteConnection($"Data Source={path};Pooling=False"))
            {
                await connection.OpenAsync();
                await using SqliteCommand read = connection.CreateCommand();
                read.CommandText = "SELECT scope_index_digest,protected_scope_value,(SELECT value FROM hpd_base_provider_state WHERE key='subject_scope_protection_generation'),(SELECT value FROM hpd_base_provider_state WHERE key='subject_scope_protection_key_id'),(SELECT COUNT(*) FROM hpd_base_subject_lifecycle_facts WHERE scope_index_digest=$expected) FROM hpd_base_subject_lifecycle_facts ORDER BY commit_position LIMIT 1;";
                read.Parameters.Add("$expected", SqliteType.Blob).Value = expected.IndexDigest;
                await using SqliteDataReader reader = await read.ExecuteReaderAsync();
                (await reader.ReadAsync()).Should().BeTrue();
                ((byte[])reader.GetValue(0)).Should().Equal(expected.IndexDigest);
                scopes.Matches(new BaseProtectedSubjectScope { Kind = BaseSubjectScopeKind.Tenant, IndexDigest = (byte[])reader.GetValue(0), ProtectedCanonicalValue = (byte[])reader.GetValue(1) }, logicalScope).Should().BeTrue();
                reader.GetString(2).Should().Be("2"); reader.GetString(3).Should().Be("32"); reader.GetInt64(4).Should().Be(300);
            }
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            foreach (string candidate in new[] { path, path + "-wal", path + "-shm" }) if (File.Exists(candidate)) File.Delete(candidate);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Lifecycle_scope_rotation_resumes_only_verified_staging_after_interruption(bool corruptStage)
    {
        string path = Path.Combine(Path.GetTempPath(), $"hpd-base-subject-scope-rotation-resume-{Guid.NewGuid():N}.db");
        const string checksum = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
        using var protector = new BaseOpaqueTokenProtector(Options.Create(new HPDBaseTokenProtectionOptions
        {
            ActiveKey = new BaseOpaqueTokenKey { Id = 31, Key = Enumerable.Repeat((byte)0x31, 32).ToArray(), IssueNotBefore = DateTimeOffset.UnixEpoch },
            DecryptionKeys = [new BaseOpaqueTokenKey { Id = 32, Key = Enumerable.Repeat((byte)0x32, 32).ToArray(), IssueNotBefore = DateTimeOffset.UnixEpoch }],
        }));
        var options = new HPDBaseSqliteOptions
        {
            StoreId = $"subject-scope-rotation-resume-{Guid.NewGuid():N}", DataSource = path, EnableWal = false,
            Collections = [ConsumerCollection(checksum)], ExportedSubjects = [SubjectDefinition(checksum)],
        };
        try
        {
            var interruption = new OneShotPhaseInterruption("subjectLifecycleRotationAfterPage");
            await using SqliteRecordStore store = SqliteTestFactory.Create(options, tokenProtector: protector, administrationOperations: interruption);
            var scopes = new BaseSubjectScopeProtector(protector);
            var logicalScope = new BaseOwnedSubjectScopeEvidence { Kind = BaseSubjectScopeKind.Tenant, Value = "tenant-a" };
            BaseProtectedSubjectScope prior = scopes.Protect(logicalScope, 31);
            await using (var connection = new SqliteConnection($"Data Source={path};Pooling=False"))
            {
                await connection.OpenAsync();
                await using SqliteCommand insert = connection.CreateCommand();
                insert.CommandText = "INSERT INTO hpd_base_subject_lifecycle_facts(commit_position,contract_id,contract_version,subject_id,authority_epoch,incarnation,subject_sequence,contract_state_generation,delivery_epoch,fact_kind,previous_state,current_state,scope_kind,scope_index_digest,protected_scope_value) VALUES(2,'example.subject',1,'subject-one',$epoch,$incarnation,1,1,1,0,NULL,0,1,$digest,$value);";
                insert.Parameters.Add("$epoch", SqliteType.Blob).Value = Enumerable.Repeat((byte)4, 16).ToArray();
                insert.Parameters.Add("$incarnation", SqliteType.Blob).Value = Incarnation(7);
                insert.Parameters.Add("$digest", SqliteType.Blob).Value = prior.IndexDigest;
                insert.Parameters.Add("$value", SqliteType.Blob).Value = prior.ProtectedCanonicalValue;
                await insert.ExecuteNonQueryAsync();
            }
            byte[] fingerprint = System.Security.Cryptography.SHA256.HashData("scope-rotation-resume"u8);
            var request = new BaseSubjectLifecycleMaintenanceExecutionRequest
            {
                FormatVersion = 1,
                Kind = BaseSubjectLifecycleMaintenanceKind.RotateScopeProtection,
                Identity = BaseMutationRequestIdentity.Create("control-plane", "rotate-subject-scope-protection", "scope-rotation-resume-1", BaseMutationRequestFingerprint.Create(fingerprint)),
                PlanChecksum = new byte[32], ExpectedStoreGeneration = store.VectorSchemaGeneration, ExpectedSchemaGeneration = store.VectorSchemaGeneration, ExpectedRestoreEpoch = 0, ExpectedDeliveryEpoch = 1,
                ExpectedScopeProtectionGeneration = 1, ExpectedScopeProtectionKeyId = "31", ReplacementScopeProtectionKeyId = "32",
                PageSize = 1, OperationTimeout = TimeSpan.FromSeconds(5), CommitCompletionTimeout = TimeSpan.FromSeconds(5),
            };
            request = request with { PlanChecksum = BaseSubjectLifecycleMaintenanceProcessor.PlanChecksum(request) };
            await FluentActions.Awaiting(async () => await store.ExecuteMaintenanceAsync(new BaseSubjectLifecycleMaintenanceProcessor(), request))
                .Should().ThrowAsync<IOException>();
            await using (var connection = new SqliteConnection($"Data Source={path};Pooling=False"))
            {
                await connection.OpenAsync();
                await using SqliteCommand state = connection.CreateCommand();
                state.CommandText = "SELECT (SELECT COUNT(*) FROM hpd_base_subject_lifecycle_maintenance),(SELECT COUNT(*) FROM hpd_base_subject_lifecycle_scope_stage),(SELECT value FROM hpd_base_provider_state WHERE key='subject_scope_protection_key_id');";
                await using SqliteDataReader reader = await state.ExecuteReaderAsync();
                (await reader.ReadAsync()).Should().BeTrue();
                reader.GetInt64(0).Should().Be(1);
                reader.GetInt64(1).Should().Be(1);
                reader.GetString(2).Should().Be("31");
                if (corruptStage)
                {
                    await reader.DisposeAsync();
                    await using SqliteCommand corrupt = connection.CreateCommand();
                    corrupt.CommandText = "UPDATE hpd_base_subject_lifecycle_scope_stage SET replacement_value=randomblob(length(replacement_value));";
                    (await corrupt.ExecuteNonQueryAsync()).Should().Be(1);
                }
            }
            var resumedProcessor = new BaseSubjectLifecycleMaintenanceProcessor();
            RecordMutationExecutionResult resumed = await store.ExecuteMaintenanceAsync(resumedProcessor, request);
            if (corruptStage)
            {
                resumed.Outcome.Should().Be(RecordMutationExecutionOutcome.RollbackConfirmed);
                resumed.Error?.Code.Should().Be(BaseSubjectErrorCodes.LifecycleProviderContractInvalid);
                await using var connection = new SqliteConnection($"Data Source={path};Pooling=False");
                await connection.OpenAsync();
                await using SqliteCommand state = connection.CreateCommand();
                state.CommandText = "SELECT (SELECT COUNT(*) FROM hpd_base_subject_lifecycle_maintenance),(SELECT value FROM hpd_base_provider_state WHERE key='subject_scope_protection_key_id');";
                await using SqliteDataReader reader = await state.ExecuteReaderAsync();
                (await reader.ReadAsync()).Should().BeTrue();
                reader.GetInt64(0).Should().Be(1);
                reader.GetString(1).Should().Be("31");
                return;
            }
            resumed.Outcome.Should().Be(RecordMutationExecutionOutcome.Committed, resumed.Error?.Code ?? resumed.Processing?.Error?.Code);
            resumedProcessor.Result!.DeliveryEpoch.Should().Be(2);
            BaseProtectedSubjectScope expected = scopes.Protect(logicalScope, 32);
            await using (var connection = new SqliteConnection($"Data Source={path};Pooling=False"))
            {
                await connection.OpenAsync();
                await using SqliteCommand state = connection.CreateCommand();
                state.CommandText = "SELECT scope_index_digest,(SELECT COUNT(*) FROM hpd_base_subject_lifecycle_maintenance),(SELECT COUNT(*) FROM hpd_base_subject_lifecycle_scope_stage) FROM hpd_base_subject_lifecycle_facts;";
                await using SqliteDataReader reader = await state.ExecuteReaderAsync();
                (await reader.ReadAsync()).Should().BeTrue();
                ((byte[])reader.GetValue(0)).Should().Equal(expected.IndexDigest);
                reader.GetInt64(1).Should().Be(0);
                reader.GetInt64(2).Should().Be(0);
            }
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            foreach (string candidate in new[] { path, path + "-wal", path + "-shm" }) if (File.Exists(candidate)) File.Delete(candidate);
        }
    }

    [Fact]
    public void Restore_revision_derivation_is_positive_deterministic_and_context_bound()
    {
        long expected = SqliteRecordStore.RestoreDerivedRevision(7, 19);
        expected.Should().BePositive();
        SqliteRecordStore.RestoreDerivedRevision(7, 19).Should().Be(expected);
        SqliteRecordStore.RestoreDerivedRevision(8, 19).Should().NotBe(expected);
        SqliteRecordStore.RestoreDerivedRevision(7, 20).Should().NotBe(expected);
    }

    [Fact]
    public async Task Rotation_resumes_from_a_durable_page_checkpoint_and_remains_closed_between_attempts()
    {
        string path = Path.Combine(Path.GetTempPath(), $"hpd-base-subject-resume-{Guid.NewGuid():N}.db");
        const string checksum = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
        CollectionDefinition collection = ConsumerCollection(checksum);
        var interruption = new OneShotSubjectRewriteInterruption();
        var options = new HPDBaseSqliteOptions
        {
            StoreId = $"subject-resume-{Guid.NewGuid():N}", DataSource = path, EnableWal = false,
            AdministrationEnabled = true, Collections = [collection], ExportedSubjects = [SubjectDefinition(checksum)],
        };
        try
        {
            await using SqliteRecordStore store = SqliteTestFactory.Create(options, administrationOperations: interruption);
            await store.InitializeUnacceptedSchemaForTestsAsync();
            byte[] epoch = await ReadEpochAsync(path);
            for (int index = 0; index < 300; index++)
                (await store.CreateAsync(collection, Create($"consumer-{index:D4}", Reference(epoch, 7)),
                    Operation(BaseOperationKind.Create, collection.Id))).IsSuccess().Should().BeTrue();

            OperationResult<BaseSubjectEpochRotationResult> interrupted = await store.RotateEpochAsync(Rotation(1));
            interrupted.IsSuccess().Should().BeFalse();
            (await store.GetAsync(collection, new RecordId("consumer-0000"), Operation(BaseOperationKind.Get, collection.Id)))
                .IsSuccess().Should().BeFalse();

            OperationResult<BaseSubjectEpochRotationResult> resumed = await store.RotateEpochAsync(Rotation(1));
            resumed.IsSuccess().Should().BeTrue(resumed.Error?.Code);
            resumed.Value!.ExaminedRecords.Should().Be(300);
            resumed.Value.RewrittenReferences.Should().Be(300);
            (await store.GetAsync(collection, new RecordId("consumer-0299"), Operation(BaseOperationKind.Get, collection.Id)))
                .IsSuccess().Should().BeTrue();
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            foreach (string candidate in new[] { path, path + "-wal", path + "-shm" })
                if (File.Exists(candidate)) File.Delete(candidate);
        }
    }

    [Theory]
    [InlineData("UPDATE hpd_base_subject_maintenance SET checksum=lower(hex(randomblob(32))) WHERE singleton=1;")]
    [InlineData("UPDATE hpd_base_subject_rewrite_stage SET payload_json=x'7B7D' WHERE record_id='consumer-0000';")]
    [InlineData("UPDATE hpd_base_subject_rewrite_stage SET previous_revision=previous_revision+7 WHERE record_id='consumer-0000';")]
    [InlineData("UPDATE hpd_base_subject_rewrite_stage SET replacement_revision=replacement_revision+7 WHERE record_id='consumer-0000';")]
    public async Task Rotation_rejects_corrupt_checkpoint_or_staged_payload_and_remains_closed(string corruption)
    {
        string path = Path.Combine(Path.GetTempPath(), $"hpd-base-subject-corrupt-{Guid.NewGuid():N}.db");
        const string checksum = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
        CollectionDefinition collection = ConsumerCollection(checksum);
        var options = new HPDBaseSqliteOptions
        {
            StoreId = $"subject-corrupt-{Guid.NewGuid():N}", DataSource = path, EnableWal = false,
            AdministrationEnabled = true, Collections = [collection], ExportedSubjects = [SubjectDefinition(checksum)],
        };
        try
        {
            await using SqliteRecordStore store = SqliteTestFactory.Create(
                options, administrationOperations: new OneShotSubjectRewriteInterruption());
            byte[] epoch = await ReadEpochAsync(path);
            for (int index = 0; index < 300; index++)
                (await store.CreateAsync(collection, Create($"consumer-{index:D4}", Reference(epoch, 7)),
                    Operation(BaseOperationKind.Create, collection.Id))).IsSuccess().Should().BeTrue();
            (await store.RotateEpochAsync(Rotation(1))).IsSuccess().Should().BeFalse();

            await using (var connection = new SqliteConnection($"Data Source={path};Pooling=False"))
            {
                await connection.OpenAsync();
                await using SqliteCommand command = connection.CreateCommand();
                command.CommandText = corruption;
                await command.ExecuteNonQueryAsync();
            }

            OperationResult<BaseSubjectEpochRotationResult> resumed = await store.RotateEpochAsync(Rotation(1));
            resumed.IsSuccess().Should().BeFalse();
            resumed.Error!.Code.Should().Be(BaseSubjectErrorCodes.ProviderContractInvalid);
            (await store.GetAsync(collection, new RecordId("consumer-0000"), Operation(BaseOperationKind.Get, collection.Id)))
                .IsSuccess().Should().BeFalse();
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            foreach (string candidate in new[] { path, path + "-wal", path + "-shm" })
                if (File.Exists(candidate)) File.Delete(candidate);
        }
    }

    [Fact]
    public async Task Rotation_projection_receives_true_previous_payload_and_changed_fields()
    {
        string path = Path.Combine(Path.GetTempPath(), $"hpd-base-subject-projection-{Guid.NewGuid():N}.db");
        const string checksum = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
        CollectionDefinition collection = ConsumerCollection(checksum);
        var observer = new SubjectProjectionObserver();
        try
        {
            await using SqliteRecordStore store = SqliteTestFactory.Create(new HPDBaseSqliteOptions
            {
                StoreId = $"subject-projection-{Guid.NewGuid():N}", DataSource = path, EnableWal = false,
                AdministrationEnabled = true, Collections = [collection], ExportedSubjects = [SubjectDefinition(checksum)],
            }, mutationProjectionContributors: [observer]);
            byte[] epoch = await ReadEpochAsync(path);
            (await store.CreateAsync(collection, Create("consumer-one", Reference(epoch, 7)),
                Operation(BaseOperationKind.Create, collection.Id))).IsSuccess().Should().BeTrue();
            observer.Facts.Clear();

            (await store.RotateEpochAsync(Rotation(1))).IsSuccess().Should().BeTrue();

            BaseAtomicMutationProjectionFact fact = observer.Facts.Should().ContainSingle().Subject;
            fact.Before.Should().NotBeNull();
            fact.After.Should().NotBeNull();
            fact.Before!.Fields.Single(field => field.StableFieldId == "consumer.owner").Value.CanonicalJsonUtf8
                .Should().NotEqual(fact.After!.Fields.Single(field => field.StableFieldId == "consumer.owner").Value.CanonicalJsonUtf8);
            fact.ChangedFieldIds.Should().Equal("consumer.owner");
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            foreach (string candidate in new[] { path, path + "-wal", path + "-shm" })
                if (File.Exists(candidate)) File.Delete(candidate);
        }
    }

    [Fact]
    public async Task Interrupted_final_publication_rolls_back_and_resumes_from_verified_stage()
    {
        string path = Path.Combine(Path.GetTempPath(), $"hpd-base-subject-finalize-{Guid.NewGuid():N}.db");
        const string checksum = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
        CollectionDefinition collection = ConsumerCollection(checksum);
        var options = new HPDBaseSqliteOptions
        {
            StoreId = $"subject-finalize-{Guid.NewGuid():N}", DataSource = path, EnableWal = false,
            AdministrationEnabled = true, Collections = [collection], ExportedSubjects = [SubjectDefinition(checksum)],
        };
        try
        {
            await using SqliteRecordStore store = SqliteTestFactory.Create(
                options, administrationOperations: new OneShotPhaseInterruption("subjectRewriteBeforePublicationCommit"));
            byte[] epoch = await ReadEpochAsync(path);
            (await store.CreateAsync(collection, Create("consumer-one", Reference(epoch, 7)),
                Operation(BaseOperationKind.Create, collection.Id))).IsSuccess().Should().BeTrue();

            (await store.RotateEpochAsync(Rotation(1))).IsSuccess().Should().BeFalse();
            (await store.GetAsync(collection, new RecordId("consumer-one"), Operation(BaseOperationKind.Get, collection.Id)))
                .IsSuccess().Should().BeFalse();

            OperationResult<BaseSubjectEpochRotationResult> resumed = await store.RotateEpochAsync(Rotation(1));
            resumed.IsSuccess().Should().BeTrue(resumed.Error?.Code);
            resumed.Value!.PublishedStateGeneration.Should().Be(2);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            foreach (string candidate in new[] { path, path + "-wal", path + "-shm" })
                if (File.Exists(candidate)) File.Delete(candidate);
        }
    }

    [Fact]
    public async Task Rotation_revision_overflow_fails_closed_without_publication()
    {
        string path = Path.Combine(Path.GetTempPath(), $"hpd-base-subject-overflow-{Guid.NewGuid():N}.db");
        const string checksum = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
        CollectionDefinition collection = ConsumerCollection(checksum);
        try
        {
            await using SqliteRecordStore store = SqliteTestFactory.Create(new HPDBaseSqliteOptions
            {
                StoreId = $"subject-overflow-{Guid.NewGuid():N}", DataSource = path, EnableWal = false,
                AdministrationEnabled = true, Collections = [collection], ExportedSubjects = [SubjectDefinition(checksum)],
            });
            byte[] epoch = await ReadEpochAsync(path);
            (await store.CreateAsync(collection, Create("consumer-one", Reference(epoch, 7)),
                Operation(BaseOperationKind.Create, collection.Id))).IsSuccess().Should().BeTrue();
            await using (var connection = new SqliteConnection($"Data Source={path};Pooling=False"))
            {
                await connection.OpenAsync();
                await using SqliteCommand command = connection.CreateCommand();
                command.CommandText = "SELECT name FROM sqlite_master WHERE type='table' AND name LIKE 'b_c_%';";
                string table = (string)(await command.ExecuteScalarAsync())!;
                command.Parameters.Clear();
                command.CommandText = $"UPDATE {table} SET revision=$revision WHERE record_id='consumer-one';";
                command.Parameters.AddWithValue("$revision", long.MaxValue);
                await command.ExecuteNonQueryAsync();
            }

            OperationResult<BaseSubjectEpochRotationResult> result = await store.RotateEpochAsync(Rotation(1));
            result.IsSuccess().Should().BeFalse();
            result.Error!.Code.Should().Be(BaseSubjectErrorCodes.ProviderContractInvalid);
            (await ReadPublicationStateAsync(path)).Generation.Should().Be(1);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            foreach (string candidate in new[] { path, path + "-wal", path + "-shm" })
                if (File.Exists(candidate)) File.Delete(candidate);
        }
    }

    [Fact]
    public async Task LoweringReceiptUsesExactInstalledPlanStoreAndSchemaAuthority()
    {
        const string checksum = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
        BaseExportedSubjectDefinition definition = SubjectDefinition(checksum);
        await using var store = new SqliteRecordStore(new HPDBaseSqliteOptions
        {
            StoreId = "sqlite-subject-receipts",
            DataSource = ":memory:",
            ExportedSubjects = [definition],
        }, NullLoggerFactory.Instance);
        await store.InitializeUnacceptedSchemaForTestsAsync();

        OperationResult<BaseSubjectValidationPlanReceipt[]> result =
            await store.ReadSubjectValidationPlanReceiptsAsync();

        result.IsSuccess().Should().BeTrue();
        BaseSubjectValidationPlanReceipt receipt = result.Value!.Should().ContainSingle().Subject;
        receipt.PlanId.Should().Be(definition.ValidationPlan.Id);
        receipt.PlanVersion.Should().Be(definition.ValidationPlan.Version);
        receipt.PlanChecksum.Should().Be(BaseSubjectContractNormalizer.NormalizePlan(definition.ValidationPlan).Checksum);
        receipt.StoreInstanceId.Should().Be("sqlite-subject-receipts");
        receipt.SchemaGeneration.Should().Be(store.VectorSchemaGeneration);
        receipt.Access.Should().Be(BaseSubjectValidationAccessShape.ContractAndSubjectPrimaryKeys);
        receipt.LoweringFormatVersion.Should().Be(1);
    }

    [Fact]
    public async Task RestorePublishesFreshEpochAndMaxPlusOneGenerationWhilePreservingHistoricalJournal()
    {
        string path = Path.Combine(AdministrationTempDirectory(), $"hpd-base-subject-restore-{Guid.NewGuid():N}.db");
        const string checksum = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
        CollectionDefinition collection = ConsumerCollection(checksum);
        using var protector = new BaseOpaqueTokenProtector(Options.Create(new HPDBaseTokenProtectionOptions
        {
            ActiveKey = new BaseOpaqueTokenKey
            {
                Id = 31,
                Key = Enumerable.Repeat((byte)0x31, 32).ToArray(),
                IssueNotBefore = DateTimeOffset.UnixEpoch,
            },
        }));
        var options = new HPDBaseSqliteOptions
        {
            StoreId = "sqlite-subject-restore",
            DataSource = path,
            EnableWal = false,
            AdministrationEnabled = true,
            Collections = [collection],
            ExportedSubjects = [SubjectDefinition(checksum)],
            MaxBackupArtifactBytes = 16 * 1024 * 1024,
        };
        var restorePages = new CountingPhaseController("subjectLifecycleRestorePageCommitted");
        try
        {
            await using var store = new SqliteRecordStore(
                options,
                NullLoggerFactory.Instance,
                TimeProvider.System,
                administrationOperations: restorePages,
                tokenProtector: protector);
            store.AdministrationCapability.Backup.Should().BeTrue(options.DataSource);
            await store.InitializeUnacceptedSchemaForTestsAsync();
            InitializeAuthorityMetadata(path);
            byte[] artifactEpoch = await ReadEpochAsync(path);
            JsonElement reference = Reference(artifactEpoch, 9);
            (await store.CreateAsync(
                collection,
                Create("consumer-one", reference),
                Operation(BaseOperationKind.Create, collection.Id))).IsSuccess().Should().BeTrue();
            await InsertLifecycleProjectionAuthorityAsync(path, 257);

            var destination = new MemoryStream();
            OperationResult<BaseBackupManifest> backup = await store.CreateBackupAsync(
                destination,
                new BaseBackupRequest { StoreId = options.StoreId, Principal = SystemPrincipal() });
            backup.IsSuccess().Should().BeTrue(backup.Error?.Code);
            BaseBackupManifest manifest = backup.Value!;
            byte[] artifact = destination.ToArray();

            (await store.RotateEpochAsync(Rotation(1))).IsSuccess().Should().BeTrue();
            byte[] rotatedEpoch = await ReadEpochAsync(path);
            rotatedEpoch.Should().NotEqual(artifactEpoch);

            OperationResult<BaseRestoreResult> restored = await store.RestoreAsync(
                new MemoryStream(artifact),
                new BaseRestoreRequest
                {
                    StoreId = options.StoreId,
                    Principal = SystemPrincipal(),
                    ExpectedCurrentStoreIdentityDigest = manifest.StoreIdentityDigest,
                    ExpectedArtifactStoreIdentityDigest = manifest.StoreIdentityDigest,
                    IdentityMode = BaseRestoreIdentityMode.RequireCurrentStoreIdentity,
                    RecoveryImageRetention = BaseRecoveryImageRetention.DeleteAfterSuccessfulRestore,
                    ConfirmDestructiveReplacement = true,
                });

            restored.IsSuccess().Should().BeTrue(restored.Error?.Code);
            byte[] restoredEpoch = await ReadEpochAsync(path);
            restoredEpoch.Should().NotEqual(artifactEpoch);
            restoredEpoch.Should().NotEqual(rotatedEpoch);
            (long generation, long previous, int kind) = await ReadPublicationStateAsync(path);
            generation.Should().Be(3);
            previous.Should().Be(2);
            kind.Should().Be((int)BaseSubjectAuthorityPublicationKind.RestoreTransformation);
            RecordEnvelope current = (await store.GetAsync(
                collection,
                new RecordId("consumer-one"),
                Operation(BaseOperationKind.Get, collection.Id))).Value!;
            current.Payload.Fields!["owner"].GetProperty("authorityEpoch").GetString().Should().Be(Encode(restoredEpoch));
            current.Payload.Fields!["owner"].GetProperty("incarnation").GetString().Should().Be(Encode(Incarnation(9)));
            current.Metadata.Revision!.Value.Value.Should().NotBe("sqlite:1");
            BaseMutationJournalPage journal = await store.ReadMutationJournalAsync(new BaseMutationJournalReadRequest { Limit = 16 });
            journal.Entries.Should().HaveCount(3);
            journal.Entries[0].SubjectAuthorityPublication!.Kind.Should().Be(BaseSubjectAuthorityPublicationKind.InitialInstallation);
            journal.Entries[1].Kind.Should().Be(BaseMutationJournalEntryKind.RecordMutation);
            journal.Entries[2].SubjectAuthorityPublication!.Kind.Should().Be(BaseSubjectAuthorityPublicationKind.RestoreTransformation);
            (long deliveryEpoch, long projectionGeneration) = await ReadLifecycleProjectionAuthorityAsync(path);
            deliveryEpoch.Should().Be(2);
            projectionGeneration.Should().Be(2);
            restorePages.Count.Should().Be(2, "257 lifecycle consumer rows require two fixed 256-row restore pages");
            (long restoredConsumers, long minimumProjection, long maximumProjection) = await ReadLifecycleConsumerGenerationsAsync(path);
            restoredConsumers.Should().Be(257);
            minimumProjection.Should().Be(2);
            maximumProjection.Should().Be(2);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            foreach (string candidate in Directory.GetFiles(Path.GetDirectoryName(path)!, Path.GetFileName(path) + "*"))
                File.Delete(candidate);
        }
    }

    [Fact]
    public async Task InterruptedPagedLifecycleRestoreRecoversCompleteOriginalAuthority()
    {
        string path = Path.Combine(AdministrationTempDirectory(), $"hpd-base-subject-restore-interrupt-{Guid.NewGuid():N}.db");
        const string checksum = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
        CollectionDefinition collection = ConsumerCollection(checksum);
        using var protector = new BaseOpaqueTokenProtector(Options.Create(new HPDBaseTokenProtectionOptions
        {
            ActiveKey = new BaseOpaqueTokenKey
            {
                Id = 31,
                Key = Enumerable.Repeat((byte)0x31, 32).ToArray(),
                IssueNotBefore = DateTimeOffset.UnixEpoch,
            },
        }));
        var options = new HPDBaseSqliteOptions
        {
            StoreId = "sqlite-subject-restore-interrupt",
            DataSource = path,
            EnableWal = false,
            AdministrationEnabled = true,
            Collections = [collection],
            ExportedSubjects = [SubjectDefinition(checksum)],
            MaxBackupArtifactBytes = 16 * 1024 * 1024,
        };
        try
        {
            await using var store = new SqliteRecordStore(
                options,
                NullLoggerFactory.Instance,
                TimeProvider.System,
                administrationOperations: new OneShotPhaseInterruption("subjectLifecycleRestorePageCommitted"),
                tokenProtector: protector);
            await store.InitializeUnacceptedSchemaForTestsAsync();
            InitializeAuthorityMetadata(path);
            byte[] artifactEpoch = await ReadEpochAsync(path);
            (await store.CreateAsync(
                collection,
                Create("consumer-one", Reference(artifactEpoch, 9)),
                Operation(BaseOperationKind.Create, collection.Id))).IsSuccess().Should().BeTrue();
            await InsertLifecycleProjectionAuthorityAsync(path, 257);

            var destination = new MemoryStream();
            OperationResult<BaseBackupManifest> backup = await store.CreateBackupAsync(
                destination,
                new BaseBackupRequest { StoreId = options.StoreId, Principal = SystemPrincipal() });
            backup.IsSuccess().Should().BeTrue(backup.Error?.Code);
            byte[] artifact = destination.ToArray();

            (await store.RotateEpochAsync(Rotation(1))).IsSuccess().Should().BeTrue();
            byte[] expectedOriginalEpoch = await ReadEpochAsync(path);
            (long expectedGeneration, long expectedPrevious, int expectedKind) = await ReadPublicationStateAsync(path);

            OperationResult<BaseRestoreResult> restored = await store.RestoreAsync(
                new MemoryStream(artifact),
                new BaseRestoreRequest
                {
                    StoreId = options.StoreId,
                    Principal = SystemPrincipal(),
                    ExpectedCurrentStoreIdentityDigest = backup.Value!.StoreIdentityDigest,
                    ExpectedArtifactStoreIdentityDigest = backup.Value.StoreIdentityDigest,
                    IdentityMode = BaseRestoreIdentityMode.RequireCurrentStoreIdentity,
                    RecoveryImageRetention = BaseRecoveryImageRetention.DeleteAfterSuccessfulRestore,
                    ConfirmDestructiveReplacement = true,
                });

            restored.IsSuccess().Should().BeFalse();
            restored.Error!.Code.Should().Be(BaseAdministrationErrorCodes.RestoreFailed);
            (await ReadEpochAsync(path)).Should().Equal(expectedOriginalEpoch);
            (await ReadPublicationStateAsync(path)).Should().Be((expectedGeneration, expectedPrevious, expectedKind));
            (long deliveryEpoch, long projectionGeneration) = await ReadLifecycleProjectionAuthorityAsync(path);
            deliveryEpoch.Should().Be(1);
            projectionGeneration.Should().Be(1);
            (long count, long minimum, long maximum) = await ReadLifecycleConsumerGenerationsAsync(path);
            count.Should().Be(257);
            minimum.Should().Be(1);
            maximum.Should().Be(1);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            foreach (string candidate in Directory.GetFiles(Path.GetDirectoryName(path)!, Path.GetFileName(path) + "*"))
                File.Delete(candidate);
        }
    }

    private static async ValueTask InsertLifecycleProjectionAuthorityAsync(string path, int count = 1)
    {
        await using var connection = new SqliteConnection($"Data Source={path};Pooling=False");
        await connection.OpenAsync();
        await using SqliteTransaction transaction = (SqliteTransaction)await connection.BeginTransactionAsync();
        for (int index = 0; index < count; index++)
        {
            await using SqliteCommand command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = "INSERT INTO hpd_base_subject_lifecycle_consumers(consumer_id,consumer_version,consumer_checksum,contract_id,contract_version,projection_generation,cutoff_position,published_graph_generation,state) VALUES($consumer,1,$checksum,'example.subject',1,1,0,1,0);";
            command.Parameters.AddWithValue("$consumer", index == 0 ? "restore-consumer" : $"restore-consumer-{index:D3}");
            command.Parameters.AddWithValue("$checksum", new string('b', 64));
            await command.ExecuteNonQueryAsync();
        }
        await transaction.CommitAsync();
    }

    private static async ValueTask<(long Count, long Minimum, long Maximum)> ReadLifecycleConsumerGenerationsAsync(string path)
    {
        await using var connection = new SqliteConnection($"Data Source={path};Pooling=False");
        await connection.OpenAsync();
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*),MIN(projection_generation),MAX(projection_generation) FROM hpd_base_subject_lifecycle_consumers;";
        await using SqliteDataReader reader = await command.ExecuteReaderAsync();
        (await reader.ReadAsync()).Should().BeTrue();
        return (reader.GetInt64(0), reader.GetInt64(1), reader.GetInt64(2));
    }

    private static async ValueTask<(long DeliveryEpoch, long ProjectionGeneration)> ReadLifecycleProjectionAuthorityAsync(string path)
    {
        await using var connection = new SqliteConnection($"Data Source={path};Pooling=False");
        await connection.OpenAsync();
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = "SELECT CAST((SELECT value FROM hpd_base_provider_state WHERE key='subject_lifecycle_delivery_epoch') AS INTEGER),(SELECT projection_generation FROM hpd_base_subject_lifecycle_consumers WHERE consumer_id='restore-consumer' AND consumer_version=1);";
        await using SqliteDataReader reader = await command.ExecuteReaderAsync();
        (await reader.ReadAsync()).Should().BeTrue();
        return (reader.GetInt64(0), reader.GetInt64(1));
    }

    [Fact]
    public async Task RotationRewritesCurrentReferencesAndPublishesOneAtomicGeneration()
    {
        string path = Path.Combine(Path.GetTempPath(), $"hpd-base-subject-{Guid.NewGuid():N}.db");
        const string checksum = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
        CollectionDefinition collection = ConsumerCollection(checksum);
        var options = new HPDBaseSqliteOptions
        {
            StoreId = $"subject-{Guid.NewGuid():N}",
            DataSource = path,
            EnableWal = false,
            AdministrationEnabled = true,
            Collections = [collection],
            ExportedSubjects = [SubjectDefinition(checksum)],
        };
        try
        {
            await using var store = new SqliteRecordStore(options, NullLoggerFactory.Instance);
            await store.InitializeUnacceptedSchemaForTestsAsync();
            byte[] initialEpoch = await ReadEpochAsync(path);
            JsonElement reference = Reference(initialEpoch, 7);
            OperationResult<RecordEnvelope> created = await store.CreateAsync(
                collection,
                new RecordCreateRequest
                {
                    RequestedId = new RecordId("consumer-one"),
                    Payload = new RecordPayload
                    {
                        Kind = RecordPayloadKind.FieldMap,
                        Fields = new Dictionary<string, JsonElement>(StringComparer.Ordinal) { ["owner"] = reference },
                    },
                },
                new OperationContext { Operation = BaseOperationKind.Create, CollectionId = collection.Id });
            created.IsSuccess().Should().BeTrue();

            OperationResult<BaseSubjectEpochRotationResult> rotated = await store.RotateEpochAsync(
                new BaseSubjectEpochRotationRequest
                {
                    ContractId = "example.subject",
                    ContractVersion = 1,
                    ExpectedStateGeneration = 1,
                    DestructiveIntent = "rotate-subject-authority-epoch",
                });

            rotated.IsSuccess().Should().BeTrue(rotated.Error?.Code);
            rotated.Value!.PreviousStateGeneration.Should().Be(1);
            rotated.Value.PublishedStateGeneration.Should().Be(2);
            rotated.Value.ExaminedRecords.Should().Be(1);
            rotated.Value.RewrittenReferences.Should().Be(1);
            RecordEnvelope current = (await store.GetAsync(
                collection,
                new RecordId("consumer-one"),
                new OperationContext { Operation = BaseOperationKind.Get, CollectionId = collection.Id })).Value!;
            current.Metadata.Revision!.Value.Value.Should().Be("sqlite:2");
            JsonElement rewritten = current.Payload.Fields!["owner"];
            rewritten.GetProperty("authorityEpoch").GetString().Should().NotBe(Encode(initialEpoch));
            rewritten.GetProperty("subjectId").GetString().Should().Be("subject-one");
            rewritten.GetProperty("incarnation").GetString().Should().Be(Encode(Incarnation(7)));
            BaseMutationJournalPage journal = await store.ReadMutationJournalAsync(new BaseMutationJournalReadRequest { Limit = 16 });
            journal.Entries.Select(static entry => entry.Kind).Should().Equal(
                BaseMutationJournalEntryKind.SubjectAuthorityPublication,
                BaseMutationJournalEntryKind.RecordMutation,
                BaseMutationJournalEntryKind.SubjectAuthorityPublication);
            journal.Entries[^1].SubjectAuthorityPublication!.PublishedStateGeneration.Should().Be(2);

            OperationResult<BaseSubjectEpochRotationResult> stale = await store.RotateEpochAsync(
                new BaseSubjectEpochRotationRequest
                {
                    ContractId = "example.subject",
                    ContractVersion = 1,
                    ExpectedStateGeneration = 1,
                    DestructiveIntent = "rotate-subject-authority-epoch",
                });
            stale.Status.Should().Be(OperationStatus.Conflict);
            stale.Error!.Code.Should().Be(BaseSubjectErrorCodes.SchemaGenerationChanged);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            foreach (string candidate in new[] { path, path + "-wal", path + "-shm" })
                if (File.Exists(candidate)) File.Delete(candidate);
        }
    }

    private static async ValueTask<byte[]> ReadEpochAsync(string path)
    {
        await using var connection = new SqliteConnection($"Data Source={path};Mode=ReadWrite;Pooling=False");
        await connection.OpenAsync();
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = "SELECT authority_epoch FROM hpd_base_subject_contracts WHERE contract_id='example.subject' AND contract_version=1;";
        return (byte[])(await command.ExecuteScalarAsync())!;
    }

    private static async ValueTask<(long Generation, long Previous, int Kind)> ReadPublicationStateAsync(string path)
    {
        await using var connection = new SqliteConnection($"Data Source={path};Mode=ReadWrite;Pooling=False");
        await connection.OpenAsync();
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = "SELECT state_generation,publication_previous_generation,publication_kind FROM hpd_base_subject_contracts WHERE contract_id='example.subject' AND contract_version=1;";
        await using SqliteDataReader reader = await command.ExecuteReaderAsync();
        (await reader.ReadAsync()).Should().BeTrue();
        return (reader.GetInt64(0), reader.GetInt64(1), reader.GetInt32(2));
    }

    private sealed class OneShotSubjectRewriteInterruption : ISqliteAdministrationOperationController
    {
        private int _remaining = 1;
        public ValueTask BeforePhaseAsync(string phase, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (phase == "subjectRewritePageCommitted" && Interlocked.Exchange(ref _remaining, 0) == 1)
                throw new IOException("Injected interruption after a durable subject rewrite page.");
            return ValueTask.CompletedTask;
        }

        public void DeleteFile(string path) => File.Delete(path);
    }

    private sealed class OneShotPhaseInterruption(string target) : ISqliteAdministrationOperationController
    {
        private int _remaining = 1;
        public ValueTask BeforePhaseAsync(string phase, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (phase == target && Interlocked.Exchange(ref _remaining, 0) == 1)
                throw new IOException("Injected interruption before subject publication commit.");
            return ValueTask.CompletedTask;
        }
        public void DeleteFile(string path) => File.Delete(path);
    }

    private sealed class CountingPhaseController(string target) : ISqliteAdministrationOperationController
    {
        private int _count;
        public int Count => Volatile.Read(ref _count);
        public ValueTask BeforePhaseAsync(string phase, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (phase == target) Interlocked.Increment(ref _count);
            return ValueTask.CompletedTask;
        }
        public void DeleteFile(string path) => File.Delete(path);
    }

    private sealed class SubjectProjectionObserver : ISqliteAtomicMutationProjection, ISqliteAtomicMutationProjectionCatalog
    {
        public string Id => "subject-projection-observer";
        public IReadOnlyList<SqliteProjectionStatement> Statements => [];
        public IReadOnlyList<string> SchemaStatements => [];
        public IReadOnlyList<string> RequiredSchemaTables => [];
        public IReadOnlyList<SqliteProjectionTableShape> RequiredSchemaShapes => [];
        public List<BaseAtomicMutationProjectionFact> Facts { get; } = [];
        public ValueTask<OperationResult> ApplyAsync(ISqliteAtomicProjectionContext context, BaseAtomicMutationProjectionRequest request, CancellationToken cancellationToken = default)
        {
            Facts.AddRange(request.Mutations);
            return ValueTask.FromResult(OperationResults.NoContent());
        }
    }

    private static void InitializeAuthorityMetadata(string path)
    {
        using var connection = new SqliteConnection($"Data Source={path};Pooling=False");
        connection.Open();
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = """
INSERT OR IGNORE INTO hpd_base_schema_identity(singleton,store_instance_id) VALUES (1,'subject-restore-instance');
INSERT OR IGNORE INTO hpd_base_schema_baseline(application_id,store_instance_id,baseline_id,checksum,generation,last_plan_id,applied_at)
VALUES ('subject-restore','subject-restore-instance','baseline-1','checksum-1',1,'plan-1','2026-08-13T00:00:00Z');
""";
        command.ExecuteNonQuery();
    }

    private static JsonElement Reference(byte[] epoch, byte incarnation) => JsonSerializer.Deserialize<JsonElement>($$"""
{"subjectId":"subject-one","authorityEpoch":"{{Encode(epoch)}}","incarnation":"{{Encode(Incarnation(incarnation))}}"}
""");

    private static byte[] Incarnation(byte nonce)
    {
        byte[] value = new byte[24];
        System.Buffers.Binary.BinaryPrimitives.WriteInt64BigEndian(value, 1);
        value.AsSpan(8).Fill(nonce);
        return value;
    }

    private static RecordCreateRequest Create(string id, JsonElement reference) => new()
    {
        RequestedId = new RecordId(id),
        Payload = new RecordPayload
        {
            Kind = RecordPayloadKind.FieldMap,
            Fields = new Dictionary<string, JsonElement>(StringComparer.Ordinal) { ["owner"] = reference },
        },
    };

    private static BaseSubjectEpochRotationRequest Rotation(long generation) => new()
    {
        ContractId = "example.subject",
        ContractVersion = 1,
        ExpectedStateGeneration = generation,
        DestructiveIntent = "rotate-subject-authority-epoch",
    };

    private static OperationContext Operation(BaseOperationKind operation, string collectionId) => new()
    {
        Operation = operation,
        CollectionId = collectionId,
    };

    private static PrincipalContext SystemPrincipal() => new()
    {
        AuthenticationState = PrincipalAuthenticationState.System,
    };

    private static string AdministrationTempDirectory()
    {
        string path = Path.GetFullPath(Path.GetTempPath());
        return OperatingSystem.IsMacOS() && path.StartsWith("/var/", StringComparison.Ordinal)
            ? "/private" + path
            : path;
    }

    private static string Encode(byte[] value) => Convert.ToBase64String(value).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static CollectionDefinition ConsumerCollection(string checksum) => new()
    {
        Id = "consumer.records",
        Name = "consumer.records",
        Kind = BaseCollectionKinds.Document,
        SchemaMode = SchemaMode.Strict,
        UnknownFields = UnknownFieldPolicy.Reject,
        MutationMode = BaseCollectionMutationMode.Mutable,
        Fields =
        [
            new FieldDefinition
            {
                Id = "consumer.owner",
                ApplicationName = "owner",
                WireName = "owner",
                Type = BaseFieldTypes.Object,
                Required = true,
                Nullable = false,
                SubjectReference = new BaseSubjectReferenceDefinition
                {
                    ContractId = "example.subject",
                    ContractVersion = 1,
                    ContractChecksum = checksum,
                    Requirement = BaseSubjectReferenceRequirement.Exists,
                    Guarantee = BaseSubjectValidationGuarantee.TransactionSnapshot,
                },
            },
        ],
    };

    private static BaseExportedSubjectDefinition SubjectDefinition(string checksum) => new()
    {
        Id = "example.subject",
        Version = 1,
        OwningModuleId = "example.module",
        SubjectIdKind = BaseSubjectIdKind.OrdinalString,
        MaximumSubjectIdUtf8Bytes = 64,
        Scope = BaseSubjectScopeKind.Global,
        AcquisitionGrantId = "example.subject.acquire",
        ValidationGrantId = "example.subject.validate",
        AdministrationGrantId = "example.subject.admin",
        TombstoneFieldId = "subject.tombstoned",
        SupportsCoordinatedRetirement = false,
        Audiences = [HPDBaseEndpointAudience.Application],
        ValidationPlan = new BaseSubjectValidationPlanDefinition
        {
            Id = "example.subject.plan",
            Version = 1,
            ContractId = "example.subject",
            ContractVersion = 1,
            ContractChecksum = checksum,
            PrivateCollectionId = "private.subjects",
            SubjectId = BaseSubjectIdBinding.RecordId,
            Active = new BaseSubjectActiveBinding { Kind = BaseSubjectActiveBindingKind.RequiredBooleanField, FieldId = "subject.active", ActiveValue = true },
            Scope = new BaseSubjectScopeBinding { Kind = BaseSubjectScopeBindingKind.Global },
            Access = BaseSubjectValidationAccessShape.ContractAndSubjectPrimaryKeys,
            Limits = BaseSubjectValidationLimits.Default,
        },
    };
}
