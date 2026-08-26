using System.Buffers.Binary;
using System.Globalization;
using System.Collections.Immutable;
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
                string? presence = field.Presence == BaseFieldPresence.Required && field.Nullability == BaseFieldNullability.NonNullable ? null : Native("p_", field.Id);
                Claim(nativeNames, column, field.Id);
                if (presence is not null) Claim(nativeNames, presence, field.Id + ":presence");
                return new FieldModel(field, column, presence);
            }).ToArray();
            IndexModel[] indexes = (definition.Indexes ?? []).Select(index =>
            {
                FieldDefinition[] ordered = (definition.Fields ?? []).OrderBy(static field => field.Id, StringComparer.Ordinal).ToArray();
                index = BaseSchemaContract.SealIndex(index, ordered);
                string id = index.Id.ToString();
                string name = Native("b_i_", id);
                Claim(nativeNames, name, id);
                string? equalityColumn = index.Unique ? Native("k_", id) : null;
                if (equalityColumn is not null) Claim(nativeNames, equalityColumn, id + ":equality");
                FieldModel[] parts = index.Parts.Select(part => fields.Single(field => string.Equals(field.Definition.Id, ordered[part.FieldOrdinal].Id, StringComparison.Ordinal))).ToArray();
                bool orderable = index.Parts.All(part => ordered[part.FieldOrdinal].ScalarCodec is { OrderingVersion: not null, OrderingChecksum: not null });
                OrderingPartModel[] ordering = !orderable ? [] : index.Parts.Select((part, ordinal) =>
                {
                    FieldModel field = fields.Single(candidate => string.Equals(candidate.Definition.Id, ordered[part.FieldOrdinal].Id, StringComparison.Ordinal));
                    string rank = Native("r_", id + ":" + ordinal.ToString(CultureInfo.InvariantCulture));
                    string value = Native("o_", id + ":" + ordinal.ToString(CultureInfo.InvariantCulture));
                    Claim(nativeNames, rank, id + ":rank:" + ordinal); Claim(nativeNames, value, id + ":value:" + ordinal);
                    return new OrderingPartModel(part, field, rank, value);
                }).ToArray();
                FieldModel[] predicateFields = ordered.Select(field => fields.Single(model => model.Definition.Id == field.Id)).ToArray();
                return new IndexModel(index, name, equalityColumn, parts, ordering, predicateFields);
            }).ToArray();
            CollectionDefinition physicalDefinition = definition with { Indexes = indexes.Select(static model => model.Definition).ToArray() };
            if (!_collections.TryAdd(definition.Id, new CollectionModel(physicalDefinition, table, fields, indexes)))
                throw new InvalidOperationException("SQLite collection identity is duplicated.");
        }
        _relations = options.Collections.SelectMany(static collection => collection.Fields ?? [])
            .Where(static field => field.Relation is { OwningSide: BaseRelationOwningSide.Source, LocalMultiplicity: BaseRelationMultiplicity.Many })
            .Select(field =>
            {
                RelationDefinition relation = field.Relation!;
                string table = Native("b_r_", relation.Id);
                Claim(nativeNames, table, relation.Id);
                return new RelationModel(relation, table, field.WireName);
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
        internal string SelectList => string.Join(", ", new[] { "record_id", "revision", "created_at", "updated_at", "append_position" }
            .Concat(Fields.SelectMany(static item => item.PresenceColumn is null ? [item.Column] : new[] { item.PresenceColumn, item.Column }))
            .Concat(HasExtensionJson ? Enumerable.Repeat("extension_json", 1) : Enumerable.Empty<string>()));
        internal string PayloadColumns => string.Join(", ", Fields.SelectMany((item, index) => item.PresenceColumn is null ? [item.Column] : new[] { item.PresenceColumn, item.Column })
            .Concat(Indexes.Where(static index => index.EqualityColumn is not null).Select(static index => index.EqualityColumn!))
            .Concat(Indexes.SelectMany(static index => index.OrderingParts.SelectMany(static part => new[] { part.StateColumn, part.ValueColumn })))
            .Concat(HasExtensionJson ? Enumerable.Repeat("extension_json", 1) : Enumerable.Empty<string>()));
        internal string PayloadParameters => string.Join(", ", Fields.SelectMany((item, index) => item.PresenceColumn is null ? ["$f" + index] : new[] { "$p" + index, "$f" + index })
            .Concat(Indexes.Where(static index => index.EqualityColumn is not null).Select((_, index) => "$k" + index))
            .Concat(Indexes.SelectMany((index, indexOrdinal) => index.OrderingParts.SelectMany((_, partOrdinal) => new[] { $"$r{indexOrdinal}_{partOrdinal}", $"$o{indexOrdinal}_{partOrdinal}" })))
            .Concat(HasExtensionJson ? Enumerable.Repeat("$extension", 1) : Enumerable.Empty<string>()));
        internal string PayloadAssignments => string.Join(", ", Fields.SelectMany((item, index) => item.PresenceColumn is null ? [item.Column + " = $f" + index] : new[] { item.PresenceColumn + " = $p" + index, item.Column + " = $f" + index })
            .Concat(Indexes.Where(static index => index.EqualityColumn is not null).Select((item, index) => item.EqualityColumn + " = $k" + index))
            .Concat(Indexes.SelectMany((index, indexOrdinal) => index.OrderingParts.SelectMany((part, partOrdinal) => new[] { part.StateColumn + $" = $r{indexOrdinal}_{partOrdinal}", part.ValueColumn + $" = $o{indexOrdinal}_{partOrdinal}" })))
            .Concat(HasExtensionJson ? Enumerable.Repeat("extension_json = $extension", 1) : Enumerable.Empty<string>()));
        internal string IndexPayloadAssignments => string.Join(", ",
            Indexes.Where(static index => index.EqualityColumn is not null).Select((item, index) => item.EqualityColumn + " = $k" + index)
                .Concat(Indexes.SelectMany((index, indexOrdinal) => index.OrderingParts.SelectMany((part, partOrdinal) => new[] { part.StateColumn + $" = $r{indexOrdinal}_{partOrdinal}", part.ValueColumn + $" = $o{indexOrdinal}_{partOrdinal}" }))));
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
                "append_position INTEGER NOT NULL UNIQUE CHECK (append_position > 0)",
                "latest_mutation_position INTEGER NOT NULL DEFAULT 0 CHECK (latest_mutation_position >= 0)",
            };
            foreach (FieldModel field in Fields)
            {
                if (field.PresenceColumn is not null) columns.Add(field.PresenceColumn + " INTEGER NOT NULL DEFAULT 0 CHECK (" + field.PresenceColumn + " IN (0,1))");
                columns.Add(field.Column + " " + field.SqlType +
                    (field.Definition.ScalarKind is BaseScalarKind.RecordId or BaseScalarKind.ModuleGeneration ? " COLLATE BINARY" : "") +
                    (field.PresenceColumn is null ? " NOT NULL" : ""));
            }
            foreach (IndexModel index in Indexes.Where(static index => index.EqualityColumn is not null))
                columns.Add(index.EqualityColumn + " BLOB NULL");
            foreach (OrderingPartModel part in Indexes.SelectMany(static index => index.OrderingParts))
            {
                columns.Add(part.StateColumn + " INTEGER NOT NULL");
                columns.Add(part.ValueColumn + " " + part.SqlType +
                    (part.Field.Definition.ScalarKind is BaseScalarKind.RecordId or BaseScalarKind.ModuleGeneration ? " COLLATE BINARY" : "") +
                    " NOT NULL");
            }
            if (HasExtensionJson) columns.Add("extension_json TEXT NULL");
            return $"CREATE TABLE IF NOT EXISTS {table} (\n  {string.Join(",\n  ", columns)}\n);";
        }

        internal void AddPayloadParameters(SqliteCommand command, RecordPayload payload, bool includeExtensions)
        {
            Dictionary<string, JsonElement> values = SqliteRecordSerializer.NormalizeObjectPayload(payload).Fields ?? [];
            var known = Fields.Select(static field => field.Definition.WireName).ToHashSet(StringComparer.Ordinal);
            for (int index = 0; index < Fields.Length; index++)
            {
                FieldModel field = Fields[index];
                bool present = values.TryGetValue(field.Definition.WireName, out JsonElement value);
                if (field.PresenceColumn is not null) command.Parameters.AddWithValue("$p" + index, present ? 1 : 0);
                command.Parameters.AddWithValue("$f" + index, present ? field.Encode(value) : DBNull.Value);
            }
            int equalityOrdinal = 0;
            var normalized = new RecordPayload { Kind = RecordPayloadKind.FieldMap, Fields = values };
            foreach (IndexModel index in Indexes.Where(static index => index.EqualityColumn is not null))
            {
                object key = BaseLogicalIndexEvaluator.Includes(Definition, index.Definition, normalized)
                    ? BaseLogicalIndexEvaluator.Key(Definition, index.Definition, normalized)
                    : DBNull.Value;
                command.Parameters.AddWithValue("$k" + equalityOrdinal++, key);
            }
            for (int indexOrdinal = 0; indexOrdinal < Indexes.Length; indexOrdinal++)
            {
                IndexModel index = Indexes[indexOrdinal];
                for (int partOrdinal = 0; partOrdinal < index.OrderingParts.Length; partOrdinal++)
                {
                    (long rank, object shadow) = index.OrderingParts[partOrdinal].Encode(values);
                    command.Parameters.AddWithValue($"$r{indexOrdinal}_{partOrdinal}", rank);
                    command.Parameters.AddWithValue($"$o{indexOrdinal}_{partOrdinal}", shadow);
                }
            }
            if (HasExtensionJson && includeExtensions)
            {
                var extensions = values.Where(pair => !known.Contains(pair.Key)).ToDictionary(static pair => pair.Key, static pair => pair.Value, StringComparer.Ordinal);
                command.Parameters.AddWithValue("$extension", extensions.Count == 0 ? DBNull.Value : SqliteRecordSerializer.Serialize(new RecordPayload { Kind = RecordPayloadKind.FieldMap, Fields = extensions }));
            }
        }

        internal void AddIndexPayloadParameters(SqliteCommand command, RecordPayload payload)
        {
            Dictionary<string, JsonElement> values = SqliteRecordSerializer.NormalizeObjectPayload(payload).Fields ?? [];
            int equalityOrdinal = 0;
            var normalized = new RecordPayload { Kind = RecordPayloadKind.FieldMap, Fields = values };
            foreach (IndexModel index in Indexes.Where(static index => index.EqualityColumn is not null))
            {
                object key = BaseLogicalIndexEvaluator.Includes(Definition, index.Definition, normalized)
                    ? BaseLogicalIndexEvaluator.Key(Definition, index.Definition, normalized)
                    : DBNull.Value;
                command.Parameters.AddWithValue("$k" + equalityOrdinal++, key);
            }
            for (int indexOrdinal = 0; indexOrdinal < Indexes.Length; indexOrdinal++)
                for (int partOrdinal = 0; partOrdinal < Indexes[indexOrdinal].OrderingParts.Length; partOrdinal++)
                {
                    (long rank, object shadow) = Indexes[indexOrdinal].OrderingParts[partOrdinal].Encode(values);
                    command.Parameters.AddWithValue($"$r{indexOrdinal}_{partOrdinal}", rank);
                    command.Parameters.AddWithValue($"$o{indexOrdinal}_{partOrdinal}", shadow);
                }
        }

        internal RecordEnvelope ReadEnvelope(SqliteDataReader reader, string storeId) =>
            ReadEnvelope(reader, storeId, out _);

        internal RecordEnvelope ReadEnvelope(SqliteDataReader reader, string storeId, out long appendPosition)
        {
            int ordinal = 0;
            string recordId = reader.GetString(ordinal++);
            long revision = reader.GetInt64(ordinal++);
            DateTimeOffset created = DateTimeOffset.Parse(reader.GetString(ordinal++), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);
            DateTimeOffset updated = DateTimeOffset.Parse(reader.GetString(ordinal++), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);
            appendPosition = reader.GetInt64(ordinal++);
            var payload = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
            foreach (FieldModel field in Fields)
            {
                bool present = field.PresenceColumn is null || reader.GetInt64(ordinal++) == 1;
                if (present) payload[field.Definition.WireName] = reader.IsDBNull(ordinal) ? Json("null") : field.Decode(reader.GetValue(ordinal));
                ordinal++;
            }
            if (HasExtensionJson && !reader.IsDBNull(ordinal))
                foreach (var pair in SqliteRecordSerializer.Deserialize(reader.GetString(ordinal)).Fields ?? []) payload[pair.Key] = pair.Value;
            return new RecordEnvelope
            {
                CollectionId = Definition.Id,
                Id = RecordId.Create(recordId),
                Payload = new RecordPayload { Kind = RecordPayloadKind.FieldMap, Fields = payload },
                Metadata = SqliteRecordMapper.Metadata(revision, created, updated, storeId),
            };
        }

        private static JsonElement Json(string json) { using JsonDocument document = JsonDocument.Parse(json); return document.RootElement.Clone(); }
    }

internal sealed class IndexModel
    {
        private readonly FieldModel[] _fields;
        internal IndexModel(BaseLogicalIndexDefinition definition, string name, string? equalityColumn, FieldModel[] parts, OrderingPartModel[] orderingParts, FieldModel[] fields)
        { Definition = definition; Name = name; EqualityColumn = equalityColumn; Parts = parts; OrderingParts = orderingParts; _fields = fields; }
        internal BaseLogicalIndexDefinition Definition { get; }
        internal string Name { get; }
        internal string? EqualityColumn { get; }
        internal FieldModel[] Parts { get; }
        internal OrderingPartModel[] OrderingParts { get; }
        internal bool HasOrdering => OrderingParts.Length != 0;
        internal string? UniqueName => EqualityColumn is null ? null : HasOrdering ? Name + "_u" : Name;
        internal string CreateSql(CollectionModel collection)
            => !HasOrdering ? UniqueSql(collection) + ";" : EqualityColumn is null ? OrderingSql(collection) + ";" : OrderingSql(collection) + ";" + Environment.NewLine + UniqueSql(collection) + ";";
        internal string OrderingSql(CollectionModel collection)
        {
            if (!HasOrdering) throw new InvalidOperationException(BaseSchemaErrorCodes.ContractInvalid);
            string columns = string.Join(", ", OrderingParts.SelectMany(part => new[]
            {
                part.StateColumn + (part.Definition.Direction == BaseIndexSortDirection.Descending ? " DESC" : " ASC"),
                part.ValueColumn + (part.SqlType == "TEXT" ? " COLLATE BINARY" : "") + (part.Definition.Direction == BaseIndexSortDirection.Descending ? " DESC" : " ASC"),
            }).Concat(["record_id ASC"]));
            string predicate = Predicate(Definition.MembershipPredicate.Root, Definition.MembershipPredicate.Nodes.ToDictionary(static node => node.Id));
            return $"CREATE INDEX IF NOT EXISTS {Name} ON {collection.Table}({columns}) WHERE {predicate}";
        }
        internal string UniqueSql(CollectionModel collection) => EqualityColumn is null
            ? throw new InvalidOperationException(BaseSchemaErrorCodes.ContractInvalid)
            : $"CREATE UNIQUE INDEX IF NOT EXISTS {UniqueName} ON {collection.Table}({EqualityColumn}) WHERE ({PredicateSql}) AND {EqualityColumn} IS NOT NULL";
        internal string PredicateSql => Predicate(Definition.MembershipPredicate.Root, Definition.MembershipPredicate.Nodes.ToDictionary(static node => node.Id));

        private string Predicate(BaseIndexPredicateId id, Dictionary<BaseIndexPredicateId, BaseIndexPredicateNode> nodes)
        {
            BaseIndexPredicateNode node = nodes[id]; FieldModel? field = node.FieldOrdinal is { } ordinal ? _fields[ordinal] : null;
            return node.Kind switch
            {
                BaseIndexPredicateNodeKind.True => "1=1",
                BaseIndexPredicateNodeKind.False => "1=0",
                BaseIndexPredicateNodeKind.IsDefined => field!.PresenceColumn is null ? "1=1" : field.PresenceColumn + "=1",
                BaseIndexPredicateNodeKind.IsMissing => field!.PresenceColumn is null ? "1=0" : field.PresenceColumn + "=0",
                BaseIndexPredicateNodeKind.IsNull => field!.PresenceColumn is null ? field.Column + " IS NULL" : $"({field.PresenceColumn}=1 AND {field.Column} IS NULL)",
                BaseIndexPredicateNodeKind.IsNotNull => field!.PresenceColumn is null ? field.Column + " IS NOT NULL" : $"({field.PresenceColumn}=1 AND {field.Column} IS NOT NULL)",
                BaseIndexPredicateNodeKind.Equal => $"({(field!.PresenceColumn is null ? "" : field.PresenceColumn + "=1 AND ")}{field.Column}={Literal(node.Literal!)})",
                BaseIndexPredicateNodeKind.And => "(" + string.Join(" AND ", node.Children.Select(child => Predicate(child, nodes))) + ")",
                BaseIndexPredicateNodeKind.Or => "(" + string.Join(" OR ", node.Children.Select(child => Predicate(child, nodes))) + ")",
                BaseIndexPredicateNodeKind.Not => "NOT (" + Predicate(node.Children[0], nodes) + ")",
                _ => throw new InvalidOperationException(BaseSchemaErrorCodes.ContractInvalid),
            };
        }

        private static string Literal(BaseCanonicalScalarLiteral literal)
        {
            ReadOnlySpan<byte> bytes = literal.CanonicalBytes.AsSpan();
            return literal.Kind switch
            {
                BaseScalarKind.String or BaseScalarKind.RecordId or BaseScalarKind.ModuleGeneration or BaseScalarKind.ClosedEnum => "'" + StrictUtf8(bytes).Replace("'", "''", StringComparison.Ordinal) + "'",
                BaseScalarKind.Guid when bytes.Length == 16 => "'" + new Guid(bytes, bigEndian: true).ToString("D") + "'",
                BaseScalarKind.UtcDateTime when bytes.Length == 8 => "'" + new DateTimeOffset(BinaryPrimitives.ReadInt64BigEndian(bytes), TimeSpan.Zero).ToString("yyyy-MM-dd'T'HH:mm:ss.fffffff'Z'", CultureInfo.InvariantCulture) + "'",
                BaseScalarKind.Int32 => BinaryPrimitives.ReadInt32BigEndian(bytes).ToString(CultureInfo.InvariantCulture),
                BaseScalarKind.Int64 => BinaryPrimitives.ReadInt64BigEndian(bytes).ToString(CultureInfo.InvariantCulture),
                BaseScalarKind.UInt32 => BinaryPrimitives.ReadUInt32BigEndian(bytes).ToString(CultureInfo.InvariantCulture),
                BaseScalarKind.UInt64 => "'" + BinaryPrimitives.ReadUInt64BigEndian(bytes).ToString(CultureInfo.InvariantCulture) + "'",
                BaseScalarKind.Decimal when bytes.Length == 17 => "'" + BaseScalarCanonical.DecimalText(new BaseDecimalValue(BinaryPrimitives.ReadInt128BigEndian(bytes[..16]), bytes[16])) + "'",
                BaseScalarKind.Boolean => bytes[0] == 0 ? "0" : "1",
                BaseScalarKind.Binary => "X'" + Convert.ToHexString(bytes) + "'",
                _ => throw new InvalidOperationException(BaseSchemaErrorCodes.ContractInvalid),
            };
        }

        private static string StrictUtf8(ReadOnlySpan<byte> bytes) => new UTF8Encoding(false, true).GetString(bytes);
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

internal sealed class OrderingPartModel
    {
        internal OrderingPartModel(BaseLogicalIndexPart definition, FieldModel field, string stateColumn, string valueColumn)
        { Definition = definition; Field = field; StateColumn = stateColumn; ValueColumn = valueColumn; }
        internal BaseLogicalIndexPart Definition { get; }
        internal FieldModel Field { get; }
        internal string StateColumn { get; }
        internal string ValueColumn { get; }
        internal string SqlType => Field.Definition.ScalarKind switch
        {
            BaseScalarKind.Binary or BaseScalarKind.Guid => "BLOB",
            BaseScalarKind.Int32 or BaseScalarKind.Int64 or BaseScalarKind.UInt32 or BaseScalarKind.Boolean or BaseScalarKind.UtcDateTime or BaseScalarKind.ClosedEnum => "INTEGER",
            BaseScalarKind.RecordId => "TEXT",
            BaseScalarKind.String or BaseScalarKind.ModuleGeneration or BaseScalarKind.UInt64 or BaseScalarKind.Decimal => "TEXT",
            _ => throw new InvalidOperationException(BaseSchemaErrorCodes.ContractInvalid),
        };

        internal (long Rank, object Shadow) Encode(Dictionary<string, JsonElement> values)
        {
            bool present = values.TryGetValue(Field.Definition.WireName, out JsonElement value); bool nonNull = present && value.ValueKind != JsonValueKind.Null;
            long rank = Definition.NullOrder switch
            {
                BaseIndexNullOrder.MissingThenNullThenValue => !present ? 0 : nonNull ? 2 : 1,
                BaseIndexNullOrder.ValueThenNullThenMissing => nonNull ? 0 : present ? 1 : 2,
                _ => throw new InvalidOperationException(BaseSchemaErrorCodes.ContractInvalid),
            };
            return (rank, nonNull ? Value(value) : Neutral());
        }

        private object Value(JsonElement value) => Field.Definition.ScalarKind switch
        {
            BaseScalarKind.String or BaseScalarKind.RecordId or BaseScalarKind.ModuleGeneration => value.GetString()!,
            BaseScalarKind.Binary => BaseBinary.FromBase64(value.GetString()!).ToArray(),
            BaseScalarKind.Int32 => (long)value.GetInt32(), BaseScalarKind.Int64 => value.GetInt64(), BaseScalarKind.UInt32 => (long)value.GetUInt32(),
            BaseScalarKind.UInt64 => value.GetUInt64().ToString("D20", CultureInfo.InvariantCulture),
            BaseScalarKind.Decimal when BaseScalarCanonical.TryParseDecimal(value.GetRawText(), out BaseDecimalValue item) => SortableDecimal(item),
            BaseScalarKind.Boolean => value.GetBoolean() ? 1L : 0L,
            BaseScalarKind.Guid when Guid.TryParseExact(value.GetString(), "D", out Guid item) => GuidBytes(item),
            BaseScalarKind.UtcDateTime => value.GetDateTimeOffset().Ticks,
            BaseScalarKind.ClosedEnum => EnumOrdinal(value.GetString()!),
            _ => throw new InvalidOperationException(BaseSchemaErrorCodes.ContractInvalid),
        };

        private object Neutral() => SqlType switch { "BLOB" => Array.Empty<byte>(), "INTEGER" => 0L, _ => string.Empty };
        private long EnumOrdinal(string value)
        {
            ImmutableArray<string> literals = Field.Definition.ScalarConstraints?.AllowedEnumLiterals ?? [];
            int ordinal = literals.BinarySearch(value, StringComparer.Ordinal);
            return ordinal >= 0 ? ordinal : throw new InvalidOperationException(BaseSchemaErrorCodes.ContractInvalid);
        }
        private static byte[] GuidBytes(Guid value) { byte[] bytes = new byte[16]; value.TryWriteBytes(bytes, bigEndian: true, out _); return bytes; }
        private static string SortableDecimal(BaseDecimalValue value)
        {
            if (value.Coefficient == 0) return "1" + new string('0', 41);
            bool negative = value.Coefficient < 0; string digits = value.Coefficient.ToString(CultureInfo.InvariantCulture).TrimStart('-');
            int biasedExponent = digits.Length - value.Scale + 27; string padded = digits.PadRight(39, '0');
            if (!negative) return "2" + biasedExponent.ToString("D2", CultureInfo.InvariantCulture) + padded;
            char[] inverted = padded.Select(static character => (char)('9' - (character - '0'))).ToArray();
            return "0" + (99 - biasedExponent).ToString("D2", CultureInfo.InvariantCulture) + new string(inverted);
        }
    }

internal sealed class FieldModel
    {
        internal FieldModel(FieldDefinition definition, string column, string? presenceColumn)
        { Definition = definition; Column = column; PresenceColumn = presenceColumn; }
        internal FieldDefinition Definition { get; }
        internal string Column { get; }
        internal string? PresenceColumn { get; }
        internal string SqlType => Definition.Format == "base64" ? "BLOB" : Definition.ScalarKind switch
        {
            BaseScalarKind.RecordId or BaseScalarKind.ModuleGeneration => "TEXT",
            BaseScalarKind.Decimal or BaseScalarKind.UInt64 => "TEXT",
            BaseScalarKind.Int32 or BaseScalarKind.Int64 or BaseScalarKind.UInt32 or BaseScalarKind.Boolean => "INTEGER",
            _ => Definition.Type == "number" ? "REAL" : "TEXT",
        };

        internal object Encode(JsonElement value)
        {
            if (value.ValueKind == JsonValueKind.Null) return DBNull.Value;
            if (Definition.Format == "date-time")
            {
                _ = BaseScalarCanonical.Encode(BaseScalarKind.UtcDateTime, value);
                return value.GetString()!;
            }
            if (Definition.Format == "base64")
            {
                BaseBinary binary = BaseBinary.FromBase64(value.GetString()!);
                if (Definition.MaximumBytes is not int maximum || binary.Length > maximum)
                    throw new InvalidOperationException(BaseBinaryErrorCodes.ValueTooLarge);
                return binary.ToArray();
            }
            if (Definition.ScalarKind == BaseScalarKind.Decimal)
            {
                if (!BaseScalarCanonical.TryParseDecimal(value.GetRawText(), out BaseDecimalValue parsed)) throw new InvalidOperationException(BaseSchemaErrorCodes.ContractInvalid);
                return BaseScalarCanonical.DecimalText(parsed);
            }
            if (Definition.ScalarKind == BaseScalarKind.UInt64)
                return value.TryGetUInt64(out _) ? value.GetRawText() : throw new InvalidOperationException(BaseSchemaErrorCodes.ContractInvalid);
            if (Definition.ScalarKind == BaseScalarKind.ModuleGeneration)
                return BaseModuleGeneration.ParseCanonical(value.GetString()!).ToCanonicalString();
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
            _ when Definition.Format == "base64" => Parse("\"" + Convert.ToBase64String((byte[])value) + "\""),
            _ when Definition.ScalarKind is BaseScalarKind.Decimal or BaseScalarKind.UInt64 => Parse(Convert.ToString(value, CultureInfo.InvariantCulture)!),
            _ when Definition.ScalarKind == BaseScalarKind.ModuleGeneration => Parse("\"" + Convert.ToString(value, CultureInfo.InvariantCulture) + "\""),
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
