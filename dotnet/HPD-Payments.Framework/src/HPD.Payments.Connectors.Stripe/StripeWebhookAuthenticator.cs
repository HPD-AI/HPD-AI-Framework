using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using HPD.Payments.Primitives.Identity;

namespace HPD.Payments.Connectors.Stripe;

/// <summary>Owns the minimal authenticated Stripe event fields required for later evidence adjudication.</summary>
public sealed record StripeWebhookEvidence
{
    /// <summary>Gets the Stripe event identity.</summary>
    public string EventId { get; }
    /// <summary>Gets the Stripe event type.</summary>
    public string EventType { get; }
    /// <summary>Gets the provider object identity.</summary>
    public string ObjectId { get; }
    /// <summary>Gets the provider object status token.</summary>
    public string Status { get; }
    /// <summary>Gets the digest of the exact authenticated payload bytes.</summary>
    public CanonicalDigest PayloadDigest { get; }
    /// <summary>Gets the signed Unix timestamp.</summary>
    public long SignedUnixTime { get; }

    internal StripeWebhookEvidence(string eventId, string eventType, string objectId, string status,
        CanonicalDigest payloadDigest, long signedUnixTime) =>
        (EventId, EventType, ObjectId, Status, PayloadDigest, SignedUnixTime) =
        (eventId, eventType, objectId, status, payloadDigest, signedUnixTime);
}

/// <summary>Authenticates Stripe webhook bytes before parsing or normalization.</summary>
public static class StripeWebhookAuthenticator
{
    /// <summary>Maximum admitted webhook payload bytes.</summary>
    public const int MaximumPayloadBytes = 1_048_576;
    private static readonly CanonicalDigestProfileId PayloadProfile =
        new("stripe-webhook", ContractVersion.Create(1, 0), "raw-authenticated-bytes", "ordinal", "utc", "none", "none");

    /// <summary>Authenticates the signed bytes, applies a bounded clock tolerance, then parses required fields.</summary>
    public static bool TryAuthenticateAndParse(ReadOnlySpan<byte> payload, string signatureHeader,
        ReadOnlySpan<byte> webhookSecret, DateTimeOffset nowUtc, TimeSpan tolerance, out StripeWebhookEvidence? evidence)
    {
        evidence = null;
        if (payload.Length is 0 or > MaximumPayloadBytes || webhookSecret.Length is < 16 or > 1024 ||
            nowUtc.Offset != TimeSpan.Zero || tolerance < TimeSpan.Zero || tolerance > TimeSpan.FromHours(1) ||
            !TrySignature(signatureHeader, out var signedTime, out var suppliedTag))
            return false;
        DateTimeOffset signedAt;
        try { signedAt = DateTimeOffset.FromUnixTimeSeconds(signedTime); }
        catch (ArgumentOutOfRangeException) { return false; }
        if ((nowUtc - signedAt).Duration() > tolerance) return false;

        var prefix = Encoding.ASCII.GetBytes($"{signedTime}.");
        var signedBytes = new byte[prefix.Length + payload.Length];
        prefix.CopyTo(signedBytes, 0); payload.CopyTo(signedBytes.AsSpan(prefix.Length));
        var expected = HMACSHA256.HashData(webhookSecret, signedBytes);
        if (suppliedTag.Length != expected.Length || !CryptographicOperations.FixedTimeEquals(expected, suppliedTag))
            return false;

        if (!TryParse(payload, out var eventId, out var eventType, out var objectId, out var status))
            return false;
        evidence = new(eventId, eventType, objectId, status, CanonicalDigest.Sha256(PayloadProfile, payload), signedTime);
        return true;
    }

    private static bool TrySignature(string header, out long timestamp, out byte[] tag)
    {
        timestamp = 0; tag = [];
        if (string.IsNullOrEmpty(header) || header.Length > 4096) return false;
        foreach (var part in header.Split(',', StringSplitOptions.TrimEntries))
        {
            if (part.StartsWith("t=", StringComparison.Ordinal) &&
                long.TryParse(part.AsSpan(2), System.Globalization.NumberStyles.None,
                    System.Globalization.CultureInfo.InvariantCulture, out var parsed))
                timestamp = parsed;
            else if (part.StartsWith("v1=", StringComparison.Ordinal))
                try { tag = Convert.FromHexString(part[3..]); } catch (FormatException) { return false; }
        }
        return timestamp > 0 && tag.Length > 0;
    }

    private static bool TryParse(ReadOnlySpan<byte> payload, out string eventId, out string eventType,
        out string objectId, out string status)
    {
        eventId = eventType = objectId = status = string.Empty;
        try
        {
            var reader = new Utf8JsonReader(payload, new JsonReaderOptions { MaxDepth = 16 });
            var path = new Stack<string>();
            string? property = null;
            while (reader.Read())
            {
                if (reader.TokenType == JsonTokenType.PropertyName) { property = reader.GetString(); continue; }
                if (reader.TokenType == JsonTokenType.StartObject && property is not null) { path.Push(property); property = null; continue; }
                if (reader.TokenType == JsonTokenType.EndObject && path.Count > 0) { path.Pop(); continue; }
                if (reader.TokenType != JsonTokenType.String || property is null) continue;
                var value = reader.GetString()!;
                if (path.Count == 0 && property == "id") eventId = value;
                else if (path.Count == 0 && property == "type") eventType = value;
                else if (path.Contains("object") && property == "id") objectId = value;
                else if (path.Contains("object") && property == "status") status = value;
                property = null;
            }
        }
        catch (JsonException) { return false; }
        return ScopeId.TryCreate("stripe", "event", eventId, out _) &&
            ScopeId.TryCreate("stripe", "event-type", eventType, out _) &&
            ScopeId.TryCreate("stripe", "object", objectId, out _) &&
            ScopeId.TryCreate("stripe", "status", status, out _);
    }
}
