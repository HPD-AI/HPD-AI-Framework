using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using HPD.Agent.SourceGenerator.Capabilities;
using HPD.Agent.SourceGenerator.Contracts;

/// <summary>
/// Source generator for HPD-Agent AI ToolHarnesses. Generates AOT-compatible ToolHarness registration code.
/// </summary>
[Generator]
public class HPDToolSourceGenerator : IIncrementalGenerator
{
    private static readonly System.Collections.Generic.List<string> _diagnosticMessages = new();

    // Phase 4: Feature flag removed - new polymorphic generation is now the only path

    /// <summary>
    /// Initializes the incremental generator with syntax providers and output callbacks.
    /// </summary>
    /// <param name="context">The generator initialization context.</param>
    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        // ToolHarness detection (classes with [AIFunction], [Skill], or [SubAgent] methods)
        var toolClasses = context.SyntaxProvider
            .CreateSyntaxProvider(
                predicate: static (node, ct) => IsToolClass(node, ct),
                transform: static (ctx, ct) => GetToolDeclaration(ctx, ct))
            .Where(static ToolHarness => ToolHarness is not null)
            .Collect();

        context.RegisterSourceOutput(
            toolClasses.Combine(context.CompilationProvider),
            static (sourceContext, value) => GenerateToolRegistrations(sourceContext, value.Left, value.Right));

        // Middleware detection (classes with [Middleware] attribute)
        var middlewareClasses = context.SyntaxProvider
            .CreateSyntaxProvider(
                predicate: static (node, ct) => IsMiddlewareClass(node),
                transform: static (ctx, ct) => GetMiddlewareDeclaration(ctx, ct))
            .Where(static middleware => middleware is not null)
            .Collect();

        context.RegisterSourceOutput(middlewareClasses, GenerateMiddlewareRegistry);
    }

    private static bool IsToolClass(SyntaxNode node, CancellationToken cancellationToken = default)
    {
        if (node is not ClassDeclarationSyntax classDecl)
            return false;

        var className = classDecl.Identifier.ValueText;
        System.Diagnostics.Debug.WriteLine($"[HPDToolSourceGenerator] Checking class: {className}");

        // Skip private classes - they cannot be accessed by generated Registration classes
        // This prevents compilation errors when private test classes have [Skill] or [AIFunction] attributes
        if (classDecl.Modifiers.Any(SyntaxKind.PrivateKeyword))
        {
            System.Diagnostics.Debug.WriteLine($"[HPDToolSourceGenerator]   Class {className} is private - SKIPPED");
            return false;
        }

        var methods = classDecl.Members.OfType<MethodDeclarationSyntax>().ToList();
        System.Diagnostics.Debug.WriteLine($"[HPDToolSourceGenerator]   Class {className} has {methods.Count} methods");

        var hasCollapseAttribute = classDecl.AttributeLists
            .SelectMany(attrList => attrList.Attributes)
            .Select(attr => attr.Name.ToString())
            .Any(name => name is "Collapse" or "CollapseAttribute" ||
                         name.EndsWith(".Collapse", System.StringComparison.Ordinal) ||
                         name.EndsWith(".CollapseAttribute", System.StringComparison.Ordinal));

        if (hasCollapseAttribute)
        {
            System.Diagnostics.Debug.WriteLine($"[HPDToolSourceGenerator]   Class {className} has [Collapse] - SELECTED");
            return true;
        }

        // PHASE 2: Unified detection - check for ANY capability attribute
        // This replaces the 3 separate detection threads (AIFunction, Skill, SubAgent)
        var hasCapabilityMethods = methods.Any(method =>
        {
            var attrs = method.AttributeLists
                .SelectMany(attrList => attrList.Attributes)
                .Select(attr => attr.Name.ToString());

            // A ToolHarness class has methods with any of these attributes
            return attrs.Any(name =>
                name.Contains("AIFunction") ||
                name.Contains("Skill") ||
                name.Contains("SubAgent") ||
                name.Contains("McpServer") ||
                name.Contains("OpenApi"));
        });

        if (hasCapabilityMethods)
        {
            System.Diagnostics.Debug.WriteLine($"[HPDToolSourceGenerator]   Class {className} has capability methods - SELECTED");
        }

        return hasCapabilityMethods;
    }

    private static ToolHarnessInfo? GetToolDeclaration(GeneratorSyntaxContext context, CancellationToken cancellationToken)
    {
        var classDecl = (ClassDeclarationSyntax)context.Node;
        var semanticModel = context.SemanticModel;

        // Get class info (needed for capability analysis)
        var className = classDecl.Identifier.ValueText;
        var namespaceName = GetNamespace(classDecl);

        // PHASE 5: Unified analysis for ALL capability types (Functions, Skills, SubAgents, MultiAgents)
        // Use CapabilityAnalyzer to discover all capabilities

        var capabilityDiagnostics = new List<Microsoft.CodeAnalysis.Diagnostic>();
        var capabilities = new List<HPD.Agent.SourceGenerator.Capabilities.ICapability>();

        foreach (var method in classDecl.Members.OfType<MethodDeclarationSyntax>())
        {
            var capability = HPD.Agent.SourceGenerator.Capabilities.CapabilityAnalyzer.AnalyzeMethod(
                method, semanticModel, context, className, namespaceName, out var methodDiagnostics);

            capabilityDiagnostics.AddRange(methodDiagnostics);

            if (capability != null)
            {
                capabilities.Add(capability);
            }
        }

        // Check for [Collapse] attribute and validate dual-context configuration
        var (isCollapsed, containerDescription, FunctionResult, FunctionResultExpression, FunctionResultIsStatic, SystemPrompt, SystemPromptExpression, SystemPromptIsStatic, diagnostics, customName) = GetCollapseAttribute(classDecl, semanticModel);

        // Must have at least one capability or collapse metadata.
        // Partial toolharnesses commonly put [Collapse] on one partial declaration and
        // [AIFunction] methods on other partial declarations. Keep the metadata-only
        // part so the partial merge can generate the container.
        if (!capabilities.Any() && !isCollapsed && capabilityDiagnostics.Count == 0)
            return null;

        // Merge capability diagnostics with toolharness diagnostics
        diagnostics.AddRange(capabilityDiagnostics);

        // Diagnostics will be stored in ToolHarnessInfo and reported in GenerateToolRegistrations

        // Check if the class has a parameterless constructor (either explicit or implicit)
        var hasParameterlessConstructor = HasParameterlessConstructor(classDecl);

        // Check if the class is publicly accessible (for ToolHarnessRegistry.All inclusion)
        // A class is publicly accessible if it's public and not nested inside a non-public class
        var isPubliclyAccessible = IsClassPubliclyAccessible(classDecl);

        // Build description from capabilities
        var functionCount = capabilities.OfType<HPD.Agent.SourceGenerator.Capabilities.FunctionCapability>().Count();
        var skillCount = capabilities.OfType<HPD.Agent.SourceGenerator.Capabilities.SkillCapability>().Count();
        var subAgentCount = capabilities.OfType<HPD.Agent.SourceGenerator.Capabilities.SubAgentCapability>().Count();
        var mcpServerCount = capabilities.OfType<HPD.Agent.SourceGenerator.Capabilities.MCPServerCapability>().Count();
        var openApiCount = capabilities.OfType<HPD.Agent.SourceGenerator.Capabilities.OpenApiCapability>().Count();
        var description = BuildToolHarnessDescription(functionCount, skillCount, subAgentCount, mcpServerCount, openApiCount);

        // NEW: Extract function names for selective registration
        var functionNames = capabilities
            .OfType<HPD.Agent.SourceGenerator.Capabilities.FunctionCapability>()
            .Select(f => f.FunctionName)
            .ToList();

        // NEW: Extract config constructor type (single-parameter constructor with *Config type)
        var configConstructorTypeName = GetConfigConstructorTypeName(classDecl, semanticModel);

        // NEW: Detect ISecretResolver-only constructor
        var hasSecretsConstructor = HasSecretsOnlyConstructor(classDecl);

        // NEW: Extract metadata type from capabilities
        var metadataTypeName = capabilities
            .OfType<HPD.Agent.SourceGenerator.Capabilities.BaseCapability>()
            .Where(c => !string.IsNullOrEmpty(c.ContextTypeName))
            .Select(c => c.ContextTypeName)
            .FirstOrDefault();

        // ToolHarness-scoped middleware (015): extract [Collapse(Middlewares = [...])] type names
        List<CollapseMiddlewareEntry>? collapseMiddlewareEntries = null;
        if (isCollapsed)
        {
            collapseMiddlewareEntries = GetCollapseMiddlewareEntries(classDecl, semanticModel, diagnostics);
        }

        return new ToolHarnessInfo
        {
            // ClassName is always the class identifier
            ClassName = classDecl.Identifier.ValueText,
            Description = description,
            Namespace = namespaceName,
            AssemblyName = semanticModel.Compilation.AssemblyName ?? string.Empty,

            // PHASE 5: Unified Capabilities list (all capability types)
            Capabilities = capabilities!,

            IsCollapsed = isCollapsed,
            ContainerDescription = containerDescription,
            FunctionResult = FunctionResult,
            FunctionResultExpression = FunctionResultExpression,
            FunctionResultIsStatic = FunctionResultIsStatic,
            SystemPrompt = SystemPrompt,
            SystemPromptExpression = SystemPromptExpression,
            SystemPromptIsStatic = SystemPromptIsStatic,
            HasParameterlessConstructor = hasParameterlessConstructor,
            HasSecretsConstructor = hasSecretsConstructor,

            // Diagnostics from dual-context validation
            Diagnostics = diagnostics,
            IsPubliclyAccessible = isPubliclyAccessible,

            // NEW: Config serialization fields
            FunctionNames = functionNames,
            ConfigConstructorTypeName = configConstructorTypeName,
            MetadataTypeName = metadataTypeName,

            // ToolHarness-scoped middleware (015)
            CollapseMiddlewareEntries = collapseMiddlewareEntries
        };
    }

    /// <summary>
    /// Detects if the toolharness class has a constructor that accepts a single *Config parameter.
    /// This enables config-based instantiation from JSON.
    /// </summary>
    private static string? GetConfigConstructorTypeName(ClassDeclarationSyntax classDecl, SemanticModel semanticModel)
    {
        var constructors = classDecl.Members
            .OfType<ConstructorDeclarationSyntax>()
            .ToList();

        foreach (var ctor in constructors)
        {
            // Look for single-parameter constructor where parameter type ends with "Config"
            if (ctor.ParameterList.Parameters.Count == 1)
            {
                var param = ctor.ParameterList.Parameters[0];
                var paramTypeName = param.Type?.ToString() ?? "";

                // Check if parameter type ends with "Config" (convention for config classes)
                if (paramTypeName.EndsWith("Config"))
                {
                    // Get fully qualified type name via semantic model
                    var typeInfo = semanticModel.GetTypeInfo(param.Type!);
                    if (typeInfo.Type != null)
                    {
                        return typeInfo.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
                    }
                    return paramTypeName;
                }
            }
        }

        return null;
    }

    /// <summary>
    /// Checks if a class has a parameterless constructor (either explicit or implicit).
    /// A class has an implicit parameterless constructor if it has NO explicit constructors.
    /// A class has an explicit parameterless constructor if it declares one.
    /// </summary>
    private static bool HasParameterlessConstructor(ClassDeclarationSyntax classDecl)
    {
        var constructors = classDecl.Members
            .OfType<ConstructorDeclarationSyntax>()
            .ToList();

        // If no explicit constructors, compiler generates implicit parameterless constructor
        if (!constructors.Any())
            return true;

        // Check if any explicit constructor is parameterless
        return constructors.Any(c => c.ParameterList.Parameters.Count == 0);
    }

    /// <summary>
    /// Detects if the toolharness has a constructor whose sole parameter is ISecretResolver.
    /// Also handles primary constructors (parameter lists on the class declaration itself).
    /// Example: public class StripeToolHarness(ISecretResolver secrets) { ... }
    /// </summary>
    private static bool HasSecretsOnlyConstructor(ClassDeclarationSyntax classDecl)
    {
        // Check primary constructor (C# 12 syntax: class Foo(ISecretResolver secrets))
        if (classDecl.ParameterList is { Parameters: { Count: 1 } primaryParams })
        {
            var typeName = primaryParams[0].Type?.ToString() ?? "";
            if (IsSecretResolverType(typeName))
                return true;
        }

        // Check explicit constructor declarations
        return classDecl.Members
            .OfType<ConstructorDeclarationSyntax>()
            .Any(ctor =>
                ctor.ParameterList.Parameters.Count == 1 &&
                IsSecretResolverType(ctor.ParameterList.Parameters[0].Type?.ToString() ?? ""));
    }

    private static bool IsSecretResolverType(string typeName) =>
        typeName == "ISecretResolver" || typeName.EndsWith(".ISecretResolver");

    /// <summary>
    /// Checks if a class is publicly accessible from outside the assembly.
    /// A class must be:
    /// 1. Declared with 'public' modifier
    /// 2. Not nested inside a non-public class
    /// Private/internal classes (e.g., test fixtures) are excluded from ToolRegistry.All
    /// but are still processed for individual Registration files.
    /// </summary>
    private static bool IsClassPubliclyAccessible(ClassDeclarationSyntax classDecl)
    {
        // Check if this class has the public modifier
        if (!classDecl.Modifiers.Any(SyntaxKind.PublicKeyword))
            return false;

        // Check if nested inside another class that's not public
        var parent = classDecl.Parent;
        while (parent != null)
        {
            if (parent is ClassDeclarationSyntax parentClass)
            {
                // If parent class is not public, this class is not publicly accessible
                if (!parentClass.Modifiers.Any(SyntaxKind.PublicKeyword))
                    return false;
            }
            parent = parent.Parent;
        }

        return true;
    }

    private static string BuildToolHarnessDescription(int functionCount, int skillCount, int subAgentCount, int mcpServerCount = 0, int openApiCount = 0)
    {
        var parts = new List<string>();
        if (functionCount > 0) parts.Add($"{functionCount} AI functions");
        if (skillCount > 0) parts.Add($"{skillCount} skills");
        if (subAgentCount > 0) parts.Add($"{subAgentCount} sub-agents");
        if (mcpServerCount > 0) parts.Add($"{mcpServerCount} MCP servers");
        if (openApiCount > 0) parts.Add($"{openApiCount} OpenAPI source(s)");

        if (parts.Count == 0)
            return "Empty ToolHarness container.";
        else if (parts.Count == 1)
            return $"ToolHarness containing {parts[0]}.";
        else
        {
            var last = parts[parts.Count - 1];
            var rest = string.Join(", ", parts.Take(parts.Count - 1));
            return $"ToolHarness containing {rest}, and {last}.";
        }
    }

    private static void GenerateToolRegistrations(
        SourceProductionContext context,
        ImmutableArray<ToolHarnessInfo?> ToolHarnesses,
        Compilation compilation)
    {
        // Group ToolHarnesses by name+namespace to handle partial classes FIRST
        // This prevents duplicate generation by merging partial classes before validation
        var ToolHarnessGroups = ToolHarnesses
            .Where(p => p != null)
            .GroupBy(p => $"{p!.Namespace}.{p.ClassName}")
            .Select(group =>
            {
                // Merge all partial class parts into one ToolHarness
                var first = group.First()!;

                // PHASE 5: Merge unified Capabilities list (all capability types)
                var allCapabilities = group.SelectMany(p => p!.Capabilities).ToList();

                // Count capabilities by type for description
                var functionCount = allCapabilities.OfType<HPD.Agent.SourceGenerator.Capabilities.FunctionCapability>().Count();
                var skillCount = allCapabilities.OfType<HPD.Agent.SourceGenerator.Capabilities.SkillCapability>().Count();
                var subAgentCount = allCapabilities.OfType<HPD.Agent.SourceGenerator.Capabilities.SubAgentCapability>().Count();
                var mcpServerCount = allCapabilities.OfType<HPD.Agent.SourceGenerator.Capabilities.MCPServerCapability>().Count();
                var openApiCount = allCapabilities.OfType<HPD.Agent.SourceGenerator.Capabilities.OpenApiCapability>().Count();

                // Preserve IsCollapsed and ContainerDescription from any partial class that has it
                var isCollapsed = group.Any(p => p!.IsCollapsed);
                var containerDescription = group.FirstOrDefault(p => p!.IsCollapsed)?.ContainerDescription;

                // All partial class parts must have parameterless constructor for the ToolHarness to be AOT-instantiable
                // (If any part declares a constructor with parameters, no implicit parameterless constructor is generated)
                var hasParameterlessConstructor = group.All(p => p!.HasParameterlessConstructor);

                // Detect ISecretResolver-only constructor (from any partial part)
                var hasSecretsConstructor = group.Any(p => p!.HasSecretsConstructor);

                // All partial class parts must be publicly accessible for the ToolHarness to be in the registry
                var isPubliclyAccessible = group.All(p => p!.IsPubliclyAccessible);

                // Merge diagnostics from all partial class parts
                var allDiagnostics = group.SelectMany(p => p!.Diagnostics).ToList();

                // Merge function names from all partial classes
                var allFunctionNames = group.SelectMany(p => p!.FunctionNames).Distinct().ToList();

                // Use first config constructor type found (should only be defined in one partial)
                var configConstructorTypeName = group.FirstOrDefault(p => !string.IsNullOrEmpty(p!.ConfigConstructorTypeName))?.ConfigConstructorTypeName;

                    // Use first metadata type found
                var metadataTypeName = group.FirstOrDefault(p => !string.IsNullOrEmpty(p!.MetadataTypeName))?.MetadataTypeName;

                return new ToolHarnessInfo
                {
                    ClassName = first.ClassName,
                    Description = BuildToolHarnessDescription(functionCount, skillCount, subAgentCount, mcpServerCount, openApiCount),
                    Namespace = first.Namespace,
                    AssemblyName = first.AssemblyName,

                    // PHASE 5: Unified Capabilities list (all capability types)
                    Capabilities = allCapabilities,
                    IsCollapsed = isCollapsed,
                    ContainerDescription = containerDescription,
                    // NEW: Dual-context properties
                    FunctionResult = group.FirstOrDefault(p => p?.FunctionResult != null)?.FunctionResult,
                    FunctionResultExpression = group.FirstOrDefault(p => p?.FunctionResultExpression != null)?.FunctionResultExpression,
                    FunctionResultIsStatic = group.FirstOrDefault(p => p?.FunctionResultExpression != null)?.FunctionResultIsStatic ?? true,
                    SystemPrompt = group.FirstOrDefault(p => p?.SystemPrompt != null)?.SystemPrompt,
                    SystemPromptExpression = group.FirstOrDefault(p => p?.SystemPromptExpression != null)?.SystemPromptExpression,
                    SystemPromptIsStatic = group.FirstOrDefault(p => p?.SystemPromptExpression != null)?.SystemPromptIsStatic ?? true,
                    HasParameterlessConstructor = hasParameterlessConstructor,
                    HasSecretsConstructor = hasSecretsConstructor,
                    IsPubliclyAccessible = isPubliclyAccessible,
                    // Diagnostics from dual-context validation
                    Diagnostics = allDiagnostics,
                    // NEW: Config serialization fields
                    FunctionNames = allFunctionNames,
                    ConfigConstructorTypeName = configConstructorTypeName,
                    MetadataTypeName = metadataTypeName,
                    // ToolHarness-scoped middleware (015): merge from any partial part that has them
                    CollapseMiddlewareEntries = group.FirstOrDefault(p => p?.CollapseMiddlewareEntries != null)?.CollapseMiddlewareEntries
                };
            })
            .ToList();

        // Report diagnostics for all ToolHarnesses
        foreach (var ToolHarness in ToolHarnessGroups)
        {
            foreach (var diagnostic in ToolHarness.Diagnostics)
            {
                context.ReportDiagnostic(diagnostic);
            }
        }

        // DIAGNOSTIC: Generate detailed diagnostic report AFTER grouping
        var reportLines = string.Join("\\n", _diagnosticMessages.Select(m => m.Replace("\"", "\\\"").Replace("\n", "\\n").Replace("\r", "")));
        var diagnosticCode = $@"
// HPD Source Generator Diagnostic Report
// Generated at: {DateTime.Now}
// ToolHarnesses found: {ToolHarnesses.Length} raw, {ToolHarnessGroups.Count} after merging

#pragma warning disable CS1591 // Missing XML comment for publicly visible type or member

namespace HPD.Agent.Diagnostics {{
    /// <summary>
    /// Diagnostic information from the HPD-Agent source generator execution.
    /// </summary>
    public static class SourceGeneratorDiagnostic {{
        /// <summary>
        /// Gets the source generator execution status message.
        /// </summary>
        public const string Message = ""Source generator executed successfully"";

        /// <summary>
        /// Gets the number of toolharnesses found during source generation.
        /// </summary>
        public const int ToolHarnessesFound = {ToolHarnessGroups.Count};

        /// <summary>
        /// Gets the detailed diagnostic report from source generation.
        /// </summary>
        public const string DetailedReport = @""{reportLines}"";
    }}
}}

#pragma warning restore CS1591
";
        context.AddSource("HPD.Agent.Diagnostics.SourceGeneratorDiagnostic.g.cs", diagnosticCode);

        // Clear for next compilation
        _diagnosticMessages.Clear();

        var debugInfo = new StringBuilder();
        debugInfo.AppendLine($"// Found {ToolHarnesses.Length} ToolHarness parts total");
        debugInfo.AppendLine($"// Merged into {ToolHarnessGroups.Count} unique ToolHarnesses");
        foreach (var ToolHarness in ToolHarnessGroups)
        {
            debugInfo.AppendLine($"// ToolHarness: {ToolHarness.Namespace}.{ToolHarness.ClassName} with {ToolHarness.FunctionCapabilities.Count()} functions, {ToolHarness.SkillCapabilities.Count()} skills, and {ToolHarness.SubAgentCapabilities.Count()} sub-agents");
        }
        context.AddSource("HPD.Agent.Generated.SourceGeneratorDebug.g.cs", debugInfo.ToString());

        // Resolve skill references before validation and code generation
        // PHASE 5: Use unified SkillCapabilities from Capabilities list
        var allSkillCapabilities = ToolHarnessGroups
            .SelectMany(p => p.SkillCapabilities)
            .ToList();
        if (allSkillCapabilities.Any())
        {
            ResolveSkillCapabilities(allSkillCapabilities);
        }

        foreach (var duplicate in allSkillCapabilities.GroupBy(skill => skill.Name, StringComparer.Ordinal)
            .Where(group => group.Count() > 1))
        {
            foreach (var skill in duplicate)
            {
                context.ReportDiagnostic(Diagnostic.Create(
                    HPD.Agent.SourceGenerator.Capabilities.SkillDiagnostics.DuplicateSkillName,
                    Location.None,
                    duplicate.Key));
            }
        }

        foreach (var toolHarness in ToolHarnessGroups)
        {
            foreach (var function in toolHarness.FunctionCapabilities)
            {
                var qualifiedModelName = $"{toolHarness.ClassName}.{function.FunctionName}";
                function.ParentSkillIds = allSkillCapabilities
                    .Where(skill => skill.ResolvedFunctionReferences.Contains(qualifiedModelName))
                    .Select(skill =>
                    {
                        var owner = string.IsNullOrEmpty(skill.ParentNamespace)
                            ? skill.ParentToolHarnessName
                            : $"{skill.ParentNamespace}.{skill.ParentToolHarnessName}";
                        return $"generated:{owner}:{skill.Name}";
                    })
                    .Distinct()
                    .OrderBy(id => id)
                    .ToList();
            }
        }

        foreach (var ToolHarness in ToolHarnessGroups)
        {
            if (ToolHarness == null) continue;

            foreach (var function in ToolHarness.FunctionCapabilities)
            {
                if (function.ValidationData?.NeedsValidation == true)
                {
                    var contextTypeName = function.ContextTypeName;
                    if (!string.IsNullOrEmpty(contextTypeName))
                    {
                        var contextType = function.ValidationData.SemanticModel.Compilation.GetTypeByMetadataName(contextTypeName!);
                        if (contextType != null)
                        {
                            ValidateTemplateProperties(context, function, contextType, function.ValidationData.Method);
                            if (!string.IsNullOrEmpty(function.ConditionalExpression))
                            {
                                ValidateConditionalExpression(context, function.ConditionalExpression!, contextType, function.ValidationData.Method, $"function {function.Name}");
                            }
                            ValidateFunctionContextUsage(context, function, function.ValidationData.Method);

                            foreach (var parameter in function.Parameters.Where(p => p.IsModelFacing && p.IsConditional))
                            {
                                if (!string.IsNullOrEmpty(parameter.ConditionalExpression))
                                {
                                    ValidateConditionalExpression(context, parameter.ConditionalExpression!, contextType, function.ValidationData.Method, $"parameter {parameter.Name} in function {function.Name}");
                                }
                            }
                        }
                    }
                }
            }

            var source = GenerateToolHarnessRegistration(ToolHarness);
            // Use fully qualified name as hint to prevent duplicates
            var hintName = string.IsNullOrEmpty(ToolHarness.Namespace)
                ? $"{ToolHarness.ClassName}Registration.g.cs"
                : $"{ToolHarness.Namespace}.{ToolHarness.ClassName}Registration.g.cs";
            context.AddSource(hintName, source);
        }

        // NEW: Generate ToolHarness registry catalog for AOT-compatible ToolHarness discovery
        if (ToolHarnessGroups.Any())
        {
            var registrySource = GenerateToolHarnessRegistry(ToolHarnessGroups, GetEventModuleProvider(compilation));
            context.AddSource("HPD.Agent.Generated.ToolHarnessRegistry.g.cs", registrySource);
        }
    }

    /// <summary>
    /// Generates the ToolHarnessRegistry.All array that serves as a catalog of all ToolHarnesses in the assembly.
    /// This eliminates reflection in hot paths by providing direct delegate references.
    /// Only ToolHarnesses with parameterless constructors and public accessibility are included.
    /// </summary>
    private static string GenerateToolHarnessRegistry(
        List<ToolHarnessInfo> ToolHarnesses,
        string? eventModuleProvider)
    {
        // Filter to only include ToolHarnesses that can be instantiated via the registry:
        // 1. Must have parameterless constructor, config constructor, or ISecretResolver-only constructor
        // 2. Must be publicly accessible (private/internal test classes are excluded)
        var instantiableToolHarnesses = ToolHarnesses
            .Where(p => (p.HasParameterlessConstructor || p.HasSecretsConstructor || !string.IsNullOrEmpty(p.ConfigConstructorTypeName)) && p.IsPubliclyAccessible)
            .OrderBy(p => p.EffectiveName)
            .ToList();
        var sb = new StringBuilder();

        sb.AppendLine("// <auto-generated/>");
        sb.AppendLine("#pragma warning disable");
        sb.AppendLine();
        sb.AppendLine("using System;");
        sb.AppendLine("using System.Collections.Generic;");
        sb.AppendLine("using System.Runtime.CompilerServices;");
        sb.AppendLine("using System.Text.Json;");
        sb.AppendLine("using System.Text.Json.Serialization;");
        sb.AppendLine("using System.Text.Json.Serialization.Metadata;");
        sb.AppendLine("using Microsoft.Extensions.AI;");
        sb.AppendLine("using HPD.Agent;  // For ToolHarnessFactory and IToolMetadata types");
        sb.AppendLine();
        sb.AppendLine("namespace HPD.Agent.Generated");
        sb.AppendLine("{");
        sb.AppendLine("    /// <summary>");
        sb.AppendLine("    /// AOT-compatible catalog of all ToolHarnesses in this assembly.");
        sb.AppendLine("    /// Generated by HPDToolSourceGenerator.");
        sb.AppendLine("    /// Provides direct delegate references eliminating reflection in hot paths.");
        sb.AppendLine($"    /// Contains {instantiableToolHarnesses.Count} ToolHarnesses (pure DI-only toolharnesses excluded).");
        sb.AppendLine("    /// </summary>");
        sb.AppendLine("    [System.CodeDom.Compiler.GeneratedCodeAttribute(\"HPDToolSourceGenerator\", \"1.0.0.0\")]");
        sb.AppendLine("    public static class ToolHarnessRegistry");
        sb.AppendLine("    {");
        sb.AppendLine("        /// <summary>");
        sb.AppendLine("        /// Catalog of all ToolHarnesses in this assembly with parameterless or ISecretResolver-only constructors.");
        sb.AppendLine("        /// AgentBuilder automatically discovers and uses this at construction time.");
        sb.AppendLine("        /// </summary>");
        sb.AppendLine("        public static readonly ToolHarnessFactory[] All = new ToolHarnessFactory[]");
        sb.AppendLine("        {");

        foreach (var ToolHarness in instantiableToolHarnesses)
        {
            var ns = string.IsNullOrEmpty(ToolHarness.Namespace) ? "" : $"{ToolHarness.Namespace}.";
            var fullTypeName = $"{ns}{ToolHarness.ClassName}";

            sb.AppendLine($"            new ToolHarnessFactory(");
            sb.AppendLine($"                // ========== EXISTING FIELDS ==========");
            // Use EffectiveName for registry lookup (always ClassName now)
            sb.AppendLine($"                Name: \"{ToolHarness.EffectiveName}\",");
            sb.AppendLine($"                ToolHarnessType: typeof({fullTypeName}),");
            if (ToolHarness.HasParameterlessConstructor)
                sb.AppendLine($"                CreateInstance: () => new {fullTypeName}(),  // Direct instantiation (AOT-safe)");
            else if (!string.IsNullOrEmpty(ToolHarness.ConfigConstructorTypeName))
                sb.AppendLine($"                CreateInstance: () => throw new InvalidOperationException(\"{fullTypeName} requires config — use CreateFromConfig\"),");
            else
                sb.AppendLine($"                CreateInstance: () => throw new InvalidOperationException(\"{fullTypeName} requires ISecretResolver — use CreateWithSecrets\"),");

            // ========== SECRETS-BASED INSTANTIATION ==========
            sb.AppendLine($"                // ========== SECRETS-BASED INSTANTIATION ==========");
            if (ToolHarness.HasSecretsConstructor)
                sb.AppendLine($"                CreateWithSecrets: secrets => new {fullTypeName}(secrets),");
            else
                sb.AppendLine($"                CreateWithSecrets: null,");

            // Handle skill-only containers (no instance parameter)
            if (!ToolHarness.RequiresInstance)
            {
                sb.AppendLine($"                CreateFunctions: (_, ctx, serialization) => {ToolHarness.ClassName}Registration.CreateToolHarness(ctx, serialization),");
            }
            else
            {
                sb.AppendLine($"                CreateFunctions: (instance, ctx, serialization) => {ToolHarness.ClassName}Registration.CreateToolHarness(({fullTypeName})instance, ctx, serialization),");
            }

            // Add GetReferencedToolHarnesses if ToolHarness has skills
            if (ToolHarness.SkillCapabilities.Any())
            {
                sb.AppendLine($"                GetReferencedToolHarnesses: {ToolHarness.ClassName}Registration.GetReferencedToolHarnesses,");
                sb.AppendLine($"                GetReferencedFunctions: {ToolHarness.ClassName}Registration.GetReferencedFunctions,");
            }
            else
            {
                sb.AppendLine($"                GetReferencedToolHarnesses: () => Array.Empty<string>(),");
                sb.AppendLine($"                GetReferencedFunctions: () => new Dictionary<string, string[]>(),");
            }

            // NEW: Collapsing metadata (from [Collapse] attribute)
            sb.AppendLine($"                // ========== COLLAPSING METADATA ==========");
            sb.AppendLine($"                HasDescription: {ToolHarness.IsCollapsed.ToString().ToLower()},");
            sb.AppendLine($"                Description: {(string.IsNullOrEmpty(ToolHarness.ContainerDescription) ? "null" : $"@\"{EscapeForVerbatim(ToolHarness.ContainerDescription)}\"")},");
            sb.AppendLine($"                FunctionResult: {(string.IsNullOrEmpty(ToolHarness.FunctionResult) ? "null" : $"@\"{EscapeForVerbatim(ToolHarness.FunctionResult)}\"")},");
            sb.AppendLine($"                SystemPrompt: {(string.IsNullOrEmpty(ToolHarness.SystemPrompt) ? "null" : $"@\"{EscapeForVerbatim(ToolHarness.SystemPrompt)}\"")},");

            // NEW: Config-based instantiation
            sb.AppendLine($"                // ========== CONFIG INSTANTIATION ==========");
            if (!string.IsNullOrEmpty(ToolHarness.ConfigConstructorTypeName))
            {
                sb.AppendLine($"                ConfigType: typeof({ToolHarness.ConfigConstructorTypeName}),");
                sb.AppendLine($"                // No JSON metadata is registered for tool harness config type");
                sb.AppendLine($"                CreateFromConfig: json => new {fullTypeName}(JsonSerializer.Deserialize(json, GetJsonTypeInfo<{ToolHarness.ConfigConstructorTypeName}>())!),");
            }
            else
            {
                sb.AppendLine($"                ConfigType: null,");
                sb.AppendLine($"                CreateFromConfig: null,");
            }

            // NEW: Metadata type
            sb.AppendLine($"                // ========== METADATA ==========");
            if (!string.IsNullOrEmpty(ToolHarness.MetadataTypeName))
            {
                sb.AppendLine($"                MetadataType: typeof({ToolHarness.MetadataTypeName}),");
                sb.AppendLine($"                // No JSON metadata is registered for tool metadata type");
                sb.AppendLine($"                DeserializeMetadata: json => JsonSerializer.Deserialize(json, GetJsonTypeInfo<{ToolHarness.MetadataTypeName}>()),");
            }
            else
            {
                sb.AppendLine($"                MetadataType: null,");
                sb.AppendLine($"                DeserializeMetadata: null,");
            }

            // NEW: Function names for selective registration
            var functionNamesArray = ToolHarness.FunctionNames.Any()
                ? $"new string[] {{ {string.Join(", ", ToolHarness.FunctionNames.Select(n => $"\"{n}\""))} }}"
                : "Array.Empty<string>()";
            sb.AppendLine($"                FunctionNames: {functionNamesArray},");

            // NEW: MCP Server support
            sb.AppendLine($"                // ========== MCP SERVERS ==========");
            sb.AppendLine($"                HasMcpServers: {ToolHarness.MCPServerCapabilities.Any().ToString().ToLower()},");
            if (ToolHarness.MCPServerCapabilities.Any())
            {
                sb.AppendLine($"                CollectMcpServers: {ToolHarness.ClassName}Registration.CollectMcpServers,");
            }
            else
            {
                sb.AppendLine($"                CollectMcpServers: null,");
            }

            // NEW: OpenAPI support
            sb.AppendLine($"                // ========== OPENAPI SOURCES ==========");
            if (ToolHarness.OpenApiCapabilities.Any())
            {
                sb.AppendLine($"                CollectOpenApiSources: {ToolHarness.ClassName}Registration.CollectOpenApiSources,");
            }
            else
            {
                sb.AppendLine($"                CollectOpenApiSources: null,");
            }

            sb.AppendLine($"                StableIdentity: @\"{EscapeForVerbatim(ToolHarness.AssemblyName)}:{EscapeForVerbatim(ToolHarness.Namespace)}:{EscapeForVerbatim(ToolHarness.EffectiveName)}\",");
            var agentResources = ToolHarness.CollapseMiddlewareEntries?
                .Where(static entry => entry.AgentResourceTypeFqn is not null)
                .GroupBy(static entry => entry.AgentResourceTypeFqn, StringComparer.Ordinal)
                .Select(static group => group.First())
                .ToArray();
            if (agentResources is { Length: > 0 })
            {
                sb.AppendLine("                AgentResources: new global::HPD.Agent.ToolHarnessAgentResourceDescriptor[]");
                sb.AppendLine("                {");
                foreach (var resource in agentResources)
                {
                    sb.AppendLine("                    new global::HPD.Agent.ToolHarnessAgentResourceDescriptor");
                    sb.AppendLine("                    {");
                    sb.AppendLine($"                        ResourceType = typeof({resource.AgentResourceTypeFqn}),");
                    sb.AppendLine($"                        ImplementationType = typeof({resource.AgentResourceImplementationTypeFqn}),");
                    sb.AppendLine($"                        Factory = static () => new {resource.AgentResourceImplementationTypeFqn}()");
                    sb.AppendLine("                    },");
                }
                sb.AppendLine("                },");
            }
            if (ToolHarness.CollapseMiddlewareEntries is { Count: > 0 })
            {
                sb.AppendLine($"                Middleware: new global::HPD.Agent.ToolHarnessMiddlewareDescriptor[]");
                sb.AppendLine($"                {{");
                foreach (var entry in ToolHarness.CollapseMiddlewareEntries)
                {
                    sb.AppendLine("                    new global::HPD.Agent.ToolHarnessMiddlewareDescriptor");
                    sb.AppendLine("                    {");
                    sb.AppendLine($"                        MiddlewareType = typeof({entry.FullyQualifiedTypeName}),");
                    if (entry.ServicesOwned)
                    {
                        sb.AppendLine($"                        Factory = static context => global::HPD.Agent.ToolHarnessMiddlewareActivation.ServicesOwned(context.GetRequiredService<{entry.FullyQualifiedTypeName}>())");
                    }
                    else if (entry.AgentResourceTypeFqn is not null)
                    {
                        sb.AppendLine($"                        ConfigurationType = typeof({entry.ConfigTypeFqn}),");
                        sb.AppendLine($"                        Factory = static context => global::HPD.Agent.ToolHarnessMiddlewareActivation.ExecutionOwned(new {entry.FullyQualifiedTypeName}(context.GetRequiredAgentResource<{entry.AgentResourceTypeFqn}>(), context.GetCanonicalWorkspaceIdentity(), context.GetConfigurationOrDefault(GetJsonTypeInfo<{entry.ConfigTypeFqn}>({entry.JsonContextTypeFqn}.Default), static () => new {entry.ConfigTypeFqn}())) )");
                    }
                    else if (entry.ConfigTypeFqn is not null)
                    {
                        sb.AppendLine($"                        ConfigurationType = typeof({entry.ConfigTypeFqn}),");
                        if (entry.ConfigHasGeneratedDefault)
                            sb.AppendLine($"                        Factory = static context => global::HPD.Agent.ToolHarnessMiddlewareActivation.ExecutionOwned(new {entry.FullyQualifiedTypeName}(context.GetConfigurationOrDefault(GetJsonTypeInfo<{entry.ConfigTypeFqn}>({entry.JsonContextTypeFqn}.Default), static () => new {entry.ConfigTypeFqn}())))");
                        else
                            sb.AppendLine($"                        Factory = static context => global::HPD.Agent.ToolHarnessMiddlewareActivation.ExecutionOwned(new {entry.FullyQualifiedTypeName}(context.GetConfiguration(GetJsonTypeInfo<{entry.ConfigTypeFqn}>({entry.JsonContextTypeFqn}.Default))))");
                    }
                    else
                    {
                        sb.AppendLine($"                        Factory = static context => global::HPD.Agent.ToolHarnessMiddlewareActivation.ExecutionOwned(new {entry.FullyQualifiedTypeName}())");
                    }
                    sb.AppendLine("                    },");
                }
                sb.AppendLine($"                }}");
            }
            else
            {
                sb.AppendLine($"                Middleware: null");
            }

            if (eventModuleProvider is not null)
            {
                sb.AppendLine(",");
                sb.AppendLine($"                EventModule: {eventModuleProvider}.Fragment");
            }

            sb.AppendLine($"            ),");
        }

        sb.AppendLine("        };");
        sb.AppendLine();
        sb.AppendLine("        private static JsonTypeInfo<T> GetJsonTypeInfo<T>()");
        sb.AppendLine("        {");
        sb.AppendLine("            foreach (var resolver in AIJsonUtilities.DefaultOptions.TypeInfoResolverChain)");
        sb.AppendLine("            {");
        sb.AppendLine("                if (resolver.GetTypeInfo(typeof(T), AIJsonUtilities.DefaultOptions) is JsonTypeInfo<T> typeInfo)");
        sb.AppendLine("                    return typeInfo;");
        sb.AppendLine("            }");
        sb.AppendLine();
        sb.AppendLine("            if (HPDJsonContext.Default.GetTypeInfo(typeof(T)) is JsonTypeInfo<T> hpdTypeInfo)");
        sb.AppendLine("                return hpdTypeInfo;");
        sb.AppendLine();
        sb.AppendLine("            throw new NotSupportedException($\"No JSON metadata is registered for tool harness config type '{typeof(T).FullName}'. No JSON metadata is registered for tool metadata type '{typeof(T).FullName}'. No JSON metadata is registered for collapse middleware config type '{typeof(T).FullName}'.\");");
        sb.AppendLine("        }");
        sb.AppendLine();
        sb.AppendLine("        private static JsonTypeInfo<T> GetJsonTypeInfo<T>(JsonSerializerContext context)");
        sb.AppendLine("            => context.GetTypeInfo(typeof(T)) as JsonTypeInfo<T>");
        sb.AppendLine("                ?? throw new InvalidOperationException($\"Generated JSON context '{context.GetType()}' does not contain metadata for '{typeof(T)}'.\");");
        sb.AppendLine();
        sb.AppendLine("#pragma warning disable CA2255");
        sb.AppendLine("        [ModuleInitializer]");
        sb.AppendLine("        internal static void RegisterGeneratedCatalog()");
        sb.AppendLine("#pragma warning restore CA2255");
        sb.AppendLine("        {");
        sb.AppendLine("            AgentGeneratedRegistry.Register(toolharnesses: All);");
        sb.AppendLine("        }");
        sb.AppendLine("    }");
        sb.AppendLine("}");

        return sb.ToString();
    }

    private static string? GetEventModuleProvider(Compilation compilation)
    {
        foreach (var attribute in compilation.Assembly.GetAttributes())
        {
            if (attribute.AttributeClass?.Name == "HpdAgentEventModuleManifestAttribute" &&
                attribute.ConstructorArguments.Length > 1 &&
                attribute.ConstructorArguments[1].Value is INamedTypeSymbol providerType)
            {
                return providerType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
            }
        }

        var ownsAgentEvents = compilation.SyntaxTrees.Any(tree =>
            tree.GetRoot().DescendantNodes().OfType<RecordDeclarationSyntax>().Any(declaration =>
            {
                if (compilation.GetSemanticModel(tree).GetDeclaredSymbol(declaration) is not INamedTypeSymbol type ||
                    type.IsAbstract || type.IsGenericType)
                    return false;
                for (var current = type.BaseType; current is not null; current = current.BaseType)
                    if (current.Name == "AgentEvent")
                        return true;
                return false;
            }));
        if (ownsAgentEvents && !string.IsNullOrWhiteSpace(compilation.AssemblyName))
        {
            if (StringComparer.Ordinal.Equals(compilation.AssemblyName, "HPD-Agent"))
                return "global::HPD.Agent.Serialization.CoreAgentEventModule";
            return $"global::HPD.Agent.Serialization.{GetGeneratedEventProviderTypeName(compilation.AssemblyName!)}";
        }

        return null;
    }

    private static string GetGeneratedEventProviderTypeName(string moduleId)
    {
        var sanitized = Regex.Replace(moduleId, "[^A-Za-z0-9_]", "_");
        uint hash = 2166136261;
        foreach (var value in moduleId)
        {
            hash ^= value;
            hash *= 16777619;
        }
        return $"GeneratedAgentEventModule_{sanitized}_{hash:x8}";
    }

    /// <summary>
    /// Escapes a string for use in a verbatim string literal (@"...").
    /// Only quotes need to be doubled.
    /// </summary>
    private static string EscapeForVerbatim(string value)
    {
        if (string.IsNullOrEmpty(value))
            return value;
        return value.Replace("\"", "\"\"");
    }

    // ========== MIDDLEWARE SOURCE GENERATION ==========

    /// <summary>
    /// Checks if a class has the [Middleware] attribute.
    /// </summary>
    private static bool IsMiddlewareClass(SyntaxNode node)
    {
        if (node is not ClassDeclarationSyntax classDecl)
            return false;

        // Skip private classes
        if (classDecl.Modifiers.Any(SyntaxKind.PrivateKeyword))
            return false;

        // Check for [Middleware] attribute on the class
        var hasMiddlewareAttribute = classDecl.AttributeLists
            .SelectMany(attrList => attrList.Attributes)
            .Any(attr => attr.Name.ToString().Contains("Middleware"));

        return hasMiddlewareAttribute;
    }

    /// <summary>
    /// Extracts middleware information from a class with [Middleware] attribute.
    /// </summary>
    private static HPD.Agent.SourceGenerator.MiddlewareInfo? GetMiddlewareDeclaration(GeneratorSyntaxContext context, CancellationToken ct)
    {
        var classDecl = (ClassDeclarationSyntax)context.Node;
        var semanticModel = context.SemanticModel;

        var className = classDecl.Identifier.ValueText;
        var namespaceName = GetNamespace(classDecl);

        // Get custom name from [Middleware(Name = "...")] or [Middleware("name")]
        var customName = GetMiddlewareCustomName(classDecl);

        // Check constructor patterns
        var hasParameterlessConstructor = HasParameterlessConstructor(classDecl);
        var configConstructorTypeName = GetConfigConstructorTypeName(classDecl, semanticModel);
        var isPubliclyAccessible = IsClassPubliclyAccessible(classDecl);

        return new HPD.Agent.SourceGenerator.MiddlewareInfo
        {
            ClassName = className,
            CustomName = customName,
            Namespace = namespaceName,
            HasParameterlessConstructor = hasParameterlessConstructor,
            ConfigConstructorTypeName = configConstructorTypeName,
            IsPubliclyAccessible = isPubliclyAccessible
        };
    }

    /// <summary>
    /// Gets the custom name from [Middleware] attribute if specified.
    /// </summary>
    private static string? GetMiddlewareCustomName(ClassDeclarationSyntax classDecl)
    {
        var middlewareAttr = classDecl.AttributeLists
            .SelectMany(attrList => attrList.Attributes)
            .FirstOrDefault(attr => attr.Name.ToString().Contains("Middleware"));

        if (middlewareAttr?.ArgumentList?.Arguments.Count > 0)
        {
            var args = middlewareAttr.ArgumentList.Arguments;

            // Check for named argument: Name = "..."
            var namedArg = args.FirstOrDefault(a => a.NameEquals?.Name.Identifier.ValueText == "Name");
            if (namedArg != null)
            {
                return ExtractStringLiteral(namedArg.Expression);
            }

            // Check for positional argument: [Middleware("name")]
            var firstArg = args.FirstOrDefault();
            if (firstArg != null && firstArg.NameEquals == null)
            {
                var value = ExtractStringLiteral(firstArg.Expression);
                if (!string.IsNullOrEmpty(value))
                    return value;
            }
        }

        return null;
    }

    /// <summary>
    /// Generates the MiddlewareRegistry.All array for AOT-compatible middleware resolution.
    /// Only middlewares with parameterless constructors OR config constructors are included.
    /// DI-only middlewares are marked with RequiresDI = true.
    /// </summary>
    private static void GenerateMiddlewareRegistry(SourceProductionContext context, ImmutableArray<HPD.Agent.SourceGenerator.MiddlewareInfo?> middlewares)
    {
        var validMiddlewares = middlewares
            .Where(m => m != null && m.IsPubliclyAccessible)
            .OrderBy(m => m!.EffectiveName)
            .ToList();
        if (!validMiddlewares.Any())
            return;

        var sb = new StringBuilder();

        sb.AppendLine("// <auto-generated/>");
        sb.AppendLine("#pragma warning disable");
        sb.AppendLine();
        sb.AppendLine("using System;");
        sb.AppendLine("using System.Collections.Generic;");
        sb.AppendLine("using System.Runtime.CompilerServices;");
        sb.AppendLine("using System.Text.Json;");
        sb.AppendLine("using System.Text.Json.Serialization.Metadata;");
        sb.AppendLine("using Microsoft.Extensions.AI;");
        sb.AppendLine("using HPD.Agent;");
        sb.AppendLine("using HPD.Agent.Middleware;  // For MiddlewareFactory and IAgentMiddleware");
        sb.AppendLine();
        sb.AppendLine("namespace HPD.Agent.Generated");
        sb.AppendLine("{");
        sb.AppendLine("    /// <summary>");
        sb.AppendLine("    /// AOT-compatible catalog of all middlewares in this assembly.");
        sb.AppendLine("    /// Generated by HPDToolSourceGenerator.");
        sb.AppendLine("    /// Provides direct delegate references eliminating reflection in hot paths.");
        sb.AppendLine($"    /// Contains {validMiddlewares.Count} middlewares.");
        sb.AppendLine("    /// </summary>");
        sb.AppendLine("    [System.CodeDom.Compiler.GeneratedCodeAttribute(\"HPDToolSourceGenerator\", \"1.0.0.0\")]");
        sb.AppendLine("    public static class MiddlewareRegistry");
        sb.AppendLine("    {");
        sb.AppendLine("        /// <summary>");
        sb.AppendLine("        /// Catalog of all middlewares in this assembly.");
        sb.AppendLine("        /// AgentBuilder automatically discovers and uses this at construction time.");
        sb.AppendLine("        /// </summary>");
        sb.AppendLine("        public static readonly MiddlewareFactory[] All = new MiddlewareFactory[]");
        sb.AppendLine("        {");

        foreach (var middleware in validMiddlewares)
        {
            var m = middleware!;
            var fullTypeName = m.FullTypeName;

            sb.AppendLine($"            new MiddlewareFactory(");
            sb.AppendLine($"                Name: \"{m.EffectiveName}\",");
            sb.AppendLine($"                MiddlewareType: typeof({fullTypeName}),");

            // CreateInstance: Only if has parameterless constructor
            if (m.HasParameterlessConstructor)
            {
                sb.AppendLine($"                CreateInstance: () => new {fullTypeName}(),");
            }
            else
            {
                sb.AppendLine($"                CreateInstance: null,");
            }

            // Config constructor support
            if (!string.IsNullOrEmpty(m.ConfigConstructorTypeName))
            {
                sb.AppendLine($"                ConfigType: typeof({m.ConfigConstructorTypeName}),");
                sb.AppendLine($"                // No JSON metadata is registered for middleware config type");
                sb.AppendLine($"                CreateFromConfig: json => new {fullTypeName}(JsonSerializer.Deserialize(json, GetJsonTypeInfo<{m.ConfigConstructorTypeName}>())!),");
            }
            else
            {
                sb.AppendLine($"                ConfigType: null,");
                sb.AppendLine($"                CreateFromConfig: null,");
            }

            sb.AppendLine($"                RequiresDI: {m.RequiresDI.ToString().ToLower()}");
            sb.AppendLine($"            ),");
        }

        sb.AppendLine("        };");
        sb.AppendLine();
        sb.AppendLine("        private static JsonTypeInfo<T> GetJsonTypeInfo<T>()");
        sb.AppendLine("        {");
        sb.AppendLine("            foreach (var resolver in AIJsonUtilities.DefaultOptions.TypeInfoResolverChain)");
        sb.AppendLine("            {");
        sb.AppendLine("                if (resolver.GetTypeInfo(typeof(T), AIJsonUtilities.DefaultOptions) is JsonTypeInfo<T> typeInfo)");
        sb.AppendLine("                    return typeInfo;");
        sb.AppendLine("            }");
        sb.AppendLine();
        sb.AppendLine("            if (HPDJsonContext.Default.GetTypeInfo(typeof(T)) is JsonTypeInfo<T> hpdTypeInfo)");
        sb.AppendLine("                return hpdTypeInfo;");
        sb.AppendLine();
        sb.AppendLine("            throw new NotSupportedException($\"No JSON metadata is registered for middleware config type '{typeof(T).FullName}'.\");");
        sb.AppendLine("        }");
        sb.AppendLine();
        sb.AppendLine("#pragma warning disable CA2255");
        sb.AppendLine("        [ModuleInitializer]");
        sb.AppendLine("        internal static void RegisterGeneratedCatalog()");
        sb.AppendLine("#pragma warning restore CA2255");
        sb.AppendLine("        {");
        sb.AppendLine("            HPD.Agent.AgentGeneratedRegistry.Register(middlewares: All);");
        sb.AppendLine("        }");
        sb.AppendLine("    }");
        sb.AppendLine("}");

        context.AddSource("HPD.Agent.Generated.MiddlewareRegistry.g.cs", sb.ToString());
    }

    // ========== END MIDDLEWARE SOURCE GENERATION ==========

    /// <summary>
    /// Generates the CreateToolHarness method using unified polymorphic ICapability iteration.
    /// Phase 4: Now the single unified generation path (old path removed).
    /// </summary>
    private static string GenerateCreateToolHarnessMethod(ToolHarnessInfo ToolHarness)
    {
        var sb = new StringBuilder();
        sb.AppendLine("    /// <summary>");
        sb.AppendLine($"    /// Creates an AIFunction list for the {ToolHarness.ClassName} ToolHarness.");
        sb.AppendLine("    /// </summary>");

        // Only include instance parameter if ToolHarness has capabilities that need it
        if (!ToolHarness.RequiresInstance)
        {
            sb.AppendLine($"    /// <param name=\"context\">The execution context (optional)</param>");
            sb.AppendLine($"    public static List<AIFunction> CreateToolHarness(IToolMetadata? context = null, HPDToolSerializationOptions? serialization = null)");
        }
        else
        {
            sb.AppendLine($"    /// <param name=\"instance\">The ToolHarness instance</param>");
            sb.AppendLine($"    /// <param name=\"context\">The execution context (optional)</param>");
            sb.AppendLine($"    public static List<AIFunction> CreateToolHarness({ToolHarness.ClassName} instance, IToolMetadata? context = null, HPDToolSerializationOptions? serialization = null)");
        }

        sb.AppendLine("    {");
        sb.AppendLine("        var functions = new List<AIFunction>();");
        sb.AppendLine();

        // Add collapse container registration if needed (BEFORE individual capabilities)
        var skillRegistrations = SkillCodeGenerator.GenerateSkillRegistrations(ToolHarness);
        if (!string.IsNullOrEmpty(skillRegistrations))
        {
            sb.Append(skillRegistrations);
        }

        // PHASE 2A: POLYMORPHIC DISPATCH
        // Each capability declares via EmitsIntoCreateTools whether it belongs in the functions list.
        // Capabilities with their own registration paths (Skills, MCPServers, etc.) return false.
        var createToolsCapabilities = ToolHarness.Capabilities.Where(c => c.EmitsIntoCreateTools);

        if (createToolsCapabilities.Any())
        {
            sb.AppendLine();
            sb.AppendLine("        // Register capabilities that emit into CreateTools");
            foreach (var capability in createToolsCapabilities)
            {
                // CRITICAL: Only generate conditional check if the evaluator method was generated
                // Conditional evaluators require a ContextTypeName to be set
                var hasConditionalEvaluator = capability.IsConditional &&
                                            capability is BaseCapability baseCapability &&
                                            baseCapability.HasTypedMetadata;

                if (hasConditionalEvaluator)
                {
                    sb.AppendLine($"        if (Evaluate{capability.Name}Condition(context))");
                    sb.AppendLine("        {");
                    sb.AppendLine($"            functions.Add({capability.GenerateRegistrationCode(ToolHarness)});");
                    sb.AppendLine("        }");
                }
                else
                {
                    sb.AppendLine($"        functions.Add({capability.GenerateRegistrationCode(ToolHarness)});");
                }
                sb.AppendLine();
            }
        }

        sb.AppendLine("        return functions;");
        sb.AppendLine("    }");
        return sb.ToString();
    }


    private static string GenerateToolHarnessRegistration(ToolHarnessInfo ToolHarness)
    {
        var sb = new StringBuilder();

        sb.AppendLine("// <auto-generated/>");
        sb.AppendLine("#pragma warning disable");
        sb.AppendLine();
        sb.AppendLine("using System;");
        sb.AppendLine("using System.Collections.Generic;");
        sb.AppendLine("using System.Threading;");
        sb.AppendLine("using System.Threading.Tasks;");
        sb.AppendLine("using System.Text.Json;");
        sb.AppendLine("using System.Text.Json.Nodes;");
        sb.AppendLine("using System.Text.Json.Serialization;");
        sb.AppendLine("using System.Text.Json.Serialization.Metadata;");
        sb.AppendLine("using Microsoft.Extensions.AI;");
        sb.AppendLine("using System.Linq;");
        sb.AppendLine("using System.Text;");
        // Add HPD.Agent namespace for AgentBuilder, ConversationThread, ToolMetadata, IToolMetadata, ValidationError, etc.
        sb.AppendLine("using HPD.Agent;");
        sb.AppendLine("using HPD.Agent.Middleware;");

        // Add using directive for the ToolHarness's namespace if it's not empty
        if (!string.IsNullOrEmpty(ToolHarness.Namespace))
        {
            sb.AppendLine($"using {ToolHarness.Namespace};");
        }

        sb.AppendLine();

        sb.AppendLine(GenerateArgumentsDtoAndContext(ToolHarness));

        sb.AppendLine("/// <summary>");
        sb.AppendLine($"/// Generated registration code for {ToolHarness.ClassName} ToolHarness.");
        sb.AppendLine("/// </summary>");
        sb.AppendLine($"[System.CodeDom.Compiler.GeneratedCodeAttribute(\"HPDToolSourceGenerator\", \"1.0.0.0\")]");
        sb.AppendLine($"public static partial class {ToolHarness.ClassName}Registration");
        sb.AppendLine("    {");

        // Generate GetReferencedToolHarnesses() and GetReferencedFunctions() if there are skills
        // PHASE 5: Use SkillCapabilities (fully populated with resolved references)
        if (ToolHarness.SkillCapabilities.Any())
        {
            sb.AppendLine(SkillCodeGenerator.GenerateGetReferencedToolHarnessesMethod(ToolHarness));
            sb.AppendLine();
            sb.AppendLine(SkillCodeGenerator.GenerateGetReferencedFunctionsMethod(ToolHarness));
            sb.AppendLine();
        }

        // Generate ToolHarness metadata accessor (always generated for consistency)
        // PHASE 5: Use SkillCapabilities instead of Skills
        if (ToolHarness.SkillCapabilities.Any())
        {
            sb.AppendLine(SkillCodeGenerator.UpdateToolMetadataWithSkills(ToolHarness, ""));
        }
        else
        {
            sb.AppendLine(GenerateToolMetadataMethod(ToolHarness));
        }
        sb.AppendLine();

        // Generate empty schema helper if ToolHarness is collapsed OR has skills
        // Note: Container function is generated in SkillCodeGenerator.GenerateAllSkillCode
        if (ToolHarness.IsCollapsed || ToolHarness.SkillCapabilities.Any())
        {
            sb.AppendLine(GenerateEmptySchemaMethod());
            sb.AppendLine();
        }

        sb.AppendLine(GenerateCreateToolHarnessMethod(ToolHarness));

        foreach (var function in ToolHarness.FunctionCapabilities)
        {
            sb.AppendLine();
            sb.AppendLine(AIBindingSourceEmitter.Emit(function));
        }

        // PHASE 2B: Generate context resolvers for ALL capabilities (Functions, Skills, SubAgents)
        // This enables Skills and SubAgents to use dynamic descriptions and conditionals (feature parity!)
        // Replaces the old DSL-based GenerateContextResolutionMethods() which only worked for Functions
        foreach (var capability in ToolHarness.Capabilities)
        {
            var resolvers = capability.GenerateContextResolvers();
            if (!string.IsNullOrEmpty(resolvers))
            {
                sb.AppendLine();
                sb.AppendLine(resolvers);
            }
        }

        // Generate skill code AND toolharness container (if ToolHarness is collapsed)
        // NOTE: Container can exist even if there are no skills (e.g., collapsed ToolHarness with only functions)
        if (ToolHarness.SkillCapabilities.Any() || ToolHarness.IsCollapsed)
        {
            sb.AppendLine(SkillCodeGenerator.GenerateAllSkillCode(ToolHarness));
        }

        // Generate MCP Server registrations
        if (ToolHarness.MCPServerCapabilities.Any())
        {
            sb.AppendLine();
            sb.AppendLine("        // MCP Server configurations");
            sb.AppendLine("        public static void CollectMcpServers(object __instance, System.Action<HPD.Agent.McpServerSource> __mcpCollector)");
            sb.AppendLine("        {");

            foreach (var mcp in ToolHarness.MCPServerCapabilities)
            {
                sb.AppendLine($"            {mcp.GenerateSourceCode(ToolHarness)}");
            }

            sb.AppendLine("        }");
        }

        // Generate CollectOpenApiSources method
        if (ToolHarness.OpenApiCapabilities.Any())
        {
            sb.AppendLine();
            sb.AppendLine("        // OpenAPI source collection");
            sb.AppendLine("        /// <summary>");
            sb.AppendLine("        /// Collects OpenAPI source registrations from [OpenApi] methods.");
            sb.AppendLine("        /// Called by AgentBuilder.CreateFunctionsFromCatalog() via ToolHarnessFactory.CollectOpenApiSources.");
            sb.AppendLine("        /// Config is passed as object so ToolHarnessFactory has no compile-time dep on HPD-Agent.OpenApi.");
            sb.AppendLine("        /// Cast to OpenApiConfig happens inside OpenApiLoader.LoadAllAsync.");
            sb.AppendLine("        /// </summary>");
            sb.AppendLine($"        public static void CollectOpenApiSources(object __instance, System.Action<string, object, string> __openApiCollector)");
            sb.AppendLine("        {");

            foreach (var openApi in ToolHarness.OpenApiCapabilities)
            {
                sb.AppendLine($"            {openApi.GenerateRegistrationCode(ToolHarness)}");
            }

            sb.AppendLine("        }");
        }

        sb.AppendLine("    }");

        return sb.ToString();
    }

    private static string GenerateArgumentsDtoAndContext(ToolHarnessInfo ToolHarness)
    {
        var sb = new StringBuilder();
        var contextSerializableTypes = new List<string>();

        // Generate SubAgentInputArgs if there are sub-agents (Collapsed per ToolHarness to avoid conflicts)
        if (ToolHarness.SubAgentCapabilities.Any())
        {
            sb.AppendLine(
$@"    /// <summary>
    /// Represents the arguments for sub-agent invocations, generated at compile-time.
    /// </summary>
    [System.CodeDom.Compiler.GeneratedCodeAttribute(""HPDToolSourceGenerator"", ""1.0.0.0"")]
    public class {ToolHarness.ClassName}SubAgentInputArgs
    {{
        [System.Text.Json.Serialization.JsonPropertyName(""taskName"")]
        [System.ComponentModel.Description(""A short name used to identify this delegated task and its child thread."")]
        public required string TaskName {{ get; set; }}

        [System.Text.Json.Serialization.JsonPropertyName(""input"")]
        [System.ComponentModel.Description(""The user's question or task for the sub-agent. Pass the full request here."")]
        public required string Input {{ get; set; }}
    }}

    /// <summary>
    /// Represents the arguments for model-choice sub-agent invocations, generated at compile-time.
    /// </summary>
    [System.CodeDom.Compiler.GeneratedCodeAttribute(""HPDToolSourceGenerator"", ""1.0.0.0"")]
    public class {ToolHarness.ClassName}SubAgentInputWithModeArgs
    {{
        [System.Text.Json.Serialization.JsonPropertyName(""taskName"")]
        [System.ComponentModel.Description(""A short name used to identify this delegated task and its child thread."")]
        public required string TaskName {{ get; set; }}

        [System.Text.Json.Serialization.JsonPropertyName(""input"")]
        [System.ComponentModel.Description(""The user's question or task for the sub-agent. Pass the full request here."")]
        public required string Input {{ get; set; }}

        [System.Text.Json.Serialization.JsonPropertyName(""invocationMode"")]
        [System.ComponentModel.Description(""Whether to wait for the result now or run in the background. Use synchronous unless the task can continue independently."")]
        public string? InvocationMode {{ get; set; }}
    }}
");
        }

        // Generate MultiAgentInputArgs if there are multi-agents (Collapsed per ToolHarness to avoid conflicts)
        if (ToolHarness.MultiAgentCapabilities.Any())
        {
            sb.AppendLine(
$@"    /// <summary>
    /// Represents the arguments for multi-agent workflow invocations, generated at compile-time.
    /// </summary>
    [System.CodeDom.Compiler.GeneratedCodeAttribute(""HPDToolSourceGenerator"", ""1.0.0.0"")]
    public class {ToolHarness.ClassName}MultiAgentInputArgs
    {{
        [System.Text.Json.Serialization.JsonPropertyName(""input"")]
        [System.ComponentModel.Description(""The user's question or task to process through the multi-agent workflow. Pass the full user message here."")]
        public string Input {{ get; set; }} = string.Empty;
    }}

    /// <summary>
    /// Represents the arguments for model-choice multi-agent workflow invocations, generated at compile-time.
    /// </summary>
    [System.CodeDom.Compiler.GeneratedCodeAttribute(""HPDToolSourceGenerator"", ""1.0.0.0"")]
    public class {ToolHarness.ClassName}MultiAgentInputWithModeArgs
    {{
        [System.Text.Json.Serialization.JsonPropertyName(""input"")]
        [System.ComponentModel.Description(""The user's question or task to process through the multi-agent workflow. Pass the full user message here."")]
        public string Input {{ get; set; }} = string.Empty;

        [System.Text.Json.Serialization.JsonPropertyName(""invocationMode"")]
        [System.ComponentModel.Description(""Whether to wait for the workflow result now or run it in the background. Use synchronous unless the workflow can continue independently."")]
        public string? InvocationMode {{ get; set; }}
    }}
");
        }

        foreach (var function in ToolHarness.FunctionCapabilities)
        {
            if (!function.Parameters.Any(p => p.IsModelFacing)) continue;

            var dtoName = $"{function.Name}Args";
            contextSerializableTypes.Add(dtoName);

            sb.AppendLine(
$@"    /// <summary>
    /// Represents the arguments for the {function.Name} function, generated at compile-time.
    /// </summary>
    [System.CodeDom.Compiler.GeneratedCodeAttribute(""HPDToolSourceGenerator"", ""1.0.0.0"")]
    public class {dtoName}
    {{");

            foreach (var param in function.Parameters.Where(p => p.IsModelFacing))
            {
                sb.AppendLine($"        [System.Text.Json.Serialization.JsonPropertyName(\"{param.Name}\")]");
                if (!string.IsNullOrEmpty(param.Description))
                {
                    sb.AppendLine($"        [System.ComponentModel.Description(\"{EscapeForAttribute(param.Description)}\")]");
                }
                var identifier = SyntaxFacts.GetKeywordKind(param.Name) is SyntaxKind.None ? param.Name : "@" + param.Name;
                sb.AppendLine($"        public {param.Type} {identifier} {{ get; set; }}{ParameterAnalyzer.GetDefaultInitializer(param)}");
            }

            sb.AppendLine("    }");
            sb.AppendLine();
        }

        // Note: We cannot generate JsonSerializerContext here because the System.Text.Json source generator
        // doesn't process attributes from other source generators in the same compilation.
        // Function schemas are emitted directly from parameter metadata to keep generated tools AOT-friendly.

        return sb.ToString();
    }

    private static string EscapeForAttribute(string value)
    {
        return value.Replace("\\", "\\\\").Replace("\"", "\\\"");
    }

    private static string ExtractStringLiteral(ExpressionSyntax? expression)
    {
        if (expression is LiteralExpressionSyntax literal && literal.Token.IsKind(SyntaxKind.StringLiteralToken))
        {
            return literal.Token.ValueText;
        }
        return "";
    }

    private static string GetNamespace(SyntaxNode node)
    {
        var parent = node.Parent;
        while (parent != null)
        {
            if (parent is NamespaceDeclarationSyntax namespaceDecl)
                return namespaceDecl.Name.ToString();
            if (parent is FileScopedNamespaceDeclarationSyntax fileScopedNamespace)
                return fileScopedNamespace.Name.ToString();
            parent = parent.Parent;
        }
        return "";
    }

    private static string GetReturnType(MethodDeclarationSyntax method, SemanticModel semanticModel)
    {
        return method.ReturnType.ToString();
    }

    private static bool IsAsyncMethod(MethodDeclarationSyntax method)
    {
        return method.Modifiers.Any(SyntaxKind.AsyncKeyword) ||
               method.ReturnType.ToString().StartsWith("Task");
    }

    // V3.0 New Helper Methods

    /// <summary>
    /// Extracts context type from AIFunction&lt;TMetadata&gt; attribute.
    /// </summary>
    private static (string? contextTypeName, bool isGeneric) GetAIFunctionContextType(MethodDeclarationSyntax method, SemanticModel semanticModel)
    {
        var aiFunctionAttributes = method.AttributeLists
            .SelectMany(attrList => attrList.Attributes)
            .Where(attr => attr.Name.ToString().Contains("AIFunction"));

        foreach (var attr in aiFunctionAttributes)
        {
            var symbolInfo = semanticModel.GetSymbolInfo(attr);
            if (symbolInfo.Symbol is IMethodSymbol methodSymbol)
            {
                var attributeType = methodSymbol.ContainingType;

                // Check if it's the generic AIFunction<TMetadata>
                if (attributeType.IsGenericType && attributeType.TypeArguments.Length == 1)
                {
                    var contextType = attributeType.TypeArguments[0];
                    return (contextType.Name, true);
                }
            }
        }

        return (null, false);
    }

    /// <summary>
    /// Gets conditional expression from ConditionalFunction attribute.
    /// </summary>
    private static string? GetConditionalExpression(MethodDeclarationSyntax method)
    {
        var conditionalAttributes = method.AttributeLists
            .SelectMany(attrList => attrList.Attributes)
            .Where(attr => attr.Name.ToString().Contains("ConditionalFunction"));

        foreach (var attr in conditionalAttributes)
        {
            var arguments = attr.ArgumentList?.Arguments;
            if (arguments.HasValue && arguments.Value.Count >= 1)
            {
                return ExtractStringLiteral(arguments.Value[0].Expression);
            }
        }

        return null;
    }

    /// <summary>
    /// Validates that template properties exist on the context type.
    /// </summary>
    private static void ValidateTemplateProperties(SourceProductionContext context, HPD.Agent.SourceGenerator.Capabilities.FunctionCapability function, ITypeSymbol contextType, SyntaxNode location)
    {
        // Validate function description templates
        if (function.HasDynamicDescription)
        {
            ValidateTemplateString(context, function.Description, contextType, location, $"function {function.Name} description");
        }

        // Validate parameter description templates
        foreach (var parameter in function.Parameters.Where(p => p.HasDynamicDescription))
        {
            ValidateTemplateString(context, parameter.Description, contextType, location, $"parameter {parameter.Name} description");
        }
    }

    /// <summary>
    /// Validates a single template string for property existence.
    /// </summary>
    private static void ValidateTemplateString(SourceProductionContext context, string template, ITypeSymbol contextType, SyntaxNode location, string locationDescription)
    {
        var regex = new Regex(@"\{context\.([a-zA-Z_][a-zA-Z0-9_]*)\}");
        var availableProperties = contextType.GetMembers()
            .OfType<IPropertySymbol>()
            .Where(p => p.DeclaredAccessibility == Accessibility.Public)
            .Select(p => p.Name)
            .ToList();

        foreach (Match match in regex.Matches(template))
        {
            var propertyName = match.Groups[1].Value;
            if (!availableProperties.Contains(propertyName))
            {
                var diagnostic = Diagnostic.Create(
                    new DiagnosticDescriptor(
                        "HPD001",
                        "Invalid template property",
                        $"Property '{propertyName}' not found in {contextType.Name} for {locationDescription}. Available properties: {string.Join(", ", availableProperties)}",
                        "HPD.Template",
                        DiagnosticSeverity.Error,
                        isEnabledByDefault: true,
                        description: "Template properties must exist as public properties on the context type."),
                    location.GetLocation());
                context.ReportDiagnostic(diagnostic);
            }
        }
    }

    /// <summary>
    /// Validates conditional expressions for property existence, syntax, and type compatibility.
    /// </summary>
    private static void ValidateConditionalExpression(SourceProductionContext context, string expression, ITypeSymbol contextType, SyntaxNode location, string locationDescription)
    {
        // First validate syntax
        ValidateExpressionSyntax(context, expression, location);

        // Then validate type compatibility
        ValidateTypeCompatibility(context, expression, contextType, location);

        // Finally validate property existence (existing logic)
        var propertyNames = ExtractPropertyNames(expression);
        var availableProperties = contextType.GetMembers()
            .OfType<IPropertySymbol>()
            .Where(p => p.DeclaredAccessibility == Accessibility.Public)
            .Select(p => p.Name)
            .ToList();

        foreach (var propertyName in propertyNames)
        {
            if (!availableProperties.Contains(propertyName))
            {
                var diagnostic = Diagnostic.Create(
                    new DiagnosticDescriptor(
                        "HPD002",
                        "Invalid conditional property",
                        $"Property '{propertyName}' not found in {contextType.Name} for {locationDescription}. Available properties: {string.Join(", ", availableProperties)}",
                        "HPD.Conditional",
                        DiagnosticSeverity.Error,
                        isEnabledByDefault: true,
                        description: "Conditional expressions must reference properties that exist on the context type."),
                    location.GetLocation());
                context.ReportDiagnostic(diagnostic);
            }
        }
    }

    /// <summary>
    /// Extracts property names from conditional expressions.
    /// </summary>
    private static HashSet<string> ExtractPropertyNames(string expression)
    {
        var propertyNames = new HashSet<string>();
        var identifierRegex = new Regex(@"\b[A-Za-z_][A-Za-z0-9_]*\b");
        var keywords = new HashSet<string> { "true", "false", "null", "&&", "||", "!", "==", "!=", "<", ">", "<=", ">=" };

        foreach (Match match in identifierRegex.Matches(expression))
        {
            var identifier = match.Value;
            if (!keywords.Contains(identifier.ToLower()) && !int.TryParse(identifier, out _))
            {
                propertyNames.Add(identifier);
            }
        }

        return propertyNames;
    }

    /// <summary>
    /// NEW: Check if function should use generic AIFunction attribute.
    /// </summary>
    private static void ValidateFunctionContextUsage(SourceProductionContext context, HPD.Agent.SourceGenerator.Capabilities.FunctionCapability function, MethodDeclarationSyntax method)
    {
        // Check if function uses dynamic features but no generic context
        bool usesDynamicFeatures = function.HasDynamicDescription ||
                                  function.IsConditional ||
                                  function.HasConditionalParameters;

        bool hasGenericContext = !string.IsNullOrEmpty(function.ContextTypeName);

        if (usesDynamicFeatures && !hasGenericContext)
        {
            var diagnostic = Diagnostic.Create(
                new DiagnosticDescriptor(
                    "HPD003",
                    "Missing context type",
                    $"Function '{function.Name}' uses dynamic features but lacks AIFunction<TMetadata> attribute. Use [AIFunction<YourContext>] instead of [AIFunction].",
                    "HPD.Context",
                    DiagnosticSeverity.Warning,
                    isEnabledByDefault: true,
                    description: "Functions with conditional logic or dynamic descriptions need a typed context."),
                method.GetLocation());
            context.ReportDiagnostic(diagnostic);
        }
    }

    /// <summary>
    /// NEW: Validates expression syntax for proper structure and balanced parentheses.
    /// </summary>
    private static void ValidateExpressionSyntax(SourceProductionContext context, string expression, SyntaxNode location)
    {
        try
        {
            // Simple syntax checks
            if (expression.Contains("&&&&") || expression.Contains("||||"))
            {
                ReportError(context, "Invalid operator sequence", location);
                return;
            }

            // Check balanced parentheses
            var openCount = expression.Count(c => c == '(');
            var closeCount = expression.Count(c => c == ')');
            if (openCount != closeCount)
            {
                ReportError(context, "Unbalanced parentheses", location);
                return;
            }

            // Check for empty expressions
            if (string.IsNullOrWhiteSpace(expression))
            {
                ReportError(context, "Empty expression", location);
                return;
            }

            // Check for invalid characters
            var invalidChars = expression.Where(c => !char.IsLetterOrDigit(c) && !"()&|!<>=. _".Contains(c)).ToArray();
            if (invalidChars.Any())
            {
                ReportError(context, $"Invalid characters in expression: {string.Join(", ", invalidChars.Distinct())}", location);
                return;
            }
        }
        catch
        {
            ReportError(context, "Invalid expression syntax", location);
        }
    }

    /// <summary>
    /// NEW: Validates type compatibility between operations and property types.
    /// </summary>
    private static void ValidateTypeCompatibility(SourceProductionContext context, string expression,
        ITypeSymbol contextType, SyntaxNode location)
    {
        try
        {
            // Parse expression and check each operation
            var tokens = ParseExpressionTokens(expression);
            foreach (var token in tokens)
            {
                if (token.Type == TokenType.Comparison)
                {
                    var property = GetPropertyType(contextType, token.PropertyName);
                    if (property != null && !IsValidOperation(property, token.Operator))
                    {
                        ReportError(context,
                            $"Cannot use operator '{token.Operator}' on property '{token.PropertyName}' of type {property.Name}",
                            location);
                    }
                }
            }
        }
        catch
        {
            // If parsing fails, skip type compatibility check
            // Syntax validation will catch basic syntax errors
        }
    }

    /// <summary>
    /// Helper: Simple expression tokenizer for validation.
    /// </summary>
    private static List<ExpressionToken> ParseExpressionTokens(string expression)
    {
        var tokens = new List<ExpressionToken>();

        // Simple regex-based parsing for basic validation
        // This is a simplified version - could be enhanced with proper expression parsing
        var comparisonPattern = @"(\w+(?:\.\w+)*)\s*([<>=!]+)\s*(\w+|""[^""]*"")";
        var matches = System.Text.RegularExpressions.Regex.Matches(expression, comparisonPattern);

        foreach (System.Text.RegularExpressions.Match match in matches)
        {
            tokens.Add(new ExpressionToken
            {
                Type = TokenType.Comparison,
                PropertyName = match.Groups[1].Value,
                Operator = match.Groups[2].Value,
                Value = match.Groups[3].Value
            });
        }

        return tokens;
    }

    /// <summary>
    /// Helper: Gets the type of a property from the context type.
    /// </summary>
    private static ITypeSymbol? GetPropertyType(ITypeSymbol contextType, string propertyName)
    {
        // Handle nested property access (e.g., "context.User.Name")
        var parts = propertyName.Split('.');
        var currentType = contextType;

        foreach (var part in parts)
        {
            var property = currentType.GetMembers(part)
                .OfType<IPropertySymbol>()
                .FirstOrDefault();

            if (property == null)
                return null;

            currentType = property.Type;
        }

        return currentType;
    }

    /// <summary>
    /// Helper: Checks if an operator is valid for a given property type.
    /// </summary>
    private static bool IsValidOperation(ITypeSymbol propertyType, string operatorSymbol)
    {
        var typeName = propertyType.Name;

        return operatorSymbol switch
        {
            ">" or "<" or ">=" or "<=" => IsNumericType(typeName) || IsComparableType(typeName),
            "==" or "!=" => true, // All types support equality
            _ => false
        };
    }

    /// <summary>
    /// Helper: Checks if a type is numeric.
    /// </summary>
    private static bool IsNumericType(string typeName)
    {
        return typeName switch
        {
            "Int32" or "Int64" or "Double" or "Single" or "Decimal" or "Byte" or "SByte" or "Int16" or "UInt16" or "UInt32" or "UInt64" => true,
            _ => false
        };
    }

    /// <summary>
    /// Helper: Checks if a type implements IComparable.
    /// </summary>
    private static bool IsComparableType(string typeName)
    {
        return typeName switch
        {
            "String" or "DateTime" or "DateTimeOffset" or "TimeSpan" => true,
            _ => false
        };
    }

    /// <summary>
    /// Helper: Reports a diagnostic error.
    /// </summary>
    private static void ReportError(SourceProductionContext context, string message, SyntaxNode location)
    {
        var diagnostic = Diagnostic.Create(
            new DiagnosticDescriptor(
                "HPD004",
                "Expression validation error",
                message,
                "HPD.Validation",
                DiagnosticSeverity.Error,
                isEnabledByDefault: true),
            location.GetLocation());
        context.ReportDiagnostic(diagnostic);
    }

    // Helper to detect [RequiresPermission] attribute
    private static bool GetRequiresPermission(MethodDeclarationSyntax method)
    {
        return method.AttributeLists
            .SelectMany(attrList => attrList.Attributes)
            .Any(attr => attr.Name.ToString().Contains("RequiresPermission"));
    }

    /// <summary>
    /// Detects [Collapse] attribute on a class and extracts its configuration.
    /// Supports dual-context (FunctionResult, SystemPrompt).
    /// Analyzes expressions to determine if they're static or instance methods/properties.
    /// </summary>
    private static (
        bool isCollapsed,
        string? containerDescription,
        string? FunctionResult,
        string? FunctionResultExpression,
        bool FunctionResultIsStatic,
        string? SystemPrompt,
        string? SystemPromptExpression,
        bool SystemPromptIsStatic,
        List<Diagnostic> diagnostics,
        string? customName
    ) GetCollapseAttribute(ClassDeclarationSyntax classDecl, SemanticModel semanticModel)
    {
        // Look for [Collapse] attribute
        var allAttributes = classDecl.AttributeLists
            .SelectMany(attrList => attrList.Attributes);

        var attr = allAttributes.FirstOrDefault(attr =>
            attr.Name.ToString() == "Collapse" || attr.Name.ToString() == "CollapseAttribute");

        if (attr != null)
        {
            var arguments = attr.ArgumentList?.Arguments;

            string? description = null;
            string? funcResultCtx = null, funcResultExpr = null;
            bool funcResultIsStatic = true;
            string? sysPromptCtx = null, sysPromptExpr = null;
            bool sysPromptIsStatic = true;
            bool hasDescription = false;

            // [Collapse] attribute handling
            // Constructor forms:
            // - [Collapse("description")] - collapsible with description
            // - [Collapse("description", FunctionResult = "...")] - collapsible with contexts
            // - [Collapse("description", SystemPrompt = "...")] - collapsible with system prompt
            // - [Collapse("description", FunctionResult = "...", SystemPrompt = "...")] - full dual-context
            // Runtime override: CollapsingConfig.NeverCollapse to prevent collapsing at runtime

            if (arguments.HasValue)
            {
                foreach (var arg in arguments.Value)
                {
                    var argName = arg.NameEquals?.Name.Identifier.ValueText
                               ?? arg.NameColon?.Name.Identifier.ValueText;

                    if (argName == "Description")
                    {
                        description = ExtractStringLiteral(arg.Expression);
                        hasDescription = true;
                    }
                    else if (argName == "FunctionResult")
                    {
                        if (arg.Expression is LiteralExpressionSyntax literal && literal.Token.IsKind(SyntaxKind.StringLiteralToken))
                        {
                            funcResultCtx = literal.Token.ValueText;
                        }
                        else
                        {
                            funcResultExpr = arg.Expression.ToString();
                            funcResultIsStatic = IsExpressionStatic(arg.Expression, semanticModel, classDecl);
                        }
                    }
                    else if (argName == "SystemPrompt")
                    {
                        if (arg.Expression is LiteralExpressionSyntax literal && literal.Token.IsKind(SyntaxKind.StringLiteralToken))
                        {
                            sysPromptCtx = literal.Token.ValueText;
                        }
                        else
                        {
                            sysPromptExpr = arg.Expression.ToString();
                            sysPromptIsStatic = IsExpressionStatic(arg.Expression, semanticModel, classDecl);
                        }
                    }
                    else if (argName == null && arg == arguments.Value[0])
                    {
                        // First positional argument is description (enables collapsing)
                        description = ExtractStringLiteral(arg.Expression);
                        hasDescription = true;
                    }
                }
            }

            // Collapse always requires a description
            bool isCollapsed = hasDescription;

            // If attribute is present but no description, this is an error
            if (!isCollapsed)
            {
                return (false, null, null, null, true, null, null, true, new List<Diagnostic>(), null);
            }

            // Validate and return
            var diagnostics = ValidateDualContextConfiguration(
                funcResultCtx, funcResultExpr,
                sysPromptCtx, sysPromptExpr,
                classDecl, semanticModel);

            return (true, description, funcResultCtx, funcResultExpr, funcResultIsStatic, sysPromptCtx, sysPromptExpr, sysPromptIsStatic, diagnostics, null);
        }

        return (false, null, null, null, true, null, null, true, new List<Diagnostic>(), null);
    }

    /// <summary>
    /// Extracts middleware type info from <c>[Collapse(Middlewares = [typeof(T1), typeof(T2)])]</c>.
    /// Emits ordered, explicitly owned activation descriptors for middleware declared by a ToolHarness.
    /// </summary>
    private static List<CollapseMiddlewareEntry>? GetCollapseMiddlewareEntries(
            ClassDeclarationSyntax classDecl,
            SemanticModel semanticModel,
            List<Diagnostic> diagnostics)
    {
        var lifetimeAttributeType = semanticModel.Compilation.GetTypeByMetadataName(
            "HPD.Agent.ToolHarnessMiddlewareLifetimeAttribute");
        var jsonContextAttributeType = semanticModel.Compilation.GetTypeByMetadataName(
            "HPD.Agent.ToolHarnessJsonContextAttribute");
        var jsonSerializableAttributeType = semanticModel.Compilation.GetTypeByMetadataName(
            "System.Text.Json.Serialization.JsonSerializableAttribute");
        var agentResourceAttributeType = semanticModel.Compilation.GetTypeByMetadataName(
            "HPD.Agent.ToolHarnessAgentResourceAttribute");

        string? GetJsonContextType(ITypeSymbol configType)
        {
            var attribute = configType.GetAttributes().SingleOrDefault(candidate =>
                jsonContextAttributeType is not null &&
                SymbolEqualityComparer.Default.Equals(candidate.AttributeClass, jsonContextAttributeType));
            if (attribute is null ||
                attribute.ConstructorArguments.Length != 1 ||
                attribute.ConstructorArguments[0].Value is not INamedTypeSymbol contextType)
                return null;

            var containsMetadata = contextType.GetAttributes().Any(candidate =>
                jsonSerializableAttributeType is not null &&
                SymbolEqualityComparer.Default.Equals(candidate.AttributeClass, jsonSerializableAttributeType) &&
                candidate.ConstructorArguments.Length > 0 &&
                SymbolEqualityComparer.Default.Equals(candidate.ConstructorArguments[0].Value as ITypeSymbol, configType));
            return containsMetadata
                ? contextType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)
                : null;
        }

        var allAttributes = classDecl.AttributeLists
            .SelectMany(attrList => attrList.Attributes);

        var attr = allAttributes.FirstOrDefault(a =>
            a.Name.ToString() == "Collapse" || a.Name.ToString() == "CollapseAttribute");

        if (attr?.ArgumentList == null)
            return null;

        AttributeArgumentSyntax? middlewaresArg = null;
        foreach (var arg in attr.ArgumentList.Arguments)
        {
            var name = arg.NameEquals?.Name.Identifier.ValueText;
            if (name == "Middlewares")
            {
                middlewaresArg = arg;
                break;
            }
        }

        if (middlewaresArg == null)
            return null;

        // Expression should be an array initializer: [typeof(T1), typeof(T2)]
        // Represented as CollectionExpressionSyntax (C# 12) or ArrayCreationExpression / ImplicitArrayCreation
        var entries = new List<CollapseMiddlewareEntry>();
        var seenTypes = new HashSet<string>(StringComparer.Ordinal);
        var location = classDecl.GetLocation();

        System.Collections.Generic.IEnumerable<ExpressionSyntax>? elements = null;

        if (middlewaresArg.Expression is CollectionExpressionSyntax collExpr)
        {
            elements = collExpr.Elements.OfType<ExpressionElementSyntax>().Select(e => e.Expression);
        }
        else if (middlewaresArg.Expression is ImplicitArrayCreationExpressionSyntax implicitArray)
        {
            elements = implicitArray.Initializer.Expressions;
        }
        else if (middlewaresArg.Expression is ArrayCreationExpressionSyntax arrayExpr
                 && arrayExpr.Initializer != null)
        {
            elements = arrayExpr.Initializer.Expressions;
        }

        if (elements == null)
            return null;

        foreach (var elem in elements)
        {
            // Each element should be typeof(SomeMiddlewareType)
            if (elem is not TypeOfExpressionSyntax typeofExpr)
            {
                diagnostics.Add(Diagnostic.Create(
                    new DiagnosticDescriptor(
                        "HPDAG0201",
                        "Invalid Middlewares element",
                        "ToolHarness '{0}': Middlewares array must contain only typeof() expressions.",
                        "HPDAgent.SourceGenerator",
                        DiagnosticSeverity.Error,
                        isEnabledByDefault: true),
                    location,
                    classDecl.Identifier.ValueText));
                continue;
            }

            var typeInfo = semanticModel.GetTypeInfo(typeofExpr.Type);
            if (typeInfo.Type == null)
            {
                // Type could not be resolved — skip silently (will be a compile error anyway)
                continue;
            }

            var fqn = typeInfo.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);

            var markerType = semanticModel.Compilation.GetTypeByMetadataName(
                "HPD.Agent.Middleware.IToolHarnessMiddleware");
            bool implementsToolHarnessMarker = markerType is not null && typeInfo.Type.AllInterfaces.Any(i =>
                SymbolEqualityComparer.Default.Equals(i, markerType));

            if (!implementsToolHarnessMarker)
            {
                diagnostics.Add(Diagnostic.Create(
                    new DiagnosticDescriptor(
                        "HPDAG0203",
                        "Middleware type does not implement IToolHarnessMiddleware",
                        "ToolHarness '{0}': Type '{1}' is registered as scoped middleware but does not implement IToolHarnessMiddleware. " +
                        "Implement IToolHarnessMiddleware; ToolHarness middleware is an explicit v9 contract.",
                        "HPDAgent.SourceGenerator",
                        DiagnosticSeverity.Error,
                        isEnabledByDefault: true),
                    location,
                    classDecl.Identifier.ValueText,
                    typeInfo.Type.Name));
                continue;
            }

            if (typeInfo.Type is not INamedTypeSymbol namedType)
                continue;

            if (!seenTypes.Add(fqn))
            {
                diagnostics.Add(Diagnostic.Create(
                    new DiagnosticDescriptor("HPDAG0205", "Duplicate ToolHarness middleware",
                        "ToolHarness '{0}' declares middleware type '{1}' more than once.",
                        "HPDAgent.SourceGenerator", DiagnosticSeverity.Error, true),
                    location, classDecl.Identifier.ValueText, namedType.Name));
                continue;
            }

            if (namedType.IsAbstract || namedType.IsGenericType ||
                namedType.DeclaredAccessibility is not Accessibility.Public and not Accessibility.Internal)
            {
                diagnostics.Add(Diagnostic.Create(
                    new DiagnosticDescriptor("HPDAG0206", "Invalid ToolHarness middleware type",
                        "ToolHarness '{0}' middleware '{1}' must be non-abstract, closed, and accessible.",
                        "HPDAgent.SourceGenerator", DiagnosticSeverity.Error, true),
                    location, classDecl.Identifier.ValueText, namedType.Name));
                continue;
            }

            var lifetimeAttribute = namedType.GetAttributes().FirstOrDefault(candidate =>
                lifetimeAttributeType is not null &&
                SymbolEqualityComparer.Default.Equals(candidate.AttributeClass, lifetimeAttributeType));
            var servicesOwned = false;
            if (lifetimeAttribute is not null)
            {
                if (lifetimeAttribute.ConstructorArguments.Length != 1 ||
                    lifetimeAttribute.ConstructorArguments[0].Value is not int ownership || ownership is < 0 or > 1)
                {
                    diagnostics.Add(Diagnostic.Create(
                        new DiagnosticDescriptor("HPDAG0207", "Invalid ToolHarness middleware ownership",
                            "ToolHarness '{0}' middleware '{1}' has invalid lifetime metadata.",
                            "HPDAgent.SourceGenerator", DiagnosticSeverity.Error, true),
                        location, classDecl.Identifier.ValueText, namedType.Name));
                    continue;
                }
                servicesOwned = ownership == 1;
            }

            var publicConstructors = namedType.InstanceConstructors
                .Where(static constructor => constructor.DeclaredAccessibility == Accessibility.Public)
                .ToArray();
            var resourceConstructors = publicConstructors.Where(constructor =>
                constructor.Parameters.Length == 3 &&
                constructor.Parameters[0].Type.GetAttributes().Any(attribute =>
                    agentResourceAttributeType is not null &&
                    SymbolEqualityComparer.Default.Equals(attribute.AttributeClass, agentResourceAttributeType)) &&
                constructor.Parameters[1].Type.SpecialType == SpecialType.System_String &&
                (constructor.Parameters[2].Type.Name.EndsWith("Config") || constructor.Parameters[2].Type.Name.EndsWith("Options")))
                .ToArray();
            var configConstructors = publicConstructors.Where(static constructor =>
                constructor.Parameters.Length == 1 &&
                (constructor.Parameters[0].Type.Name.EndsWith("Config") || constructor.Parameters[0].Type.Name.EndsWith("Options")))
                .ToArray();
            var hasParameterlessCtor = publicConstructors.Any(static constructor => constructor.Parameters.IsEmpty) || namedType.IsValueType;
            var generatedShapeCount = (resourceConstructors.Length > 0 ? 1 : 0) +
                (configConstructors.Length > 0 ? 1 : 0) + (hasParameterlessCtor ? 1 : 0);

            if (servicesOwned)
            {
                // Explicit Services ownership suppresses every constructor-based generated path.
                // The child container may legitimately construct the service through any DI shape,
                // including an implicit/public parameterless constructor.
                entries.Add(new CollapseMiddlewareEntry(namedType.Name, fqn, null, null, true));
                continue;
            }

            if (generatedShapeCount > 1 || resourceConstructors.Length > 1 || configConstructors.Length > 1)
            {
                diagnostics.Add(Diagnostic.Create(
                    new DiagnosticDescriptor("HPDAG0211", "Ambiguous middleware activation shape",
                        "ToolHarness '{0}' execution-owned middleware '{1}' must expose exactly one supported generated activation shape.",
                        "HPDAgent.SourceGenerator", DiagnosticSeverity.Error, true),
                    location, classDecl.Identifier.ValueText, namedType.Name));
                continue;
            }

            // Agent-owned resource constructor: (attributed resource, canonical workspace identity, config/options).
            var resourceCtor = resourceConstructors.SingleOrDefault();
            if (resourceCtor is not null)
            {
                var resourceAttribute = resourceCtor.Parameters[0].Type.GetAttributes()
                    .Single(attribute => agentResourceAttributeType is not null &&
                        SymbolEqualityComparer.Default.Equals(attribute.AttributeClass, agentResourceAttributeType));
                if (resourceAttribute.ConstructorArguments.Length != 1 ||
                    resourceAttribute.ConstructorArguments[0].Value is not INamedTypeSymbol implementationType ||
                    implementationType.IsAbstract || implementationType.IsGenericType)
                {
                    diagnostics.Add(Diagnostic.Create(
                        new DiagnosticDescriptor("HPDAG0208", "Invalid Agent resource declaration",
                            "ToolHarness '{0}' middleware '{1}' requires an Agent resource with a concrete implementation type.",
                            "HPDAgent.SourceGenerator", DiagnosticSeverity.Error, true),
                        location, classDecl.Identifier.ValueText, namedType.Name));
                    continue;
                }

                var configType = resourceCtor.Parameters[2].Type;
                var jsonContextTypeFqn = GetJsonContextType(configType);
                if (jsonContextTypeFqn is null)
                {
                    diagnostics.Add(Diagnostic.Create(
                        new DiagnosticDescriptor("HPDAG0209", "Missing middleware configuration JSON metadata",
                            "ToolHarness '{0}' middleware '{1}' configuration type '{2}' must declare ToolHarnessJsonContext(typeof(...)) for Native AOT activation.",
                            "HPDAgent.SourceGenerator", DiagnosticSeverity.Error, true),
                        location, classDecl.Identifier.ValueText, namedType.Name, configType.Name));
                    continue;
                }
                entries.Add(new CollapseMiddlewareEntry(
                    namedType.Name,
                    fqn,
                    configType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                    jsonContextTypeFqn,
                    false,
                    resourceCtor.Parameters[0].Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                    implementationType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                    true));
                continue;
            }

            // §parameterless path: public parameterless constructor or value type
            if (hasParameterlessCtor)
            {
                entries.Add(new CollapseMiddlewareEntry(namedType.Name, fqn, null, null, false));
                continue;
            }

            // §5A path: single public constructor whose sole parameter type name ends with "Config"
            var singleConfigCtor = configConstructors.SingleOrDefault();

            if (singleConfigCtor != null)
            {
                var configTypeFqn = singleConfigCtor.Parameters[0].Type
                    .ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
                var jsonContextTypeFqn = GetJsonContextType(singleConfigCtor.Parameters[0].Type);
                if (jsonContextTypeFqn is null)
                {
                    diagnostics.Add(Diagnostic.Create(
                        new DiagnosticDescriptor("HPDAG0209", "Missing middleware configuration JSON metadata",
                            "ToolHarness '{0}' middleware '{1}' configuration type '{2}' must declare ToolHarnessJsonContext(typeof(...)) for Native AOT activation.",
                            "HPDAgent.SourceGenerator", DiagnosticSeverity.Error, true),
                        location, classDecl.Identifier.ValueText, namedType.Name, singleConfigCtor.Parameters[0].Type.Name));
                    continue;
                }
                entries.Add(new CollapseMiddlewareEntry(
                    SimpleName: namedType.Name,
                    FullyQualifiedTypeName: fqn,
                    ConfigTypeFqn: configTypeFqn,
                    JsonContextTypeFqn: jsonContextTypeFqn,
                    ServicesOwned: false,
                    ConfigHasGeneratedDefault:
                        singleConfigCtor.Parameters[0].HasExplicitDefaultValue &&
                        singleConfigCtor.Parameters[0].Type is INamedTypeSymbol configNamedType &&
                        configNamedType.InstanceConstructors.Any(static constructor =>
                            constructor.DeclaredAccessibility == Accessibility.Public &&
                            constructor.Parameters.IsEmpty)));
                continue;
            }

            // Neither form found — error, recommend DI path
            diagnostics.Add(Diagnostic.Create(
                new DiagnosticDescriptor(
                    "HPDAG0204",
                    "Scoped middleware requires a parameterless or single-config-parameter constructor",
                    "ToolHarness '{0}': Type '{1}' has no public parameterless constructor and no single-Config/Options-parameter constructor. " +
                    "Use an explicit ToolHarnessMiddlewareLifetime(Services) declaration for child-scope activation.",
                    "HPDAgent.SourceGenerator",
                    DiagnosticSeverity.Error,
                    isEnabledByDefault: true),
                location,
                classDecl.Identifier.ValueText,
                typeInfo.Type.Name));
        }

        return entries.Count > 0 ? entries : null;
    }

    /// <summary>
    /// Validates dual-context configuration to prevent conflicting settings.
    /// PHASE 2C: Compile-time validation for dual-context architecture.
    /// </summary>
    private static List<Diagnostic> ValidateDualContextConfiguration(
        string? funcResultCtx, string? funcResultExpr,
        string? sysPromptCtx, string? sysPromptExpr,
        ClassDeclarationSyntax classDecl, SemanticModel semanticModel)
    {
        var diagnostics = new List<Diagnostic>();
        var location = classDecl.GetLocation();

        // Validate: Can't have both literal AND expression for FunctionResult
        if (!string.IsNullOrEmpty(funcResultCtx) && !string.IsNullOrEmpty(funcResultExpr))
        {
            var diagnostic = Diagnostic.Create(
                new DiagnosticDescriptor(
                    "HPDAG0101",
                    "Conflicting FunctionResult configuration",
                    "ToolHarness '{0}' specifies both FunctionResult literal and expression. Use one or the other, not both.",
                    "HPDAgent.SourceGenerator",
                    DiagnosticSeverity.Error,
                    isEnabledByDefault: true,
                    description: "FunctionResult can be either a literal string or an expression, but not both. " +
                                "Literal: FunctionResult = \"text\". Expression: FunctionResult = MethodName."),
                location,
                classDecl.Identifier.ValueText);

            diagnostics.Add(diagnostic);
        }

        // Validate: Can't have both literal AND expression forSystemPrompt
        if (!string.IsNullOrEmpty(sysPromptCtx) && !string.IsNullOrEmpty(sysPromptExpr))
        {
            var diagnostic = Diagnostic.Create(
                new DiagnosticDescriptor(
                    "HPDAG0102",
                    "ConflictingSystemPrompt configuration",
                    "ToolHarness '{0}' specifies bothSystemPrompt literal and expression. Use one or the other, not both.",
                    "HPDAgent.SourceGenerator",
                    DiagnosticSeverity.Error,
                    isEnabledByDefault: true,
                    description: "SystemPrompt can be either a literal string or an expression, but not both. " +
                                "Literal:SystemPrompt = \"text\". Expression:SystemPrompt = MethodName."),
                location,
                classDecl.Identifier.ValueText);

            diagnostics.Add(diagnostic);
        }

        // Validate: Expression syntax (basic check)
        if (!string.IsNullOrEmpty(funcResultExpr))
        {
            var exprDiagnostics = ValidateContextExpression(funcResultExpr, "FunctionResult", classDecl);
            diagnostics.AddRange(exprDiagnostics);
        }

        if (!string.IsNullOrEmpty(sysPromptExpr))
        {
            var exprDiagnostics = ValidateContextExpression(sysPromptExpr, "SystemPrompt", classDecl);
            diagnostics.AddRange(exprDiagnostics);
        }

        return diagnostics;
    }

    /// <summary>
    /// Validates that a context expression has valid syntax.
    /// </summary>
    private static List<Diagnostic> ValidateContextExpression(
        string expression,
        string propertyName,
        ClassDeclarationSyntax classDecl)
    {
        var diagnostics = new List<Diagnostic>();

        if (string.IsNullOrWhiteSpace(expression))
        {
            // Empty expression - will be caught by other validation
            return diagnostics;
        }

        // Basic validation: Check for common mistakes
        // Valid examples: "MyMethod", "instance.GetInstructions", "MyClass.StaticMethod"
        // Invalid examples: Literals ("\"text\""), operators ("1 + 2"), empty strings

        // Check for string literals (user passed a string when they should use the literal parameter)
        if (expression.StartsWith("\"") || expression.StartsWith("@\""))
        {
            var diagnostic = Diagnostic.Create(
                new DiagnosticDescriptor(
                    "HPDAG0103",
                    $"Invalid {propertyName} expression syntax",
                    "ToolHarness '{0}' uses a string literal for {1} expression. Use the literal parameter instead, or provide a method/property reference.",
                    "HPDAgent.SourceGenerator",
                    DiagnosticSeverity.Warning,
                    isEnabledByDefault: true,
                    description: $"Context expressions should reference methods or properties, not string literals. " +
                                $"For literal text, use the non-expression parameter."),
                classDecl.GetLocation(),
                classDecl.Identifier.ValueText,
                propertyName);

            diagnostics.Add(diagnostic);
        }

        return diagnostics;
    }

    /// <summary>
    /// Analyzes an expression to determine if it refers to a static member or requires instance access.
    /// Returns true if static, false if instance is required.
    /// </summary>
    private static bool IsExpressionStatic(ExpressionSyntax expression, SemanticModel semanticModel, ClassDeclarationSyntax classDecl)
    {
        // For member access expressions like ClassName.Method() or OtherClass.Property
        if (expression is MemberAccessExpressionSyntax memberAccess)
        {
            var leftSymbol = semanticModel.GetSymbolInfo(memberAccess.Expression).Symbol;

            // If left side is a type (not an instance), it's static access
            if (leftSymbol is INamedTypeSymbol)
            {
                return true; // External static class or static member access
            }

            // Otherwise it's instance member access
            return false;
        }

        // For invocation expressions like Method() or Property
        if (expression is InvocationExpressionSyntax invocation)
        {
            // Get the identifier from the invocation
            if (invocation.Expression is IdentifierNameSyntax identifier)
            {
                // Check if this is a member of the current class
                var members = classDecl.Members;
                foreach (var member in members)
                {
                    if (member is MethodDeclarationSyntax method && method.Identifier.ValueText == identifier.Identifier.ValueText)
                    {
                        // Found the method in the class - check if it's static
                        return method.Modifiers.Any(m => m.IsKind(SyntaxKind.StaticKeyword));
                    }
                    else if (member is PropertyDeclarationSyntax property && property.Identifier.ValueText == identifier.Identifier.ValueText)
                    {
                        // Found the property in the class - check if it's static
                        return property.Modifiers.Any(m => m.IsKind(SyntaxKind.StaticKeyword));
                    }
                }
            }
        }

        // For simple identifiers like Property or Method() without parentheses
        if (expression is IdentifierNameSyntax simpleIdentifier)
        {
            // Check if this is a member of the current class
            var members = classDecl.Members;
            foreach (var member in members)
            {
                if (member is MethodDeclarationSyntax method && method.Identifier.ValueText == simpleIdentifier.Identifier.ValueText)
                {
                    // Found the method in the class - check if it's static
                    return method.Modifiers.Any(m => m.IsKind(SyntaxKind.StaticKeyword));
                }
                else if (member is PropertyDeclarationSyntax property && property.Identifier.ValueText == simpleIdentifier.Identifier.ValueText)
                {
                    // Found the property in the class - check if it's static
                    return property.Modifiers.Any(m => m.IsKind(SyntaxKind.StaticKeyword));
                }
            }
        }

        // Try semantic model as fallback
        var symbolInfo = semanticModel.GetSymbolInfo(expression);
        var symbol = symbolInfo.Symbol;

        if (symbol != null)
        {
            // Check if it's a method or property
            if (symbol is IMethodSymbol methodSymbol)
            {
                return methodSymbol.IsStatic;
            }
            else if (symbol is IPropertySymbol propertySymbol)
            {
                return propertySymbol.IsStatic;
            }
        }

        // Default to static if we can't determine (safer - won't add instance prefix)
        return true;
    }

    /// <summary>
    /// Generates the GetToolMetadata() method for ToolHarness Collapsing support.
    /// </summary>
    private static string GenerateToolMetadataMethod(ToolHarnessInfo ToolHarness)
    {
        var sb = new StringBuilder();

        var functionNamesArray = string.Join(", ", ToolHarness.FunctionCapabilities.Select(f => $"\"{f.FunctionName}\""));
        var description = ToolHarness.IsCollapsed && !string.IsNullOrEmpty(ToolHarness.ContainerDescription)
            ? ToolHarness.ContainerDescription
            : ToolHarness.Description;

        sb.AppendLine("        private static ToolMetadata? _cachedMetadata;");
        sb.AppendLine();
        sb.AppendLine("        /// <summary>");
        sb.AppendLine($"        /// Gets metadata for the {ToolHarness.ClassName} ToolHarness (used for Collapsing).");
        sb.AppendLine("        /// </summary>");
        sb.AppendLine("        public static ToolMetadata GetToolMetadata()");
        sb.AppendLine("        {");
        sb.AppendLine("            return _cachedMetadata ??= new ToolMetadata");
        sb.AppendLine("            {");
        // Use EffectiveName for LLM-visible name (always ClassName now)
        sb.AppendLine($"                Name = \"{ToolHarness.EffectiveName}\",");
        sb.AppendLine($"                Description = \"{description}\",");
        sb.AppendLine($"                FunctionNames = new string[] {{ {functionNamesArray} }},");
        sb.AppendLine($"                FunctionCount = {ToolHarness.FunctionCapabilities.Count()},");
        sb.AppendLine($"                IsCollapsed = {ToolHarness.IsCollapsed.ToString().ToLower()}");
        sb.AppendLine("            };");
        sb.AppendLine("        }");

        return sb.ToString();
    }

    /// <summary>
    /// Generates the container function for a Collapsed ToolHarness.
    /// </summary>
    private static string GenerateContainerFunction(ToolHarnessInfo ToolHarness)
    {
        var sb = new StringBuilder();

        // Combine both AI functions and skills
        var allCapabilities = ToolHarness.FunctionCapabilities.Select(f => f.FunctionName)
            .Concat(ToolHarness.SkillCapabilities.Select(s => s.Name))
            .ToList();
        var capabilitiesList = string.Join(", ", allCapabilities);
        var totalCount = ToolHarness.FunctionCapabilities.Count() + ToolHarness.SkillCapabilities.Count();

        var description = !string.IsNullOrEmpty(ToolHarness.ContainerDescription)
            ? ToolHarness.ContainerDescription
            : ToolHarness.Description ?? string.Empty;

        // Use shared helper to generate description and return message
        // Use EffectiveName for LLM-visible container name
        var fullDescription = ToolHarnessContainerHelper.GenerateContainerDescription(description, ToolHarness.EffectiveName, allCapabilities);

        sb.AppendLine("        /// <summary>");
        sb.AppendLine($"        /// Container function for {ToolHarness.ClassName} ToolHarness.");
        sb.AppendLine("        /// </summary>");
        // Method signature uses ClassName for type reference
        sb.AppendLine($"        private static AIFunction Create{ToolHarness.ClassName}Container({ToolHarness.ClassName} instance, HPDToolSerializationOptions? serialization)");
        sb.AppendLine("        {");
        sb.AppendLine("            return HPDAIFunctionFactory.Create(");
        sb.AppendLine("                async (arguments, functionContext, cancellationToken) =>");
        sb.AppendLine("                {");

        // Use the ContainerDescription (or ToolHarness description as fallback) in the return message
        var returnMessage = ToolHarnessContainerHelper.GenerateReturnMessage(description, allCapabilities, ToolHarness.FunctionResult);

        if (!string.IsNullOrEmpty(ToolHarness.FunctionResultExpression))
        {
            // Using an interpolated string to combine the base message and the dynamic instructions
            var baseMessage = ToolHarnessContainerHelper.GenerateReturnMessage(description, allCapabilities, null);
            // Escape special characters for the interpolated string - we need to convert \n\n to \\n\\n in source code
            baseMessage = baseMessage.Replace("\\", "\\\\").Replace("\n", "\\n").Replace("\"", "\\\"");
            // Add separator between capabilities list and dynamic instructions
            var separator = "\\n\\n";  // This will be two backslash-n sequences in the source code

            // Use instance. prefix for instance methods, nothing for static
            var expressionCall = ToolHarness.FunctionResultIsStatic
                ? ToolHarness.FunctionResultExpression
                : $"instance.{ToolHarness.FunctionResultExpression}";

            sb.AppendLine($"                    var dynamicInstructions = {expressionCall};");
            sb.AppendLine($"                    return $\"{baseMessage}{separator}{{dynamicInstructions}}\";");
        }
        else
        {
            // Using a verbatim string literal for static content
            // In a verbatim string, actual newlines are allowed but we need to represent them as \n
            var escapedReturnMessage = returnMessage
                .Replace("\\", "\\\\")  // Escape backslashes first
                .Replace("\"", "\"\"")  // Escape quotes (double them in verbatim strings)
                .Replace("\n", "\\n"); // Convert actual newlines to backslash-n
            sb.AppendLine($"                    return @\"{escapedReturnMessage}\";");
        }

        sb.AppendLine("                },");
        sb.AppendLine("                new HPDAIFunctionFactoryOptions");
        sb.AppendLine("                {");
        // Use EffectiveName for LLM-visible container function name
        sb.AppendLine($"                    Name = \"{ToolHarness.EffectiveName}\",");
        sb.AppendLine($"                    Description = \"{fullDescription}\",");
        sb.AppendLine("                    SchemaProvider = () => CreateEmptyContainerSchema(),");
        sb.AppendLine("                    SerializerOptions = serialization?.SerializerOptions,");
        sb.AppendLine("                    ResultType = typeof(string),");
        sb.AppendLine("                    AdditionalProperties = new Dictionary<string, object>");
        sb.AppendLine("                    {");
        sb.AppendLine("                        [\"IsContainer\"] = true,");
        // Use EffectiveName for ToolHarnessName metadata (always ClassName now)
        sb.AppendLine($"                        [\"ToolHarnessName\"] = \"{ToolHarness.EffectiveName}\",");
        sb.AppendLine($"                        [\"ToolHarnessIdentity\"] = @\"{EscapeForVerbatim(ToolHarness.AssemblyName)}:{EscapeForVerbatim(ToolHarness.Namespace)}:{EscapeForVerbatim(ToolHarness.EffectiveName)}\",");
        sb.AppendLine($"                        [\"ReferencedFunctions\"] = new string[] {{ {string.Join(", ", allCapabilities.Select(c => $"\"{c}\""))} }},");
        sb.AppendLine($"                        [\"FunctionCount\"] = {totalCount},");

        // AddSystemPrompt to metadata (for middleware injection)
        if (!string.IsNullOrEmpty(ToolHarness.SystemPrompt))
        {
            // Use verbatim string literal - only escape quotes (double them), NOT newlines
            var escapedSysPrompt = ToolHarness.SystemPrompt.Replace("\"", "\"\"");
            sb.AppendLine($"                        [\"SystemPrompt\"] = @\"{escapedSysPrompt}\",");
        }
        else if (!string.IsNullOrEmpty(ToolHarness.SystemPromptExpression))
        {
            // Expression - evaluate at container creation time
            // Use instance. prefix for instance methods, nothing for static
            var expressionCall = ToolHarness.SystemPromptIsStatic
                ? ToolHarness.SystemPromptExpression
                : $"instance.{ToolHarness.SystemPromptExpression}";

            sb.AppendLine($"                        [\"SystemPrompt\"] = {expressionCall},");
        }

        // Optionally store FunctionResult for introspection
        if (!string.IsNullOrEmpty(ToolHarness.FunctionResult))
        {
            // Use verbatim string literal - only escape quotes (double them), NOT newlines
            var escapedFuncResult = ToolHarness.FunctionResult.Replace("\"", "\"\"");
            sb.AppendLine($"                        [\"FunctionResult\"] = @\"{escapedFuncResult}\"");
        }
        else if (!string.IsNullOrEmpty(ToolHarness.FunctionResultExpression))
        {
            // Don't store expression in metadata (it's already executed in return statement)
            sb.AppendLine($"                        // FunctionResult is dynamic: {ToolHarness.FunctionResultExpression}");
        }
        else
        {
            // Remove trailing comma from FunctionCount if no context properties
            // This is handled by checking if we added anything after FunctionCount
        }

        sb.AppendLine("                    }");
        sb.AppendLine("                });");
        sb.AppendLine("        }");

        return sb.ToString();
    }

    /// <summary>
    /// Generates the CreateEmptyContainerSchema() helper method.
    /// </summary>
    private static string GenerateEmptySchemaMethod()
    {
        var sb = new StringBuilder();

        sb.AppendLine("        /// <summary>");
        sb.AppendLine("        /// Creates an empty JSON schema for container functions (no parameters).");
        sb.AppendLine("        /// </summary>");
        sb.AppendLine("        private static JsonElement CreateEmptyContainerSchema()");
        sb.AppendLine("        {");
        sb.AppendLine("            var options = new global::Microsoft.Extensions.AI.AIJsonSchemaCreateOptions { IncludeSchemaKeyword = false };");
        sb.AppendLine("            return global::Microsoft.Extensions.AI.AIJsonUtilities.CreateJsonSchema(");
        sb.AppendLine("                null,");
        sb.AppendLine("                serializerOptions: HPDJsonContext.Default.Options,");
        sb.AppendLine("                inferenceOptions: options");
        sb.AppendLine("            );");
        sb.AppendLine("        }");

        return sb.ToString();
    }

    /// <summary>
    /// Resolves SkillCapability references recursively (Phase 5 migration).
    /// Populates ResolvedFunctionReferences and ResolvedToolHarnessTypes from UnresolvedReferences.
    /// </summary>
    private static void ResolveSkillCapabilities(List<HPD.Agent.SourceGenerator.Capabilities.SkillCapability> skills)
    {
        // Build lookup dictionary: FullName -> SkillCapability
        var skillLookup = skills.ToDictionary(s => s.FullQualifiedName);

        // Resolve each skill
        var visited = new HashSet<string>();
        var stack = new Stack<string>();

        foreach (var skill in skills)
        {
            ResolveSkillCapability(skill, skillLookup, visited, stack);
        }
    }

    /// <summary>
    /// Recursively resolves a single SkillCapability, handling nested skills and circular dependencies.
    /// </summary>
    private static void ResolveSkillCapability(
        HPD.Agent.SourceGenerator.Capabilities.SkillCapability skill,
        Dictionary<string, HPD.Agent.SourceGenerator.Capabilities.SkillCapability> skillLookup,
        HashSet<string> visited,
        Stack<string> stack,
        int maxDepth = 50)
    {
        // Already resolved
        if (visited.Contains(skill.FullQualifiedName))
            return;

        // Circular reference detected
        if (stack.Contains(skill.FullQualifiedName))
            return;

        // Depth limit exceeded
        if (stack.Count >= maxDepth)
            return;

        stack.Push(skill.FullQualifiedName);
        visited.Add(skill.FullQualifiedName);

        var functionRefs = new List<string>();
        var toolTypes = new HashSet<string>();

        foreach (var reference in skill.UnresolvedReferences)
        {
            if (reference.ReferenceType == HPD.Agent.SourceGenerator.Capabilities.ReferenceType.Skill)
            {
                // It's a skill reference - resolve it recursively
                var referencedSkillName = $"{reference.ToolHarnessType}.{reference.MethodName}";
                if (skillLookup.TryGetValue(referencedSkillName, out var referencedSkill))
                {
                    // Recursively resolve the referenced skill first
                    ResolveSkillCapability(referencedSkill, skillLookup, visited, stack, maxDepth);

                    // Add all its function references to our list
                    functionRefs.AddRange(referencedSkill.ResolvedFunctionReferences);
                    foreach (var pt in referencedSkill.ResolvedToolHarnessTypes)
                    {
                        toolTypes.Add(pt);
                    }
                }
            }
            else
            {
                // It's a function reference - add directly
                functionRefs.Add(reference.FullName);
                toolTypes.Add(reference.ToolHarnessType);
            }
        }

        // Update the skill with resolved references
        skill.ResolvedFunctionReferences = functionRefs.Distinct().OrderBy(f => f).ToList();
        skill.ResolvedToolHarnessTypes = toolTypes.OrderBy(p => p).ToList();

        stack.Pop();
    }

}

/// <summary>
/// Token types for expression parsing.
/// </summary>
internal enum TokenType
{
    Property,
    Comparison,
    Logical,
    Value
}

/// <summary>
/// Represents a token in a conditional expression.
/// </summary>
internal class ExpressionToken
{
    public TokenType Type { get; set; }
    public string PropertyName { get; set; } = string.Empty;
    public string Operator { get; set; } = string.Empty;
    public string Value { get; set; } = string.Empty;
}
