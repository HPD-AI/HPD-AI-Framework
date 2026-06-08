using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using HPD.Agent.Bots.SourceGenerator.Diagnostics;
using HPD.Agent.Bots.SourceGenerator.Generators;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace HPD.Agent.Bots.SourceGenerator;

// ── Data models ───────────────────────────────────────────────────────────────

internal sealed record BotInfo(
    string Name,
    string ClassName,
    string Namespace,
    StreamingInfo? Streaming,
    IReadOnlyList<string> WebhookMethods,
    IReadOnlyList<HandlerInfo> Handlers,
    bool HasPermissionHandler,
    string? SocketTransportTypeFqn,   // null if no [HpdSocketTransport]
    string? SocketConfigProperty,     // e.g. "AppToken"
    string? PreDispatchMethodName,
    string? BodyExtractorMethodName);

internal sealed record StreamingInfo(
    string Strategy,
    int DebounceMs);

internal sealed record HandlerInfo(
    string MethodName,
    IReadOnlyList<string> EventTypes,
    string PayloadTypeFqn,
    string PayloadJsonTypeInfoProperty);

internal sealed record HpdBotPayloadInfo(
    string FullyQualifiedName,
    string SimpleName);

internal sealed record ThreadIdInfo(
    string RecordName,
    string Namespace,
    string Format,
    IReadOnlyList<ThreadIdPropertyInfo> Properties,
    IReadOnlyList<string> Slots);

internal sealed record ThreadIdPropertyInfo(
    string Name,
    string TypeFqn,
    bool HasExplicitDefaultValue,
    string? ExplicitDefaultValue);

// ── Generator entry point ─────────────────────────────────────────────────────

[Generator]
public sealed class BotSourceGenerator : IIncrementalGenerator
{
    private const string HpdBotAttribute           = "HPD.Agent.Bots.HpdBotAttribute";
    private const string HpdBotEventHandlerAttribute    = "HPD.Agent.Bots.HpdBotEventHandlerAttribute";
    private const string HpdStreamingAttribute         = "HPD.Agent.Bots.HpdStreamingAttribute";
    private const string HpdHttpMethodsAttribute    = "HPD.Agent.Bots.HpdHttpMethodsAttribute";
    private const string HpdPermissionHandlerAttribute = "HPD.Agent.Bots.HpdPermissionHandlerAttribute";
    private const string HpdBotPayloadAttribute       = "HPD.Agent.Bots.HpdBotPayloadAttribute";
    private const string HpdSocketTransportAttribute   = "HPD.Agent.Bots.HpdSocketTransportAttribute";
    private const string BotWebSocketServiceFqn    = "HPD.Agent.Bots.BotWebSocketService";
    private const string HpdBotPreDispatchAttribute       = "HPD.Agent.Bots.HpdBotPreDispatchAttribute";
    private const string HpdBotEnvelopeExtractorAttribute     = "HPD.Agent.Bots.HpdBotEnvelopeExtractorAttribute";
    private const string ThreadIdAttribute             = "HPD.Agent.Bots.ThreadIdAttribute";

    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        // ── Pipeline: [HpdBot] classes ───────────────────────────────────
        var adapterClasses = context.SyntaxProvider
            .ForAttributeWithMetadataName(
                HpdBotAttribute,
                predicate: static (node, _) => node is ClassDeclarationSyntax,
                transform: static (ctx, _) => (ClassDeclarationSyntax)ctx.TargetNode)
            .Collect();

        // ── Pipeline: [HpdBotPayload] records ────────────────────────────────
        var payloadRecords = context.SyntaxProvider
            .ForAttributeWithMetadataName(
                HpdBotPayloadAttribute,
                predicate: static (node, _) => node is RecordDeclarationSyntax,
                transform: static (ctx, _) => ctx.TargetSymbol as INamedTypeSymbol)
            .Where(static s => s is not null)
            .Collect();

        // ── Pipeline: [ThreadId] records ──────────────────────────────────────
        var threadIdRecords = context.SyntaxProvider
            .ForAttributeWithMetadataName(
                ThreadIdAttribute,
                predicate: static (node, _) => node is RecordDeclarationSyntax,
                transform: static (ctx, _) => (RecordDeclarationSyntax)ctx.TargetNode)
            .Collect();

        // ── Combine and emit ──────────────────────────────────────────────────
        var combined = adapterClasses
            .Combine(payloadRecords)
            .Combine(threadIdRecords)
            .Combine(context.CompilationProvider);

        context.RegisterSourceOutput(combined, static (ctx, tuple) =>
        {
            var (((adapterNodes, payloadSymbols), threadIdNodes), compilation) = tuple;
            Execute(ctx, adapterNodes, payloadSymbols!, threadIdNodes, compilation);
        });
    }

    private static void Execute(
        SourceProductionContext context,
        ImmutableArray<ClassDeclarationSyntax> adapterNodes,
        ImmutableArray<INamedTypeSymbol?> payloadSymbols,
        ImmutableArray<RecordDeclarationSyntax> threadIdNodes,
        Compilation compilation)
    {
        // ── Resolve adapter infos ─────────────────────────────────────────────
        var adapters   = new List<BotInfo>();
        var seenNames  = new Dictionary<string, string>(); // name → first class

        foreach (var node in adapterNodes)
        {
            var model  = compilation.GetSemanticModel(node.SyntaxTree);
            var symbol = model.GetDeclaredSymbol(node) as INamedTypeSymbol;
            if (symbol is null) continue;

            var info = ResolveBot(context, node, symbol);
            if (info is null) continue;

            // HPD-A005: name collision
            if (seenNames.TryGetValue(info.Name, out var existing))
            {
                context.ReportDiagnostic(Diagnostic.Create(
                    BotDiagnostics.DuplicateBotName,
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
            .Select(s => new HpdBotPayloadInfo(
                FullyQualifiedName: s!.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                SimpleName: s.Name))
            .ToList();

        // ── Resolve ThreadId infos ────────────────────────────────────────────
        var threadIds = new List<ThreadIdInfo>();
        foreach (var node in threadIdNodes)
        {
            var model = compilation.GetSemanticModel(node.SyntaxTree);
            var symbol = model.GetDeclaredSymbol(node) as INamedTypeSymbol;
            if (symbol is null) continue;

            var info = ResolveThreadId(context, node, symbol);
            if (info is not null)
                threadIds.Add(info);
        }

        // ── Emit ──────────────────────────────────────────────────────────────
        RegistrationGenerator.Generate(context, adapters);
        DispatchGenerator.Generate(context, adapters);
        RegistryGenerator.Generate(context, adapters);
        JsonContextGenerator.Generate(context, payloads);
        ThreadIdGenerator.Generate(context, threadIds);
    }

    private static BotInfo? ResolveBot(
        SourceProductionContext context,
        ClassDeclarationSyntax node,
        INamedTypeSymbol symbol)
    {
        // HPD-A001: must be public
        if (symbol.DeclaredAccessibility != Accessibility.Public)
        {
            context.ReportDiagnostic(Diagnostic.Create(
                BotDiagnostics.BotNotPublic,
                node.GetLocation(),
                symbol.Name));
            return null;
        }

        // Read [HpdBot("name")]
        var adapterAttr = symbol.GetAttributes()
            .FirstOrDefault(a => a.AttributeClass?.ToDisplayString() == HpdBotAttribute);
        if (adapterAttr is null) return null;

        var adapterName = adapterAttr.ConstructorArguments.FirstOrDefault().Value as string ?? symbol.Name.ToLower();

        // Read [HpdStreaming] — HPD-A003: must not appear more than once
        var streamingAttrs = symbol.GetAttributes()
            .Where(a => a.AttributeClass?.ToDisplayString() == HpdStreamingAttribute)
            .ToList();
        if (streamingAttrs.Count > 1)
        {
            context.ReportDiagnostic(Diagnostic.Create(
                BotDiagnostics.DuplicateStreaming,
                node.GetLocation(),
                symbol.Name));
        }

        StreamingInfo? streaming = null;
        if (streamingAttrs.Count >= 1)
        {
            var sa       = streamingAttrs[0];
            var strategy = ResolveStreamingStrategyName(sa.ConstructorArguments.FirstOrDefault().Value);
            var debounce = (int)(sa.NamedArguments.FirstOrDefault(n => n.Key == "DebounceMs").Value.Value ?? 500);
            streaming    = new StreamingInfo(strategy, debounce);
        }

        var webhookMethods = ResolveWebhookMethods(context, node, symbol);

        // Read [HpdBotEventHandler] methods — HPD-A002: must be private or internal
        var handlers                = new List<HandlerInfo>();
        var permissionHandlers      = 0;
        var preDispatchMethods      = new List<IMethodSymbol>();
        var bodyExtractorMethods    = new List<IMethodSymbol>();
        string? preDispatchMethod   = null;
        string? bodyExtractorMethod = null;

        foreach (var member in symbol.GetMembers().OfType<IMethodSymbol>())
        {
            // Permission handler count — HPD-A004
            var hasPermAttr = member.GetAttributes()
                .Any(a => a.AttributeClass?.ToDisplayString() == HpdPermissionHandlerAttribute);
            if (hasPermAttr) permissionHandlers++;

            // Detect [HpdBotPreDispatch] — HPDA009 validates the complete hook contract.
            if (member.GetAttributes().Any(a => a.AttributeClass?.ToDisplayString() == HpdBotPreDispatchAttribute))
            {
                preDispatchMethods.Add(member);
            }

            // Detect [HpdBotEnvelopeExtractor] — HPDA010 validates the complete hook contract.
            if (member.GetAttributes().Any(a => a.AttributeClass?.ToDisplayString() == HpdBotEnvelopeExtractorAttribute))
            {
                bodyExtractorMethods.Add(member);
            }

            var handlerAttrs = member.GetAttributes()
                .Where(a => a.AttributeClass?.ToDisplayString() == HpdBotEventHandlerAttribute)
                .ToList();
            if (handlerAttrs.Count == 0) continue;

            // HPD-A002
            if (member.DeclaredAccessibility != Accessibility.Private &&
                member.DeclaredAccessibility != Accessibility.Internal)
            {
                var loc = member.Locations.FirstOrDefault() ?? node.GetLocation();
                context.ReportDiagnostic(Diagnostic.Create(
                    BotDiagnostics.HandlerNotPrivate,
                    loc,
                    member.Name));
                continue;
            }

            var eventTypes = handlerAttrs
                .Select(a => a.ConstructorArguments.FirstOrDefault().Value as string ?? "")
                .Where(s => s.Length > 0)
                .ToList();

            // Second parameter (after BotRequestContext) is the payload type to deserialize.
            var payloadParam = member.Parameters.Length >= 2 ? member.Parameters[1] : null;
            var payloadFqn   = payloadParam?.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)
                               ?? "global::System.Text.Json.JsonElement";
            var payloadJsonTypeInfoProperty = payloadParam?.Type is { } payloadType
                ? GetJsonTypeInfoPropertyName(payloadType)
                : "JsonElement";

            handlers.Add(new HandlerInfo(member.Name, eventTypes, payloadFqn, payloadJsonTypeInfoProperty));
        }

        // HPD-A004
        if (permissionHandlers > 1)
        {
            context.ReportDiagnostic(Diagnostic.Create(
                BotDiagnostics.DuplicatePermissionHandler,
                node.GetLocation(),
                symbol.Name));
        }

        if (preDispatchMethods.Count > 1)
        {
            foreach (var method in preDispatchMethods)
            {
                ReportPreDispatchDiagnostic(context, node, method);
            }
        }
        else if (preDispatchMethods.Count == 1)
        {
            var method = preDispatchMethods[0];
            if (IsValidPreDispatchHook(method))
                preDispatchMethod = method.Name;
            else
                ReportPreDispatchDiagnostic(context, node, method);
        }

        if (bodyExtractorMethods.Count > 1)
        {
            foreach (var method in bodyExtractorMethods)
            {
                ReportBodyExtractorDiagnostic(context, node, method);
            }
        }
        else if (bodyExtractorMethods.Count == 1)
        {
            var method = bodyExtractorMethods[0];
            if (IsValidBodyExtractorHook(method))
                bodyExtractorMethod = method.Name;
            else
                ReportBodyExtractorDiagnostic(context, node, method);
        }

        // Read [HpdSocketTransport] — HPDA008: service type must extend BotWebSocketService
        string? socketTransportTypeFqn = null;
        string? socketConfigProperty   = null;
        var socketAttr = symbol.GetAttributes()
            .FirstOrDefault(a => a.AttributeClass?.ToDisplayString() == HpdSocketTransportAttribute);
        if (socketAttr is not null)
        {
            var serviceTypeArg = socketAttr.ConstructorArguments.FirstOrDefault().Value as INamedTypeSymbol;
            if (serviceTypeArg is not null)
            {
                // Validate the type extends BotWebSocketService
                var isValid = false;
                var current = serviceTypeArg.BaseType;
                while (current is not null)
                {
                    if (current.ToDisplayString() == BotWebSocketServiceFqn) { isValid = true; break; }
                    current = current.BaseType;
                }
                if (!isValid)
                {
                    context.ReportDiagnostic(Diagnostic.Create(
                        BotDiagnostics.SocketTransportInvalidType,
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

        return new BotInfo(
            Name:                   adapterName,
            ClassName:              symbol.Name,
            Namespace:              symbol.ContainingNamespace.ToDisplayString(),
            Streaming:              streaming,
            WebhookMethods:         webhookMethods,
            Handlers:               handlers,
            HasPermissionHandler:   permissionHandlers >= 1,
            SocketTransportTypeFqn: socketTransportTypeFqn,
            SocketConfigProperty:   socketConfigProperty,
            PreDispatchMethodName:  preDispatchMethod,
            BodyExtractorMethodName: bodyExtractorMethod);
    }

    private static IReadOnlyList<string> ResolveWebhookMethods(
        SourceProductionContext context,
        ClassDeclarationSyntax node,
        INamedTypeSymbol symbol)
    {
        var attr = symbol.GetAttributes()
            .FirstOrDefault(a => a.AttributeClass?.ToDisplayString() == HpdHttpMethodsAttribute);
        if (attr is null)
            return Array.Empty<string>();

        var methods = new List<string>();
        foreach (var arg in attr.ConstructorArguments)
        {
            if (arg.Kind == TypedConstantKind.Array)
            {
                foreach (var value in arg.Values)
                    AddWebhookMethod(context, node, symbol.Name, value.Value as string, methods);
            }
            else
            {
                AddWebhookMethod(context, node, symbol.Name, arg.Value as string, methods);
            }
        }

        if (methods.Count == 0)
        {
            context.ReportDiagnostic(Diagnostic.Create(
                BotDiagnostics.InvalidWebhookMethods,
                node.GetLocation(),
                symbol.Name));
        }

        return methods;
    }

    private static void AddWebhookMethod(
        SourceProductionContext context,
        ClassDeclarationSyntax node,
        string botClassName,
        string? method,
        List<string> methods)
    {
        if (string.IsNullOrWhiteSpace(method))
        {
            context.ReportDiagnostic(Diagnostic.Create(
                BotDiagnostics.InvalidWebhookMethods,
                node.GetLocation(),
                botClassName));
            return;
        }

        var normalized = method.Trim().ToUpperInvariant();
        if (!methods.Contains(normalized, StringComparer.Ordinal))
            methods.Add(normalized);
    }

    private static bool IsValidPreDispatchHook(IMethodSymbol method)
    {
        return IsPrivateOrInternal(method)
               && method.Parameters.Length == 2
               && IsBotRequestContext(method.Parameters[0].Type)
               && IsByteArray(method.Parameters[1].Type)
               && IsTaskOfBotAdapterResponse(method.ReturnType);
    }

    private static bool IsValidBodyExtractorHook(IMethodSymbol method)
    {
        return IsPrivateOrInternal(method)
               && method.Parameters.Length == 2
               && IsBotRequestContext(method.Parameters[0].Type)
               && IsByteArray(method.Parameters[1].Type)
               && IsStringByteArrayTuple(method.ReturnType);
    }

    private static bool IsPrivateOrInternal(IMethodSymbol method) =>
        method.DeclaredAccessibility == Accessibility.Private ||
        method.DeclaredAccessibility == Accessibility.Internal;

    private static bool IsBotRequestContext(ITypeSymbol type) =>
        type.ToDisplayString() == "HPD.Agent.Bots.BotRequestContext";

    private static bool IsByteArray(ITypeSymbol type) =>
        type is IArrayTypeSymbol { ElementType.SpecialType: SpecialType.System_Byte, Rank: 1 };

    private static bool IsTaskOfBotAdapterResponse(ITypeSymbol type)
    {
        if (type is not INamedTypeSymbol named || named.TypeArguments.Length != 1)
            return false;

        return named.Name == "Task" &&
               named.ContainingNamespace.ToDisplayString() == "System.Threading.Tasks" &&
               IsBotAdapterResponse(named.TypeArguments[0]);
    }

    private static bool IsBotAdapterResponse(ITypeSymbol type) =>
        type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)
            .TrimEnd('?') == "global::HPD.Agent.Bots.BotAdapterResponse";

    private static bool IsStringByteArrayTuple(ITypeSymbol type)
    {
        if (type is not INamedTypeSymbol named || !named.IsTupleType || named.TupleElements.Length != 2)
            return false;

        return named.TupleElements[0].Type.SpecialType == SpecialType.System_String &&
               IsByteArray(named.TupleElements[1].Type);
    }

    private static string GetJsonTypeInfoPropertyName(ITypeSymbol type)
    {
        if (type is IArrayTypeSymbol arrayType)
        {
            return arrayType.ElementType.SpecialType == SpecialType.System_Byte
                ? "ByteArray"
                : "ObjectArray";
        }

        if (type is not INamedTypeSymbol namedType)
        {
            return "JsonElement";
        }

        if (namedType.ContainingNamespace.ToDisplayString() == "System.Text.Json" &&
            namedType.Name == "JsonElement")
        {
            return "JsonElement";
        }

        return namedType.Name;
    }

    private static ThreadIdInfo? ResolveThreadId(
        SourceProductionContext context,
        RecordDeclarationSyntax node,
        INamedTypeSymbol symbol)
    {
        var attr = symbol.GetAttributes()
            .FirstOrDefault(a => a.AttributeClass?.ToDisplayString() == ThreadIdAttribute);
        if (attr is null) return null;

        var format = attr.ConstructorArguments.FirstOrDefault().Value as string ?? "";
        if (string.IsNullOrWhiteSpace(format)) return null;

        var slots = ThreadIdGenerator.GetSlots(format).ToList();
        var properties = new List<ThreadIdPropertyInfo>();
        foreach (var slot in slots)
        {
            var property = symbol.GetMembers()
                .OfType<IPropertySymbol>()
                .FirstOrDefault(p => !p.IsStatic &&
                                     p.DeclaredAccessibility == Accessibility.Public &&
                                     string.Equals(p.Name, slot, StringComparison.OrdinalIgnoreCase));

            if (property is null ||
                !TryGetPrimaryConstructorParameter(symbol, property.Name, out var parameter))
            {
                context.ReportDiagnostic(Diagnostic.Create(
                    BotDiagnostics.ThreadIdSlotMissing,
                    node.GetLocation(),
                    slot, format, symbol.Name));
                return null;
            }

            properties.Add(CreateThreadIdPropertyInfo(property, parameter));
        }

        return new ThreadIdInfo(
            RecordName: symbol.Name,
            Namespace: symbol.ContainingNamespace.ToDisplayString(),
            Format: format,
            Properties: properties,
            Slots: slots);
    }

    private static ThreadIdPropertyInfo CreateThreadIdPropertyInfo(
        IPropertySymbol property,
        IParameterSymbol parameter)
    {
        var hasDefault = parameter.HasExplicitDefaultValue;

        return new ThreadIdPropertyInfo(
            Name: property.Name,
            TypeFqn: property.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
            HasExplicitDefaultValue: hasDefault,
            ExplicitDefaultValue: hasDefault ? FormatDefaultValue(parameter.ExplicitDefaultValue) : null);
    }

    private static bool TryGetPrimaryConstructorParameter(
        INamedTypeSymbol symbol,
        string name,
        out IParameterSymbol parameter)
    {
        parameter = null!;
        var constructor = symbol.InstanceConstructors
            .FirstOrDefault(c => c.Parameters.Any(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase)));
        if (constructor is null) return false;

        var match = constructor.Parameters
            .FirstOrDefault(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase));
        if (match is null) return false;

        parameter = match;
        return true;
    }

    private static string? FormatDefaultValue(object? value)
        => value switch
        {
            null => "null",
            string s => "@\"" + s.Replace("\"", "\"\"") + "\"",
            bool b => b ? "true" : "false",
            char c => "'" + c.ToString().Replace("\\", "\\\\").Replace("'", "\\'") + "'",
            _ => Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture),
        };

    private static string ResolveStreamingStrategyName(object? value)
        => value switch
        {
            0 => "PostAndEdit",
            1 => "BufferAndPost",
            2 => "Native",
            string name => name,
            { } other => other.ToString() ?? "PostAndEdit",
            _ => "PostAndEdit",
        };

    private static void ReportPreDispatchDiagnostic(
        SourceProductionContext context,
        ClassDeclarationSyntax node,
        IMethodSymbol method)
    {
        var loc = method.Locations.FirstOrDefault() ?? node.GetLocation();
        context.ReportDiagnostic(Diagnostic.Create(
            BotDiagnostics.PreDispatchWrongSignature, loc, method.Name));
    }

    private static void ReportBodyExtractorDiagnostic(
        SourceProductionContext context,
        ClassDeclarationSyntax node,
        IMethodSymbol method)
    {
        var loc = method.Locations.FirstOrDefault() ?? node.GetLocation();
        context.ReportDiagnostic(Diagnostic.Create(
            BotDiagnostics.BodyExtractorWrongSignature, loc, method.Name));
    }
}
