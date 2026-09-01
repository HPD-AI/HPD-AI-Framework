using System.ComponentModel;

namespace HPD.Base;

/// <summary>Provides generator-only construction of canonical L54 schema authority.</summary>
[EditorBrowsable(EditorBrowsableState.Never)]
public static class BaseGeneratedSchemaRegistration
{
    /// <summary>Returns the frozen built-in scalar codec selected by generated schema code.</summary>
    [EditorBrowsable(EditorBrowsableState.Never)]
    public static BaseScalarCodecAuthority ScalarCodec(BaseScalarKind kind) => BaseSchemaContract.Codec(kind);

    /// <summary>Returns one serializer-bound generated codec authority.</summary>
    [EditorBrowsable(EditorBrowsableState.Never)]
    public static BaseScalarCodecAuthority ScalarCodec(BaseScalarKind kind, string qualifier) => BaseSchemaContract.Codec(kind, qualifier);

    /// <summary>Returns the canonical qualifier for an exact generated enum literal set.</summary>
    [EditorBrowsable(EditorBrowsableState.Never)]
    public static string EnumQualifier(params string[] literals) => BaseSchemaContract.EnumQualifier(literals);

    /// <summary>Parses one exact reduced decimal token selected by generated metadata.</summary>
    [EditorBrowsable(EditorBrowsableState.Never)]
    public static BaseDecimalValue Decimal(string canonicalToken) => BaseScalarCanonical.TryParseDecimal(canonicalToken, out BaseDecimalValue value)
        ? value : throw new InvalidOperationException(BaseSchemaErrorCodes.ContractInvalid);

    /// <summary>Seals one generated field constraint declaration.</summary>
    [EditorBrowsable(EditorBrowsableState.Never)]
    public static BaseScalarConstraintChecksum ScalarConstraintChecksum(
        string collectionId,
        string fieldId,
        BaseFieldPresence presence,
        BaseFieldNullability nullability,
        BaseScalarKind kind,
        BaseScalarConstraintSet constraints)
        => BaseSchemaContract.SealConstraints(collectionId, fieldId, presence, nullability, BaseSchemaContract.Codec(kind), constraints);

    /// <summary>Seals constraints against an exact generated codec authority.</summary>
    [EditorBrowsable(EditorBrowsableState.Never)]
    public static BaseScalarConstraintChecksum ScalarConstraintChecksum(string collectionId, string fieldId, BaseFieldPresence presence, BaseFieldNullability nullability, BaseScalarCodecAuthority codec, BaseScalarConstraintSet constraints)
        => BaseSchemaContract.SealConstraints(collectionId, fieldId, presence, nullability, codec, constraints);

    /// <summary>Creates one exact generated predicate literal from canonical JSON scalar text.</summary>
    [EditorBrowsable(EditorBrowsableState.Never)]
    public static BaseCanonicalScalarLiteral ScalarLiteral(BaseScalarKind kind, BaseScalarCodecAuthority codec, string canonicalJson)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(canonicalJson);
        using System.Text.Json.JsonDocument document = System.Text.Json.JsonDocument.Parse(canonicalJson);
        System.Text.Json.JsonElement value = document.RootElement;
        if (value.ValueKind is System.Text.Json.JsonValueKind.Null or System.Text.Json.JsonValueKind.Object or System.Text.Json.JsonValueKind.Array)
            throw new InvalidOperationException(BaseSchemaErrorCodes.ContractInvalid);
        return new BaseCanonicalScalarLiteral { Kind = kind, Codec = codec with { AllowedConstraints = [.. codec.AllowedConstraints] }, CanonicalBytes = [.. BaseScalarCanonical.Encode(kind, value)] };
    }
}
