using HPD.Agent.Authority;

namespace HPD.Agent.Audio.Authority;

/// <summary>
/// Converts typed S2-S8 owner evidence into neutral S1 journal proposals without
/// retaining state or writing an owner-local journal.
/// </summary>
internal static class TypedOwnerEvidenceAdapters
{
    internal static ProposedAuthorityFactV1 GraphGeneration(
        GraphGenerationChangedOuter value, JournalFactId factId, ThreadId? threadId,
        CorrelationEnvelopeV1 correlation, UtcInstant observedAt) =>
        Create(value.Session, TurnGenerationAuthorityOuterCodec.Encode(value),
            TurnGenerationAuthorityPayloadRegistrations.GraphGenerationChanged,
            factId, threadId, correlation, observedAt);

    internal static ProposedAuthorityFactV1 VadObservation(
        VadObservation value, JournalFactId factId, ThreadId? threadId,
        CorrelationEnvelopeV1 correlation, UtcInstant observedAt) =>
        Create(value.Session, ActivityAuthorityPayloadCodec.Encode(value),
            ActivityAuthorityPayloadRegistrations.VadObservation,
            factId, threadId, correlation, observedAt);

    internal static ProposedAuthorityFactV1 ActivityBoundary(
        ActivityBoundaryFact value, JournalFactId factId, ThreadId? threadId,
        CorrelationEnvelopeV1 correlation, UtcInstant observedAt) =>
        Create(value.Session, ActivityAuthorityPayloadCodec.Encode(value),
            ActivityAuthorityPayloadRegistrations.ActivityBoundaryFact,
            factId, threadId, correlation, observedAt);

    internal static ProposedAuthorityFactV1 TurnFinalized(
        TurnDecisionFinalizedOuter value, JournalFactId factId, ThreadId? threadId,
        CorrelationEnvelopeV1 correlation, UtcInstant observedAt) =>
        Create(value.Session, TurnGenerationAuthorityOuterCodec.Encode(value),
            TurnGenerationAuthorityPayloadRegistrations.TurnDecisionFinalized,
            factId, threadId, correlation, observedAt);

    internal static ProposedAuthorityFactV1 SemanticCandidate(
        SemanticCandidate value, JournalFactId factId, ThreadId? threadId,
        CorrelationEnvelopeV1 correlation, UtcInstant observedAt) =>
        Create(value.Session, SemanticCandidateAuthorityPayloadCodec.Encode(value),
            SemanticCandidateAuthorityPayloadRegistration.Candidate,
            factId, threadId, correlation, observedAt);

    internal static ProposedAuthorityFactV1 ProviderGeneration(
        ProviderGenerationChangedOuter value, JournalFactId factId, ThreadId? threadId,
        CorrelationEnvelopeV1 correlation, UtcInstant observedAt) =>
        Create(value.Session, TurnGenerationAuthorityOuterCodec.Encode(value),
            TurnGenerationAuthorityPayloadRegistrations.ProviderGenerationChanged,
            factId, threadId, correlation, observedAt);

    internal static ProposedAuthorityFactV1 ProviderEffectCommand(
        ProviderEffectCommand value, JournalFactId factId, ThreadId? threadId,
        CorrelationEnvelopeV1 correlation, UtcInstant observedAt) =>
        Create(value.Session, ProviderEffectAuthorityPayloadCodec.Encode(value),
            ProviderEffectAuthorityPayloadRegistrations.ProviderEffectCommand,
            factId, threadId, correlation, observedAt);

    internal static ProposedAuthorityFactV1 ProviderEffectReceipt(
        ProviderEffectReceipt value, JournalFactId factId, ThreadId? threadId,
        CorrelationEnvelopeV1 correlation, UtcInstant observedAt) =>
        Create(value.Session, ProviderEffectAuthorityPayloadCodec.Encode(value),
            ProviderEffectAuthorityPayloadRegistrations.ProviderEffectReceipt,
            factId, threadId, correlation, observedAt);

    internal static ProposedAuthorityFactV1 OutputSinkCommand(
        OutputSinkCommand value, JournalFactId factId, ThreadId? threadId,
        CorrelationEnvelopeV1 correlation, UtcInstant observedAt) =>
        Create(value.Session, OutputAuthorityPayloadCodec.Encode(value),
            OutputAuthorityPayloadRegistrations.OutputSinkCommand,
            factId, threadId, correlation, observedAt);

    internal static ProposedAuthorityFactV1 OutputSinkReceipt(
        OutputSinkReceipt value, JournalFactId factId, ThreadId? threadId,
        CorrelationEnvelopeV1 correlation, UtcInstant observedAt) =>
        Create(value.Session, OutputAuthorityPayloadCodec.Encode(value),
            OutputAuthorityPayloadRegistrations.OutputSinkReceipt,
            factId, threadId, correlation, observedAt);

    internal static ProposedAuthorityFactV1 HeardRange(
        HeardRangeFact value, JournalFactId factId, ThreadId? threadId,
        CorrelationEnvelopeV1 correlation, UtcInstant observedAt) =>
        Create(value.Session, OutputAuthorityPayloadCodec.Encode(value),
            OutputAuthorityPayloadRegistrations.HeardRangeFact,
            factId, threadId, correlation, observedAt);

    internal static ProposedAuthorityFactV1 InterruptionCommand(
        InterruptionCommand value, JournalFactId factId, ThreadId? threadId,
        CorrelationEnvelopeV1 correlation, UtcInstant observedAt) =>
        Create(value.Session, InterruptionToolAuthorityPayloadCodec.Encode(value),
            InterruptionToolAuthorityPayloadRegistrations.InterruptionCommand,
            factId, threadId, correlation, observedAt);

    internal static ProposedAuthorityFactV1 InterruptionSettled(
        InterruptionSettled value, JournalFactId factId, ThreadId? threadId,
        CorrelationEnvelopeV1 correlation, UtcInstant observedAt) =>
        Create(value.Session, InterruptionToolAuthorityPayloadCodec.Encode(value),
            InterruptionToolAuthorityPayloadRegistrations.InterruptionSettled,
            factId, threadId, correlation, observedAt);

    internal static ProposedAuthorityFactV1 ToolContinuation(
        ToolContinuation value, JournalFactId factId, ThreadId? threadId,
        CorrelationEnvelopeV1 correlation, UtcInstant observedAt) =>
        Create(value.Session, InterruptionToolAuthorityPayloadCodec.Encode(value),
            InterruptionToolAuthorityPayloadRegistrations.ToolContinuation,
            factId, threadId, correlation, observedAt);

    internal static ProposedAuthorityFactV1 ToolEffectReceipt(
        ToolEffectReceipt value, JournalFactId factId, ThreadId? threadId,
        CorrelationEnvelopeV1 correlation, UtcInstant observedAt) =>
        Create(value.Session, InterruptionToolAuthorityPayloadCodec.Encode(value),
            InterruptionToolAuthorityPayloadRegistrations.ToolEffectReceipt,
            factId, threadId, correlation, observedAt);

    internal static ProposedAuthorityFactV1 RouteGeneration(
        RouteGenerationChangedOuter value, JournalFactId factId, ThreadId? threadId,
        CorrelationEnvelopeV1 correlation, UtcInstant observedAt) =>
        Create(value.Session, TurnGenerationAuthorityOuterCodec.Encode(value),
            TurnGenerationAuthorityPayloadRegistrations.RouteGenerationChanged,
            factId, threadId, correlation, observedAt);

    internal static ProposedAuthorityFactV1 RouteSelection(
        RouteSelectionCommand value, JournalFactId factId, ThreadId? threadId,
        CorrelationEnvelopeV1 correlation, UtcInstant observedAt) =>
        Create(value.Session, RouteSelectionAuthorityPayloadCodec.Encode(value),
            RouteSelectionAuthorityPayloadRegistration.Command,
            factId, threadId, correlation, observedAt);

    private static ProposedAuthorityFactV1 Create(
        SessionAuthorityStampV1 session,
        byte[] payload,
        AuthorityPayloadRegistrationV1 registration,
        JournalFactId factId,
        ThreadId? threadId,
        CorrelationEnvelopeV1 correlation,
        UtcInstant observedAt)
    {
        ArgumentNullException.ThrowIfNull(payload);
        ArgumentNullException.ThrowIfNull(registration);

        var proposal = new ProposedAuthorityFactV1(
            factId,
            threadId,
            registration.Owner,
            registration.Schema,
            payload,
            AuthorityPayloadHashV1.Compute(
                registration.SchemaToken,
                registration.Schema,
                payload),
            correlation,
            observedAt);

        var admission = new AuthorityPayloadAdmissionRegistryV1([registration])
            .Validate(session, proposal, out var matched);
        if (admission != AuthorityPayloadAdmissionV1.Exact ||
            !ReferenceEquals(matched, registration))
        {
            throw new InvalidOperationException(
                "The typed owner payload did not validate against its exact registration.");
        }

        return proposal;
    }
}
