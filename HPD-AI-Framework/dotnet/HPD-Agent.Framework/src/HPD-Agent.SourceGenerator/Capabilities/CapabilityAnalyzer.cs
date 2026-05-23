using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System.Collections.Generic;
using System.Linq;

namespace HPD.Agent.SourceGenerator.Capabilities;

// ========== MultiAgent Diagnostics ==========
// These diagnostics are specific to [MultiAgent] capability analysis

/// <summary>
/// Result of capability analysis containing the capability (if valid) and any diagnostics.
/// </summary>
internal readonly struct CapabilityAnalysisResult
{
    public ICapability? Capability { get; }
    public IReadOnlyList<Diagnostic> Diagnostics { get; }

    public CapabilityAnalysisResult(ICapability? capability, IReadOnlyList<Diagnostic>? diagnostics = null)
    {
        Capability = capability;
        Diagnostics = diagnostics ?? System.Array.Empty<Diagnostic>();
    }

    public static CapabilityAnalysisResult None => new(null);
    public static CapabilityAnalysisResult Success(ICapability capability) => new(capability);
    public static CapabilityAnalysisResult WithDiagnostic(Diagnostic diagnostic) => new(null, new[] { diagnostic });
    public static CapabilityAnalysisResult WithDiagnostics(ICapability? capability, params Diagnostic[] diagnostics) => new(capability, diagnostics);
}

internal static class MultiAgentDiagnostics
{
    /// <summary>
    /// HPDAG0201: [MultiAgent] method has invalid return type.
    /// Must return AgentWorkflowInstance or Task&lt;AgentWorkflowInstance&gt;.
    /// </summary>
    public static readonly DiagnosticDescriptor InvalidReturnType = new(
        id: "HPDAG0201",
        title: "Invalid MultiAgent return type",
        messageFormat: "[MultiAgent] method '{0}' must return AgentWorkflowInstance or Task<AgentWorkflowInstance>, but returns '{1}'",
        category: "HPDAgent.SourceGenerator",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "Methods marked with [MultiAgent] must return AgentWorkflowInstance (sync) or Task<AgentWorkflowInstance> (async). " +
                    "The source generator needs this return type to generate the proper workflow execution wrapper.");

    /// <summary>
    /// HPDAG0202: [MultiAgent] method has conflicting capability attributes.
    /// Cannot combine with [AIFunction], [Skill], or [SubAgent].
    /// </summary>
    public static readonly DiagnosticDescriptor ConflictingAttributes = new(
        id: "HPDAG0202",
        title: "Conflicting capability attributes",
        messageFormat: "[MultiAgent] method '{0}' cannot be combined with [{1}]. Use only one capability attribute per method.",
        category: "HPDAgent.SourceGenerator",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "A method can only have one capability attribute. [MultiAgent] cannot be combined with [AIFunction], [Skill], or [SubAgent]. " +
                    "Each capability type has different execution semantics and return type requirements.");

    /// <summary>
    /// HPDAG0203: [MultiAgent] method has no description.
    /// A description helps the LLM understand when to use this workflow.
    /// </summary>
    public static readonly DiagnosticDescriptor MissingDescription = new(
        id: "HPDAG0203",
        title: "Missing MultiAgent description",
        messageFormat: "[MultiAgent] method '{0}' has no description. Consider adding one to help the LLM understand when to use this workflow.",
        category: "HPDAgent.SourceGenerator",
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "While not required, providing a description for [MultiAgent] methods helps the LLM understand when to invoke the workflow. " +
                    "Use [MultiAgent(\"description\")] or [AIDescription(\"description\")] to add one.");

    /// <summary>
    /// HPDAG0204: [MultiAgent] method has parameters.
    /// The source generator always calls the method with no arguments — parameters are not supported.
    /// </summary>
    public static readonly DiagnosticDescriptor HasParameters = new(
        id: "HPDAG0204",
        title: "MultiAgent method must not have parameters",
        messageFormat: "[MultiAgent] method '{0}' has parameters, which are not supported. The source generator calls this method with no arguments. Remove all parameters — input is provided via the workflow's input string.",
        category: "HPDAgent.SourceGenerator",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "The source generator wraps [MultiAgent] methods as AIFunctions and always invokes them with no arguments. " +
                    "Input to the workflow is passed via the AgentWorkflowInstance.ExecuteStreamingAsync/RunAsync input parameter, not method parameters.");
}

/// <summary>
/// Unified analyzer for all capability types (Functions, Skills, SubAgents, MultiAgents).
/// Provides attribute-based dispatch to the appropriate capability analyzer.
/// This replaces the fragmented analysis approach (inline functions, SkillAnalyzer, SubAgentAnalyzer).
/// </summary>
internal static class CapabilityAnalyzer
{
    /// <summary>
    /// Analyzes a method and returns the appropriate ICapability implementation
    /// based on which attribute it has ([AIFunction], [Skill], [SubAgent], or [MultiAgent]).
    ///
    /// This is the single entry point for all capability analysis, enabling polymorphic processing.
    /// </summary>
    /// <param name="method">The method declaration to analyze</param>
    /// <param name="semanticModel">The semantic model for symbol resolution</param>
    /// <param name="context">The generator syntax context (not used for diagnostics - use out parameter)</param>
    /// <param name="className">The name of the containing class</param>
    /// <param name="namespaceName">The namespace of the containing class</param>
    /// <returns>An ICapability instance, or null if the method is not a valid capability</returns>
    public static ICapability? AnalyzeMethod(
        MethodDeclarationSyntax method,
        SemanticModel semanticModel,
        GeneratorSyntaxContext context,
        string className,
        string namespaceName)
    {
        // Call the overload with diagnostics out parameter, but discard the diagnostics
        // This maintains backward compatibility
        return AnalyzeMethod(method, semanticModel, context, className, namespaceName, out _);
    }

    /// <summary>
    /// Analyzes a method and returns the appropriate ICapability implementation,
    /// also outputting any diagnostics generated during analysis.
    /// </summary>
    public static ICapability? AnalyzeMethod(
        MethodDeclarationSyntax method,
        SemanticModel semanticModel,
        GeneratorSyntaxContext context,
        string className,
        string namespaceName,
        out List<Diagnostic> diagnostics)
    {
        diagnostics = new List<Diagnostic>();

        // Must be public
        if (!method.Modifiers.Any(SyntaxKind.PublicKeyword))
            return null;

        var attrs = method.AttributeLists
            .SelectMany(al => al.Attributes)
            .ToList();

        // Dispatch based on attribute type (priority order: Skill > SubAgent > AIFunction)
        // This order matters because a method could theoretically have multiple attributes

        // 1. Check for [Skill] attribute
        if (HasAttribute(attrs, "Skill"))
        {
            return AnalyzeSkillCapability(method, attrs, semanticModel, context, className, namespaceName, diagnostics);
        }

        // 2. Check for [SubAgent] attribute
        if (HasAttribute(attrs, "SubAgent"))
        {
            return AnalyzeSubAgentCapability(method, attrs, semanticModel, context, className, namespaceName, diagnostics);
        }

        // 3. Check for [MultiAgent] attribute
        if (HasAttribute(attrs, "MultiAgent"))
        {
            // Check for conflicting attributes (HPDAG0202)
            var conflictingAttr = GetConflictingCapabilityAttribute(attrs, "MultiAgent");
            if (conflictingAttr != null)
            {
                diagnostics.Add(Diagnostic.Create(
                    MultiAgentDiagnostics.ConflictingAttributes,
                    method.Identifier.GetLocation(),
                    method.Identifier.ValueText,
                    conflictingAttr));
                return null;
            }

            return AnalyzeMultiAgentCapability(method, attrs, semanticModel, className, namespaceName, diagnostics);
        }

        // 4. Check for [MCPServer] attribute
        if (HasAttribute(attrs, "MCPServer"))
        {
            // Check for conflicting attributes (HPDAG0302)
            var conflictingAttr = GetConflictingCapabilityAttribute(attrs, "MCPServer");
            if (conflictingAttr != null)
            {
                diagnostics.Add(Diagnostic.Create(
                    MCPServerDiagnostics.ConflictingAttributes,
                    method.Identifier.GetLocation(),
                    method.Identifier.ValueText,
                    conflictingAttr));
                return null;
            }

            return AnalyzeMCPServerCapability(method, attrs, semanticModel, className, namespaceName, diagnostics);
        }

        // 5. Check for [OpenApi] attribute
        if (HasAttribute(attrs, "OpenApi"))
        {
            // Check for conflicting attributes (HPDAG0402)
            var conflictingAttr = GetConflictingCapabilityAttribute(attrs, "OpenApi");
            if (conflictingAttr != null)
            {
                diagnostics.Add(Diagnostic.Create(
                    OpenApiDiagnostics.ConflictingAttributes,
                    method.Identifier.GetLocation(),
                    method.Identifier.ValueText,
                    conflictingAttr));
                return null;
            }

            return AnalyzeOpenApiCapability(method, attrs, semanticModel, className, namespaceName, diagnostics);
        }

        // 6. Check for [AIFunction] attribute
        if (HasAttribute(attrs, "AIFunction"))
        {
            return AnalyzeFunctionCapability(method, attrs, semanticModel, context, className, namespaceName, diagnostics);
        }

        // Not a capability
        return null;
    }

    // ========== Skill Analysis ==========

    /// <summary>
    /// Analyzes a method with [Skill] attribute and creates a SkillCapability.
    /// Phase 5: Full implementation migrated from SkillAnalyzer.AnalyzeSkill().
    /// </summary>
    private static SkillCapability? AnalyzeSkillCapability(
        MethodDeclarationSyntax method,
        List<AttributeSyntax> attrs,
        SemanticModel semanticModel,
        GeneratorSyntaxContext context,
        string className,
        string namespaceName,
        List<Diagnostic> diagnostics)
    {
        // Validate return type is Skill
        var returnType = semanticModel.GetTypeInfo(method.ReturnType).Type;
        if (returnType == null || returnType.Name != "Skill")
        {
            // Invalid skill method - skip
            return null;
        }

        var methodName = method.Identifier.ValueText;
        System.Diagnostics.Debug.WriteLine($"[CapabilityAnalyzer] Analyzing Skill method: {methodName}");

        // Find SkillFactory.Create() invocation in method body
        var invocation = method.Body?.DescendantNodes()
            .OfType<InvocationExpressionSyntax>()
            .FirstOrDefault(inv => IsSkillFactoryCreate(inv, semanticModel));

        if (invocation == null)
        {
            // Check arrow expression body: => SkillFactory.Create(...)
            invocation = method.ExpressionBody?.DescendantNodes()
                .OfType<InvocationExpressionSyntax>()
                .FirstOrDefault(inv => IsSkillFactoryCreate(inv, semanticModel));
        }

        if (invocation == null)
        {
            System.Diagnostics.Debug.WriteLine($"[CapabilityAnalyzer] No SkillFactory.Create() found in {methodName}");
            return null;
        }

        System.Diagnostics.Debug.WriteLine($"[CapabilityAnalyzer] Found SkillFactory.Create() in {methodName}");

        var arguments = invocation.ArgumentList.Arguments;

        // Minimum 2 arguments: name, description (functionResult/systemPrompt are optional but one is required)
        if (arguments.Count < 2)
        {
            return null;
        }

        // Extract required positional arguments (name, description)
        var name = ExtractStringLiteral(arguments[0].Expression, semanticModel);
        var description = ExtractStringLiteral(arguments[1].Expression, semanticModel);

        // Extract dual-context instructions (functionResult and systemPrompt)
        // These can be positional (args 2,3) or named
        string? functionResult = null;
        string? systemPrompt = null;
        SkillOptionsInfo? options = null;
        int referencesStartIndex = 2;

        // Build a dictionary of named arguments for easy lookup
        var namedArgs = arguments
            .Where(a => a.NameColon != null)
            .ToDictionary(
                a => a.NameColon!.Name.Identifier.ValueText,
                a => a);

        // Extract functionResult (named or positional at index 2)
        if (namedArgs.TryGetValue("functionResult", out var funcResultArg))
        {
            functionResult = ExtractStringLiteral(funcResultArg.Expression, semanticModel);
        }
        else if (arguments.Count > 2 && arguments[2].NameColon == null)
        {
            // Legacy: positional argument at index 2 (was "instructions")
            functionResult = ExtractStringLiteral(arguments[2].Expression, semanticModel);
            referencesStartIndex = 3;
        }

        // Extract systemPrompt (named or positional at index 3)
        if (namedArgs.TryGetValue("systemPrompt", out var sysPromptArg))
        {
            systemPrompt = ExtractStringLiteral(sysPromptArg.Expression, semanticModel);
        }
        else if (arguments.Count > 3 && arguments[3].NameColon == null && referencesStartIndex == 3)
        {
            // Legacy: positional argument at index 3
            systemPrompt = ExtractStringLiteral(arguments[3].Expression, semanticModel);
            referencesStartIndex = 4;
        }

        // For backward compatibility: if only old "instructions" arg provided, use it as both contexts
        // The old API was: Create(name, description, instructions, options?, refs...)
        // Check if this looks like the old signature (no named params, index 2 is a string, not options)
        if (functionResult != null && systemPrompt == null && !namedArgs.ContainsKey("functionResult"))
        {
            // Old signature: use instructions as systemPrompt (persistent context)
            systemPrompt = functionResult;
            functionResult = null;
        }

        // Extract options (named parameter)
        if (namedArgs.TryGetValue("options", out var optionsArg))
        {
            System.Diagnostics.Debug.WriteLine($"[CapabilityAnalyzer] Found named 'options' parameter in {methodName}");
            options = ExtractSkillOptions(optionsArg.Expression, semanticModel);
        }
        else
        {
            // Check remaining positional args for SkillOptions
            for (int i = referencesStartIndex; i < arguments.Count; i++)
            {
                if (arguments[i].NameColon != null) continue; // Skip named args

                var argType = semanticModel.GetTypeInfo(arguments[i].Expression).Type;
                if (argType?.Name == "SkillOptions")
                {
                    System.Diagnostics.Debug.WriteLine($"[CapabilityAnalyzer] Found SkillOptions at position {i} in {methodName}");
                    options = ExtractSkillOptions(arguments[i].Expression, semanticModel);
                    referencesStartIndex = i + 1;
                    break;
                }
            }
        }

        // Check for [AIDescription] attribute override (supports dynamic descriptions like Functions)
        var attributeDescription = GetDescription(attrs);
        if (!string.IsNullOrWhiteSpace(attributeDescription))
        {
            description = attributeDescription;
        }

        if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(description))
        {
            return null;
        }

        // DIAGNOSTIC: Log document uploads found
        if (options != null && options.DocumentUploads.Any())
        {
            System.Diagnostics.Debug.WriteLine($"[CapabilityAnalyzer] Found {options.DocumentUploads.Count} document uploads in {methodName}:");
            foreach (var upload in options.DocumentUploads)
            {
                System.Diagnostics.Debug.WriteLine($"  - {upload.FilePath} -> {upload.DocumentId}");
            }
        }

        // Extract function/skill references (remaining arguments)
        var references = new List<ReferenceInfo>();
        for (int i = referencesStartIndex; i < arguments.Count; i++)
        {
            var argExpr = arguments[i].Expression;

            // Skip if this is a named "options" parameter
            if (arguments[i].NameColon?.Name.Identifier.ValueText == "options")
                continue;

            var reference = AnalyzeSkillReference(argExpr, semanticModel);
            if (reference != null)
                references.Add(reference);
        }

        // Extract conditional and context metadata (same as Functions)
        var conditionalExpression = GetConditionalExpression(attrs);
        var contextTypeName = GetMetadataTypeName(method, semanticModel);
        var requiresPermission = HasAttribute(attrs, "RequiresPermission");

        System.Diagnostics.Debug.WriteLine($"[AnalyzeSkillCapability] Skill={name}, Description={description}");
        System.Diagnostics.Debug.WriteLine($"[AnalyzeSkillCapability] ContextTypeName={contextTypeName ?? "NULL"}");
        System.Diagnostics.Debug.WriteLine($"[AnalyzeSkillCapability] HasDynamicDescription={description.Contains("{metadata.")}");

        return new SkillCapability
        {
            Name = name,
            MethodName = methodName,
            Description = description,
            FunctionResult = functionResult,
            SystemPrompt = systemPrompt,
            ParentHarnessName = className,
            ParentNamespace = namespaceName,
            Options = options ?? new SkillOptionsInfo(),
            UnresolvedReferences = references,
            RequiresPermission = requiresPermission,

            // Context and conditionals (feature parity with Functions!)
            ContextTypeName = contextTypeName,
            ConditionalExpression = conditionalExpression,

            // Note: ResolvedFunctionReferences and ResolvedHarnessTypes will be populated
            // during the resolution phase in HPDToolSourceGenerator
        };
    }

    // ========== SubAgent Analysis ==========

    /// <summary>
    /// Analyzes a method with [SubAgent] attribute and creates a SubAgentCapability.
    /// For Phase 1, delegates to existing SubAgentAnalyzer for full analysis.
    /// Full migration will happen in Phase 2-3.
    /// </summary>
    private static SubAgentCapability? AnalyzeSubAgentCapability(
        MethodDeclarationSyntax method,
        List<AttributeSyntax> attrs,
        SemanticModel semanticModel,
        GeneratorSyntaxContext context,
        string className,
        string namespaceName,
        List<Diagnostic> diagnostics)
    {
        // Validate return type is SubAgent
        var returnType = semanticModel.GetTypeInfo(method.ReturnType).Type;
        if (returnType == null || returnType.Name != "SubAgent")
        {
            // Invalid sub-agent method - skip
            return null;
        }

        var methodName = method.Identifier.ValueText;
        var isStatic = method.Modifiers.Any(SyntaxKind.StaticKeyword);

        // Extract metadata from SubAgentFactory calls in method body
        var (name, description, SessionMode) = ExtractSubAgentMetadata(method, semanticModel);

        if (string.IsNullOrWhiteSpace(name))
        {
            System.Diagnostics.Debug.WriteLine($"[CapabilityAnalyzer] Could not extract sub-agent name from {methodName}");
            return null;
        }

        // Extract conditional and context metadata (same as Functions and Skills)
        var conditionalExpression = GetConditionalExpression(attrs);
        var contextTypeName = GetMetadataTypeName(method, semanticModel);

        // SubAgents default to requiring permission (true), but can be overridden
        // If [RequiresPermission] is NOT present, default is true (for safety)
        // If [RequiresPermission] IS present, it's explicitly true
        // To disable permission requirement, we'd need a different mechanism (future enhancement)
        var requiresPermission = true;  // SubAgents always require permission by default

        var subAgentCapability = new SubAgentCapability
        {
            Name = name,  // Actual sub-agent name from SubAgentFactory.Create()
            MethodName = methodName,
            SubAgentName = name,  // Actual sub-agent name
            Description = description ?? GetDescription(attrs) ?? $"Sub-agent: {name}",
            ParentHarnessName = className,
            ParentNamespace = namespaceName,
            IsStatic = isStatic,
            SessionMode = SessionMode,  // Extracted from factory method (Create vs CreateStateful vs CreatePerSession)
            RequiresPermission = requiresPermission,

            // Context and conditionals (feature parity with Functions and Skills!)
            ContextTypeName = contextTypeName,
            ConditionalExpression = conditionalExpression,
        };

        return subAgentCapability;
    }

    /// <summary>
    /// Extracts sub-agent name, description, and Session mode from method body.
    /// Looks for SubAgentFactory.Create(), CreateStateful(), or CreatePerSession() calls.
    /// </summary>
    private static (string? name, string? description, string SessionMode) ExtractSubAgentMetadata(
        MethodDeclarationSyntax method,
        SemanticModel semanticModel)
    {
        string? name = null;
        string? description = null;
        string SessionMode = "Stateless";

        // Find all invocation expressions in the method body
        var invocations = method.DescendantNodes().OfType<InvocationExpressionSyntax>();

        foreach (var invocation in invocations)
        {
            // Check if this is a SubAgentFactory.Create() call
            if (invocation.Expression is MemberAccessExpressionSyntax memberAccess)
            {
                var methodSymbol = semanticModel.GetSymbolInfo(memberAccess).Symbol as IMethodSymbol;

                if (methodSymbol?.ContainingType?.Name == "SubAgentFactory")
                {
                    var methodNameStr = methodSymbol.Name;

                    // Determine Session mode from factory method name
                    if (methodNameStr == "CreateStateful")
                        SessionMode = "SharedSession";
                    else if (methodNameStr == "CreatePerSession")
                        SessionMode = "PerSession";
                    else
                        SessionMode = "Stateless";

                    // Extract arguments (name and description)
                    if (invocation.ArgumentList?.Arguments.Count >= 2)
                    {
                        // First argument is name
                        var nameArg = invocation.ArgumentList.Arguments[0];
                        if (nameArg.Expression is LiteralExpressionSyntax nameLiteral)
                        {
                            name = nameLiteral.Token.ValueText;
                        }

                        // Second argument is description
                        var descArg = invocation.ArgumentList.Arguments[1];
                        if (descArg.Expression is LiteralExpressionSyntax descLiteral)
                        {
                            description = descLiteral.Token.ValueText;
                        }
                    }

                    // Found the factory call, we're done
                    break;
                }
            }
        }

        return (name, description, SessionMode);
    }

    // ========== MultiAgent Analysis ==========

    /// <summary>
    /// Analyzes a method with [MultiAgent] attribute and creates a MultiAgentCapability.
    /// The method must return AgentWorkflowInstance or Task&lt;AgentWorkflowInstance&gt;.
    /// </summary>
    private static MultiAgentCapability? AnalyzeMultiAgentCapability(
        MethodDeclarationSyntax method,
        List<AttributeSyntax> attrs,
        SemanticModel semanticModel,
        string className,
        string namespaceName,
        List<Diagnostic> diagnostics)
    {
        // Validate return type is AgentWorkflowInstance or Task<AgentWorkflowInstance>
        var returnType = semanticModel.GetTypeInfo(method.ReturnType).Type;
        if (returnType == null)
        {
            return null;
        }

        bool isAsync = false;
        string? workflowTypeName = null;

        // Check for AgentWorkflowInstance directly
        if (returnType.Name == "AgentWorkflowInstance")
        {
            workflowTypeName = returnType.Name;
        }
        // Check for Task<AgentWorkflowInstance>
        else if (returnType.Name == "Task" && returnType is INamedTypeSymbol namedType && namedType.TypeArguments.Length == 1)
        {
            var innerType = namedType.TypeArguments[0];
            if (innerType.Name == "AgentWorkflowInstance")
            {
                isAsync = true;
                workflowTypeName = innerType.Name;
            }
        }

        if (workflowTypeName == null)
        {
            // Report HPDAG0201: Invalid return type
            diagnostics.Add(Diagnostic.Create(
                MultiAgentDiagnostics.InvalidReturnType,
                method.ReturnType.GetLocation(),
                method.Identifier.ValueText,
                returnType.ToDisplayString()));
            return null;
        }

        // Validate no parameters — generator always calls method with no args
        if (method.ParameterList.Parameters.Count > 0)
        {
            diagnostics.Add(Diagnostic.Create(
                MultiAgentDiagnostics.HasParameters,
                method.ParameterList.GetLocation(),
                method.Identifier.ValueText));
            return null;
        }

        var methodName = method.Identifier.ValueText;
        var isStatic = method.Modifiers.Any(SyntaxKind.StaticKeyword);

        // Extract attribute properties
        var multiAgentAttr = attrs.FirstOrDefault(a => a.Name.ToString().Contains("MultiAgent"));
        string? description = null;
        string? customName = null;
        bool streamEvents = true;
        int timeoutSeconds = 300;

        if (multiAgentAttr?.ArgumentList != null)
        {
            var arguments = multiAgentAttr.ArgumentList.Arguments;

            // First positional argument is description
            if (arguments.Count > 0 && arguments[0].NameEquals == null)
            {
                // Use ExtractStringLiteral to handle literals, concatenation, and const expressions
                description = ExtractStringLiteral(arguments[0].Expression, semanticModel);
            }

            // Check for named arguments
            foreach (var arg in arguments)
            {
                if (arg.NameEquals != null)
                {
                    var propName = arg.NameEquals.Name.Identifier.ValueText;
                    switch (propName)
                    {
                        case "Description":
                            description = ExtractStringLiteral(arg.Expression, semanticModel);
                            break;
                        case "Name":
                            customName = ExtractStringLiteral(arg.Expression, semanticModel);
                            break;
                        case "StreamEvents":
                            if (arg.Expression is LiteralExpressionSyntax streamLit)
                                streamEvents = streamLit.Token.ValueText == "true";
                            break;
                        case "TimeoutSeconds":
                            if (arg.Expression is LiteralExpressionSyntax timeoutLit && int.TryParse(timeoutLit.Token.ValueText, out var timeout))
                                timeoutSeconds = timeout;
                            break;
                    }
                }
            }
        }

        // Fall back to [AIDescription] attribute if no description in [MultiAgent]
        if (string.IsNullOrWhiteSpace(description))
        {
            description = GetDescription(attrs);
        }

        // Report HPDAG0203: Missing description warning
        if (string.IsNullOrWhiteSpace(description))
        {
            diagnostics.Add(Diagnostic.Create(
                MultiAgentDiagnostics.MissingDescription,
                method.Identifier.GetLocation(),
                methodName));
        }

        // Extract conditional and context metadata (same as other capabilities)
        var conditionalExpression = GetConditionalExpression(attrs);
        var contextTypeName = GetMetadataTypeName(method, semanticModel);
        var requiresPermission = true; // MultiAgents require permission by default

        System.Diagnostics.Debug.WriteLine($"[CapabilityAnalyzer] Analyzed MultiAgent: {methodName}, Description={description}, IsAsync={isAsync}");

        return new MultiAgentCapability
        {
            Name = customName ?? methodName,
            MethodName = methodName,
            Description = description ?? $"Execute {methodName} multi-agent workflow",
            ParentHarnessName = className,
            ParentNamespace = namespaceName,
            IsStatic = isStatic,
            IsAsync = isAsync,
            StreamEvents = streamEvents,
            TimeoutSeconds = timeoutSeconds,
            RequiresPermission = requiresPermission,

            // Context and conditionals (feature parity!)
            ContextTypeName = contextTypeName,
            ConditionalExpression = conditionalExpression,
        };
    }

    // ========== MCPServer Analysis ==========

    /// <summary>
    /// Analyzes a method with [MCPServer] attribute and creates an MCPServerCapability.
    /// The method must return MCPServerConfig or MCPServerConfig?.
    /// </summary>
    private static MCPServerCapability? AnalyzeMCPServerCapability(
        MethodDeclarationSyntax method,
        List<AttributeSyntax> attrs,
        SemanticModel semanticModel,
        string className,
        string namespaceName,
        List<Diagnostic> diagnostics)
    {
        // Validate return type is MCPServerConfig or MCPServerConfig? (nullable)
        var returnType = semanticModel.GetTypeInfo(method.ReturnType).Type;
        if (returnType == null)
            return null;

        // Handle nullable: MCPServerConfig? unwraps to MCPServerConfig
        var actualType = returnType;
        if (returnType is INamedTypeSymbol namedType &&
            namedType.OriginalDefinition.SpecialType == SpecialType.System_Nullable_T &&
            namedType.TypeArguments.Length == 1)
        {
            actualType = namedType.TypeArguments[0];
        }
        // Also handle nullable reference type annotation (MCPServerConfig?)
        if (returnType.NullableAnnotation == NullableAnnotation.Annotated && returnType is INamedTypeSymbol annotatedType)
        {
            actualType = annotatedType;
        }

        if (actualType.Name != "MCPServerConfig")
        {
            // Report HPDAG0301: Invalid return type
            diagnostics.Add(Diagnostic.Create(
                MCPServerDiagnostics.InvalidReturnType,
                method.ReturnType.GetLocation(),
                method.Identifier.ValueText,
                returnType.ToDisplayString()));
            return null;
        }

        var methodName = method.Identifier.ValueText;
        var isStatic = method.Modifiers.Any(SyntaxKind.StaticKeyword);

        // Extract attribute properties
        var mcpAttr = attrs.FirstOrDefault(a => a.Name.ToString().Contains("MCPServer"));
        string? serverName = null;
        string? customName = null;
        string? description = null;
        string? fromManifest = null;
        bool collapseWithinHarness = false;

        if (mcpAttr?.ArgumentList != null)
        {
            var arguments = mcpAttr.ArgumentList.Arguments;

            // First positional argument is serverName (constructor parameter)
            if (arguments.Count > 0 && arguments[0].NameEquals == null && arguments[0].NameColon == null)
            {
                serverName = ExtractStringLiteral(arguments[0].Expression, semanticModel);
            }

            // Check for named arguments
            foreach (var arg in arguments)
            {
                if (arg.NameEquals != null)
                {
                    var propName = arg.NameEquals.Name.Identifier.ValueText;
                    switch (propName)
                    {
                        case "Name":
                            customName = ExtractStringLiteral(arg.Expression, semanticModel);
                            break;
                        case "Description":
                            description = ExtractStringLiteral(arg.Expression, semanticModel);
                            break;
                        case "FromManifest":
                            fromManifest = ExtractStringLiteral(arg.Expression, semanticModel);
                            break;
                        case "CollapseWithinHarness":
                            if (arg.Expression is LiteralExpressionSyntax collapseLit)
                                collapseWithinHarness = collapseLit.IsKind(SyntaxKind.TrueLiteralExpression);
                            break;
                    }
                }
            }
        }

        // Fall back to [AIDescription] attribute if no description in [MCPServer]
        if (string.IsNullOrWhiteSpace(description))
        {
            description = GetDescription(attrs);
        }

        // Effective name: customName > methodName
        var effectiveName = customName ?? methodName;

        // Extract conditional and context metadata
        var conditionalExpression = GetConditionalExpression(attrs);
        var contextTypeName = GetMetadataTypeName(method, semanticModel);

        // Use standalone [RequiresPermission] attribute (same pattern as AIFunction/Skill)
        var requiresPermission = HasAttribute(attrs, "RequiresPermission");

        System.Diagnostics.Debug.WriteLine($"[CapabilityAnalyzer] Analyzed MCPServer: {methodName}, Name={effectiveName}, FromManifest={fromManifest}, CollapseWithinHarness={collapseWithinHarness}");

        return new MCPServerCapability
        {
            Name = effectiveName,
            MethodName = methodName,
            Description = description ?? string.Empty,
            ParentHarnessName = className,
            ParentNamespace = namespaceName,
            IsStatic = isStatic,
            FromManifest = fromManifest,
            ManifestServerName = serverName,
            CollapseWithinHarness = collapseWithinHarness,
            RequiresPermission = requiresPermission,

            // Context and conditionals (feature parity!)
            ContextTypeName = contextTypeName,
            ConditionalExpression = conditionalExpression,
        };
    }

    // ========== OpenApi Analysis ==========

    /// <summary>
    /// Analyzes a method with [OpenApi] attribute and creates an OpenApiCapability.
    /// The method must return OpenApiConfig (from HPD-Agent.OpenApi) and be parameterless.
    /// ISecretResolver is available via constructor injection — HPDAG0403 guards method parameters.
    /// </summary>
    private static OpenApiCapability? AnalyzeOpenApiCapability(
        MethodDeclarationSyntax method,
        List<AttributeSyntax> attrs,
        SemanticModel semanticModel,
        string className,
        string namespaceName,
        List<Diagnostic> diagnostics)
    {
        // Validate return type is OpenApiConfig
        var returnType = semanticModel.GetTypeInfo(method.ReturnType).Type;
        if (returnType == null || returnType.Name != "OpenApiConfig")
        {
            diagnostics.Add(Diagnostic.Create(
                OpenApiDiagnostics.InvalidReturnType,
                method.ReturnType.GetLocation(),
                method.Identifier.ValueText,
                returnType?.ToDisplayString() ?? "unknown"));
            return null;
        }

        // Validate method is parameterless (HPDAG0403)
        if (method.ParameterList.Parameters.Count > 0)
        {
            diagnostics.Add(Diagnostic.Create(
                OpenApiDiagnostics.MethodMustBeParameterless,
                method.ParameterList.GetLocation(),
                method.Identifier.ValueText));
            return null;
        }

        var methodName = method.Identifier.ValueText;
        var isStatic = method.Modifiers.Any(SyntaxKind.StaticKeyword);

        // Extract [OpenApi] attribute properties
        var openApiAttr = attrs.FirstOrDefault(a => a.Name.ToString().Contains("OpenApi"));
        string? prefix = null;

        if (openApiAttr?.ArgumentList != null)
        {
            foreach (var arg in openApiAttr.ArgumentList.Arguments)
            {
                if (arg.NameEquals != null)
                {
                    var propName = arg.NameEquals.Name.Identifier.ValueText;
                    if (propName == "Prefix")
                        prefix = ExtractStringLiteral(arg.Expression, semanticModel);
                }
            }
        }

        // Use standalone [RequiresPermission] attribute (same pattern as AIFunction/Skill/MCPServer)
        var requiresPermission = HasAttribute(attrs, "RequiresPermission");

        System.Diagnostics.Debug.WriteLine($"[CapabilityAnalyzer] Analyzed OpenApi: {methodName}, Prefix={prefix}, RequiresPermission={requiresPermission}");

        return new OpenApiCapability
        {
            Name = methodName,
            MethodName = methodName,
            Description = string.Empty,    // OpenAPI — descriptions come from spec operations
            ParentHarnessName = className,
            ParentNamespace = namespaceName,
            IsStatic = isStatic,
            Prefix = prefix,
            RequiresPermission = requiresPermission,
        };
    }

    // ========== Function Analysis ==========

    /// <summary>
    /// Analyzes a method with [AIFunction] attribute and creates a FunctionCapability.
    /// For Phase 1, performs basic analysis similar to existing inline analysis.
    /// Full migration from HPDToolSourceGenerator will happen in Phase 2-3.
    /// </summary>
    private static FunctionCapability? AnalyzeFunctionCapability(
        MethodDeclarationSyntax method,
        List<AttributeSyntax> attrs,
        SemanticModel semanticModel,
        GeneratorSyntaxContext context,
        string className,
        string namespaceName,
        List<Diagnostic> diagnostics)
    {
        var symbol = semanticModel.GetDeclaredSymbol(method);
        if (symbol == null) return null;

        var methodName = method.Identifier.ValueText;

        // Extract function metadata
        var description = GetDescription(attrs);
        var customName = GetCustomName(attrs);
        var conditionalExpression = GetConditionalExpression(attrs);
        var contextTypeName = GetMetadataTypeName(method, semanticModel);

        var parameters = ParameterAnalyzer.Analyze(method.ParameterList, semanticModel, out var parameterDiagnostics);

        // Get return type
        var returnType = method.ReturnType.ToString();
        var isAsync = returnType.Contains("Task");

        // Check for permissions
        var requiresPermission = HasAttribute(attrs, "RequiresPermission");
        var requiredPermissions = GetRequiredPermissions(attrs);

        // Extract Kind from [AIFunction(Kind = ...)]
        var kind = GetToolKind(attrs);

        var functionCapability = new FunctionCapability
        {
            Name = methodName,
            CustomName = customName,
            Description = description ?? $"Function: {methodName}",
            ParentHarnessName = className,
            ParentNamespace = namespaceName,

            // Context and conditionals (feature parity!)
            ContextTypeName = contextTypeName,
            ConditionalExpression = conditionalExpression,

            // Function-specific properties
            Parameters = parameters,
            ReturnType = returnType,
            IsAsync = isAsync,
            RequiresPermission = requiresPermission,
            RequiredPermissions = requiredPermissions.ToList(),
            Kind = kind,

            // TODO Phase 2: Add validation data
        };

        diagnostics.AddRange(parameterDiagnostics);
        return functionCapability;
    }

    // ========== Helper Methods ==========

    /// <summary>
    /// Checks if any attribute in the list matches the given name.
    /// </summary>
    private static bool HasAttribute(List<AttributeSyntax> attrs, string name)
    {
        return attrs.Any(attr => attr.Name.ToString().Contains(name));
    }

    private static AttributeArgumentSyntax? GetNamedArgument(AttributeSyntax attr, string name) =>
        attr.ArgumentList?.Arguments.FirstOrDefault(a =>
            string.Equals(a.NameEquals?.Name.Identifier.ValueText, name, System.StringComparison.Ordinal));

    private static string? GetNamedString(AttributeSyntax attr, string name)
    {
        var arg = GetNamedArgument(attr, name);
        return arg?.Expression is LiteralExpressionSyntax literal &&
               literal.IsKind(SyntaxKind.StringLiteralExpression)
            ? literal.Token.ValueText
            : null;
    }

    private static bool? GetNamedBool(AttributeSyntax attr, string name)
    {
        var arg = GetNamedArgument(attr, name);
        if (arg?.Expression is LiteralExpressionSyntax literal)
        {
            if (literal.IsKind(SyntaxKind.TrueLiteralExpression))
                return true;
            if (literal.IsKind(SyntaxKind.FalseLiteralExpression))
                return false;
        }

        return null;
    }

    private static string GetNamedToggle(AttributeSyntax attr, string name)
    {
        var enumMember = GetNamedEnumMember(attr, name);
        if (!string.IsNullOrWhiteSpace(enumMember))
            return enumMember;

        return GetNamedBool(attr, name) switch
        {
            true => "Enabled",
            false => "Disabled",
            _ => "Inherit"
        };
    }

    private static int? GetNamedInt(AttributeSyntax attr, string name)
    {
        var arg = GetNamedArgument(attr, name);
        return arg?.Expression is LiteralExpressionSyntax literal &&
               literal.Token.Value is int value
            ? value
            : null;
    }

    private static string? GetNamedEnumMember(AttributeSyntax attr, string name)
    {
        var arg = GetNamedArgument(attr, name);
        return arg?.Expression switch
        {
            MemberAccessExpressionSyntax memberAccess => memberAccess.Name.Identifier.ValueText,
            IdentifierNameSyntax identifier => identifier.Identifier.ValueText,
            _ => null
        };
    }

    /// <summary>
    /// Gets the name of a conflicting capability attribute, if any.
    /// Used to detect when [MultiAgent] is combined with other capability attributes.
    /// </summary>
    private static string? GetConflictingCapabilityAttribute(List<AttributeSyntax> attrs, string currentAttr)
    {
        var capabilityAttributes = new[] { "AIFunction", "Skill", "SubAgent", "MultiAgent", "MCPServer", "OpenApi" };

        foreach (var attr in attrs)
        {
            var attrName = attr.Name.ToString();
            foreach (var capAttr in capabilityAttributes)
            {
                // Skip the current attribute we're checking from
                if (capAttr == currentAttr) continue;

                if (attrName.Contains(capAttr))
                {
                    return capAttr;
                }
            }
        }

        return null;
    }

    /// <summary>
    /// Extracts description from [AIDescription] or [Description] attribute.
    /// </summary>
    private static string? GetDescription(List<AttributeSyntax> attrs)
    {
        // Check for [AIDescription] first
        var aiDescAttr = attrs.FirstOrDefault(a => a.Name.ToString().Contains("AIDescription"));
        if (aiDescAttr != null)
        {
            var arg = aiDescAttr.ArgumentList?.Arguments.FirstOrDefault();
            if (arg?.Expression is LiteralExpressionSyntax literal)
                return literal.Token.ValueText;
        }

        // Fallback to [Description]
        var descAttr = attrs.FirstOrDefault(a =>
            a.Name.ToString().Contains("Description") &&
            !a.Name.ToString().Contains("AIDescription"));
        if (descAttr != null)
        {
            var arg = descAttr.ArgumentList?.Arguments.FirstOrDefault();
            if (arg?.Expression is LiteralExpressionSyntax literal)
                return literal.Token.ValueText;
        }

        return null;
    }

    /// <summary>
    /// Extracts custom name from [AIFunction(Name = "...")] attribute.
    /// </summary>
    private static string? GetCustomName(List<AttributeSyntax> attrs)
    {
        // For now, return null (use method name)
        // TODO Phase 2: Extract from AIFunction attribute arguments
        return null;
    }

    /// <summary>
    /// Extracts tool kind from [AIFunction(Kind = ToolKind.Output)] attribute.
    /// Returns "Function" (default) or "Output".
    /// </summary>
    private static string GetToolKind(List<AttributeSyntax> attrs)
    {
        var aiFunctionAttr = attrs.FirstOrDefault(a =>
            a.Name.ToString().Contains("AIFunction"));

        if (aiFunctionAttr?.ArgumentList != null)
        {
            // Look for Kind = ToolKind.Output argument
            var kindArg = aiFunctionAttr.ArgumentList.Arguments
                .FirstOrDefault(a => a.NameEquals?.Name.Identifier.ValueText == "Kind");

            if (kindArg?.Expression is MemberAccessExpressionSyntax memberAccess)
            {
                // ToolKind.Output -> "Output"
                return memberAccess.Name.Identifier.ValueText;
            }
        }

        return "Function"; // Default
    }

    /// <summary>
    /// Extracts conditional expression from [ConditionalFunction], [ConditionalSkill], or [ConditionalSubAgent] attribute.
    /// All three attribute types use the same PropertyExpression parameter.
    /// </summary>
    private static string? GetConditionalExpression(List<AttributeSyntax> attrs)
    {
        // Look for any of the conditional attributes (ConditionalFunction, ConditionalSkill, ConditionalSubAgent)
        var conditionalAttr = attrs.FirstOrDefault(a =>
            a.Name.ToString().Contains("ConditionalFunction") ||
            a.Name.ToString().Contains("ConditionalSkill") ||
            a.Name.ToString().Contains("ConditionalSubAgent"));

        if (conditionalAttr != null)
        {
            var arg = conditionalAttr.ArgumentList?.Arguments.FirstOrDefault();
            if (arg?.Expression is LiteralExpressionSyntax literal)
                return literal.Token.ValueText;
        }

        return null;
    }

    /// <summary>
    /// Extracts context type name from generic attributes [AIFunction&lt;TMetadata&gt;], [Skill&lt;TMetadata&gt;], or [SubAgent&lt;TMetadata&gt;].
    /// Returns the context type name if found, null otherwise.
    /// </summary>
    private static string? GetMetadataTypeName(MethodDeclarationSyntax method, SemanticModel semanticModel)
    {
        // Check for AIFunction<TMetadata>, Skill<TMetadata>, and SubAgent<TMetadata> attributes
        var genericAttributes = method.AttributeLists
            .SelectMany(attrList => attrList.Attributes)
            .Where(attr => attr.Name.ToString().Contains("AIFunction") ||
                          attr.Name.ToString().Contains("Skill") ||
                          attr.Name.ToString().Contains("SubAgent"));

        foreach (var attr in genericAttributes)
        {
            System.Diagnostics.Debug.WriteLine($"[GetMetadataTypeName] Checking attribute: {attr.Name}");

            var symbolInfo = semanticModel.GetSymbolInfo(attr);
            System.Diagnostics.Debug.WriteLine($"[GetMetadataTypeName] Symbol: {symbolInfo.Symbol?.GetType().Name ?? "null"}");

            if (symbolInfo.Symbol is IMethodSymbol methodSymbol)
            {
                var attributeType = methodSymbol.ContainingType;
                System.Diagnostics.Debug.WriteLine($"[GetMetadataTypeName] AttributeType: {attributeType.Name}, IsGeneric: {attributeType.IsGenericType}");

                // Check if it's a generic attribute (AIFunction<TMetadata>, Skill<TMetadata>, or SubAgent<TMetadata>)
                if (attributeType.IsGenericType && attributeType.TypeArguments.Length == 1)
                {
                    var contextType = attributeType.TypeArguments[0];
                    // Use fully qualified name for code generation (global:: prefix ensures no ambiguity)
                    var fullyQualifiedName = contextType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
                    System.Diagnostics.Debug.WriteLine($"[GetMetadataTypeName] Found context type: {fullyQualifiedName}");
                    return fullyQualifiedName;
                }
            }
        }

        System.Diagnostics.Debug.WriteLine($"[GetMetadataTypeName] No context type found for method {method.Identifier.ValueText}");
        return null;
    }

    /// <summary>
    /// Extracts required permissions from [RequiresPermission] attribute.
    /// </summary>
    private static string[] GetRequiredPermissions(List<AttributeSyntax> attrs)
    {
        // TODO Phase 2: Extract permission strings
        return System.Array.Empty<string>();
    }

    // ========== Skill Helper Methods (Phase 5: Migrated from SkillAnalyzer) ==========

    /// <summary>
    /// Checks if an invocation is SkillFactory.Create()
    /// </summary>
    private static bool IsSkillFactoryCreate(InvocationExpressionSyntax invocation, SemanticModel semanticModel)
    {
        var symbolInfo = semanticModel.GetSymbolInfo(invocation);
        var symbol = symbolInfo.Symbol as IMethodSymbol;

        return symbol?.ContainingType?.Name == "SkillFactory" &&
               symbol?.Name == "Create";
    }

    /// <summary>
    /// Analyzes a reference (function or skill) from a string literal.
    /// Phase 5: Migrated from SkillAnalyzer.AnalyzeReference().
    /// </summary>
    private static ReferenceInfo? AnalyzeSkillReference(
        ExpressionSyntax expression,
        SemanticModel semanticModel)
    {
        // Extract string literal: "FileSystemHarness.ReadFile"
        var reference = ExtractStringLiteral(expression, semanticModel);

        if (string.IsNullOrWhiteSpace(reference))
        {
            return null;
        }

        // Parse "HarnessName.FunctionName" format
        var parts = reference!.Split('.');

        if (parts.Length != 2)
        {
            return null;
        }

        var toolName = parts[0];
        var methodName = parts[1];

        if (string.IsNullOrWhiteSpace(toolName) || string.IsNullOrWhiteSpace(methodName))
        {
            return null;
        }

        return new ReferenceInfo
        {
            ReferenceType = ReferenceType.Function,
            HarnessType = toolName,
            MethodName = methodName,
            FullName = reference,
            Location = expression.GetLocation()
        };
    }

    /// <summary>
    /// Extracts SkillOptions from object creation expression or method chain
    /// Phase 5: Migrated from SkillAnalyzer - supports fluent API (AddDocument/AddDocumentFromFile calls)
    /// </summary>
    private static SkillOptionsInfo ExtractSkillOptions(ExpressionSyntax expression, SemanticModel semanticModel)
    {
        var options = new SkillOptionsInfo();

        // Handle object creation with initializer (old style)
        if (expression is ObjectCreationExpressionSyntax objectCreation)
        {
            if (objectCreation.Initializer != null)
            {
                foreach (var assignment in objectCreation.Initializer.Expressions.OfType<AssignmentExpressionSyntax>())
                {
                    var propertyName = (assignment.Left as IdentifierNameSyntax)?.Identifier.ValueText;
                    var value = assignment.Right;

                    switch (propertyName)
                    {
                        case "InstructionDocuments":
                            // Extract array of strings
                            if (value is ArrayCreationExpressionSyntax arrayCreation &&
                                arrayCreation.Initializer != null)
                            {
                                options.InstructionDocuments = arrayCreation.Initializer.Expressions
                                    .Select(expr => ExtractStringLiteral(expr, semanticModel))
                                    .Where(s => s != null)
                                    .ToList()!;
                            }
                            break;

                        case "InstructionDocumentBaseDirectory":
                            var baseDir = ExtractStringLiteral(value, semanticModel);
                            if (baseDir != null)
                                options.InstructionDocumentBaseDirectory = baseDir;
                            break;
                    }
                }
            }
        }

        // Handle fluent API method chains (new SkillOptions().AddDocument(...).AddDocumentFromFile(...))
        // Walk up the expression tree to find all chained method calls
        System.Diagnostics.Debug.WriteLine($"[CapabilityAnalyzer] ExtractSkillOptions() - Walking method chain");
        var currentExpr = expression;
        int chainDepth = 0;
        while (currentExpr != null)
        {
            chainDepth++;
            System.Diagnostics.Debug.WriteLine($"[CapabilityAnalyzer]   Chain depth {chainDepth}: {currentExpr.GetType().Name}");

            if (currentExpr is InvocationExpressionSyntax invocation)
            {
                var methodSymbol = semanticModel.GetSymbolInfo(invocation.Expression).Symbol as IMethodSymbol;
                var methodName = methodSymbol?.Name;
                System.Diagnostics.Debug.WriteLine($"[CapabilityAnalyzer]   Method: {methodName}");

                if (methodName == "AddDocument")
                {
                    System.Diagnostics.Debug.WriteLine($"[CapabilityAnalyzer]   Found AddDocument() call");
                    ExtractAddDocumentCall(invocation, semanticModel, options);
                }
                else if (methodName == "AddDocumentFromFile")
                {
                    System.Diagnostics.Debug.WriteLine($"[CapabilityAnalyzer]   Found AddDocumentFromFile() call");
                    ExtractAddDocumentFromFileCall(invocation, semanticModel, options);
                }
                else if (methodName == "AddDocumentFromUrl")
                {
                    System.Diagnostics.Debug.WriteLine($"[CapabilityAnalyzer]   Found AddDocumentFromUrl() call");
                    ExtractAddDocumentFromUrlCall(invocation, semanticModel, options);
                }

                // Move to the expression that this method is called on (the receiver)
                if (invocation.Expression is MemberAccessExpressionSyntax memberAccess)
                {
                    currentExpr = memberAccess.Expression;
                }
                else
                {
                    break;
                }
            }
            else
            {
                break;
            }
        }

        System.Diagnostics.Debug.WriteLine($"[CapabilityAnalyzer] ExtractSkillOptions() - Final: {options.DocumentUploads.Count} uploads, {options.DocumentReferences.Count} references");

        return options;
    }

    /// <summary>
    /// Extracts AddDocument() call arguments (document reference by ID)
    /// API signature: AddDocument(string documentId, string? descriptionOverride = null)
    /// </summary>
    private static void ExtractAddDocumentCall(InvocationExpressionSyntax invocation, SemanticModel semanticModel, SkillOptionsInfo options)
    {
        var args = invocation.ArgumentList.Arguments;
        if (args.Count < 1)
            return;

        var documentId = ExtractStringLiteral(args[0].Expression, semanticModel);
        if (string.IsNullOrWhiteSpace(documentId))
            return;

        string? descriptionOverride = null;
        if (args.Count >= 2)
        {
            descriptionOverride = ExtractStringLiteral(args[1].Expression, semanticModel);
        }

        options.DocumentReferences.Add(new DocumentReferenceInfo
        {
            DocumentId = documentId,
            DescriptionOverride = descriptionOverride
        });
    }

    /// <summary>
    /// Extracts AddDocumentFromFile() call arguments (file path)
    /// API signature: AddDocumentFromFile(string filePath, string description, string? documentId = null)
    /// </summary>
    private static void ExtractAddDocumentFromFileCall(InvocationExpressionSyntax invocation, SemanticModel semanticModel, SkillOptionsInfo options)
    {
        var args = invocation.ArgumentList.Arguments;
        if (args.Count < 2)
            return;

        // Extract arguments in correct order
        var filePath = ExtractStringLiteral(args[0].Expression, semanticModel);
        var description = ExtractStringLiteral(args[1].Expression, semanticModel);
        var documentId = args.Count >= 3 ? ExtractStringLiteral(args[2].Expression, semanticModel) : null;

        if (!string.IsNullOrWhiteSpace(filePath) && !string.IsNullOrWhiteSpace(description))
        {
            // Auto-derive document ID from filename if not provided (matches runtime behavior)
            var effectiveDocumentId = string.IsNullOrWhiteSpace(documentId)
                ? DeriveDocumentId(filePath)
                : documentId;

            options.DocumentUploads.Add(new DocumentUploadInfo
            {
                FilePath = filePath,
                Description = description,
                DocumentId = effectiveDocumentId,
                SourceType = DocumentSourceType.FilePath
            });
        }
    }

    /// <summary>
    /// Extracts AddDocumentFromUrl() call arguments (URL upload)
    /// API signature: AddDocumentFromUrl(string url, string description, string? documentId = null)
    /// </summary>
    private static void ExtractAddDocumentFromUrlCall(InvocationExpressionSyntax invocation, SemanticModel semanticModel, SkillOptionsInfo options)
    {
        var args = invocation.ArgumentList.Arguments;
        if (args.Count < 2)
            return;

        // Extract arguments in correct order
        var url = ExtractStringLiteral(args[0].Expression, semanticModel);
        var description = ExtractStringLiteral(args[1].Expression, semanticModel);
        var documentId = args.Count >= 3 ? ExtractStringLiteral(args[2].Expression, semanticModel) : null;

        if (!string.IsNullOrWhiteSpace(url) && !string.IsNullOrWhiteSpace(description))
        {
            // Auto-derive document ID from URL if not provided (matches runtime behavior)
            var effectiveDocumentId = string.IsNullOrWhiteSpace(documentId)
                ? DeriveDocumentIdFromUrl(url)
                : documentId;

            options.DocumentUploads.Add(new DocumentUploadInfo
            {
                Url = url,
                Description = description,
                DocumentId = effectiveDocumentId,
                SourceType = DocumentSourceType.Url
            });
        }
    }

    /// <summary>
    /// Extracts string literal from expression
    /// Phase 5: Migrated from SkillAnalyzer
    /// </summary>
    private static string? ExtractStringLiteral(ExpressionSyntax expression, SemanticModel semanticModel)
    {
        // Handle string literals
        if (expression is LiteralExpressionSyntax literal &&
            literal.Token.IsKind(SyntaxKind.StringLiteralToken))
        {
            return literal.Token.ValueText;
        }

        // Handle verbatim string literals (@"...")
        if (expression is LiteralExpressionSyntax verbatim &&
            verbatim.Token.IsKind(SyntaxKind.StringLiteralToken))
        {
            return verbatim.Token.ValueText;
        }

        // Try to evaluate constant
        var constantValue = semanticModel.GetConstantValue(expression);
        if (constantValue.HasValue && constantValue.Value is string str)
        {
            return str;
        }

        return null;
    }

    /// <summary>
    /// Derives a document ID from a file path (matches runtime SkillOptions.DeriveDocumentId behavior)
    /// Example: "./docs/debugging-workflow.md" -> "debugging-workflow"
    /// </summary>
    private static string DeriveDocumentId(string filePath)
    {
        var fileName = System.IO.Path.GetFileNameWithoutExtension(filePath);

        // Normalize to lowercase-kebab-case
        return fileName.ToLowerInvariant()
            .Replace(" ", "-")
            .Replace("_", "-");
    }

    /// <summary>
    /// Derives a document ID from a URL (matches runtime SkillOptions.DeriveDocumentIdFromUrl behavior)
    /// Example: "https://docs.company.com/sops/financial-health.md" -> "financial-health"
    /// </summary>
    private static string DeriveDocumentIdFromUrl(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
            return "unknown";

        var fileName = System.IO.Path.GetFileNameWithoutExtension(uri.LocalPath);

        if (string.IsNullOrWhiteSpace(fileName))
        {
            // If no filename, use host: "https://example.com" -> "example-com"
            fileName = uri.Host.Replace(".", "-");
        }

        return fileName.ToLowerInvariant()
            .Replace(" ", "-")
            .Replace("_", "-");
    }
}
