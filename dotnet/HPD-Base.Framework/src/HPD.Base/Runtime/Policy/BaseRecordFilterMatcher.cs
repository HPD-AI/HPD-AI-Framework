using System.Globalization;
using System.Text.Json;

namespace HPD.Base;

internal static class BaseRecordFilterMatcher
{
    internal static bool Matches(RecordEnvelope record, FilterExpression? filter) => filter is null || Matches(record.Payload, filter);

    private static bool Matches(RecordPayload payload, FilterExpression filter) => filter.Kind switch
    {
        FilterNodeKind.True => true,
        FilterNodeKind.False => false,
        FilterNodeKind.Not => filter.Children is [{ } child] && !Matches(payload, child),
        FilterNodeKind.And => filter.Children is { Length: > 0 } all && all.All(child => Matches(payload, child)),
        FilterNodeKind.Or => filter.Children is { Length: > 0 } any && any.Any(child => Matches(payload, child)),
        FilterNodeKind.Compare => Read(payload, filter.Field, out JsonElement value) && filter.Value is { } query && Compare(value, query, filter.Operator),
        FilterNodeKind.In => Read(payload, filter.Field, out JsonElement value) && filter.Values is { } values && (value.ValueKind == JsonValueKind.Array ? value.EnumerateArray().Any(item => values.Any(query => Equal(item, query))) : values.Any(query => Equal(value, query))),
        FilterNodeKind.Between => Read(payload, filter.Field, out JsonElement value) && filter.Values is [{ } lower, { } upper] && Order(value, lower) is >= 0 && Order(value, upper) is <= 0,
        FilterNodeKind.IsNull => Read(payload, filter.Field, out JsonElement value) && value.ValueKind == JsonValueKind.Null,
        FilterNodeKind.IsDefined => Read(payload, filter.Field, out _),
        _ => false
    };

    private static bool Compare(JsonElement value, QueryValue query, FilterOperator operation) => operation switch
    {
        FilterOperator.Equal => Equal(value, query),
        FilterOperator.NotEqual => !Equal(value, query),
        FilterOperator.LessThan => Order(value, query) is < 0,
        FilterOperator.LessThanOrEqual => Order(value, query) is <= 0,
        FilterOperator.GreaterThan => Order(value, query) is > 0,
        FilterOperator.GreaterThanOrEqual => Order(value, query) is >= 0,
        FilterOperator.Contains => Contains(value, query),
        FilterOperator.NotContains => !Contains(value, query),
        FilterOperator.StartsWith => value.ValueKind == JsonValueKind.String && query.String is { } prefix && (value.GetString() ?? "").StartsWith(prefix, StringComparison.Ordinal),
        FilterOperator.EndsWith => value.ValueKind == JsonValueKind.String && query.String is { } suffix && (value.GetString() ?? "").EndsWith(suffix, StringComparison.Ordinal),
        _ => false
    };

    private static bool Read(RecordPayload payload, string? path, out JsonElement value)
    {
        value = default; string[] parts = path?.Split('.', StringSplitOptions.RemoveEmptyEntries) ?? [];
        if (parts.Length == 0) return false;
        if (payload.Kind == RecordPayloadKind.FieldMap)
        {
            if (payload.Fields?.TryGetValue(parts[0], out value) != true) return false;
        }
        else
        {
            value = payload.Json;
            if (value.ValueKind != JsonValueKind.Object || !value.TryGetProperty(parts[0], out value)) return false;
        }
        for (int index = 1; index < parts.Length; index++)
            if (value.ValueKind != JsonValueKind.Object || !value.TryGetProperty(parts[index], out value)) return false;
        return true;
    }

    private static bool Equal(JsonElement value, QueryValue query)
    {
        if (query.Kind == QueryValueKind.Null) return value.ValueKind == JsonValueKind.Null;
        if (Decimal(value, out decimal left) && Decimal(query, out decimal right)) return left == right;
        if (query.Kind == QueryValueKind.Boolean && value.ValueKind is JsonValueKind.True or JsonValueKind.False) return value.GetBoolean() == query.Boolean;
        return Scalar(value) is { } a && Scalar(query) is { } b && string.Equals(a, b, StringComparison.Ordinal);
    }

    private static int? Order(JsonElement value, QueryValue query)
    {
        if (Decimal(value, out decimal left) && Decimal(query, out decimal right)) return left.CompareTo(right);
        if (query.DateTime is { } target && value.ValueKind == JsonValueKind.String && DateTimeOffset.TryParse(value.GetString(), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out DateTimeOffset actual)) return actual.ToUniversalTime().CompareTo(target.ToUniversalTime());
        return Scalar(value) is { } a && Scalar(query) is { } b ? string.Compare(a, b, StringComparison.Ordinal) : null;
    }

    private static bool Contains(JsonElement value, QueryValue query) => value.ValueKind == JsonValueKind.Array
        ? value.EnumerateArray().Any(item => Equal(item, query))
        : value.ValueKind == JsonValueKind.String && query.String is { } text && (value.GetString() ?? "").Contains(text, StringComparison.Ordinal);

    private static bool Decimal(JsonElement value, out decimal number) { number = default; return value.ValueKind switch { JsonValueKind.Number => value.TryGetDecimal(out number), JsonValueKind.String => decimal.TryParse(value.GetString(), NumberStyles.Number, CultureInfo.InvariantCulture, out number), _ => false }; }
    private static bool Decimal(QueryValue value, out decimal number) { number = default; return value.Kind switch { QueryValueKind.Integer when value.Integer is { } integer => Assign(integer, out number), QueryValueKind.Number when value.Number is { } real && double.IsFinite(real) => Assign((decimal)real, out number), QueryValueKind.Decimal when value.Decimal is { } text => decimal.TryParse(text, NumberStyles.Number, CultureInfo.InvariantCulture, out number), _ => false }; }
    private static bool Assign(decimal input, out decimal output) { output = input; return true; }
    private static string? Scalar(JsonElement value) => value.ValueKind switch { JsonValueKind.String => value.GetString(), JsonValueKind.Number => value.GetRawText(), JsonValueKind.True => bool.TrueString, JsonValueKind.False => bool.FalseString, _ => null };
    private static string? Scalar(QueryValue value) => value.Kind switch { QueryValueKind.String => value.String, QueryValueKind.Id => value.Id, QueryValueKind.Integer => value.Integer?.ToString(CultureInfo.InvariantCulture), QueryValueKind.Number => value.Number?.ToString(CultureInfo.InvariantCulture), QueryValueKind.Decimal => value.Decimal, QueryValueKind.Boolean => value.Boolean?.ToString(), QueryValueKind.DateTime => value.DateTime?.ToString("O", CultureInfo.InvariantCulture), _ => null };
}
