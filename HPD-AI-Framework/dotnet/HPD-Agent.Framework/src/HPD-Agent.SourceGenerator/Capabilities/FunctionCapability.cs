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
    /// Whether the function is marked with [Sandboxable] attribute.
    /// Functions with this attribute should be executed in a sandbox.
    /// </summary>
    public bool IsSandboxable { get; set; }

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
    /// <param name="parent">The parent Harness that contains this function (HarnessInfo).</param>
    /// <returns>The generated registration code as a string.</returns>
    public override string GenerateRegistrationCode(object parent)
    {
        var Harness = (HarnessInfo)parent;

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

        string schemaProviderCode = "() => { ";
        if (relevantParams.Any())
        {
            // Use the generated model-facing DTO as the schema source so runtime parameters stay hidden.
            schemaProviderCode += $@"
    var options = new global::Microsoft.Extensions.AI.AIJsonSchemaCreateOptions {{ IncludeSchemaKeyword = false }};
    var serializerOptions = new global::System.Text.Json.JsonSerializerOptions(global::Microsoft.Extensions.AI.AIJsonUtilities.DefaultOptions);
    serializerOptions.TypeInfoResolverChain.Add(global::HPDJsonContext.Default);
    serializerOptions.MakeReadOnly();
    return global::Microsoft.Extensions.AI.AIJsonUtilities.CreateJsonSchema(
        typeof({dtoName}),
        serializerOptions: serializerOptions,
        inferenceOptions: options
    );";
        }
        else
        {
            // Empty schema for functions with no parameters
            schemaProviderCode += @"
    var options = new global::Microsoft.Extensions.AI.AIJsonSchemaCreateOptions { IncludeSchemaKeyword = false };
    return global::Microsoft.Extensions.AI.AIJsonUtilities.CreateJsonSchema(
        null,
        serializerOptions: global::Microsoft.Extensions.AI.AIJsonUtilities.DefaultOptions,
        inferenceOptions: options
    );";
        }
        schemaProviderCode += " }";

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

        // ALWAYS add ParentHarness metadata (enables HarnessReferences to work with any Harness)
        // Note: Harneses without [Collapse] remain "always visible" by default
        // Skills can use HarnessReferences to Collapse them on-demand
        options.AppendLine("                AdditionalProperties = new Dictionary<string, object>");
        options.AppendLine("                {");
        options.AppendLine($"                    [\"ParentHarness\"] = \"{Harness.Name}\",");

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

    /// <summary>
    /// Gets additional metadata properties for this function.
    /// </summary>
    /// <returns>Dictionary of metadata key-value pairs.</returns>
    public override Dictionary<string, object> GetAdditionalProperties()
    {
        var props = base.GetAdditionalProperties();
        props["IsContainer"] = false;
        props["RequiresPermission"] = RequiresPermission;
        props["IsSandboxable"] = IsSandboxable;

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
/// This is the same structure as in HarnessInfo.cs but duplicated here for Phase 1.
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
