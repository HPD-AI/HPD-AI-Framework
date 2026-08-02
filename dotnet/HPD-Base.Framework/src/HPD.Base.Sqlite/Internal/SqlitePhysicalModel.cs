using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Data.Sqlite;

namespace HPD.Base.Sqlite;

internal sealed class SqlitePhysicalModel
{
    private readonly Dictionary<string, CollectionModel> _collections;
    private readonly RelationModel[] _relations;

    internal SqlitePhysicalModel(HPDBaseSqliteOptions options)
    {
        var nativeNames = new Dictionary<string, string>(StringComparer.Ordinal);
        _collections = new Dictionary<string, CollectionModel>(StringComparer.Ordinal);
        foreach (CollectionDefinition definition in options.Collections)
        {
            string table = Native("b_c_", definition.Id);
            Claim(nativeNames, table, definition.Id);
            FieldModel[] fields = (definition.Fields ?? []).Select(field =>
            {
                string column = Native("f_", field.Id);
                string? presence = field.Required && !field.Nullable ? null : Native("p_", field.Id);
                Claim(nativeNames, column, field.Id);
                if (presence is not null) Claim(nativeNames, presence, field.Id + ":presence");
                return new FieldModel(field, column, presence);
            }).ToArray();
            IndexModel[] indexes = (definition.Indexes ?? []).Select(index =>
            {
                string name = Native("b_i_", index.Id);
                Claim(nativeNames, name, index.Id);
                FieldModel[] parts = (index.Parts ?? []).Select(part => fields.Single(field =>
                    part.Kind == IndexPartKind.Field && string.Equals(field.Definition.Id, part.FieldId, StringComparison.Ordinal))).ToArray();
                return new IndexModel(index, name, parts);
            }).ToArray();
            if (!_collections.TryAdd(definition.Id, new CollectionModel(definition, table, fields, indexes)))
                throw new InvalidOperationException("SQLite collection identity is duplicated.");
        }
        _relations = options.Collections.SelectMany(static collection => collection.Fields ?? [])
            .Where(static field => field.Relation is { OwningSide: BaseRelationOwningSide.Source, LocalMultiplicity: BaseRelationMultiplicity.Many })
            .Select(field =>
            {
                RelationDefinition relation = field.Relation!;
                string table = Native("b_r_", relation.Id);
                Claim(nativeNames, table, relation.Id);
                return new RelationModel(relation, table, field.Name);
            }).OrderBy(static relation => relation.Definition.Id, StringComparer.Ordinal).ToArray();
    }

    internal CollectionModel Collection(string id) => _collections.TryGetValue(id, out CollectionModel? model)
        ? model
        : throw new InvalidOperationException("SQLite collection physical mapping is unavailable.");

    internal CollectionModel[] Collections => _collections.Values.OrderBy(static model => model.Definition.Id, StringComparer.Ordinal).ToArray();
    internal RelationModel[] Relations => _relations;
    internal RelationModel[] RelationsFrom(string collectionId) => _relations.Where(relation => relation.Definition.SourceCollectionId == collectionId).ToArray();
    internal RelationModel[] RelationsTo(string collectionId) => _relations.Where(relation => relation.Definition.TargetCollectionId == collectionId).ToArray();

    private static string Native(string prefix, string id) => prefix + Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(id))).Substring(0, 32);
    private static void Claim(Dictionary<string, string> names, string name, string id)
    { if (names.TryGetValue(name, out string? owner) && owner != id) throw new InvalidOperationException("SQLite stable native-name collision detected."); names[name] = id; }

internal sealed class CollectionModel
    {
        internal CollectionModel(CollectionDefinition definition, string table, FieldModel[] fields, IndexModel[] indexes)
        { Definition = definition; Table = table; Fields = fields; Indexes = indexes; }
        internal CollectionDefinition Definition { get; }
        internal string Table { get; }
        internal FieldModel[] Fields { get; }
        internal IndexModel[] Indexes { get; }
        internal bool HasExtensionJson => Definition.SchemaMode == SchemaMode.Loose || Definition.UnknownFields == UnknownFieldPolicy.Preserve;
        internal string SelectList => string.Join(", ", new[] { "record_id", "revision", "created_at", "updated_at" }
            .Concat(Fields.SelectMany(static item => item.PresenceColumn is null ? [item.Column] : new[] { item.PresenceColumn, item.Column }))
            .Concat(HasExtensionJson ? Enumerable.Repeat("extension_json", 1) : Enumerable.Empty<string>()));
        internal string PayloadColumns => string.Join(", ", Fields.SelectMany((item, index) => item.PresenceColumn is null ? [item.Column] : new[] { item.PresenceColumn, item.Column })
            .Concat(HasExtensionJson ? Enumerable.Repeat("extension_json", 1) : Enumerable.Empty<string>()));
        internal string PayloadParameters => string.Join(", ", Fields.SelectMany((item, index) => item.PresenceColumn is null ? ["$f" + index] : new[] { "$p" + index, "$f" + index })
            .Concat(HasExtensionJson ? Enumerable.Repeat("$extension", 1) : Enumerable.Empty<string>()));
        internal string PayloadAssignments => string.Join(", ", Fields.SelectMany((item, index) => item.PresenceColumn is null ? [item.Column + " = $f" + index] : new[] { item.PresenceColumn + " = $p" + index, item.Column + " = $f" + index })
            .Concat(HasExtensionJson ? Enumerable.Repeat("extension_json = $extension", 1) : Enumerable.Empty<string>()));
        internal string PayloadColumnClause => PayloadColumns.Length == 0 ? "" : ", " + PayloadColumns;
        internal string PayloadParameterClause => PayloadParameters.Length == 0 ? "" : ", " + PayloadParameters;
        internal string PayloadAssignmentClause => PayloadAssignments.Length == 0 ? "" : ", " + PayloadAssignments;

        internal string CreateSql(string? table = null)
        {
            table ??= Table;
            var columns = new List<string>
            {
                "record_id TEXT PRIMARY KEY",
                "revision INTEGER NOT NULL",
                "created_at TEXT NOT NULL",
                "updated_at TEXT NOT NULL",
            };
            foreach (FieldModel field in Fields)
            {
                if (field.PresenceColumn is not null) columns.Add(field.PresenceColumn + " INTEGER NOT NULL DEFAULT 0 CHECK (" + field.PresenceColumn + " IN (0,1))");
                columns.Add(field.Column + " " + field.SqlType + (field.PresenceColumn is null ? " NOT NULL" : ""));
            }
            if (HasExtensionJson) columns.Add("extension_json TEXT NULL");
            return $"CREATE TABLE IF NOT EXISTS {table} (\n  {string.Join(",\n  ", columns)}\n);";
        }

        internal void AddPayloadParameters(SqliteCommand command, RecordPayload payload, bool includeExtensions)
        {
            Dictionary<string, JsonElement> values = SqliteRecordSerializer.NormalizeObjectPayload(payload).Fields ?? [];
            var known = Fields.Select(static field => field.Definition.Name).ToHashSet(StringComparer.Ordinal);
            for (int index = 0; index < Fields.Length; index++)
            {
                FieldModel field = Fields[index];
                bool present = values.TryGetValue(field.Definition.Name, out JsonElement value);
                if (field.PresenceColumn is not null) command.Parameters.AddWithValue("$p" + index, present ? 1 : 0);
                command.Parameters.AddWithValue("$f" + index, present ? field.Encode(value) : DBNull.Value);
            }
            if (HasExtensionJson && includeExtensions)
            {
                var extensions = values.Where(pair => !known.Contains(pair.Key)).ToDictionary(static pair => pair.Key, static pair => pair.Value, StringComparer.Ordinal);
                command.Parameters.AddWithValue("$extension", extensions.Count == 0 ? DBNull.Value : SqliteRecordSerializer.Serialize(new RecordPayload { Kind = RecordPayloadKind.FieldMap, Fields = extensions }));
            }
        }

        internal RecordEnvelope ReadEnvelope(SqliteDataReader reader, string storeId)
        {
            int ordinal = 0;
            string recordId = reader.GetString(ordinal++);
            long revision = reader.GetInt64(ordinal++);
            DateTimeOffset created = DateTimeOffset.Parse(reader.GetString(ordinal++), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);
            DateTimeOffset updated = DateTimeOffset.Parse(reader.GetString(ordinal++), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);
            var payload = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
            foreach (FieldModel field in Fields)
            {
                bool present = field.PresenceColumn is null || reader.GetInt64(ordinal++) == 1;
                if (present) payload[field.Definition.Name] = reader.IsDBNull(ordinal) ? Json("null") : field.Decode(reader.GetValue(ordinal));
                ordinal++;
            }
            if (HasExtensionJson && !reader.IsDBNull(ordinal))
                foreach (var pair in SqliteRecordSerializer.Deserialize(reader.GetString(ordinal)).Fields ?? []) payload[pair.Key] = pair.Value;
            return new RecordEnvelope
            {
                CollectionId = Definition.Id,
                Id = new RecordId(recordId),
                Payload = new RecordPayload { Kind = RecordPayloadKind.FieldMap, Fields = payload },
                Metadata = SqliteRecordMapper.Metadata(revision, created, updated, storeId),
            };
        }

        private static JsonElement Json(string json) { using JsonDocument document = JsonDocument.Parse(json); return document.RootElement.Clone(); }
    }

internal sealed class IndexModel
    {
        internal IndexModel(IndexDefinition definition, string name, FieldModel[] parts)
        { Definition = definition; Name = name; Parts = parts; }
        internal IndexDefinition Definition { get; }
        internal string Name { get; }
        internal FieldModel[] Parts { get; }
        internal string CreateSql(CollectionModel collection)
        {
            string unique = Definition.Unique || Definition.Kind == IndexKind.Unique ? "UNIQUE " : "";
            string columns = string.Join(", ", Parts.Select((field, index) => field.Column +
                (Definition.Parts![index].Direction == IndexSortDirection.Desc ? " DESC" : " ASC")));
            return $"CREATE {unique}INDEX IF NOT EXISTS {Name} ON {collection.Table}({columns});";
        }
    }

internal sealed class RelationModel
    {
        internal RelationModel(RelationDefinition definition, string table, string sourceFieldName)
        { Definition = definition; Table = table; SourceFieldName = sourceFieldName; }
        internal RelationDefinition Definition { get; }
        internal string Table { get; }
        internal string SourceFieldName { get; }
        internal string CreateSql() => $"""
CREATE TABLE IF NOT EXISTS {Table} (
  source_record_id TEXT NOT NULL,
  target_record_id TEXT NOT NULL,
  ordinal INTEGER NOT NULL CHECK (ordinal >= 0),
  PRIMARY KEY (source_record_id, ordinal),
  UNIQUE (source_record_id, target_record_id)
);
""";
        internal string SourceIndex => "ix_" + Table + "_source";
        internal string TargetIndex => "ix_" + Table + "_target";
    }

internal sealed class FieldModel
    {
        internal FieldModel(FieldDefinition definition, string column, string? presenceColumn)
        { Definition = definition; Column = column; PresenceColumn = presenceColumn; }
        internal FieldDefinition Definition { get; }
        internal string Column { get; }
        internal string? PresenceColumn { get; }
        internal string SqlType => Definition.Type switch
        { "boolean" or "integer" => "INTEGER", "number" => "REAL", _ => "TEXT" };

        internal object Encode(JsonElement value)
        {
            if (value.ValueKind == JsonValueKind.Null) return DBNull.Value;
            if (Definition.Format == "date-time")
                return value.GetDateTimeOffset().ToUniversalTime().ToString("O", CultureInfo.InvariantCulture);
            return Definition.Type switch
            {
                "boolean" => value.GetBoolean() ? 1L : 0L,
                "integer" => value.GetInt64(),
                "number" => Finite(value.GetDouble()),
                "string" or "id" => value.GetString()!,
                "decimal" => value.GetDecimal().ToString(CultureInfo.InvariantCulture),
                _ => value.GetRawText(),
            };
        }

        internal JsonElement Decode(object value) => Definition.Type switch
        {
            _ when Definition.Format == "date-time" => Parse("\"" + JsonEncodedText.Encode(Convert.ToString(value, CultureInfo.InvariantCulture)!).ToString() + "\""),
            "boolean" => Parse(Convert.ToInt64(value, CultureInfo.InvariantCulture) == 0 ? "false" : "true"),
            "integer" => Parse(Convert.ToInt64(value, CultureInfo.InvariantCulture).ToString(CultureInfo.InvariantCulture)),
            "number" => Parse(Convert.ToDouble(value, CultureInfo.InvariantCulture).ToString("R", CultureInfo.InvariantCulture)),
            "string" or "id" => Parse("\"" + JsonEncodedText.Encode(Convert.ToString(value, CultureInfo.InvariantCulture)!).ToString() + "\""),
            "decimal" => Parse(decimal.Parse(Convert.ToString(value, CultureInfo.InvariantCulture)!, CultureInfo.InvariantCulture).ToString(CultureInfo.InvariantCulture)),
            _ => Parse(Convert.ToString(value, CultureInfo.InvariantCulture)!),
        };

        private static double Finite(double value) => double.IsFinite(value) ? value : throw new InvalidOperationException("SQLite cannot store a non-finite number.");
        private static JsonElement Parse(string json) { using JsonDocument document = JsonDocument.Parse(json); return document.RootElement.Clone(); }
    }
}
