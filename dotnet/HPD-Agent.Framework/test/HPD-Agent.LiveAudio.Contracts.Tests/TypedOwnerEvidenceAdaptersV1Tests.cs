using System.Reflection;
using HPD.Agent.Authority;
using Xunit;

namespace HPD.Agent.Audio.Authority;

public sealed class TypedOwnerEvidenceAdaptersV1Tests
{
    [Fact]
    public void Every_S2_through_S8_payload_maps_to_its_exact_registration()
    {
        var session = new SessionAuthorityStampV1(
            RuntimeGenerationId.Create(), LiveSessionId.Create());
        var authority = ExpectedAuthorityVectorV1.Create(session, []);
        var correlation = new CorrelationEnvelopeV1(
            TenantId.Create(), operationId: OperationId.Create());
        var body = new byte[] { 1, 2, 3 };
        var observedAt = new UtcInstant(10);

        var cases = new (ProposedAuthorityFactV1 Proposal,
            AuthorityPayloadRegistrationV1 Registration)[]
        {
            (TypedOwnerEvidenceAdaptersV1.GraphGeneration(
                new GraphGenerationChangedOuterV1(session, authority, body),
                JournalFactId.Create(), null, correlation, observedAt),
                TurnGenerationAuthorityPayloadRegistrationsV1.GraphGenerationChanged),
            (TypedOwnerEvidenceAdaptersV1.VadObservation(
                new VadObservationV1(session, authority, body),
                JournalFactId.Create(), null, correlation, observedAt),
                ActivityAuthorityPayloadRegistrationsV1.VadObservation),
            (TypedOwnerEvidenceAdaptersV1.ActivityBoundary(
                new ActivityBoundaryFactV1(session, authority, body),
                JournalFactId.Create(), null, correlation, observedAt),
                ActivityAuthorityPayloadRegistrationsV1.ActivityBoundaryFact),
            (TypedOwnerEvidenceAdaptersV1.TurnFinalized(
                new TurnDecisionFinalizedOuterV1(session, authority, body),
                JournalFactId.Create(), null, correlation, observedAt),
                TurnGenerationAuthorityPayloadRegistrationsV1.TurnDecisionFinalized),
            (TypedOwnerEvidenceAdaptersV1.SemanticCandidate(
                new SemanticCandidateV1(session, authority, body),
                JournalFactId.Create(), null, correlation, observedAt),
                SemanticCandidateAuthorityPayloadRegistrationV1.Candidate),
            (TypedOwnerEvidenceAdaptersV1.ProviderGeneration(
                new ProviderGenerationChangedOuterV1(session, authority, body),
                JournalFactId.Create(), null, correlation, observedAt),
                TurnGenerationAuthorityPayloadRegistrationsV1.ProviderGenerationChanged),
            (TypedOwnerEvidenceAdaptersV1.ProviderEffectCommand(
                new ProviderEffectCommandV1(session, authority, body),
                JournalFactId.Create(), null, correlation, observedAt),
                ProviderEffectAuthorityPayloadRegistrationsV1.ProviderEffectCommand),
            (TypedOwnerEvidenceAdaptersV1.ProviderEffectReceipt(
                new ProviderEffectReceiptV1(session, authority, body),
                JournalFactId.Create(), null, correlation, observedAt),
                ProviderEffectAuthorityPayloadRegistrationsV1.ProviderEffectReceipt),
            (TypedOwnerEvidenceAdaptersV1.OutputSinkCommand(
                new OutputSinkCommandV1(session, authority, body),
                JournalFactId.Create(), null, correlation, observedAt),
                OutputAuthorityPayloadRegistrationsV1.OutputSinkCommand),
            (TypedOwnerEvidenceAdaptersV1.OutputSinkReceipt(
                new OutputSinkReceiptV1(session, authority, body),
                JournalFactId.Create(), null, correlation, observedAt),
                OutputAuthorityPayloadRegistrationsV1.OutputSinkReceipt),
            (TypedOwnerEvidenceAdaptersV1.HeardRange(
                new HeardRangeFactV1(session, authority, body),
                JournalFactId.Create(), null, correlation, observedAt),
                OutputAuthorityPayloadRegistrationsV1.HeardRangeFact),
            (TypedOwnerEvidenceAdaptersV1.InterruptionCommand(
                new InterruptionCommandV1(session, authority, body),
                JournalFactId.Create(), null, correlation, observedAt),
                InterruptionToolAuthorityPayloadRegistrationsV1.InterruptionCommand),
            (TypedOwnerEvidenceAdaptersV1.InterruptionSettled(
                new InterruptionSettledV1(session, authority, body),
                JournalFactId.Create(), null, correlation, observedAt),
                InterruptionToolAuthorityPayloadRegistrationsV1.InterruptionSettled),
            (TypedOwnerEvidenceAdaptersV1.ToolContinuation(
                new ToolContinuationV1(session, authority, body),
                JournalFactId.Create(), null, correlation, observedAt),
                InterruptionToolAuthorityPayloadRegistrationsV1.ToolContinuation),
            (TypedOwnerEvidenceAdaptersV1.ToolEffectReceipt(
                new ToolEffectReceiptV1(session, authority, body),
                JournalFactId.Create(), null, correlation, observedAt),
                InterruptionToolAuthorityPayloadRegistrationsV1.ToolEffectReceipt),
            (TypedOwnerEvidenceAdaptersV1.RouteGeneration(
                new RouteGenerationChangedOuterV1(session, authority, body),
                JournalFactId.Create(), null, correlation, observedAt),
                TurnGenerationAuthorityPayloadRegistrationsV1.RouteGenerationChanged),
            (TypedOwnerEvidenceAdaptersV1.RouteSelection(
                new RouteSelectionCommandV1(session, authority, body),
                JournalFactId.Create(), null, correlation, observedAt),
                RouteSelectionAuthorityPayloadRegistrationV1.Command),
        };

        Assert.Equal(17, cases.Length);
        Assert.Equal(
            [OwnerSliceId.S2, OwnerSliceId.S3, OwnerSliceId.S4,
             OwnerSliceId.S5, OwnerSliceId.S6, OwnerSliceId.S7, OwnerSliceId.S8],
            cases.Select(value => value.Proposal.Owner).Distinct().Order().ToArray());

        foreach (var (proposal, registration) in cases)
        {
            Assert.Equal(registration.Owner, proposal.Owner);
            Assert.Equal(registration.Schema, proposal.PayloadSchema);
            Assert.Equal(
                AuthorityPayloadHashV1.Compute(
                    registration.SchemaToken, registration.Schema, proposal.PayloadBytes),
                proposal.PayloadHash);

            var admission = new AuthorityPayloadAdmissionRegistryV1([registration])
                .Validate(session, proposal, out var matched);
            Assert.Equal(AuthorityPayloadAdmissionV1.Exact, admission);
            Assert.Same(registration, matched);
        }
    }

    [Fact]
    public void Adapter_is_stateless_owns_bytes_and_exposes_no_journal_or_store_port()
    {
        var session = new SessionAuthorityStampV1(
            RuntimeGenerationId.Create(), LiveSessionId.Create());
        var authority = ExpectedAuthorityVectorV1.Create(session, []);
        var correlation = new CorrelationEnvelopeV1(TenantId.Create());
        var body = new byte[] { 7, 8, 9 };
        var payload = new VadObservationV1(session, authority, body);
        var proposal = TypedOwnerEvidenceAdaptersV1.VadObservation(
            payload, JournalFactId.Create(), null, correlation, new UtcInstant(1));

        body.AsSpan().Fill(0xff);
        Assert.True(ActivityAuthorityPayloadCodecV1.TryDecodeVadObservation(
            proposal.PayloadMemory, out var decoded));
        Assert.Equal(new byte[] { 7, 8, 9 }, decoded!.Body);

        var adapter = typeof(TypedOwnerEvidenceAdaptersV1);
        Assert.Empty(adapter.GetFields(
            BindingFlags.Static | BindingFlags.Instance |
            BindingFlags.Public | BindingFlags.NonPublic));
        Assert.All(adapter.GetMethods(BindingFlags.Static | BindingFlags.NonPublic), method =>
        {
            Assert.DoesNotContain(method.GetParameters(), parameter =>
                typeof(IAuthorityJournalV1).IsAssignableFrom(parameter.ParameterType));
            Assert.DoesNotContain(method.GetParameters(), parameter =>
                parameter.ParameterType.Name.Contains("Store", StringComparison.Ordinal));
        });
    }
}
