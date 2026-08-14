namespace HPD.Payments.Adapters.Postgres;

/// <summary>Defines the PostgreSQL D-OWNER, D-REL, and D-CONT schema and locking statements.</summary>
public static class PostgresSql
{
    /// <summary>Creates adapter-owned append-only tables and indexes; it grants no authority and performs no migration deletion.</summary>
    /// <param name="schema">Validated PostgreSQL schema identifier.</param>
    /// <returns>Idempotent PostgreSQL DDL.</returns>
    public static string CreateSchema(string schema) => $"""
        CREATE SCHEMA IF NOT EXISTS {schema};
        CREATE TABLE IF NOT EXISTS {schema}.owner_heads (
          scope text NOT NULL, authority smallint NOT NULL, subject text NOT NULL,
          generation bigint NOT NULL CHECK (generation > 0), epoch bigint NOT NULL CHECK (epoch > 0),
          fence bigint NOT NULL CHECK (fence > 0), topology_revision bigint NOT NULL CHECK (topology_revision > 0),
          PRIMARY KEY (scope, authority, subject));
        CREATE TABLE IF NOT EXISTS {schema}.owner_facts (
          scope text NOT NULL, authority smallint NOT NULL, subject text NOT NULL,
          generation bigint NOT NULL CHECK (generation > 0), semantic_digest bytea NOT NULL,
          payload bytea NOT NULL, recorded_at timestamptz NOT NULL DEFAULT clock_timestamp(),
          PRIMARY KEY (scope, authority, subject, generation),
          UNIQUE (scope, authority, subject, semantic_digest));
        CREATE TABLE IF NOT EXISTS {schema}.relations (
          scope text NOT NULL, relation_id text NOT NULL, relation_kind smallint NOT NULL,
          source_authority smallint NOT NULL, source_subject text NOT NULL, source_generation bigint NOT NULL,
          target_authority smallint NOT NULL, target_subject text NOT NULL, target_generation bigint NOT NULL,
          relation_revision bigint NOT NULL, state smallint NOT NULL, residue_code text NOT NULL,
          PRIMARY KEY (scope, relation_id));
        CREATE TABLE IF NOT EXISTS {schema}.continuations (
          scope text NOT NULL, owner_authority smallint NOT NULL, owner_subject text NOT NULL,
          owner_generation bigint NOT NULL, continuation_id text NOT NULL, digest bytea NOT NULL,
          state smallint NOT NULL, lease_epoch bigint NOT NULL, fence bigint NOT NULL,
          available_at timestamptz NOT NULL DEFAULT clock_timestamp(), PRIMARY KEY (scope, continuation_id));
        CREATE INDEX IF NOT EXISTS continuations_discovery ON {schema}.continuations
          (scope, state, available_at, continuation_id);
        CREATE TABLE IF NOT EXISTS {schema}.custody_observations (
          scope text NOT NULL, owner_authority smallint NOT NULL, owner_subject text NOT NULL,
          inventory_generation bigint NOT NULL, instance_id text NOT NULL, payload bytea NOT NULL,
          recorded_at timestamptz NOT NULL DEFAULT clock_timestamp(),
          PRIMARY KEY (scope, instance_id, inventory_generation));
        """;

    /// <summary>Gets the serializable owner-head lock used before compare-bind and append.</summary>
    public const string LockOwner = "SELECT generation, epoch, fence, topology_revision FROM {0}.owner_heads WHERE scope=@scope AND authority=@authority AND subject=@subject FOR UPDATE";

    /// <summary>Gets the two-endpoint relation guard lock. Endpoints must be sorted before binding to prevent lock-order inversion.</summary>
    public const string LockRelationEndpoints = "SELECT authority, subject, generation FROM {0}.owner_heads WHERE scope=@scope AND ((authority=@a1 AND subject=@s1) OR (authority=@a2 AND subject=@s2)) ORDER BY authority, subject FOR UPDATE";

    /// <summary>Gets the continuation claim statement using skip-locked only for competing workers, never for authority admission.</summary>
    public const string ClaimContinuations = "SELECT continuation_id FROM {0}.continuations WHERE scope=@scope AND state=1 AND available_at<=clock_timestamp() AND (continuation_id>@after OR @after='') ORDER BY continuation_id FOR UPDATE SKIP LOCKED LIMIT @maximum";
}
