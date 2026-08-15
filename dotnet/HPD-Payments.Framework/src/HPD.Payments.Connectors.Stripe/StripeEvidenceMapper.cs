using HPD.Payments.Primitives.Identity;
using HPD.Payments.Primitives.Time;
using HPD.Payments.Runtime.ExternalEffects;

namespace HPD.Payments.Connectors.Stripe;

/// <summary>Maps authenticated Stripe observations into question-scoped runtime evidence without adjudicating them.</summary>
public static class StripeEvidenceMapper
{
    /// <summary>Maps authenticated API, webhook, or poll evidence about provider occurrence.</summary>
    public static ProviderEvidenceClaim MapOccurrence(string evidenceId, ProviderEvidenceChannel channel,
        string providerObjectId, string state, CanonicalDigest digest, ulong sourceSequence,
        DateTimeOffset observedAtUtc, bool authenticated, bool compatible)
    {
        if (channel is not (ProviderEvidenceChannel.ApiResponse or ProviderEvidenceChannel.Webhook or ProviderEvidenceChannel.Poll))
            throw new ArgumentException("Occurrence evidence must use an API-response, webhook, or poll channel.", nameof(channel));
        return Create(evidenceId, providerObjectId, EvidenceQuestion.ProviderOccurrence, channel, state, digest,
            sourceSequence, observedAtUtc, authenticated, compatible);
    }

    /// <summary>Maps authenticated settlement evidence without treating occurrence evidence as settlement authority.</summary>
    public static ProviderEvidenceClaim MapSettlement(string evidenceId, string providerObjectId, string state,
        CanonicalDigest digest, ulong sourceSequence, DateTimeOffset observedAtUtc,
        bool authenticated, bool compatible) =>
        Create(evidenceId, providerObjectId, EvidenceQuestion.SettlementInclusion,
            ProviderEvidenceChannel.Settlement, state, digest, sourceSequence, observedAtUtc, authenticated, compatible);

    private static ProviderEvidenceClaim Create(string evidenceId, string providerObjectId, EvidenceQuestion question,
        ProviderEvidenceChannel channel, string state, CanonicalDigest digest, ulong sourceSequence,
        DateTimeOffset observedAtUtc, bool authenticated, bool compatible)
    {
        ArgumentNullException.ThrowIfNull(digest);
        var scope = ScopeId.Create("stripe", "provider", "evidence");
        var claimId = SemanticId.Create(scope, "stripe", "evidence", evidenceId, "stripe", providerObjectId);
        return new(claimId, question, channel, digest, sourceSequence,
            NamedTime.Create(TimeKind.Observed, observedAtUtc), authenticated, compatible, state);
    }
}
