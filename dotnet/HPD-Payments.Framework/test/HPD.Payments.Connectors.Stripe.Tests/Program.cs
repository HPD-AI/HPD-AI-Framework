using System.Security.Cryptography;
using System.Text;
using HPD.Payments.Connectors.Stripe;
using HPD.Payments.Primitives.Identity;
using HPD.Payments.Runtime.ExternalEffects;
using HPD.Payments.Contracts.CapabilityEvidence;
using HPD.Payments.Contracts.ExternalEffect;
using HPD.Payments.Primitives.Time;

var failures = new List<string>();
void Check(bool value, string message) { if (!value) failures.Add(message); }
var credential = Revision.Create("credential", 1);
var configuration = Revision.Create("configuration", 1);
var api = Revision.Create("api", 20240101);
var capture1 = StripeRequestPlanner.Create(StripeOperation.Capture, "pi_123", "capture-1", 500, "usd",
    credential, configuration, api);
var capture2 = StripeRequestPlanner.Create(StripeOperation.Capture, "pi_123", "capture-1", 500, "usd",
    credential, configuration, api);
Check(capture1.RequestDigest.Equals(capture2.RequestDigest) && capture1.CopyBody().SequenceEqual("amount_to_capture=500"u8.ToArray()),
    "Stripe capture plan is not deterministic");
var rotated = StripeRequestPlanner.Create(StripeOperation.Capture, "pi_123", "capture-1", 500, "usd",
    Revision.Create("credential", 2), configuration, api);
Check(!capture1.RequestDigest.Equals(rotated.RequestDigest), "credential rotation did not alter exact request binding");
var retrieve = StripeRequestPlanner.Create(StripeOperation.Retrieve, "pi_123", null, 0, "usd", credential, configuration, api);
Check(retrieve.Method == "GET" && retrieve.IdempotencyKey is null && retrieve.CopyBody().Length == 0, "retrieve plan is not side-effect free");

var now = new DateTimeOffset(2030, 1, 1, 0, 0, 0, TimeSpan.Zero);
var timestamp = now.ToUnixTimeSeconds();
var payload = Encoding.UTF8.GetBytes("{\"id\":\"evt_1\",\"type\":\"payment_intent.succeeded\",\"data\":{\"object\":{\"id\":\"pi_123\",\"status\":\"succeeded\"}}}");
var secret = Encoding.ASCII.GetBytes("whsec_12345678901234567890123456789012");
var signed = Encoding.ASCII.GetBytes($"{timestamp}.{Encoding.UTF8.GetString(payload)}");
var tag = Convert.ToHexString(HMACSHA256.HashData(secret, signed));
var header = $"t={timestamp},v1={tag}";
Check(StripeWebhookAuthenticator.TryAuthenticateAndParse(payload, header, secret, now, TimeSpan.FromMinutes(5), out var evidence) &&
    evidence!.EventId == "evt_1" && evidence.ObjectId == "pi_123" && evidence.Status == "succeeded",
    "valid Stripe webhook was not authenticated and parsed");
var tampered = payload.ToArray(); tampered[^2] ^= 1;
Check(!StripeWebhookAuthenticator.TryAuthenticateAndParse(tampered, header, secret, now, TimeSpan.FromMinutes(5), out _),
    "tampered payload passed authentication");
Check(!StripeWebhookAuthenticator.TryAuthenticateAndParse(payload, header, secret, now.AddMinutes(6), TimeSpan.FromMinutes(5), out _),
    "expired signature passed tolerance");
Check(!StripeWebhookAuthenticator.TryAuthenticateAndParse(payload, $"t={timestamp},v1=00", secret, now, TimeSpan.FromMinutes(5), out _),
    "invalid signature passed");

var webhookClaim = StripeEvidenceMapper.MapOccurrence("evt-1", ProviderEvidenceChannel.Webhook, "pi-123", "succeeded",
    evidence!.PayloadDigest, 10, now, authenticated: true, compatible: true);
var pollClaim = StripeEvidenceMapper.MapOccurrence("poll-1", ProviderEvidenceChannel.Poll, "pi-123", "canceled",
    CanonicalDigest.Sha256(evidence.PayloadDigest.Profile, "poll-canceled"u8), 11, now.AddSeconds(1), true, true);
var settlementClaim = StripeEvidenceMapper.MapSettlement("settlement-1", "pi-123", "included",
    CanonicalDigest.Sha256(evidence.PayloadDigest.Profile, "settlement-included"u8), 12, now.AddSeconds(2), true, true);
var adjudicator = ProviderEvidenceAdjudicator.Create();
var webhookResult = adjudicator.Admit(webhookClaim);
var pollResult = webhookResult.Adjudicator.Admit(pollClaim);
var settlementResult = pollResult.Adjudicator.Admit(settlementClaim);
Check(pollResult.Disposition == EvidenceClaimDisposition.Selected &&
    pollResult.Adjudicator.Selected[EvidenceQuestion.ProviderOccurrence].State == "canceled",
    "fresh poll evidence did not supersede webhook occurrence projection");
Check(settlementResult.Adjudicator.Selected[EvidenceQuestion.SettlementInclusion].State == "included" &&
    settlementResult.Adjudicator.Selected[EvidenceQuestion.ProviderOccurrence].State == "canceled",
    "settlement authority was collapsed into occurrence authority");
var quarantined = settlementResult.Adjudicator.Admit(StripeEvidenceMapper.MapOccurrence("poll-2",
    ProviderEvidenceChannel.Poll, "pi-123", "succeeded", evidence.PayloadDigest, 13, now.AddSeconds(3), false, true));
Check(quarantined.Disposition == EvidenceClaimDisposition.Quarantined,
    "unauthenticated poll evidence was admitted");

var capabilityScope = ScopeId.Create("tenant", "construction", "stripe-probe");
var account = SemanticId.Create(capabilityScope, "connector", "account", "unselected", "stripe", "test");
var capabilityContext = new CapabilityContext(account, "capture", "2024-01-01", Revision.Create("code", 1),
    configuration, credential, "static", "osx-arm64");
SemanticId CapabilityId(string local) => SemanticId.Create(capabilityScope, "connector", "capability-evidence", local);
var verified = NamedTime.Create(TimeKind.Verify, now);
var validUntil = NamedTime.Create(TimeKind.Expiry, now.AddHours(1));
var unsupported = new CapabilityEvidenceFact(CapabilityId("unsupported"), capabilityContext, CapabilityDisposition.Negative,
    "tuple-not-selected", verified, validUntil, capture1.RequestDigest);
Check(!unsupported.EstablishesSupport(now), "unselected Stripe tuple established support");
foreach (CapabilityDisposition disposition in new[] { CapabilityDisposition.Conditional, CapabilityDisposition.Expired,
    CapabilityDisposition.Withdrawn, CapabilityDisposition.Conflicted })
{
    SemanticId? predecessor = disposition is CapabilityDisposition.Expired or CapabilityDisposition.Withdrawn or CapabilityDisposition.Conflicted
        ? unsupported.EvidenceId : null;
    var fact = new CapabilityEvidenceFact(CapabilityId($"disposition-{(int)disposition}"), capabilityContext, disposition,
        "nonpositive-probe", verified, validUntil, rotated.RequestDigest, predecessor);
    Check(!fact.EstablishesSupport(now), $"{disposition} Stripe evidence established support");
}
Check(StripeRetryPolicy.Evaluate(capture1, ExternalEffectState.PossibleDispatch, credential, configuration, api,
    idempotencyRetentionProven: false, accountFailoverRequested: false) == StripeRetryDisposition.SynchronizeRequired,
    "unsafe Stripe retry was admitted");
Check(StripeRetryPolicy.Evaluate(capture1, ExternalEffectState.PossibleDispatch, credential, configuration, api,
    idempotencyRetentionProven: true, accountFailoverRequested: false) == StripeRetryDisposition.SafeSameIdentity,
    "proven same-identity Stripe retry was rejected");
Check(StripeRetryPolicy.Evaluate(capture1, ExternalEffectState.NotDispatched, credential, configuration, api,
    idempotencyRetentionProven: false, accountFailoverRequested: true) == StripeRetryDisposition.SynchronizeRequired,
    "side-effecting Stripe account failover was admitted");
Check(StripeRetryPolicy.Evaluate(capture1, ExternalEffectState.NotDispatched, Revision.Create("credential", 2),
    configuration, api, false, false) == StripeRetryDisposition.RejectStale, "stale Stripe credential was admitted");

if (failures.Count != 0) { foreach (var failure in failures) await Console.Error.WriteLineAsync(failure).ConfigureAwait(false); return 1; }
var message = "PASS Stripe probe: deterministic requests, revision pins, signature-before-parse, poll precedence, settlement separation";
await Console.Out.WriteLineAsync(message).ConfigureAwait(false);
return 0;
