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

    [Fact]
    public async Task Typed_proposal_survives_crash_conflict_retry_replay_and_stale_fencing()
    {
        var directory = Path.Combine(Path.GetTempPath(),
            "hpd-owner-adapter-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "authority.log");
        FileAuthorityJournalV1? journal = null;
        try
        {
            var session = new SessionAuthorityStampV1(
                RuntimeGenerationId.Create(), LiveSessionId.Create());
            var authority = ExpectedAuthorityVectorV1.Create(session, []);
            var correlation = new CorrelationEnvelopeV1(
                TenantId.Create(), operationId: OperationId.Create());
            var registration = ActivityAuthorityPayloadRegistrationsV1.VadObservation;
            var runtimeRegistration =
                new AuthorityGenerationTransitionPayloadRegistrationV1(AuthorityAxisId.Runtime);
            var registry = new AuthorityPayloadAdmissionRegistryV1(
                [registration, runtimeRegistration]);
            var capacity = new AuthorityJournalCapacityV1(4, 32, 4 * 1024 * 1024);

            var proposal = TypedOwnerEvidenceAdaptersV1.VadObservation(
                new VadObservationV1(session, authority, [1, 2, 3]),
                JournalFactId.Create(), null, correlation, new UtcInstant(10));
            AppendAuthorityBatchV1 Batch(long head, ProposedAuthorityFactV1 fact) =>
                new(session, head, [], [fact], ProposedAuthorityFactV1.MaximumPayloadBytes);
            ValueTask<FileAuthorityJournalV1> Open() =>
                FileAuthorityJournalV1.OpenAsync(path, registry,
                    static () => new UtcInstant(20), capacity);
            ValueTask<FileAuthorityJournalV1> OpenWithFault(string stage) =>
                FileAuthorityJournalV1.OpenForTestingAsync(path, registry,
                    static () => new UtcInstant(20), capacity,
                    value => value.ToString() == stage
                        ? ValueTask.FromException(new IOException("injected-owner-adapter-crash"))
                        : ValueTask.CompletedTask);

            journal = await OpenWithFault("before-record-write");
            await Assert.ThrowsAsync<IOException>(async () =>
                await journal.AppendAsync(Batch(0, proposal)));
            await journal.DisposeAsync();
            journal = await Open();
            var before = Assert.IsType<ReadAuthorityRangeResultV1.Batch>(
                await journal.ReadAsync(new ReadAuthorityRangeV1(
                    session, 0, long.MaxValue, 32, ProposedAuthorityFactV1.MaximumPayloadBytes)));
            Assert.Empty(before.Facts);
            await journal.DisposeAsync();

            journal = await OpenWithFault("after-record-flush");
            await Assert.ThrowsAsync<IOException>(async () =>
                await journal.AppendAsync(Batch(0, proposal)));
            await journal.DisposeAsync();
            journal = await Open();
            var after = Assert.IsType<ReadAuthorityRangeResultV1.Batch>(
                await journal.ReadAsync(new ReadAuthorityRangeV1(
                    session, 0, long.MaxValue, 32, ProposedAuthorityFactV1.MaximumPayloadBytes)));
            var replayed = Assert.Single(after.Facts);
            Assert.Equal(proposal.FactId, replayed.FactId);
            Assert.Equal(proposal.Payload, replayed.Payload);

            var retry = Assert.IsType<AppendAuthorityResultV1.AlreadyCommitted>(
                await journal.AppendAsync(Batch(0, proposal)));
            Assert.Equal(replayed.FactId, Assert.Single(retry.Envelopes).FactId);

            var competing = TypedOwnerEvidenceAdaptersV1.VadObservation(
                new VadObservationV1(session, authority, [4, 5, 6]),
                JournalFactId.Create(), null, correlation, new UtcInstant(11));
            Assert.IsType<AppendAuthorityResultV1.SessionConflict>(
                await journal.AppendAsync(Batch(0, competing)));

            Span<byte> currentBytes = stackalloc byte[16];
            Assert.True(session.RuntimeGenerationId.TryWriteBytes(currentBytes));
            var nextRuntime = RuntimeGenerationId.Create();
            Span<byte> nextBytes = stackalloc byte[16];
            Assert.True(nextRuntime.TryWriteBytes(nextBytes));
            var transitionPayload = AuthorityGenerationTransitionCodecV1.Encode(
                session, AuthorityAxisId.Runtime, StableId128.FromBytes(currentBytes),
                StableId128.FromBytes(nextBytes));
            var transition = new ProposedAuthorityFactV1(
                JournalFactId.Create(), null, runtimeRegistration.Owner,
                runtimeRegistration.Schema, transitionPayload,
                AuthorityPayloadHashV1.Compute(runtimeRegistration.SchemaToken,
                    runtimeRegistration.Schema, transitionPayload), correlation,
                new UtcInstant(12));
            Assert.IsType<AppendAuthorityResultV1.Committed>(
                await journal.AppendAsync(Batch(1, transition)));
            await journal.DisposeAsync();
            journal = await Open();

            var stale = TypedOwnerEvidenceAdaptersV1.VadObservation(
                new VadObservationV1(session, authority, [7, 8, 9]),
                JournalFactId.Create(), null, correlation, new UtcInstant(13));
            var staleResult = Assert.IsType<AppendAuthorityResultV1.InvalidPayload>(
                await journal.AppendAsync(Batch(2, stale)));
            Assert.Equal("generation-state-conflict", staleResult.SafeCode.ToString());
        }
        finally
        {
            if (journal is not null) await journal.DisposeAsync();
            try { Directory.Delete(directory, recursive: true); }
            catch (DirectoryNotFoundException) { }
        }
    }
}
