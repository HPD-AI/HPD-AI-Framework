using System.Text.Json;
using System.Globalization;

namespace HPD.Agent;

/// <summary>
/// Provides exact, reflection-free JSON primitives used by generated AI-function binders.
/// </summary>
public static class HPDGeneratedToolArgumentBinder
{
    /// <summary>Requires an object at the supplied contract path.</summary>
    public static void RequireObject(JsonElement value, string path)
    {
        if (value.ValueKind is not JsonValueKind.Object)
            Throw(path, "invalid_json_kind", "Expected an object.");
    }

    /// <summary>Requires an array at the supplied contract path.</summary>
    public static void RequireArray(JsonElement value, string path)
    {
        if (value.ValueKind is not JsonValueKind.Array)
            Throw(path, "invalid_json_kind", "Expected an array.");
    }

    /// <summary>Gets one required property using exact ordinal name matching.</summary>
    public static JsonElement GetRequiredProperty(JsonElement value, string name, string path)
    {
        RequireObject(value, path);
        var found = false;
        var result = default(JsonElement);
        foreach (var property in value.EnumerateObject())
        {
            if (!string.Equals(property.Name, name, StringComparison.Ordinal))
                continue;
            if (found)
                Throw(Append(path, name), "duplicate_property", $"Property '{name}' occurs more than once.");
            found = true;
            result = property.Value;
        }

        if (!found)
            Throw(Append(path, name), "missing_required_property", $"Required property '{name}' is missing.");
        return result;
    }

    /// <summary>Attempts to get one optional property using exact ordinal name matching.</summary>
    public static bool TryGetOptionalProperty(JsonElement value, string name, string path, out JsonElement result)
    {
        RequireObject(value, path);
        var found = false;
        result = default;
        foreach (var property in value.EnumerateObject())
        {
            if (!string.Equals(property.Name, name, StringComparison.Ordinal))
                continue;
            if (found)
                Throw(Append(path, name), "duplicate_property", $"Property '{name}' occurs more than once.");
            found = true;
            result = property.Value;
        }
        return found;
    }

    /// <summary>Rejects duplicate and unknown properties using exact ordinal names.</summary>
    public static void ValidateProperties(JsonElement value, string path, params string[] expectedNames)
    {
        RequireObject(value, path);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var property in value.EnumerateObject())
        {
            if (!seen.Add(property.Name))
                Throw(Append(path, property.Name), "duplicate_property", $"Property '{property.Name}' occurs more than once.");
            if (!expectedNames.Contains(property.Name, StringComparer.Ordinal))
                Throw(Append(path, property.Name), "unknown_property", $"Property '{property.Name}' is not allowed.");
        }
    }

    /// <summary>Binds a JSON string.</summary>
    public static string BindString(JsonElement value, string path) =>
        value.ValueKind is JsonValueKind.String
            ? value.GetString()!
            : throw Error(path, "invalid_json_kind", "Expected a string.");

    /// <summary>Binds a single-character JSON string.</summary>
    public static char BindChar(JsonElement value, string path)
    {
        var text = BindString(value, path);
        if (text.Length != 1)
            Throw(path, "invalid_string_value", "Expected a string containing exactly one character.");
        return text[0];
    }

    /// <summary>Binds a JSON boolean.</summary>
    public static bool BindBoolean(JsonElement value, string path) =>
        value.ValueKind is JsonValueKind.True or JsonValueKind.False
            ? value.GetBoolean()
            : throw Error(path, "invalid_json_kind", "Expected a boolean.");

    /// <summary>Binds a signed byte.</summary>
    public static sbyte BindSByte(JsonElement value, string path) => ReadNumber(value, path, static element => element.GetSByte());
    /// <summary>Binds an unsigned byte.</summary>
    public static byte BindByte(JsonElement value, string path) => ReadNumber(value, path, static element => element.GetByte());
    /// <summary>Binds a 16-bit signed integer.</summary>
    public static short BindInt16(JsonElement value, string path) => ReadNumber(value, path, static element => element.GetInt16());
    /// <summary>Binds a 16-bit unsigned integer.</summary>
    public static ushort BindUInt16(JsonElement value, string path) => ReadNumber(value, path, static element => element.GetUInt16());
    /// <summary>Binds a 32-bit signed integer.</summary>
    public static int BindInt32(JsonElement value, string path) => ReadNumber(value, path, static element => element.GetInt32());
    /// <summary>Binds a 32-bit unsigned integer.</summary>
    public static uint BindUInt32(JsonElement value, string path) => ReadNumber(value, path, static element => element.GetUInt32());
    /// <summary>Binds a 64-bit signed integer.</summary>
    public static long BindInt64(JsonElement value, string path) => ReadNumber(value, path, static element => element.GetInt64());
    /// <summary>Binds a 64-bit unsigned integer.</summary>
    public static ulong BindUInt64(JsonElement value, string path) => ReadNumber(value, path, static element => element.GetUInt64());
    /// <summary>Binds a single-precision number.</summary>
    public static float BindSingle(JsonElement value, string path) => ReadNumber(value, path, static element => element.GetSingle());
    /// <summary>Binds a double-precision number.</summary>
    public static double BindDouble(JsonElement value, string path) => ReadNumber(value, path, static element => element.GetDouble());
    /// <summary>Binds a decimal number.</summary>
    public static decimal BindDecimal(JsonElement value, string path) => ReadNumber(value, path, static element => element.GetDecimal());

    /// <summary>Binds a GUID string.</summary>
    public static Guid BindGuid(JsonElement value, string path) =>
        Guid.TryParse(BindString(value, path), out var result)
            ? result
            : throw Error(path, "invalid_string_value", "Expected a UUID string.");

    /// <summary>Binds an RFC 3339 date-time string.</summary>
    public static DateTime BindDateTime(JsonElement value, string path)
    {
        if (value.ValueKind is JsonValueKind.String && value.TryGetDateTime(out var result)) return result;
        throw Error(path, "invalid_string_value", "Expected a date-time string.");
    }

    /// <summary>Binds an RFC 3339 date-time-offset string.</summary>
    public static DateTimeOffset BindDateTimeOffset(JsonElement value, string path)
    {
        if (value.ValueKind is JsonValueKind.String && value.TryGetDateTimeOffset(out var result)) return result;
        throw Error(path, "invalid_string_value", "Expected a date-time string.");
    }

    /// <summary>Binds an ISO calendar-date string.</summary>
    public static DateOnly BindDateOnly(JsonElement value, string path) =>
        DateOnly.TryParseExact(BindString(value, path), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var result)
            ? result
            : throw Error(path, "invalid_string_value", "Expected a date string in yyyy-MM-dd format.");

    /// <summary>Binds an invariant time-of-day string.</summary>
    public static TimeOnly BindTimeOnly(JsonElement value, string path) =>
        TimeOnly.TryParse(BindString(value, path), CultureInfo.InvariantCulture, DateTimeStyles.None, out var result)
            ? result
            : throw Error(path, "invalid_string_value", "Expected a time string.");

    /// <summary>Binds an invariant duration string.</summary>
    public static TimeSpan BindTimeSpan(JsonElement value, string path) =>
        TimeSpan.TryParse(BindString(value, path), CultureInfo.InvariantCulture, out var result)
            ? result
            : throw Error(path, "invalid_string_value", "Expected a duration string.");

    /// <summary>Creates a stable child JSON path.</summary>
    public static string Append(string path, string property) =>
        string.IsNullOrEmpty(path) ? property : path + "." + property;

    /// <summary>Creates a stable indexed JSON path.</summary>
    public static string AppendIndex(string path, int index) => $"{path}[{index}]";

    /// <summary>Creates a generated binding exception.</summary>
    public static HPDToolArgumentException Error(string path, string errorCode, string message) =>
        new(path, message, errorCode);

    private static T ReadNumber<T>(JsonElement value, string path, Func<JsonElement, T> reader)
    {
        if (value.ValueKind is not JsonValueKind.Number)
            throw Error(path, "invalid_json_kind", "Expected a number.");
        try
        {
            return reader(value);
        }
        catch (FormatException exception)
        {
            throw new HPDToolArgumentException(path, "The number is outside the supported range.", "invalid_number", exception);
        }
    }

    private static void Throw(string path, string errorCode, string message) => throw Error(path, errorCode, message);
}
