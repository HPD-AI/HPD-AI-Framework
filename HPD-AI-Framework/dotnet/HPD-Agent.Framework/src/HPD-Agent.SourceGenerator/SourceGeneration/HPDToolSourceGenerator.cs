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

/// <summary>
/// Source generator for HPD-Agent AI Harneses. Generates AOT-compatible Harness registration code.
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
        // Harness detection (classes with [AIFunction], [Skill], or [SubAgent] methods)
        var toolClasses = context.SyntaxProvider
            .CreateSyntaxProvider(
                predicate: static (node, ct) => IsToolClass(node, ct),
                transform: static (ctx, ct) => GetToolDeclaration(ctx, ct))
            .Where(static Harness => Harness is not null)
            .Collect();

        context.RegisterSourceOutput(toolClasses, GenerateToolRegistrations);

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
        // This replaces the 3 separate detection branches (AIFunction, Skill, SubAgent)
        var hasCapabilityMethods = methods.Any(method =>
        {
            var attrs = method.AttributeLists
                .SelectMany(attrList => attrList.Attributes)
                .Select(attr => attr.Name.ToString());

            // A Harness class has methods with any of these attributes
            return attrs.Any(name =>
                name.Contains("AIFunction") ||
                name.Contains("Skill") ||
                name.Contains("SubAgent") ||
                name.Contains("MCPServer") ||
                name.Contains("OpenApi"));
        });

        if (hasCapabilityMethods)
        {
            System.Diagnostics.Debug.WriteLine($"[HPDToolSourceGenerator]   Class {className} has capability methods - SELECTED");
        }

        return hasCapabilityMethods;
    }
    
    private static HarnessInfo? GetToolDeclaration(GeneratorSyntaxContext context, CancellationToken cancellationToken)
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
        // Partial harnesses commonly put [Collapse] on one partial declaration and
        // [AIFunction] methods on other partial declarations. Keep the metadata-only
        // part so the partial merge can generate the container.
        if (!capabilities.Any() && !isCollapsed)
            return null;

        // Merge capability diagnostics with harness diagnostics
        diagnostics.AddRange(capabilityDiagnostics);

        // Diagnostics will be stored in HarnessInfo and reported in GenerateToolRegistrations

        // Check if the class has a parameterless constructor (either explicit or implicit)
        var hasParameterlessConstructor = HasParameterlessConstructor(classDecl);

        // Check if the class is publicly accessible (for HarnessRegistry.All inclusion)
        // A class is publicly accessible if it's public and not nested inside a non-public class
        var isPubliclyAccessible = IsClassPubliclyAccessible(classDecl);

        // Build description from capabilities
        var functionCount = capabilities.OfType<HPD.Agent.SourceGenerator.Capabilities.FunctionCapability>().Count();
        var skillCount = capabilities.OfType<HPD.Agent.SourceGenerator.Capabilities.SkillCapability>().Count();
        var subAgentCount = capabilities.OfType<HPD.Agent.SourceGenerator.Capabilities.SubAgentCapability>().Count();
        var mcpServerCount = capabilities.OfType<HPD.Agent.SourceGenerator.Capabilities.MCPServerCapability>().Count();
        var openApiCount = capabilities.OfType<HPD.Agent.SourceGenerator.Capabilities.OpenApiCapability>().Count();
        var description = BuildHarnessDescription(functionCount, skillCount, subAgentCount, mcpServerCount, openApiCount);

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

        // Harness-scoped middleware (015): extract [Collapse(Middlewares = [...])] type names
        List<string>? collapseMiddlewareTypeNames = null;
        List<CollapseMiddlewareConfigEntry>? collapseMiddlewareConfigTypeNames = null;
        if (isCollapsed)
        {
            var middlewareResult = GetCollapseMiddlewareTypeNames(classDecl, semanticModel, diagnostics);
            collapseMiddlewareTypeNames = middlewareResult.Parameterless;
            collapseMiddlewareConfigTypeNames = middlewareResult.ConfigConstructor;
        }

        return new HarnessInfo
        {
            // ClassName is always the class identifier
            ClassName = classDecl.Identifier.ValueText,
            Description = description,
            Namespace = namespaceName,

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

            // Harness-scoped middleware (015)
            CollapseMiddlewareTypeNames = collapseMiddlewareTypeNames,
            CollapseMiddlewareConfigTypeNames = collapseMiddlewareConfigTypeNames
        };
    }

    /// <summary>
    /// Detects if the harness class has a constructor that accepts a single *Config parameter.
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
    /// Detects if the harness has a constructor whose sole parameter is ISecretResolver.
    /// Also handles primary constructors (parameter lists on the class declaration itself).
    /// Example: public class StripeHarness(ISecretResolver secrets) { ... }
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

    private static string BuildHarnessDescription(int functionCount, int skillCount, int subAgentCount, int mcpServerCount = 0, int openApiCount = 0)
    {
        var parts = new List<string>();
        if (functionCount > 0) parts.Add($"{functionCount} AI functions");
        if (skillCount > 0) parts.Add($"{skillCount} skills");
        if (subAgentCount > 0) parts.Add($"{subAgentCount} sub-agents");
        if (mcpServerCount > 0) parts.Add($"{mcpServerCount} MCP servers");
        if (openApiCount > 0) parts.Add($"{openApiCount} OpenAPI source(s)");

        if (parts.Count == 0)
            return "Empty Harness container.";
        else if (parts.Count == 1)
            return $"Harness containing {parts[0]}.";
        else
        {
            var last = parts[parts.Count - 1];
            var rest = string.Join(", ", parts.Take(parts.Count - 1));
            return $"Harness containing {rest}, and {last}.";
        }
    }
    
    private static void GenerateToolRegistrations(SourceProductionContext context, ImmutableArray<HarnessInfo?> Harneses)
    {
        // Group Harneses by name+namespace to handle partial classes FIRST
        // This prevents duplicate generation by merging partial classes before validation
        var HarnessGroups = Harneses
            .Where(p => p != null)
            .GroupBy(p => $"{p!.Namespace}.{p.Name}")
            .Select(group =>
            {
                // Merge all partial class parts into one Harness
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

                // All partial class parts must have parameterless constructor for the Harness to be AOT-instantiable
                // (If any part declares a constructor with parameters, no implicit parameterless constructor is generated)
                var hasParameterlessConstructor = group.All(p => p!.HasParameterlessConstructor);

                // Detect ISecretResolver-only constructor (from any partial part)
                var hasSecretsConstructor = group.Any(p => p!.HasSecretsConstructor);

                // All partial class parts must be publicly accessible for the Harness to be in the registry
                var isPubliclyAccessible = group.All(p => p!.IsPubliclyAccessible);

                // Merge diagnostics from all partial class parts
                var allDiagnostics = group.SelectMany(p => p!.Diagnostics).ToList();

                // Merge function names from all partial classes
                var allFunctionNames = group.SelectMany(p => p!.FunctionNames).Distinct().ToList();

                // Use first config constructor type found (should only be defined in one partial)
                var configConstructorTypeName = group.FirstOrDefault(p => !string.IsNullOrEmpty(p!.ConfigConstructorTypeName))?.ConfigConstructorTypeName;

                    // Use first metadata type found
                var metadataTypeName = group.FirstOrDefault(p => !string.IsNullOrEmpty(p!.MetadataTypeName))?.MetadataTypeName;

                return new HarnessInfo
                {
                    Name = first.Name,
                    Description = BuildHarnessDescription(functionCount, skillCount, subAgentCount, mcpServerCount, openApiCount),
                    Namespace = first.Namespace,

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
                    // Harness-scoped middleware (015): merge from any partial part that has them
                    CollapseMiddlewareTypeNames = group.FirstOrDefault(p => p?.CollapseMiddlewareTypeNames != null)?.CollapseMiddlewareTypeNames,
                    CollapseMiddlewareConfigTypeNames = group.FirstOrDefault(p => p?.CollapseMiddlewareConfigTypeNames != null)?.CollapseMiddlewareConfigTypeNames
                };
            })
            .ToList();

        // Report diagnostics for all Harneses
        foreach (var Harness in HarnessGroups)
        {
            foreach (var diagnostic in Harness.Diagnostics)
            {
                context.ReportDiagnostic(diagnostic);
            }
        }

        // DIAGNOSTIC: Generate detailed diagnostic report AFTER grouping
        var reportLines = string.Join("\\n", _diagnosticMessages.Select(m => m.Replace("\"", "\\\"").Replace("\n", "\\n").Replace("\r", "")));
        var diagnosticCode = $@"
// HPD Source Generator Diagnostic Report
// Generated at: {DateTime.Now}
// Harneses found: {Harneses.Length} raw, {HarnessGroups.Count} after merging

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
        /// Gets the number of harnesses found during source generation.
        /// </summary>
        public const int HarnesesFound = {HarnessGroups.Count};

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
        debugInfo.AppendLine($"// Found {Harneses.Length} Harness parts total");
        debugInfo.AppendLine($"// Merged into {HarnessGroups.Count} unique Harneses");
        foreach (var Harness in HarnessGroups)
        {
            debugInfo.AppendLine($"// Harness: {Harness.Namespace}.{Harness.Name} with {Harness.FunctionCapabilities.Count()} functions, {Harness.SkillCapabilities.Count()} skills, and {Harness.SubAgentCapabilities.Count()} sub-agents");
        }
        context.AddSource("HPD.Agent.Generated.SourceGeneratorDebug.g.cs", debugInfo.ToString());

        // Resolve skill references before validation and code generation
        // PHASE 5: Use unified SkillCapabilities from Capabilities list
        var allSkillCapabilities = HarnessGroups
            .SelectMany(p => p.SkillCapabilities)
            .ToList();
        if (allSkillCapabilities.Any())
        {
            ResolveSkillCapabilities(allSkillCapabilities);
        }

        foreach (var Harness in HarnessGroups)
        {
            if (Harness == null) continue;

            foreach (var function in Harness.FunctionCapabilities)
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

            var source = GenerateHarnessRegistration(Harness);
            // Use fully qualified name as hint to prevent duplicates
            var hintName = string.IsNullOrEmpty(Harness.Namespace)
                ? $"{Harness.Name}Registration.g.cs"
                : $"{Harness.Namespace}.{Harness.Name}Registration.g.cs";
            context.AddSource(hintName, source);
        }

        // NEW: Generate Harness registry catalog for AOT-compatible Harness discovery
        if (HarnessGroups.Any())
        {
            var registrySource = GenerateHarnessRegistry(HarnessGroups);
            context.AddSource("HPD.Agent.Generated.HarnessRegistry.g.cs", registrySource);
        }
    }

    /// <summary>
    /// Generates the HarnessRegistry.All array that serves as a catalog of all Harneses in the assembly.
    /// This eliminates reflection in hot paths by providing direct delegate references.
    /// Only Harneses with parameterless constructors and public accessibility are included.
    /// </summary>
    private static string GenerateHarnessRegistry(List<HarnessInfo> Harneses)
    {
        // Filter to only include Harneses that can be instantiated via the registry:
        // 1. Must have parameterless constructor OR ISecretResolver-only constructor
        // 2. Must be publicly accessible (private/internal test classes are excluded)
        var instantiableHarneses = Harneses
            .Where(p => (p.HasParameterlessConstructor || p.HasSecretsConstructor) && p.IsPubliclyAccessible)
            .OrderBy(p => p.Name)
            .ToList();
        var sb = new StringBuilder();

        sb.AppendLine("// <auto-generated/>");
        sb.AppendLine("#pragma warning disable");
        sb.AppendLine();
        sb.AppendLine("using System;");
        sb.AppendLine("using System.Collections.Generic;");
        sb.AppendLine("using System.Runtime.CompilerServices;");
        sb.AppendLine("using System.Text.Json;");
        sb.AppendLine("using Microsoft.Extensions.AI;");
        sb.AppendLine("using HPD.Agent;  // For HarnessFactory and IToolMetadata types");
        sb.AppendLine();
        sb.AppendLine("namespace HPD.Agent.Generated");
        sb.AppendLine("{");
        sb.AppendLine("    /// <summary>");
        sb.AppendLine("    /// AOT-compatible catalog of all Harneses in this assembly.");
        sb.AppendLine("    /// Generated by HPDToolSourceGenerator.");
        sb.AppendLine("    /// Provides direct delegate references eliminating reflection in hot paths.");
        sb.AppendLine($"    /// Contains {instantiableHarneses.Count} Harneses (pure DI-only harnesses excluded).");
        sb.AppendLine("    /// </summary>");
        sb.AppendLine("    [System.CodeDom.Compiler.GeneratedCodeAttribute(\"HPDToolSourceGenerator\", \"1.0.0.0\")]");
        sb.AppendLine("    public static class HarnessRegistry");
        sb.AppendLine("    {");
        sb.AppendLine("        /// <summary>");
        sb.AppendLine("        /// Catalog of all Harneses in this assembly with parameterless or ISecretResolver-only constructors.");
        sb.AppendLine("        /// AgentBuilder automatically discovers and uses this at construction time.");
        sb.AppendLine("        /// </summary>");
        sb.AppendLine("        public static readonly HarnessFactory[] All = new HarnessFactory[]");
        sb.AppendLine("        {");

        foreach (var Harness in instantiableHarneses)
        {
            var ns = string.IsNullOrEmpty(Harness.Namespace) ? "" : $"{Harness.Namespace}.";
            var fullTypeName = $"{ns}{Harness.ClassName}";

            sb.AppendLine($"            new HarnessFactory(");
            sb.AppendLine($"                // ========== EXISTING FIELDS ==========");
            // Use EffectiveName for registry lookup (always ClassName now)
            sb.AppendLine($"                Name: \"{Harness.EffectiveName}\",");
            sb.AppendLine($"                HarnessType: typeof({fullTypeName}),");
            if (Harness.HasParameterlessConstructor)
                sb.AppendLine($"                CreateInstance: () => new {fullTypeName}(),  // Direct instantiation (AOT-safe)");
            else
                sb.AppendLine($"                CreateInstance: () => throw new InvalidOperationException(\"{fullTypeName} requires ISecretResolver — use CreateWithSecrets\"),");

            // ========== SECRETS-BASED INSTANTIATION ==========
            sb.AppendLine($"                // ========== SECRETS-BASED INSTANTIATION ==========");
            if (Harness.HasSecretsConstructor)
                sb.AppendLine($"                CreateWithSecrets: secrets => new {fullTypeName}(secrets),");
            else
                sb.AppendLine($"                CreateWithSecrets: null,");

            // Handle skill-only containers (no instance parameter)
            if (!Harness.RequiresInstance)
            {
                sb.AppendLine($"                CreateFunctions: (_, ctx, serialization) => {Harness.Name}Registration.CreateHarness(ctx, serialization),");
            }
            else
            {
                sb.AppendLine($"                CreateFunctions: (instance, ctx, serialization) => {Harness.Name}Registration.CreateHarness(({fullTypeName})instance, ctx, serialization),");
            }

            // Add GetReferencedHarneses if Harness has skills
            if (Harness.SkillCapabilities.Any())
            {
                sb.AppendLine($"                GetReferencedHarneses: {Harness.Name}Registration.GetReferencedHarneses,");
                sb.AppendLine($"                GetReferencedFunctions: {Harness.Name}Registration.GetReferencedFunctions,");
            }
            else
            {
                sb.AppendLine($"                GetReferencedHarneses: () => Array.Empty<string>(),");
                sb.AppendLine($"                GetReferencedFunctions: () => new Dictionary<string, string[]>(),");
            }

            // NEW: Collapsing metadata (from [Collapse] attribute)
            sb.AppendLine($"                // ========== COLLAPSING METADATA ==========");
            sb.AppendLine($"                HasDescription: {Harness.IsCollapsed.ToString().ToLower()},");
            sb.AppendLine($"                Description: {(string.IsNullOrEmpty(Harness.ContainerDescription) ? "null" : $"@\"{EscapeForVerbatim(Harness.ContainerDescription)}\"")},");
            sb.AppendLine($"                FunctionResult: {(string.IsNullOrEmpty(Harness.FunctionResult) ? "null" : $"@\"{EscapeForVerbatim(Harness.FunctionResult)}\"")},");
            sb.AppendLine($"                SystemPrompt: {(string.IsNullOrEmpty(Harness.SystemPrompt) ? "null" : $"@\"{EscapeForVerbatim(Harness.SystemPrompt)}\"")},");

            // NEW: Config-based instantiation
            sb.AppendLine($"                // ========== CONFIG INSTANTIATION ==========");
            if (!string.IsNullOrEmpty(Harness.ConfigConstructorTypeName))
            {
                sb.AppendLine($"                ConfigType: typeof({Harness.ConfigConstructorTypeName}),");
                sb.AppendLine($"                CreateFromConfig: json => new {fullTypeName}(System.Text.Json.JsonSerializer.Deserialize<{Harness.ConfigConstructorTypeName}>(json.GetRawText())!),");
            }
            else
            {
                sb.AppendLine($"                ConfigType: null,");
                sb.AppendLine($"                CreateFromConfig: null,");
            }

            // NEW: Metadata type
            sb.AppendLine($"                // ========== METADATA ==========");
            if (!string.IsNullOrEmpty(Harness.MetadataTypeName))
            {
                sb.AppendLine($"                MetadataType: typeof({Harness.MetadataTypeName}),");
                sb.AppendLine($"                DeserializeMetadata: json => System.Text.Json.JsonSerializer.Deserialize<{Harness.MetadataTypeName}>(json.GetRawText()),");
            }
            else
            {
                sb.AppendLine($"                MetadataType: null,");
                sb.AppendLine($"                DeserializeMetadata: null,");
            }

            // NEW: Function names for selective registration
            var functionNamesArray = Harness.FunctionNames.Any()
                ? $"new string[] {{ {string.Join(", ", Harness.FunctionNames.Select(n => $"\"{n}\""))} }}"
                : "Array.Empty<string>()";
            sb.AppendLine($"                FunctionNames: {functionNamesArray},");

            // NEW: MCP Server support
            sb.AppendLine($"                // ========== MCP SERVERS ==========");
            sb.AppendLine($"                HasMCPServers: {Harness.MCPServerCapabilities.Any().ToString().ToLower()},");
            if (Harness.MCPServerCapabilities.Any())
            {
                sb.AppendLine($"                CollectMcpServers: {Harness.Name}Registration.CollectMcpServers,");
            }
            else
            {
                sb.AppendLine($"                CollectMcpServers: null,");
            }

            // NEW: OpenAPI support
            sb.AppendLine($"                // ========== OPENAPI SOURCES ==========");
            if (Harness.OpenApiCapabilities.Any())
            {
                sb.AppendLine($"                CollectOpenApiSources: {Harness.Name}Registration.CollectOpenApiSources,");
            }
            else
            {
                sb.AppendLine($"                CollectOpenApiSources: null,");
            }

            //  Content store document initialization
            sb.AppendLine($"                // ========== V3 CONTENT STORE DOCUMENTS ==========");
            var hasSkillDocs = Harness.SkillCapabilities.Any(s =>
                s.Options.DocumentUploads.Any() || s.Options.DocumentReferences.Any());
            if (hasSkillDocs)
            {
                sb.AppendLine($"                InitializeDocumentsAsync: {Harness.Name}Registration.InitializeDocumentsAsync,");
            }
            else
            {
                sb.AppendLine($"                InitializeDocumentsAsync: null,");
            }

            // Harness-scoped middleware (015): emit CollapseMiddlewareFactories (parameterless ctors)
            sb.AppendLine($"                // ========== HARNESS-SCOPED MIDDLEWARE (015) ==========");
            if (Harness.CollapseMiddlewareTypeNames != null && Harness.CollapseMiddlewareTypeNames.Count > 0)
            {
                sb.AppendLine($"                CollapseMiddlewareFactories: new global::System.Func<global::HPD.Agent.Middleware.IAgentMiddleware>[]");
                sb.AppendLine($"                {{");
                foreach (var typeName in Harness.CollapseMiddlewareTypeNames)
                {
                    sb.AppendLine($"                    static () => new {typeName}(),");
                }
                sb.AppendLine($"                }},");
            }
            else
            {
                sb.AppendLine($"                CollapseMiddlewareFactories: null,");
            }

            // Harness-scoped middleware (015 §5A): emit CollapseMiddlewareConfigFactories (config-ctor middlewares)
            if (Harness.CollapseMiddlewareConfigTypeNames != null && Harness.CollapseMiddlewareConfigTypeNames.Count > 0)
            {
                sb.AppendLine($"                CollapseMiddlewareConfigFactories: new global::HPD.Agent.CollapseMiddlewareConfigFactory[]");
                sb.AppendLine($"                {{");
                foreach (var entry in Harness.CollapseMiddlewareConfigTypeNames)
                {
                    sb.AppendLine($"                    new global::HPD.Agent.CollapseMiddlewareConfigFactory(");
                    sb.AppendLine($"                        MiddlewareTypeName: \"{entry.SimpleName}\",");
                    sb.AppendLine($"                        Factory: static json => new {entry.FullyQualifiedTypeName}(");
                    sb.AppendLine($"                            global::System.Text.Json.JsonSerializer.Deserialize<{entry.ConfigTypeFqn}>(json.GetRawText())!)),");
                }
                sb.AppendLine($"                }}");
            }
            else
            {
                sb.AppendLine($"                CollapseMiddlewareConfigFactories: null");
            }

            sb.AppendLine($"            ),");
        }

        sb.AppendLine("        };");
        sb.AppendLine();
        sb.AppendLine("#pragma warning disable CA2255");
        sb.AppendLine("        [ModuleInitializer]");
        sb.AppendLine("        internal static void RegisterGeneratedCatalog()");
        sb.AppendLine("#pragma warning restore CA2255");
        sb.AppendLine("        {");
        sb.AppendLine("            AgentGeneratedRegistry.Register(harneses: All);");
        sb.AppendLine("        }");
        sb.AppendLine("    }");
        sb.AppendLine("}");

        return sb.ToString();
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
                sb.AppendLine($"                CreateFromConfig: json => new {fullTypeName}(System.Text.Json.JsonSerializer.Deserialize<{m.ConfigConstructorTypeName}>(json.GetRawText())!),");
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
    /// Generates the CreateHarness method using unified polymorphic ICapability iteration.
    /// Phase 4: Now the single unified generation path (old path removed).
    /// </summary>
    private static string GenerateCreateHarnessMethod(HarnessInfo Harness)
    {
        var sb = new StringBuilder();
        sb.AppendLine("    /// <summary>");
        sb.AppendLine($"    /// Creates an AIFunction list for the {Harness.Name} Harness.");
        sb.AppendLine("    /// </summary>");

        // Only include instance parameter if Harness has capabilities that need it
        if (!Harness.RequiresInstance)
        {
            sb.AppendLine($"    /// <param name=\"context\">The execution context (optional)</param>");
            sb.AppendLine($"    public static List<AIFunction> CreateHarness(IToolMetadata? context = null, HPDToolSerializationOptions? serialization = null)");
        }
        else
        {
            sb.AppendLine($"    /// <param name=\"instance\">The Harness instance</param>");
            sb.AppendLine($"    /// <param name=\"context\">The execution context (optional)</param>");
            sb.AppendLine($"    public static List<AIFunction> CreateHarness({Harness.Name} instance, IToolMetadata? context = null, HPDToolSerializationOptions? serialization = null)");
        }

        sb.AppendLine("    {");
        sb.AppendLine("        var functions = new List<AIFunction>();");
        sb.AppendLine();

        // Add collapse container registration if needed (BEFORE individual capabilities)
        var skillRegistrations = SkillCodeGenerator.GenerateSkillRegistrations(Harness);
        if (!string.IsNullOrEmpty(skillRegistrations))
        {
            sb.Append(skillRegistrations);
        }

        // PHASE 2A: POLYMORPHIC DISPATCH
        // Each capability declares via EmitsIntoCreateTools whether it belongs in the functions list.
        // Capabilities with their own registration paths (Skills, MCPServers, etc.) return false.
        var createToolsCapabilities = Harness.Capabilities.Where(c => c.EmitsIntoCreateTools);

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
                    sb.AppendLine($"            functions.Add({capability.GenerateRegistrationCode(Harness)});");
                    sb.AppendLine("        }");
                }
                else
                {
                    sb.AppendLine($"        functions.Add({capability.GenerateRegistrationCode(Harness)});");
                }
                sb.AppendLine();
            }
        }

        sb.AppendLine("        return functions;");
        sb.AppendLine("    }");
        return sb.ToString();
    }


    private static string GenerateHarnessRegistration(HarnessInfo Harness)
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

        // Add using directive for the Harness's namespace if it's not empty
        if (!string.IsNullOrEmpty(Harness.Namespace))
        {
            sb.AppendLine($"using {Harness.Namespace};");
        }

        sb.AppendLine();

        sb.AppendLine(GenerateArgumentsDtoAndContext(Harness));

        sb.AppendLine("/// <summary>");
        sb.AppendLine($"/// Generated registration code for {Harness.Name} Harness.");
        sb.AppendLine("/// </summary>");
        sb.AppendLine($"[System.CodeDom.Compiler.GeneratedCodeAttribute(\"HPDToolSourceGenerator\", \"1.0.0.0\")]");
        sb.AppendLine($"public static partial class {Harness.Name}Registration");
        sb.AppendLine("    {");

        // Generate GetReferencedHarneses() and GetReferencedFunctions() if there are skills
        // PHASE 5: Use SkillCapabilities (fully populated with resolved references)
        if (Harness.SkillCapabilities.Any())
        {
            sb.AppendLine(SkillCodeGenerator.GenerateGetReferencedHarnesesMethod(Harness));
            sb.AppendLine();
            sb.AppendLine(SkillCodeGenerator.GenerateGetReferencedFunctionsMethod(Harness));
            sb.AppendLine();
        }

        // Generate Harness metadata accessor (always generated for consistency)
        // PHASE 5: Use SkillCapabilities instead of Skills
        if (Harness.SkillCapabilities.Any())
        {
            sb.AppendLine(SkillCodeGenerator.UpdateToolMetadataWithSkills(Harness, ""));
        }
        else
        {
            sb.AppendLine(GenerateToolMetadataMethod(Harness));
        }
        sb.AppendLine();

        // Generate empty schema helper if Harness is collapsed OR has skills
        // Note: Container function is generated in SkillCodeGenerator.GenerateAllSkillCode
        if (Harness.IsCollapsed || Harness.SkillCapabilities.Any())
        {
            sb.AppendLine(GenerateEmptySchemaMethod());
            sb.AppendLine();
        }

        sb.AppendLine(GenerateCreateHarnessMethod(Harness));

        foreach (var function in Harness.FunctionCapabilities)
        {
            sb.AppendLine();
            sb.AppendLine(GenerateSchemaValidator(function, Harness));
            
            // Generate manual JSON parser for AOT compatibility
            var relevantParams = function.Parameters.Where(p => p.IsModelFacing).ToList();
            if (relevantParams.Any())
            {
                sb.AppendLine();
                sb.AppendLine(GenerateJsonParser(function, Harness));
            }
        }

        // PHASE 2B: Generate context resolvers for ALL capabilities (Functions, Skills, SubAgents)
        // This enables Skills and SubAgents to use dynamic descriptions and conditionals (feature parity!)
        // Replaces the old DSL-based GenerateContextResolutionMethods() which only worked for Functions
        foreach (var capability in Harness.Capabilities)
        {
            var resolvers = capability.GenerateContextResolvers();
            if (!string.IsNullOrEmpty(resolvers))
            {
                sb.AppendLine();
                sb.AppendLine(resolvers);
            }
        }

        // Generate skill code AND harness container (if Harness is collapsed)
        // NOTE: Container can exist even if there are no skills (e.g., collapsed Harness with only functions)
        if (Harness.SkillCapabilities.Any() || Harness.IsCollapsed)
        {
            sb.AppendLine(SkillCodeGenerator.GenerateAllSkillCode(Harness));
        }

        // Generate MCP Server registrations
        if (Harness.MCPServerCapabilities.Any())
        {
            sb.AppendLine();
            sb.AppendLine("        // MCP Server configurations");
            sb.AppendLine("        public static void CollectMcpServers(object __instance, System.Action<HPD.Agent.McpServerSource> __mcpCollector)");
            sb.AppendLine("        {");

            foreach (var mcp in Harness.MCPServerCapabilities)
            {
                sb.AppendLine($"            {mcp.GenerateSourceCode(Harness)}");
            }

            sb.AppendLine("        }");
        }

        // Generate CollectOpenApiSources method
        if (Harness.OpenApiCapabilities.Any())
        {
            sb.AppendLine();
            sb.AppendLine("        // OpenAPI source collection");
            sb.AppendLine("        /// <summary>");
            sb.AppendLine("        /// Collects OpenAPI source registrations from [OpenApi] methods.");
            sb.AppendLine("        /// Called by AgentBuilder.CreateFunctionsFromCatalog() via HarnessFactory.CollectOpenApiSources.");
            sb.AppendLine("        /// Config is passed as object so HarnessFactory has no compile-time dep on HPD-Agent.OpenApi.");
            sb.AppendLine("        /// Cast to OpenApiConfig happens inside OpenApiLoader.LoadAllAsync.");
            sb.AppendLine("        /// </summary>");
            sb.AppendLine($"        public static void CollectOpenApiSources(object __instance, System.Action<string, object, string> __openApiCollector)");
            sb.AppendLine("        {");

            foreach (var openApi in Harness.OpenApiCapabilities)
            {
                sb.AppendLine($"            {openApi.GenerateRegistrationCode(Harness)}");
            }

            sb.AppendLine("        }");
        }

        sb.AppendLine("    }");

        return sb.ToString();
    }

    private static string GenerateArgumentsDtoAndContext(HarnessInfo Harness)
    {
        var sb = new StringBuilder();
        var contextSerializableTypes = new List<string>();

        // Generate SubAgentQueryArgs if there are sub-agents (Collapsed per Harness to avoid conflicts)
        if (Harness.SubAgentCapabilities.Any())
        {
            sb.AppendLine(
$@"    /// <summary>
    /// Represents the arguments for sub-agent invocations, generated at compile-time.
    /// </summary>
    [System.CodeDom.Compiler.GeneratedCodeAttribute(""HPDToolSourceGenerator"", ""1.0.0.0"")]
    public class {Harness.Name}SubAgentQueryArgs
    {{
        [System.Text.Json.Serialization.JsonPropertyName(""query"")]
        [System.ComponentModel.Description(""Query for the sub-agent"")]
        public string Query {{ get; set; }} = string.Empty;
    }}
");
        }

        // Generate MultiAgentInputArgs if there are multi-agents (Collapsed per Harness to avoid conflicts)
        if (Harness.MultiAgentCapabilities.Any())
        {
            sb.AppendLine(
$@"    /// <summary>
    /// Represents the arguments for multi-agent workflow invocations, generated at compile-time.
    /// </summary>
    [System.CodeDom.Compiler.GeneratedCodeAttribute(""HPDToolSourceGenerator"", ""1.0.0.0"")]
    public class {Harness.Name}MultiAgentInputArgs
    {{
        [System.Text.Json.Serialization.JsonPropertyName(""input"")]
        [System.ComponentModel.Description(""The user's question or task to process through the multi-agent workflow. Pass the full user message here."")]
        public string Input {{ get; set; }} = string.Empty;
    }}
");
        }

        foreach (var function in Harness.FunctionCapabilities)
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
                sb.AppendLine($"        public {param.Type} {param.Name} {{ get; set; }}{ParameterAnalyzer.GetDefaultInitializer(param)}");
            }

            sb.AppendLine("    }");
            sb.AppendLine();
        }

        // Note: We cannot generate JsonSerializerContext here because the System.Text.Json source generator
        // doesn't process attributes from other source generators in the same compilation.
        // Instead, we'll use JsonSerializerOptions with TypeInfoResolver for AOT compatibility.

        return sb.ToString();
    }

    private static string GenerateSchemaValidator(HPD.Agent.SourceGenerator.Capabilities.FunctionCapability function, HarnessInfo Harness)
    {
        var relevantParams = function.Parameters.Where(p => p.IsModelFacing).ToList();

        if (!relevantParams.Any())
        {
            return
$@"        private static Func<JsonElement, JsonSerializerOptions, List<ValidationError>>? Create{function.Name}Validator() => (jsonArgs, serializerOptions) =>
        {{
            var errors = new List<ValidationError>();
            try
            {{
                HPDToolArgumentBinder.ValidateNoUnmappedProperties(jsonArgs, serializerOptions);
            }}
            catch (HPDToolArgumentException ex)
            {{
                errors.Add(new ValidationError {{ Property = ex.PropertyName, ErrorMessage = ex.Message, ErrorCode = ex.ErrorCode }});
            }}
            return errors;
        }};";
        }

        var dtoName = $"{function.Name}Args";
        var sb = new StringBuilder();
        sb.AppendLine($"        private static Func<JsonElement, JsonSerializerOptions, List<ValidationError>> Create{function.Name}Validator()");
        sb.AppendLine("        {");
        sb.AppendLine("            return (jsonArgs, serializerOptions) =>");
        sb.AppendLine("            {");
        sb.AppendLine("                var errors = new List<ValidationError>();");
        sb.AppendLine();

        sb.AppendLine("                // Parse and validate property types");
        sb.AppendLine("                try");
        sb.AppendLine("                {");
        sb.AppendLine($"                    HPDToolArgumentBinder.ValidateNoUnmappedProperties(jsonArgs, serializerOptions, {FormatStringArray(relevantParams.Select(p => p.Name))});");
        sb.AppendLine($"                    var dto = Parse{dtoName}(jsonArgs, serializerOptions);");

        sb.AppendLine("                }");
        sb.AppendLine("                catch (HPDToolArgumentException ex)");
        sb.AppendLine("                {");
        sb.AppendLine("                    errors.Add(new ValidationError { Property = ex.PropertyName, ErrorMessage = ex.Message, ErrorCode = ex.ErrorCode });");
        sb.AppendLine("                }");
        sb.AppendLine("                catch (JsonException ex)");
        sb.AppendLine("                {");
        sb.AppendLine("                    // Type conversion error - extract property name from exception if available");
        sb.AppendLine("                    string propertyName = ex.Path ?? \"Unknown\";");
        sb.AppendLine("                    errors.Add(new ValidationError { Property = propertyName, ErrorMessage = ex.Message, ErrorCode = \"type_conversion_error\" });");
        sb.AppendLine("                }");
        sb.AppendLine("                return errors;");
        sb.AppendLine("            };");
        sb.AppendLine("        }");
        return sb.ToString();
    }

    private static bool IsNullableParameter(ParameterInfo param)
    {
        // Simple heuristic - check if type ends with ?
        return param.Type.EndsWith("?");
    }

    private static string FormatStringArray(IEnumerable<string> values)
    {
        return string.Join(", ", values.Select(value => $"\"{value.Replace("\\", "\\\\").Replace("\"", "\\\"")}\""));
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
    /// Splits results into two buckets:
    /// <list type="bullet">
    /// <item><c>Parameterless</c> — types with a public parameterless constructor → <c>CollapseMiddlewareFactories</c></item>
    /// <item><c>ConfigConstructor</c> — types with a single config-parameter constructor → <c>CollapseMiddlewareConfigFactories</c> (§5A)</item>
    /// </list>
    /// Emits diagnostics for types that do not implement IAgentMiddleware or have neither constructor form.
    /// Returns null fields (not empty lists) when no types of a given kind are found.
    /// </summary>
    private static (List<string>? Parameterless, List<CollapseMiddlewareConfigEntry>? ConfigConstructor)
        GetCollapseMiddlewareTypeNames(
            ClassDeclarationSyntax classDecl,
            SemanticModel semanticModel,
            List<Diagnostic> diagnostics)
    {
        var allAttributes = classDecl.AttributeLists
            .SelectMany(attrList => attrList.Attributes);

        var attr = allAttributes.FirstOrDefault(a =>
            a.Name.ToString() == "Collapse" || a.Name.ToString() == "CollapseAttribute");

        if (attr?.ArgumentList == null)
            return (null, null);

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
            return (null, null);

        // Expression should be an array initializer: [typeof(T1), typeof(T2)]
        // Represented as CollectionExpressionSyntax (C# 12) or ArrayCreationExpression / ImplicitArrayCreation
        var parameterlessNames = new List<string>();
        var configEntries = new List<CollapseMiddlewareConfigEntry>();
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
            return (null, null);

        foreach (var elem in elements)
        {
            // Each element should be typeof(SomeMiddlewareType)
            if (elem is not TypeOfExpressionSyntax typeofExpr)
            {
                diagnostics.Add(Diagnostic.Create(
                    new DiagnosticDescriptor(
                        "HPDAG0201",
                        "Invalid Middlewares element",
                        "Harness '{0}': Middlewares array must contain only typeof() expressions.",
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

            // Check for IAgentMiddleware implementation
            bool implementsMiddleware = typeInfo.Type.AllInterfaces.Any(i =>
                i.Name == "IAgentMiddleware" || i.ToDisplayString().EndsWith(".IAgentMiddleware"));

            if (!implementsMiddleware)
            {
                diagnostics.Add(Diagnostic.Create(
                    new DiagnosticDescriptor(
                        "HPDAG0202",
                        "Middleware type does not implement IAgentMiddleware",
                        "Harness '{0}': Type '{1}' in Middlewares does not implement IAgentMiddleware.",
                        "HPDAgent.SourceGenerator",
                        DiagnosticSeverity.Error,
                        isEnabledByDefault: true),
                    location,
                    classDecl.Identifier.ValueText,
                    typeInfo.Type.Name));
                continue;
            }

            // Warn if not marked with IHarnessMiddleware
            bool implementsHarnessMarker = typeInfo.Type.AllInterfaces.Any(i =>
                i.Name == "IHarnessMiddleware" || i.ToDisplayString().EndsWith(".IHarnessMiddleware"));

            if (!implementsHarnessMarker)
            {
                diagnostics.Add(Diagnostic.Create(
                    new DiagnosticDescriptor(
                        "HPDAG0203",
                        "Middleware type does not implement IHarnessMiddleware",
                        "Harness '{0}': Type '{1}' is registered as scoped middleware but does not implement IHarnessMiddleware. " +
                        "Implement IHarnessMiddleware to signal harness-scoped intent. This is a warning only.",
                        "HPDAgent.SourceGenerator",
                        DiagnosticSeverity.Warning,
                        isEnabledByDefault: true),
                    location,
                    classDecl.Identifier.ValueText,
                    typeInfo.Type.Name));
                // Still include — marker is optional
            }

            if (typeInfo.Type is not INamedTypeSymbol namedType)
                continue;

            // §parameterless path: public parameterless constructor or value type
            bool hasParameterlessCtor =
                namedType.InstanceConstructors.Any(c => c.Parameters.IsEmpty && c.DeclaredAccessibility == Accessibility.Public)
                || namedType.IsValueType;

            if (hasParameterlessCtor)
            {
                parameterlessNames.Add(fqn);
                continue;
            }

            // §5A path: single public constructor whose sole parameter type name ends with "Config"
            var singleConfigCtor = namedType.InstanceConstructors.FirstOrDefault(c =>
                c.DeclaredAccessibility == Accessibility.Public
                && c.Parameters.Length == 1
                && (c.Parameters[0].Type.Name.EndsWith("Config") || c.Parameters[0].Type.Name.EndsWith("Options")));

            if (singleConfigCtor != null)
            {
                var configTypeFqn = singleConfigCtor.Parameters[0].Type
                    .ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
                configEntries.Add(new CollapseMiddlewareConfigEntry(
                    SimpleName: namedType.Name,
                    FullyQualifiedTypeName: fqn,
                    ConfigTypeFqn: configTypeFqn));
                continue;
            }

            // Neither form found — error, recommend DI path
            diagnostics.Add(Diagnostic.Create(
                new DiagnosticDescriptor(
                    "HPDAG0204",
                    "Scoped middleware requires a parameterless or single-config-parameter constructor",
                    "Harness '{0}': Type '{1}' has no public parameterless constructor and no single-Config/Options-parameter constructor. " +
                    "Use WithHarness<T>(opts => opts.AddScopedMiddleware(...)) to supply instances requiring DI.",
                    "HPDAgent.SourceGenerator",
                    DiagnosticSeverity.Error,
                    isEnabledByDefault: true),
                location,
                classDecl.Identifier.ValueText,
                typeInfo.Type.Name));
        }

        return (
            parameterlessNames.Count > 0 ? parameterlessNames : null,
            configEntries.Count > 0 ? configEntries : null
        );
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
                    "Harness '{0}' specifies both FunctionResult literal and expression. Use one or the other, not both.",
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
                    "Harness '{0}' specifies bothSystemPrompt literal and expression. Use one or the other, not both.",
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
                    "Harness '{0}' uses a string literal for {1} expression. Use the literal parameter instead, or provide a method/property reference.",
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
    /// Generates an argument parser that delegates conversion to the shared AOT-safe binder.
    /// </summary>
    private static string GenerateJsonParser(HPD.Agent.SourceGenerator.Capabilities.FunctionCapability function, HarnessInfo Harness)
    {
        var dtoName = $"{function.Name}Args";
        var relevantParams = function.Parameters.Where(p => p.IsModelFacing).ToList();
        
        var sb = new StringBuilder();
        sb.AppendLine($"        /// <summary>");
        sb.AppendLine($"        /// Parses JSON arguments for {dtoName}.");
        sb.AppendLine($"        /// </summary>");
        sb.AppendLine($"        private static {dtoName} Parse{dtoName}(JsonElement json, JsonSerializerOptions serializerOptions)");
        sb.AppendLine("        {");
        sb.AppendLine($"            var result = new {dtoName}();");
        sb.AppendLine();
        
        foreach (var param in relevantParams)
        {
            if (!IsNullableParameter(param) && !param.HasDefaultValue)
            {
                sb.AppendLine($"            result.{param.Name} = HPDToolArgumentBinder.BindRequired<{param.Type}>(json, \"{param.Name}\", serializerOptions);");
            }
            else
            {
                sb.AppendLine($"            result.{param.Name} = HPDToolArgumentBinder.BindOptional<{param.Type}>(json, \"{param.Name}\", result.{param.Name}, serializerOptions);");
            }
        }
        
        sb.AppendLine("            return result;");
        sb.AppendLine("        }");
        
        return sb.ToString();
    }

    /// <summary>
    /// Generates the GetToolMetadata() method for Harness Collapsing support.
    /// </summary>
    private static string GenerateToolMetadataMethod(HarnessInfo Harness)
    {
        var sb = new StringBuilder();

        var functionNamesArray = string.Join(", ", Harness.FunctionCapabilities.Select(f => $"\"{f.FunctionName}\""));
        var description = Harness.IsCollapsed && !string.IsNullOrEmpty(Harness.ContainerDescription)
            ? Harness.ContainerDescription
            : Harness.Description;

        sb.AppendLine("        private static ToolMetadata? _cachedMetadata;");
        sb.AppendLine();
        sb.AppendLine("        /// <summary>");
        sb.AppendLine($"        /// Gets metadata for the {Harness.ClassName} Harness (used for Collapsing).");
        sb.AppendLine("        /// </summary>");
        sb.AppendLine("        public static ToolMetadata GetToolMetadata()");
        sb.AppendLine("        {");
        sb.AppendLine("            return _cachedMetadata ??= new ToolMetadata");
        sb.AppendLine("            {");
        // Use EffectiveName for LLM-visible name (always ClassName now)
        sb.AppendLine($"                Name = \"{Harness.EffectiveName}\",");
        sb.AppendLine($"                Description = \"{description}\",");
        sb.AppendLine($"                FunctionNames = new string[] {{ {functionNamesArray} }},");
        sb.AppendLine($"                FunctionCount = {Harness.FunctionCapabilities.Count()},");
        sb.AppendLine($"                IsCollapsed = {Harness.IsCollapsed.ToString().ToLower()}");
        sb.AppendLine("            };");
        sb.AppendLine("        }");

        return sb.ToString();
    }

    /// <summary>
    /// Generates the container function for a Collapsed Harness.
    /// </summary>
    private static string GenerateContainerFunction(HarnessInfo Harness)
    {
        var sb = new StringBuilder();

        // Combine both AI functions and skills
        var allCapabilities = Harness.FunctionCapabilities.Select(f => f.FunctionName)
            .Concat(Harness.SkillCapabilities.Select(s => s.Name))
            .ToList();
        var capabilitiesList = string.Join(", ", allCapabilities);
        var totalCount = Harness.FunctionCapabilities.Count() + Harness.SkillCapabilities.Count();

        var description = !string.IsNullOrEmpty(Harness.ContainerDescription)
            ? Harness.ContainerDescription
            : Harness.Description ?? string.Empty;

        // Use shared helper to generate description and return message
        // Use EffectiveName for LLM-visible container name
        var fullDescription = HarnessContainerHelper.GenerateContainerDescription(description, Harness.EffectiveName, allCapabilities);

        sb.AppendLine("        /// <summary>");
        sb.AppendLine($"        /// Container function for {Harness.ClassName} Harness.");
        sb.AppendLine("        /// </summary>");
        // Method signature uses ClassName for type reference
        sb.AppendLine($"        private static AIFunction Create{Harness.ClassName}Container({Harness.ClassName} instance, HPDToolSerializationOptions? serialization)");
        sb.AppendLine("        {");
        sb.AppendLine("            return HPDAIFunctionFactory.Create(");
        sb.AppendLine("                async (arguments, functionContext, cancellationToken) =>");
        sb.AppendLine("                {");

        // Use the ContainerDescription (or Harness description as fallback) in the return message
        var returnMessage = HarnessContainerHelper.GenerateReturnMessage(description, allCapabilities, Harness.FunctionResult);

        if (!string.IsNullOrEmpty(Harness.FunctionResultExpression))
        {
            // Using an interpolated string to combine the base message and the dynamic instructions
            var baseMessage = HarnessContainerHelper.GenerateReturnMessage(description, allCapabilities, null);
            // Escape special characters for the interpolated string - we need to convert \n\n to \\n\\n in source code
            baseMessage = baseMessage.Replace("\\", "\\\\").Replace("\n", "\\n").Replace("\"", "\\\"");
            // Add separator between capabilities list and dynamic instructions
            var separator = "\\n\\n";  // This will be two backslash-n sequences in the source code

            // Use instance. prefix for instance methods, nothing for static
            var expressionCall = Harness.FunctionResultIsStatic
                ? Harness.FunctionResultExpression
                : $"instance.{Harness.FunctionResultExpression}";

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
        sb.AppendLine($"                    Name = \"{Harness.EffectiveName}\",");
        sb.AppendLine($"                    Description = \"{fullDescription}\",");
        sb.AppendLine("                    SchemaProvider = () => CreateEmptyContainerSchema(),");
        sb.AppendLine("                    SerializerOptions = serialization?.SerializerOptions,");
        sb.AppendLine("                    ResultType = typeof(string),");
        sb.AppendLine("                    AdditionalProperties = new Dictionary<string, object>");
        sb.AppendLine("                    {");
        sb.AppendLine("                        [\"IsContainer\"] = true,");
        // Use EffectiveName for HarnessName metadata (always ClassName now)
        sb.AppendLine($"                        [\"HarnessName\"] = \"{Harness.EffectiveName}\",");
        sb.AppendLine($"                        [\"FunctionNames\"] = new string[] {{ {string.Join(", ", allCapabilities.Select(c => $"\"{c}\""))} }},");
        sb.AppendLine($"                        [\"FunctionCount\"] = {totalCount},");

        // AddSystemPrompt to metadata (for middleware injection)
        if (!string.IsNullOrEmpty(Harness.SystemPrompt))
        {
            // Use verbatim string literal - only escape quotes (double them), NOT newlines
            var escapedSysPrompt = Harness.SystemPrompt.Replace("\"", "\"\"");
            sb.AppendLine($"                        [\"SystemPrompt\"] = @\"{escapedSysPrompt}\",");
        }
        else if (!string.IsNullOrEmpty(Harness.SystemPromptExpression))
        {
            // Expression - evaluate at container creation time
            // Use instance. prefix for instance methods, nothing for static
            var expressionCall = Harness.SystemPromptIsStatic
                ? Harness.SystemPromptExpression
                : $"instance.{Harness.SystemPromptExpression}";

            sb.AppendLine($"                        [\"SystemPrompt\"] = {expressionCall},");
        }

        // Optionally store FunctionResult for introspection
        if (!string.IsNullOrEmpty(Harness.FunctionResult))
        {
            // Use verbatim string literal - only escape quotes (double them), NOT newlines
            var escapedFuncResult = Harness.FunctionResult.Replace("\"", "\"\"");
            sb.AppendLine($"                        [\"FunctionResult\"] = @\"{escapedFuncResult}\"");
        }
        else if (!string.IsNullOrEmpty(Harness.FunctionResultExpression))
        {
            // Don't store expression in metadata (it's already executed in return statement)
            sb.AppendLine($"                        // FunctionResult is dynamic: {Harness.FunctionResultExpression}");
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
    /// Populates ResolvedFunctionReferences and ResolvedHarnessTypes from UnresolvedReferences.
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
                var referencedSkillName = $"{reference.HarnessType}.{reference.MethodName}";
                if (skillLookup.TryGetValue(referencedSkillName, out var referencedSkill))
                {
                    // Recursively resolve the referenced skill first
                    ResolveSkillCapability(referencedSkill, skillLookup, visited, stack, maxDepth);

                    // Add all its function references to our list
                    functionRefs.AddRange(referencedSkill.ResolvedFunctionReferences);
                    foreach (var pt in referencedSkill.ResolvedHarnessTypes)
                    {
                        toolTypes.Add(pt);
                    }
                }
            }
            else
            {
                // It's a function reference - add directly
                functionRefs.Add(reference.FullName);
                toolTypes.Add(reference.HarnessType);
            }
        }

        // Update the skill with resolved references
        skill.ResolvedFunctionReferences = functionRefs.Distinct().OrderBy(f => f).ToList();
        skill.ResolvedHarnessTypes = toolTypes.OrderBy(p => p).ToList();

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
