using System.Text;
using HPD.Payments.Primitives.Identity;

namespace HPD.Payments.Connectors.Stripe;

/// <summary>Names the bounded Stripe operations implemented by the construction probe.</summary>
public enum StripeOperation
{
    /// <summary>Invalid default operation.</summary>
    None = 0,
    /// <summary>Capture an existing PaymentIntent.</summary>
    Capture,
    /// <summary>Cancel an existing PaymentIntent before capture.</summary>
    Cancel,
    /// <summary>Refund an existing charge or PaymentIntent.</summary>
    Refund,
    /// <summary>Retrieve an existing PaymentIntent for synchronization.</summary>
    Retrieve,
}

/// <summary>Owns one deterministic Stripe HTTP request plan without sending it.</summary>
public sealed class StripeRequestPlan
{
    private readonly byte[] _body;

    /// <summary>Gets the semantic operation.</summary>
    public StripeOperation Operation { get; }
    /// <summary>Gets the HTTP method token.</summary>
    public string Method { get; }
    /// <summary>Gets the relative Stripe API path.</summary>
    public string Path { get; }
    /// <summary>Gets the semantic idempotency key; absent only for Retrieve.</summary>
    public string? IdempotencyKey { get; }
    /// <summary>Gets the pinned credential revision.</summary>
    public Revision CredentialRevision { get; }
    /// <summary>Gets the pinned configuration revision.</summary>
    public Revision ConfigurationRevision { get; }
    /// <summary>Gets the pinned Stripe API revision.</summary>
    public Revision ApiRevision { get; }
    /// <summary>Gets the canonical digest of method, path, headers, and body.</summary>
    public CanonicalDigest RequestDigest { get; }

    internal StripeRequestPlan(StripeOperation operation, string method, string path, string? idempotencyKey,
        Revision credentialRevision, Revision configurationRevision, Revision apiRevision, ReadOnlySpan<byte> body,
        CanonicalDigest requestDigest)
    {
        Operation = operation; Method = method; Path = path; IdempotencyKey = idempotencyKey;
        CredentialRevision = credentialRevision; ConfigurationRevision = configurationRevision; ApiRevision = apiRevision;
        _body = body.ToArray(); RequestDigest = requestDigest;
    }

    /// <summary>Returns a new copy of the exact form body.</summary>
    public byte[] CopyBody() => _body.ToArray();
}

/// <summary>Builds deterministic Stripe request plans with no transport or ambient configuration.</summary>
public static class StripeRequestPlanner
{
    private static readonly CanonicalDigestProfileId DigestProfile =
        new("stripe-request", ContractVersion.Create(1, 0), "method-path-idempotency-revisions-body", "ordinal", "utc", "ordered", "none");

    /// <summary>Creates an exact request plan.</summary>
    public static StripeRequestPlan Create(StripeOperation operation, string providerObjectId, string? idempotencyKey,
        long amountMinor, string currency, Revision credentialRevision, Revision configurationRevision, Revision apiRevision)
    {
        ArgumentNullException.ThrowIfNull(currency);
        if (operation == StripeOperation.None || !Enum.IsDefined(operation) ||
            !ScopeId.TryCreate("stripe", "object", providerObjectId, out _) ||
            operation != StripeOperation.Retrieve && !ScopeId.TryCreate("stripe", "idempotency", idempotencyKey, out _) ||
            amountMinor < 0 || !ScopeId.TryCreate("stripe", "currency", currency, out _) ||
            currency.Length != 3 || !credentialRevision.IsValid || !configurationRevision.IsValid || !apiRevision.IsValid)
            throw new ArgumentException("Stripe request requires operation, object, idempotency, amount/currency, and pinned revisions.");

        var (method, path) = operation switch
        {
            StripeOperation.Capture => ("POST", $"/v1/payment_intents/{providerObjectId}/capture"),
            StripeOperation.Cancel => ("POST", $"/v1/payment_intents/{providerObjectId}/cancel"),
            StripeOperation.Refund => ("POST", "/v1/refunds"),
            StripeOperation.Retrieve => ("GET", $"/v1/payment_intents/{providerObjectId}"),
            _ => throw new InvalidOperationException("Unreachable Stripe operation."),
        };
        var body = operation switch
        {
            StripeOperation.Capture => Encoding.ASCII.GetBytes($"amount_to_capture={amountMinor}"),
            StripeOperation.Refund => Encoding.ASCII.GetBytes($"amount={amountMinor}&currency={currency}&payment_intent={providerObjectId}"),
            _ => [],
        };
        var canonical = Encoding.UTF8.GetBytes(string.Join('\n', method, path, idempotencyKey ?? string.Empty,
            credentialRevision.ToString(), configurationRevision.ToString(), apiRevision.ToString(),
            Convert.ToHexString(body)));
        var digest = CanonicalDigest.Sha256(DigestProfile, canonical);
        return new(operation, method, path, operation == StripeOperation.Retrieve ? null : idempotencyKey,
            credentialRevision, configurationRevision, apiRevision, body, digest);
    }
}
