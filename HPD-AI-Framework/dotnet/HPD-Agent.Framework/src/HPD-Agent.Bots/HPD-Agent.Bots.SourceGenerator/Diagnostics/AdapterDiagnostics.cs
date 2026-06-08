using Microsoft.CodeAnalysis;

namespace HPD.Agent.Bots.SourceGenerator.Diagnostics;

internal static class BotDiagnostics
{
    private const string Category = "HPD.Bots";

    /// <summary>HPDA001: [HpdBot] class must be public.</summary>
    public static readonly DiagnosticDescriptor BotNotPublic = new(
        id:                 "HPDA001",
        title:              "[HpdBot] class must be public",
        messageFormat:      "Bot class '{0}' decorated with [HpdBot] must be public",
        category:           Category,
        defaultSeverity:    DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    /// <summary>HPDA002: [HpdBotEventHandler] method must be private or internal.</summary>
    public static readonly DiagnosticDescriptor HandlerNotPrivate = new(
        id:                 "HPDA002",
        title:              "[HpdBotEventHandler] method must be private or internal",
        messageFormat:      "Bot event handler '{0}' must be private or internal — the generator produces the public dispatch entry point",
        category:           Category,
        defaultSeverity:    DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    /// <summary>HPDA003: [HpdStreaming] declared more than once on the same class.</summary>
    public static readonly DiagnosticDescriptor DuplicateStreaming = new(
        id:                 "HPDA003",
        title:              "[HpdStreaming] declared more than once",
        messageFormat:      "Bot class '{0}' has multiple [HpdStreaming] attributes — only one is allowed",
        category:           Category,
        defaultSeverity:    DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    /// <summary>HPDA004: [HpdPermissionHandler] declared more than once on the same class.</summary>
    public static readonly DiagnosticDescriptor DuplicatePermissionHandler = new(
        id:                 "HPDA004",
        title:              "[HpdPermissionHandler] declared more than once",
        messageFormat:      "Bot class '{0}' has multiple [HpdPermissionHandler] methods — only one is allowed",
        category:           Category,
        defaultSeverity:    DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    /// <summary>HPDA005: [HpdBot] name collides with another adapter in the same assembly.</summary>
    public static readonly DiagnosticDescriptor DuplicateBotName = new(
        id:                 "HPDA005",
        title:              "[HpdBot] name collision",
        messageFormat:      "Bot name '{0}' is used by both '{1}' and '{2}' in the same assembly",
        category:           Category,
        defaultSeverity:    DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    /// <summary>HPDA006: [HpdBotPayload] type must be a record.</summary>
    public static readonly DiagnosticDescriptor HpdBotPayloadNotRecord = new(
        id:                 "HPDA006",
        title:              "[HpdBotPayload] type must be a record",
        messageFormat:      "Type '{0}' decorated with [HpdBotPayload] must be a record for AOT-safe JSON serialization",
        category:           Category,
        defaultSeverity:    DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    /// <summary>HPDA007: [ThreadId] format string slot has no matching record property.</summary>
    public static readonly DiagnosticDescriptor ThreadIdSlotMissing = new(
        id:                 "HPDA007",
        title:              "[ThreadId] format string slot has no matching property",
        messageFormat:      "Format string slot '{{{0}}}' in [ThreadId(\"{1}\")] has no matching property on record '{2}'",
        category:           Category,
        defaultSeverity:    DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    /// <summary>HPDA008: [HpdSocketTransport] service type must extend BotWebSocketService.</summary>
    public static readonly DiagnosticDescriptor SocketTransportInvalidType = new(
        id:                 "HPDA008",
        title:              "[HpdSocketTransport] service type must extend BotWebSocketService",
        messageFormat:      "Type '{0}' used in [HpdSocketTransport] must extend BotWebSocketService",
        category:           Category,
        defaultSeverity:    DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    /// <summary>HPDA009: [HpdBotPreDispatch] method has wrong signature.</summary>
    public static readonly DiagnosticDescriptor PreDispatchWrongSignature = new(
        id:                 "HPDA009",
        title:              "[HpdBotPreDispatch] method has wrong signature",
        messageFormat:      "Method '{0}' decorated with [HpdBotPreDispatch] must be 'private/internal Task<BotAdapterResponse?>(BotRequestContext ctx, byte[] bodyBytes)'",
        category:           Category,
        defaultSeverity:    DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    /// <summary>HPDA010: [HpdBotEnvelopeExtractor] method has wrong signature.</summary>
    public static readonly DiagnosticDescriptor BodyExtractorWrongSignature = new(
        id:                 "HPDA010",
        title:              "[HpdBotEnvelopeExtractor] method has wrong signature",
        messageFormat:      "Method '{0}' decorated with [HpdBotEnvelopeExtractor] must be 'private/internal (string? eventType, byte[] dispatchBytes)(BotRequestContext ctx, byte[] bodyBytes)'",
        category:           Category,
        defaultSeverity:    DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    /// <summary>HPDA011: [HpdHttpMethods] must contain at least one non-empty method.</summary>
    public static readonly DiagnosticDescriptor InvalidWebhookMethods = new(
        id:                 "HPDA011",
        title:              "[HpdHttpMethods] method list is invalid",
        messageFormat:      "Bot class '{0}' has [HpdHttpMethods] but no non-empty HTTP methods",
        category:           Category,
        defaultSeverity:    DiagnosticSeverity.Error,
        isEnabledByDefault: true);
}
