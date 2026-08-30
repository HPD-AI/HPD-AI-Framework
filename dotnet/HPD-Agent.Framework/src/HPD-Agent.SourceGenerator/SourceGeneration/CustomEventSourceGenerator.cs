// Copyright 2026 Einstein Essibu
// SPDX-License-Identifier: FSL-1.1-ALv2

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace HPD.Agent.SourceGenerator;

/// <summary>
/// Incremental source generator for custom AgentEvent and AgentStructEvent types.
/// Auto-discovers user-defined events extending AgentEvent or implementing AgentStructEvent and generates:
/// - EventTypes constants (SCREAMING_SNAKE_CASE)
/// - TypeNames dictionary registrations
/// - Serializer registration
/// </summary>
[Generator]
public class CustomEventSourceGenerator : IIncrementalGenerator
{
    #region Diagnostic Descriptors

    private static readonly DiagnosticDescriptor HPD010_DuplicateEventType = new(
        id: "HPD010",
        title: "Duplicate event type discriminator",
        messageFormat: "Multiple events generate the same type discriminator '{0}': {1}. Consider renaming one of the events or using [EventType(\"CUSTOM_NAME\")] attribute.",
        category: "HPD.Agent.Serialization",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "Each custom event must have a unique type discriminator for proper JSON serialization.");

    private static readonly DiagnosticDescriptor HPD011_GenericEventNotSupported = new(
        id: "HPD011",
        title: "Generic events not supported",
        messageFormat: "Event type '{0}' is generic. Custom events cannot use type parameters. Consider creating concrete event types instead.",
        category: "HPD.Agent.Serialization",
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "Generic events cannot be serialized properly because type parameters are not known at compile time.");

    private static readonly DiagnosticDescriptor HPD012_AbstractEventSkipped = new(
        id: "HPD012",
        title: "Abstract event skipped",
        messageFormat: "Abstract event type '{0}' will not be registered. Only concrete event types are serializable.",
        category: "HPD.Agent.Serialization",
        defaultSeverity: DiagnosticSeverity.Info,
        isEnabledByDefault: true,
        description: "Abstract event types are valid base classes but cannot be serialized directly.");

    private static readonly DiagnosticDescriptor HPDAEVT002_MissingJsonMetadata = new(
        id: "HPDAEVT002",
        title: "Agent event is missing generated JSON metadata",
        messageFormat: "Event '{0}' is not declared by [JsonSerializable] on context '{1}'",
        category: "HPD.Agent.Serialization",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    #endregion

    #region Initialization

    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        // Find all record types that inherit from AgentEvent or implement AgentStructEvent.
        var customEvents = context.SyntaxProvider
            .CreateSyntaxProvider(
                predicate: static (node, ct) => IsCustomEventCandidate(node),
                transform: static (ctx, ct) => GetCustomEventInfo(ctx, ct))
            .Where(static evt => evt is not null);

        // Collect all events and generate registration code
        context.RegisterSourceOutput(
            context.CompilationProvider.Combine(customEvents.Collect()).Combine(context.AnalyzerConfigOptionsProvider),
            (spc, value) => GenerateEventRegistrations(
                spc,
                value.Left.Left,
                value.Left.Right!,
                value.Right));
    }

    #endregion

    #region Syntax Predicate

    /// <summary>
    /// Quick syntactic check for potential custom event types.
    /// </summary>
    private static bool IsCustomEventCandidate(SyntaxNode node)
    {
        // Only check record declarations
        if (node is not RecordDeclarationSyntax recordDecl)
            return false;

        // Must have a base type
        var baseList = recordDecl.BaseList;
        if (baseList == null)
            return false;

        // Semantic analysis below follows the complete inheritance chain. Keeping
        // this predicate broad is required for events derived through module-local
        // abstract event bases.
        return baseList.Types.Count > 0;
    }

    #endregion

    #region Semantic Analysis

    /// <summary>
    /// Extracts custom event info with full semantic analysis.
    /// </summary>
    private static CustomEventInfo? GetCustomEventInfo(
        GeneratorSyntaxContext context,
        CancellationToken ct)
    {
        var recordDecl = (RecordDeclarationSyntax)context.Node;
        var semanticModel = context.SemanticModel;

        var symbol = semanticModel.GetDeclaredSymbol(recordDecl, ct);
        if (symbol is not INamedTypeSymbol typeSymbol)
            return null;

        var diagnostics = new List<Diagnostic>();

        // Skip framework contracts themselves.
        if (typeSymbol.Name == "AgentEvent" || typeSymbol.Name == "AgentStructEvent")
            return null;
        if (!IsModuleVisible(typeSymbol))
            return null;

        var namespaceName = typeSymbol.ContainingNamespace?.ToDisplayString() ?? "";

        // Skip generic types with warning
        if (typeSymbol.IsGenericType)
        {
            diagnostics.Add(Diagnostic.Create(
                HPD011_GenericEventNotSupported,
                recordDecl.Identifier.GetLocation(),
                typeSymbol.Name));
            return new CustomEventInfo(
                Name: typeSymbol.Name,
                Namespace: namespaceName,
                FullTypeName: typeSymbol.ToDisplayString(),
                ScreamingSnakeCaseName: "",
                Kind: EventRegistrationKind.AgentEvent,
                IsValid: false,
                Diagnostics: diagnostics);
        }

        // Skip abstract types with info
        if (typeSymbol.IsAbstract)
        {
            diagnostics.Add(Diagnostic.Create(
                HPD012_AbstractEventSkipped,
                recordDecl.Identifier.GetLocation(),
                typeSymbol.Name));
            return new CustomEventInfo(
                Name: typeSymbol.Name,
                Namespace: namespaceName,
                FullTypeName: typeSymbol.ToDisplayString(),
                ScreamingSnakeCaseName: "",
                Kind: EventRegistrationKind.AgentEvent,
                IsValid: false,
                Diagnostics: diagnostics);
        }

        var eventKind = GetEventRegistrationKind(typeSymbol);
        if (eventKind is null)
            return null;

        // Check for [EventType("CUSTOM_NAME")] attribute override
        var customDiscriminator = GetCustomEventTypeAttribute(typeSymbol);
        var durability = HasAttribute(typeSymbol, "DurableEventAttribute", "DurableEvent")
            ? "Durable"
            : "LiveOnly";
        var contentPolicy = GetContentPolicy(typeSymbol);
        var discriminator = customDiscriminator ?? ToScreamingSnakeCase(typeSymbol.Name);

        return new CustomEventInfo(
            Name: typeSymbol.Name,
            Namespace: namespaceName,
            FullTypeName: typeSymbol.ToDisplayString(),
            ScreamingSnakeCaseName: discriminator,
            Kind: eventKind.Value,
            IsValid: true,
            Diagnostics: diagnostics,
            Durability: durability,
            ContentPolicy: contentPolicy);
    }

    /// <summary>
    /// Gets the registration kind for a supported custom event type.
    /// </summary>
    private static EventRegistrationKind? GetEventRegistrationKind(INamedTypeSymbol typeSymbol)
    {
        var baseType = typeSymbol.BaseType;
        while (baseType != null)
        {
            if (baseType.Name == "AgentEvent")
                return EventRegistrationKind.AgentEvent;
            baseType = baseType.BaseType;
        }

        if (!typeSymbol.IsValueType)
            return null;

        return ImplementsInterface(typeSymbol, "AgentStructEvent")
            ? EventRegistrationKind.AgentStructEvent
            : null;
    }

    private static bool ImplementsInterface(INamedTypeSymbol typeSymbol, string interfaceName)
    {
        foreach (var iface in typeSymbol.AllInterfaces)
        {
            if (iface.Name == interfaceName)
                return true;
        }

        return false;
    }

    private static bool IsModuleVisible(INamedTypeSymbol typeSymbol)
    {
        for (var current = typeSymbol; current is not null; current = current.ContainingType)
            if (current.DeclaredAccessibility is not Accessibility.Public and not Accessibility.Internal)
                return false;
        return true;
    }

    /// <summary>
    /// Gets custom type discriminator from [EventType("...")] attribute if present.
    /// </summary>
    private static string? GetCustomEventTypeAttribute(INamedTypeSymbol typeSymbol)
    {
        foreach (var attr in typeSymbol.GetAttributes())
        {
            if (attr.AttributeClass?.Name == "EventTypeAttribute" ||
                attr.AttributeClass?.Name == "EventType")
            {
                if (attr.ConstructorArguments.Length > 0 &&
                    attr.ConstructorArguments[0].Value is string discriminator)
                {
                    return discriminator;
                }
            }
        }
        return null;
    }

    private static bool HasAttribute(INamedTypeSymbol typeSymbol, string longName, string shortName)
        => typeSymbol.GetAttributes().Any(attr =>
            attr.AttributeClass?.Name == longName || attr.AttributeClass?.Name == shortName);

    private static EventContentPolicyInfo? GetContentPolicy(INamedTypeSymbol typeSymbol)
    {
        var attribute = typeSymbol.GetAttributes().FirstOrDefault(attr =>
            attr.AttributeClass?.Name is "PersistEventContentAttribute" or "PersistEventContent");
        if (attribute is null || attribute.ConstructorArguments.Length == 0 ||
            attribute.ConstructorArguments[0].Value is not string kind)
            return null;

        var contentType = "application/json";
        var origin = "Agent";
        string? scope = null;
        foreach (var argument in attribute.NamedArguments)
        {
            if (argument.Key == "ContentType" && argument.Value.Value is string configuredContentType)
                contentType = configuredContentType;
            else if (argument.Key == "Origin" && argument.Value.Value is int originValue)
                origin = originValue == 0 ? "User" : originValue == 2 ? "System" : "Agent";
            else if (argument.Key == "Scope" && argument.Value.Value is string configuredScope)
                scope = configuredScope;
        }
        return new EventContentPolicyInfo(kind.Trim(), contentType, origin, scope);
    }

    /// <summary>
    /// Converts PascalCase event name to SCREAMING_SNAKE_CASE.
    /// </summary>
    private static string ToScreamingSnakeCase(string pascalCase)
    {
        // Remove "Event" suffix if present
        if (pascalCase.EndsWith("Event"))
            pascalCase = pascalCase.Substring(0, pascalCase.Length - 5);

        // Insert underscores before capitals and uppercase
        var result = new StringBuilder();
        for (int i = 0; i < pascalCase.Length; i++)
        {
            var c = pascalCase[i];
            if (i > 0 && char.IsUpper(c) && char.IsLower(pascalCase[i - 1]))
            {
                result.Append('_');
            }
            result.Append(char.ToUpperInvariant(c));
        }
        return result.ToString();
    }

    #endregion

    #region Code Generation

    /// <summary>
    /// Generates all registration code for discovered custom events.
    /// </summary>
    private static void GenerateEventRegistrations(
        SourceProductionContext context,
        Compilation compilation,
        ImmutableArray<CustomEventInfo?> events,
        AnalyzerConfigOptionsProvider options)
    {
        // Filter valid events and report diagnostics
        var validEvents = new List<CustomEventInfo>();
        foreach (var evt in events)
        {
            if (evt == null) continue;

            // Report any diagnostics
            foreach (var diagnostic in evt.Diagnostics)
            {
                context.ReportDiagnostic(diagnostic);
            }

            if (evt.IsValid)
            {
                validEvents.Add(evt);
            }
        }

        var assemblyAttributes = compilation.Assembly.GetAttributes();
        options.GlobalOptions.TryGetValue("build_property.HpdAgentApplication", out var applicationOption);
        options.GlobalOptions.TryGetValue("build_property.NativeLib", out var nativeLib);
        var isApplication = bool.TryParse(applicationOption, out var explicitlyApplication)
            ? explicitlyApplication
            : compilation.Options.OutputKind is OutputKind.ConsoleApplication or OutputKind.WindowsApplication ||
                !string.IsNullOrWhiteSpace(nativeLib);
        if (validEvents.Count == 0 && !isApplication)
            return;

        var validAgentEvents = validEvents
            .Where(e => e.Kind == EventRegistrationKind.AgentEvent)
            .ToList();
        var validStructEvents = validEvents
            .Where(e => e.Kind == EventRegistrationKind.AgentStructEvent)
            .ToList();

        // Check for duplicate type discriminators within each serializer surface.
        var agentDuplicates = validAgentEvents
            .GroupBy(e => e.ScreamingSnakeCaseName)
            .Where(g => g.Count() > 1)
            .ToList();
        var structDuplicates = validStructEvents
            .GroupBy(e => e.ScreamingSnakeCaseName)
            .Where(g => g.Count() > 1)
            .ToList();

        if (agentDuplicates.Any() || structDuplicates.Any())
        {
            foreach (var group in agentDuplicates.Concat(structDuplicates))
            {
                var types = string.Join(", ", group.Select(e => e.FullTypeName));
                context.ReportDiagnostic(Diagnostic.Create(
                    HPD010_DuplicateEventType,
                    Location.None,
                    group.Key,
                    types));
            }
            return; // Don't generate code with conflicts
        }

        var hasHandwrittenManifest = assemblyAttributes.Any(static attribute =>
            attribute.AttributeClass?.Name == "HpdAgentEventModuleManifestAttribute");
        options.GlobalOptions.TryGetValue("build_property.HpdAgentEventModuleId", out var configuredModuleId);
        var moduleId = !string.IsNullOrWhiteSpace(configuredModuleId)
            ? configuredModuleId!
            : compilation.AssemblyName
            ?? "HPD.Agent.Module";
        var jsonContextType = FindJsonContext(compilation, validAgentEvents);

        if (validAgentEvents.Count > 0 && !hasHandwrittenManifest)
        {
            context.AddSource("CustomEventTypes.g.cs",
                GenerateEventTypesPartial(validAgentEvents));

            if (jsonContextType is null)
            {
                foreach (var evt in validAgentEvents)
                    context.ReportDiagnostic(Diagnostic.Create(
                        HPDAEVT002_MissingJsonMetadata,
                        Location.None,
                        evt.FullTypeName,
                        "an assembly-local JsonSerializerContext"));
                return;
            }

            var metadataTypes = new HashSet<string>(jsonContextType.GetAttributes()
                .Where(static attribute => attribute.AttributeClass?.Name == "JsonSerializableAttribute")
                .Select(static attribute => attribute.ConstructorArguments.FirstOrDefault().Value as INamedTypeSymbol)
                .Where(static type => type is not null)
                .Select(static type => type!.ToDisplayString()), StringComparer.Ordinal);
            var missingMetadata = false;
            foreach (var evt in validAgentEvents.Where(evt => !metadataTypes.Contains(evt.FullTypeName)))
            {
                missingMetadata = true;
                context.ReportDiagnostic(Diagnostic.Create(HPDAEVT002_MissingJsonMetadata, Location.None, evt.FullTypeName, jsonContextType.ToDisplayString()));
            }
            if (missingMetadata)
                return;

            context.AddSource("GeneratedAgentEventModule.g.cs",
                GenerateEventModule(validAgentEvents, moduleId, jsonContextType));
        }
        if (isApplication)
        {
            var providers = new List<string>();
            AddManifestProviders(compilation.Assembly, providers);
            foreach (var referencedAssembly in compilation.SourceModule.ReferencedAssemblySymbols)
                AddManifestProviders(referencedAssembly, providers);
            if (!hasHandwrittenManifest && validAgentEvents.Count > 0 && jsonContextType is not null)
            {
                providers.Add($"global::HPD.Agent.Serialization.{GetGeneratedProviderTypeName(moduleId)}");
            }
            context.AddSource("GeneratedAgentEventComposition.g.cs",
                GenerateApplicationComposition(
                    compilation.AssemblyName ?? "HPD.Agent.Application",
                    providers));
        }

        if (validStructEvents.Count > 0)
        {
            context.AddSource("CustomStructEventTypes.g.cs",
                GenerateStructEventTypesPartial(validStructEvents));

            context.AddSource("CustomStructEventSerializer.g.cs",
                GenerateStructSerializerPartial(validStructEvents));
        }
    }

    private static INamedTypeSymbol? FindJsonContext(
        Compilation compilation,
        IReadOnlyList<CustomEventInfo> events)
    {
        var required = new HashSet<string>(
            events.Select(static value => value.FullTypeName),
            StringComparer.Ordinal);
        foreach (var type in GetAllTypes(compilation.Assembly.GlobalNamespace))
        {
            if (!InheritsFrom(type, "JsonSerializerContext"))
                continue;
            var declared = new HashSet<string>(type.GetAttributes()
                .Where(static attribute => attribute.AttributeClass?.Name == "JsonSerializableAttribute")
                .Select(static attribute => attribute.ConstructorArguments.FirstOrDefault().Value as INamedTypeSymbol)
                .Where(static value => value is not null)
                .Select(static value => value!.ToDisplayString()),
                StringComparer.Ordinal);
            if (required.All(declared.Contains))
                return type;
        }
        return null;
    }

    private static IEnumerable<INamedTypeSymbol> GetAllTypes(INamespaceSymbol namespaceSymbol)
    {
        foreach (var type in namespaceSymbol.GetTypeMembers())
        {
            yield return type;
            foreach (var nested in GetNestedTypes(type))
                yield return nested;
        }
        foreach (var child in namespaceSymbol.GetNamespaceMembers())
        foreach (var type in GetAllTypes(child))
            yield return type;
    }

    private static IEnumerable<INamedTypeSymbol> GetNestedTypes(INamedTypeSymbol type)
    {
        foreach (var nested in type.GetTypeMembers())
        {
            yield return nested;
            foreach (var descendant in GetNestedTypes(nested))
                yield return descendant;
        }
    }

    private static bool InheritsFrom(INamedTypeSymbol type, string baseName)
    {
        for (var current = type.BaseType; current is not null; current = current.BaseType)
            if (current.Name == baseName)
                return true;
        return false;
    }

    /// <summary>
    /// Generates partial EventTypes class with custom event constants.
    /// </summary>
    private static string GenerateEventTypesPartial(List<CustomEventInfo> events)
    {
        var sb = new StringBuilder();
        sb.AppendLine("// <auto-generated/>");
        sb.AppendLine("#nullable enable");
        sb.AppendLine();
        sb.AppendLine("namespace HPD.Agent.Serialization;");
        sb.AppendLine();
        sb.AppendLine("internal static class CustomEventTypes");
        sb.AppendLine("{");
        sb.AppendLine("    /// <summary>");
        sb.AppendLine("    /// Auto-generated constants for custom event type discriminators.");
        sb.AppendLine("    /// </summary>");

        foreach (var evt in events.OrderBy(e => e.ScreamingSnakeCaseName))
        {
            sb.AppendLine($"    /// <summary>Auto-generated from {evt.FullTypeName}</summary>");
            sb.AppendLine($"    public const string {evt.ScreamingSnakeCaseName} = \"{evt.ScreamingSnakeCaseName}\";");
        }

        sb.AppendLine("}");

        return sb.ToString();
    }

    private static string GenerateStructEventTypesPartial(List<CustomEventInfo> events)
    {
        var sb = new StringBuilder();
        sb.AppendLine("// <auto-generated/>");
        sb.AppendLine("#nullable enable");
        sb.AppendLine();
        sb.AppendLine("namespace HPD.Agent.Serialization");
        sb.AppendLine("{");
        sb.AppendLine();
        sb.AppendLine("internal static class CustomStructEventTypes");
        sb.AppendLine("{");
        sb.AppendLine("    /// <summary>");
        sb.AppendLine("    /// Auto-generated constants for custom struct event type discriminators.");
        sb.AppendLine("    /// </summary>");

        foreach (var evt in events.OrderBy(e => e.ScreamingSnakeCaseName))
        {
            sb.AppendLine($"    /// <summary>Auto-generated from {evt.FullTypeName}</summary>");
            sb.AppendLine($"    public const string {evt.ScreamingSnakeCaseName} = \"{evt.ScreamingSnakeCaseName}\";");
        }

        sb.AppendLine("}");
        sb.AppendLine("}");

        return sb.ToString();
    }

    /// <summary>
    /// Generates one immutable module fragment and assembly manifest for custom events.
    /// </summary>
    private static string GenerateEventModule(
        List<CustomEventInfo> events,
        string moduleId,
        INamedTypeSymbol jsonContextType)
    {
        var contextName = jsonContextType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
        var providerTypeName = GetGeneratedProviderTypeName(moduleId);
        var sb = new StringBuilder();
        sb.AppendLine("// <auto-generated/>");
        sb.AppendLine("#nullable enable");
        sb.AppendLine($"[assembly: global::HPD.Agent.Serialization.HpdAgentEventModuleManifestAttribute(\"{moduleId}\", typeof(global::HPD.Agent.Serialization.{providerTypeName}), typeof(global::HPD.Agent.Serialization.CoreAgentEventModule))]");
        sb.AppendLine();
        sb.AppendLine("namespace HPD.Agent.Serialization;");
        sb.AppendLine();
        sb.AppendLine("/// <summary>Provides the immutable event fragment generated for this module.</summary>");
        sb.AppendLine($"public static class {providerTypeName}");
        sb.AppendLine("{");
        sb.AppendLine("    /// <summary>Gets the immutable generated event fragment.</summary>");
        sb.AppendLine("    public static AgentEventModuleFragment Fragment { get; } = new()");
        sb.AppendLine("    {");
        sb.AppendLine($"        ModuleId = \"{moduleId}\",");
        sb.AppendLine("        Events = global::System.Array.AsReadOnly<AgentEventDescriptor>([");

        foreach (var evt in events.OrderBy(e => e.FullTypeName))
        {
            sb.AppendLine("            new AgentEventDescriptor");
            sb.AppendLine("            {");
            sb.AppendLine($"                EventType = typeof(global::{evt.FullTypeName}),");
            sb.AppendLine($"                Discriminator = \"{evt.ScreamingSnakeCaseName}\",");
            sb.AppendLine($"                JsonTypeInfo = {contextName}.Default.GetTypeInfo(typeof(global::{evt.FullTypeName}))");
            sb.AppendLine($"                    ?? throw new global::System.InvalidOperationException(\"Missing generated JSON metadata for {evt.FullTypeName}.\"),");
            sb.AppendLine($"                Durability = AgentEventDurability.{evt.Durability},");
            if (evt.ContentPolicy is { } policy)
            {
                var scope = policy.Scope is null ? "null" : $"\"{EscapeString(policy.Scope)}\"";
                sb.AppendLine($"                ContentPolicy = new global::HPD.Agent.Serialization.AgentEventContentPolicy(\"{EscapeString(policy.Kind)}\", \"{EscapeString(policy.ContentType)}\", global::HPD.Agent.ContentSource.{policy.Origin}, {scope}),");
            }
            sb.AppendLine($"                ModuleId = \"{moduleId}\"");
            sb.AppendLine("            },");
        }

        sb.AppendLine("        ])");
        sb.AppendLine("    };");
        sb.AppendLine("}");

        return sb.ToString();
    }

    private static string GetGeneratedProviderTypeName(string moduleId)
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

    private static string EscapeString(string value) =>
        value.Replace("\\", "\\\\").Replace("\"", "\\\"");

    private static void AddManifestProviders(
        IAssemblySymbol assembly,
        List<string> providers)
    {
        foreach (var attribute in assembly.GetAttributes().Where(static value =>
            value.AttributeClass?.Name == "HpdAgentEventModuleManifestAttribute"))
        {
            if (attribute.ConstructorArguments.Length < 2 ||
                attribute.ConstructorArguments[0].Value is not string moduleId ||
                attribute.ConstructorArguments[1].Value is not INamedTypeSymbol providerType)
                continue;
            providers.Add(providerType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat));
            if (attribute.ConstructorArguments.Length < 3)
                continue;
            foreach (var dependency in attribute.ConstructorArguments[2].Values)
            {
                if (dependency.Value is INamedTypeSymbol dependencyType)
                {
                    var dependencyName = dependencyType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
                    if (!providers.Contains(dependencyName, StringComparer.Ordinal))
                        providers.Add(dependencyName);
                }
            }
        }
    }

    private static string GenerateApplicationComposition(
        string assemblyIdentity,
        List<string> providers)
    {
        var providerList = providers.Distinct(StringComparer.Ordinal).OrderBy(static value => value, StringComparer.Ordinal).ToArray();
        var sb = new StringBuilder();
        sb.AppendLine("// <auto-generated/>");
        sb.AppendLine("#nullable enable");
        sb.AppendLine("namespace HPD.Agent.Serialization");
        sb.AppendLine("{");
        sb.AppendLine("internal static class GeneratedAgentEventComposition");
        sb.AppendLine("{");
        sb.AppendLine("    public static AgentEventComposition Composition { get; } = AgentEventComposition.Create([");
        foreach (var provider in providerList)
            sb.AppendLine($"        {provider}.Fragment,");
        sb.AppendLine("    ]);");
        sb.AppendLine("}");
        sb.AppendLine("internal static class GeneratedAgentEventCompositionRegistration");
        sb.AppendLine("{");
        sb.AppendLine("    [global::System.Runtime.CompilerServices.ModuleInitializer]");
        sb.AppendLine("    internal static void Register() => AgentEventCompositionHost.RegisterApplication(");
        sb.AppendLine($"        GeneratedAgentEventComposition.Composition, \"{assemblyIdentity}\");");
        sb.AppendLine("}");
        sb.AppendLine("}");
        sb.AppendLine("namespace Microsoft.Extensions.DependencyInjection");
        sb.AppendLine("{");
        sb.AppendLine("/// <summary>Registers the generated application event composition.</summary>");
        sb.AppendLine("public static class GeneratedAgentEventServiceCollectionExtensions");
        sb.AppendLine("{");
        sb.AppendLine("    /// <summary>Adds the generated immutable application event composition.</summary>");
        sb.AppendLine("    /// <param name=\"services\">The target service collection.</param>");
        sb.AppendLine("    /// <returns>The same service collection.</returns>");
        sb.AppendLine("    public static global::Microsoft.Extensions.DependencyInjection.IServiceCollection AddHpdGeneratedAgentEvents(");
        sb.AppendLine("        this global::Microsoft.Extensions.DependencyInjection.IServiceCollection services)");
        sb.AppendLine("    {");
        sb.AppendLine("        global::System.ArgumentNullException.ThrowIfNull(services);");
        sb.AppendLine("        global::Microsoft.Extensions.DependencyInjection.ServiceCollectionServiceExtensions.AddSingleton(");
        sb.AppendLine("            services, global::HPD.Agent.Serialization.GeneratedAgentEventComposition.Composition);");
        sb.AppendLine("        return services;");
        sb.AppendLine("    }");
        sb.AppendLine("}");
        sb.AppendLine("}");
        return sb.ToString();
    }

    private static string GenerateStructSerializerPartial(List<CustomEventInfo> events)
    {
        var sb = new StringBuilder();
        sb.AppendLine("// <auto-generated/>");
        sb.AppendLine("#nullable enable");
        sb.AppendLine();
        sb.AppendLine("using System.Runtime.CompilerServices;");
        sb.AppendLine();
        sb.AppendLine("namespace HPD.Agent.Serialization;");
        sb.AppendLine();
        sb.AppendLine("internal static class CustomStructEventSerializerRegistration");
        sb.AppendLine("{");
        sb.AppendLine("    /// <summary>");
        sb.AppendLine("    /// Registers all auto-discovered custom struct events when the assembly loads.");
        sb.AppendLine("    /// </summary>");
        sb.AppendLine("#pragma warning disable CA2255");
        sb.AppendLine("    [ModuleInitializer]");
        sb.AppendLine("    internal static void RegisterCustomStructEvents()");
        sb.AppendLine("#pragma warning restore CA2255");
        sb.AppendLine("    {");

        foreach (var evt in events.OrderBy(e => e.FullTypeName))
        {
            sb.AppendLine($"        AgentStructEventSerializer.RegisterEventType(");
            sb.AppendLine($"            typeof(global::{evt.FullTypeName}),");
            sb.AppendLine($"            CustomStructEventTypes.{evt.ScreamingSnakeCaseName});");
        }

        sb.AppendLine("    }");
        sb.AppendLine("}");

        return sb.ToString();
    }

    #endregion

    #region Helper Types

    /// <summary>
    /// Information about a discovered custom event type.
    /// </summary>
    private sealed record CustomEventInfo(
        string Name,
        string Namespace,
        string FullTypeName,
        string ScreamingSnakeCaseName,
        EventRegistrationKind Kind,
        bool IsValid,
        List<Diagnostic> Diagnostics,
        string Durability = "LiveOnly",
        EventContentPolicyInfo? ContentPolicy = null);

    private sealed record EventContentPolicyInfo(
        string Kind,
        string ContentType,
        string Origin,
        string? Scope);

    private enum EventRegistrationKind
    {
        AgentEvent,
        AgentStructEvent
    }

    #endregion
}
