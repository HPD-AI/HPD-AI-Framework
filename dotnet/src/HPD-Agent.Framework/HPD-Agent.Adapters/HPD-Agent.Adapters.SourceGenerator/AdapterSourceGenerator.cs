using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using HPD.Agent.Adapters.SourceGenerator.Diagnostics;
using HPD.Agent.Adapters.SourceGenerator.Generators;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace HPD.Agent.Adapters.SourceGenerator;

// ── Data models ───────────────────────────────────────────────────────────────

internal sealed record AdapterInfo(
    string Name,
    string ClassName,
    string Namespace,
    StreamingInfo? Streaming,
    IReadOnlyList<HandlerInfo> Handlers,
    bool HasPermissionHandler,
    string? SocketTransportTypeFqn,   // null if no [HpdSocketTransport]
    string? SocketConfigProperty,     // e.g. "AppToken"
    bool HasPreDispatch,
    bool HasBodyExtractor);

internal sealed record StreamingInfo(
    string Strategy,
    int DebounceMs);

internal sealed record HandlerInfo(
    string MethodName,
    IReadOnlyList<string> EventTypes,
    string PayloadTypeFqn);

internal sealed record WebhookPayloadInfo(
    string FullyQualifiedName,
    string SimpleName);

// ── Generator entry point ─────────────────────────────────────────────────────

[Generator]
public sealed class AdapterSourceGenerator : IIncrementalGenerator
{
    private const string HpdAdapterAttribute           = "HPD.Agent.Adapters.HpdAdapterAttribute";
    private const string HpdWebhookHandlerAttribute    = "HPD.Agent.Adapters.HpdWebhookHandlerAttribute";
    private const string HpdStreamingAttribute         = "HPD.Agent.Adapters.HpdStreamingAttribute";
    private const string HpdPermissionHandlerAttribute = "HPD.Agent.Adapters.HpdPermissionHandlerAttribute";
    private const string WebhookPayloadAttribute       = "HPD.Agent.Adapters.WebhookPayloadAttribute";
    private const string HpdSocketTransportAttribute   = "HPD.Agent.Adapters.HpdSocketTransportAttribute";
    private const string AdapterWebSocketServiceFqn    = "HPD.Agent.Adapters.AdapterWebSocketService";
    private const string HpdPreDispatchAttribute       = "HPD.Agent.Adapters.HpdPreDispatchAttribute";
    private const string HpdBodyExtractorAttribute     = "HPD.Agent.Adapters.HpdBodyExtractorAttribute";

    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        // ── Pipeline: [HpdAdapter] classes ───────────────────────────────────
        var adapterClasses = context.SyntaxProvider
            .ForAttributeWithMetadataName(
                HpdAdapterAttribute,
                predicate: static (node, _) => node is ClassDeclarationSyntax,
                transform: static (ctx, _) => (ClassDeclarationSyntax)ctx.TargetNode)
            .Collect();

        // ── Pipeline: [WebhookPayload] records ────────────────────────────────
        var payloadRecords = context.SyntaxProvider
            .ForAttributeWithMetadataName(
                WebhookPayloadAttribute,
                predicate: static (node, _) => node is RecordDeclarationSyntax,
                transform: static (ctx, _) => ctx.TargetSymbol as INamedTypeSymbol)
            .Where(static s => s is not null)
            .Collect();

        // ── Combine and emit ──────────────────────────────────────────────────
        var combined = adapterClasses
            .Combine(payloadRecords)
            .Combine(context.CompilationProvider);

        context.RegisterSourceOutput(combined, static (ctx, tuple) =>
        {
            var ((adapterNodes, payloadSymbols), compilation) = tuple;
            Execute(ctx, adapterNodes, payloadSymbols!, compilation);
        });
    }

    private static void Execute(
        SourceProductionContext context,
        ImmutableArray<ClassDeclarationSyntax> adapterNodes,
        ImmutableArray<INamedTypeSymbol?> payloadSymbols,
        Compilation compilation)
    {
        // ── Resolve adapter infos ─────────────────────────────────────────────
        var adapters   = new List<AdapterInfo>();
        var seenNames  = new Dictionary<string, string>(); // name → first class

        foreach (var node in adapterNodes)
        {
            var model  = compilation.GetSemanticModel(node.SyntaxTree);
            var symbol = model.GetDeclaredSymbol(node) as INamedTypeSymbol;
            if (symbol is null) continue;

            var info = ResolveAdapter(context, node, symbol);
            if (info is null) continue;

            // HPD-A005: name collision
            if (seenNames.TryGetValue(info.Name, out var existing))
            {
                context.ReportDiagnostic(Diagnostic.Create(
                    AdapterDiagnostics.DuplicateAdapterName,
                    node.GetLocation(),
                    info.Name, existing, symbol.Name));
                continue;
            }

            seenNames[info.Name] = symbol.Name;
            adapters.Add(info);
        }

        // ── Resolve payload infos ─────────────────────────────────────────────
        var payloads = payloadSymbols
            .Where(s => s is not null)
            .Select(s => new WebhookPayloadInfo(
                FullyQualifiedName: s!.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                SimpleName: s.Name))
            .ToList();

        // ── Emit ──────────────────────────────────────────────────────────────
        RegistrationGenerator.Generate(context, adapters);
        DispatchGenerator.Generate(context, adapters);
        RegistryGenerator.Generate(context, adapters);
        JsonContextGenerator.Generate(context, payloads);
    }

    private static AdapterInfo? ResolveAdapter(
        SourceProductionContext context,
        ClassDeclarationSyntax node,
        INamedTypeSymbol symbol)
    {
        // HPD-A001: must be public
        if (symbol.DeclaredAccessibility != Accessibility.Public)
        {
            context.ReportDiagnostic(Diagnostic.Create(
                AdapterDiagnostics.AdapterNotPublic,
                node.GetLocation(),
                symbol.Name));
            return null;
        }

        // Read [HpdAdapter("name")]
        var adapterAttr = symbol.GetAttributes()
            .FirstOrDefault(a => a.AttributeClass?.ToDisplayString() == HpdAdapterAttribute);
        if (adapterAttr is null) return null;

        var adapterName = adapterAttr.ConstructorArguments.FirstOrDefault().Value as string ?? symbol.Name.ToLower();

        // Read [HpdStreaming] — HPD-A003: must not appear more than once
        var streamingAttrs = symbol.GetAttributes()
            .Where(a => a.AttributeClass?.ToDisplayString() == HpdStreamingAttribute)
            .ToList();
        if (streamingAttrs.Count > 1)
        {
            context.ReportDiagnostic(Diagnostic.Create(
                AdapterDiagnostics.DuplicateStreaming,
                node.GetLocation(),
                symbol.Name));
        }

        StreamingInfo? streaming = null;
        if (streamingAttrs.Count >= 1)
        {
            var sa       = streamingAttrs[0];
            var strategy = sa.ConstructorArguments.FirstOrDefault().Value?.ToString() ?? "PostAndEdit";
            var debounce = (int)(sa.NamedArguments.FirstOrDefault(n => n.Key == "DebounceMs").Value.Value ?? 500);
            streaming    = new StreamingInfo(strategy, debounce);
        }

        // Read [HpdWebhookHandler] methods — HPD-A002: must be private or internal
        var handlers           = new List<HandlerInfo>();
        var permissionHandlers = 0;
        var hasPreDispatch     = false;
        var hasBodyExtractor   = false;

        foreach (var member in symbol.GetMembers().OfType<IMethodSymbol>())
        {
            // Permission handler count — HPD-A004
            var hasPermAttr = member.GetAttributes()
                .Any(a => a.AttributeClass?.ToDisplayString() == HpdPermissionHandlerAttribute);
            if (hasPermAttr) permissionHandlers++;

            // Detect [HpdPreDispatch] — HPDA009: must have exactly 2 parameters
            if (member.GetAttributes().Any(a => a.AttributeClass?.ToDisplayString() == HpdPreDispatchAttribute))
            {
                if (member.Parameters.Length != 2)
                {
                    var loc = member.Locations.FirstOrDefault() ?? node.GetLocation();
                    context.ReportDiagnostic(Diagnostic.Create(
                        AdapterDiagnostics.PreDispatchWrongSignature, loc, member.Name));
                }
                else
                {
                    hasPreDispatch = true;
                }
            }

            // Detect [HpdBodyExtractor] — HPDA010: must have exactly 2 parameters
            if (member.GetAttributes().Any(a => a.AttributeClass?.ToDisplayString() == HpdBodyExtractorAttribute))
            {
                if (member.Parameters.Length != 2)
                {
                    var loc = member.Locations.FirstOrDefault() ?? node.GetLocation();
                    context.ReportDiagnostic(Diagnostic.Create(
                        AdapterDiagnostics.BodyExtractorWrongSignature, loc, member.Name));
                }
                else
                {
                    hasBodyExtractor = true;
                }
            }

            var handlerAttrs = member.GetAttributes()
                .Where(a => a.AttributeClass?.ToDisplayString() == HpdWebhookHandlerAttribute)
                .ToList();
            if (handlerAttrs.Count == 0) continue;

            // HPD-A002
            if (member.DeclaredAccessibility != Accessibility.Private &&
                member.DeclaredAccessibility != Accessibility.Internal)
            {
                var loc = member.Locations.FirstOrDefault() ?? node.GetLocation();
                context.ReportDiagnostic(Diagnostic.Create(
                    AdapterDiagnostics.HandlerNotPrivate,
                    loc,
                    member.Name));
                continue;
            }

            var eventTypes = handlerAttrs
                .Select(a => a.ConstructorArguments.FirstOrDefault().Value as string ?? "")
                .Where(s => s.Length > 0)
                .ToList();

            // Second parameter (after HttpContext) is the payload type to deserialize.
            var payloadParam = member.Parameters.Length >= 2 ? member.Parameters[1] : null;
            var payloadFqn   = payloadParam?.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)
                               ?? "global::System.Text.Json.JsonElement";

            handlers.Add(new HandlerInfo(member.Name, eventTypes, payloadFqn));
        }

        // HPD-A004
        if (permissionHandlers > 1)
        {
            context.ReportDiagnostic(Diagnostic.Create(
                AdapterDiagnostics.DuplicatePermissionHandler,
                node.GetLocation(),
                symbol.Name));
        }

        // Read [HpdSocketTransport] — HPDA008: service type must extend AdapterWebSocketService
        string? socketTransportTypeFqn = null;
        string? socketConfigProperty   = null;
        var socketAttr = symbol.GetAttributes()
            .FirstOrDefault(a => a.AttributeClass?.ToDisplayString() == HpdSocketTransportAttribute);
        if (socketAttr is not null)
        {
            var serviceTypeArg = socketAttr.ConstructorArguments.FirstOrDefault().Value as INamedTypeSymbol;
            if (serviceTypeArg is not null)
            {
                // Validate the type extends AdapterWebSocketService
                var isValid = false;
                var current = serviceTypeArg.BaseType;
                while (current is not null)
                {
                    if (current.ToDisplayString() == AdapterWebSocketServiceFqn) { isValid = true; break; }
                    current = current.BaseType;
                }
                if (!isValid)
                {
                    context.ReportDiagnostic(Diagnostic.Create(
                        AdapterDiagnostics.SocketTransportInvalidType,
                        node.GetLocation(),
                        serviceTypeArg.Name));
                }
                else
                {
                    socketTransportTypeFqn = serviceTypeArg.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
                    socketConfigProperty   = socketAttr.NamedArguments
                        .FirstOrDefault(n => n.Key == "ConfigProperty").Value.Value as string ?? "";
                }
            }
        }

        return new AdapterInfo(
            Name:                   adapterName,
            ClassName:              symbol.Name,
            Namespace:              symbol.ContainingNamespace.ToDisplayString(),
            Streaming:              streaming,
            Handlers:               handlers,
            HasPermissionHandler:   permissionHandlers >= 1,
            SocketTransportTypeFqn: socketTransportTypeFqn,
            SocketConfigProperty:   socketConfigProperty,
            HasPreDispatch:         hasPreDispatch,
            HasBodyExtractor:       hasBodyExtractor);
    }
}
