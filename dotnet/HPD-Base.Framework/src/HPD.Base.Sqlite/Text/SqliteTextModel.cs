using System.Security.Cryptography;
using System.Text;

namespace HPD.Base.Sqlite;

internal sealed class SqliteTextModel
{
    internal const string StateTable = "hpd_base_text_indexes";

    internal SqliteTextModel(IReadOnlyList<CollectionDefinition> collections) =>
        Indexes = collections
            .SelectMany(collection => (collection.TextIndexes ?? []).Select(index => new IndexModel(collection, index)))
            .OrderBy(static value => value.Definition.Id, StringComparer.Ordinal)
            .ToArray();

    internal IndexModel[] Indexes { get; }

    internal IEnumerable<string> SchemaStatements()
    {
        yield return $"CREATE TABLE IF NOT EXISTS {StateTable} (collection_id TEXT NOT NULL COLLATE BINARY, index_id TEXT NOT NULL COLLATE BINARY, generation INTEGER NOT NULL CHECK(generation > 0), purge_generation INTEGER NOT NULL CHECK(purge_generation >= 0), applied_position INTEGER NOT NULL CHECK(applied_position >= 0), state TEXT NOT NULL COLLATE BINARY, definition_checksum BLOB NOT NULL, PRIMARY KEY(collection_id,index_id));";
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
        internal string CreateSql => $"CREATE TABLE IF NOT EXISTS {Table} (record_id TEXT NOT NULL COLLATE BINARY PRIMARY KEY, revision TEXT NOT NULL COLLATE BINARY, journal_position INTEGER NOT NULL CHECK(journal_position > 0));";
        internal string CreateFtsSql => $"CREATE VIRTUAL TABLE IF NOT EXISTS {FtsTable} USING fts5(record_id UNINDEXED, content, tokenize='unicode61 remove_diacritics 0');";
    }
}
