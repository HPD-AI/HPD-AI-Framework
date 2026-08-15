using HPD.Payments.Primitives.Identity;
using HPD.Payments.Primitives.Time;
using HPD.Payments.Runtime.ExternalEffects;

namespace HPD.Payments.Runtime.Tests.ExternalEffects;

internal static class ProviderEvidencePrecedenceProofs
{
    internal static void Run(List<string> failures)
    {
        void Check(bool value, string message) { if (!value) failures.Add(message); }
        var scope = ScopeId.Create("tenant", "runtime", "precedence");
        SemanticId Id(string kind, string local) => SemanticId.Create(scope, "provider", kind, local);
        var profile = new CanonicalDigestProfileId("runtime", ContractVersion.Create(1, 0), "fields", "ordinal", "utc", "ordered", "none");
        CanonicalDigest Digest(string value) => CanonicalDigest.Sha256(profile, System.Text.Encoding.UTF8.GetBytes(value));
        ProviderEvidenceClaim Claim(string id, EvidenceQuestion question, ProviderEvidenceChannel channel, ulong sequence,
            string state, bool authenticated = true, bool compatible = true, string? digest = null) =>
            new(Id("claim", id), question, channel, Digest(digest ?? state), sequence,
                NamedTime.Create(TimeKind.Observed, DateTimeOffset.UnixEpoch.AddSeconds(sequence)),
                authenticated, compatible, state);

        var adjudicator = ProviderEvidenceAdjudicator.Create();
        var webhook = Claim("webhook-1", EvidenceQuestion.ProviderOccurrence, ProviderEvidenceChannel.Webhook, 2, "succeeded");
        var selectedWebhook = adjudicator.Admit(webhook);
        Check(selectedWebhook.Disposition == EvidenceClaimDisposition.Selected, "authenticated webhook was not selected");
        var lateResponse = selectedWebhook.Adjudicator.Admit(
            Claim("response-1", EvidenceQuestion.ProviderOccurrence, ProviderEvidenceChannel.ApiResponse, 1, "pending"));
        Check(lateResponse.Disposition == EvidenceClaimDisposition.Retained &&
            lateResponse.Adjudicator.Selected[EvidenceQuestion.ProviderOccurrence].State == "succeeded",
            "arrival order overrode question-specific precedence");

        var replay = lateResponse.Adjudicator.Admit(webhook);
        Check(replay.Disposition == EvidenceClaimDisposition.Replay, "exact provider claim did not replay");
        var conflict = lateResponse.Adjudicator.Admit(
            Claim("webhook-1", EvidenceQuestion.ProviderOccurrence, ProviderEvidenceChannel.Webhook, 2, "failed", digest: "changed"));
        Check(conflict.Disposition == EvidenceClaimDisposition.Conflict, "same identity with changed digest was not conflicted");
        var unauthenticated = lateResponse.Adjudicator.Admit(
            Claim("webhook-2", EvidenceQuestion.ProviderOccurrence, ProviderEvidenceChannel.Webhook, 3, "succeeded", authenticated: false));
        Check(unauthenticated.Disposition == EvidenceClaimDisposition.Quarantined, "unauthenticated evidence entered adjudication");

        var poll = lateResponse.Adjudicator.Admit(
            Claim("poll-1", EvidenceQuestion.ProviderOccurrence, ProviderEvidenceChannel.Poll, 3, "failed"));
        Check(poll.Disposition == EvidenceClaimDisposition.Selected &&
            poll.Adjudicator.Selected[EvidenceQuestion.ProviderOccurrence].State == "failed" &&
            poll.Adjudicator.Claims.Count == 3,
            "provider contradiction was not retained and question-specifically selected");

        var settlement = poll.Adjudicator.Admit(
            Claim("settlement-1", EvidenceQuestion.SettlementInclusion, ProviderEvidenceChannel.Settlement, 1, "excluded"));
        Check(settlement.Adjudicator.Selected[EvidenceQuestion.ProviderOccurrence].State == "failed" &&
            settlement.Adjudicator.Selected[EvidenceQuestion.SettlementInclusion].State == "excluded",
            "settlement evidence overwrote provider-occurrence truth");
    }
}
