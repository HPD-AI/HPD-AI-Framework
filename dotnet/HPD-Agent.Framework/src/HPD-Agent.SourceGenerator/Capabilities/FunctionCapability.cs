using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Collections.Immutable;
    using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using HPD.Agent.SourceGenerator.Contracts;

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

    /// <summary>Explicit stable permission scope declared by the function.</summary>
    public string? PermissionScope { get; set; }

    /// <summary>Stable descriptor ID of the function permission policy.</summary>
    public string? PermissionPolicyDescriptorId { get; set; }

    /// <summary>Semantic policy type used to emit its presentation descriptor.</summary>
    public ITypeSymbol? PermissionPolicyType { get; set; }

    /// <summary>Stable descriptor ID of the function permission interaction.</summary>
    public string? PermissionInteractionDescriptorId { get; set; }

    /// <summary>Semantic interaction type used to emit its generated activation factory.</summary>
    public ITypeSymbol? PermissionInteractionType { get; set; }

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
    /// Defines whether this function runs synchronously, in the background, or lets the model choose per call.
    /// </summary>
    public string InvocationModePolicy { get; set; } = "SynchronousOnly";

    /// <summary>
    /// Defines whether HPD runtime or the function body handles invocation mode.
    /// </summary>
    public string InvocationModeHandling { get; set; } = "Runtime";

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

    /// <summary>Stable IDs of skills that may reveal this function.</summary>
    public List<string> ParentSkillIds { get; set; } = new();

    // ========== Code Generation ==========

    /// <summary>
    /// Generates the registration code for this function.
    /// Creates HPDAIFunctionFactory.Create(...) call with all necessary metadata.
    ///
    /// </summary>
    /// <param name="parent">The parent ToolHarness that contains this function (ToolHarnessInfo).</param>
    /// <returns>The generated registration code as a string.</returns>
    public override string GenerateRegistrationCode(object parent)
    {
        var ToolHarness = (ToolHarnessInfo)parent;
        var capabilityParents = ParentSkillIds.ToList();
        if (ToolHarness.IsCollapsed)
        {
            var owner = string.IsNullOrEmpty(ToolHarness.Namespace)
                ? ToolHarness.ClassName
                : $"{ToolHarness.Namespace}.{ToolHarness.ClassName}";
            capabilityParents.Add($"generated:{owner}:harness");
        }

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
                _ => $"args.{EscapeIdentifier(p.Name)}"
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
                var args = arguments.GetBoundArguments<{dtoName}>();
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
        if (RequiresPermission)
            options.AppendLine($"                FunctionPermission = {CreatePermissionDeclaration(true, PermissionScope ?? GeneratedScope(FunctionName), PermissionPolicyDescriptorId, PermissionInteractionDescriptorId, "FunctionAttribute")},");
        options.AppendLine($"                PermissionDescriptors = {GeneratePermissionDescriptorsCode(relevantParams)},");
        options.AppendLine($"                InvocationModePolicy = global::HPD.Agent.AgentInvocationModePolicy.{InvocationModePolicy},");
        options.AppendLine($"                InvocationModeHandling = global::HPD.Agent.AgentInvocationModeHandling.{InvocationModeHandling},");
        var operationContract = GenerateOperationContractCode(relevantParams);
        if (operationContract is not null)
        {
            options.AppendLine($"                VerifiedActionComposition = new global::HPD.Agent.VerifiedAIFunctionActionComposition(((global::System.Func<global::System.Text.Json.JsonElement>)({schemaProviderCode}))(), {operationContract}, Bind{Name}Arguments),");
        }
        options.AppendLine($"                ArgumentBinder = Bind{Name}Arguments,");
        options.AppendLine($"                SchemaProvider = {schemaProviderCode},");
        options.AppendLine("                SerializerOptions = serialization?.SerializerOptions,");
        if (!isVoidReturn)
        {
            options.AppendLine($"                ResultType = typeof({GetDeclaredResultType(ReturnType)}),");
        }
        options.AppendLine($"                ParameterDescriptions = {GenerateParameterDescriptions()},");

        options.AppendLine("                AdditionalProperties = new Dictionary<string, object>");
        options.AppendLine("                {");
        options.AppendLine("                    [HPDCapabilityMetadata.AdditionalPropertiesKey] = new HPDCapabilityMetadata");
        options.AppendLine("                    {");
        options.AppendLine($"                        Id = CapabilityId.Create(@\"generated:{ToolHarness.ClassName}.{FunctionName}\"),");
        options.AppendLine("                        Kind = HPDCapabilityKind.Function,");
        options.AppendLine($"                        DeclarationMemberName = @\"{Name.Replace("\"", "\"\"")}\",");
        options.AppendLine($"                        ParentContainerIds = System.Collections.Immutable.ImmutableArray.Create<CapabilityId>({string.Join(", ", capabilityParents.Select(id => $"CapabilityId.Create(@\"{id.Replace("\"", "\"\"")}\")"))})");
        options.AppendLine("                    },");
        options.AppendLine($"                    [\"ToolHarnessName\"] = @\"{ToolHarness.EffectiveName.Replace("\"", "\"\"")}\",");

        // Add Kind if it's an output tool (structured output)
        if (Kind == "Output")
        {
            options.AppendLine($"                    [\"Kind\"] = \"Output\",");
        }

        options.AppendLine($"                    [\"InvocationModePolicy\"] = \"{InvocationModePolicy}\",");
        options.AppendLine($"                    [\"InvocationModeHandling\"] = \"{InvocationModeHandling}\"");
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
        var parameters = relevantParams.Select(param => new AIFunctionContractParameter(
            param.Symbol!,
            param.JsonName,
            ComposeActionContract(param.Contract!),
            IsRequired: !param.HasDefaultValue)).ToImmutableArray();
        return AICanonicalSchemaEmitter.Emit(new AIFunctionMethodContract(parameters));
    }

    private AIContractNode ComposeActionContract(AIContractNode contract)
    {
        if (contract is not UnionContractNode union || !union.Cases.Any(unionCase =>
                unionCase.ConcreteType.GetAttributes().Any(data => data.AttributeClass?.Name == "AIFunctionActionAttribute")))
            return contract;
        var cases = union.Cases.Select(unionCase =>
        {
            var attribute = unionCase.ConcreteType.GetAttributes().SingleOrDefault(data =>
                data.AttributeClass?.Name == "AIFunctionActionAttribute")
                ?? throw new InvalidOperationException($"Action type '{unionCase.ConcreteType.Name}' requires AIFunctionActionAttribute.");
            var policy = ResolveActionOverride(attribute, "InvocationModePolicy", InvocationModePolicy,
                "SynchronousOnly", "BackgroundOnly", "ModelChoice");
            var handling = ResolveActionOverride(attribute, "InvocationModeHandling", InvocationModeHandling,
                "Runtime", "ToolBody");
            return unionCase with { InvocationModePolicy = policy, InvocationModeHandling = handling };
        }).ToImmutableArray();
        return union with { Cases = cases };
    }

    private string? GenerateOperationContractCode(List<ParameterInfo> relevantParams)
    {
        var candidates = relevantParams
            .Where(parameter => parameter.Contract is UnionContractNode union && union.Cases.Any(unionCase =>
                unionCase.ConcreteType.GetAttributes().Any(data => data.AttributeClass?.Name == "AIFunctionActionAttribute")))
            .ToArray();
        if (candidates.Length == 0) return null;
        if (candidates.Length != 1)
            throw new InvalidOperationException($"Function '{FunctionName}' has more than one direct action union.");
        var parameter = candidates[0];
        var union = (UnionContractNode)parameter.Contract!;
        var entries = new List<string>();
        foreach (var unionCase in union.Cases)
        {
            var attribute = unionCase.ConcreteType.GetAttributes().SingleOrDefault(data =>
                data.AttributeClass?.Name == "AIFunctionActionAttribute");
            if (attribute is null)
                throw new InvalidOperationException($"Action type '{unionCase.ConcreteType.Name}' requires AIFunctionActionAttribute.");
            var declared = attribute.ConstructorArguments.FirstOrDefault().Value as string;
            if (!string.Equals(declared, unionCase.Discriminator, StringComparison.Ordinal))
                throw new InvalidOperationException($"Action declaration '{declared}' does not match discriminator '{unionCase.Discriminator}'.");
            var policy = ResolveActionOverride(attribute, "InvocationModePolicy", InvocationModePolicy,
                "SynchronousOnly", "BackgroundOnly", "ModelChoice");
            var handling = ResolveActionOverride(attribute, "InvocationModeHandling", InvocationModeHandling,
                "Runtime", "ToolBody");
            var permission = ResolveActionPermissionOverride(attribute, unionCase.Discriminator);
            entries.Add($"[\"{Escape(unionCase.Discriminator)}\"] = new global::HPD.Agent.AIFunctionActionPolicy {{ InvocationModePolicy = global::HPD.Agent.AgentInvocationModePolicy.{policy}, InvocationModeHandling = global::HPD.Agent.AgentInvocationModeHandling.{handling}, Permission = {permission} }}");
        }
        return $"new global::HPD.Agent.AIFunctionOperationContract {{ ActionArgumentName = \"{Escape(parameter.JsonName)}\", Discriminator = \"{Escape(union.DiscriminatorPropertyName)}\", Actions = new global::System.Collections.Generic.Dictionary<string, global::HPD.Agent.AIFunctionActionPolicy>(global::System.StringComparer.Ordinal) {{ {string.Join(", ", entries)} }} }}";
    }

    private string ResolveActionPermissionOverride(AttributeData attribute, string action)
    {
        var permission = attribute.NamedArguments.FirstOrDefault(pair => pair.Key == "Permission");
        var scope = attribute.NamedArguments.FirstOrDefault(pair => pair.Key == "PermissionScope");
        var policy = attribute.NamedArguments.FirstOrDefault(pair => pair.Key == "PermissionPolicy");
        var interaction = attribute.NamedArguments.FirstOrDefault(pair => pair.Key == "PermissionInteraction");
        var hasOverride = permission.Key is not null || scope.Key is not null ||
            policy.Key is not null || interaction.Key is not null;
        if (!hasOverride)
            return CreatePermissionDeclaration(
                RequiresPermission,
                PermissionScope ?? GeneratedScope(FunctionName, action),
                PermissionPolicyDescriptorId,
                PermissionInteractionDescriptorId,
                RequiresPermission ? "FunctionAttribute" : "FrameworkDefault");
        var numeric = permission.Key is null || permission.Value.Value is null
            ? 1
            : Convert.ToInt32(permission.Value.Value, System.Globalization.CultureInfo.InvariantCulture);
        var required = numeric switch
        {
            0 => true,
            1 => true,
            2 => false,
            var value => throw new InvalidOperationException($"Unsupported RequiresPermission value '{value}'.")
        };
        return CreatePermissionDeclaration(
            required,
            scope.Value.Value as string ?? GeneratedScope(FunctionName, action),
            (policy.Value.Value as ITypeSymbol)?.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
            (interaction.Value.Value as ITypeSymbol)?.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
            "ActionOverride");
    }

    private static string GeneratedScope(string function, string? action = null) => action is null
        ? $"function/{Uri.EscapeDataString(function)}"
        : $"function/{Uri.EscapeDataString(function)}/action/{Uri.EscapeDataString(action)}";

    private static string CreatePermissionDeclaration(
        bool required,
        string scope,
        string? policy,
        string? interaction,
        string source) =>
        $"new global::HPD.Agent.AIFunctionPermissionDeclaration {{ RequiresPermission = {required.ToString().ToLowerInvariant()}, Scope = \"{Escape(scope)}\", PolicyDescriptorId = {Literal(policy)}, InteractionDescriptorId = {Literal(interaction)}, Source = global::HPD.Agent.PermissionDeclarationSource.{source} }}";

    private static string Literal(string? value) => value is null ? "null" : $"\"{Escape(value)}\"";

    private string GeneratePermissionDescriptorsCode(IReadOnlyList<ParameterInfo> parameters)
    {
        var descriptors = new Dictionary<string, (bool Policy, bool Interaction, ITypeSymbol? PolicyType, ITypeSymbol? InteractionType)>(StringComparer.Ordinal);
        Add(PermissionPolicyDescriptorId, policy: true, PermissionPolicyType);
        Add(PermissionInteractionDescriptorId, policy: false, PermissionInteractionType);
        foreach (var union in parameters.Select(static parameter => parameter.Contract).OfType<UnionContractNode>())
        {
            foreach (var unionCase in union.Cases)
            {
                var attribute = unionCase.ConcreteType.GetAttributes().SingleOrDefault(data =>
                    data.AttributeClass?.Name == "AIFunctionActionAttribute");
                if (attribute is null) continue;
                var actionPolicyType = GetNamedType(attribute, "PermissionPolicy");
                Add(GetNamedTypeId(attribute, "PermissionPolicy"), policy: true, actionPolicyType);
                Add(GetNamedTypeId(attribute, "PermissionInteraction"), policy: false,
                    GetNamedType(attribute, "PermissionInteraction"));
            }
        }
        var entries = descriptors.Select(pair =>
        {
            var policyFactory = pair.Value.Policy
                ? CreatePolicyFactory(pair.Key, pair.Value.PolicyType)
                : string.Empty;
            var interactionFactory = pair.Value.Interaction
                ? CreateInteractionFactory(pair.Key, pair.Value.InteractionType)
                : string.Empty;
            var interactionEvents = CreateInteractionEventContract(pair.Value.InteractionType);
            var presentation = CreatePresentationDescriptor(pair.Value.PolicyType);
            return $"[\"{Escape(pair.Key)}\"] = new global::HPD.Agent.Permissions.AIFunctionPermissionDescriptor {{ DescriptorId = \"{Escape(pair.Key)}\", {policyFactory} {interactionFactory} {interactionEvents} {presentation} }}";
        });
        return $"new global::System.Collections.Generic.Dictionary<string, global::HPD.Agent.Permissions.AIFunctionPermissionDescriptor>(global::System.StringComparer.Ordinal) {{ {string.Join(", ", entries)} }}";

        void Add(string? id, bool policy, ITypeSymbol? serviceType = null)
        {
            if (id is null) return;
            descriptors.TryGetValue(id, out var current);
            descriptors[id] = policy
                ? (true, current.Interaction, serviceType ?? current.PolicyType, current.InteractionType)
                : (current.Policy, true, current.PolicyType, serviceType ?? current.InteractionType);
        }
    }

    private static string CreatePolicyFactory(string typeName, ITypeSymbol? policyType)
    {
        if (policyType is INamedTypeSymbol named && named.InstanceConstructors.Any(static constructor =>
                constructor.Parameters.Length == 0 &&
                constructor.DeclaredAccessibility is Accessibility.Public or Accessibility.Internal))
        {
            return $"PolicyFactory = static services => new {typeName}(),";
        }

        return $"PolicyFactory = static services => (global::HPD.Agent.Permissions.IPermissionPolicy)(services.GetService(typeof({typeName})) ?? throw new global::System.InvalidOperationException(\"Permission policy service '{Escape(typeName)}' is not registered.\")),";
    }

    private static string CreateInteractionFactory(string typeName, ITypeSymbol? interactionType)
    {
        if (interactionType is INamedTypeSymbol named && named.InstanceConstructors.Any(static constructor =>
                constructor.Parameters.Length == 0 &&
                constructor.DeclaredAccessibility is Accessibility.Public or Accessibility.Internal))
        {
            return $"InteractionFactory = static services => new {typeName}(),";
        }

        return $"InteractionFactory = static services => (global::HPD.Agent.Permissions.IPermissionInteraction)(services.GetService(typeof({typeName})) ?? throw new global::System.InvalidOperationException(\"Permission interaction service '{Escape(typeName)}' is not registered.\")),";
    }

    private static string CreateInteractionEventContract(ITypeSymbol? interactionType)
    {
        if (interactionType is not INamedTypeSymbol named) return string.Empty;
        var contract = named.AllInterfaces.FirstOrDefault(static candidate =>
            candidate.IsGenericType && candidate.Name == "IPermissionInteractionEventContract" &&
            candidate.TypeArguments.Length == 2);
        return contract is null
            ? string.Empty
            : $"RequestEventType = typeof({contract.TypeArguments[0].ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)}), ResponseEventType = typeof({contract.TypeArguments[1].ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)}),";
    }

    private static ITypeSymbol? GetNamedType(AttributeData attribute, string name) =>
        attribute.NamedArguments.FirstOrDefault(pair => pair.Key == name).Value.Value as ITypeSymbol;

    private static string CreatePresentationDescriptor(ITypeSymbol? policyType)
    {
        if (policyType is not INamedTypeSymbol named) return string.Empty;
        INamedTypeSymbol? current = named;
        while (current is not null)
        {
            if (current.IsGenericType && current.Name == "PermissionPolicy" && current.TypeArguments.Length == 1)
            {
                var presentationType = current.TypeArguments[0];
                var attribute = presentationType.GetAttributes().FirstOrDefault(static data =>
                    data.AttributeClass?.Name == "PermissionPresentationAttribute");
                var id = attribute?.ConstructorArguments.FirstOrDefault().Value as string;
                if (string.IsNullOrWhiteSpace(id)) return string.Empty;
                var typeName = presentationType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
                return $"Presentation = new global::HPD.Agent.Permissions.PermissionPresentationDescriptor {{ PresentationId = \"{Escape(id)}\", PresentationType = typeof({typeName}), TypeInfo = (serialization?.SerializerOptions ?? global::Microsoft.Extensions.AI.AIJsonUtilities.DefaultOptions).GetTypeInfo(typeof({typeName})) ?? throw new global::System.InvalidOperationException(\"Source-generated JSON metadata for permission presentation '{Escape(id)}' is required.\"), Serialize = value => global::System.Text.Json.JsonSerializer.SerializeToElement(value, (serialization?.SerializerOptions ?? global::Microsoft.Extensions.AI.AIJsonUtilities.DefaultOptions).GetTypeInfo(typeof({typeName})) ?? throw new global::System.InvalidOperationException(\"Source-generated JSON metadata for permission presentation '{Escape(id)}' is required.\")) }},";
            }
            current = current.BaseType;
        }
        return string.Empty;
    }

    private static string? GetNamedTypeId(AttributeData attribute, string name) =>
        (attribute.NamedArguments.FirstOrDefault(pair => pair.Key == name).Value.Value as ITypeSymbol)?
            .ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);

    private static string ResolveActionOverride(
        AttributeData attribute, string name, string inherited, params string[] values)
    {
        var argument = attribute.NamedArguments.FirstOrDefault(pair => pair.Key == name);
        if (argument.Key is null || argument.Value.Value is null) return inherited;
        var numeric = Convert.ToInt32(argument.Value.Value, System.Globalization.CultureInfo.InvariantCulture);
        return numeric == 0 ? inherited : numeric <= values.Length ? values[numeric - 1] :
            throw new InvalidOperationException($"Unsupported {name} value '{numeric}'.");
    }

    private static string Escape(string value) =>
        value.Replace("\\", "\\\\").Replace("\"", "\\\"");

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

    private static string EscapeIdentifier(string value) =>
        SyntaxFacts.GetKeywordKind(value) is SyntaxKind.None ? value : "@" + value;

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
/// Carries the Roslyn symbol and analyzed semantic contract used by every generated surface.
/// </summary>
internal class ParameterInfo
{
    /// <summary>Gets or sets the Roslyn parameter symbol.</summary>
    public IParameterSymbol? Symbol { get; set; }

    /// <summary>Gets or sets the recursively analyzed model-facing contract.</summary>
    public AIContractNode? Contract { get; set; }
    public string Name { get; set; } = string.Empty;
    /// <summary>Gets or sets the analyzed model-facing JSON argument name.</summary>
    public string JsonName { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;
    public string MetadataTypeName { get; set; } = "object";
    public FunctionParameterKind Kind { get; set; } = FunctionParameterKind.ModelFacing;
    public string Description { get; set; } = string.Empty;
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
    /// Whether this parameter contract permits explicit JSON null.
    /// </summary>
    public bool IsNullable => Contract?.AllowsNull == true;
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
