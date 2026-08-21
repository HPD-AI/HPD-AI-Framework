using System.Security.Cryptography;
using System.Text;

namespace HPD.Base.Sqlite;

internal sealed class SqliteTextModel
{
    internal const string StateTable = "hpd_base_text_indexes";
    internal const string RebuildReceiptTable = "hpd_base_text_rebuild_receipts";
    internal const string RebuildProgressTable = "hpd_base_text_rebuild_progress";
    internal const string RebuildStageTable = "hpd_base_text_rebuild_stage";
    internal const string RebuildAppliedTable = "hpd_base_text_rebuild_applied";

    internal SqliteTextModel(IReadOnlyList<CollectionDefinition> collections) =>
        Indexes = collections
            .SelectMany(collection => (collection.TextIndexes ?? []).Select(index => new IndexModel(collection, index)))
            .OrderBy(static value => value.Definition.Id, StringComparer.Ordinal)
            .ToArray();

    internal IndexModel[] Indexes { get; }

    internal IEnumerable<string> SchemaStatements()
    {
        yield return $"CREATE TABLE IF NOT EXISTS {StateTable} (collection_id TEXT NOT NULL COLLATE BINARY, index_id TEXT NOT NULL COLLATE BINARY, generation INTEGER NOT NULL CHECK(generation > 0), purge_generation INTEGER NOT NULL CHECK(purge_generation >= 0), applied_position INTEGER NOT NULL CHECK(applied_position >= 0), state TEXT NOT NULL COLLATE BINARY, definition_checksum BLOB NOT NULL, PRIMARY KEY(collection_id,index_id));";
        yield return $"CREATE TABLE IF NOT EXISTS {RebuildReceiptTable} (scope TEXT NOT NULL COLLATE BINARY, operation TEXT NOT NULL COLLATE BINARY, idempotency_key TEXT NOT NULL COLLATE BINARY, fingerprint BLOB NOT NULL, previous_generation INTEGER NOT NULL, published_generation INTEGER NOT NULL, visible_through INTEGER NOT NULL, record_count INTEGER NOT NULL, publication_checksum BLOB NOT NULL, PRIMARY KEY(scope,operation,idempotency_key));";
        yield return $"CREATE TABLE IF NOT EXISTS {RebuildProgressTable} (scope TEXT NOT NULL COLLATE BINARY, operation TEXT NOT NULL COLLATE BINARY, idempotency_key TEXT NOT NULL COLLATE BINARY, fingerprint BLOB NOT NULL, collection_id TEXT NOT NULL COLLATE BINARY, index_id TEXT NOT NULL COLLATE BINARY, expected_generation INTEGER NOT NULL, staging_generation INTEGER NOT NULL, source_head INTEGER NOT NULL, publication_head INTEGER NULL, applied_through INTEGER NULL, applied_mutation_count INTEGER NOT NULL DEFAULT 0, applied_mutation_checksum BLOB NULL, phase TEXT NOT NULL COLLATE BINARY CHECK(phase IN ('scan','catchup','published')), last_record_id TEXT NULL COLLATE BINARY, record_count INTEGER NOT NULL, canonical_bytes INTEGER NOT NULL, rolling_checksum BLOB NOT NULL, scan_complete INTEGER NOT NULL CHECK(scan_complete IN (0,1)), PRIMARY KEY(scope,operation,idempotency_key));";
        yield return $"CREATE TABLE IF NOT EXISTS {RebuildStageTable} (scope TEXT NOT NULL COLLATE BINARY, operation TEXT NOT NULL COLLATE BINARY, idempotency_key TEXT NOT NULL COLLATE BINARY, record_id TEXT NOT NULL COLLATE BINARY, revision TEXT NULL COLLATE BINARY, journal_position INTEGER NOT NULL, content TEXT NULL, deleted INTEGER NOT NULL CHECK(deleted IN (0,1)), PRIMARY KEY(scope,operation,idempotency_key,record_id));";
        yield return $"CREATE TABLE IF NOT EXISTS {RebuildAppliedTable} (scope TEXT NOT NULL COLLATE BINARY, operation TEXT NOT NULL COLLATE BINARY, idempotency_key TEXT NOT NULL COLLATE BINARY, journal_position INTEGER NOT NULL, record_id TEXT NOT NULL COLLATE BINARY, PRIMARY KEY(scope,operation,idempotency_key,journal_position,record_id));";
        yield return $"CREATE INDEX IF NOT EXISTS ix_hpd_base_text_rebuild_stage_page ON {RebuildStageTable}(scope,operation,idempotency_key,record_id COLLATE BINARY);";
        foreach (IndexModel index in Indexes)
        {
            yield return index.CreateSql;
            yield return index.CreateFtsSql;
            yield return $"INSERT INTO {StateTable}(collection_id,index_id,generation,purge_generation,applied_position,state,definition_checksum) VALUES ('{Escape(index.Definition.CollectionId)}','{Escape(index.Definition.Id)}',1,0,0,'ready',X'{Convert.ToHexString(index.Definition.DefinitionChecksum.AsSpan())}') ON CONFLICT(collection_id,index_id) DO NOTHING;";
        }
    }

    internal IndexModel Get(string collectionId, string indexId) =>
        Indexes.Single(value => value.Definition.CollectionId == collectionId && value.Definition.Id == indexId);

    private static string Escape(string value) => value.Replace("'", "''", StringComparison.Ordinal);
    private static string Native(string prefix, string id) => prefix + Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(id))).Substring(0, 32);

    internal sealed class IndexModel
    {
        internal IndexModel(CollectionDefinition collection, BaseTextIndexDefinition definition)
        {
            Collection = collection;
            Definition = definition;
            Table = Native("b_t_", definition.Id);
            FtsTable = Native("b_tf_", definition.Id);
        }

        internal CollectionDefinition Collection { get; }
        internal BaseTextIndexDefinition Definition { get; }
        internal string Table { get; }
        internal string FtsTable { get; }
        internal string CreateSql => $"CREATE TABLE IF NOT EXISTS {Table} (generation INTEGER NOT NULL CHECK(generation > 0), record_id TEXT NOT NULL COLLATE BINARY, revision TEXT NOT NULL COLLATE BINARY, journal_position INTEGER NOT NULL CHECK(journal_position > 0), PRIMARY KEY(generation,record_id));";
        internal string CreateFtsSql => $"CREATE VIRTUAL TABLE IF NOT EXISTS {FtsTable} USING fts5(generation UNINDEXED, record_id UNINDEXED, content, tokenize='unicode61 remove_diacritics 0');";
    }
}
