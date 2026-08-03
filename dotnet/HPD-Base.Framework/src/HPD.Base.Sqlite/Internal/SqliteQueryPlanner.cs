using HPD.Base;
using HPD.Base.Sqlite;
using Microsoft.Data.Sqlite;
using System.Globalization;
using System.Text.Json;

namespace HPD.Base.Sqlite;

internal sealed class SqliteQueryPlanner
{
    private readonly HPDBaseSqliteOptions _options;
    private readonly SqlitePhysicalModel.CollectionModel _collection;
    private int _parameterId;
    private readonly List<(string Name, object? Value)> _parameters = [];
    private readonly List<string> _unsupported = [];

    /// <summary>Initializes a new instance.</summary>
    public SqliteQueryPlanner(HPDBaseSqliteOptions options, SqlitePhysicalModel.CollectionModel collection)
    {
        _options = options;
        _collection = collection;
    }

    /// <summary>Executes the plan operation.</summary>
    public SqliteQueryPlan Plan(
        RecordQuery query,
        BaseQueryCursorPayload? cursor = null,
        long? appendHighWater = null)
    {
        _parameterId = 0;
        _parameters.Clear();
        _unsupported.Clear();

        if (query.Include is { Length: > 0 }) _unsupported.Add("include");
        if (query.Extensions is { Length: > 0 }) _unsupported.Add("extensions");
        if (query.Count is QueryCountMode.Estimated or QueryCountMode.Limited) _unsupported.Add("count." + query.Count);
        if (query.Select is { Length: > 0 } && query.Select.Length > _options.MaxSelectFields) _unsupported.Add("select.tooManyFields");
        if (query.Select?.Any(field => string.IsNullOrWhiteSpace(field) || field.Contains('.') || field.Any(char.IsControl)) == true) _unsupported.Add("select.field");

        var filter = "1 = 1";
        if (query.Filter is not null)
        {
            var plannedFilter = PlanFilter(query.Filter, 0, new NodeCounter());
            if (plannedFilter is null)
            {
                _unsupported.Add("filter");
            }
            else
            {
                filter += " AND " + plannedFilter;
            }
        }

        if (appendHighWater is { } highWater)
            filter += " AND append_position <= " + AddParameter(highWater);
        if (cursor is not null)
        {
            string? continuation = PlanCursor(query.Sort, cursor);
            if (continuation is null) _unsupported.Add("cursor.key");
            else filter += " AND (" + continuation + ")";
        }

        var sort = PlanSort(query.Sort);
        var page = PlanPage(query.Page);
        var unsupported = _unsupported.ToArray();
        return new SqliteQueryPlan(
            unsupported.Length == 0,
            unsupported,
            $"SELECT {_collection.SelectList} FROM {_collection.Table} WHERE {filter}{sort}{page.Sql}",
            $"SELECT COUNT(*) FROM {_collection.Table} WHERE {filter}",
            _parameters.ToArray(),
            page.PageInfo);
    }

    private string? PlanFilter(FilterExpression filter, int depth, NodeCounter counter)
    {
        if (depth > _options.MaxFilterDepth || ++counter.Value > _options.MaxFilterNodes)
        {
            return null;
        }

        return filter.Kind switch
        {
            FilterNodeKind.True => "(1 = 1)",
            FilterNodeKind.False => "(1 = 0)",
            FilterNodeKind.Not => filter.Children is [{ } child] && PlanFilter(child, depth + 1, counter) is { } childSql ? $"(NOT {childSql})" : null,
            FilterNodeKind.And => PlanChildren("AND", filter.Children, depth, counter),
            FilterNodeKind.Or => PlanChildren("OR", filter.Children, depth, counter),
            FilterNodeKind.Compare => PlanCompare(filter),
            FilterNodeKind.In => PlanIn(filter),
            FilterNodeKind.Between => PlanBetween(filter),
            FilterNodeKind.IsNull => FieldModel(filter.Field) is { } nullField ? $"({nullField.Column} IS NULL{(nullField.PresenceColumn is null ? "" : " AND " + nullField.PresenceColumn + " = 1")})" : null,
            FilterNodeKind.IsDefined => FieldModel(filter.Field) is { } definedField ? (definedField.PresenceColumn is null ? "(1 = 1)" : $"({definedField.PresenceColumn} = 1)") : null,
            _ => null
        };
    }

    private string? PlanChildren(string op, FilterExpression[]? children, int depth, NodeCounter counter)
    {
        if (children is null || children.Length == 0)
        {
            return null;
        }

        var planned = new List<string>(children.Length);
        foreach (var child in children)
        {
            var childSql = PlanFilter(child, depth + 1, counter);
            if (childSql is null)
            {
                return null;
            }

            planned.Add(childSql);
        }

        return "(" + string.Join($" {op} ", planned) + ")";
    }

    private string? PlanCompare(FilterExpression filter)
    {
        if (FieldExpression(filter.Field, forExistence: false) is not { } field || filter.Value is null)
        {
            return null;
        }

        var op = filter.Operator switch
        {
            FilterOperator.Equal => "=",
            FilterOperator.NotEqual => "<>",
            FilterOperator.LessThan => "<",
            FilterOperator.LessThanOrEqual => "<=",
            FilterOperator.GreaterThan => ">",
            FilterOperator.GreaterThanOrEqual => ">=",
            _ => null
        };

        if (op is null || QueryValueToSqlValue(filter.Value) is not { } value)
        {
            return null;
        }

        return $"({field} {op} {AddParameter(value)})";
    }

    private string? PlanIn(FilterExpression filter)
    {
        if (FieldExpression(filter.Field, forExistence: false) is not { } field || filter.Values is null || filter.Values.Length == 0 || filter.Values.Length > _options.MaxInValues)
        {
            return null;
        }

        var values = new List<string>(filter.Values.Length);
        foreach (var value in filter.Values)
        {
            if (QueryValueToSqlValue(value) is not { } sqlValue)
            {
                return null;
            }

            values.Add(AddParameter(sqlValue));
        }

        return $"({field} IN ({string.Join(", ", values)}))";
    }

    private string? PlanBetween(FilterExpression filter)
    {
        if (FieldExpression(filter.Field, forExistence: false) is not { } field || filter.Values is not { Length: 2 })
        {
            return null;
        }

        var lower = QueryValueToSqlValue(filter.Values[0]);
        var upper = QueryValueToSqlValue(filter.Values[1]);
        return lower is null || upper is null ? null : $"({field} BETWEEN {AddParameter(lower)} AND {AddParameter(upper)})";
    }

    private string PlanSort(QuerySort[]? sort)
    {
        if (sort is null || sort.Length == 0)
        {
            return " ORDER BY updated_at ASC, record_id ASC";
        }

        if (sort.Length > _options.MaxSortFields)
        {
            _unsupported.Add("sort.tooManyFields");
            return "";
        }

        var parts = new List<string>(sort.Length + 1);
        foreach (var item in sort)
        {
            if (item.Nulls != QueryNullOrder.Unspecified)
            {
                _unsupported.Add("sort.nullOrdering");
                return "";
            }

            var expression = item.Field switch
            {
                "id" => "record_id",
                "createdAt" => "created_at",
                "updatedAt" => "updated_at",
                "revision" => "revision",
                _ => FieldExpression(item.Field, forExistence: false)
            };

            if (expression is null)
            {
                _unsupported.Add("sort.field");
                return "";
            }

            parts.Add(expression + (item.Direction == QuerySortDirection.Desc ? " DESC" : " ASC"));
        }

        parts.Add("record_id ASC");
        return " ORDER BY " + string.Join(", ", parts);
    }

    private (string Sql, PageInfo PageInfo) PlanPage(QueryPage? page)
    {
        var limit = Math.Min(_options.DefaultPageSize, _options.MaxPageSize);
        var offset = 0;
        int? pageNumber = null;

        if (page is not null)
        {
            if (page.Mode == QueryPaginationMode.Cursor)
            {
                int cursorLimit = page.Limit ?? _options.DefaultPageSize;
                if (cursorLimit <= 0 || cursorLimit > _options.MaxPageSize)
                    _unsupported.Add("page.limit");
                cursorLimit = Math.Min(Math.Max(1, cursorLimit), _options.MaxPageSize);
                string cursorLimitParameter = AddParameter(cursorLimit + 1);
                return ($" LIMIT {cursorLimitParameter}", new PageInfo { Limit = cursorLimit, Cursor = page.Cursor });
            }

            var requested = page.PerPage ?? page.Limit ?? _options.DefaultPageSize;
            if (requested <= 0 || requested > _options.MaxPageSize)
            {
                _unsupported.Add("page.limit");
            }

            limit = Math.Min(Math.Max(1, requested), _options.MaxPageSize);
            if (page.Mode == QueryPaginationMode.Offset)
            {
                offset = Math.Max(0, page.Offset ?? 0);
            }
            else
            {
                pageNumber = Math.Max(1, page.Page ?? 1);
                offset = (pageNumber.Value - 1) * limit;
            }
        }

        var limitParam = AddParameter(limit + 1);
        var offsetParam = AddParameter(offset);
        return ($" LIMIT {limitParam} OFFSET {offsetParam}", new PageInfo { Page = pageNumber, PerPage = pageNumber is null ? null : limit, Offset = pageNumber is null ? offset : null, Limit = pageNumber is null ? limit : null });
    }

    private string? PlanCursor(QuerySort[]? sort, BaseQueryCursorPayload cursor)
    {
        if (sort is null || sort.Length == 0 || cursor.Keys.Length != sort.Length)
            return null;
        return CursorTerm(0);

        string? CursorTerm(int index)
        {
            if (index == sort.Length)
                return "record_id > " + AddParameter(cursor.RecordId);
            QuerySort item = sort[index];
            string? expression = item.Field switch
            {
                "id" => "record_id",
                "createdAt" => "created_at",
                "updatedAt" => "updated_at",
                "revision" => "revision",
                _ => FieldExpression(item.Field, forExistence: false)
            };
            if (expression is null || !TryCursorValue(item.Field, cursor.Keys[index], out object? value))
                return null;
            string equality = value is null
                ? expression + " IS NULL"
                : expression + " = " + AddParameter(value);
            string greater;
            if (value is null)
            {
                greater = item.Direction == QuerySortDirection.Asc
                    ? expression + " IS NOT NULL"
                    : "0 = 1";
            }
            else
            {
                string parameter = AddParameter(value);
                greater = item.Direction == QuerySortDirection.Asc
                    ? expression + " > " + parameter
                    : "(" + expression + " < " + parameter + " OR " + expression + " IS NULL)";
            }
            string? next = CursorTerm(index + 1);
            return next is null ? null : "(" + greater + " OR (" + equality + " AND " + next + "))";
        }
    }

    private bool TryCursorValue(string field, BaseQueryCursorKey key, out object? value)
    {
        value = null;
        if (!key.Present || string.Equals(key.Json, "null", StringComparison.Ordinal)) return true;
        try
        {
            using JsonDocument document = JsonDocument.Parse(key.Json);
            JsonElement element = document.RootElement;
            value = field switch
            {
                "id" => element.GetString(),
                "createdAt" or "updatedAt" => element.GetDateTimeOffset().ToUniversalTime().ToString("O", CultureInfo.InvariantCulture),
                "revision" => element.GetInt64(),
                _ => FieldModel(field)?.Encode(element)
            };
            return value is not null;
        }
        catch (Exception exception) when (exception is JsonException or FormatException or InvalidOperationException or OverflowException)
        {
            return false;
        }
    }

    private string? FieldExpression(string? field, bool forExistence)
    {
        if (string.IsNullOrWhiteSpace(field))
        {
            return null;
        }

        return field switch
        {
            "id" => "record_id",
            "createdAt" => "created_at",
            "updatedAt" => "updated_at",
            "revision" => "revision",
            _ => FieldModel(field)?.Column
        };
    }

    private SqlitePhysicalModel.FieldModel? FieldModel(string? name) => string.IsNullOrWhiteSpace(name)
        ? null
        : _collection.Fields.SingleOrDefault(field => string.Equals(field.Definition.Name, name, StringComparison.Ordinal));

    private string AddParameter(object? value)
    {
        var name = "$p" + (++_parameterId).ToString(CultureInfo.InvariantCulture);
        _parameters.Add((name, value ?? DBNull.Value));
        return name;
    }

    private static object? QueryValueToSqlValue(QueryValue value) =>
        value.Kind switch
        {
            QueryValueKind.Null => null,
            QueryValueKind.String => value.String,
            QueryValueKind.Boolean => value.Boolean is true ? 1 : 0,
            QueryValueKind.Integer => value.Integer,
            QueryValueKind.Number => value.Number,
            QueryValueKind.Decimal => value.Decimal,
            QueryValueKind.DateTime => value.DateTime?.ToString("O"),
            QueryValueKind.Id => value.Id,
            _ => null
        };

    private sealed class NodeCounter
    {
        /// <summary>Provides the value value.</summary>
        public int Value;
    }
}

internal sealed record SqliteQueryPlan(
    bool Supported,
    string[] UnsupportedParts,
    string SelectSql,
    string CountSql,
    (string Name, object? Value)[] Parameters,
    PageInfo PageInfo)
{
    /// <summary>Executes the bind operation.</summary>
    public void Bind(SqliteCommand command)
    {
        foreach (var parameter in Parameters)
        {
            command.Parameters.AddWithValue(parameter.Name, parameter.Value ?? DBNull.Value);
        }
    }
}
