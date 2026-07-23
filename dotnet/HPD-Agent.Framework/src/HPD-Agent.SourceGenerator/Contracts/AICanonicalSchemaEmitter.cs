using System;
using System.Text;

namespace HPD.Agent.SourceGenerator.Contracts;

/// <summary>
/// Emits deterministic canonical JSON Schema from the compile-time AI-function contract.
/// </summary>
internal static class AICanonicalSchemaEmitter
{
    /// <summary>Emits the canonical schema for a model-facing method contract.</summary>
    public static string Emit(AIFunctionMethodContract contract)
    {
        var builder = new StringBuilder();
        builder.Append("{\"type\":\"object\",\"properties\":{");
        for (var index = 0; index < contract.Parameters.Length; index++)
        {
            if (index > 0)
            {
                builder.Append(',');
            }

            AppendString(builder, contract.Parameters[index].JsonName);
            builder.Append(':');
            AppendNode(builder, contract.Parameters[index].Contract);
        }

        builder.Append('}');
        var wroteRequired = false;
        foreach (var parameter in contract.Parameters)
        {
            if (!parameter.IsRequired)
            {
                continue;
            }

            if (!wroteRequired)
            {
                builder.Append(",\"required\":[");
                wroteRequired = true;
            }
            else
            {
                builder.Append(',');
            }

            AppendString(builder, parameter.JsonName);
        }

        if (wroteRequired)
        {
            builder.Append(']');
        }

        builder.Append(",\"additionalProperties\":false}");
        return builder.ToString();
    }

    private static void AppendNode(StringBuilder builder, AIContractNode node)
    {
        builder.Append('{');
        switch (node)
        {
            case ScalarContractNode scalar:
                AppendScalar(builder, scalar);
                break;
            case ArrayContractNode array:
                AppendType(builder, "array", array.AllowsNull);
                builder.Append(",\"items\":");
                AppendNode(builder, array.Item);
                break;
            case DictionaryContractNode dictionary:
                AppendType(builder, "object", dictionary.AllowsNull);
                builder.Append(",\"additionalProperties\":");
                AppendNode(builder, dictionary.Value);
                break;
            case ObjectContractNode objectContract:
                AppendObject(builder, objectContract, discriminatorName: null, discriminatorValue: null);
                break;
            case UnionContractNode union:
                AppendUnion(builder, union);
                break;
            default:
                throw new InvalidOperationException($"Canonical schema emission is not implemented for contract node '{node.GetType().Name}'.");
        }

        if (!string.IsNullOrWhiteSpace(node.Description))
        {
            builder.Append(",\"description\":");
            AppendString(builder, node.Description!);
        }

        builder.Append('}');
    }

    private static void AppendObject(
        StringBuilder builder,
        ObjectContractNode contract,
        string? discriminatorName,
        string? discriminatorValue)
    {
        AppendType(builder, "object", contract.AllowsNull);
        builder.Append(",\"properties\":{");
        var wroteProperty = false;
        if (discriminatorName is not null)
        {
            AppendString(builder, discriminatorName);
            builder.Append(":{\"type\":\"string\",\"const\":");
            AppendString(builder, discriminatorValue!);
            builder.Append('}');
            wroteProperty = true;
        }

        foreach (var property in contract.Properties)
        {
            if (wroteProperty)
            {
                builder.Append(',');
            }
            AppendString(builder, property.JsonName);
            builder.Append(':');
            AppendNode(builder, property.Contract);
            wroteProperty = true;
        }
        builder.Append('}');

        var wroteRequired = false;
        if (discriminatorName is not null)
        {
            builder.Append(",\"required\":[");
            AppendString(builder, discriminatorName);
            wroteRequired = true;
        }
        foreach (var property in contract.Properties)
        {
            if (!property.IsRequired)
            {
                continue;
            }
            if (!wroteRequired)
            {
                builder.Append(",\"required\":[");
                wroteRequired = true;
            }
            else
            {
                builder.Append(',');
            }
            AppendString(builder, property.JsonName);
        }
        if (wroteRequired)
        {
            builder.Append(']');
        }
        builder.Append(",\"additionalProperties\":false");
    }

    private static void AppendUnion(StringBuilder builder, UnionContractNode union)
    {
        if (union.AllowsNull)
        {
            builder.Append("\"anyOf\":[{");
        }

        builder.Append("\"oneOf\":[");
        for (var index = 0; index < union.Cases.Length; index++)
        {
            if (index > 0)
            {
                builder.Append(',');
            }
            builder.Append('{');
            AppendObject(
                builder,
                union.Cases[index].Contract,
                union.DiscriminatorPropertyName,
                union.Cases[index].Discriminator);
            builder.Append('}');
        }
        builder.Append(']');

        if (union.AllowsNull)
        {
            builder.Append("},{\"type\":\"null\"}]");
        }
    }

    private static void AppendScalar(StringBuilder builder, ScalarContractNode scalar)
    {
        var jsonType = scalar.Kind switch
        {
            AIScalarKind.String or AIScalarKind.Enum => "string",
            AIScalarKind.Boolean => "boolean",
            AIScalarKind.Integer => "integer",
            AIScalarKind.Number => "number",
            _ => throw new InvalidOperationException($"Unknown scalar kind '{scalar.Kind}'.")
        };
        AppendType(builder, jsonType, scalar.AllowsNull);

        if (scalar.Kind is AIScalarKind.Enum)
        {
            builder.Append(",\"enum\":[");
            for (var index = 0; index < scalar.AllowedValues.Length; index++)
            {
                if (index > 0)
                {
                    builder.Append(',');
                }

                AppendString(builder, scalar.AllowedValues[index]);
            }
            builder.Append(']');
        }

        if (scalar.Format is not null)
        {
            builder.Append(",\"format\":");
            AppendString(builder, scalar.Format);
        }
    }

    private static void AppendType(StringBuilder builder, string type, bool allowsNull)
    {
        builder.Append("\"type\":");
        if (allowsNull)
        {
            builder.Append('[');
            AppendString(builder, type);
            builder.Append(",\"null\"]");
        }
        else
        {
            AppendString(builder, type);
        }
    }

    private static void AppendString(StringBuilder builder, string value)
    {
        builder.Append('"');
        foreach (var character in value)
        {
            switch (character)
            {
                case '"': builder.Append("\\\""); break;
                case '\\': builder.Append("\\\\"); break;
                case '\b': builder.Append("\\b"); break;
                case '\f': builder.Append("\\f"); break;
                case '\n': builder.Append("\\n"); break;
                case '\r': builder.Append("\\r"); break;
                case '\t': builder.Append("\\t"); break;
                default:
                    if (character < 0x20)
                    {
                        builder.Append("\\u").Append(((int)character).ToString("x4"));
                    }
                    else
                    {
                        builder.Append(character);
                    }
                    break;
            }
        }
        builder.Append('"');
    }
}
