namespace HPD.Agent.Bots;

/// <summary>
/// Marks a method that extracts the event type string and the bytes to dispatch
/// from the raw inbound envelope body. The generator calls this instead of its default
/// JSON "type" extraction when present.
/// </summary>
/// <remarks>
/// Method signature must be:
///   private (string? eventType, byte[] dispatchBytes) {name}(BotRequestContext ctx, byte[] bodyBytes)
///
/// If absent, the generator uses the default: parse JSON, read top-level "type" field,
/// dispatch on raw bodyBytes.
/// Only one [HpdBotEnvelopeExtractor] method is allowed per adapter class.
/// </remarks>
[AttributeUsage(AttributeTargets.Method, Inherited = false)]
public sealed class HpdBotEnvelopeExtractorAttribute : Attribute { }
