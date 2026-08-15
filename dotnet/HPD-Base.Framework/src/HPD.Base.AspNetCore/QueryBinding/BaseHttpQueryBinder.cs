using System.Text.Json;
using HPD.Base;
using HPD.Base.AspNetCore;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Primitives;

namespace HPD.Base.AspNetCore;

internal sealed class BaseHttpQueryBinder : IBaseHttpQueryBinder
{
    private readonly HPDBaseAspNetCoreSnapshot _options;

    /// <summary>Initializes a new instance.</summary>
    public BaseHttpQueryBinder(HPDBaseAspNetCoreSnapshot options)
    {
        _options = options;
    }

    private static readonly HashSet<string> AllowedManifestExpand = new(StringComparer.Ordinal)
    {
        "schema",
        "capabilities",
        "health",
        "diagnostics",
        "collections"
    };

    /// <summary>Executes the bind list query async operation.</summary>
    public ValueTask<OperationResult<RecordQuery>> BindListQueryAsync(
        HttpContext httpContext,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(httpContext);

        var query = httpContext.Request.Query;
        var limitValidation = ValidateTransport(httpContext);
        if (!limitValidation.Succeeded)
            return ValueTask.FromResult(Validation<RecordQuery>(limitValidation.ErrorCode!, limitValidation.ErrorMessage!, limitValidation.Target));

        if (query.ContainsKey("filter") && query.Keys.Any(static key => key.StartsWith("where[", StringComparison.Ordinal)))
            return ValueTask.FromResult(Validation<RecordQuery>("base.http.query.mixedFilter", "filter and where[...] cannot be combined.", "query"));

        FilterExpression? filter = null;
        if (query.TryGetValue("filter", out var filterValues) && !StringValues.IsNullOrEmpty(filterValues))
        {
            try
            {
                if (filterValues.ToString().Length > _options.Limits.MaxFilterLength)
                    return ValueTask.FromResult(Validation<RecordQuery>("base.http.query.filterTooLong", "filter exceeds the configured maximum length.", "filter"));

                filter = JsonSerializer.Deserialize(filterValues.ToString(), HPDBaseJsonSerializerContext.Default.FilterExpression);
            }
            catch (JsonException ex)
            {
                return ValueTask.FromResult(Validation<RecordQuery>("base.http.query.invalidFilter", ex.Message, "filter"));
            }
        }
        else
        {
            var where = BindWhere(query, _options.Limits);
            if (!where.Succeeded)
                return ValueTask.FromResult(Validation<RecordQuery>(where.ErrorCode!, where.ErrorMessage!, where.Target));
            filter = where.Filter;
        }

        var sort = BindSort(query, _options.Limits, out var sortValidation);
        if (!sortValidation.Succeeded)
            return ValueTask.FromResult(Validation<RecordQuery>(sortValidation.ErrorCode!, sortValidation.ErrorMessage!, sortValidation.Target));

        var select = SplitComma(query, "select", _options.Limits, out var selectValidation);
        if (!selectValidation.Succeeded)
            return ValueTask.FromResult(Validation<RecordQuery>(selectValidation.ErrorCode!, selectValidation.ErrorMessage!, selectValidation.Target));

        var include = SplitComma(query, "include", _options.Limits, out var includeValidation);
        if (!includeValidation.Succeeded)
            return ValueTask.FromResult(Validation<RecordQuery>(includeValidation.ErrorCode!, includeValidation.ErrorMessage!, includeValidation.Target));

        var recordQuery = new RecordQuery
        {
            Filter = filter,
            Sort = sort,
            Page = BindPage(query),
            Select = select,
            Include = include?.Select(static navigationId => new RecordInclude { NavigationId = navigationId }).ToArray(),
            Count = BindCount(query, out var countValidation),
            Extensions = BindExtensions(query, _options.Limits, out var extensionValidation)
        };

        if (!extensionValidation.Succeeded)
            return ValueTask.FromResult(Validation<RecordQuery>(extensionValidation.ErrorCode!, extensionValidation.ErrorMessage!, extensionValidation.Target));

        if (!countValidation.Succeeded)
            return ValueTask.FromResult(Validation<RecordQuery>(countValidation.ErrorCode!, countValidation.ErrorMessage!, countValidation.Target));

        return ValueTask.FromResult(new OperationResult<RecordQuery> { Status = OperationStatus.Ok, Value = recordQuery });
    }

    private BaseHttpQueryParseResult ValidateTransport(HttpContext httpContext)
    {
        var limits = _options.Limits;
        if (httpContext.Request.QueryString.Value is { Length: > 0 } queryString
            && queryString.Length > limits.MaxQueryStringLength)
            return new BaseHttpQueryParseResult(false, "base.http.query.tooLong", "Query string exceeds the configured maximum length.", "query");

        foreach (var (key, values) in httpContext.Request.Query)
        {
            if (values.Count > limits.MaxRepeatedParameterValues)
                return new BaseHttpQueryParseResult(false, "base.http.query.tooManyValues", $"Query parameter '{key}' has too many values.", key);
        }

        return new BaseHttpQueryParseResult(true);
    }

    /// <summary>Executes the bind manifest expand operation.</summary>
    public OperationResult<string[]> BindManifestExpand(HttpContext httpContext)
    {
        ArgumentNullException.ThrowIfNull(httpContext);

        var values = httpContext.Request.Query["expand"];
        if (StringValues.IsNullOrEmpty(values))
            return new OperationResult<string[]> { Status = OperationStatus.Ok, Value = [] };

        var tokens = values
            .SelectMany(static value => value?.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries) ?? [])
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        var unknown = tokens.FirstOrDefault(token => !AllowedManifestExpand.Contains(token));
        if (unknown is not null)
            return Validation<string[]>("base.http.manifest.unknownExpand", $"Unknown manifest expansion token '{unknown}'.", "expand");

        return new OperationResult<string[]> { Status = OperationStatus.Ok, Value = tokens };
    }

    private static (bool Succeeded, FilterExpression? Filter, string? ErrorCode, string? ErrorMessage, string? Target) BindWhere(IQueryCollection query, HPDBaseHttpLimitOptions limits)
    {
        var filters = new List<FilterExpression>();

        foreach (var (key, values) in query)
        {
            if (!key.StartsWith("where[", StringComparison.Ordinal))
                continue;

            var parsed = ParseWhereKey(key);
            if (!parsed.Succeeded)
                return (false, null, parsed.ErrorCode, parsed.ErrorMessage, parsed.Target);
            if (!IsKnownWhereModifier(parsed.Modifier))
                return (false, null, "base.http.query.unknownOperator", $"Unknown where operator '{parsed.Modifier}'.", key);

            var field = parsed.Field!;
            var modifier = parsed.Modifier;
            if (modifier is "in")
            {
                var inValues = SplitValues(values).ToArray();
                if (inValues.Length > limits.MaxQueryListItems)
                    return (false, null, "base.http.query.tooManyListItems", $"Query parameter '{key}' has too many list items.", key);

                filters.Add(new FilterExpression
                {
                    Kind = FilterNodeKind.In,
                    Field = field,
                    Values = inValues.Select(InferQueryValue).ToArray()
                });
                continue;
            }

            if (modifier is "isNull" or "isDefined")
            {
                if (BindBoolean(values) != true)
                    continue;

                filters.Add(new FilterExpression
                {
                    Kind = modifier == "isNull" ? FilterNodeKind.IsNull : FilterNodeKind.IsDefined,
                    Field = field,
                });
                continue;
            }

            filters.Add(new FilterExpression
            {
                Kind = FilterNodeKind.Compare,
                Field = field,
                Operator = BindOperator(modifier),
                Value = InferQueryValue(values.ToString())
            });
        }

        return filters.Count switch
        {
            0 => (true, null, null, null, null),
            1 => (true, filters[0], null, null, null),
            _ => (true, new FilterExpression { Kind = FilterNodeKind.And, Children = filters.ToArray() }, null, null, null)
        };
    }

    private static (bool Succeeded, string? Field, string? Modifier, string? ErrorCode, string? ErrorMessage, string? Target) ParseWhereKey(string key)
    {
        var firstClose = key.IndexOf(']', StringComparison.Ordinal);
        if (firstClose <= "where[".Length)
            return (false, null, null, "base.http.query.invalidWhere", $"Invalid where key '{key}'.", key);

        var field = key["where[".Length..firstClose];
        if (string.IsNullOrWhiteSpace(field))
            return (false, null, null, "base.http.query.invalidWhere", "where field cannot be empty.", key);

        if (firstClose == key.Length - 1)
            return (true, field, null, null, null, null);

        if (firstClose + 1 >= key.Length || key[firstClose + 1] != '[' || key[^1] != ']')
            return (false, null, null, "base.http.query.invalidWhere", $"Invalid where key '{key}'.", key);

        return (true, field, key[(firstClose + 2)..^1], null, null, null);
    }

    private static QuerySort[]? BindSort(IQueryCollection query, HPDBaseHttpLimitOptions limits, out BaseHttpQueryParseResult validation)
    {
        var values = SplitComma(query, "sort", limits, out validation);
        if (values is null)
            return null;

        return values.Select(value =>
        {
            var descending = value.StartsWith("-", StringComparison.Ordinal);
            var field = descending ? value[1..] : value;
            var nulls = BindNulls(query, field);
            return new QuerySort(field, descending ? QuerySortDirection.Desc : QuerySortDirection.Asc, nulls);
        }).ToArray();
    }

    private static QueryNullOrder BindNulls(IQueryCollection query, string field)
    {
        var key = $"nulls[{field}]";
        if (!query.TryGetValue(key, out var values))
            return QueryNullOrder.Unspecified;

        return values.ToString() switch
        {
            "first" => QueryNullOrder.First,
            "last" => QueryNullOrder.Last,
            _ => QueryNullOrder.Unspecified
        };
    }

    private static QueryPage? BindPage(IQueryCollection query)
    {
        if (query.TryGetValue("cursor", out var cursor))
        {
            return new QueryPage
            {
                Mode = QueryPaginationMode.Cursor,
                Cursor = cursor.ToString(),
                CursorDirection = query["cursorDir"].ToString() == "before" ? QueryCursorDirection.Before : QueryCursorDirection.After,
                Limit = BindInt(query, "limit")
            };
        }

        if (query.ContainsKey("offset") || query.ContainsKey("limit"))
        {
            return new QueryPage
            {
                Mode = QueryPaginationMode.Offset,
                Offset = BindInt(query, "offset"),
                Limit = BindInt(query, "limit")
            };
        }

        if (query.ContainsKey("page") || query.ContainsKey("perPage"))
        {
            return new QueryPage
            {
                Mode = QueryPaginationMode.Page,
                Page = BindInt(query, "page"),
                PerPage = BindInt(query, "perPage")
            };
        }

        return null;
    }

    private static QueryCountMode BindCount(IQueryCollection query, out BaseHttpQueryParseResult validation)
    {
        var value = query["count"].ToString();
        validation = new BaseHttpQueryParseResult(true);
        var mode = value switch
        {
            "" => QueryCountMode.IfAvailable,
            "none" => QueryCountMode.None,
            "ifAvailable" => QueryCountMode.IfAvailable,
            "exact" => QueryCountMode.Exact,
            "estimated" => QueryCountMode.Estimated,
            "limited" => QueryCountMode.Limited,
            _ => QueryCountMode.IfAvailable
        };

        if (!string.IsNullOrEmpty(value)
            && mode == QueryCountMode.IfAvailable
            && !string.Equals(value, "ifAvailable", StringComparison.Ordinal))
        {
            validation = new BaseHttpQueryParseResult(false, "base.http.query.invalidCount", $"Invalid count mode '{value}'.", "count");
        }

        return mode;
    }

    private static QueryExtension[]? BindExtensions(IQueryCollection query, HPDBaseHttpLimitOptions limits, out BaseHttpQueryParseResult validation)
    {
        validation = new BaseHttpQueryParseResult(true);
        var extensions = query
            .Where(static pair => pair.Key.StartsWith("ext[", StringComparison.Ordinal) && pair.Key.EndsWith("]", StringComparison.Ordinal))
            .Select(pair =>
            {
                var name = pair.Key[4..^1];
                var separator = name.LastIndexOf('.');
                var moduleId = separator > 0 ? name[..separator] : name;
                var argumentName = separator > 0 ? name[(separator + 1)..] : "value";
                return new QueryExtension
                {
                    ModuleId = moduleId,
                    Name = argumentName,
                    Arguments = SplitValues(pair.Value).Select(InferQueryValue).ToArray()
                };
            })
            .ToArray();

        var tooLarge = extensions.FirstOrDefault(extension => extension.Arguments is { Length: var length } && length > limits.MaxQueryListItems);
        if (tooLarge is not null)
            validation = new BaseHttpQueryParseResult(false, "base.http.query.tooManyListItems", $"Extension '{tooLarge.ModuleId}.{tooLarge.Name}' has too many list items.", $"ext[{tooLarge.ModuleId}.{tooLarge.Name}]");

        return extensions.Length == 0 ? null : extensions;
    }

    private static string[]? SplitComma(IQueryCollection query, string key, HPDBaseHttpLimitOptions limits, out BaseHttpQueryParseResult validation)
    {
        validation = new BaseHttpQueryParseResult(true);
        if (!query.TryGetValue(key, out var values) || StringValues.IsNullOrEmpty(values))
            return null;

        var split = values
            .SelectMany(static value => value?.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries) ?? [])
            .ToArray();
        if (split.Length > limits.MaxQueryListItems)
            validation = new BaseHttpQueryParseResult(false, "base.http.query.tooManyListItems", $"Query parameter '{key}' has too many list items.", key);

        return split.Length == 0 ? null : split;
    }

    private static IEnumerable<string> SplitValues(StringValues values) =>
        values.SelectMany(static value => value?.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries) ?? []);

    private static int? BindInt(IQueryCollection query, string key) =>
        int.TryParse(query[key].ToString(), System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out var value)
            ? value
            : null;

    private static bool? BindBoolean(IQueryCollection query, string key) => BindBoolean(query[key]);

    private static bool? BindBoolean(StringValues values) =>
        bool.TryParse(values.ToString(), out var value) ? value : null;

    private static FilterOperator BindOperator(string? modifier) =>
        modifier switch
        {
            null or "" or "eq" => FilterOperator.Equal,
            "neq" => FilterOperator.NotEqual,
            "lt" => FilterOperator.LessThan,
            "lte" => FilterOperator.LessThanOrEqual,
            "gt" => FilterOperator.GreaterThan,
            "gte" => FilterOperator.GreaterThanOrEqual,
            "contains" => FilterOperator.Contains,
            "notContains" => FilterOperator.NotContains,
            "startsWith" => FilterOperator.StartsWith,
            "endsWith" => FilterOperator.EndsWith,
            "like" => FilterOperator.Like,
            "notLike" => FilterOperator.NotLike,
            _ => FilterOperator.Equal
        };

    private static bool IsKnownWhereModifier(string? modifier) =>
        modifier is null or "" or "eq" or "neq" or "lt" or "lte" or "gt" or "gte" or "contains" or "notContains" or "startsWith" or "endsWith" or "like" or "notLike" or "in" or "isNull" or "isDefined";

    private static QueryValue InferQueryValue(string value)
    {
        if (bool.TryParse(value, out var boolean))
            return new QueryValue { Kind = QueryValueKind.Boolean, Boolean = boolean };
        if (long.TryParse(value, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out var integer))
            return new QueryValue { Kind = QueryValueKind.Integer, Integer = integer };
        return new QueryValue { Kind = QueryValueKind.String, String = value };
    }

    private static OperationResult<T> Validation<T>(string code, string message, string? target) =>
        new()
        {
            Status = OperationStatus.ValidationFailed,
            Error = new BaseError
            {
                Code = code,
                Message = message,
                Target = target,
                Category = ErrorCategory.Validation
            }
        };
}
