using HPD.Agent.Authority;

namespace HPD.Agent.Audio.Authority;

/// <summary>
/// Converts typed S2-S8 owner evidence into neutral S1 journal proposals without
/// retaining state or writing an owner-local journal.
/// </summary>
internal static class TypedOwnerEvidenceAdaptersV1
{
    internal static ProposedAuthorityFactV1 GraphGeneration(
        GraphGenerationChangedOuterV1 value, JournalFactId factId, ThreadId? threadId,
        CorrelationEnvelopeV1 correlation, UtcInstant observedAt) =>
        Create(value.Session, TurnGenerationAuthorityOuterCodecV1.Encode(value),
            TurnGenerationAuthorityPayloadRegistrationsV1.GraphGenerationChanged,
            factId, threadId, correlation, observedAt);

    internal static ProposedAuthorityFactV1 VadObservation(
        VadObservationV1 value, JournalFactId factId, ThreadId? threadId,
        CorrelationEnvelopeV1 correlation, UtcInstant observedAt) =>
        Create(value.Session, ActivityAuthorityPayloadCodecV1.Encode(value),
            ActivityAuthorityPayloadRegistrationsV1.VadObservation,
            factId, threadId, correlation, observedAt);

    internal static ProposedAuthorityFactV1 ActivityBoundary(
        ActivityBoundaryFactV1 value, JournalFactId factId, ThreadId? threadId,
        CorrelationEnvelopeV1 correlation, UtcInstant observedAt) =>
        Create(value.Session, ActivityAuthorityPayloadCodecV1.Encode(value),
            ActivityAuthorityPayloadRegistrationsV1.ActivityBoundaryFact,
            factId, threadId, correlation, observedAt);

    internal static ProposedAuthorityFactV1 TurnFinalized(
        TurnDecisionFinalizedOuterV1 value, JournalFactId factId, ThreadId? threadId,
        CorrelationEnvelopeV1 correlation, UtcInstant observedAt) =>
        Create(value.Session, TurnGenerationAuthorityOuterCodecV1.Encode(value),
            TurnGenerationAuthorityPayloadRegistrationsV1.TurnDecisionFinalized,
            factId, threadId, correlation, observedAt);

    internal static ProposedAuthorityFactV1 SemanticCandidate(
        SemanticCandidateV1 value, JournalFactId factId, ThreadId? threadId,
        CorrelationEnvelopeV1 correlation, UtcInstant observedAt) =>
        Create(value.Session, SemanticCandidateAuthorityPayloadCodecV1.Encode(value),
            SemanticCandidateAuthorityPayloadRegistrationV1.Candidate,
            factId, threadId, correlation, observedAt);

    internal static ProposedAuthorityFactV1 ProviderGeneration(
        ProviderGenerationChangedOuterV1 value, JournalFactId factId, ThreadId? threadId,
        CorrelationEnvelopeV1 correlation, UtcInstant observedAt) =>
        Create(value.Session, TurnGenerationAuthorityOuterCodecV1.Encode(value),
            TurnGenerationAuthorityPayloadRegistrationsV1.ProviderGenerationChanged,
            factId, threadId, correlation, observedAt);

    internal static ProposedAuthorityFactV1 ProviderEffectCommand(
        ProviderEffectCommandV1 value, JournalFactId factId, ThreadId? threadId,
        CorrelationEnvelopeV1 correlation, UtcInstant observedAt) =>
        Create(value.Session, ProviderEffectAuthorityPayloadCodecV1.Encode(value),
            ProviderEffectAuthorityPayloadRegistrationsV1.ProviderEffectCommand,
            factId, threadId, correlation, observedAt);

    internal static ProposedAuthorityFactV1 ProviderEffectReceipt(
        ProviderEffectReceiptV1 value, JournalFactId factId, ThreadId? threadId,
        CorrelationEnvelopeV1 correlation, UtcInstant observedAt) =>
        Create(value.Session, ProviderEffectAuthorityPayloadCodecV1.Encode(value),
            ProviderEffectAuthorityPayloadRegistrationsV1.ProviderEffectReceipt,
            factId, threadId, correlation, observedAt);

    internal static ProposedAuthorityFactV1 OutputSinkCommand(
        OutputSinkCommandV1 value, JournalFactId factId, ThreadId? threadId,
        CorrelationEnvelopeV1 correlation, UtcInstant observedAt) =>
        Create(value.Session, OutputAuthorityPayloadCodecV1.Encode(value),
            OutputAuthorityPayloadRegistrationsV1.OutputSinkCommand,
            factId, threadId, correlation, observedAt);

    internal static ProposedAuthorityFactV1 OutputSinkReceipt(
        OutputSinkReceiptV1 value, JournalFactId factId, ThreadId? threadId,
        CorrelationEnvelopeV1 correlation, UtcInstant observedAt) =>
        Create(value.Session, OutputAuthorityPayloadCodecV1.Encode(value),
            OutputAuthorityPayloadRegistrationsV1.OutputSinkReceipt,
            factId, threadId, correlation, observedAt);

    internal static ProposedAuthorityFactV1 HeardRange(
        HeardRangeFactV1 value, JournalFactId factId, ThreadId? threadId,
        CorrelationEnvelopeV1 correlation, UtcInstant observedAt) =>
        Create(value.Session, OutputAuthorityPayloadCodecV1.Encode(value),
            OutputAuthorityPayloadRegistrationsV1.HeardRangeFact,
            factId, threadId, correlation, observedAt);

    internal static ProposedAuthorityFactV1 InterruptionCommand(
        InterruptionCommandV1 value, JournalFactId factId, ThreadId? threadId,
        CorrelationEnvelopeV1 correlation, UtcInstant observedAt) =>
        Create(value.Session, InterruptionToolAuthorityPayloadCodecV1.Encode(value),
            InterruptionToolAuthorityPayloadRegistrationsV1.InterruptionCommand,
            factId, threadId, correlation, observedAt);

    internal static ProposedAuthorityFactV1 InterruptionSettled(
        InterruptionSettledV1 value, JournalFactId factId, ThreadId? threadId,
        CorrelationEnvelopeV1 correlation, UtcInstant observedAt) =>
        Create(value.Session, InterruptionToolAuthorityPayloadCodecV1.Encode(value),
            InterruptionToolAuthorityPayloadRegistrationsV1.InterruptionSettled,
            factId, threadId, correlation, observedAt);

    internal static ProposedAuthorityFactV1 ToolContinuation(
        ToolContinuationV1 value, JournalFactId factId, ThreadId? threadId,
        CorrelationEnvelopeV1 correlation, UtcInstant observedAt) =>
        Create(value.Session, InterruptionToolAuthorityPayloadCodecV1.Encode(value),
            InterruptionToolAuthorityPayloadRegistrationsV1.ToolContinuation,
            factId, threadId, correlation, observedAt);

    internal static ProposedAuthorityFactV1 ToolEffectReceipt(
        ToolEffectReceiptV1 value, JournalFactId factId, ThreadId? threadId,
        CorrelationEnvelopeV1 correlation, UtcInstant observedAt) =>
        Create(value.Session, InterruptionToolAuthorityPayloadCodecV1.Encode(value),
            InterruptionToolAuthorityPayloadRegistrationsV1.ToolEffectReceipt,
            factId, threadId, correlation, observedAt);

    internal static ProposedAuthorityFactV1 RouteGeneration(
        RouteGenerationChangedOuterV1 value, JournalFactId factId, ThreadId? threadId,
        CorrelationEnvelopeV1 correlation, UtcInstant observedAt) =>
        Create(value.Session, TurnGenerationAuthorityOuterCodecV1.Encode(value),
            TurnGenerationAuthorityPayloadRegistrationsV1.RouteGenerationChanged,
            factId, threadId, correlation, observedAt);

    internal static ProposedAuthorityFactV1 RouteSelection(
        RouteSelectionCommandV1 value, JournalFactId factId, ThreadId? threadId,
        CorrelationEnvelopeV1 correlation, UtcInstant observedAt) =>
        Create(value.Session, RouteSelectionAuthorityPayloadCodecV1.Encode(value),
            RouteSelectionAuthorityPayloadRegistrationV1.Command,
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
