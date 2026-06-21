using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace HPD.Agent.SourceGenerator.Capabilities;

/// <summary>
/// Represents a function capability - a standard AI function that performs a specific operation.
/// Decorated with [AIFunction] attribute.
/// </summary>
internal class FunctionCapability : BaseCapability
{
    public override CapabilityType Type => CapabilityType.Function;
    public override bool IsContainer => false;  // Functions are NOT containers (direct execution)
    public override bool EmitsIntoCreateTools => true;
    public override bool RequiresInstance => true;  // Functions typically require instance (unless static)

    // ========== Function-Specific Properties ==========

    /// <summary>
    /// Custom name override from [AIFunction(Name = "...")] attribute.
    /// If null, uses the method Name property.
    /// </summary>
    public string? CustomName { get; set; }

    /// <summary>
    /// The parameters of the function.
    /// </summary>
    public List<ParameterInfo> Parameters { get; set; } = new();

    /// <summary>
    /// The return type of the function (e.g., "Task&lt;string&gt;", "void", "int").
    /// </summary>
    public string ReturnType { get; set; } = string.Empty;

    /// <summary>
    /// Whether the function is asynchronous (returns Task or Task&lt;T&gt;).
    /// </summary>
    public bool IsAsync { get; set; }

    /// <summary>
    /// Whether the function is marked with [RequiresPermission].
    /// </summary>
    public bool RequiresPermission { get; set; }

    /// <summary>
    /// The list of required permissions from [RequiresPermission(...)] attribute.
    /// </summary>
    public List<string> RequiredPermissions { get; set; } = new();

    /// <summary>
    /// The kind of tool this function represents (Function or Output).
    /// Output tools are used for structured output and don't execute - their args ARE the output.
    /// </summary>
    public string Kind { get; set; } = "Function";

    /// <summary>
    /// Whether this function has any conditional parameters.
    /// </summary>
    public bool HasConditionalParameters => Parameters.Any(p => p.IsConditional);

    /// <summary>
    /// Effective function name (custom name if provided, otherwise method name).
    /// </summary>
    public string FunctionName => CustomName ?? Name;

    /// <summary>
    /// Validation data for later processing.
    /// </summary>
    public ValidationData? ValidationData { get; set; }

    // ========== Code Generation ==========

    /// <summary>
    /// Generates the registration code for this function.
    /// Creates HPDAIFunctionFactory.Create(...) call with all necessary metadata.
    ///
    /// Phase 3: Full implementation migrated from HPDToolSourceGenerator.GenerateFunctionRegistration().
    /// </summary>
    /// <param name="parent">The parent ToolHarness that contains this function (ToolHarnessInfo).</param>
    /// <returns>The generated registration code as a string.</returns>
    public override string GenerateRegistrationCode(object parent)
    {
        var ToolHarness = (ToolHarnessInfo)parent;

        var nameCode = $"\"{FunctionName}\"";
        var descriptionCode = HasDynamicDescription
            ? $"Resolve{Name}Description(context)"
            : $"\"{Description}\"";

        var relevantParams = Parameters.Where(p => p.IsModelFacing).ToList();

        var dtoName = relevantParams.Any() ? $"{Name}Args" : "object";

        var invocationArgs = string.Join(", ", Parameters.Select(p =>
        {
            return p.Kind switch
            {
                FunctionParameterKind.CancellationToken => "cancellationToken",
                FunctionParameterKind.AIFunctionArguments => "arguments",
                FunctionParameterKind.ServiceProvider => "arguments.Services",
                FunctionParameterKind.FunctionExecutionContext => "functionContext",
                _ => $"args.{p.Name}"
            };
        }));

        string asyncKeyword = IsAsync ? "async" : "";
        string awaitKeyword = IsAsync ? "await" : "";
        string returnType = "Task<object?>";
        string returnWrapper = IsAsync ? "" : "Task.FromResult";

        string schemaProviderCode = GenerateSchemaProviderCode(relevantParams);

        // Check if the return type is void (includes non-generic Task — await Task yields void)
        bool isVoidReturn = ReturnType == "void" || ReturnType == "System.Void"
            || ReturnType == "Task" || ReturnType == "System.Threading.Tasks.Task";

        string invocationLogic;
        if (relevantParams.Any())
        {
            string returnStatement;
            if (isVoidReturn)
            {
                // For void methods, call the method and return null
                returnStatement = IsAsync
                    ? $"{awaitKeyword} instance.{Name}({invocationArgs}); return null;"
                    : $"instance.{Name}({invocationArgs}); return null;";
            }
            else
            {
                // For non-void methods, return the result as object
                returnStatement = IsAsync
                    ? $"return ({awaitKeyword} instance.{Name}({invocationArgs})) as object;"
                    : $"return {returnWrapper}(({awaitKeyword} instance.{Name}({invocationArgs})) as object);";
            }

            invocationLogic =
$@"({asyncKeyword} (arguments, functionContext, cancellationToken) =>
            {{
                var jsonArgs = arguments.GetJson();
                var args = Parse{dtoName}(jsonArgs, arguments.GetJsonSerializerOptions());
                {returnStatement}
            }})";
        }
        else
        {
            string returnStatement;
            if (isVoidReturn)
            {
                // For void methods, call the method and return null
                returnStatement = IsAsync
                    ? $"{awaitKeyword} instance.{Name}({invocationArgs}); return null;"
                    : $"instance.{Name}({invocationArgs}); return null;";
            }
            else
            {
                // For non-void methods, return the result as object
                returnStatement = IsAsync
                    ? $"return ({awaitKeyword} instance.{Name}({invocationArgs})) as object;"
                    : $"return {returnWrapper}(({awaitKeyword} instance.{Name}({invocationArgs})) as object);";
            }

            invocationLogic =
$@"({asyncKeyword} (arguments, functionContext, cancellationToken) =>
            {{
                {returnStatement}
            }})";
        }

        var options = new StringBuilder();
        options.AppendLine($"                Name = {nameCode},");
        options.AppendLine($"                Description = {descriptionCode},");
        options.AppendLine($"                RequiresPermission = {RequiresPermission.ToString().ToLower()},");
        options.AppendLine($"                Validator = Create{Name}Validator(),");
        options.AppendLine($"                SchemaProvider = {schemaProviderCode},");
        options.AppendLine("                SerializerOptions = serialization?.SerializerOptions,");
        if (!isVoidReturn)
        {
            options.AppendLine($"                ResultType = typeof({GetDeclaredResultType(ReturnType)}),");
        }
        options.AppendLine($"                ParameterDescriptions = {GenerateParameterDescriptions()},");

        // ALWAYS add ParentToolHarness metadata (enables ToolHarnessReferences to work with any ToolHarness)
        // Note: ToolHarnesses without [Collapse] remain "always visible" by default
        // Skills can use ToolHarnessReferences to Collapse them on-demand
        options.AppendLine("                AdditionalProperties = new Dictionary<string, object>");
        options.AppendLine("                {");
        options.AppendLine($"                    [\"ParentToolHarness\"] = \"{ToolHarness.ClassName}\",");

        // Add Kind if it's an output tool (structured output)
        if (Kind == "Output")
        {
            options.AppendLine($"                    [\"Kind\"] = \"Output\",");
        }

        options.AppendLine("                    [\"IsContainer\"] = false");
        options.Append("                }");

        return
$@"HPDAIFunctionFactory.Create(
            new Func<AIFunctionArguments, FunctionExecutionContext, CancellationToken, {returnType}>{invocationLogic},
            new HPDAIFunctionFactoryOptions
            {{
{options}
            }}
        )";
    }

    /// <summary>
    /// Generates parameter descriptions dictionary for this function.
    /// </summary>
    private string GenerateParameterDescriptions()
    {
        var paramsWithDesc = Parameters.Where(p => p.IsModelFacing && !string.IsNullOrEmpty(p.Description)).ToList();
        if (!paramsWithDesc.Any())
            return "null";

        var descriptions = new StringBuilder();
        descriptions.AppendLine("new Dictionary<string, string> {");

        for (int i = 0; i < paramsWithDesc.Count; i++)
        {
            var param = paramsWithDesc[i];
            var comma = i < paramsWithDesc.Count - 1 ? "," : "";
            var descCode = param.HasDynamicDescription
                ? $"Resolve{Name}Parameter{param.Name}Description(context)"
                : $"\"{param.Description}\"";
            descriptions.AppendLine($"                    {{ \"{param.Name}\", {descCode} }}{comma}");
        }

        descriptions.Append("                }");
        return descriptions.ToString();
    }

    private string GenerateSchemaProviderCode(List<ParameterInfo> relevantParams)
    {
        var schemaJson = GenerateJsonSchema(relevantParams);
        return $@"() =>
                {{
                    using var document = global::System.Text.Json.JsonDocument.Parse(""{EscapeStringLiteral(schemaJson)}"");
                    return document.RootElement.Clone();
                }}";
    }

    private string GenerateJsonSchema(List<ParameterInfo> relevantParams)
    {
        var sb = new StringBuilder();
        sb.Append("{\"type\":\"object\",\"properties\":{");

        for (var i = 0; i < relevantParams.Count; i++)
        {
            var param = relevantParams[i];
            if (i > 0)
                sb.Append(',');

            sb.Append('"').Append(EscapeJsonString(param.Name)).Append("\":{");
            AppendJsonSchemaForParameter(sb, param);
            sb.Append('}');
        }

        sb.Append("}");

        var requiredParams = relevantParams
            .Where(param => !param.IsNullable && !param.HasDefaultValue)
            .Select(param => param.Name)
            .ToList();

        if (requiredParams.Count > 0)
        {
            sb.Append(",\"required\":[");
            for (var i = 0; i < requiredParams.Count; i++)
            {
                if (i > 0)
                    sb.Append(',');

                sb.Append('"').Append(EscapeJsonString(requiredParams[i])).Append('"');
            }
            sb.Append(']');
        }

        sb.Append(",\"additionalProperties\":false}");
        return sb.ToString();
    }

    private static void AppendJsonSchemaForParameter(StringBuilder sb, ParameterInfo param)
    {
        sb.Append("\"type\":\"").Append(GetJsonSchemaType(param.Type)).Append('"');

        if (!string.IsNullOrWhiteSpace(param.Description) && !param.HasDynamicDescription)
        {
            sb.Append(",\"description\":\"").Append(EscapeJsonString(param.Description)).Append('"');
        }
    }

    private static string GetJsonSchemaType(string type)
    {
        var normalized = NormalizeTypeName(type);

        if (normalized.EndsWith("[]", StringComparison.Ordinal) ||
            normalized.StartsWith("System.Collections.Generic.IEnumerable<", StringComparison.Ordinal) ||
            normalized.StartsWith("IEnumerable<", StringComparison.Ordinal) ||
            normalized.StartsWith("System.Collections.Generic.IReadOnlyList<", StringComparison.Ordinal) ||
            normalized.StartsWith("IReadOnlyList<", StringComparison.Ordinal) ||
            normalized.StartsWith("System.Collections.Generic.List<", StringComparison.Ordinal) ||
            normalized.StartsWith("List<", StringComparison.Ordinal))
        {
            return "array";
        }

        return normalized switch
        {
            "string" or "System.String" or "char" or "System.Char" or "System.Guid" or "System.DateTime" or "System.DateOnly" or "System.TimeOnly" => "string",
            "bool" or "System.Boolean" => "boolean",
            "byte" or "sbyte" or "short" or "ushort" or "int" or "uint" or "long" or "ulong"
                or "System.Byte" or "System.SByte" or "System.Int16" or "System.UInt16" or "System.Int32" or "System.UInt32" or "System.Int64" or "System.UInt64" => "integer",
            "float" or "double" or "decimal" or "System.Single" or "System.Double" or "System.Decimal" => "number",
            _ => "object"
        };
    }

    private static string NormalizeTypeName(string type)
    {
        var normalized = type.Trim();

        if (normalized.EndsWith("?", StringComparison.Ordinal))
            normalized = normalized.Substring(0, normalized.Length - 1);

        const string nullablePrefix = "System.Nullable<";
        if (normalized.StartsWith(nullablePrefix, StringComparison.Ordinal) && normalized.EndsWith(">", StringComparison.Ordinal))
            normalized = normalized.Substring(nullablePrefix.Length, normalized.Length - nullablePrefix.Length - 1);

        return normalized;
    }

    private static string GetDeclaredResultType(string returnType)
    {
        var normalized = returnType.Trim();
        if (TryUnwrapGeneric(normalized, "System.Threading.Tasks.Task", out var taskResult) ||
            TryUnwrapGeneric(normalized, "Task", out taskResult) ||
            TryUnwrapGeneric(normalized, "System.Threading.Tasks.ValueTask", out taskResult) ||
            TryUnwrapGeneric(normalized, "ValueTask", out taskResult))
        {
            return taskResult;
        }

        return normalized;
    }

    private static bool TryUnwrapGeneric(string typeName, string genericTypeName, out string typeArgument)
    {
        var prefix = genericTypeName + "<";
        if (typeName.StartsWith(prefix, StringComparison.Ordinal) && typeName.EndsWith(">", StringComparison.Ordinal))
        {
            typeArgument = typeName.Substring(prefix.Length, typeName.Length - prefix.Length - 1);
            return true;
        }

        typeArgument = string.Empty;
        return false;
    }

    private static string[] SplitCommaSeparated(string value) =>
        string.IsNullOrWhiteSpace(value)
            ? Array.Empty<string>()
            : value.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(item => item.Trim())
                .Where(item => item.Length > 0)
                .ToArray();

    private static string GenerateStringArrayLiteral(string[] values)
    {
        if (values.Length == 0)
            return "global::System.Array.Empty<string>()";

        return $"new string[] {{ {string.Join(", ", values.Select(v => $"\"{EscapeStringLiteral(v)}\""))} }}";
    }

    private static void AppendStringArrayMetadata(StringBuilder options, string key, string value)
    {
        var values = SplitCommaSeparated(value);
        if (values.Length > 0)
            options.AppendLine($"                    [\"{key}\"] = {GenerateStringArrayLiteral(values)},");
    }

    private static void AppendToggleMetadata(StringBuilder options, string key, string value)
    {
        var boolText = ToggleToBoolLiteral(value);
        if (boolText is not null)
            options.AppendLine($"                    [\"{key}\"] = {boolText},");
    }

    private static void AddStringArrayProperty(Dictionary<string, object> props, string key, string value)
    {
        var values = SplitCommaSeparated(value);
        if (values.Length > 0)
            props[key] = values;
    }

    private static void AddToggleProperty(Dictionary<string, object> props, string key, string value)
    {
        var boolValue = ToggleToBool(value);
        if (boolValue is bool set)
            props[key] = set;
    }

    private static bool IsInherit(string value) =>
        string.IsNullOrWhiteSpace(value) ||
        string.Equals(value, "Inherit", StringComparison.OrdinalIgnoreCase);

    private static string? ToggleToBoolLiteral(string value) =>
        ToggleToBool(value) switch
        {
            true => "true",
            false => "false",
            _ => null
        };

    private static bool? ToggleToBool(string value)
    {
        if (string.Equals(value, "Enabled", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(value, "True", StringComparison.OrdinalIgnoreCase))
            return true;

        if (string.Equals(value, "Disabled", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(value, "False", StringComparison.OrdinalIgnoreCase))
            return false;

        return null;
    }

    private static string EscapeStringLiteral(string value) =>
        value
            .Replace("\\", "\\\\")
            .Replace("\"", "\\\"")
            .Replace("\r", "\\r")
            .Replace("\n", "\\n")
            .Replace("\t", "\\t");

    private static string EscapeJsonString(string value) =>
        value
            .Replace("\\", "\\\\")
            .Replace("\"", "\\\"")
            .Replace("\r", "\\r")
            .Replace("\n", "\\n")
            .Replace("\t", "\\t");

    /// <summary>
    /// Gets additional metadata properties for this function.
    /// </summary>
    /// <returns>Dictionary of metadata key-value pairs.</returns>
    public override Dictionary<string, object> GetAdditionalProperties()
    {
        var props = base.GetAdditionalProperties();
        props["IsContainer"] = false;
        props["RequiresPermission"] = RequiresPermission;
        if (RequiredPermissions.Any())
            props["RequiredPermissions"] = RequiredPermissions.ToArray();

        return props;
    }

    /// <summary>
    /// Generates context resolvers for functions, including parameter-specific resolvers.
    /// Overrides base implementation to add parameter description and conditional resolvers.
    /// </summary>
    public override string GenerateContextResolvers()
    {
        var sb = new StringBuilder();

        // Get base resolvers (function-level description and conditional)
        var baseResolvers = base.GenerateContextResolvers();
        if (!string.IsNullOrEmpty(baseResolvers))
        {
            sb.Append(baseResolvers);
        }

        // Generate parameter description resolvers
        if (HasTypedMetadata)
        {
            foreach (var param in Parameters.Where(p => p.IsModelFacing && p.HasDynamicDescription))
            {
                // Convert {metadata.PropertyName} templates to {typedMetadata.PropertyName} for string interpolation
                var interpolatedDescription = param.Description.Replace("{metadata.", "{typedMetadata.");

                sb.AppendLine($"    private static string Resolve{Name}Parameter{param.Name}Description(IToolMetadata? context)");
                sb.AppendLine("    {");
                sb.AppendLine("        if (context == null) return string.Empty;");
                sb.AppendLine($"        if (context is not {ContextTypeName} typedMetadata) return string.Empty;");
                sb.AppendLine($"        return $@\"{interpolatedDescription.Replace("\"", "\"\"")}\";");
                sb.AppendLine("    }");
                sb.AppendLine();
            }

            // Generate parameter conditional evaluators
            foreach (var param in Parameters.Where(p => p.IsModelFacing && p.IsConditional))
            {
                // Ensure all property names in the expression are properly prefixed with "typedMetadata."
                var expression = param.ConditionalExpression;
                if (!string.IsNullOrEmpty(expression))
                {
                    expression = System.Text.RegularExpressions.Regex.Replace(
                        expression,
                        @"(?<!typedMetadata\.)(?<!metadata\.)(\b[A-Z][a-zA-Z0-9_]*\b)",
                        "typedMetadata.$1"
                    );
                }

                sb.AppendLine($"    private static bool Evaluate{Name}Parameter{param.Name}Condition(IToolMetadata? context)");
                sb.AppendLine("    {");
                sb.AppendLine("        if (context == null) return true;");
                sb.AppendLine($"        if (context is not {ContextTypeName} typedMetadata) return false;");
                sb.AppendLine($"        return {expression};");
                sb.AppendLine("    }");
                sb.AppendLine();
            }
        }

        return sb.ToString();
    }

    // ========== Helper Methods ==========

    /// <summary>
    /// Formats a property value for code generation.
    /// </summary>
    private string FormatPropertyValue(object value)
    {
        return value switch
        {
            string s => $"\"{s.Replace("\"", "\"\"")}\"",
            bool b => b.ToString().ToLower(),
            int i => i.ToString(),
            string[] arr => $"new[] {{ {string.Join(", ", arr.Select(s => $"\"{s}\""))} }}",
            _ => value.ToString() ?? "null"
        };
    }
}

/// <summary>
/// Information about a function parameter discovered during source generation.
/// This is the same structure as in ToolHarnessInfo.cs but duplicated here for Phase 1.
/// In Phase 2, we'll consolidate to use a single shared ParameterInfo class.
/// </summary>
internal class ParameterInfo
{
    public string Name { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;
    public string MetadataTypeName { get; set; } = "object";
    public FunctionParameterKind Kind { get; set; } = FunctionParameterKind.ModelFacing;
    public string Description { get; set; } = string.Empty;
    public bool IsEnum { get; set; }
    public bool HasDefaultValue { get; set; }
    public string? DefaultValue { get; set; }

    /// <summary>
    /// Conditional expression for parameter visibility (null if always visible).
    /// </summary>
    public string? ConditionalExpression { get; set; }

    /// <summary>
    /// Whether this parameter has conditional visibility.
    /// </summary>
    public bool IsConditional => !string.IsNullOrEmpty(ConditionalExpression);

    /// <summary>
    /// Whether this parameter has dynamic description templates.
    /// </summary>
    public bool HasDynamicDescription => Description.Contains("{metadata.");

    /// <summary>
    /// Whether this parameter should be serialized (not special framework types).
    /// </summary>
    public bool IsSerializable => IsModelFacing;

    public bool IsModelFacing => Kind == FunctionParameterKind.ModelFacing;

    /// <summary>
    /// Whether this parameter is nullable (simple heuristic).
    /// </summary>
    public bool IsNullable => Type.EndsWith("?");
}

/// <summary>
/// Validation data for functions that need validation after source generation.
/// </summary>
internal class ValidationData
{
    public Microsoft.CodeAnalysis.CSharp.Syntax.MethodDeclarationSyntax Method { get; set; } = null!;
    public Microsoft.CodeAnalysis.SemanticModel SemanticModel { get; set; } = null!;
    public bool NeedsValidation { get; set; }
}
