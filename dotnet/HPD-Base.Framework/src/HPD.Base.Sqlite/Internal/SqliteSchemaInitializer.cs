using HPD.Base.Sqlite;
using HPD.Base;
using Microsoft.Data.Sqlite;

namespace HPD.Base.Sqlite;

internal sealed class SqliteSchemaInitializer
{
    private readonly HPDBaseSqliteOptions _options;
    private readonly SqliteNames _names;
    private readonly SqlitePhysicalModel _physical;
    private readonly string[] _projectionSchemaStatements;
    private readonly string[] _projectionSchemaTables;
    private readonly SqliteProjectionTableShape[] _projectionSchemaShapes;

    /// <summary>Initializes a new instance.</summary>
    public SqliteSchemaInitializer(HPDBaseSqliteOptions options, string[]? projectionSchemaStatements = null, string[]? projectionSchemaTables = null, SqliteProjectionTableShape[]? projectionSchemaShapes = null)
    {
        _options = options;
        _names = new SqliteNames(options);
        _physical = new SqlitePhysicalModel(options);
        _projectionSchemaStatements = projectionSchemaStatements?.ToArray() ?? [];
        _projectionSchemaTables = projectionSchemaTables?.Distinct(StringComparer.Ordinal).ToArray() ?? [];
        _projectionSchemaShapes = projectionSchemaShapes?.ToArray() ?? [];
    }

    /// <summary>Executes the initialize async operation.</summary>
    public ValueTask InitializeAsync(SqliteConnection connection, CancellationToken cancellationToken) =>
        HPDBaseSqliteTelemetry.TraceSchemaAsync(HPDBaseTelemetrySpans.SqliteSchemaInitialize, _options.StoreId, () => InitializeCoreAsync(connection, cancellationToken));

    internal string[] GetExecutionStatements()
    {
        var statements = new List<string>();
        statements.AddRange(_physical.Collections.Select(static collection => collection.CreateSql()));
        statements.AddRange(_physical.Relations.Select(static relation => relation.CreateSql()));
        statements.Add($"""
CREATE TABLE IF NOT EXISTS {_names.Collections} (
  collection_id TEXT NOT NULL PRIMARY KEY,
  schema_hash TEXT NULL,
  registered_at TEXT NOT NULL,
  native_name TEXT NOT NULL,
  mutation_mode INTEGER NOT NULL,
  next_append_position INTEGER NOT NULL DEFAULT 0 CHECK (next_append_position >= 0),
  purge_generation INTEGER NOT NULL DEFAULT 0 CHECK (purge_generation >= 0),
  descriptor_json TEXT NULL
);
CREATE TABLE IF NOT EXISTS {_names.ProviderState} (
  key TEXT NOT NULL PRIMARY KEY,
  value TEXT NOT NULL
);
INSERT OR IGNORE INTO {_names.ProviderState}(key, value) VALUES ('restore_epoch', '0');
INSERT OR IGNORE INTO {_names.ProviderState}(key, value) VALUES ('subject_lifecycle_delivery_epoch', '1');
INSERT OR IGNORE INTO {_names.ProviderState}(key, value) VALUES ('subject_retirement_position', '0');
INSERT OR IGNORE INTO {_names.ProviderState}(key, value) VALUES ('activation_generation', '0');
INSERT OR IGNORE INTO {_names.ProviderState}(key, value) VALUES ('activation_accepted_utc', '0');
CREATE TABLE IF NOT EXISTS {_names.SchemaIdentity} (
  singleton INTEGER NOT NULL PRIMARY KEY CHECK (singleton = 1),
  store_instance_id TEXT NOT NULL
);
CREATE TABLE IF NOT EXISTS {_names.SchemaBaseline} (
  application_id TEXT NOT NULL PRIMARY KEY,
  store_instance_id TEXT NOT NULL,
  baseline_id TEXT NOT NULL,
  checksum TEXT NOT NULL,
  generation INTEGER NOT NULL,
  last_plan_id TEXT NOT NULL,
  applied_at TEXT NOT NULL
);
CREATE TABLE IF NOT EXISTS {_names.SchemaAssets} (
  application_id TEXT NOT NULL,
  logical_id TEXT NOT NULL,
  safe_summary TEXT NOT NULL,
  state INTEGER NOT NULL,
  PRIMARY KEY(application_id, logical_id)
);
CREATE TABLE IF NOT EXISTS {_names.SchemaHistory} (
  application_id TEXT NOT NULL,
  generation INTEGER NOT NULL,
  baseline_id TEXT NOT NULL,
  checksum TEXT NOT NULL,
  plan_id TEXT NOT NULL,
  classification INTEGER NOT NULL,
  outcome INTEGER NOT NULL,
  provider_version TEXT NOT NULL,
  structural_verification INTEGER NOT NULL,
  external_data_migration INTEGER NOT NULL,
  semantic_conversion INTEGER NOT NULL,
  external_attestation_id TEXT NULL,
  external_signer_id TEXT NULL,
  applied_at TEXT NOT NULL,
  PRIMARY KEY(application_id, generation)
);
CREATE TABLE IF NOT EXISTS {_names.SchemaLease} (
  application_id TEXT NOT NULL PRIMARY KEY,
  generation INTEGER NOT NULL,
  owner_token TEXT NULL,
  acquired_at TEXT NULL
);
CREATE TABLE IF NOT EXISTS {_names.SubjectContracts} (
  contract_id TEXT NOT NULL,
  contract_version INTEGER NOT NULL CHECK(contract_version > 0),
  contract_checksum TEXT NOT NULL CHECK(length(contract_checksum) = 64),
  authority_epoch BLOB NOT NULL CHECK(length(authority_epoch) = 16),
  restore_epoch INTEGER NOT NULL CHECK(restore_epoch >= 0),
  state_generation INTEGER NOT NULL CHECK(state_generation > 0),
  publication_previous_generation INTEGER NOT NULL CHECK(publication_previous_generation >= 0),
  publication_kind INTEGER NOT NULL,
  publication_position INTEGER NOT NULL CHECK(publication_position > 0),
  publication_digest TEXT NOT NULL CHECK(length(publication_digest) = 64),
  PRIMARY KEY(contract_id, contract_version)
);
CREATE TABLE IF NOT EXISTS {_names.SubjectLifetimes} (
  contract_id TEXT NOT NULL,
  contract_version INTEGER NOT NULL,
  subject_id TEXT NOT NULL,
  incarnation BLOB NOT NULL CHECK(length(incarnation) = 24),
  lifetime_generation INTEGER NOT NULL CHECK(lifetime_generation > 0),
  lifecycle_state INTEGER NOT NULL CHECK(lifecycle_state BETWEEN 0 AND 2),
  subject_sequence INTEGER NOT NULL CHECK(subject_sequence > 0),
  scope_kind INTEGER NOT NULL CHECK(scope_kind BETWEEN 0 AND 2),
  scope_index_digest BLOB NOT NULL CHECK(length(scope_index_digest)=32),
  protected_scope_value BLOB NOT NULL,
  private_collection_id TEXT NOT NULL,
  private_record_id TEXT NOT NULL,
  created_journal_position INTEGER NOT NULL CHECK(created_journal_position > 0),
  last_lifecycle_position INTEGER NOT NULL CHECK(last_lifecycle_position > 0),
  PRIMARY KEY(scope_kind, scope_index_digest, contract_id, contract_version, subject_id),
  FOREIGN KEY(contract_id, contract_version) REFERENCES {_names.SubjectContracts}(contract_id, contract_version) ON DELETE RESTRICT
);
CREATE TABLE IF NOT EXISTS {_names.SubjectTerminalLifetimes} (
  contract_id TEXT NOT NULL, contract_version INTEGER NOT NULL, subject_id TEXT NOT NULL,
  scope_kind INTEGER NOT NULL CHECK(scope_kind BETWEEN 0 AND 2), scope_index_digest BLOB NOT NULL CHECK(length(scope_index_digest)=32), protected_scope_value BLOB NOT NULL,
  retired_authority_epoch BLOB NOT NULL CHECK(length(retired_authority_epoch)=16),
  retired_incarnation BLOB NOT NULL CHECK(length(retired_incarnation)=24),
  retired_lifetime_generation INTEGER NOT NULL CHECK(retired_lifetime_generation > 0),
  retired_subject_sequence INTEGER NOT NULL CHECK(retired_subject_sequence > 0),
  retired_position INTEGER NOT NULL CHECK(retired_position > 0), contract_state_generation INTEGER NOT NULL CHECK(contract_state_generation > 0),
  restore_epoch INTEGER NOT NULL CHECK(restore_epoch >= 0), receipt_checksum TEXT NOT NULL CHECK(length(receipt_checksum)=64),
  PRIMARY KEY(scope_kind,scope_index_digest,contract_id,contract_version,subject_id)
);
CREATE TABLE IF NOT EXISTS {_names.SubjectLifecycleFacts} (
  commit_position INTEGER NOT NULL CHECK(commit_position > 0), contract_id TEXT NOT NULL, contract_version INTEGER NOT NULL,
  subject_id TEXT NOT NULL, authority_epoch BLOB NOT NULL CHECK(length(authority_epoch)=16), incarnation BLOB NOT NULL CHECK(length(incarnation)=24),
  subject_sequence INTEGER NOT NULL CHECK(subject_sequence > 0), contract_state_generation INTEGER NOT NULL CHECK(contract_state_generation > 0),
  delivery_epoch INTEGER NOT NULL CHECK(delivery_epoch > 0), fact_kind INTEGER NOT NULL CHECK(fact_kind BETWEEN 0 AND 2),
  previous_state INTEGER NULL, current_state INTEGER NULL, scope_kind INTEGER NOT NULL CHECK(scope_kind BETWEEN 0 AND 2), scope_index_digest BLOB NOT NULL CHECK(length(scope_index_digest)=32), protected_scope_value BLOB NOT NULL,
  PRIMARY KEY(commit_position,subject_id,authority_epoch,incarnation,subject_sequence)
);
CREATE TABLE IF NOT EXISTS {_names.SubjectLifecycleMemberships} (
  consumer_id TEXT NOT NULL, consumer_version INTEGER NOT NULL, consumer_checksum TEXT NOT NULL CHECK(length(consumer_checksum)=64),
  contract_id TEXT NOT NULL, contract_version INTEGER NOT NULL, projection_generation INTEGER NOT NULL CHECK(projection_generation > 0),
  matched_state INTEGER NOT NULL CHECK(matched_state BETWEEN 0 AND 3), scope_kind INTEGER NOT NULL CHECK(scope_kind BETWEEN 0 AND 2), scope_index_digest BLOB NOT NULL CHECK(length(scope_index_digest)=32), protected_scope_value BLOB NOT NULL,
  commit_position INTEGER NOT NULL, subject_id TEXT NOT NULL, authority_epoch BLOB NOT NULL, incarnation BLOB NOT NULL, subject_sequence INTEGER NOT NULL,
  PRIMARY KEY(consumer_id,consumer_version,consumer_checksum,contract_id,contract_version,projection_generation,scope_kind,scope_index_digest,commit_position,subject_id,authority_epoch,incarnation,subject_sequence)
);
CREATE TABLE IF NOT EXISTS {_names.SubjectLifecycleConsumers} (
  consumer_id TEXT NOT NULL, consumer_version INTEGER NOT NULL, consumer_checksum TEXT NOT NULL CHECK(length(consumer_checksum)=64),
  contract_id TEXT NOT NULL, contract_version INTEGER NOT NULL, projection_generation INTEGER NOT NULL CHECK(projection_generation > 0),
  cutoff_position INTEGER NOT NULL CHECK(cutoff_position >= 0), cutoff_subject_id TEXT NULL, cutoff_authority_epoch BLOB NULL,
  cutoff_incarnation BLOB NULL, cutoff_sequence INTEGER NULL, published_graph_generation INTEGER NOT NULL CHECK(published_graph_generation > 0),
  installed_at TEXT NOT NULL, maximum_checkpoint_lag_ticks INTEGER NOT NULL CHECK(maximum_checkpoint_lag_ticks > 0),
  state INTEGER NOT NULL DEFAULT 0,
  CHECK((cutoff_subject_id IS NULL AND cutoff_authority_epoch IS NULL AND cutoff_incarnation IS NULL AND cutoff_sequence IS NULL)
     OR (cutoff_subject_id IS NOT NULL AND length(cutoff_authority_epoch)=16 AND length(cutoff_incarnation)=24 AND cutoff_sequence > 0)),
  PRIMARY KEY(consumer_id,consumer_version)
);
CREATE TABLE IF NOT EXISTS {_names.SubjectLifecycleCheckpoints} (
  consumer_id TEXT NOT NULL, consumer_version INTEGER NOT NULL, consumer_checksum TEXT NOT NULL, contract_id TEXT NOT NULL, contract_version INTEGER NOT NULL,
  projection_generation INTEGER NOT NULL, scope_kind INTEGER NOT NULL, scope_index_digest BLOB NOT NULL CHECK(length(scope_index_digest)=32), protected_scope_value BLOB NOT NULL, through_position INTEGER NULL, through_subject_id TEXT NULL,
  through_authority_epoch BLOB NULL, through_incarnation BLOB NULL, through_sequence INTEGER NULL, checkpoint_generation INTEGER NOT NULL CHECK(checkpoint_generation > 0),
  advanced_at TEXT NOT NULL, overtaken_at TEXT NULL, state INTEGER NOT NULL DEFAULT 0,
  PRIMARY KEY(consumer_id,consumer_version,scope_kind,scope_index_digest)
);
CREATE TABLE IF NOT EXISTS {_names.SubjectLifecycleMaintenance} (
  singleton INTEGER NOT NULL PRIMARY KEY CHECK(singleton=1), kind INTEGER NOT NULL,
  request_scope TEXT NOT NULL, request_operation TEXT NOT NULL, request_key TEXT NOT NULL,
  fingerprint BLOB NOT NULL CHECK(length(fingerprint)=32), plan_checksum BLOB NOT NULL CHECK(length(plan_checksum)=32),
  expected_store_generation INTEGER NOT NULL, expected_restore_epoch INTEGER NOT NULL, expected_delivery_epoch INTEGER NOT NULL,
  expected_scope_generation INTEGER NOT NULL, old_key_id TEXT NOT NULL, replacement_key_id TEXT NOT NULL,
  domain_ordinal INTEGER NOT NULL, last_rowid INTEGER NOT NULL, examined_count INTEGER NOT NULL,
  changed_count INTEGER NOT NULL, canonical_bytes INTEGER NOT NULL, rolling_checksum TEXT NOT NULL CHECK(length(rolling_checksum)=64)
);
CREATE TABLE IF NOT EXISTS {_names.SubjectLifecycleScopeStage} (
  domain_ordinal INTEGER NOT NULL, source_rowid INTEGER NOT NULL, prior_digest BLOB NOT NULL CHECK(length(prior_digest)=32),
  prior_value BLOB NOT NULL, replacement_digest BLOB NOT NULL CHECK(length(replacement_digest)=32), replacement_value BLOB NOT NULL,
  PRIMARY KEY(domain_ordinal,source_rowid)
) WITHOUT ROWID;
CREATE TABLE IF NOT EXISTS {_names.SubjectLifecycleMembershipStage} (
  source_rowid INTEGER NOT NULL PRIMARY KEY, consumer_id TEXT NOT NULL, consumer_version INTEGER NOT NULL,
  consumer_checksum TEXT NOT NULL CHECK(length(consumer_checksum)=64), contract_id TEXT NOT NULL, contract_version INTEGER NOT NULL,
  projection_generation INTEGER NOT NULL CHECK(projection_generation > 0), matched_state INTEGER NOT NULL CHECK(matched_state BETWEEN 0 AND 3),
  scope_kind INTEGER NOT NULL CHECK(scope_kind BETWEEN 0 AND 2), scope_index_digest BLOB NOT NULL CHECK(length(scope_index_digest)=32),
  protected_scope_value BLOB NOT NULL, commit_position INTEGER NOT NULL, subject_id TEXT NOT NULL,
  authority_epoch BLOB NOT NULL CHECK(length(authority_epoch)=16), incarnation BLOB NOT NULL CHECK(length(incarnation)=24), subject_sequence INTEGER NOT NULL
) WITHOUT ROWID;
CREATE TABLE IF NOT EXISTS {_names.SubjectRetirementBarriers} (
  scope_kind INTEGER NOT NULL CHECK(scope_kind BETWEEN 0 AND 2), scope_index_digest BLOB NOT NULL CHECK(length(scope_index_digest)=32), protected_scope_value BLOB NOT NULL,
  contract_id TEXT NOT NULL, contract_version INTEGER NOT NULL, subject_id TEXT NOT NULL,
  authority_epoch BLOB NOT NULL CHECK(length(authority_epoch)=16), incarnation BLOB NOT NULL CHECK(length(incarnation)=24),
  tombstone_sequence INTEGER NOT NULL CHECK(tombstone_sequence>0), required_consumer_set_checksum TEXT NOT NULL CHECK(length(required_consumer_set_checksum)=64),
  created_at TEXT NOT NULL, deadline_at TEXT NOT NULL, state INTEGER NOT NULL CHECK(state BETWEEN 0 AND 4),
  generation INTEGER NOT NULL CHECK(generation>0), barrier_checksum TEXT NOT NULL CHECK(length(barrier_checksum)=64),
  policy_checksum TEXT NOT NULL CHECK(length(policy_checksum)=64),
  PRIMARY KEY(scope_kind,scope_index_digest,contract_id,contract_version,subject_id,authority_epoch,incarnation)
);
CREATE TABLE IF NOT EXISTS {_names.SubjectRetirementAcknowledgements} (
  scope_kind INTEGER NOT NULL, scope_index_digest BLOB NOT NULL, protected_scope_value BLOB NOT NULL, contract_id TEXT NOT NULL, contract_version INTEGER NOT NULL,
  subject_id TEXT NOT NULL, authority_epoch BLOB NOT NULL, incarnation BLOB NOT NULL,
  consumer_id TEXT NOT NULL, consumer_version INTEGER NOT NULL, consumer_checksum TEXT NOT NULL,
  through_sequence INTEGER NOT NULL, disposition INTEGER NOT NULL, retirement_position INTEGER NOT NULL,
  PRIMARY KEY(scope_kind,scope_index_digest,contract_id,contract_version,subject_id,authority_epoch,incarnation,consumer_id,consumer_version)
);
CREATE TABLE IF NOT EXISTS {_names.SubjectRetirementTerminals} (
  scope_kind INTEGER NOT NULL, scope_index_digest BLOB NOT NULL, protected_scope_value BLOB NOT NULL,
  contract_id TEXT NOT NULL, contract_version INTEGER NOT NULL, subject_id TEXT NOT NULL,
  authority_epoch BLOB NOT NULL, incarnation BLOB NOT NULL, tombstone_sequence INTEGER NOT NULL,
  authorizing_state INTEGER NOT NULL, final_barrier_generation INTEGER NOT NULL, final_barrier_checksum TEXT NOT NULL,
  required_consumer_set_checksum TEXT NOT NULL, acknowledgements_blob BLOB NOT NULL,
  retired_position INTEGER NOT NULL, purged_at TEXT NOT NULL, receipt_checksum TEXT NOT NULL,
  PRIMARY KEY(scope_kind,scope_index_digest,contract_id,contract_version,subject_id)
);
CREATE TABLE IF NOT EXISTS {_names.SubjectRetirementPublications} (
  position INTEGER NOT NULL PRIMARY KEY CHECK(position>0), kind INTEGER NOT NULL CHECK(kind BETWEEN 0 AND 9),
  scope_kind INTEGER NULL, scope_index_digest BLOB NULL, protected_scope_value BLOB NULL,
  payload BLOB NOT NULL, CHECK((scope_kind IS NULL AND scope_index_digest IS NULL AND protected_scope_value IS NULL) OR
    (scope_kind BETWEEN 0 AND 2 AND length(scope_index_digest)=32 AND protected_scope_value IS NOT NULL))
);
CREATE TABLE IF NOT EXISTS {_names.SubjectMaintenance} (
  singleton INTEGER NOT NULL PRIMARY KEY CHECK(singleton=1), contract_id TEXT NOT NULL,
  contract_version INTEGER NOT NULL, expected_generation INTEGER NOT NULL,
  old_epoch BLOB NOT NULL CHECK(length(old_epoch)=16), new_epoch BLOB NOT NULL CHECK(length(new_epoch)=16),
  collection_ordinal INTEGER NOT NULL, last_record_id TEXT NOT NULL,
  examined_count INTEGER NOT NULL, rewritten_count INTEGER NOT NULL,
  canonical_bytes INTEGER NOT NULL, checksum TEXT NOT NULL CHECK(length(checksum)=64)
);
CREATE TABLE IF NOT EXISTS {_names.SubjectRewriteStage} (
  collection_id TEXT NOT NULL, record_id TEXT NOT NULL, previous_revision INTEGER NOT NULL,
  replacement_revision INTEGER NOT NULL, previous_payload_json BLOB NOT NULL, payload_json BLOB NOT NULL,
  PRIMARY KEY(collection_id,record_id)
) WITHOUT ROWID;
CREATE TABLE IF NOT EXISTS {_names.ModuleGenerations} (
  cell_id TEXT NOT NULL,
  cell_version INTEGER NOT NULL CHECK(cell_version > 0),
  scope_kind INTEGER NOT NULL,
  tenant TEXT NOT NULL,
  project TEXT NOT NULL,
  key_bytes BLOB NOT NULL,
  generation INTEGER NOT NULL CHECK(generation > 0),
  PRIMARY KEY(cell_id,cell_version,scope_kind,tenant,project,key_bytes)
) WITHOUT ROWID;
CREATE TABLE IF NOT EXISTS {_names.Activations} (
  activation_id TEXT NOT NULL PRIMARY KEY,
  definition_id TEXT NOT NULL,
  definition_version INTEGER NOT NULL CHECK(definition_version > 0),
  definition_checksum BLOB NOT NULL CHECK(length(definition_checksum) = 32),
  canonical_input BLOB NOT NULL,
  input_checksum BLOB NOT NULL CHECK(length(input_checksum) = 32),
  scope_kind INTEGER NOT NULL,
  scope_value TEXT NOT NULL,
  scope_digest BLOB NOT NULL CHECK(length(scope_digest) = 32),
  payload_checksum BLOB NOT NULL CHECK(length(payload_checksum) = 32),
  fingerprint BLOB NOT NULL CHECK(length(fingerprint) = 32),
  state INTEGER NOT NULL,
  generation INTEGER NOT NULL CHECK(generation > 0),
  requested_due_at INTEGER NOT NULL CHECK(requested_due_at >= 0),
  effective_due_at INTEGER NOT NULL CHECK(effective_due_at >= 0),
  occurrence_id TEXT NULL,
  priority INTEGER NOT NULL CHECK(priority BETWEEN -32 AND 32),
  overlap_key BLOB NULL CHECK(overlap_key IS NULL OR length(overlap_key)=32),
  overlap_policy INTEGER NOT NULL,
  eligible INTEGER NOT NULL CHECK(eligible IN (0,1)),
  control_checksum BLOB NOT NULL CHECK(length(control_checksum) = 32),
  attempt_number INTEGER NOT NULL DEFAULT 0 CHECK(attempt_number >= 0),
  claim_epoch INTEGER NOT NULL DEFAULT 0 CHECK(claim_epoch >= 0),
  claim_fence BLOB NULL,
  claim_worker TEXT NULL,
  lease_revision INTEGER NULL,
  lease_expires_at INTEGER NULL,
  canonical_result BLOB NULL
) WITHOUT ROWID;
CREATE INDEX IF NOT EXISTS {_names.Prefix}activation_due_idx ON {_names.Activations}(scope_kind,scope_digest,eligible,state,priority,effective_due_at,occurrence_id,activation_id);
CREATE TABLE IF NOT EXISTS {_names.Executors} (
  application_id TEXT NOT NULL, host_id TEXT NOT NULL, process_incarnation_id TEXT NOT NULL,
  executor_generation INTEGER NOT NULL CHECK(executor_generation > 0),
  store_instance_id TEXT NOT NULL, restore_epoch INTEGER NOT NULL CHECK(restore_epoch >= 0),
  worker_set_checksum BLOB NOT NULL CHECK(length(worker_set_checksum) = 32),
  authority_checksum BLOB NOT NULL CHECK(length(authority_checksum) = 32),
  heartbeat_revision INTEGER NOT NULL CHECK(heartbeat_revision > 0),
  heartbeat_expires_at INTEGER NOT NULL CHECK(heartbeat_expires_at >= 0),
  heartbeat_checksum BLOB NOT NULL CHECK(length(heartbeat_checksum) = 32),
  retired INTEGER NOT NULL CHECK(retired IN (0,1)),
  PRIMARY KEY(application_id,host_id,process_incarnation_id)
) WITHOUT ROWID;
CREATE TABLE IF NOT EXISTS {_names.ActivationEffects} (
  activation_id TEXT NOT NULL PRIMARY KEY,
  claim_attempt INTEGER NOT NULL, claim_epoch INTEGER NOT NULL, claim_fence BLOB NOT NULL CHECK(length(claim_fence)=32),
  claim_worker TEXT NOT NULL, cancellation_generation INTEGER NOT NULL, claim_store_id TEXT NOT NULL,
  claim_restore_epoch INTEGER NOT NULL, definition_checksum BLOB NOT NULL CHECK(length(definition_checksum)=32),
  executor_application TEXT NOT NULL, executor_host TEXT NOT NULL, executor_process TEXT NOT NULL,
  executor_generation INTEGER NOT NULL, executor_store_id TEXT NOT NULL, executor_restore_epoch INTEGER NOT NULL,
  worker_set_checksum BLOB NOT NULL CHECK(length(worker_set_checksum)=32), executor_checksum BLOB NOT NULL CHECK(length(executor_checksum)=32),
  effect_start_generation INTEGER NOT NULL, heartbeat_revision INTEGER NOT NULL, heartbeat_expires_at INTEGER NOT NULL,
  effect_checksum BLOB NOT NULL CHECK(length(effect_checksum)=32)
) WITHOUT ROWID;
CREATE TABLE IF NOT EXISTS {_names.ActivationSchedules} (
  schedule_id TEXT NOT NULL, schedule_version INTEGER NOT NULL, definition_json BLOB NOT NULL,
  definition_generation INTEGER NOT NULL, enabled INTEGER NOT NULL, schedule_epoch INTEGER NOT NULL,
  last_nominal INTEGER NULL, next_nominal INTEGER NULL, authority_checksum BLOB NOT NULL CHECK(length(authority_checksum)=32),
  PRIMARY KEY(schedule_id,schedule_version)
) WITHOUT ROWID;
CREATE TABLE IF NOT EXISTS {_names.ActivationOccurrences} (
  occurrence_id TEXT NOT NULL PRIMARY KEY, schedule_id TEXT NOT NULL, schedule_version INTEGER NOT NULL,
  schedule_epoch INTEGER NOT NULL, nominal_at INTEGER NOT NULL, effective_at INTEGER NOT NULL,
  overlap_ordinal INTEGER NOT NULL, fact_json BLOB NOT NULL, fact_checksum BLOB NOT NULL CHECK(length(fact_checksum)=32)
) WITHOUT ROWID;
CREATE INDEX IF NOT EXISTS {_names.Prefix}activation_occurrence_schedule_idx ON {_names.ActivationOccurrences}(schedule_id,schedule_version,schedule_epoch,nominal_at,overlap_ordinal);
CREATE TABLE IF NOT EXISTS {_names.ActivationScheduleCancellations} (
  maintenance_id TEXT NOT NULL PRIMARY KEY, replacement_activation_id TEXT NOT NULL,
  overlap_key BLOB NOT NULL CHECK(length(overlap_key)=32), high_due_at INTEGER NOT NULL,
  high_activation_id TEXT NOT NULL, after_due_at INTEGER NULL, after_activation_id TEXT NULL,
  completed INTEGER NOT NULL CHECK(completed IN (0,1))
) WITHOUT ROWID;
CREATE TABLE IF NOT EXISTS {_names.ModuleMutationDefinitions} (
  operation_id TEXT NOT NULL, operation_version INTEGER NOT NULL CHECK(operation_version > 0),
  owning_module_id TEXT NOT NULL, operation_checksum TEXT NOT NULL CHECK(length(operation_checksum)=64),
  PRIMARY KEY(operation_id,operation_version)
) WITHOUT ROWID;
CREATE TABLE IF NOT EXISTS {_names.ModuleGenerationDefinitions} (
  cell_id TEXT NOT NULL, cell_version INTEGER NOT NULL CHECK(cell_version > 0),
  owning_module_id TEXT NOT NULL, scope_kind INTEGER NOT NULL,
  maximum_key_bytes INTEGER NOT NULL, maximum_cells INTEGER NOT NULL,
  definition_checksum TEXT NOT NULL CHECK(length(definition_checksum)=64),
  PRIMARY KEY(cell_id,cell_version)
) WITHOUT ROWID;
CREATE TABLE IF NOT EXISTS {_names.MutationJournal} (
  position INTEGER PRIMARY KEY AUTOINCREMENT,
  entry_kind INTEGER NOT NULL DEFAULT 0,
  event_id TEXT NULL UNIQUE,
  event_type TEXT NULL,
  schema_version TEXT NULL,
  occurred_at TEXT NULL,
  tenant_id TEXT NULL,
  operation INTEGER NULL,
  visibility INTEGER NULL,
  collection_id TEXT NULL,
  record_id TEXT NULL,
  before_json TEXT NULL,
  after_json TEXT NULL,
  subject_contract_id TEXT NULL,
  subject_contract_version INTEGER NULL,
  subject_previous_generation INTEGER NULL,
  subject_published_generation INTEGER NULL,
  subject_restore_epoch INTEGER NULL,
  subject_publication_kind INTEGER NULL
);
CREATE TABLE IF NOT EXISTS {_names.OperationReceipts} (
  scope TEXT NOT NULL,
  operation TEXT NOT NULL,
  idempotency_key TEXT NOT NULL,
  fingerprint BLOB NOT NULL CHECK(length(fingerprint) = 32),
  structural_digest BLOB NOT NULL CHECK(length(structural_digest) = 32),
  result_json BLOB NOT NULL,
  result_format_version INTEGER NOT NULL,
  schema_generation INTEGER NOT NULL,
  store_instance_id TEXT NOT NULL,
  committed_at TEXT NOT NULL,
  expires_at TEXT NOT NULL,
  PRIMARY KEY(scope, operation, idempotency_key)
) WITHOUT ROWID;
CREATE INDEX IF NOT EXISTS "{_names.OperationReceipts}_module_retirement" ON {_names.OperationReceipts}(operation, expires_at);
""");
        foreach (SqlitePhysicalModel.CollectionModel collection in _physical.Collections)
        {
            statements.Add($"CREATE INDEX IF NOT EXISTS ix_{collection.Table}_updated ON {collection.Table}(updated_at, record_id);");
            statements.AddRange(collection.Indexes.Select(index => index.CreateSql(collection)));
        }
        foreach (SqlitePhysicalModel.RelationModel relation in _physical.Relations)
        {
            statements.Add($"CREATE INDEX IF NOT EXISTS {relation.SourceIndex} ON {relation.Table}(source_record_id, ordinal);");
            statements.Add($"CREATE INDEX IF NOT EXISTS {relation.TargetIndex} ON {relation.Table}(target_record_id, source_record_id);");
        }
        statements.Add($"CREATE INDEX IF NOT EXISTS {_names.MutationJournalScopeIndex} ON {_names.MutationJournal}(tenant_id, collection_id, record_id, position);");
        statements.AddRange(_projectionSchemaStatements);
        return statements.ToArray();
    }

    private async ValueTask InitializeCoreAsync(SqliteConnection connection, CancellationToken cancellationToken)
    {
        if (!SqliteValidation.IsValidSchemaPrefix(_options.SchemaPrefix))
        {
            throw new InvalidOperationException("SQLite schema prefix must contain only ASCII letters, digits, and underscores.");
        }

        foreach (SqlitePhysicalModel.CollectionModel collection in _physical.Collections)
        {
            await ExecuteAsync(connection, collection.CreateSql(), cancellationToken).ConfigureAwait(false);
        }
        foreach (SqlitePhysicalModel.RelationModel relation in _physical.Relations)
            await ExecuteAsync(connection, relation.CreateSql(), cancellationToken).ConfigureAwait(false);

        await ExecuteAsync(connection, $"""
CREATE TABLE IF NOT EXISTS {_names.Collections} (
  collection_id TEXT NOT NULL PRIMARY KEY,
  schema_hash TEXT NULL,
  registered_at TEXT NOT NULL,
  native_name TEXT NOT NULL,
  mutation_mode INTEGER NOT NULL,
  next_append_position INTEGER NOT NULL DEFAULT 0 CHECK (next_append_position >= 0),
  purge_generation INTEGER NOT NULL DEFAULT 0 CHECK (purge_generation >= 0),
  descriptor_json TEXT NULL
);
""", cancellationToken).ConfigureAwait(false);

        await ExecuteAsync(connection, $"""
CREATE TABLE IF NOT EXISTS {_names.ProviderState} (
  key TEXT NOT NULL PRIMARY KEY,
  value TEXT NOT NULL
);
INSERT OR IGNORE INTO {_names.ProviderState}(key, value) VALUES ('restore_epoch', '0');
INSERT OR IGNORE INTO {_names.ProviderState}(key, value) VALUES ('subject_lifecycle_delivery_epoch', '1');
INSERT OR IGNORE INTO {_names.ProviderState}(key, value) VALUES ('activation_generation', '0');
INSERT OR IGNORE INTO {_names.ProviderState}(key, value) VALUES ('activation_accepted_utc', '0');
""", cancellationToken).ConfigureAwait(false);

        await ExecuteAsync(connection, $"""
CREATE TABLE IF NOT EXISTS {_names.SchemaIdentity} (
  singleton INTEGER NOT NULL PRIMARY KEY CHECK (singleton = 1),
  store_instance_id TEXT NOT NULL
);
CREATE TABLE IF NOT EXISTS {_names.SchemaBaseline} (
  application_id TEXT NOT NULL PRIMARY KEY,
  store_instance_id TEXT NOT NULL,
  baseline_id TEXT NOT NULL,
  checksum TEXT NOT NULL,
  generation INTEGER NOT NULL,
  last_plan_id TEXT NOT NULL,
  applied_at TEXT NOT NULL
);
CREATE TABLE IF NOT EXISTS {_names.SchemaAssets} (
  application_id TEXT NOT NULL,
  logical_id TEXT NOT NULL,
  safe_summary TEXT NOT NULL,
  state INTEGER NOT NULL,
  PRIMARY KEY(application_id, logical_id)
);
CREATE TABLE IF NOT EXISTS {_names.SchemaHistory} (
  application_id TEXT NOT NULL,
  generation INTEGER NOT NULL,
  baseline_id TEXT NOT NULL,
  checksum TEXT NOT NULL,
  plan_id TEXT NOT NULL,
  classification INTEGER NOT NULL,
  outcome INTEGER NOT NULL,
  provider_version TEXT NOT NULL,
  structural_verification INTEGER NOT NULL,
  external_data_migration INTEGER NOT NULL,
  semantic_conversion INTEGER NOT NULL,
  external_attestation_id TEXT NULL,
  external_signer_id TEXT NULL,
  applied_at TEXT NOT NULL,
  PRIMARY KEY(application_id, generation)
);
CREATE TABLE IF NOT EXISTS {_names.SchemaLease} (
  application_id TEXT NOT NULL PRIMARY KEY,
  generation INTEGER NOT NULL,
  owner_token TEXT NULL,
  acquired_at TEXT NULL
);
""", cancellationToken).ConfigureAwait(false);

        await ExecuteAsync(connection, $"""
CREATE TABLE IF NOT EXISTS {_names.SubjectContracts} (
  contract_id TEXT NOT NULL,
  contract_version INTEGER NOT NULL CHECK(contract_version > 0),
  contract_checksum TEXT NOT NULL CHECK(length(contract_checksum) = 64),
  authority_epoch BLOB NOT NULL CHECK(length(authority_epoch) = 16),
  restore_epoch INTEGER NOT NULL CHECK(restore_epoch >= 0),
  state_generation INTEGER NOT NULL CHECK(state_generation > 0),
  publication_previous_generation INTEGER NOT NULL CHECK(publication_previous_generation >= 0),
  publication_kind INTEGER NOT NULL,
  publication_position INTEGER NOT NULL CHECK(publication_position > 0),
  publication_digest TEXT NOT NULL CHECK(length(publication_digest) = 64),
  PRIMARY KEY(contract_id, contract_version)
);
CREATE TABLE IF NOT EXISTS {_names.SubjectLifetimes} (
  contract_id TEXT NOT NULL,
  contract_version INTEGER NOT NULL,
  subject_id TEXT NOT NULL,
  incarnation BLOB NOT NULL CHECK(length(incarnation) = 24),
  lifetime_generation INTEGER NOT NULL CHECK(lifetime_generation > 0),
  lifecycle_state INTEGER NOT NULL CHECK(lifecycle_state BETWEEN 0 AND 2),
  subject_sequence INTEGER NOT NULL CHECK(subject_sequence > 0),
  scope_kind INTEGER NOT NULL CHECK(scope_kind BETWEEN 0 AND 2),
  scope_index_digest BLOB NOT NULL CHECK(length(scope_index_digest)=32),
  protected_scope_value BLOB NOT NULL,
  private_collection_id TEXT NOT NULL,
  private_record_id TEXT NOT NULL,
  created_journal_position INTEGER NOT NULL CHECK(created_journal_position > 0),
  last_lifecycle_position INTEGER NOT NULL CHECK(last_lifecycle_position > 0),
  PRIMARY KEY(scope_kind, scope_index_digest, contract_id, contract_version, subject_id),
  FOREIGN KEY(contract_id, contract_version) REFERENCES {_names.SubjectContracts}(contract_id, contract_version) ON DELETE RESTRICT
);
CREATE TABLE IF NOT EXISTS {_names.SubjectTerminalLifetimes} (
  contract_id TEXT NOT NULL, contract_version INTEGER NOT NULL, subject_id TEXT NOT NULL,
  scope_kind INTEGER NOT NULL CHECK(scope_kind BETWEEN 0 AND 2), scope_index_digest BLOB NOT NULL CHECK(length(scope_index_digest)=32), protected_scope_value BLOB NOT NULL,
  retired_authority_epoch BLOB NOT NULL CHECK(length(retired_authority_epoch)=16),
  retired_incarnation BLOB NOT NULL CHECK(length(retired_incarnation)=24),
  retired_lifetime_generation INTEGER NOT NULL CHECK(retired_lifetime_generation > 0),
  retired_subject_sequence INTEGER NOT NULL CHECK(retired_subject_sequence > 0),
  retired_position INTEGER NOT NULL CHECK(retired_position > 0), contract_state_generation INTEGER NOT NULL CHECK(contract_state_generation > 0),
  restore_epoch INTEGER NOT NULL CHECK(restore_epoch >= 0), receipt_checksum TEXT NOT NULL CHECK(length(receipt_checksum)=64),
  PRIMARY KEY(scope_kind,scope_index_digest,contract_id,contract_version,subject_id)
);
CREATE TABLE IF NOT EXISTS {_names.SubjectLifecycleFacts} (
  commit_position INTEGER NOT NULL CHECK(commit_position > 0), contract_id TEXT NOT NULL, contract_version INTEGER NOT NULL,
  subject_id TEXT NOT NULL, authority_epoch BLOB NOT NULL CHECK(length(authority_epoch)=16), incarnation BLOB NOT NULL CHECK(length(incarnation)=24),
  subject_sequence INTEGER NOT NULL CHECK(subject_sequence > 0), contract_state_generation INTEGER NOT NULL CHECK(contract_state_generation > 0),
  delivery_epoch INTEGER NOT NULL CHECK(delivery_epoch > 0), fact_kind INTEGER NOT NULL CHECK(fact_kind BETWEEN 0 AND 2),
  previous_state INTEGER NULL, current_state INTEGER NULL, scope_kind INTEGER NOT NULL CHECK(scope_kind BETWEEN 0 AND 2), scope_index_digest BLOB NOT NULL CHECK(length(scope_index_digest)=32), protected_scope_value BLOB NOT NULL,
  PRIMARY KEY(commit_position,subject_id,authority_epoch,incarnation,subject_sequence)
);
CREATE TABLE IF NOT EXISTS {_names.SubjectLifecycleMemberships} (
  consumer_id TEXT NOT NULL, consumer_version INTEGER NOT NULL, consumer_checksum TEXT NOT NULL CHECK(length(consumer_checksum)=64),
  contract_id TEXT NOT NULL, contract_version INTEGER NOT NULL, projection_generation INTEGER NOT NULL CHECK(projection_generation > 0),
  matched_state INTEGER NOT NULL CHECK(matched_state BETWEEN 0 AND 3), scope_kind INTEGER NOT NULL CHECK(scope_kind BETWEEN 0 AND 2), scope_index_digest BLOB NOT NULL CHECK(length(scope_index_digest)=32), protected_scope_value BLOB NOT NULL,
  commit_position INTEGER NOT NULL, subject_id TEXT NOT NULL, authority_epoch BLOB NOT NULL, incarnation BLOB NOT NULL, subject_sequence INTEGER NOT NULL,
  PRIMARY KEY(consumer_id,consumer_version,consumer_checksum,contract_id,contract_version,projection_generation,scope_kind,scope_index_digest,commit_position,subject_id,authority_epoch,incarnation,subject_sequence)
);
CREATE TABLE IF NOT EXISTS {_names.SubjectLifecycleConsumers} (
  consumer_id TEXT NOT NULL, consumer_version INTEGER NOT NULL, consumer_checksum TEXT NOT NULL CHECK(length(consumer_checksum)=64),
  contract_id TEXT NOT NULL, contract_version INTEGER NOT NULL, projection_generation INTEGER NOT NULL CHECK(projection_generation > 0),
  cutoff_position INTEGER NOT NULL CHECK(cutoff_position >= 0), cutoff_subject_id TEXT NULL, cutoff_authority_epoch BLOB NULL,
  cutoff_incarnation BLOB NULL, cutoff_sequence INTEGER NULL, published_graph_generation INTEGER NOT NULL CHECK(published_graph_generation > 0),
  installed_at TEXT NOT NULL, maximum_checkpoint_lag_ticks INTEGER NOT NULL CHECK(maximum_checkpoint_lag_ticks > 0),
  state INTEGER NOT NULL DEFAULT 0,
  CHECK((cutoff_subject_id IS NULL AND cutoff_authority_epoch IS NULL AND cutoff_incarnation IS NULL AND cutoff_sequence IS NULL)
     OR (cutoff_subject_id IS NOT NULL AND length(cutoff_authority_epoch)=16 AND length(cutoff_incarnation)=24 AND cutoff_sequence > 0)),
  PRIMARY KEY(consumer_id,consumer_version)
);
CREATE TABLE IF NOT EXISTS {_names.SubjectLifecycleCheckpoints} (
  consumer_id TEXT NOT NULL, consumer_version INTEGER NOT NULL, consumer_checksum TEXT NOT NULL, contract_id TEXT NOT NULL, contract_version INTEGER NOT NULL,
  projection_generation INTEGER NOT NULL, scope_kind INTEGER NOT NULL, scope_index_digest BLOB NOT NULL CHECK(length(scope_index_digest)=32), protected_scope_value BLOB NOT NULL, through_position INTEGER NULL, through_subject_id TEXT NULL,
  through_authority_epoch BLOB NULL, through_incarnation BLOB NULL, through_sequence INTEGER NULL, checkpoint_generation INTEGER NOT NULL CHECK(checkpoint_generation > 0),
  advanced_at TEXT NOT NULL, overtaken_at TEXT NULL, state INTEGER NOT NULL DEFAULT 0,
  PRIMARY KEY(consumer_id,consumer_version,scope_kind,scope_index_digest)
);
CREATE TABLE IF NOT EXISTS {_names.SubjectLifecycleMaintenance} (
  singleton INTEGER NOT NULL PRIMARY KEY CHECK(singleton=1), kind INTEGER NOT NULL,
  request_scope TEXT NOT NULL, request_operation TEXT NOT NULL, request_key TEXT NOT NULL,
  fingerprint BLOB NOT NULL CHECK(length(fingerprint)=32), plan_checksum BLOB NOT NULL CHECK(length(plan_checksum)=32),
  expected_store_generation INTEGER NOT NULL, expected_restore_epoch INTEGER NOT NULL, expected_delivery_epoch INTEGER NOT NULL,
  expected_scope_generation INTEGER NOT NULL, old_key_id TEXT NOT NULL, replacement_key_id TEXT NOT NULL,
  domain_ordinal INTEGER NOT NULL, last_rowid INTEGER NOT NULL, examined_count INTEGER NOT NULL,
  changed_count INTEGER NOT NULL, canonical_bytes INTEGER NOT NULL, rolling_checksum TEXT NOT NULL CHECK(length(rolling_checksum)=64)
);
CREATE TABLE IF NOT EXISTS {_names.SubjectLifecycleScopeStage} (
  domain_ordinal INTEGER NOT NULL, source_rowid INTEGER NOT NULL, prior_digest BLOB NOT NULL CHECK(length(prior_digest)=32),
  prior_value BLOB NOT NULL, replacement_digest BLOB NOT NULL CHECK(length(replacement_digest)=32), replacement_value BLOB NOT NULL,
  PRIMARY KEY(domain_ordinal,source_rowid)
) WITHOUT ROWID;
CREATE TABLE IF NOT EXISTS {_names.SubjectLifecycleMembershipStage} (
  source_rowid INTEGER NOT NULL PRIMARY KEY, consumer_id TEXT NOT NULL, consumer_version INTEGER NOT NULL,
  consumer_checksum TEXT NOT NULL CHECK(length(consumer_checksum)=64), contract_id TEXT NOT NULL, contract_version INTEGER NOT NULL,
  projection_generation INTEGER NOT NULL CHECK(projection_generation > 0), matched_state INTEGER NOT NULL CHECK(matched_state BETWEEN 0 AND 3),
  scope_kind INTEGER NOT NULL CHECK(scope_kind BETWEEN 0 AND 2), scope_index_digest BLOB NOT NULL CHECK(length(scope_index_digest)=32),
  protected_scope_value BLOB NOT NULL, commit_position INTEGER NOT NULL, subject_id TEXT NOT NULL,
  authority_epoch BLOB NOT NULL CHECK(length(authority_epoch)=16), incarnation BLOB NOT NULL CHECK(length(incarnation)=24), subject_sequence INTEGER NOT NULL
) WITHOUT ROWID;
CREATE TABLE IF NOT EXISTS {_names.SubjectRetirementBarriers} (
  scope_kind INTEGER NOT NULL CHECK(scope_kind BETWEEN 0 AND 2), scope_index_digest BLOB NOT NULL CHECK(length(scope_index_digest)=32), protected_scope_value BLOB NOT NULL,
  contract_id TEXT NOT NULL, contract_version INTEGER NOT NULL, subject_id TEXT NOT NULL,
  authority_epoch BLOB NOT NULL CHECK(length(authority_epoch)=16), incarnation BLOB NOT NULL CHECK(length(incarnation)=24),
  tombstone_sequence INTEGER NOT NULL CHECK(tombstone_sequence>0), required_consumer_set_checksum TEXT NOT NULL CHECK(length(required_consumer_set_checksum)=64),
  created_at TEXT NOT NULL, deadline_at TEXT NOT NULL, state INTEGER NOT NULL CHECK(state BETWEEN 0 AND 4),
  generation INTEGER NOT NULL CHECK(generation>0), barrier_checksum TEXT NOT NULL CHECK(length(barrier_checksum)=64),
  policy_checksum TEXT NOT NULL CHECK(length(policy_checksum)=64),
  PRIMARY KEY(scope_kind,scope_index_digest,contract_id,contract_version,subject_id,authority_epoch,incarnation)
);
CREATE TABLE IF NOT EXISTS {_names.SubjectRetirementAcknowledgements} (
  scope_kind INTEGER NOT NULL, scope_index_digest BLOB NOT NULL, protected_scope_value BLOB NOT NULL, contract_id TEXT NOT NULL, contract_version INTEGER NOT NULL,
  subject_id TEXT NOT NULL, authority_epoch BLOB NOT NULL, incarnation BLOB NOT NULL,
  consumer_id TEXT NOT NULL, consumer_version INTEGER NOT NULL, consumer_checksum TEXT NOT NULL,
  through_sequence INTEGER NOT NULL, disposition INTEGER NOT NULL, retirement_position INTEGER NOT NULL,
  PRIMARY KEY(scope_kind,scope_index_digest,contract_id,contract_version,subject_id,authority_epoch,incarnation,consumer_id,consumer_version)
);
CREATE TABLE IF NOT EXISTS {_names.SubjectRetirementTerminals} (
  scope_kind INTEGER NOT NULL, scope_index_digest BLOB NOT NULL, protected_scope_value BLOB NOT NULL,
  contract_id TEXT NOT NULL, contract_version INTEGER NOT NULL, subject_id TEXT NOT NULL,
  authority_epoch BLOB NOT NULL, incarnation BLOB NOT NULL, tombstone_sequence INTEGER NOT NULL,
  authorizing_state INTEGER NOT NULL, final_barrier_generation INTEGER NOT NULL, final_barrier_checksum TEXT NOT NULL,
  required_consumer_set_checksum TEXT NOT NULL, acknowledgements_blob BLOB NOT NULL,
  retired_position INTEGER NOT NULL, purged_at TEXT NOT NULL, receipt_checksum TEXT NOT NULL,
  PRIMARY KEY(scope_kind,scope_index_digest,contract_id,contract_version,subject_id)
);
CREATE TABLE IF NOT EXISTS {_names.SubjectRetirementPublications} (
  position INTEGER NOT NULL PRIMARY KEY CHECK(position>0), kind INTEGER NOT NULL CHECK(kind BETWEEN 0 AND 9),
  scope_kind INTEGER NULL, scope_index_digest BLOB NULL, protected_scope_value BLOB NULL,
  payload BLOB NOT NULL, CHECK((scope_kind IS NULL AND scope_index_digest IS NULL AND protected_scope_value IS NULL) OR
    (scope_kind BETWEEN 0 AND 2 AND length(scope_index_digest)=32 AND protected_scope_value IS NOT NULL))
);
CREATE TABLE IF NOT EXISTS {_names.SubjectMaintenance} (
  singleton INTEGER NOT NULL PRIMARY KEY CHECK(singleton=1), contract_id TEXT NOT NULL,
  contract_version INTEGER NOT NULL, expected_generation INTEGER NOT NULL,
  old_epoch BLOB NOT NULL CHECK(length(old_epoch)=16), new_epoch BLOB NOT NULL CHECK(length(new_epoch)=16),
  collection_ordinal INTEGER NOT NULL, last_record_id TEXT NOT NULL,
  examined_count INTEGER NOT NULL, rewritten_count INTEGER NOT NULL,
  canonical_bytes INTEGER NOT NULL, checksum TEXT NOT NULL CHECK(length(checksum)=64)
);
CREATE TABLE IF NOT EXISTS {_names.SubjectRewriteStage} (
  collection_id TEXT NOT NULL, record_id TEXT NOT NULL, previous_revision INTEGER NOT NULL,
  replacement_revision INTEGER NOT NULL, previous_payload_json BLOB NOT NULL, payload_json BLOB NOT NULL,
  PRIMARY KEY(collection_id,record_id)
) WITHOUT ROWID;
CREATE TABLE IF NOT EXISTS {_names.ModuleGenerations} (
  cell_id TEXT NOT NULL,
  cell_version INTEGER NOT NULL CHECK(cell_version > 0),
  scope_kind INTEGER NOT NULL,
  tenant TEXT NOT NULL,
  project TEXT NOT NULL,
  key_bytes BLOB NOT NULL,
  generation INTEGER NOT NULL CHECK(generation > 0),
  PRIMARY KEY(cell_id,cell_version,scope_kind,tenant,project,key_bytes)
) WITHOUT ROWID;
CREATE TABLE IF NOT EXISTS {_names.Activations} (
  activation_id TEXT NOT NULL PRIMARY KEY,
  definition_id TEXT NOT NULL,
  definition_version INTEGER NOT NULL CHECK(definition_version > 0),
  definition_checksum BLOB NOT NULL CHECK(length(definition_checksum) = 32),
  canonical_input BLOB NOT NULL,
  input_checksum BLOB NOT NULL CHECK(length(input_checksum) = 32),
  scope_kind INTEGER NOT NULL,
  scope_value TEXT NOT NULL,
  scope_digest BLOB NOT NULL CHECK(length(scope_digest) = 32),
  payload_checksum BLOB NOT NULL CHECK(length(payload_checksum) = 32),
  fingerprint BLOB NOT NULL CHECK(length(fingerprint) = 32),
  state INTEGER NOT NULL,
  generation INTEGER NOT NULL CHECK(generation > 0),
  requested_due_at INTEGER NOT NULL CHECK(requested_due_at >= 0),
  effective_due_at INTEGER NOT NULL CHECK(effective_due_at >= 0),
  occurrence_id TEXT NULL,
  priority INTEGER NOT NULL CHECK(priority BETWEEN -32 AND 32),
  overlap_key BLOB NULL CHECK(overlap_key IS NULL OR length(overlap_key)=32),
  overlap_policy INTEGER NOT NULL,
  eligible INTEGER NOT NULL CHECK(eligible IN (0,1)),
  control_checksum BLOB NOT NULL CHECK(length(control_checksum) = 32),
  attempt_number INTEGER NOT NULL DEFAULT 0 CHECK(attempt_number >= 0),
  claim_epoch INTEGER NOT NULL DEFAULT 0 CHECK(claim_epoch >= 0),
  claim_fence BLOB NULL,
  claim_worker TEXT NULL,
  lease_revision INTEGER NULL,
  lease_expires_at INTEGER NULL,
  canonical_result BLOB NULL
) WITHOUT ROWID;
CREATE INDEX IF NOT EXISTS {_names.Prefix}activation_due_idx ON {_names.Activations}(scope_kind,scope_digest,eligible,state,priority,effective_due_at,occurrence_id,activation_id);
CREATE TABLE IF NOT EXISTS {_names.Executors} (
  application_id TEXT NOT NULL, host_id TEXT NOT NULL, process_incarnation_id TEXT NOT NULL,
  executor_generation INTEGER NOT NULL CHECK(executor_generation > 0),
  store_instance_id TEXT NOT NULL, restore_epoch INTEGER NOT NULL CHECK(restore_epoch >= 0),
  worker_set_checksum BLOB NOT NULL CHECK(length(worker_set_checksum) = 32),
  authority_checksum BLOB NOT NULL CHECK(length(authority_checksum) = 32),
  heartbeat_revision INTEGER NOT NULL CHECK(heartbeat_revision > 0),
  heartbeat_expires_at INTEGER NOT NULL CHECK(heartbeat_expires_at >= 0),
  heartbeat_checksum BLOB NOT NULL CHECK(length(heartbeat_checksum) = 32),
  retired INTEGER NOT NULL CHECK(retired IN (0,1)),
  PRIMARY KEY(application_id,host_id,process_incarnation_id)
) WITHOUT ROWID;
CREATE TABLE IF NOT EXISTS {_names.ActivationEffects} (
  activation_id TEXT NOT NULL PRIMARY KEY,
  claim_attempt INTEGER NOT NULL, claim_epoch INTEGER NOT NULL, claim_fence BLOB NOT NULL CHECK(length(claim_fence)=32),
  claim_worker TEXT NOT NULL, cancellation_generation INTEGER NOT NULL, claim_store_id TEXT NOT NULL,
  claim_restore_epoch INTEGER NOT NULL, definition_checksum BLOB NOT NULL CHECK(length(definition_checksum)=32),
  executor_application TEXT NOT NULL, executor_host TEXT NOT NULL, executor_process TEXT NOT NULL,
  executor_generation INTEGER NOT NULL, executor_store_id TEXT NOT NULL, executor_restore_epoch INTEGER NOT NULL,
  worker_set_checksum BLOB NOT NULL CHECK(length(worker_set_checksum)=32), executor_checksum BLOB NOT NULL CHECK(length(executor_checksum)=32),
  effect_start_generation INTEGER NOT NULL, heartbeat_revision INTEGER NOT NULL, heartbeat_expires_at INTEGER NOT NULL,
  effect_checksum BLOB NOT NULL CHECK(length(effect_checksum)=32)
) WITHOUT ROWID;
CREATE TABLE IF NOT EXISTS {_names.ActivationSchedules} (
  schedule_id TEXT NOT NULL, schedule_version INTEGER NOT NULL, definition_json BLOB NOT NULL,
  definition_generation INTEGER NOT NULL, enabled INTEGER NOT NULL, schedule_epoch INTEGER NOT NULL,
  last_nominal INTEGER NULL, next_nominal INTEGER NULL, authority_checksum BLOB NOT NULL CHECK(length(authority_checksum)=32),
  PRIMARY KEY(schedule_id,schedule_version)
) WITHOUT ROWID;
CREATE TABLE IF NOT EXISTS {_names.ActivationOccurrences} (
  occurrence_id TEXT NOT NULL PRIMARY KEY, schedule_id TEXT NOT NULL, schedule_version INTEGER NOT NULL,
  schedule_epoch INTEGER NOT NULL, nominal_at INTEGER NOT NULL, effective_at INTEGER NOT NULL,
  overlap_ordinal INTEGER NOT NULL, fact_json BLOB NOT NULL, fact_checksum BLOB NOT NULL CHECK(length(fact_checksum)=32)
) WITHOUT ROWID;
CREATE INDEX IF NOT EXISTS {_names.Prefix}activation_occurrence_schedule_idx ON {_names.ActivationOccurrences}(schedule_id,schedule_version,schedule_epoch,nominal_at,overlap_ordinal);
CREATE TABLE IF NOT EXISTS {_names.ActivationScheduleCancellations} (
  maintenance_id TEXT NOT NULL PRIMARY KEY, replacement_activation_id TEXT NOT NULL,
  overlap_key BLOB NOT NULL CHECK(length(overlap_key)=32), high_due_at INTEGER NOT NULL,
  high_activation_id TEXT NOT NULL, after_due_at INTEGER NULL, after_activation_id TEXT NULL,
  completed INTEGER NOT NULL CHECK(completed IN (0,1))
) WITHOUT ROWID;
CREATE TABLE IF NOT EXISTS {_names.ModuleMutationDefinitions} (
  operation_id TEXT NOT NULL, operation_version INTEGER NOT NULL CHECK(operation_version > 0),
  owning_module_id TEXT NOT NULL, operation_checksum TEXT NOT NULL CHECK(length(operation_checksum)=64),
  PRIMARY KEY(operation_id,operation_version)
) WITHOUT ROWID;
CREATE TABLE IF NOT EXISTS {_names.ModuleGenerationDefinitions} (
  cell_id TEXT NOT NULL, cell_version INTEGER NOT NULL CHECK(cell_version > 0),
  owning_module_id TEXT NOT NULL, scope_kind INTEGER NOT NULL,
  maximum_key_bytes INTEGER NOT NULL, maximum_cells INTEGER NOT NULL,
  definition_checksum TEXT NOT NULL CHECK(length(definition_checksum)=64),
  PRIMARY KEY(cell_id,cell_version)
) WITHOUT ROWID;
""", cancellationToken).ConfigureAwait(false);

        await ExecuteAsync(connection, $"""
CREATE TABLE IF NOT EXISTS {_names.MutationJournal} (
  position INTEGER PRIMARY KEY AUTOINCREMENT,
  entry_kind INTEGER NOT NULL DEFAULT 0,
  event_id TEXT NULL UNIQUE,
  event_type TEXT NULL,
  schema_version TEXT NULL,
  occurred_at TEXT NULL,
  tenant_id TEXT NULL,
  operation INTEGER NULL,
  visibility INTEGER NULL,
  collection_id TEXT NULL,
  record_id TEXT NULL,
  before_json TEXT NULL,
  after_json TEXT NULL,
  subject_contract_id TEXT NULL,
  subject_contract_version INTEGER NULL,
  subject_previous_generation INTEGER NULL,
  subject_published_generation INTEGER NULL,
  subject_restore_epoch INTEGER NULL,
  subject_publication_kind INTEGER NULL
);
CREATE TABLE IF NOT EXISTS {_names.OperationReceipts} (
  scope TEXT NOT NULL,
  operation TEXT NOT NULL,
  idempotency_key TEXT NOT NULL,
  fingerprint BLOB NOT NULL CHECK(length(fingerprint) = 32),
  structural_digest BLOB NOT NULL CHECK(length(structural_digest) = 32),
  result_json BLOB NOT NULL,
  result_format_version INTEGER NOT NULL,
  schema_generation INTEGER NOT NULL,
  store_instance_id TEXT NOT NULL,
  committed_at TEXT NOT NULL,
  expires_at TEXT NOT NULL,
  PRIMARY KEY(scope, operation, idempotency_key)
) WITHOUT ROWID;
CREATE INDEX IF NOT EXISTS "{_names.OperationReceipts}_module_retirement" ON {_names.OperationReceipts}(operation, expires_at);
""", cancellationToken).ConfigureAwait(false);

        var malformedColumns = new List<string>();
        malformedColumns.AddRange(await GetMissingCollectionStateAsync(connection, cancellationToken).ConfigureAwait(false));
        foreach (SqlitePhysicalModel.CollectionModel collection in _physical.Collections)
            malformedColumns.AddRange(await GetMissingRecordColumnsAsync(connection, collection, cancellationToken).ConfigureAwait(false));
        malformedColumns.AddRange(await GetMissingMutationJournalColumnsAsync(connection, cancellationToken).ConfigureAwait(false));
        malformedColumns.AddRange(await GetMissingSchemaAuthorityColumnsAsync(connection, cancellationToken).ConfigureAwait(false));
        foreach (SqlitePhysicalModel.RelationModel relation in _physical.Relations)
            malformedColumns.AddRange(await GetMissingRelationColumnsAsync(connection, relation, cancellationToken).ConfigureAwait(false));
        if (malformedColumns.Count != 0)
            throw new InvalidOperationException("SQLite provider-owned schema is missing required parts: " + string.Join(", ", malformedColumns));

        foreach (SqlitePhysicalModel.CollectionModel collection in _physical.Collections)
        {
            await ExecuteAsync(connection,
                $"CREATE INDEX IF NOT EXISTS ix_{collection.Table}_updated ON {collection.Table}(updated_at, record_id);",
                cancellationToken).ConfigureAwait(false);
            foreach (SqlitePhysicalModel.IndexModel index in collection.Indexes)
                await ExecuteAsync(connection, index.CreateSql(collection), cancellationToken).ConfigureAwait(false);
        }
        foreach (SqlitePhysicalModel.RelationModel relation in _physical.Relations)
        {
            await ExecuteAsync(connection, $"CREATE INDEX IF NOT EXISTS {relation.SourceIndex} ON {relation.Table}(source_record_id, ordinal);", cancellationToken).ConfigureAwait(false);
            await ExecuteAsync(connection, $"CREATE INDEX IF NOT EXISTS {relation.TargetIndex} ON {relation.Table}(target_record_id, source_record_id);", cancellationToken).ConfigureAwait(false);
        }

        await ExecuteAsync(connection, $"""
CREATE INDEX IF NOT EXISTS {_names.MutationJournalScopeIndex}
  ON {_names.MutationJournal}(tenant_id, collection_id, record_id, position);
""", cancellationToken).ConfigureAwait(false);

        foreach (SqlitePhysicalModel.CollectionModel collection in _physical.Collections)
        {
            string collectionId = collection.Definition.Id;
            if (!SqliteValidation.IsValidIdText(collectionId))
            {
                continue;
            }

            await using var command = connection.CreateCommand();
            command.CommandText = $"""
INSERT INTO {_names.Collections}(collection_id, schema_hash, registered_at, native_name, mutation_mode, next_append_position, purge_generation, descriptor_json)
VALUES ($collection, NULL, $registered, $native, $mode, 0, 0, NULL)
ON CONFLICT(collection_id) DO UPDATE SET native_name = excluded.native_name, mutation_mode = excluded.mutation_mode;
""";
            command.CommandTimeout = TimeoutSeconds();
            command.Parameters.AddWithValue("$collection", collectionId);
            command.Parameters.AddWithValue("$registered", DateTimeOffset.UtcNow.ToString("O"));
            command.Parameters.AddWithValue("$native", collection.Table);
            command.Parameters.AddWithValue("$mode", (int)collection.Definition.MutationMode);
            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        foreach (BaseExportedSubjectDefinition subject in _options.ExportedSubjects)
        {
            await using SqliteTransaction transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
            await using SqliteCommand current = connection.CreateCommand();
            current.Transaction = transaction;
            current.CommandTimeout = TimeoutSeconds();
            current.CommandText = $"SELECT contract_checksum FROM {_names.SubjectContracts} WHERE contract_id=$id AND contract_version=$version;";
            current.Parameters.AddWithValue("$id", subject.Id);
            current.Parameters.AddWithValue("$version", subject.Version);
            object? existing = await current.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
            if (existing is not null)
            {
                if (!string.Equals(Convert.ToString(existing, System.Globalization.CultureInfo.InvariantCulture), subject.ValidationPlan.ContractChecksum, StringComparison.Ordinal))
                    throw new InvalidOperationException(BaseSubjectErrorCodes.RegistrationConflict);
                await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
                continue;
            }

            long restoreEpoch;
            await using (SqliteCommand restore = connection.CreateCommand())
            {
                restore.Transaction = transaction;
                restore.CommandTimeout = TimeoutSeconds();
                restore.CommandText = $"SELECT COALESCE(CAST(value AS INTEGER),0) FROM {_names.ProviderState} WHERE key='restore_epoch';";
                restoreEpoch = Convert.ToInt64(await restore.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false), System.Globalization.CultureInfo.InvariantCulture);
            }
            BaseSubjectAuthorityEpoch epoch = BaseSubjectAuthorityEpoch.Create();
            long position;
            await using (SqliteCommand publication = connection.CreateCommand())
            {
                publication.Transaction = transaction;
                publication.CommandTimeout = TimeoutSeconds();
                publication.CommandText = $"""
INSERT INTO {_names.MutationJournal}(
  entry_kind, subject_contract_id, subject_contract_version, subject_previous_generation,
  subject_published_generation, subject_restore_epoch, subject_publication_kind)
VALUES (1,$id,$version,0,1,$restore,0)
RETURNING position;
""";
                publication.Parameters.AddWithValue("$id", subject.Id);
                publication.Parameters.AddWithValue("$version", subject.Version);
                publication.Parameters.AddWithValue("$restore", restoreEpoch);
                position = Convert.ToInt64(await publication.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false), System.Globalization.CultureInfo.InvariantCulture);
            }
            string digest = BaseSubjectPublicationIntegrity.Compute(
                subject.Id, subject.Version, subject.ValidationPlan.ContractChecksum,
                0, 1, restoreEpoch, BaseSubjectAuthorityPublicationKind.InitialInstallation,
                new BaseMutationJournalPosition(position), epoch);
            await using (SqliteCommand insert = connection.CreateCommand())
            {
                insert.Transaction = transaction;
                insert.CommandTimeout = TimeoutSeconds();
                insert.CommandText = $"""
INSERT INTO {_names.SubjectContracts}(
  contract_id, contract_version, contract_checksum, authority_epoch, restore_epoch, state_generation,
  publication_previous_generation, publication_kind, publication_position, publication_digest)
VALUES ($id,$version,$checksum,$epoch,$restore,1,0,0,$position,$digest);
""";
                insert.Parameters.AddWithValue("$id", subject.Id);
                insert.Parameters.AddWithValue("$version", subject.Version);
                insert.Parameters.AddWithValue("$checksum", subject.ValidationPlan.ContractChecksum);
                insert.Parameters.Add("$epoch", Microsoft.Data.Sqlite.SqliteType.Blob).Value = epoch.ToArray();
                insert.Parameters.AddWithValue("$restore", restoreEpoch);
                insert.Parameters.AddWithValue("$position", position);
                insert.Parameters.AddWithValue("$digest", digest);
                await insert.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            }
            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        }

        foreach (string statement in _projectionSchemaStatements)
            await ExecuteAsync(connection, statement, cancellationToken).ConfigureAwait(false);

        var missing = await GetMissingSchemaPartsAsync(connection, cancellationToken).ConfigureAwait(false);
        if (missing.Length != 0)
        {
            throw new InvalidOperationException("SQLite provider-owned schema is missing required parts: " + string.Join(", ", missing));
        }
    }

    /// <summary>Executes the has required schema async operation.</summary>
    public ValueTask<bool> HasRequiredSchemaAsync(SqliteConnection connection, CancellationToken cancellationToken) =>
        HPDBaseSqliteTelemetry.TraceSchemaAsync(HPDBaseTelemetrySpans.SqliteSchemaValidate, _options.StoreId, async () =>
        {
            var missing = await GetMissingSchemaPartsAsync(connection, cancellationToken).ConfigureAwait(false);
            return missing.Length == 0;
        });

    /// <summary>Executes the get missing schema parts async operation.</summary>
    public async ValueTask<string[]> GetMissingSchemaPartsAsync(SqliteConnection connection, CancellationToken cancellationToken)
    {
        var missing = new List<string>();
        foreach (var table in new[] { _names.Collections, _names.ProviderState, _names.MutationJournal, _names.OperationReceipts, _names.SchemaIdentity, _names.SchemaBaseline, _names.SchemaAssets, _names.SchemaHistory, _names.SchemaLease, _names.SubjectContracts, _names.SubjectLifetimes, _names.SubjectTerminalLifetimes, _names.SubjectLifecycleFacts, _names.SubjectLifecycleMemberships, _names.SubjectLifecycleConsumers, _names.SubjectLifecycleCheckpoints, _names.SubjectLifecycleMaintenance, _names.SubjectLifecycleScopeStage, _names.SubjectLifecycleMembershipStage, _names.SubjectRetirementBarriers, _names.SubjectRetirementAcknowledgements, _names.SubjectRetirementTerminals, _names.SubjectRetirementPublications, _names.SubjectMaintenance, _names.SubjectRewriteStage, _names.ModuleGenerations, _names.ModuleMutationDefinitions, _names.ModuleGenerationDefinitions, _names.Activations }
            .Concat(_physical.Collections.Select(static collection => collection.Table))
            .Concat(_physical.Relations.Select(static relation => relation.Table))
            .Concat(_projectionSchemaTables))
        {
            if (!await ObjectExistsAsync(connection, "table", table, cancellationToken).ConfigureAwait(false))
            {
                missing.Add("table:" + table);
            }
        }

        if (missing.Count == 0)
        {
            foreach (SqlitePhysicalModel.CollectionModel collection in _physical.Collections)
            {
                missing.AddRange(await GetMissingRecordColumnsAsync(connection, collection, cancellationToken).ConfigureAwait(false));
                missing.AddRange(await GetMalformedRecordColumnsAsync(connection, collection, cancellationToken).ConfigureAwait(false));
            }
            foreach (SqlitePhysicalModel.RelationModel relation in _physical.Relations)
            {
                missing.AddRange(await GetMissingRelationColumnsAsync(connection, relation, cancellationToken).ConfigureAwait(false));
                missing.AddRange(await GetMalformedRelationColumnsAsync(connection, relation, cancellationToken).ConfigureAwait(false));
            }
            foreach (SqliteProjectionTableShape shape in _projectionSchemaShapes)
                missing.AddRange(await GetMalformedProjectionColumnsAsync(connection, shape, cancellationToken).ConfigureAwait(false));
            missing.AddRange(await GetMissingMutationJournalColumnsAsync(connection, cancellationToken).ConfigureAwait(false));
            missing.AddRange(await GetMissingReceiptColumnsAsync(connection, cancellationToken).ConfigureAwait(false));
            missing.AddRange(await GetMalformedMutationJournalColumnsAsync(connection, cancellationToken).ConfigureAwait(false));
            missing.AddRange(await GetMalformedReceiptColumnsAsync(connection, cancellationToken).ConfigureAwait(false));
            missing.AddRange(await GetMissingSchemaAuthorityColumnsAsync(connection, cancellationToken).ConfigureAwait(false));
            missing.AddRange(await GetMissingCollectionStateAsync(connection, cancellationToken).ConfigureAwait(false));

            foreach (SqlitePhysicalModel.CollectionModel collection in _physical.Collections)
            {
                if (!await ObjectExistsAsync(connection, "index", $"ix_{collection.Table}_updated", cancellationToken).ConfigureAwait(false))
                    missing.Add("index:ix_" + collection.Table + "_updated");
                else if (!await IndexMatchesAsync(connection, $"ix_{collection.Table}_updated", false, ["updated_at", "record_id"], [false, false], cancellationToken).ConfigureAwait(false))
                    missing.Add("index-shape:ix_" + collection.Table + "_updated");
                foreach (SqlitePhysicalModel.IndexModel index in collection.Indexes)
                    if (!await ObjectExistsAsync(connection, "index", index.Name, cancellationToken).ConfigureAwait(false))
                        missing.Add("index:" + index.Name);
                    else if (!await IndexMatchesAsync(connection, index.Name, index.Definition.Unique || index.Definition.Kind == IndexKind.Unique, index.Parts.Select(static part => part.Column).ToArray(), index.Definition.Parts!.Select(static part => part.Direction == IndexSortDirection.Desc).ToArray(), cancellationToken).ConfigureAwait(false))
                        missing.Add("index-shape:" + index.Name);
            }
            foreach (SqlitePhysicalModel.RelationModel relation in _physical.Relations)
            {
                if (!await ObjectExistsAsync(connection, "index", relation.SourceIndex, cancellationToken).ConfigureAwait(false)) missing.Add("index:" + relation.SourceIndex);
                else if (!await IndexMatchesAsync(connection, relation.SourceIndex, false, ["source_record_id", "ordinal"], [false, false], cancellationToken).ConfigureAwait(false)) missing.Add("index-shape:" + relation.SourceIndex);
                if (!await ObjectExistsAsync(connection, "index", relation.TargetIndex, cancellationToken).ConfigureAwait(false)) missing.Add("index:" + relation.TargetIndex);
                else if (!await IndexMatchesAsync(connection, relation.TargetIndex, false, ["target_record_id", "source_record_id"], [false, false], cancellationToken).ConfigureAwait(false)) missing.Add("index-shape:" + relation.TargetIndex);
            }

            if (!await ObjectExistsAsync(connection, "index", _names.MutationJournalScopeIndex, cancellationToken).ConfigureAwait(false))
            {
                missing.Add("index:" + _names.MutationJournalScopeIndex);
            }
            else if (!await IndexMatchesAsync(connection, _names.MutationJournalScopeIndex, false, ["tenant_id", "collection_id", "record_id", "position"], [false, false, false, false], cancellationToken).ConfigureAwait(false))
            {
                missing.Add("index-shape:" + _names.MutationJournalScopeIndex);
            }
        }

        var result = missing.ToArray();
        HPDBaseSqliteTelemetry.RecordSchemaMissingParts(_options.StoreId, result.Length);
        return result;
    }

    private async ValueTask ExecuteAsync(SqliteConnection connection, string commandText, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = commandText;
        command.CommandTimeout = TimeoutSeconds();
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private int TimeoutSeconds() => Math.Max(1, (int)Math.Ceiling(_options.CommandTimeout.TotalSeconds));

    private async ValueTask<bool> ObjectExistsAsync(SqliteConnection connection, string type, string name, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type = $type AND name = $name;";
        command.CommandTimeout = TimeoutSeconds();
        command.Parameters.AddWithValue("$type", type);
        command.Parameters.AddWithValue("$name", name);
        return (long)(await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false))! > 0;
    }

    private async ValueTask<bool> ColumnExistsAsync(SqliteConnection connection, string table, string column, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = $"PRAGMA table_info({table});";
        command.CommandTimeout = TimeoutSeconds();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            if (string.Equals(reader.GetString(1), column, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    private async ValueTask<Dictionary<string, ColumnShape>> GetColumnShapesAsync(SqliteConnection connection, string table, CancellationToken cancellationToken)
    {
        var result = new Dictionary<string, ColumnShape>(StringComparer.Ordinal);
        await using var command = connection.CreateCommand();
        command.CommandText = $"PRAGMA table_info({table});";
        command.CommandTimeout = TimeoutSeconds();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            result[reader.GetString(1)] = new ColumnShape(reader.GetString(2).ToUpperInvariant(), reader.GetInt64(3) != 0, reader.GetInt64(5) != 0);
        return result;
    }

    private async ValueTask<bool> IndexMatchesAsync(SqliteConnection connection, string index, bool unique, string[] columns, bool[] descending, CancellationToken cancellationToken)
    {
        await using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT [sql] FROM sqlite_master WHERE type = 'index' AND name = $name;";
            command.CommandTimeout = TimeoutSeconds();
            command.Parameters.AddWithValue("$name", index);
            string? sql = await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false) as string;
            if (sql is null || sql.Contains("CREATE UNIQUE INDEX", StringComparison.OrdinalIgnoreCase) != unique) return false;
        }
        var actual = new List<(string Column, bool Descending)>();
        await using var info = connection.CreateCommand();
        info.CommandText = $"PRAGMA index_xinfo({index});";
        info.CommandTimeout = TimeoutSeconds();
        await using var reader = await info.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            if (reader.GetInt64(5) != 0) actual.Add((reader.GetString(2), reader.GetInt64(3) != 0));
        return actual.Select(static item => item.Column).SequenceEqual(columns, StringComparer.Ordinal) &&
            actual.Select(static item => item.Descending).SequenceEqual(descending);
    }

    private async ValueTask<string[]> GetMalformedRecordColumnsAsync(SqliteConnection connection, SqlitePhysicalModel.CollectionModel collection, CancellationToken cancellationToken)
    {
        Dictionary<string, ColumnShape> shapes = await GetColumnShapesAsync(connection, collection.Table, cancellationToken).ConfigureAwait(false);
        var malformed = new List<string>();
        Check(shapes, malformed, collection.Table, "record_id", "TEXT", false, true);
        Check(shapes, malformed, collection.Table, "revision", "INTEGER", true, false);
        Check(shapes, malformed, collection.Table, "created_at", "TEXT", true, false);
        Check(shapes, malformed, collection.Table, "updated_at", "TEXT", true, false);
        Check(shapes, malformed, collection.Table, "append_position", "INTEGER", true, false);
        Check(shapes, malformed, collection.Table, "latest_mutation_position", "INTEGER", true, false);
        foreach (SqlitePhysicalModel.FieldModel field in collection.Fields)
        {
            if (field.PresenceColumn is not null) Check(shapes, malformed, collection.Table, field.PresenceColumn, "INTEGER", true, false);
            Check(shapes, malformed, collection.Table, field.Column, field.SqlType, field.PresenceColumn is null, false);
        }
        if (collection.HasExtensionJson) Check(shapes, malformed, collection.Table, "extension_json", "TEXT", false, false);
        return malformed.ToArray();
    }

    private async ValueTask<string[]> GetMalformedRelationColumnsAsync(SqliteConnection connection, SqlitePhysicalModel.RelationModel relation, CancellationToken cancellationToken)
    {
        Dictionary<string, ColumnShape> shapes = await GetColumnShapesAsync(connection, relation.Table, cancellationToken).ConfigureAwait(false);
        var malformed = new List<string>();
        Check(shapes, malformed, relation.Table, "source_record_id", "TEXT", true, true);
        Check(shapes, malformed, relation.Table, "target_record_id", "TEXT", true, false);
        Check(shapes, malformed, relation.Table, "ordinal", "INTEGER", true, true);
        return malformed.ToArray();
    }

    private async ValueTask<string[]> GetMalformedProjectionColumnsAsync(SqliteConnection connection, SqliteProjectionTableShape projection, CancellationToken cancellationToken)
    {
        Dictionary<string, ColumnShape> shapes = await GetColumnShapesAsync(connection, projection.Table, cancellationToken).ConfigureAwait(false);
        var malformed = new List<string>();
        foreach (SqliteProjectionColumnShape expected in projection.Columns)
        {
            if (!shapes.ContainsKey(expected.Name)) malformed.Add("column:" + projection.Table + "." + expected.Name);
            else Check(shapes, malformed, projection.Table, expected.Name, expected.Type, expected.NotNull, expected.PrimaryKey);
        }
        return malformed.ToArray();
    }

    private static void Check(Dictionary<string, ColumnShape> shapes, List<string> malformed, string table, string column, string type, bool notNull, bool primaryKey)
    {
        if (shapes.TryGetValue(column, out ColumnShape shape) && (shape.Type != type || shape.NotNull != notNull || shape.PrimaryKey != primaryKey))
            malformed.Add("column-shape:" + table + "." + column);
    }

    private readonly record struct ColumnShape(string Type, bool NotNull, bool PrimaryKey);

    private async ValueTask<string[]> GetMissingRecordColumnsAsync(SqliteConnection connection, SqlitePhysicalModel.CollectionModel collection, CancellationToken cancellationToken)
    {
        var missing = new List<string>();
        IEnumerable<string> columns = new[] { "record_id", "revision", "created_at", "updated_at", "append_position" }
            .Concat(collection.Fields.SelectMany(static field => field.PresenceColumn is null ? [field.Column] : new[] { field.PresenceColumn, field.Column }))
            .Concat(collection.HasExtensionJson ? ["extension_json"] : []);
        foreach (var column in columns)
        {
            if (!await ColumnExistsAsync(connection, collection.Table, column, cancellationToken).ConfigureAwait(false))
            {
                missing.Add("column:" + collection.Table + "." + column);
            }
        }

        return missing.ToArray();
    }

    private async ValueTask<string[]> GetMissingMutationJournalColumnsAsync(
        SqliteConnection connection,
        CancellationToken cancellationToken)
    {
        var missing = new List<string>();
        foreach (var column in new[]
        {
            "position",
            "entry_kind",
            "event_id",
            "event_type",
            "schema_version",
            "occurred_at",
            "tenant_id",
            "operation",
            "visibility",
            "collection_id",
            "record_id",
            "before_json",
            "after_json",
            "subject_contract_id",
            "subject_contract_version",
            "subject_previous_generation",
            "subject_published_generation",
            "subject_restore_epoch",
            "subject_publication_kind"
        })
        {
            if (!await ColumnExistsAsync(connection, _names.MutationJournal, column, cancellationToken).ConfigureAwait(false))
                missing.Add("column:" + _names.MutationJournal + "." + column);
        }

        return missing.ToArray();
    }

    private async ValueTask<string[]> GetMalformedMutationJournalColumnsAsync(SqliteConnection connection, CancellationToken cancellationToken)
    {
        Dictionary<string, ColumnShape> shapes = await GetColumnShapesAsync(connection, _names.MutationJournal, cancellationToken).ConfigureAwait(false);
        var malformed = new List<string>();
        Check(shapes, malformed, _names.MutationJournal, "position", "INTEGER", false, true);
        Check(shapes, malformed, _names.MutationJournal, "entry_kind", "INTEGER", true, false);
        foreach (string column in new[]
                 {
                     "event_id", "event_type", "schema_version", "occurred_at", "tenant_id",
                     "collection_id", "record_id", "before_json", "after_json", "subject_contract_id"
                 })
            Check(shapes, malformed, _names.MutationJournal, column, "TEXT", false, false);
        foreach (string column in new[]
                 {
                     "operation", "visibility", "subject_contract_version", "subject_previous_generation",
                     "subject_published_generation", "subject_restore_epoch", "subject_publication_kind"
                 })
            Check(shapes, malformed, _names.MutationJournal, column, "INTEGER", false, false);
        return malformed.ToArray();
    }

    private async ValueTask<string[]> GetMissingReceiptColumnsAsync(SqliteConnection connection, CancellationToken cancellationToken)
    {
        var missing = new List<string>();
        foreach (string column in new[] { "scope", "operation", "idempotency_key", "fingerprint", "structural_digest", "result_json", "result_format_version", "schema_generation", "store_instance_id", "committed_at", "expires_at" })
            if (!await ColumnExistsAsync(connection, _names.OperationReceipts, column, cancellationToken).ConfigureAwait(false))
                missing.Add("column:" + _names.OperationReceipts + "." + column);
        return missing.ToArray();
    }

    private async ValueTask<string[]> GetMalformedReceiptColumnsAsync(SqliteConnection connection, CancellationToken cancellationToken)
    {
        Dictionary<string, ColumnShape> shapes = await GetColumnShapesAsync(connection, _names.OperationReceipts, cancellationToken).ConfigureAwait(false);
        var malformed = new List<string>();
        foreach (string column in new[] { "scope", "operation", "idempotency_key" })
            Check(shapes, malformed, _names.OperationReceipts, column, "TEXT", true, true);
        foreach (string column in new[] { "fingerprint", "structural_digest", "result_json" })
            Check(shapes, malformed, _names.OperationReceipts, column, "BLOB", true, false);
        Check(shapes, malformed, _names.OperationReceipts, "result_format_version", "INTEGER", true, false);
        Check(shapes, malformed, _names.OperationReceipts, "schema_generation", "INTEGER", true, false);
        foreach (string column in new[] { "store_instance_id", "committed_at", "expires_at" })
            Check(shapes, malformed, _names.OperationReceipts, column, "TEXT", true, false);
        return malformed.ToArray();
    }

    private async ValueTask<string[]> GetMissingCollectionStateAsync(
        SqliteConnection connection,
        CancellationToken cancellationToken)
    {
        var missing = new List<string>();
        foreach (string column in new[]
        {
            "collection_id", "schema_hash", "registered_at", "native_name",
            "mutation_mode", "next_append_position", "purge_generation", "descriptor_json"
        })
            if (!await ColumnExistsAsync(connection, _names.Collections, column, cancellationToken).ConfigureAwait(false))
                missing.Add("column:" + _names.Collections + "." + column);
        if (missing.Count != 0) return missing.ToArray();
        await using var command = connection.CreateCommand();
        command.CommandText = $"SELECT value FROM {_names.ProviderState} WHERE key = 'restore_epoch';";
        command.CommandTimeout = TimeoutSeconds();
        object? value = await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
        if (value is not string text || !long.TryParse(text, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out long epoch) || epoch < 0)
            missing.Add("state:" + _names.ProviderState + ".restore_epoch");
        return missing.ToArray();
    }

    private async ValueTask<string[]> GetMissingSchemaAuthorityColumnsAsync(SqliteConnection connection, CancellationToken cancellationToken)
    {
        var missing = new List<string>();
        foreach (string column in new[] { "application_id", "store_instance_id", "baseline_id", "checksum", "generation", "last_plan_id", "applied_at" })
            if (!await ColumnExistsAsync(connection, _names.SchemaBaseline, column, cancellationToken).ConfigureAwait(false)) missing.Add("column:" + _names.SchemaBaseline + "." + column);
        foreach (string column in new[] { "application_id", "generation", "baseline_id", "checksum", "plan_id", "classification", "outcome", "provider_version", "structural_verification", "external_data_migration", "semantic_conversion", "external_attestation_id", "external_signer_id", "applied_at" })
            if (!await ColumnExistsAsync(connection, _names.SchemaHistory, column, cancellationToken).ConfigureAwait(false)) missing.Add("column:" + _names.SchemaHistory + "." + column);
        return missing.ToArray();
    }

    private async ValueTask<string[]> GetMissingRelationColumnsAsync(SqliteConnection connection, SqlitePhysicalModel.RelationModel relation, CancellationToken cancellationToken)
    {
        var missing = new List<string>();
        foreach (string column in new[] { "source_record_id", "target_record_id", "ordinal" })
            if (!await ColumnExistsAsync(connection, relation.Table, column, cancellationToken).ConfigureAwait(false))
                missing.Add("column:" + relation.Table + "." + column);
        return missing.ToArray();
    }
}
