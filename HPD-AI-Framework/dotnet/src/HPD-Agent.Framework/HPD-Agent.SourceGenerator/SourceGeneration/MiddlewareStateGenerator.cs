// Copyright 2026 Einstein Essibu
// SPDX-License-Identifier: AGPL-3.0-only

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text;

namespace HPD.Agent.SourceGenerator;

/// <summary>
/// Incremental source generator for middleware state.
/// Generates per-assembly MiddlewareStateRegistry.g.cs and MiddlewareStateExtensions.g.cs.
/// Follows the ToolkitRegistry pattern for cross-assembly state discovery.
/// </summary>
[Generator]
public class MiddlewareStateGenerator : IIncrementalGenerator
{
    //
    // DIAGNOSTIC DESCRIPTORS
    //

    private static readonly DiagnosticDescriptor HPD001_MustBeRecord = new(
        id: "HPD001",
        title: "Middleware state must be a record",
        messageFormat: "[MiddlewareState] can only be applied to record types. Class '{0}' must be declared as a record.",
        category: "Design",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "Records provide immutability guarantees, structural equality, and 'with' expressions that are essential for the immutable state pattern.");

    private static readonly DiagnosticDescriptor HPD002_ShouldBeSealed = new(
        id: "HPD002",
        title: "Middleware state should be sealed",
        messageFormat: "Middleware state record '{0}' should be sealed for performance. Consider adding 'sealed' modifier.",
        category: "Design",
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "Sealed records enable compiler optimizations (devirtualization) and prevent unintended inheritance.");

    private static readonly DiagnosticDescriptor HPD003_DuplicateKey = new(
        id: "HPD003",
        title: "Duplicate middleware state key",
        messageFormat: "Middleware state key '{0}' is already registered by type '{1}'. Each middleware state must have a unique fully-qualified name.",
        category: "Design",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "The container uses fully-qualified type names as dictionary keys. Duplicates would cause runtime conflicts.");

    private static readonly DiagnosticDescriptor HPD005_PropertyNameConflict = new(
        id: "HPD005",
        title: "Middleware state property name conflicts with container API",
        messageFormat: "Generated property name '{0}' conflicts with MiddlewareState API. Rename type '{1}' to avoid conflicts with: GetState, SetState, States.",
        category: "Design",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "The generator creates extension methods based on type names. Conflicts with base class methods would cause compilation errors.");

    // Reserved names that would conflict with the container API
    private static readonly HashSet<string> ReservedPropertyNames = new(System.StringComparer.Ordinal)
    {
        "GetState",
        "SetState",
        "States"
    };

    //
    // INITIALIZATION
    //

    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        // Find all types with [MiddlewareState] attribute in SOURCE CODE ONLY
        // Each assembly generates its own registry - no cross-assembly scanning
        var sourceStateTypes = context.SyntaxProvider
            .ForAttributeWithMetadataName(
                fullyQualifiedMetadataName: "HPD.Agent.MiddlewareStateAttribute",
                predicate: (node, _) => node is TypeDeclarationSyntax,
                transform: GetStateInfo)
            .Where(static info => info is not null);

        // Collect all state types and generate registry + extensions
        var collectedTypes = sourceStateTypes.Collect();

        context.RegisterSourceOutput(
            collectedTypes,
            (spc, types) => GenerateRegistryAndExtensions(spc, types!));
    }

    //
    // STATE INFO EXTRACTION
    //

    private StateInfo? GetStateInfo(
        GeneratorAttributeSyntaxContext context,
        CancellationToken ct)
    {
        if (context.TargetSymbol is not INamedTypeSymbol typeSymbol)
            return null;

        var syntax = context.TargetNode as TypeDeclarationSyntax;
        if (syntax is null)
            return null;

        var diagnostics = new List<Diagnostic>();

        // HPD001: Must be a record
        if (typeSymbol.TypeKind != TypeKind.Class || !typeSymbol.IsRecord)
        {
            diagnostics.Add(Diagnostic.Create(
                HPD001_MustBeRecord,
                syntax.Identifier.GetLocation(),
                typeSymbol.Name));
            return new StateInfo(
                TypeName: typeSymbol.Name,
                FullyQualifiedName: "",
                PropertyName: "",
                Namespace: "",
                Version: 1,
                Persistent: false,
                Scope: "Branch",
                Diagnostics: diagnostics);
        }

        // HPD002: Should be sealed (warning only)
        if (!typeSymbol.IsSealed)
        {
            diagnostics.Add(Diagnostic.Create(
                HPD002_ShouldBeSealed,
                syntax.Identifier.GetLocation(),
                typeSymbol.Name));
        }

        // Extract info
        var typeName = typeSymbol.Name;
        var fullyQualifiedName = typeSymbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)
            .Replace("global::", ""); // Remove global:: prefix
        var namespaceName = typeSymbol.ContainingNamespace?.ToDisplayString() ?? "";
        var propertyName = GetPropertyName(typeName);

        // Extract version, persistent, and scope from [MiddlewareState(Version = X, Persistent = Y, Scope = Z)] attribute
        int version = 1; // Default
        bool persistent = false; // Default
        string scope = "Branch"; // Default (StateScope.Branch = 0)
        var attribute = context.Attributes.FirstOrDefault();
        if (attribute != null)
        {
            foreach (var namedArg in attribute.NamedArguments)
            {
                if (namedArg.Key == "Version" && namedArg.Value.Value is int v)
                {
                    version = v;
                }
                else if (namedArg.Key == "Persistent" && namedArg.Value.Value is bool p)
                {
                    persistent = p;
                }
                else if (namedArg.Key == "Scope" && namedArg.Value.Value is int s)
                {
                    // StateScope enum: Branch = 0, Session = 1
                    scope = s == 1 ? "Session" : "Branch";
                }
            }
        }

        // HPD005: Check for property name conflicts
        if (ReservedPropertyNames.Contains(propertyName))
        {
            diagnostics.Add(Diagnostic.Create(
                HPD005_PropertyNameConflict,
                syntax.Identifier.GetLocation(),
                propertyName,
                typeName));
            return new StateInfo(
                TypeName: typeName,
                FullyQualifiedName: fullyQualifiedName,
                PropertyName: propertyName,
                Namespace: namespaceName,
                Version: version,
                Persistent: persistent,
                Scope: scope,
                Diagnostics: diagnostics);
        }

        return new StateInfo(
            TypeName: typeName,
            FullyQualifiedName: fullyQualifiedName,
            PropertyName: propertyName,
            Namespace: namespaceName,
            Version: version,
            Persistent: persistent,
            Scope: scope,
            Diagnostics: diagnostics);
    }

    /// <summary>
    /// Generates property name from type name.
    /// Examples: "CircuitBreakerStateData" -> "CircuitBreaker"
    ///           "ErrorTrackingStateData" -> "ErrorTracking"
    /// </summary>
    private static string GetPropertyName(string typeName)
    {
        // Remove common suffixes
        var suffixes = new[] { "StateData", "State", "Data" };
        foreach (var suffix in suffixes)
        {
            if (typeName.EndsWith(suffix) && typeName.Length > suffix.Length)
            {
                return typeName.Substring(0, typeName.Length - suffix.Length);
            }
        }
        return typeName;
    }

    //
    // CODE GENERATION
    //

    private void GenerateRegistryAndExtensions(
        SourceProductionContext context,
        ImmutableArray<StateInfo> types)
    {
        if (types.IsEmpty)
            return;

        // Report all diagnostics from state info collection
        foreach (var type in types)
        {
            foreach (var diagnostic in type.Diagnostics)
            {
                context.ReportDiagnostic(diagnostic);
            }
        }

        // Filter out types with errors (keep warnings)
        var validTypes = types
            .Where(t => !t.Diagnostics.Any(d => d.Severity == DiagnosticSeverity.Error))
            .ToList();

        if (validTypes.Count == 0)
            return;

        // HPD003: Check for duplicate keys
        var duplicates = validTypes
            .GroupBy(t => t.FullyQualifiedName)
            .Where(g => g.Count() > 1)
            .ToList();

        foreach (var duplicate in duplicates)
        {
            var first = duplicate.First();
            var others = duplicate.Skip(1);
            foreach (var other in others)
            {
                context.ReportDiagnostic(Diagnostic.Create(
                    HPD003_DuplicateKey,
                    Location.None,
                    duplicate.Key,
                    first.TypeName));
            }
        }

        // Remove duplicates for generation
        var uniqueTypes = validTypes
            .GroupBy(t => t.FullyQualifiedName)
            .Select(g => g.First())
            .ToList();

        // Generate MiddlewareStateRegistry.g.cs
        GenerateRegistry(context, uniqueTypes);

        // Generate MiddlewareStateExtensions.g.cs
        GenerateExtensions(context, uniqueTypes);
    }

    /// <summary>
    /// Generates MiddlewareStateRegistry.g.cs with factory array.
    /// This follows the ToolkitRegistry pattern for cross-assembly discovery.
    /// </summary>
    private void GenerateRegistry(
        SourceProductionContext context,
        List<StateInfo> uniqueTypes)
    {
        var sb = new StringBuilder();
        sb.AppendLine("// <auto-generated/>");
        sb.AppendLine("// Generated by MiddlewareStateGenerator");
        sb.AppendLine();
        sb.AppendLine("#nullable enable");
        sb.AppendLine();
        sb.AppendLine("using System;");
        sb.AppendLine("using System.Text.Json;");
        sb.AppendLine("using HPD.Agent;");
        sb.AppendLine("using Microsoft.Extensions.AI;");
        sb.AppendLine();
        sb.AppendLine("namespace HPD.Agent.Generated;");
        sb.AppendLine();
        sb.AppendLine("/// <summary>");
        sb.AppendLine("/// Registry of middleware state types in this assembly.");
        sb.AppendLine("/// Loaded by AgentBuilder.LoadStateRegistryFromAssembly() at runtime.");
        sb.AppendLine("/// </summary>");
        sb.AppendLine("public static class MiddlewareStateRegistry");
        sb.AppendLine("{");
        sb.AppendLine("    /// <summary>");
        sb.AppendLine("    /// All middleware state factories in this assembly.");
        sb.AppendLine("    /// </summary>");
        sb.AppendLine("    public static readonly MiddlewareStateFactory[] All = new MiddlewareStateFactory[]");
        sb.AppendLine("    {");

        foreach (var stateInfo in uniqueTypes)
        {
            sb.AppendLine($"        new MiddlewareStateFactory(");
            sb.AppendLine($"            FullyQualifiedName: \"{stateInfo.FullyQualifiedName}\",");
            sb.AppendLine($"            StateType: typeof({stateInfo.FullyQualifiedName}),");
            sb.AppendLine($"            PropertyName: \"{stateInfo.PropertyName}\",");
            sb.AppendLine($"            Version: {stateInfo.Version},");
            sb.AppendLine($"            Persistent: {(stateInfo.Persistent ? "true" : "false")},");
            sb.AppendLine($"            Scope: StateScope.{stateInfo.Scope},");
            sb.AppendLine($"            Deserialize: json => JsonSerializer.Deserialize<{stateInfo.FullyQualifiedName}>(json, AIJsonUtilities.DefaultOptions),");
            sb.AppendLine($"            Serialize: state => JsonSerializer.Serialize(({stateInfo.FullyQualifiedName})state, AIJsonUtilities.DefaultOptions)");
            sb.AppendLine($"        ),");
        }

        sb.AppendLine("    };");
        sb.AppendLine("}");

        context.AddSource("MiddlewareStateRegistry.g.cs", sb.ToString());
    }

    /// <summary>
    /// Generates MiddlewareStateExtensions.g.cs with typed extension methods.
    /// This provides consistent API regardless of which assembly defines the state.
    /// </summary>
    private void GenerateExtensions(
        SourceProductionContext context,
        List<StateInfo> uniqueTypes)
    {
        // Group types by namespace to generate proper using statements
        var namespaces = uniqueTypes
            .Select(t => t.Namespace)
            .Where(ns => !string.IsNullOrEmpty(ns))
            .Distinct()
            .OrderBy(ns => ns)
            .ToList();

        // Determine the namespace for extensions (use the first state's namespace or HPD.Agent)
        var extensionNamespace = namespaces.FirstOrDefault() ?? "HPD.Agent";

        var sb = new StringBuilder();
        sb.AppendLine("// <auto-generated/>");
        sb.AppendLine("// Generated by MiddlewareStateGenerator");
        sb.AppendLine();
        sb.AppendLine("#nullable enable");
        sb.AppendLine();
        sb.AppendLine("using HPD.Agent;");

        // Add using statements for all namespaces that contain state types
        foreach (var ns in namespaces)
        {
            if (ns != "HPD.Agent" && ns != extensionNamespace)
            {
                sb.AppendLine($"using {ns};");
            }
        }

        sb.AppendLine();
        sb.AppendLine($"namespace {extensionNamespace};");
        sb.AppendLine();
        sb.AppendLine("/// <summary>");
        sb.AppendLine("/// Extension methods for accessing middleware states defined in this assembly.");
        sb.AppendLine("/// All state access uses extension methods for consistent API.");
        sb.AppendLine("/// </summary>");
        sb.AppendLine("public static class MiddlewareStateExtensions");
        sb.AppendLine("{");

        // Generate key constants
        foreach (var stateInfo in uniqueTypes)
        {
            sb.AppendLine($"    private const string {stateInfo.PropertyName}Key = \"{stateInfo.FullyQualifiedName}\";");
        }
        sb.AppendLine();

        // Generate extension methods for each state type
        foreach (var stateInfo in uniqueTypes)
        {
            sb.AppendLine($"    //");
            sb.AppendLine($"    // {stateInfo.PropertyName} ({stateInfo.TypeName})");
            sb.AppendLine($"    //");
            sb.AppendLine();

            // Generate getter extension method
            sb.AppendLine($"    /// <summary>");
            sb.AppendLine($"    /// Gets {stateInfo.PropertyName} middleware state.");
            sb.AppendLine($"    /// </summary>");
            sb.AppendLine($"    public static {stateInfo.FullyQualifiedName}? {stateInfo.PropertyName}(this MiddlewareState state)");
            sb.AppendLine($"        => state.GetState<{stateInfo.FullyQualifiedName}>({stateInfo.PropertyName}Key);");
            sb.AppendLine();

            // Generate WithX extension method
            sb.AppendLine($"    /// <summary>");
            sb.AppendLine($"    /// Creates a new container with updated {stateInfo.PropertyName} state (immutable).");
            sb.AppendLine($"    /// </summary>");
            sb.AppendLine($"    /// <param name=\"state\">The middleware state container.</param>");
            sb.AppendLine($"    /// <param name=\"value\">New state value (null to keep unchanged).</param>");
            sb.AppendLine($"    /// <returns>New container instance with updated state.</returns>");
            sb.AppendLine($"    public static MiddlewareState With{stateInfo.PropertyName}(");
            sb.AppendLine($"        this MiddlewareState state,");
            sb.AppendLine($"        {stateInfo.FullyQualifiedName}? value)");
            sb.AppendLine($"    {{");
            sb.AppendLine($"        return value == null ? state : state.SetState({stateInfo.PropertyName}Key, value);");
            sb.AppendLine($"    }}");
            sb.AppendLine();
        }

        sb.AppendLine("}");

        context.AddSource("MiddlewareStateExtensions.g.cs", sb.ToString());
    }

    //
    // STATE INFO RECORD
    //

    private record StateInfo(
        string TypeName,
        string FullyQualifiedName,
        string PropertyName,
        string Namespace,
        int Version,
        bool Persistent,
        string Scope,
        List<Diagnostic> Diagnostics);
}
