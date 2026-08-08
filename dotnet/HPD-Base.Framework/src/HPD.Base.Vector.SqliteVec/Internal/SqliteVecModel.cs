using System.Security.Cryptography;
using System.Text;

namespace HPD.Base.Vector.SqliteVec;

internal sealed class SqliteVecModel
{
    internal SqliteVecModel(IReadOnlyList<CollectionDefinition> collections)
    {
        Indexes = collections.SelectMany(collection => (collection.VectorIndexes ?? []).Select(index => new IndexModel(collection, index))).OrderBy(static item => item.Definition.Id, StringComparer.Ordinal).ToArray();
    }
    internal IndexModel[] Indexes { get; }
    internal const string StateTable = "hpd_base_vector_indexes";
    internal string StateSchemaSql => $"CREATE TABLE IF NOT EXISTS {StateTable} (collection_id TEXT NOT NULL COLLATE BINARY, index_id TEXT NOT NULL COLLATE BINARY, generation INTEGER NOT NULL CHECK(generation > 0), purge_generation INTEGER NOT NULL CHECK(purge_generation >= 0), applied_position INTEGER NOT NULL CHECK(applied_position >= 0), state TEXT NOT NULL COLLATE BINARY, PRIMARY KEY(collection_id,index_id));";
    internal IEnumerable<string> SchemaStatements()
    {
        yield return StateSchemaSql;
        foreach (IndexModel index in Indexes)
        {
            yield return index.CreateSql;
            yield return $"INSERT INTO {StateTable}(collection_id,index_id,generation,purge_generation,applied_position,state) VALUES ('{index.Definition.CollectionId.Replace("'", "''", StringComparison.Ordinal)}','{index.Definition.Id.Replace("'", "''", StringComparison.Ordinal)}',1,0,0,'ready') ON CONFLICT(collection_id,index_id) DO NOTHING;";
        }
    }
    internal IndexModel Get(string collectionId, string indexId) => Indexes.Single(item => item.Definition.CollectionId == collectionId && item.Definition.Id == indexId);
    internal static string Native(string prefix, string id) => prefix + Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(id))).Substring(0, 32);

    internal sealed class IndexModel
    {
        internal IndexModel(CollectionDefinition collection, VectorIndexDefinition definition)
        {
            Collection = collection; Definition = definition; Table = Native("b_v_", definition.Id);
            Filters = definition.FilterFieldIds.Select(id => new FilterModel((collection.Fields ?? []).Single(field => field.Id == id), Native("p_", id), Native("f_", id))).ToArray();
        }
        internal CollectionDefinition Collection { get; }
        internal VectorIndexDefinition Definition { get; }
        internal string Table { get; }
        internal FilterModel[] Filters { get; }
        internal string CreateSql => $"CREATE TABLE IF NOT EXISTS {Table} (record_id TEXT NOT NULL COLLATE BINARY PRIMARY KEY, revision TEXT NOT NULL COLLATE BINARY, journal_position INTEGER NOT NULL, vector BLOB NOT NULL{string.Concat(Filters.Select(filter => $", {filter.PresenceColumn} INTEGER NOT NULL CHECK({filter.PresenceColumn} IN (0,1)), {filter.ValueColumn} {filter.SqlType} COLLATE BINARY NULL"))});";
    }

    internal sealed record FilterModel(FieldDefinition Definition, string PresenceColumn, string ValueColumn)
    {
        internal string SqlType => Definition.Type is "integer" or "boolean" ? "INTEGER" : "TEXT";
    }
}
