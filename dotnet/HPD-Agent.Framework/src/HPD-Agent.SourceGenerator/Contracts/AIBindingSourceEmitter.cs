using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using HPD.Agent.SourceGenerator.Capabilities;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace HPD.Agent.SourceGenerator.Contracts;

/// <summary>
/// Emits reflection-free, one-shot argument binding code from analyzed contracts.
/// </summary>
internal static class AIBindingSourceEmitter
{
    /// <summary>Emits the binder entry point and recursive helpers for one generated function.</summary>
    public static string Emit(FunctionCapability function)
    {
        var parameters = function.Parameters.Where(static parameter => parameter.IsModelFacing).ToArray();
        var builder = new StringBuilder();
        var helpers = new StringBuilder();
        var emittedHelpers = new HashSet<string>(StringComparer.Ordinal);
        var entryName = "Bind" + Sanitize(function.Name) + "Arguments";
        var dtoName = parameters.Length == 0 ? "object" : function.Name + "Args";

        builder.AppendLine($"        private static global::HPD.Agent.AIFunctionBindingResult {entryName}(global::System.Text.Json.JsonElement json)");
        builder.AppendLine("        {");
        builder.AppendLine("            try");
        builder.AppendLine("            {");
        builder.AppendLine($"                global::HPD.Agent.HPDGeneratedToolArgumentBinder.ValidateProperties(json, \"\"{FormatNameArguments(parameters.Select(static parameter => parameter.Name))});");
        builder.AppendLine($"                var result = new {dtoName}();");
        foreach (var parameter in parameters)
        {
            var helperName = EmitNodeHelper(function.Name + "_" + parameter.Name, parameter.Contract!, helpers, emittedHelpers);
            var jsonVariable = "json_" + Sanitize(parameter.Name);
            if (!parameter.HasDefaultValue)
            {
                builder.AppendLine($"                result.{EscapeIdentifier(parameter.Name)} = {helperName}(global::HPD.Agent.HPDGeneratedToolArgumentBinder.GetRequiredProperty(json, \"{Escape(parameter.Name)}\", \"\"), \"{Escape(parameter.Name)}\");");
            }
            else
            {
                builder.AppendLine($"                if (global::HPD.Agent.HPDGeneratedToolArgumentBinder.TryGetOptionalProperty(json, \"{Escape(parameter.Name)}\", \"\", out var {jsonVariable}))");
                builder.AppendLine($"                    result.{EscapeIdentifier(parameter.Name)} = {helperName}({jsonVariable}, \"{Escape(parameter.Name)}\");");
            }
        }
        builder.AppendLine("                return global::HPD.Agent.AIFunctionBindingResult.Success(result);");
        builder.AppendLine("            }");
        builder.AppendLine("            catch (global::HPD.Agent.HPDToolArgumentException exception)");
        builder.AppendLine("            {");
        builder.AppendLine("                return global::HPD.Agent.AIFunctionBindingResult.Failure(new global::HPD.Agent.ValidationError");
        builder.AppendLine("                {");
        builder.AppendLine("                    Property = exception.PropertyName,");
        builder.AppendLine("                    ErrorMessage = exception.Message,");
        builder.AppendLine("                    ErrorCode = exception.ErrorCode");
        builder.AppendLine("                });");
        builder.AppendLine("            }");
        builder.AppendLine("            catch (global::System.Text.Json.JsonException exception)");
        builder.AppendLine("            {");
        builder.AppendLine("                return global::HPD.Agent.AIFunctionBindingResult.Failure(new global::HPD.Agent.ValidationError");
        builder.AppendLine("                {");
        builder.AppendLine("                    Property = exception.Path ?? string.Empty,");
        builder.AppendLine("                    ErrorMessage = exception.Message,");
        builder.AppendLine("                    ErrorCode = \"binding_construction_failed\"");
        builder.AppendLine("                });");
        builder.AppendLine("            }");
        builder.AppendLine("            catch (global::System.Exception exception)");
        builder.AppendLine("            {");
        builder.AppendLine("                return global::HPD.Agent.AIFunctionBindingResult.Failure(new global::HPD.Agent.ValidationError");
        builder.AppendLine("                {");
        builder.AppendLine("                    Property = string.Empty,");
        builder.AppendLine("                    ErrorMessage = exception.Message,");
        builder.AppendLine("                    ErrorCode = \"binding_construction_failed\"");
        builder.AppendLine("                });");
        builder.AppendLine("            }");
        builder.AppendLine("        }");
        builder.Append(helpers);
        return builder.ToString();
    }

    private static string EmitNodeHelper(
        string identity,
        AIContractNode node,
        StringBuilder helpers,
        HashSet<string> emittedHelpers)
    {
        var methodName = "BindContract_" + Sanitize(identity) + "_" + StableSuffix(identity);
        if (!emittedHelpers.Add(methodName))
            return methodName;

        EmitDependencies(identity, node, helpers, emittedHelpers);

        var typeName = TypeName(node.Type);
        helpers.AppendLine();
        helpers.AppendLine($"        private static {typeName} {methodName}(global::System.Text.Json.JsonElement value, string path)");
        helpers.AppendLine("        {");
        if (node.AllowsNull)
        {
            helpers.AppendLine("            if (value.ValueKind == global::System.Text.Json.JsonValueKind.Null)");
            helpers.AppendLine("                return default!;");
        }
        else
        {
            helpers.AppendLine("            if (value.ValueKind == global::System.Text.Json.JsonValueKind.Null)");
            helpers.AppendLine("                throw global::HPD.Agent.HPDGeneratedToolArgumentBinder.Error(path, \"null_not_allowed\", \"Null is not allowed.\");");
        }

        switch (node)
        {
            case ScalarContractNode scalar:
                EmitScalarBody(helpers, scalar);
                break;
            case ArrayContractNode array:
                EmitArrayBody(identity, helpers, emittedHelpers, array);
                break;
            case DictionaryContractNode dictionary:
                EmitDictionaryBody(identity, helpers, emittedHelpers, dictionary);
                break;
            case ObjectContractNode objectContract:
                EmitObjectBody(identity, helpers, emittedHelpers, objectContract);
                break;
            case UnionContractNode union:
                EmitUnionBody(identity, helpers, emittedHelpers, union);
                break;
            default:
                throw new InvalidOperationException($"Binding emission is not implemented for '{node.GetType().Name}'.");
        }

        helpers.AppendLine("        }");
        return methodName;
    }

    private static void EmitDependencies(string identity, AIContractNode node, StringBuilder helpers, HashSet<string> emittedHelpers)
    {
        switch (node)
        {
            case ArrayContractNode array:
                EmitNodeHelper(identity + "_item", array.Item, helpers, emittedHelpers);
                break;
            case DictionaryContractNode dictionary:
                EmitNodeHelper(identity + "_value", dictionary.Value, helpers, emittedHelpers);
                break;
            case ObjectContractNode objectContract:
                foreach (var property in objectContract.Properties)
                    EmitNodeHelper(identity + "_" + property.JsonName, property.Contract, helpers, emittedHelpers);
                break;
            case UnionContractNode union:
                foreach (var @case in union.Cases)
                {
                    var caseContract = @case.Contract with { AcceptedFrameworkProperties = [union.DiscriminatorPropertyName] };
                    EmitNodeHelper(identity + "_case_" + @case.Discriminator, caseContract, helpers, emittedHelpers);
                }
                break;
        }
    }

    private static void EmitScalarBody(StringBuilder builder, ScalarContractNode scalar)
    {
        if (scalar.Kind is AIScalarKind.Enum)
        {
            builder.AppendLine("            if (value.ValueKind != global::System.Text.Json.JsonValueKind.String)");
            builder.AppendLine("                throw global::HPD.Agent.HPDGeneratedToolArgumentBinder.Error(path, \"invalid_json_kind\", \"Expected an enum string.\");");
            builder.AppendLine("            return value.GetString() switch");
            builder.AppendLine("            {");
            foreach (var enumValue in scalar.AllowedValues)
                builder.AppendLine($"                \"{Escape(enumValue)}\" => {TypeName(UnwrapNullable(scalar.Type))}.{EscapeIdentifier(enumValue)},");
            builder.AppendLine("                _ => throw global::HPD.Agent.HPDGeneratedToolArgumentBinder.Error(path, \"invalid_enum_value\", \"Unsupported enum value.\")");
            builder.AppendLine("            };");
            return;
        }

        var effectiveType = UnwrapNullable(scalar.Type);
        var call = effectiveType.SpecialType switch
        {
            SpecialType.System_String => "BindString",
            SpecialType.System_Char => "BindChar",
            SpecialType.System_Boolean => "BindBoolean",
            SpecialType.System_SByte => "BindSByte",
            SpecialType.System_Byte => "BindByte",
            SpecialType.System_Int16 => "BindInt16",
            SpecialType.System_UInt16 => "BindUInt16",
            SpecialType.System_Int32 => "BindInt32",
            SpecialType.System_UInt32 => "BindUInt32",
            SpecialType.System_Int64 => "BindInt64",
            SpecialType.System_UInt64 => "BindUInt64",
            SpecialType.System_Single => "BindSingle",
            SpecialType.System_Double => "BindDouble",
            SpecialType.System_Decimal => "BindDecimal",
            _ => WellKnownBinder(effectiveType)
        };
        builder.AppendLine($"            return global::HPD.Agent.HPDGeneratedToolArgumentBinder.{call}(value, path);");
    }

    private static void EmitArrayBody(string identity, StringBuilder builder, HashSet<string> emittedHelpers, ArrayContractNode array)
    {
        var itemHelper = EmitNodeHelper(identity + "_item", array.Item, builder, emittedHelpers);
        var itemType = TypeName(array.Item.Type);
        builder.AppendLine("            global::HPD.Agent.HPDGeneratedToolArgumentBinder.RequireArray(value, path);");
        builder.AppendLine($"            var result = new global::System.Collections.Generic.List<{itemType}>();");
        builder.AppendLine("            var index = 0;");
        builder.AppendLine("            foreach (var item in value.EnumerateArray())");
        builder.AppendLine("            {");
        builder.AppendLine($"                result.Add({itemHelper}(item, global::HPD.Agent.HPDGeneratedToolArgumentBinder.AppendIndex(path, index)));");
        builder.AppendLine("                index++;");
        builder.AppendLine("            }");
        builder.AppendLine(array.Type is IArrayTypeSymbol ? "            return result.ToArray();" : "            return result;");
    }

    private static void EmitDictionaryBody(string identity, StringBuilder builder, HashSet<string> emittedHelpers, DictionaryContractNode dictionary)
    {
        var valueHelper = EmitNodeHelper(identity + "_value", dictionary.Value, builder, emittedHelpers);
        var valueType = TypeName(dictionary.Value.Type);
        builder.AppendLine("            global::HPD.Agent.HPDGeneratedToolArgumentBinder.RequireObject(value, path);");
        builder.AppendLine($"            var result = new global::System.Collections.Generic.Dictionary<string, {valueType}>(global::System.StringComparer.Ordinal);");
        builder.AppendLine("            foreach (var property in value.EnumerateObject())");
        builder.AppendLine("            {");
        builder.AppendLine("                if (result.ContainsKey(property.Name))");
        builder.AppendLine("                    throw global::HPD.Agent.HPDGeneratedToolArgumentBinder.Error(global::HPD.Agent.HPDGeneratedToolArgumentBinder.Append(path, property.Name), \"duplicate_property\", \"Dictionary key occurs more than once.\");");
        builder.AppendLine($"                result.Add(property.Name, {valueHelper}(property.Value, global::HPD.Agent.HPDGeneratedToolArgumentBinder.Append(path, property.Name)));");
        builder.AppendLine("            }");
        builder.AppendLine("            return result;");
    }

    private static void EmitObjectBody(string identity, StringBuilder builder, HashSet<string> emittedHelpers, ObjectContractNode contract)
    {
        var acceptedNames = contract.Properties.Select(static property => property.JsonName)
            .Concat(contract.AcceptedFrameworkProperties.IsDefault ? Array.Empty<string>() : contract.AcceptedFrameworkProperties);
        builder.AppendLine($"            global::HPD.Agent.HPDGeneratedToolArgumentBinder.ValidateProperties(value, path{FormatNameArguments(acceptedNames)});");
        foreach (var property in contract.Properties)
        {
            var helper = EmitNodeHelper(identity + "_" + property.JsonName, property.Contract, builder, emittedHelpers);
            var variable = "bound_" + Sanitize(property.JsonName);
            if (property.IsRequired)
            {
                builder.AppendLine($"            var {variable} = {helper}(global::HPD.Agent.HPDGeneratedToolArgumentBinder.GetRequiredProperty(value, \"{Escape(property.JsonName)}\", path), global::HPD.Agent.HPDGeneratedToolArgumentBinder.Append(path, \"{Escape(property.JsonName)}\"));");
            }
            else
            {
                builder.AppendLine($"            var has_{variable} = global::HPD.Agent.HPDGeneratedToolArgumentBinder.TryGetOptionalProperty(value, \"{Escape(property.JsonName)}\", path, out var json_{variable});");
                builder.AppendLine($"            var {variable} = has_{variable} ? {helper}(json_{variable}, global::HPD.Agent.HPDGeneratedToolArgumentBinder.Append(path, \"{Escape(property.JsonName)}\")) : {DefaultValue(property, contract)};");
            }
        }

        var constructor = contract.Construction.Constructor!;
        var constructorArguments = constructor.Parameters.Select(parameter =>
        {
            var property = contract.Properties.Single(candidate => string.Equals(candidate.Symbol.Name, parameter.Name, StringComparison.OrdinalIgnoreCase));
            return "bound_" + Sanitize(property.JsonName);
        });
        builder.AppendLine($"            var result = new {TypeName(contract.Type)}({string.Join(", ", constructorArguments)});");
        foreach (var binding in contract.Construction.Members.Where(static binding => binding.ConstructorParameter is null))
        {
            var property = contract.Properties.Single(candidate => SymbolEqualityComparer.Default.Equals(candidate.Symbol, binding.Property));
            var variable = "bound_" + Sanitize(property.JsonName);
            if (property.IsRequired)
                builder.AppendLine($"            result.{EscapeIdentifier(binding.Property.Name)} = {variable};");
            else
                builder.AppendLine($"            if (has_{variable}) result.{EscapeIdentifier(binding.Property.Name)} = {variable};");
        }
        builder.AppendLine("            return result;");
    }

    private static void EmitUnionBody(string identity, StringBuilder builder, HashSet<string> emittedHelpers, UnionContractNode union)
    {
        builder.AppendLine("            global::HPD.Agent.HPDGeneratedToolArgumentBinder.RequireObject(value, path);");
        builder.AppendLine($"            var discriminator = global::HPD.Agent.HPDGeneratedToolArgumentBinder.BindString(global::HPD.Agent.HPDGeneratedToolArgumentBinder.GetRequiredProperty(value, \"{Escape(union.DiscriminatorPropertyName)}\", path), global::HPD.Agent.HPDGeneratedToolArgumentBinder.Append(path, \"{Escape(union.DiscriminatorPropertyName)}\"));");
        builder.AppendLine("            return discriminator switch");
        builder.AppendLine("            {");
        foreach (var @case in union.Cases)
        {
            var caseContract = @case.Contract with { AcceptedFrameworkProperties = [union.DiscriminatorPropertyName] };
            var helper = EmitNodeHelper(identity + "_case_" + @case.Discriminator, caseContract, builder, emittedHelpers);
            builder.AppendLine($"                \"{Escape(@case.Discriminator)}\" => {helper}(value, path),");
        }
        builder.AppendLine("                _ => throw global::HPD.Agent.HPDGeneratedToolArgumentBinder.Error(global::HPD.Agent.HPDGeneratedToolArgumentBinder.Append(path, \"" + Escape(union.DiscriminatorPropertyName) + "\"), \"unknown_union_discriminator\", \"Unsupported discriminator value.\")");
        builder.AppendLine("            };");
    }

    private static string DefaultValue(AIContractProperty property, ObjectContractNode contract)
    {
        var parameter = contract.Construction.Members
            .First(binding => SymbolEqualityComparer.Default.Equals(binding.Property, property.Symbol))
            .ConstructorParameter;
        return parameter is not null && parameter.HasExplicitDefaultValue
            ? FormatConstant(parameter.ExplicitDefaultValue, parameter.Type)
            : "default!";
    }

    private static string FormatConstant(object? value, ITypeSymbol type)
    {
        if (value is null) return "null";
        if (type.TypeKind is TypeKind.Enum) return $"({TypeName(type)}){Convert.ToInt64(value, CultureInfo.InvariantCulture).ToString(CultureInfo.InvariantCulture)}";
        return value switch
        {
            string text => "\"" + Escape(text) + "\"",
            char character => "'" + (character == '\'' ? "\\'" : character.ToString()) + "'",
            bool boolean => boolean ? "true" : "false",
            float number => number.ToString("R", CultureInfo.InvariantCulture) + "f",
            double number => number.ToString("R", CultureInfo.InvariantCulture) + "d",
            decimal number => number.ToString(CultureInfo.InvariantCulture) + "m",
            long number => number.ToString(CultureInfo.InvariantCulture) + "L",
            ulong number => number.ToString(CultureInfo.InvariantCulture) + "UL",
            uint number => number.ToString(CultureInfo.InvariantCulture) + "U",
            _ => Convert.ToString(value, CultureInfo.InvariantCulture) ?? "default!"
        };
    }

    private static string WellKnownBinder(ITypeSymbol type) => type.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat) switch
    {
        "System.Guid" => "BindGuid",
        "System.DateTime" => "BindDateTime",
        "System.DateTimeOffset" => "BindDateTimeOffset",
        "System.DateOnly" => "BindDateOnly",
        "System.TimeOnly" => "BindTimeOnly",
        "System.TimeSpan" => "BindTimeSpan",
        _ => throw new InvalidOperationException($"No generated scalar binder exists for '{type}'.")
    };

    private static string TypeName(ITypeSymbol type) => type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
    private static ITypeSymbol UnwrapNullable(ITypeSymbol type) =>
        type is INamedTypeSymbol named && named.OriginalDefinition.SpecialType is SpecialType.System_Nullable_T
            ? named.TypeArguments[0]
            : type;
    private static string FormatNameArguments(IEnumerable<string> names)
    {
        var formatted = string.Join(", ", names.Select(name => "\"" + Escape(name) + "\""));
        return formatted.Length == 0 ? string.Empty : ", " + formatted;
    }
    private static string Sanitize(string value) => new(value.Select(character => char.IsLetterOrDigit(character) ? character : '_').ToArray());
    private static string EscapeIdentifier(string value) =>
        SyntaxFacts.GetKeywordKind(value) is SyntaxKind.None ? value : "@" + value;
    private static string StableSuffix(string value)
    {
        const uint offset = 2166136261;
        const uint prime = 16777619;
        var hash = offset;
        foreach (var character in value)
        {
            hash ^= character;
            hash = unchecked(hash * prime);
        }
        return hash.ToString("x8", CultureInfo.InvariantCulture);
    }
    private static string Escape(string value) => value.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\r", "\\r").Replace("\n", "\\n");
}
