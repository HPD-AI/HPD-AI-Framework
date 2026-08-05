using System.Globalization;
using System.Text.Json;

namespace HPD.Base;

internal static class BasePolicyWriteConstraintEvaluator
{
    /// <summary>Executes the evaluate operation.</summary>
    public static BasePolicyWriteCheckEvaluation Evaluate(
        RecordPayload? payload,
        FilterExpression filter)
    {
        if (payload is null)
        {
            return BasePolicyWriteCheckEvaluation.Unsupported;
        }

        return EvaluateNode(payload, filter);
    }

    private static BasePolicyWriteCheckEvaluation EvaluateNode(
        RecordPayload payload,
        FilterExpression filter) =>
        filter.Kind switch
        {
            FilterNodeKind.True => BasePolicyWriteCheckEvaluation.Allowed,
            FilterNodeKind.False => BasePolicyWriteCheckEvaluation.Denied,
            FilterNodeKind.Not => EvaluateNot(payload, filter),
            FilterNodeKind.And => EvaluateAnd(payload, filter),
            FilterNodeKind.Or => EvaluateOr(payload, filter),
            FilterNodeKind.Compare => EvaluateCompare(payload, filter),
            FilterNodeKind.In => EvaluateIn(payload, filter),
            FilterNodeKind.Between => EvaluateBetween(payload, filter),
            FilterNodeKind.IsNull => TryReadField(payload, filter.Field, out var value) && value.ValueKind == JsonValueKind.Null
                ? BasePolicyWriteCheckEvaluation.Allowed
                : BasePolicyWriteCheckEvaluation.Denied,
            FilterNodeKind.IsDefined => TryReadField(payload, filter.Field, out _)
                ? BasePolicyWriteCheckEvaluation.Allowed
                : BasePolicyWriteCheckEvaluation.Denied,
            _ => BasePolicyWriteCheckEvaluation.Unsupported
        };

    private static BasePolicyWriteCheckEvaluation EvaluateNot(RecordPayload payload, FilterExpression filter)
    {
        if (filter.Children is not [{ } child])
        {
            return BasePolicyWriteCheckEvaluation.Unsupported;
        }

        return EvaluateNode(payload, child) switch
        {
            BasePolicyWriteCheckEvaluation.Allowed => BasePolicyWriteCheckEvaluation.Denied,
            BasePolicyWriteCheckEvaluation.Denied => BasePolicyWriteCheckEvaluation.Allowed,
            _ => BasePolicyWriteCheckEvaluation.Unsupported
        };
    }

    private static BasePolicyWriteCheckEvaluation EvaluateAnd(RecordPayload payload, FilterExpression filter)
    {
        if (filter.Children is not { Length: > 0 })
        {
            return BasePolicyWriteCheckEvaluation.Unsupported;
        }

        var sawUnsupported = false;
        foreach (var child in filter.Children)
        {
            var result = EvaluateNode(payload, child);
            if (result == BasePolicyWriteCheckEvaluation.Denied)
            {
                return BasePolicyWriteCheckEvaluation.Denied;
            }

            sawUnsupported |= result == BasePolicyWriteCheckEvaluation.Unsupported;
        }

        return sawUnsupported ? BasePolicyWriteCheckEvaluation.Unsupported : BasePolicyWriteCheckEvaluation.Allowed;
    }

    private static BasePolicyWriteCheckEvaluation EvaluateOr(RecordPayload payload, FilterExpression filter)
    {
        if (filter.Children is not { Length: > 0 })
        {
            return BasePolicyWriteCheckEvaluation.Unsupported;
        }

        var sawUnsupported = false;
        foreach (var child in filter.Children)
        {
            var result = EvaluateNode(payload, child);
            if (result == BasePolicyWriteCheckEvaluation.Allowed)
            {
                return BasePolicyWriteCheckEvaluation.Allowed;
            }

            sawUnsupported |= result == BasePolicyWriteCheckEvaluation.Unsupported;
        }

        return sawUnsupported ? BasePolicyWriteCheckEvaluation.Unsupported : BasePolicyWriteCheckEvaluation.Denied;
    }

    private static BasePolicyWriteCheckEvaluation EvaluateCompare(RecordPayload payload, FilterExpression filter)
    {
        if (!TryReadField(payload, filter.Field, out var fieldValue) || filter.Value is null)
        {
            return BasePolicyWriteCheckEvaluation.Denied;
        }

        bool? allowed = filter.Operator switch
        {
            FilterOperator.Equal => ValueEquals(fieldValue, filter.Value),
            FilterOperator.NotEqual => !ValueEquals(fieldValue, filter.Value),
            FilterOperator.LessThan => CompareValues(fieldValue, filter.Value) is < 0,
            FilterOperator.LessThanOrEqual => CompareValues(fieldValue, filter.Value) is <= 0,
            FilterOperator.GreaterThan => CompareValues(fieldValue, filter.Value) is > 0,
            FilterOperator.GreaterThanOrEqual => CompareValues(fieldValue, filter.Value) is >= 0,
            FilterOperator.Contains => ContainsValue(fieldValue, filter.Value),
            FilterOperator.NotContains => !ContainsValue(fieldValue, filter.Value),
            FilterOperator.StartsWith => fieldValue.ValueKind == JsonValueKind.String
                && filter.Value.String is { } prefix
                && (fieldValue.GetString() ?? string.Empty).StartsWith(prefix, StringComparison.Ordinal),
            FilterOperator.EndsWith => fieldValue.ValueKind == JsonValueKind.String
                && filter.Value.String is { } suffix
                && (fieldValue.GetString() ?? string.Empty).EndsWith(suffix, StringComparison.Ordinal),
            FilterOperator.Like or FilterOperator.NotLike => null,
            _ => null
        };

        return allowed switch
        {
            true => BasePolicyWriteCheckEvaluation.Allowed,
            false => BasePolicyWriteCheckEvaluation.Denied,
            _ => BasePolicyWriteCheckEvaluation.Unsupported
        };
    }

    private static BasePolicyWriteCheckEvaluation EvaluateIn(RecordPayload payload, FilterExpression filter)
    {
        if (!TryReadField(payload, filter.Field, out var fieldValue) || filter.Values is null)
        {
            return BasePolicyWriteCheckEvaluation.Denied;
        }

        var allowed = fieldValue.ValueKind == JsonValueKind.Array
            ? fieldValue.EnumerateArray().Any(item => filter.Values.Any(queryValue => ValueEquals(item, queryValue)))
            : filter.Values.Any(queryValue => ValueEquals(fieldValue, queryValue));

        return allowed ? BasePolicyWriteCheckEvaluation.Allowed : BasePolicyWriteCheckEvaluation.Denied;
    }

    private static BasePolicyWriteCheckEvaluation EvaluateBetween(RecordPayload payload, FilterExpression filter)
    {
        if (!TryReadField(payload, filter.Field, out var fieldValue)
            || filter.Values is not [{ } lower, { } upper])
        {
            return BasePolicyWriteCheckEvaluation.Denied;
        }

        var lowerComparison = CompareValues(fieldValue, lower);
        var upperComparison = CompareValues(fieldValue, upper);
        if (lowerComparison is null || upperComparison is null)
        {
            return BasePolicyWriteCheckEvaluation.Unsupported;
        }

        return lowerComparison >= 0 && upperComparison <= 0
            ? BasePolicyWriteCheckEvaluation.Allowed
            : BasePolicyWriteCheckEvaluation.Denied;
    }

    private static bool TryReadField(RecordPayload payload, string? fieldPath, out JsonElement value)
    {
        value = default;
        if (string.IsNullOrWhiteSpace(fieldPath))
        {
            return false;
        }

        var parts = fieldPath.Split('.', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0)
        {
            return false;
        }

        if (payload.Kind == RecordPayloadKind.FieldMap)
        {
            if (payload.Fields?.TryGetValue(parts[0], out value) != true)
            {
                return false;
            }
        }
        else
        {
            value = payload.Json;
            if (value.ValueKind != JsonValueKind.Object || !value.TryGetProperty(parts[0], out value))
            {
                return false;
            }
        }

        for (var index = 1; index < parts.Length; index++)
        {
            if (value.ValueKind != JsonValueKind.Object || !value.TryGetProperty(parts[index], out value))
            {
                return false;
            }
        }

        return true;
    }

    private static bool ValueEquals(JsonElement fieldValue, QueryValue queryValue)
    {
        if (queryValue.Kind == QueryValueKind.Null)
        {
            return fieldValue.ValueKind == JsonValueKind.Null;
        }

        if (TryDecimal(fieldValue, out var fieldDecimal) && TryDecimal(queryValue, out var queryDecimal))
        {
            return fieldDecimal == queryDecimal;
        }

        if (queryValue.Kind == QueryValueKind.Boolean && fieldValue.ValueKind is JsonValueKind.True or JsonValueKind.False)
        {
            return fieldValue.GetBoolean() == queryValue.Boolean;
        }

        var fieldString = ScalarString(fieldValue);
        var queryString = ScalarString(queryValue);
        return fieldString is not null
            && queryString is not null
            && string.Equals(fieldString, queryString, StringComparison.Ordinal);
    }

    private static int? CompareValues(JsonElement fieldValue, QueryValue queryValue)
    {
        if (TryDecimal(fieldValue, out var fieldDecimal) && TryDecimal(queryValue, out var queryDecimal))
        {
            return fieldDecimal.CompareTo(queryDecimal);
        }

        if (queryValue.DateTime is { } queryDate
            && fieldValue.ValueKind == JsonValueKind.String
            && DateTimeOffset.TryParse(fieldValue.GetString(), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var fieldDate))
        {
            return fieldDate.ToUniversalTime().CompareTo(queryDate.ToUniversalTime());
        }

        var fieldString = ScalarString(fieldValue);
        var queryString = ScalarString(queryValue);
        return fieldString is null || queryString is null
            ? null
            : string.Compare(fieldString, queryString, StringComparison.Ordinal);
    }

    private static bool ContainsValue(JsonElement fieldValue, QueryValue queryValue)
    {
        if (fieldValue.ValueKind == JsonValueKind.Array)
        {
            return fieldValue.EnumerateArray().Any(item => ValueEquals(item, queryValue));
        }

        return fieldValue.ValueKind == JsonValueKind.String
            && queryValue.String is { } text
            && (fieldValue.GetString() ?? string.Empty).Contains(text, StringComparison.Ordinal);
    }

    private static bool TryDecimal(JsonElement value, out decimal result)
    {
        if (value.ValueKind == JsonValueKind.Number && value.TryGetDecimal(out result))
        {
            return true;
        }

        if (value.ValueKind == JsonValueKind.String && decimal.TryParse(value.GetString(), NumberStyles.Number, CultureInfo.InvariantCulture, out result))
        {
            return true;
        }

        result = default;
        return false;
    }

    private static bool TryDecimal(QueryValue value, out decimal result)
    {
        result = value.Kind switch
        {
            QueryValueKind.Integer when value.Integer is { } integer => integer,
            QueryValueKind.Number when value.Number is { } number => (decimal)number,
            QueryValueKind.Decimal when decimal.TryParse(value.Decimal, NumberStyles.Number, CultureInfo.InvariantCulture, out var parsed) => parsed,
            _ => default
        };

        return value.Kind switch
        {
            QueryValueKind.Integer => value.Integer is not null,
            QueryValueKind.Number => value.Number is not null,
            QueryValueKind.Decimal => decimal.TryParse(value.Decimal, NumberStyles.Number, CultureInfo.InvariantCulture, out _),
            _ => false
        };
    }

    private static string? ScalarString(JsonElement value) =>
        value.ValueKind switch
        {
            JsonValueKind.String => value.GetString(),
            JsonValueKind.Number => value.GetRawText(),
            JsonValueKind.True => bool.TrueString,
            JsonValueKind.False => bool.FalseString,
            JsonValueKind.Null => null,
            _ => null
        };

    private static string? ScalarString(QueryValue value) =>
        value.Kind switch
        {
            QueryValueKind.String => value.String,
            QueryValueKind.Id => value.Id,
            QueryValueKind.Integer => value.Integer?.ToString(CultureInfo.InvariantCulture),
            QueryValueKind.Number => value.Number?.ToString(CultureInfo.InvariantCulture),
            QueryValueKind.Decimal => value.Decimal,
            QueryValueKind.Boolean => value.Boolean?.ToString(),
            QueryValueKind.DateTime => value.DateTime?.ToString("O", CultureInfo.InvariantCulture),
            _ => null
        };
}

internal enum BasePolicyWriteCheckEvaluation
{
Allowed,
Denied,
Unsupported
}
