using HPD.Payments.Primitives.Identity;
using HPD.Payments.Primitives.Time;

namespace HPD.Payments.Runtime.ExternalEffects;

/// <summary>Names a closed external evidence channel without assigning universal authority.</summary>
public enum ProviderEvidenceChannel
{
    /// <summary>Invalid default channel.</summary>
    None = 0,
    /// <summary>Synchronous provider API response.</summary>
    ApiResponse,
    /// <summary>Authenticated provider webhook.</summary>
    Webhook,
    /// <summary>Authenticated provider synchronization or poll.</summary>
    Poll,
    /// <summary>Authenticated settlement authority evidence.</summary>
    Settlement,
    /// <summary>Governed operator evidence; never inherently preferred.</summary>
    Operator,
}

/// <summary>Names the exact question for which evidence is adjudicated.</summary>
public enum EvidenceQuestion
{
    /// <summary>Invalid default question.</summary>
    None = 0,
    /// <summary>Whether the provider operation occurred.</summary>
    ProviderOccurrence,
    /// <summary>Whether settlement included the operation.</summary>
    SettlementInclusion,
    /// <summary>Whether a named owner postcondition is freshly verified.</summary>
    OwnerPostcondition,
}

/// <summary>Represents one authenticated, compatible, append-only evidence claim.</summary>
public sealed record ProviderEvidenceClaim
{
    /// <summary>Gets the semantic source-item identity.</summary>
    public SemanticId ClaimId { get; }
    /// <summary>Gets the exact adjudication question.</summary>
    public EvidenceQuestion Question { get; }
    /// <summary>Gets the source channel.</summary>
    public ProviderEvidenceChannel Channel { get; }
    /// <summary>Gets the canonical source bytes digest.</summary>
    public CanonicalDigest Digest { get; }
    /// <summary>Gets the source ordering cursor.</summary>
    public ulong SourceSequence { get; }
    /// <summary>Gets when HPD observed the claim.</summary>
    public NamedTime ObservedAt { get; }
    /// <summary>Gets whether source authentication was established before normalization.</summary>
    public bool Authenticated { get; }
    /// <summary>Gets whether the claim schema/protocol is admitted.</summary>
    public bool Compatible { get; }
    /// <summary>Gets the source-specific state token.</summary>
    public string State { get; }

    /// <summary>Creates an immutable source claim.</summary>
    public ProviderEvidenceClaim(SemanticId claimId, EvidenceQuestion question, ProviderEvidenceChannel channel,
        CanonicalDigest digest, ulong sourceSequence, NamedTime observedAt, bool authenticated, bool compatible, string state)
    {
        ArgumentNullException.ThrowIfNull(digest);
        if (!claimId.IsValid || question == EvidenceQuestion.None || !Enum.IsDefined(question) ||
            channel == ProviderEvidenceChannel.None || !Enum.IsDefined(channel) || sourceSequence == 0 ||
            !observedAt.IsValid || observedAt.Kind != TimeKind.Observed ||
            !ScopeId.TryCreate("evidence", "state", state, out _))
            throw new ArgumentException("Evidence claim requires identity, question, channel, sequence, observed time, and bounded state.");
        ClaimId = claimId; Question = question; Channel = channel; Digest = digest; SourceSequence = sourceSequence;
        ObservedAt = observedAt; Authenticated = authenticated; Compatible = compatible; State = state;
    }
}

/// <summary>Names the admission/adjudication result for one evidence claim.</summary>
public enum EvidenceClaimDisposition
{
    /// <summary>Invalid default disposition.</summary>
    None = 0,
    /// <summary>The claim was appended but does not supersede the current selection.</summary>
    Retained,
    /// <summary>The claim became the selected projection for its exact question.</summary>
    Selected,
    /// <summary>The exact identity and digest replayed an existing claim.</summary>
    Replay,
    /// <summary>The same identity carried different canonical content.</summary>
    Conflict,
    /// <summary>Authentication or compatibility failed before adjudication.</summary>
    Quarantined,
}

/// <summary>Represents one immutable evidence-admission result.</summary>
public sealed record EvidenceClaimResult(ProviderEvidenceAdjudicator Adjudicator, EvidenceClaimDisposition Disposition, string Code);

/// <summary>Adjudicates provider evidence using question-specific channel precedence and source ordering.</summary>
/// <remarks>Arrival time, transport acknowledgement, health, and operator assertion never provide implicit precedence.</remarks>
public sealed record ProviderEvidenceAdjudicator
{
    private readonly Dictionary<SemanticId, ProviderEvidenceClaim> _claims;
    private readonly Dictionary<EvidenceQuestion, ProviderEvidenceClaim> _selected;

    /// <summary>Gets defensive copies of all admitted claims.</summary>
    public IReadOnlyCollection<ProviderEvidenceClaim> Claims => _claims.Values.ToArray();
    /// <summary>Gets defensive copies of selected question-specific projections.</summary>
    public IReadOnlyDictionary<EvidenceQuestion, ProviderEvidenceClaim> Selected =>
        new Dictionary<EvidenceQuestion, ProviderEvidenceClaim>(_selected);

    private ProviderEvidenceAdjudicator(Dictionary<SemanticId, ProviderEvidenceClaim> claims,
        Dictionary<EvidenceQuestion, ProviderEvidenceClaim> selected) => (_claims, _selected) = (claims, selected);

    /// <summary>Creates an empty adjudicator.</summary>
    public static ProviderEvidenceAdjudicator Create() => new([], []);

    /// <summary>Authenticates, admits, deduplicates, and question-specifically selects one claim.</summary>
    public EvidenceClaimResult Admit(ProviderEvidenceClaim claim)
    {
        ArgumentNullException.ThrowIfNull(claim);
        if (!claim.Authenticated || !claim.Compatible)
            return new(this, EvidenceClaimDisposition.Quarantined,
                !claim.Authenticated ? "authentication-failed" : "compatibility-failed");
        if (_claims.TryGetValue(claim.ClaimId, out var prior))
            return prior.Digest.Equals(claim.Digest)
                ? new(this, EvidenceClaimDisposition.Replay, "exact-replay")
                : new(this, EvidenceClaimDisposition.Conflict, "identity-digest-conflict");

        var claims = new Dictionary<SemanticId, ProviderEvidenceClaim>(_claims) { [claim.ClaimId] = claim };
        var selected = new Dictionary<EvidenceQuestion, ProviderEvidenceClaim>(_selected);
        var becomesSelected = !selected.TryGetValue(claim.Question, out var current) || Prefer(claim, current);
        if (becomesSelected) selected[claim.Question] = claim;
        return new(new(claims, selected), becomesSelected ? EvidenceClaimDisposition.Selected : EvidenceClaimDisposition.Retained,
            becomesSelected ? "question-selection-updated" : "claim-retained");
    }

    private static bool Prefer(ProviderEvidenceClaim candidate, ProviderEvidenceClaim current)
    {
        var candidateRank = Rank(candidate.Question, candidate.Channel);
        var currentRank = Rank(current.Question, current.Channel);
        return candidateRank > currentRank || candidateRank == currentRank && candidate.SourceSequence > current.SourceSequence;
    }

    private static int Rank(EvidenceQuestion question, ProviderEvidenceChannel channel) => question switch
    {
        EvidenceQuestion.ProviderOccurrence => channel switch
        {
            ProviderEvidenceChannel.Poll => 40,
            ProviderEvidenceChannel.Webhook => 30,
            ProviderEvidenceChannel.ApiResponse => 20,
            ProviderEvidenceChannel.Operator => 10,
            _ => 0,
        },
        EvidenceQuestion.SettlementInclusion => channel == ProviderEvidenceChannel.Settlement ? 40 : 0,
        EvidenceQuestion.OwnerPostcondition => channel == ProviderEvidenceChannel.Operator ? 10 : 0,
        _ => 0,
    };
}
