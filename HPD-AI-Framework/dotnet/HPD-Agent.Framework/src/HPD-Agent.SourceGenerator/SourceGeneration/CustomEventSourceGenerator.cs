// Copyright 2026 Einstein Essibu
// SPDX-License-Identifier: AGPL-3.0-only

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
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
            customEvents.Collect(),
            (spc, events) => GenerateEventRegistrations(spc, events!));
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

        // Check if any base type references one of the supported event contracts.
        return baseList.Types.Any(t =>
            t.Type.ToString().Contains("AgentEvent") ||
            t.Type.ToString().Contains("AgentStructEvent"));
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

        // Skip framework events (HPD.Agent namespace)
        var namespaceName = typeSymbol.ContainingNamespace?.ToDisplayString() ?? "";
        if (namespaceName.StartsWith("HPD.Agent") || namespaceName.StartsWith("HPD.Agent"))
            return null;

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
        var discriminator = customDiscriminator ?? ToScreamingSnakeCase(typeSymbol.Name);

        return new CustomEventInfo(
            Name: typeSymbol.Name,
            Namespace: namespaceName,
            FullTypeName: typeSymbol.ToDisplayString(),
            ScreamingSnakeCaseName: discriminator,
            Kind: eventKind.Value,
            IsValid: true,
            Diagnostics: diagnostics);
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
        ImmutableArray<CustomEventInfo?> events)
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

        if (validEvents.Count == 0)
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

        if (validAgentEvents.Count > 0)
        {
            context.AddSource("CustomEventTypes.g.cs",
                GenerateEventTypesPartial(validAgentEvents));

            context.AddSource("CustomEventSerializer.g.cs",
                GenerateSerializerPartial(validAgentEvents));
        }

        if (validStructEvents.Count > 0)
        {
            context.AddSource("CustomStructEventTypes.g.cs",
                GenerateStructEventTypesPartial(validStructEvents));

            context.AddSource("CustomStructEventSerializer.g.cs",
                GenerateStructSerializerPartial(validStructEvents));
        }
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
        sb.AppendLine("namespace HPD.Agent.Serialization;");
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

        return sb.ToString();
    }

    /// <summary>
    /// Generates module initializer registration for custom events.
    /// </summary>
    private static string GenerateSerializerPartial(List<CustomEventInfo> events)
    {
        var sb = new StringBuilder();
        sb.AppendLine("// <auto-generated/>");
        sb.AppendLine("#nullable enable");
        sb.AppendLine();
        sb.AppendLine("using System.Runtime.CompilerServices;");
        sb.AppendLine();
        sb.AppendLine("namespace HPD.Agent.Serialization;");
        sb.AppendLine();
        sb.AppendLine("internal static class CustomEventSerializerRegistration");
        sb.AppendLine("{");
        sb.AppendLine("    /// <summary>");
        sb.AppendLine("    /// Registers all auto-discovered custom events when the assembly loads.");
        sb.AppendLine("    /// </summary>");
        sb.AppendLine("#pragma warning disable CA2255");
        sb.AppendLine("    [ModuleInitializer]");
        sb.AppendLine("    internal static void RegisterCustomEvents()");
        sb.AppendLine("#pragma warning restore CA2255");
        sb.AppendLine("    {");

        foreach (var evt in events.OrderBy(e => e.FullTypeName))
        {
            sb.AppendLine($"        AgentEventSerializer.RegisterEventType(");
            sb.AppendLine($"            typeof(global::{evt.FullTypeName}),");
            sb.AppendLine($"            CustomEventTypes.{evt.ScreamingSnakeCaseName});");
        }

        sb.AppendLine("    }");
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
        List<Diagnostic> Diagnostics);

    private enum EventRegistrationKind
    {
        AgentEvent,
        AgentStructEvent
    }

    #endregion
}
