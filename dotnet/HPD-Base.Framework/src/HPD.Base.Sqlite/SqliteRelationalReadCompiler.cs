using System.Collections.Immutable;
using System.Globalization;
using System.Text;
using Microsoft.Data.Sqlite;

namespace HPD.Base.Sqlite;

internal sealed class SqliteRelationalReadCompiler(
    SqlitePhysicalModel physical,
    SqliteNames names,
    IReadOnlyList<BaseExportedSubjectDefinition> subjects,
    BaseRelationalReadExecutionRequest request)
{
    private readonly Dictionary<string, BaseRelationalParameterValue> _parameters =
        request.ParameterValues.ToDictionary(static value => value.ParameterId, StringComparer.Ordinal);
    private readonly Dictionary<string, BaseRelationalReadParameter> _parameterDefinitions =
        request.Plan.Parameters.ToDictionary(static value => value.Id, StringComparer.Ordinal);
    private readonly Dictionary<string, BaseRelationalReadSourcePolicy> _policies =
        request.SourcePolicies.ToDictionary(static value => value.SourceId, StringComparer.Ordinal);
    private readonly Dictionary<string, Source> _sources = request.Plan.Sources
        .Select((source, index) => new Source(source, physical.Collection(source.CollectionId), "s" + index.ToString(CultureInfo.InvariantCulture)))
        .ToDictionary(static source => source.Definition.Id, StringComparer.Ordinal);
    private readonly List<(string Name, QueryValue Value, bool Binary)> _bound = [];
    private readonly Dictionary<BaseRelationalOperand, SubjectJoin> _subjectJoins = [];

    internal CompiledRead Compile()
    {
        BaseRelationalReadPlan plan = request.Plan;
        if (plan.Sources.Length == 0 || plan.Sources.Length != plan.Joins.Length + 1)
            throw new InvalidOperationException();
        _ = _policies.Count == plan.Sources.Length ? true : throw new InvalidOperationException();

        string from = " FROM " + _sources[plan.Sources[0].Id].Collection.Table + " s0";
        var where = new List<string>();
        BaseRelationalReadSource rootDefinition = plan.Sources[0];
        if (_policies[rootDefinition.Id].Filter is { } rootPolicy)
            where.Add(Policy(rootPolicy, _sources[rootDefinition.Id]));
        for (int index = 0; index < plan.Joins.Length; index++)
        {
            BaseRelationalReadJoin join = plan.Joins[index];
            Source source = _sources[plan.Sources[index + 1].Id];
            string equality = Present(join.Left) + " AND " + Present(join.Right) + " AND " +
                Operand(join.Left, plan) + " IS " + Operand(join.Right, plan);
            BaseRelationalReadSourcePolicy sourcePolicy = _policies[source.Definition.Id];
            if (sourcePolicy.CollectionId != source.Definition.CollectionId) throw new InvalidOperationException();
            string policy = sourcePolicy.Filter is null ? "" : " AND (" + Policy(sourcePolicy.Filter, source) + ")";
            if (join.Kind is BaseJoinKind.Inner or BaseJoinKind.Left)
                from += (join.Kind == BaseJoinKind.Inner ? " INNER JOIN " : " LEFT JOIN ") + source.Collection.Table + " " + source.Alias + " ON " + equality + policy;
            else
                where.Add((join.Kind == BaseJoinKind.Anti ? "NOT " : "") + "EXISTS (SELECT 1 FROM " + source.Collection.Table + " " + source.Alias + " WHERE " + equality + policy + ")");
        }
        foreach (BaseRelationalOperand operand in plan.Projection.Select(static value => value.Operand)
                     .Where(static value => value.Kind == BaseRelationalOperandKind.SubjectReference))
        {
            Source source = _sources[Required(operand.SourceId)];
            BaseExportedSubjectDefinition definition = subjects.Single(value =>
                string.Equals(value.Id, operand.SubjectContractId, StringComparison.Ordinal)
                && value.Version == operand.SubjectContractVersion
                && string.Equals(value.ValidationPlan.PrivateCollectionId, source.Definition.CollectionId, StringComparison.Ordinal));
            string suffix = _subjectJoins.Count.ToString(CultureInfo.InvariantCulture);
            var joined = new SubjectJoin("sc" + suffix, "sl" + suffix, definition);
            _subjectJoins.Add(operand, joined);
            from += $" INNER JOIN {names.SubjectContracts} {joined.ContractAlias} ON {joined.ContractAlias}.contract_id={BindText(definition.Id)} AND {joined.ContractAlias}.contract_version={definition.Version.ToString(CultureInfo.InvariantCulture)}";
            from += $" INNER JOIN {names.SubjectLifetimes} {joined.LifetimeAlias} ON {joined.LifetimeAlias}.contract_id={joined.ContractAlias}.contract_id AND {joined.LifetimeAlias}.contract_version={joined.ContractAlias}.contract_version AND {joined.LifetimeAlias}.subject_id={source.Alias}.record_id AND {joined.LifetimeAlias}.private_collection_id={BindText(source.Definition.CollectionId)} AND {joined.LifetimeAlias}.private_record_id={source.Alias}.record_id";
        }
        string projection = string.Join(", ", plan.Projection.Select((item, index) =>
            Operand(item.Operand, plan) + " AS v" + index.ToString(CultureInfo.InvariantCulture)));
        if (plan.Predicate is not null) where.Add(Predicate(plan.Predicate, plan));

        string whereSql = where.Count == 0 ? "" : " WHERE " + string.Join(" AND ", where.Select(static value => "(" + value + ")"));
        string group = plan.GroupKeys.Length == 0 ? "" : " GROUP BY " + string.Join(", ", plan.GroupKeys.Select(item => Operand(item, plan)));
        string having = plan.Having is null ? "" : " HAVING " + Predicate(plan.Having, plan);
        string distinct = plan.Distinct ? "DISTINCT " : "";
        string core = "SELECT " + distinct + projection + from + whereSql + group + having;
        string projectionOrder = string.Join(", ", plan.Projection.Select((_, index) => "v" + index.ToString(CultureInfo.InvariantCulture)));
        string order = plan.Sort.Length == 0
            ? " ORDER BY " + projectionOrder
            : " ORDER BY " + string.Join(", ", plan.Sort.Select(item => Operand(item.Operand, plan) + (item.Direction == QuerySortDirection.Desc ? " DESC" : " ASC") + NullOrder(item.Nulls))) + ", " + projectionOrder;
        string count = "SELECT COUNT(*) FROM (" + core + ") counted";
        string page = core + order + " LIMIT $__limit OFFSET $__offset";
        return new CompiledRead(count, page, _bound.ToArray(), plan.Projection.Select(item => Kind(item.Operand, plan)).ToArray(),
            plan.Projection.Select(item => BinaryOperand(item.Operand)).ToArray(),
            plan.Projection.Select(item => item.Operand.Kind switch
            {
                BaseRelationalOperandKind.SubjectReference => _subjectJoins[item.Operand].Definition,
                BaseRelationalOperandKind.StoredSubjectReference => subjects.Single(subject =>
                    string.Equals(subject.Id, item.Operand.SubjectContractId, StringComparison.Ordinal)
                    && subject.Version == item.Operand.SubjectContractVersion),
                _ => null,
            }).ToArray(),
            plan.Projection.Select(static item => item.FieldId).ToArray());
    }

    private string Predicate(BaseRelationalPredicate node, BaseRelationalReadPlan plan) => node.Kind switch
    {
        FilterNodeKind.True => "1=1",
        FilterNodeKind.False => "1=0",
        FilterNodeKind.Not => "NOT (" + Predicate(Only(node.Children), plan) + ")",
        FilterNodeKind.And => JoinChildren(node.Children, " AND ", plan),
        FilterNodeKind.Or => JoinChildren(node.Children, " OR ", plan),
        FilterNodeKind.IsNull => Present(Required(node.Left)) + " AND " + Operand(Required(node.Left), plan) + " IS NULL",
        FilterNodeKind.IsDefined => Present(Required(node.Left)),
        FilterNodeKind.Compare => Comparison(node, plan),
        FilterNodeKind.In => In(node, plan),
        FilterNodeKind.Between => Between(node, plan),
        _ => throw new InvalidOperationException(),
    };

    private string Comparison(BaseRelationalPredicate node, BaseRelationalReadPlan plan)
    {
        BaseRelationalOperand left = Required(node.Left);
        BaseRelationalOperand right = Required(node.Right);
        string operation = node.Operator switch
        {
            FilterOperator.Equal => " IS ",
            FilterOperator.NotEqual => " IS NOT ",
            _ => Compare(node.Operator),
        };
        return Present(left) + " AND " + Present(right) + " AND " + Operand(left, plan) + operation + Operand(right, plan);
    }

    private string In(BaseRelationalPredicate node, BaseRelationalReadPlan plan)
    {
        BaseRelationalOperand left = Required(node.Left);
        QueryValue[] values = ArrayValues(Required(node.Right));
        QueryValue[] nonNull = values.Where(static value => value.Kind != QueryValueKind.Null).ToArray();
        var branches = new List<string>();
        if (nonNull.Length != 0)
        {
            bool binary = BinaryOperand(left) || BinaryOperand(Required(node.Right));
            branches.Add(Operand(left, plan) + " IN (" + string.Join(",", nonNull.Select(value => Bind(value, binary))) + ")");
        }
        if (values.Any(static value => value.Kind == QueryValueKind.Null)) branches.Add(Operand(left, plan) + " IS NULL");
        return Present(left) + " AND " + Present(Required(node.Right)) + " AND (" + (branches.Count == 0 ? "1=0" : string.Join(" OR ", branches)) + ")";
    }

    private string Between(BaseRelationalPredicate node, BaseRelationalReadPlan plan)
    {
        QueryValue[] values = ArrayValues(Required(node.Right));
        if (values.Length != 2) throw new InvalidOperationException();
        bool binary = BinaryOperand(Required(node.Left)) || BinaryOperand(Required(node.Right));
        return Present(Required(node.Left)) + " AND " + Present(Required(node.Right)) + " AND " + Operand(Required(node.Left), plan) + " BETWEEN " + Bind(values[0], binary) + " AND " + Bind(values[1], binary);
    }

    private QueryValue[] ArrayValues(BaseRelationalOperand operand) => operand.Kind switch
    {
        BaseRelationalOperandKind.Parameter => _parameters[Required(operand.ParameterId)].Value.Array ?? throw new InvalidOperationException(),
        BaseRelationalOperandKind.Literal => Required(operand.Literal).Array ?? throw new InvalidOperationException(),
        _ => throw new InvalidOperationException(),
    };

    private string Policy(FilterExpression node, Source source) => node.Kind switch
    {
        FilterNodeKind.True => "1=1",
        FilterNodeKind.False => "1=0",
        FilterNodeKind.Not => "NOT (" + Policy(Only(node.Children), source) + ")",
        FilterNodeKind.And => JoinPolicy(node.Children, " AND ", source),
        FilterNodeKind.Or => JoinPolicy(node.Children, " OR ", source),
        FilterNodeKind.IsNull => FieldPresent(source, Required(node.Field)) + " AND " + Field(source, Required(node.Field)) + " IS NULL",
        FilterNodeKind.IsDefined => FieldPresent(source, Required(node.Field)),
        FilterNodeKind.Compare => PolicyComparison(node, source),
        FilterNodeKind.In => PolicyIn(node, source),
        FilterNodeKind.Between when node.Values is { Length: 2 } => FieldPresent(source, Required(node.Field)) + " AND " + Field(source, Required(node.Field)) + " BETWEEN " + Bind(node.Values[0], BinaryField(source, Required(node.Field))) + " AND " + Bind(node.Values[1], BinaryField(source, Required(node.Field))),
        _ => throw new InvalidOperationException(),
    };

    private string PolicyComparison(FilterExpression node, Source source)
    {
        string fieldId = Required(node.Field);
        QueryValue value = Required(node.Value);
        string operation = node.Operator switch
        {
            FilterOperator.Equal => " IS ",
            FilterOperator.NotEqual => " IS NOT ",
            _ => Compare(node.Operator),
        };
        return FieldPresent(source, fieldId) + " AND " + Field(source, fieldId) + operation + Bind(value, BinaryField(source, fieldId));
    }

    private string PolicyIn(FilterExpression node, Source source)
    {
        string fieldId = Required(node.Field);
        QueryValue[] values = node.Values ?? throw new InvalidOperationException();
        QueryValue[] nonNull = values.Where(static value => value.Kind != QueryValueKind.Null).ToArray();
        var branches = new List<string>();
        if (nonNull.Length != 0) branches.Add(Field(source, fieldId) + " IN (" + string.Join(",", nonNull.Select(value => Bind(value, BinaryField(source, fieldId)))) + ")");
        if (values.Any(static value => value.Kind == QueryValueKind.Null)) branches.Add(Field(source, fieldId) + " IS NULL");
        return FieldPresent(source, fieldId) + " AND (" + (branches.Count == 0 ? "1=0" : string.Join(" OR ", branches)) + ")";
    }

    private string Operand(BaseRelationalOperand operand, BaseRelationalReadPlan plan) => operand.Kind switch
    {
        BaseRelationalOperandKind.RecordId => _sources[Required(operand.SourceId)].Alias + ".record_id",
        BaseRelationalOperandKind.RecordRevision => "('sqlite:' || CAST(" + _sources[Required(operand.SourceId)].Alias + ".revision AS TEXT))",
        BaseRelationalOperandKind.SourceField => Field(_sources[Required(operand.SourceId)], Required(operand.FieldId)),
        BaseRelationalOperandKind.Parameter => Bind(
            _parameters[Required(operand.ParameterId)].Value,
            _parameterDefinitions[Required(operand.ParameterId)].MaximumBinaryBytes is not null),
        BaseRelationalOperandKind.Literal => Bind(Required(operand.Literal)),
        BaseRelationalOperandKind.Aggregate => Aggregate(plan.Aggregates.Single(item => item.Id == operand.AggregateId), plan),
        BaseRelationalOperandKind.SubjectReference => SubjectReference(_subjectJoins[operand], _sources[Required(operand.SourceId)]),
        BaseRelationalOperandKind.StoredSubjectReference => Field(_sources[Required(operand.SourceId)], Required(operand.FieldId)),
        _ => throw new InvalidOperationException(),
    };

    private string Aggregate(BaseRelationalReadAggregate aggregate, BaseRelationalReadPlan plan)
    {
        string value = aggregate.Operand is null ? "*" : Operand(aggregate.Operand, plan);
        bool exactDecimal = aggregate.Operand is not null && Kind(aggregate.Operand, plan) == QueryValueKind.Decimal;
        return aggregate.Kind switch
        {
            BaseAggregateKind.Count => "COUNT(" + value + ")",
            BaseAggregateKind.CountDistinct => "COUNT(DISTINCT " + value + ")",
            BaseAggregateKind.Sum when exactDecimal => "HPD_BASE_DECIMAL_SUM(" + value + ")",
            BaseAggregateKind.Sum => "COALESCE(SUM(" + value + "), 0)",
            BaseAggregateKind.Average when exactDecimal => "HPD_BASE_DECIMAL_AVERAGE(" + value + ")",
            BaseAggregateKind.Average => "AVG(" + value + ")",
            BaseAggregateKind.Minimum => "MIN(" + value + ")",
            BaseAggregateKind.Maximum => "MAX(" + value + ")",
            BaseAggregateKind.Any => "COALESCE(MAX(CASE WHEN " + value + " IS NULL THEN NULL WHEN " + value + " THEN 1 ELSE 0 END), 0)",
            BaseAggregateKind.All => "COALESCE(MIN(CASE WHEN " + value + " IS NULL THEN NULL WHEN " + value + " THEN 1 ELSE 0 END), 1)",
            _ => throw new InvalidOperationException(),
        };
    }

    private QueryValueKind Kind(BaseRelationalOperand operand, BaseRelationalReadPlan plan) => operand.Kind switch
    {
        BaseRelationalOperandKind.RecordId => QueryValueKind.Id,
        BaseRelationalOperandKind.RecordRevision => QueryValueKind.String,
        BaseRelationalOperandKind.SourceField => FieldKind(_sources[Required(operand.SourceId)].Collection.Fields.Single(item => item.Definition.Id == operand.FieldId).Definition),
        BaseRelationalOperandKind.Parameter => _parameters[Required(operand.ParameterId)].Value.Kind,
        BaseRelationalOperandKind.Literal => Required(operand.Literal).Kind,
        BaseRelationalOperandKind.Aggregate => AggregateKind(plan.Aggregates.Single(item => item.Id == operand.AggregateId), plan),
        BaseRelationalOperandKind.SubjectReference => QueryValueKind.SubjectReference,
        BaseRelationalOperandKind.StoredSubjectReference => QueryValueKind.SubjectReference,
        _ => throw new InvalidOperationException(),
    };

    private QueryValueKind AggregateKind(BaseRelationalReadAggregate aggregate, BaseRelationalReadPlan plan) => aggregate.Kind switch
    {
        BaseAggregateKind.Count or BaseAggregateKind.CountDistinct => QueryValueKind.Integer,
        BaseAggregateKind.Any or BaseAggregateKind.All => QueryValueKind.Boolean,
        BaseAggregateKind.Sum => Kind(Required(aggregate.Operand), plan) switch
        {
            QueryValueKind.Integer => QueryValueKind.Integer,
            QueryValueKind.Number => QueryValueKind.Number,
            _ => QueryValueKind.Decimal,
        },
        BaseAggregateKind.Average => Kind(Required(aggregate.Operand), plan) == QueryValueKind.Number
            ? QueryValueKind.Number
            : QueryValueKind.Decimal,
        _ => Kind(Required(aggregate.Operand), plan),
    };

    private static QueryValueKind FieldKind(FieldDefinition field) => field.ScalarKind is BaseScalarKind.Guid or BaseScalarKind.RecordId
        ? QueryValueKind.Id
        : field.ScalarKind == BaseScalarKind.CanonicalJson
        ? QueryValueKind.CanonicalJson
        : field.ScalarKind == BaseScalarKind.ModuleGeneration
        ? QueryValueKind.String
        : field.Format == "date-time"
        ? QueryValueKind.DateTime
        : field.Type switch
    {
        "boolean" => QueryValueKind.Boolean,
        "integer" => QueryValueKind.Integer,
        "number" => QueryValueKind.Number,
        "decimal" => QueryValueKind.Decimal,
        "id" => QueryValueKind.Id,
        "dateTime" => QueryValueKind.DateTime,
        _ => QueryValueKind.String,
    };

    private string Field(Source source, string fieldId)
    {
        SqlitePhysicalModel.FieldModel field = source.Collection.Fields.Single(candidate => candidate.Definition.Id == fieldId);
        return source.Alias + "." + field.Column + (field.Definition.Type == "decimal" ? " COLLATE HPD_BASE_DECIMAL" : "");
    }
    private string SubjectReference(SubjectJoin joined, Source source) =>
        $"json_object('subjectId',{source.Alias}.record_id,'authorityEpochHex',lower(hex({joined.ContractAlias}.authority_epoch)),'incarnationHex',lower(hex({joined.LifetimeAlias}.incarnation)))";
    private string BindText(string value)
    {
        string name = "$s" + _bound.Count.ToString(CultureInfo.InvariantCulture);
        _bound.Add((name, new QueryValue { Kind = QueryValueKind.String, String = value }, false));
        return name;
    }
    private string FieldPresent(Source source, string fieldId)
    {
        SqlitePhysicalModel.FieldModel field = source.Collection.Fields.Single(candidate => candidate.Definition.Id == fieldId);
        return field.PresenceColumn is null ? "1=1" : source.Alias + "." + field.PresenceColumn + " = 1";
    }
    private string Present(BaseRelationalOperand operand) => operand.Kind switch
    {
        BaseRelationalOperandKind.SourceField => FieldPresent(_sources[Required(operand.SourceId)], Required(operand.FieldId)),
        BaseRelationalOperandKind.RecordId => _sources[Required(operand.SourceId)].Alias + ".record_id IS NOT NULL",
        BaseRelationalOperandKind.RecordRevision => _sources[Required(operand.SourceId)].Alias + ".revision IS NOT NULL",
        _ => "1=1",
    };
    private string Bind(QueryValue value, bool binary = false) { string name = "$r" + _bound.Count.ToString(CultureInfo.InvariantCulture); _bound.Add((name, value, binary)); return name; }
    private bool BinaryOperand(BaseRelationalOperand operand) => operand.Kind switch
    {
        BaseRelationalOperandKind.SourceField => _sources[Required(operand.SourceId)].Collection.Fields
            .Single(item => item.Definition.Id == operand.FieldId).Definition.ScalarKind == BaseScalarKind.Binary,
        BaseRelationalOperandKind.Parameter => _parameterDefinitions[Required(operand.ParameterId)].MaximumBinaryBytes is not null,
        _ => false,
    };
    private static bool BinaryField(Source source, string fieldId) => source.Collection.Fields
        .Single(item => item.Definition.Id == fieldId).Definition.ScalarKind == BaseScalarKind.Binary;
    private string JoinChildren(BaseRelationalPredicate[]? children, string separator, BaseRelationalReadPlan plan) => "(" + string.Join(separator, (children ?? throw new InvalidOperationException()).Select(child => Predicate(child, plan))) + ")";
    private string JoinPolicy(FilterExpression[]? children, string separator, Source source) => "(" + string.Join(separator, (children ?? throw new InvalidOperationException()).Select(child => Policy(child, source))) + ")";
    private static string Compare(FilterOperator operation) => operation switch { FilterOperator.Equal => " = ", FilterOperator.NotEqual => " <> ", FilterOperator.LessThan => " < ", FilterOperator.LessThanOrEqual => " <= ", FilterOperator.GreaterThan => " > ", FilterOperator.GreaterThanOrEqual => " >= ", _ => throw new InvalidOperationException() };
    private static string NullOrder(QueryNullOrder order) => order switch { QueryNullOrder.First => " NULLS FIRST", QueryNullOrder.Last => " NULLS LAST", _ => "" };
    private static T Required<T>(T? value) where T : class => value ?? throw new InvalidOperationException();
    private static string Required(string? value) => string.IsNullOrWhiteSpace(value) ? throw new InvalidOperationException() : value;
    private static T Only<T>(T[]? values) => values is { Length: 1 } ? values[0] : throw new InvalidOperationException();

    private sealed record Source(BaseRelationalReadSource Definition, SqlitePhysicalModel.CollectionModel Collection, string Alias);
    private sealed record SubjectJoin(string ContractAlias, string LifetimeAlias, BaseExportedSubjectDefinition Definition);

internal sealed record CompiledRead(
        string CountSql,
        string PageSql,
        (string Name, QueryValue Value, bool Binary)[] Parameters,
        QueryValueKind[] Kinds,
        bool[] BinaryOutputs,
        BaseExportedSubjectDefinition?[] SubjectDefinitions,
        string[] FieldIds)
    {
        internal void Bind(SqliteCommand command)
        {
            foreach ((string name, QueryValue value, bool binary) in Parameters)
                command.Parameters.AddWithValue(name, Native(value, binary));
        }

        internal BaseRelationalRow ReadRow(SqliteDataReader reader)
        {
            var fields = new BaseRelationalFieldValue[FieldIds.Length];
            for (int index = 0; index < fields.Length; index++)
                fields[index] = new BaseRelationalFieldValue { FieldId = FieldIds[index], Value = ReadValue(reader, index, Kinds[index], BinaryOutputs[index], SubjectDefinitions[index]) };
            return new BaseRelationalRow { Fields = fields };
        }
    }

    internal static long EstimateBytes(BaseRelationalRow row)
    {
        long bytes = 0;
        foreach (BaseRelationalFieldValue field in row.Fields)
            bytes = checked(bytes + (long)field.FieldId.Length * 2 +
                (field.Value.Kind == QueryValueKind.CanonicalJson
                    ? field.Value.CanonicalJsonUtf8.Length
                    : (long)ValueText(field.Value).Length * 2) + 16);
        return bytes;
    }
    private static object Native(QueryValue value, bool binary)
    {
        if (binary)
            return value.Kind == QueryValueKind.Null
                ? DBNull.Value
                : BaseBinary.FromBase64(value.String ?? throw new InvalidOperationException()).ToArray();
        return value.Kind switch
    {
        QueryValueKind.Null => DBNull.Value,
        QueryValueKind.String => value.String!,
        QueryValueKind.Id => value.Id!,
        QueryValueKind.Boolean => value.Boolean == true ? 1L : 0L,
        QueryValueKind.Integer => value.Integer!.Value,
        QueryValueKind.Number => value.Number!.Value,
        QueryValueKind.Decimal => value.Decimal!,
        QueryValueKind.DateTime => value.DateTime!.Value.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture),
        QueryValueKind.CanonicalJson => new UTF8Encoding(false, true).GetString(value.CanonicalJsonUtf8.AsSpan()),
        _ => throw new InvalidOperationException(),
    };
    }
    private static BaseRelationalReadExecutionResult Never() => throw new InvalidOperationException();
    private static QueryValue ReadValue(SqliteDataReader reader, int ordinal, QueryValueKind kind, bool binary, BaseExportedSubjectDefinition? subject)
    {
        if (reader.IsDBNull(ordinal)) return new QueryValue { Kind = QueryValueKind.Null };
        if (binary)
            return new QueryValue { Kind = QueryValueKind.String, String = Convert.ToBase64String((byte[])reader.GetValue(ordinal)) };
        return kind switch
        {
            QueryValueKind.Boolean => new QueryValue { Kind = kind, Boolean = Convert.ToInt64(reader.GetValue(ordinal), CultureInfo.InvariantCulture) != 0 },
            QueryValueKind.Integer => new QueryValue { Kind = kind, Integer = Convert.ToInt64(reader.GetValue(ordinal), CultureInfo.InvariantCulture) },
            QueryValueKind.Number => new QueryValue { Kind = kind, Number = Convert.ToDouble(reader.GetValue(ordinal), CultureInfo.InvariantCulture) },
            QueryValueKind.Decimal => new QueryValue { Kind = kind, Decimal = Convert.ToString(reader.GetValue(ordinal), CultureInfo.InvariantCulture) },
            QueryValueKind.DateTime => new QueryValue { Kind = kind, DateTime = DateTimeOffset.Parse(Convert.ToString(reader.GetValue(ordinal), CultureInfo.InvariantCulture)!, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind) },
            QueryValueKind.Id => new QueryValue { Kind = kind, Id = Convert.ToString(reader.GetValue(ordinal), CultureInfo.InvariantCulture) },
            QueryValueKind.SubjectReference => ReadSubjectReference(Convert.ToString(reader.GetValue(ordinal), CultureInfo.InvariantCulture)!, subject!),
            QueryValueKind.CanonicalJson => new QueryValue
            {
                Kind = kind,
                CanonicalJsonUtf8 = ImmutableArray.Create(BaseStrictUtf8.Encode(Convert.ToString(reader.GetValue(ordinal), CultureInfo.InvariantCulture)!)),
            },
            _ => new QueryValue { Kind = QueryValueKind.String, String = Convert.ToString(reader.GetValue(ordinal), CultureInfo.InvariantCulture) },
        };
    }
    private static QueryValue ReadSubjectReference(string json, BaseExportedSubjectDefinition definition)
    {
        using System.Text.Json.JsonDocument document = System.Text.Json.JsonDocument.Parse(json);
        System.Text.Json.JsonElement root = document.RootElement;
        string epoch = root.TryGetProperty("authorityEpochHex", out System.Text.Json.JsonElement epochHex)
            ? BaseSubjectReferenceEncoding.Encode(Convert.FromHexString(epochHex.GetString()!))
            : root.GetProperty("authorityEpoch").GetString()!;
        string incarnation = root.TryGetProperty("incarnationHex", out System.Text.Json.JsonElement incarnationHex)
            ? BaseSubjectReferenceEncoding.Encode(Convert.FromHexString(incarnationHex.GetString()!))
            : root.GetProperty("incarnation").GetString()!;
        BaseSubjectId subjectId = BaseSubjectId.Create(
            root.GetProperty("subjectId").GetString()!, definition.SubjectIdKind, definition.MaximumSubjectIdUtf8Bytes);
        BaseSubjectAuthorityEpoch authorityEpoch = BaseSubjectAuthorityEpoch.Parse(epoch);
        BaseSubjectIncarnation subjectIncarnation = BaseSubjectIncarnation.Parse(incarnation);
        if (root.EnumerateObject().Count() != 3)
            throw new InvalidOperationException(BaseSubjectErrorCodes.ReferenceInvalid);
        return new QueryValue
        {
            Kind = QueryValueKind.SubjectReference,
            SubjectId = new string(subjectId.Value.AsSpan()),
            SubjectIdKind = definition.SubjectIdKind,
            SubjectIdMaximumUtf8Bytes = definition.MaximumSubjectIdUtf8Bytes,
            SubjectAuthorityEpoch = authorityEpoch.ToBase64Url(),
            SubjectIncarnation = subjectIncarnation.ToBase64Url(),
        };
    }
    private static string ValueText(QueryValue value) => value.Kind switch
    { QueryValueKind.Null => "", QueryValueKind.String => value.String ?? "", QueryValueKind.Id => value.Id ?? "", QueryValueKind.Boolean => value.Boolean?.ToString() ?? "", QueryValueKind.Integer => value.Integer?.ToString(CultureInfo.InvariantCulture) ?? "", QueryValueKind.Number => value.Number?.ToString("R", CultureInfo.InvariantCulture) ?? "", QueryValueKind.Decimal => value.Decimal ?? "", QueryValueKind.DateTime => value.DateTime?.ToString("O", CultureInfo.InvariantCulture) ?? "", QueryValueKind.CanonicalJson => value.CanonicalJsonUtf8.IsDefault ? "" : Convert.ToBase64String(value.CanonicalJsonUtf8.AsSpan()), _ => "" };
}
