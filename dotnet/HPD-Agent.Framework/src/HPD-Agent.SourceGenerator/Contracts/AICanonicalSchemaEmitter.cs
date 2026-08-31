using System;
using System.Linq;
using System.Text;

namespace HPD.Agent.SourceGenerator.Contracts;

/// <summary>
/// Emits deterministic canonical JSON Schema from the compile-time AI-function contract.
/// </summary>
internal static class AICanonicalSchemaEmitter
{
    /// <summary>Emits the canonical schema for one reusable root contract.</summary>
    public static string EmitRoot(AIContractNode contract)
    {
        var builder = new StringBuilder();
        AppendNode(builder, contract);
        return builder.ToString();
    }

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
            AppendNode(
                builder,
                contract.Parameters[index].Contract,
                contract.Parameters[index].Symbol.HasExplicitDefaultValue,
                contract.Parameters[index].Symbol.HasExplicitDefaultValue
                    ? contract.Parameters[index].Symbol.ExplicitDefaultValue
                    : null);
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

    private static void AppendNode(
        StringBuilder builder,
        AIContractNode node,
        bool hasDefault = false,
        object? defaultValue = null)
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
        if (hasDefault)
        {
            builder.Append(",\"default\":");
            AppendDefault(builder, node, defaultValue);
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
            var binding = contract.Construction.Members.First(member =>
                Microsoft.CodeAnalysis.SymbolEqualityComparer.Default.Equals(member.Property, property.Symbol));
            var hasDefault = binding.ConstructorParameter?.HasExplicitDefaultValue == true;
            AppendNode(
                builder,
                property.Contract,
                hasDefault,
                hasDefault ? binding.ConstructorParameter!.ExplicitDefaultValue : null);
            wroteProperty = true;
        }
        if (!contract.AcceptedFrameworkProperties.IsDefaultOrEmpty &&
            contract.AcceptedFrameworkProperties.Contains("invocationMode", StringComparer.Ordinal))
        {
            if (wroteProperty) builder.Append(',');
            builder.Append("\"invocationMode\":{\"type\":\"string\",\"enum\":[\"synchronous\",\"background\"],\"description\":\"Whether this action completes now or runs in the background.\"}");
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
            var unionCase = union.Cases[index];
            var caseContract = string.Equals(unionCase.InvocationModePolicy, "ModelChoice", StringComparison.Ordinal)
                ? unionCase.Contract with { AcceptedFrameworkProperties = ["invocationMode"] }
                : unionCase.Contract;
            AppendObject(
                builder,
                caseContract,
                union.DiscriminatorPropertyName,
                unionCase.Discriminator);
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

    private static void AppendConstant(StringBuilder builder, object? value)
    {
        if (value is null) { builder.Append("null"); return; }
        switch (value)
        {
            case string text: AppendString(builder, text); break;
            case char character: AppendString(builder, character.ToString()); break;
            case bool boolean: builder.Append(boolean ? "true" : "false"); break;
            case float number: builder.Append(number.ToString("R", System.Globalization.CultureInfo.InvariantCulture)); break;
            case double number: builder.Append(number.ToString("R", System.Globalization.CultureInfo.InvariantCulture)); break;
            case decimal number: builder.Append(number.ToString(System.Globalization.CultureInfo.InvariantCulture)); break;
            default: builder.Append(Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture)); break;
        }
    }

    private static void AppendDefault(StringBuilder builder, AIContractNode node, object? value)
    {
        if (value is not null &&
            node is ScalarContractNode { Kind: AIScalarKind.Enum } scalar &&
            scalar.Type is Microsoft.CodeAnalysis.INamedTypeSymbol enumType)
        {
            var member = enumType.GetMembers()
                .OfType<Microsoft.CodeAnalysis.IFieldSymbol>()
                .FirstOrDefault(field =>
                    field.HasConstantValue &&
                    Equals(
                        Convert.ToInt64(field.ConstantValue, System.Globalization.CultureInfo.InvariantCulture),
                        Convert.ToInt64(value, System.Globalization.CultureInfo.InvariantCulture)));
            if (member is null)
                throw new InvalidOperationException($"Enum default '{value}' is not declared by '{enumType.Name}'.");
            AppendString(builder, member.Name);
            return;
        }

        AppendConstant(builder, value);
    }
}
