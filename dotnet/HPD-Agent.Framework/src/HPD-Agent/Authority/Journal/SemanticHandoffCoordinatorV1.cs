namespace HPD.Agent.Authority;

internal abstract record SemanticHandoffResultV1
{
    private SemanticHandoffResultV1() { }
    internal sealed record Bound(JournalPositionV1 DecisionPosition, JournalPositionV1 ReservationPosition,
        JournalPositionV1 DispositionPosition, JournalPositionV1 AcceptancePosition,
        JournalPositionV1 BindingPosition) : SemanticHandoffResultV1;
    internal sealed record RetryRequired(long ObservedHead) : SemanticHandoffResultV1;
    internal sealed record Ineligible(BoundedAscii SafeCode) : SemanticHandoffResultV1;
    internal sealed record InvalidHistory(BoundedAscii SafeCode) : SemanticHandoffResultV1;
    internal sealed record OutcomeUnknown(BoundedAscii SafeCode) : SemanticHandoffResultV1;
}

internal static class SemanticHandoffCoordinatorV1
{
    // One bounded replay/append opportunity is required for each of L1, L2, L3, and L4.
    // Two additional passes close acknowledgement loss and a single competing-head race.
    private const int MaximumAttempts = 6;
    private const int MaximumReadPasses = 10;
    private const ushort MaximumFacts = AppendAuthorityBatchV1.MaximumItems;
    private const uint MaximumBytes = ProposedAuthorityFactV1.MaximumPayloadBytes;
    private const uint MaximumAppendBytes = 16_384;
    private static readonly SchemaReferenceV1 DecisionSchema = new(
        AuthoritySchemaIdentityV1.Derive(new BoundedAscii("hpd.authority-payload-turn-decision-finalized.v1")), 1, 0);

    internal static async ValueTask<SemanticHandoffResultV1> BindAsync(IAuthorityJournalV1 journal,
        JournalPositionV1 decisionPosition, OperationId operationId, ExpectedAuthorityVectorV1 authority,
        CorrelationEnvelopeV1 correlation, UtcInstant observedAt, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(journal);
        if (!decisionPosition.IsValid || !operationId.IsValid || authority is null ||
            authority.Session != decisionPosition.Session || correlation.OperationId != operationId)
            throw new ArgumentException("Semantic handoff inputs must bind one valid decision and operation.");

        for (var attempt = 0; attempt < MaximumAttempts; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var read = await ReadAsync(journal, decisionPosition.Session, cancellationToken).ConfigureAwait(false);
            if (read.Result is not null) return read.Result;
            var projection = Project(read.Facts!, decisionPosition, operationId, authority);
            if (projection.Result is not null) return projection.Result;
            if (projection.Binding is not null)
                return new SemanticHandoffResultV1.Bound(decisionPosition, projection.Reservation!.Position,
                    projection.Disposition!.Position, projection.Acceptance!.Position, projection.Binding.Position);

            if (projection.Reservation is null)
            {
                var reservation = new SemanticReservationCreatedV1(operationId, decisionPosition, authority, 1);
                var registration = new SemanticReservationCreatedPayloadRegistrationV1();
                var appended = await AppendAsync(journal, decisionPosition.Session, read.Head,
                    Proposal(registration, SemanticHandoffFactIdsV1.Reservation(CoreLifecycleRecordCodecsV1.ComputeHash(reservation)),
                        CoreLifecycleRecordCodecsV1.Encode(reservation), correlation, observedAt),
                    decisionPosition, operationId, authority, cancellationToken).ConfigureAwait(false);
                if (appended is not null) return appended;
                continue;
            }
            if (projection.Disposition is null)
            {
                var disposition = new SubmissionDispositionChosenV1(operationId, projection.Reservation.Position,
                    authority, SubmissionDispositionV1.SubmissionClaimed);
                var registration = new SubmissionDispositionChosenPayloadRegistrationV1();
                var hash = SubmissionDispositionChosenV1Codec.ComputeIntegrityHash(disposition);
                var appended = await AppendAsync(journal, decisionPosition.Session, read.Head,
                    Proposal(registration, SemanticHandoffFactIdsV1.Disposition(hash),
                        SubmissionDispositionChosenV1Codec.Encode(disposition), correlation, observedAt),
                    decisionPosition, operationId, authority, cancellationToken).ConfigureAwait(false);
                if (appended is not null) return appended;
                continue;
            }
            if (projection.Acceptance is null)
            {
                var admitted = await SemanticAcceptanceAdmissionV1.AdmitAsync(
                    journal, projection.Disposition.Position, cancellationToken).ConfigureAwait(false);
                switch (admitted)
                {
                    case SemanticAcceptanceAdmissionResultV1.Committed:
                    case SemanticAcceptanceAdmissionResultV1.AlreadyCommitted:
                        continue;
                    case SemanticAcceptanceAdmissionResultV1.RetryRequired retry:
                        if (attempt + 1 == MaximumAttempts) return new SemanticHandoffResultV1.RetryRequired(retry.ObservedHead);
                        continue;
                    case SemanticAcceptanceAdmissionResultV1.ProofRejected rejected:
                        return new SemanticHandoffResultV1.Ineligible(new BoundedAscii(rejected.Proof.GetType().Name));
                    case SemanticAcceptanceAdmissionResultV1.ContradictoryDuplicate:
                        return new SemanticHandoffResultV1.InvalidHistory(new BoundedAscii("acceptance-contradictory-duplicate"));
                    case SemanticAcceptanceAdmissionResultV1.Rejected rejected:
                        return new SemanticHandoffResultV1.Ineligible(rejected.SafeCode);
                    case SemanticAcceptanceAdmissionResultV1.OutcomeUnknown unknown:
                        return new SemanticHandoffResultV1.OutcomeUnknown(unknown.SafeCode);
                }
            }
            else if (projection.Binding is null)
            {
                var binding = new SemanticAcceptanceBoundV1(operationId, projection.Acceptance.Position, authority, 1);
                var registration = new SemanticAcceptanceBoundPayloadRegistrationV1();
                var appended = await AppendAsync(journal, decisionPosition.Session, read.Head,
                    Proposal(registration, SemanticHandoffFactIdsV1.AcceptanceBinding(CoreLifecycleRecordCodecsV1.ComputeHash(binding)),
                        CoreLifecycleRecordCodecsV1.Encode(binding), correlation, observedAt),
                    decisionPosition, operationId, authority, cancellationToken).ConfigureAwait(false);
                if (appended is not null) return appended;
                continue;
            }
        }
        var finalRead = await ReadAsync(journal, decisionPosition.Session, CancellationToken.None).ConfigureAwait(false);
        if (finalRead.Result is not null) return finalRead.Result;
        var final = Project(finalRead.Facts!, decisionPosition, operationId, authority);
        if (final.Result is not null) return final.Result;
        return final.Binding is not null
            ? new SemanticHandoffResultV1.Bound(decisionPosition, final.Reservation!.Position,
                final.Disposition!.Position, final.Acceptance!.Position, final.Binding.Position)
            : new SemanticHandoffResultV1.RetryRequired(finalRead.Head);
    }

    private static async ValueTask<SemanticHandoffResultV1?> AppendAsync(IAuthorityJournalV1 journal,
        SessionAuthorityStampV1 session, long expectedHead, ProposedAuthorityFactV1 proposal,
        JournalPositionV1 decisionPosition, OperationId operation, ExpectedAuthorityVectorV1 authority,
        CancellationToken cancellationToken)
    {
        AppendAuthorityResultV1 result;
        try
        {
            result = await journal.AppendAsync(new AppendAuthorityBatchV1(session, expectedHead, [], [proposal], MaximumAppendBytes),
                cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            var reconciled = await ReconcileAsync(journal, decisionPosition, operation, authority, proposal.FactId).ConfigureAwait(false);
            if (reconciled) return null;
            throw;
        }
        catch (Exception)
        {
            return await ReconcileAsync(journal, decisionPosition, operation, authority, proposal.FactId).ConfigureAwait(false)
                ? null : new SemanticHandoffResultV1.OutcomeUnknown(new BoundedAscii("append-exception"));
        }
        return result switch
        {
            AppendAuthorityResultV1.Committed committed when committed.PreviousHead == expectedHead &&
                committed.Envelopes.Count == 1 && Matches(committed.Envelopes[0], proposal, session) => null,
            AppendAuthorityResultV1.AlreadyCommitted existing when existing.Envelopes.Count == 1 &&
                Matches(existing.Envelopes[0], proposal, session) => null,
            AppendAuthorityResultV1.SessionConflict => null,
            AppendAuthorityResultV1.OutcomeUnknown => await ReconcileAsync(journal, decisionPosition, operation, authority, proposal.FactId).ConfigureAwait(false)
                ? null : new SemanticHandoffResultV1.OutcomeUnknown(new BoundedAscii("append-outcome-unknown")),
            AppendAuthorityResultV1.StoreUnavailable unavailable => new SemanticHandoffResultV1.OutcomeUnknown(unavailable.SafeCode),
            AppendAuthorityResultV1.InvalidPayload invalid => new SemanticHandoffResultV1.Ineligible(invalid.SafeCode),
            AppendAuthorityResultV1.UnknownSchema => new SemanticHandoffResultV1.Ineligible(new BoundedAscii("unknown-schema")),
            AppendAuthorityResultV1.CapacityRefused => new SemanticHandoffResultV1.Ineligible(new BoundedAscii("capacity-refused")),
            AppendAuthorityResultV1.ContradictoryDuplicate => new SemanticHandoffResultV1.InvalidHistory(new BoundedAscii("contradictory-duplicate")),
            _ => new SemanticHandoffResultV1.OutcomeUnknown(new BoundedAscii("append-result-invalid")),
        };
    }

    private static async ValueTask<bool> ReconcileAsync(IAuthorityJournalV1 journal,
        JournalPositionV1 decision, OperationId operation, ExpectedAuthorityVectorV1 authority, JournalFactId factId)
    {
        var read = await ReadAsync(journal, decision.Session, CancellationToken.None).ConfigureAwait(false);
        if (read.Result is not null) return false;
        var projected = Project(read.Facts!, decision, operation, authority);
        return projected.Result is null && read.Facts!.Any(fact => fact.FactId == factId);
    }

    private static async ValueTask<(IReadOnlyList<AuthorityFactEnvelopeV1>? Facts, long Head, SemanticHandoffResultV1? Result)> ReadAsync(
        IAuthorityJournalV1 journal, SessionAuthorityStampV1 session, CancellationToken cancellationToken)
    {
        var facts = new List<AuthorityFactEnvelopeV1>();
        var cursor = 0L;
        long? through = null;
        for (var pass = 0; pass < MaximumReadPasses; pass++)
        {
            ReadAuthorityRangeResultV1 result;
            try
            {
                result = await journal.ReadAsync(new ReadAuthorityRangeV1(session, cursor, through ?? long.MaxValue,
                    MaximumFacts, MaximumBytes), cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
            catch (Exception) { return (null, cursor, new SemanticHandoffResultV1.OutcomeUnknown(new BoundedAscii("read-exception"))); }
            if (result is ReadAuthorityRangeResultV1.StoreUnavailable unavailable)
                return (null, cursor, new SemanticHandoffResultV1.OutcomeUnknown(unavailable.SafeCode));
            if (result is not ReadAuthorityRangeResultV1.Batch batch || batch.Session != session || batch.AfterExclusive != cursor ||
                batch.Facts.Count > MaximumFacts)
                return (null, cursor, new SemanticHandoffResultV1.InvalidHistory(new BoundedAscii("read-result-invalid")));
            through ??= batch.SnapshotThrough;
            if (through != batch.SnapshotThrough)
                return (null, cursor, new SemanticHandoffResultV1.OutcomeUnknown(new BoundedAscii("snapshot-drift")));
            facts.AddRange(batch.Facts);
            if (batch.Facts.Count > 0) cursor = batch.Facts[^1].Position.Sequence;
            if (!batch.HasMore)
                return cursor == through ? (facts, through.Value, null)
                    : (null, cursor, new SemanticHandoffResultV1.InvalidHistory(new BoundedAscii("snapshot-incomplete")));
            if (batch.Facts.Count == 0)
                return (null, cursor, new SemanticHandoffResultV1.OutcomeUnknown(new BoundedAscii("empty-continuation")));
        }
        return (null, cursor, new SemanticHandoffResultV1.OutcomeUnknown(new BoundedAscii("read-pass-bound")));
    }

    private static Projection Project(IReadOnlyList<AuthorityFactEnvelopeV1> facts, JournalPositionV1 decision,
        OperationId operation, ExpectedAuthorityVectorV1 authority)
    {
        var decisionEnvelope = facts.SingleOrDefault(fact => fact.Position == decision);
        if (decisionEnvelope is null || decisionEnvelope.Owner != OwnerSliceId.S4 || decisionEnvelope.PayloadSchema != DecisionSchema ||
            decisionEnvelope.Correlation.OperationId != operation)
            return new(null, null, null, null, new SemanticHandoffResultV1.InvalidHistory(new BoundedAscii("decision-proof-invalid")));
        AuthorityFactEnvelopeV1? reservation = null, disposition = null, acceptance = null, binding = null;
        foreach (var fact in facts)
        {
            if (fact.PayloadSchema == CoreLifecycleRecordCodecsV1.ReservationSchema &&
                CoreLifecycleRecordCodecsV1.TryDecodeReservation(fact.PayloadMemory, out var value) && value!.OperationId == operation)
            {
                if (reservation is not null || fact.Owner != OwnerSliceId.S1 || value.SourcePosition != decision || value.Authority != authority ||
                    fact.PayloadHash != CoreLifecycleRecordCodecsV1.ComputeHash(value) ||
                    fact.FactId != SemanticHandoffFactIdsV1.Reservation(fact.PayloadHash))
                    return Invalid("reservation-invalid");
                reservation = fact;
            }
            else if (fact.PayloadSchema == SubmissionDispositionChosenV1Codec.Schema &&
                SubmissionDispositionChosenV1Codec.TryDecode(fact.PayloadMemory, out var chosen) && chosen!.OperationId == operation)
            {
                if (disposition is not null || reservation is null || fact.Owner != OwnerSliceId.S1 || chosen.SourcePosition != reservation.Position ||
                    chosen.Authority != authority || chosen.Disposition != SubmissionDispositionV1.SubmissionClaimed ||
                    fact.PayloadHash != SubmissionDispositionChosenV1Codec.ComputeIntegrityHash(chosen) ||
                    fact.FactId != SemanticHandoffFactIdsV1.Disposition(fact.PayloadHash))
                    return Invalid("disposition-invalid");
                disposition = fact;
            }
            else if (fact.PayloadSchema == SemanticInputAcceptedV1Codec.Schema &&
                SemanticInputAcceptedV1Codec.TryDecode(fact.PayloadMemory, out var accepted) && accepted!.OperationId == operation)
            {
                if (acceptance is not null || disposition is null || fact.Owner != OwnerSliceId.AgentCore ||
                    accepted.SourcePosition != disposition.Position || accepted.Authority != authority ||
                    fact.PayloadHash != SemanticInputAcceptedV1Codec.ComputeIntegrityHash(accepted) ||
                    fact.FactId != SemanticInputAcceptedFactIdV1.Derive(fact.PayloadHash))
                    return Invalid("acceptance-invalid");
                acceptance = fact;
            }
            else if (fact.PayloadSchema == CoreLifecycleRecordCodecsV1.AcceptanceSchema &&
                CoreLifecycleRecordCodecsV1.TryDecodeAcceptance(fact.PayloadMemory, out var bound) && bound!.OperationId == operation)
            {
                if (binding is not null || acceptance is null || fact.Owner != OwnerSliceId.S1 || bound.SourcePosition != acceptance.Position ||
                    bound.Authority != authority || fact.PayloadHash != CoreLifecycleRecordCodecsV1.ComputeHash(bound) ||
                    fact.FactId != SemanticHandoffFactIdsV1.AcceptanceBinding(fact.PayloadHash))
                    return Invalid("binding-invalid");
                binding = fact;
            }
        }
        return new(reservation, disposition, acceptance, binding, null);

        Projection Invalid(string code) => new(null, null, null, null,
            new SemanticHandoffResultV1.InvalidHistory(new BoundedAscii(code)));
    }

    private static ProposedAuthorityFactV1 Proposal(AuthorityPayloadRegistrationV1 registration,
        JournalFactId factId, byte[] payload, CorrelationEnvelopeV1 correlation, UtcInstant observedAt) =>
        new(factId, null, registration.Owner, registration.Schema, payload,
            AuthorityPayloadHashV1.Compute(registration.SchemaToken, registration.Schema, payload), correlation, observedAt);

    private static bool Matches(AuthorityFactEnvelopeV1 envelope, ProposedAuthorityFactV1 proposal, SessionAuthorityStampV1 session) =>
        envelope.Position.Session == session && envelope.FactId == proposal.FactId && envelope.ThreadScope is null &&
        envelope.Owner == proposal.Owner && envelope.PayloadSchema == proposal.PayloadSchema &&
        envelope.PayloadHash == proposal.PayloadHash && envelope.Payload.SequenceEqual(proposal.Payload) &&
        envelope.Correlation == proposal.Correlation && envelope.ObservedAt == proposal.ObservedAt;

    private sealed record Projection(AuthorityFactEnvelopeV1? Reservation, AuthorityFactEnvelopeV1? Disposition,
        AuthorityFactEnvelopeV1? Acceptance, AuthorityFactEnvelopeV1? Binding, SemanticHandoffResultV1? Result);
}
