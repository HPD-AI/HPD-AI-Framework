using Microsoft.CodeAnalysis;

namespace HPD.Agent.Adapters.SourceGenerator.Diagnostics;

internal static class AdapterDiagnostics
{
    private const string Category = "HPD.Adapters";

    /// <summary>HPDA001: [HpdAdapter] class must be public.</summary>
    public static readonly DiagnosticDescriptor AdapterNotPublic = new(
        id:                 "HPDA001",
        title:              "[HpdAdapter] class must be public",
        messageFormat:      "Adapter class '{0}' decorated with [HpdAdapter] must be public",
        category:           Category,
        defaultSeverity:    DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    /// <summary>HPDA002: [HpdWebhookHandler] method must be private or internal.</summary>
    public static readonly DiagnosticDescriptor HandlerNotPrivate = new(
        id:                 "HPDA002",
        title:              "[HpdWebhookHandler] method must be private or internal",
        messageFormat:      "Webhook handler '{0}' must be private or internal — the generator produces the public dispatch entry point",
        category:           Category,
        defaultSeverity:    DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    /// <summary>HPDA003: [HpdStreaming] declared more than once on the same class.</summary>
    public static readonly DiagnosticDescriptor DuplicateStreaming = new(
        id:                 "HPDA003",
        title:              "[HpdStreaming] declared more than once",
        messageFormat:      "Adapter class '{0}' has multiple [HpdStreaming] attributes — only one is allowed",
        category:           Category,
        defaultSeverity:    DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    /// <summary>HPDA004: [HpdPermissionHandler] declared more than once on the same class.</summary>
    public static readonly DiagnosticDescriptor DuplicatePermissionHandler = new(
        id:                 "HPDA004",
        title:              "[HpdPermissionHandler] declared more than once",
        messageFormat:      "Adapter class '{0}' has multiple [HpdPermissionHandler] methods — only one is allowed",
        category:           Category,
        defaultSeverity:    DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    /// <summary>HPDA005: [HpdAdapter] name collides with another adapter in the same assembly.</summary>
    public static readonly DiagnosticDescriptor DuplicateAdapterName = new(
        id:                 "HPDA005",
        title:              "[HpdAdapter] name collision",
        messageFormat:      "Adapter name '{0}' is used by both '{1}' and '{2}' in the same assembly",
        category:           Category,
        defaultSeverity:    DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    /// <summary>HPDA006: [WebhookPayload] type must be a record.</summary>
    public static readonly DiagnosticDescriptor WebhookPayloadNotRecord = new(
        id:                 "HPDA006",
        title:              "[WebhookPayload] type must be a record",
        messageFormat:      "Type '{0}' decorated with [WebhookPayload] must be a record for AOT-safe JSON serialization",
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

    /// <summary>HPDA008: [HpdSocketTransport] service type must extend AdapterWebSocketService.</summary>
    public static readonly DiagnosticDescriptor SocketTransportInvalidType = new(
        id:                 "HPDA008",
        title:              "[HpdSocketTransport] service type must extend AdapterWebSocketService",
        messageFormat:      "Type '{0}' used in [HpdSocketTransport] must extend AdapterWebSocketService",
        category:           Category,
        defaultSeverity:    DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    /// <summary>HPDA009: [HpdPreDispatch] method has wrong signature.</summary>
    public static readonly DiagnosticDescriptor PreDispatchWrongSignature = new(
        id:                 "HPDA009",
        title:              "[HpdPreDispatch] method has wrong signature",
        messageFormat:      "Method '{0}' decorated with [HpdPreDispatch] must be 'private/internal async Task<IResult?>(HttpContext ctx, byte[] bodyBytes)'",
        category:           Category,
        defaultSeverity:    DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    /// <summary>HPDA010: [HpdBodyExtractor] method has wrong signature.</summary>
    public static readonly DiagnosticDescriptor BodyExtractorWrongSignature = new(
        id:                 "HPDA010",
        title:              "[HpdBodyExtractor] method has wrong signature",
        messageFormat:      "Method '{0}' decorated with [HpdBodyExtractor] must be 'private/internal (string? eventType, byte[] dispatchBytes)(HttpContext ctx, byte[] bodyBytes)'",
        category:           Category,
        defaultSeverity:    DiagnosticSeverity.Error,
        isEnabledByDefault: true);
}
