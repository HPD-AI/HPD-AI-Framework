namespace HPD.Agent.Authority;

internal abstract record SemanticAcceptanceAdmissionResultV1
{
    private SemanticAcceptanceAdmissionResultV1() { }
    internal sealed record Committed : SemanticAcceptanceAdmissionResultV1
    { internal Committed(AuthorityFactEnvelopeV1 envelope) => Envelope = envelope ?? throw new ArgumentNullException(nameof(envelope)); internal AuthorityFactEnvelopeV1 Envelope { get; } }
    internal sealed record AlreadyCommitted : SemanticAcceptanceAdmissionResultV1
    { internal AlreadyCommitted(AuthorityFactEnvelopeV1 envelope) => Envelope = envelope ?? throw new ArgumentNullException(nameof(envelope)); internal AuthorityFactEnvelopeV1 Envelope { get; } }
    internal sealed record ProofRejected : SemanticAcceptanceAdmissionResultV1
    { internal ProofRejected(SemanticAcceptanceProofResultV1 proof) => Proof = proof ?? throw new ArgumentNullException(nameof(proof)); internal SemanticAcceptanceProofResultV1 Proof { get; } }
    internal sealed record RetryRequired : SemanticAcceptanceAdmissionResultV1
    { internal RetryRequired(long observedHead) { if (observedHead < 0) throw new ArgumentOutOfRangeException(nameof(observedHead)); ObservedHead = observedHead; } internal long ObservedHead { get; } }
    internal sealed record ContradictoryDuplicate : SemanticAcceptanceAdmissionResultV1
    { internal ContradictoryDuplicate(JournalFactId factId) { if (!factId.IsValid) throw new ArgumentException("A fact identity is required.", nameof(factId)); FactId = factId; } internal JournalFactId FactId { get; } }
    internal sealed record Rejected : SemanticAcceptanceAdmissionResultV1
    { internal Rejected(BoundedAscii safeCode) { if (!safeCode.IsValid) throw new ArgumentException("A safe code is required.", nameof(safeCode)); SafeCode = safeCode; } internal BoundedAscii SafeCode { get; } }
    internal sealed record OutcomeUnknown : SemanticAcceptanceAdmissionResultV1
    { internal OutcomeUnknown(JournalFactId factId, BoundedAscii safeCode) { if (!factId.IsValid || !safeCode.IsValid) throw new ArgumentException("A fact identity and safe code are required."); FactId = factId; SafeCode = safeCode; } internal JournalFactId FactId { get; } internal BoundedAscii SafeCode { get; } }
}

internal static class SemanticAcceptanceAdmissionV1
{
    private const uint MaximumAppendBytes = 16_384;
    private static readonly SchemaReferenceV1 AcceptedSchema = SemanticInputAcceptedV1Codec.Schema;

    internal static async ValueTask<SemanticAcceptanceAdmissionResultV1> AdmitAsync(
        IAuthorityJournalV1 journal,
        JournalPositionV1 dispositionPosition,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(journal);
        var proof = await SemanticAcceptanceProofReaderV1.ReadAsync(
            journal, dispositionPosition, cancellationToken: cancellationToken).ConfigureAwait(false);
        if (proof is SemanticAcceptanceProofResultV1.AlreadyAccepted already)
            return new SemanticAcceptanceAdmissionResultV1.AlreadyCommitted(already.Envelope);
        if (proof is not SemanticAcceptanceProofResultV1.Proven proven)
            return new SemanticAcceptanceAdmissionResultV1.ProofRejected(proof);

        var accepted = new SemanticInputAcceptedV1(
            proven.Claim.OperationId, proven.DispositionPosition, proven.Claim.Authority,
            SemanticInputAcceptanceDispositionV1.Accepted);
        var payload = SemanticInputAcceptedV1Codec.Encode(accepted);
        var payloadHash = SemanticInputAcceptedV1Codec.ComputeIntegrityHash(accepted);
        var factId = SemanticInputAcceptedFactIdV1.Derive(payloadHash);
        var sourceCorrelation = proven.DispositionEnvelope.Correlation;
        var correlation = new CorrelationEnvelopeV1(
            sourceCorrelation.TenantId, sourceCorrelation.PrincipalId, sourceCorrelation.SessionId,
            sourceCorrelation.ThreadId, sourceCorrelation.ParticipantId, proven.Claim.OperationId);
        var proposal = new ProposedAuthorityFactV1(
            factId, null, OwnerSliceId.AgentCore, AcceptedSchema, payload, payloadHash, correlation,
            proven.DispositionEnvelope.AdmittedAt);
        var request = new AppendAuthorityBatchV1(
            dispositionPosition.Session, proven.SnapshotThrough, [], [proposal], MaximumAppendBytes);
        AppendAuthorityResultV1 result;
        try
        {
            result = await journal.AppendAsync(request, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            return Unknown(factId, "append-exception");
        }
        return result switch
        {
            AppendAuthorityResultV1.Committed committed when committed.PreviousHead == proven.SnapshotThrough && committed.Envelopes.Count == 1 &&
                Matches(committed.Envelopes[0], proposal, dispositionPosition.Session) =>
                new SemanticAcceptanceAdmissionResultV1.Committed(committed.Envelopes[0]),
            AppendAuthorityResultV1.AlreadyCommitted existing when existing.Envelopes.Count == 1 &&
                Matches(existing.Envelopes[0], proposal, dispositionPosition.Session) =>
                new SemanticAcceptanceAdmissionResultV1.AlreadyCommitted(existing.Envelopes[0]),
            AppendAuthorityResultV1.SessionConflict conflict => new SemanticAcceptanceAdmissionResultV1.RetryRequired(conflict.Actual),
            AppendAuthorityResultV1.ContradictoryDuplicate => new SemanticAcceptanceAdmissionResultV1.ContradictoryDuplicate(factId),
            AppendAuthorityResultV1.InvalidPayload invalid => new SemanticAcceptanceAdmissionResultV1.Rejected(invalid.SafeCode),
            AppendAuthorityResultV1.UnknownSchema => new SemanticAcceptanceAdmissionResultV1.Rejected(new BoundedAscii("unknown-schema")),
            AppendAuthorityResultV1.CapacityRefused => new SemanticAcceptanceAdmissionResultV1.Rejected(new BoundedAscii("capacity-refused")),
            AppendAuthorityResultV1.StoreUnavailable unavailable => new SemanticAcceptanceAdmissionResultV1.OutcomeUnknown(factId, unavailable.SafeCode),
            AppendAuthorityResultV1.OutcomeUnknown => Unknown(factId, "append-outcome-unknown"),
            _ => Unknown(factId, "unexpected-append-result"),
        };
    }

    private static SemanticAcceptanceAdmissionResultV1.OutcomeUnknown Unknown(JournalFactId factId, string code) =>
        new(factId, new BoundedAscii(code));

    private static bool Matches(AuthorityFactEnvelopeV1 envelope, ProposedAuthorityFactV1 proposal, SessionAuthorityStampV1 session) =>
        envelope.Position.Session == session && envelope.FactId == proposal.FactId && envelope.ThreadScope is null &&
        envelope.Owner == proposal.Owner && envelope.PayloadSchema == proposal.PayloadSchema &&
        envelope.PayloadHash == proposal.PayloadHash && envelope.Payload.SequenceEqual(proposal.Payload) &&
        envelope.Correlation == proposal.Correlation && envelope.ObservedAt == proposal.ObservedAt;
}
