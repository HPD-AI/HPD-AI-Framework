using System.Text.Json;
using System.Globalization;

namespace HPD.Base;

/// <summary>Applies graph-owned scalar constraints to canonical record values.</summary>
public static class BaseCanonicalRecordValidator
{
    /// <summary>Validates one present, non-null canonical field value.</summary>
    public static BaseError? Validate(FieldDefinition field, JsonElement value)
    {
        ArgumentNullException.ThrowIfNull(field);
        if (value.ValueKind == JsonValueKind.Null || field.ScalarConstraints is not { } constraints)
            return null;

        try
        {
            bool admitted = field.ScalarKind switch
            {
                BaseScalarKind.String => String(value, constraints),
                BaseScalarKind.RecordId => RecordIdentifier(value, constraints),
                BaseScalarKind.ModuleGeneration => ModuleGeneration(value, constraints),
                BaseScalarKind.Binary => Binary(value, constraints),
                BaseScalarKind.Int32 => Int32(value, constraints),
                BaseScalarKind.Int64 => Int64(value, constraints),
                BaseScalarKind.UInt32 => UInt32(value, constraints),
                BaseScalarKind.UInt64 => UInt64(value, constraints),
                BaseScalarKind.Decimal => Decimal(value, constraints),
                BaseScalarKind.Boolean => value.ValueKind is JsonValueKind.True or JsonValueKind.False,
                BaseScalarKind.Guid => Guid(value),
                BaseScalarKind.UtcDateTime => UtcDateTime(value),
                BaseScalarKind.ClosedEnum => Enum(value, constraints),
                BaseScalarKind.FrozenArray => Array(value, constraints),
                BaseScalarKind.CanonicalJson => CanonicalJson(value, constraints),
                _ => true,
            };
            return admitted ? null : Violation(field.WireName);
        }
        catch (Exception exception) when (exception is FormatException or OverflowException or JsonException)
        {
            return Violation(field.WireName);
        }
    }

    private static bool String(JsonElement value, BaseScalarConstraintSet constraints)
    {
        if (value.ValueKind != JsonValueKind.String) return false;
        string text = value.GetString()!;
        int bytes = BaseStrictUtf8.GetByteCount(text);
        if (constraints.MinimumUtf8Bytes is { } minimum && bytes < minimum || constraints.MaximumUtf8Bytes is { } maximum && bytes > maximum) return false;
        return constraints.StringNormalization is null || BaseUnicode17Nfc.IsNormalized(text);
    }

    private static bool RecordIdentifier(JsonElement value, BaseScalarConstraintSet constraints) =>
        String(value, constraints) && RecordId.TryParse(value.GetString(), out _);

    private static bool ModuleGeneration(JsonElement value, BaseScalarConstraintSet constraints)
    {
        if (!String(value, constraints)) return false;
        _ = BaseModuleGeneration.ParseCanonical(value.GetString()!);
        return true;
    }

    private static bool Binary(JsonElement value, BaseScalarConstraintSet constraints)
    {
        if (value.ValueKind != JsonValueKind.String) return false;
        byte[] bytes = Convert.FromBase64String(value.GetString()!);
        return bytes.Length >= (constraints.MinimumBinaryBytes ?? 0)
            && (constraints.MaximumBinaryBytes is not { } maximum || bytes.Length <= maximum);
    }

    private static bool Int32(JsonElement value, BaseScalarConstraintSet constraints) => value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out int item) && (constraints.MinimumInt32 is not { } minimum || item >= minimum) && (constraints.MaximumInt32 is not { } maximum || item <= maximum);
    private static bool Int64(JsonElement value, BaseScalarConstraintSet constraints) => value.ValueKind == JsonValueKind.Number && value.TryGetInt64(out long item) && (constraints.MinimumInt64 is not { } minimum || item >= minimum) && (constraints.MaximumInt64 is not { } maximum || item <= maximum);
    private static bool UInt32(JsonElement value, BaseScalarConstraintSet constraints) => value.ValueKind == JsonValueKind.Number && value.TryGetUInt32(out uint item) && (constraints.MinimumUInt32 is not { } minimum || item >= minimum) && (constraints.MaximumUInt32 is not { } maximum || item <= maximum);
    private static bool UInt64(JsonElement value, BaseScalarConstraintSet constraints) => value.ValueKind == JsonValueKind.Number && value.TryGetUInt64(out ulong item) && (constraints.MinimumUInt64 is not { } minimum || item >= minimum) && (constraints.MaximumUInt64 is not { } maximum || item <= maximum);

    private static bool Decimal(JsonElement value, BaseScalarConstraintSet constraints)
    {
        if (value.ValueKind != JsonValueKind.Number || !BaseScalarCanonical.TryParseDecimal(value.GetRawText(), out BaseDecimalValue item)) return false;
        return (constraints.MinimumDecimal is not { } minimum || BaseScalarCanonical.Compare(item, minimum) >= 0) && (constraints.MaximumDecimal is not { } maximum || BaseScalarCanonical.Compare(item, maximum) <= 0);
    }

    private static bool Guid(JsonElement value) => value.ValueKind == JsonValueKind.String
        && System.Guid.TryParseExact(value.GetString(), "D", out System.Guid parsed)
        && string.Equals(value.GetString(), parsed.ToString("D"), StringComparison.Ordinal);

    private static bool UtcDateTime(JsonElement value) => value.ValueKind == JsonValueKind.String
        && DateTimeOffset.TryParseExact(value.GetString(), "yyyy-MM-dd'T'HH:mm:ss.fffffff'Z'", CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out DateTimeOffset parsed)
        && parsed.Offset == TimeSpan.Zero;

    private static bool Enum(JsonElement value, BaseScalarConstraintSet constraints)
    {
        if (constraints.AllowedEnumLiterals.IsDefaultOrEmpty) return true;
        return value.ValueKind == JsonValueKind.String && constraints.AllowedEnumLiterals.Contains(value.GetString()!, StringComparer.Ordinal);
    }

    private static bool Array(JsonElement value, BaseScalarConstraintSet constraints)
    {
        if (value.ValueKind != JsonValueKind.Array) return false;
        int count = value.GetArrayLength();
        return (constraints.MinimumCollectionItems is not { } minimum || count >= minimum) && (constraints.MaximumCollectionItems is not { } maximum || count <= maximum);
    }

    private static bool CanonicalJson(JsonElement value, BaseScalarConstraintSet constraints)
    {
        if (constraints.MaximumCanonicalJsonBytes is not > 0 || constraints.MaximumJsonDepth is not > 0 || constraints.MaximumJsonArrayItems is not > 0 || constraints.MaximumJsonObjectProperties is not > 0 || constraints.MaximumJsonTotalNodes is not > 0 || constraints.MaximumJsonTotalStringUtf8Bytes is not > 0 || constraints.MaximumJsonTotalNameUtf8Bytes is not > 0) return false;
        if (constraints.JsonShape == BaseJsonShape.Object && value.ValueKind != JsonValueKind.Object || constraints.JsonShape == BaseJsonShape.Array && value.ValueKind != JsonValueKind.Array || constraints.JsonShape == BaseJsonShape.ObjectOrArray && value.ValueKind is not JsonValueKind.Object and not JsonValueKind.Array) return false;
        byte[] bytes = BaseStrictUtf8.Encode(value.GetRawText());
        _ = BaseCanonicalJson.ParseAndValidate(bytes, new BaseCanonicalJsonLimits
        {
            MaximumCanonicalBytes = constraints.MaximumCanonicalJsonBytes!.Value,
            MaximumDepth = constraints.MaximumJsonDepth!.Value,
            MaximumArrayItemsPerContainer = constraints.MaximumJsonArrayItems!.Value,
            MaximumObjectPropertiesPerContainer = constraints.MaximumJsonObjectProperties!.Value,
            MaximumTotalNodes = constraints.MaximumJsonTotalNodes!.Value,
            MaximumTotalStringUtf8Bytes = constraints.MaximumJsonTotalStringUtf8Bytes!.Value,
            MaximumTotalNameUtf8Bytes = constraints.MaximumJsonTotalNameUtf8Bytes!.Value,
        });
        return true;
    }

    private static BaseError Violation(string field) => new() { Code = BaseSchemaErrorCodes.ScalarConstraintViolated, Message = "A stored value violates its schema.", Category = ErrorCategory.Validation, Target = field };
}
