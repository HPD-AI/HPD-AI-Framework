namespace HPD.Agent.Authority;

internal abstract record SemanticAcceptanceProofResultV1
{
    private SemanticAcceptanceProofResultV1() { }

    internal sealed record Proven : SemanticAcceptanceProofResultV1
    {
        internal Proven(SubmissionDispositionChosenV1 claim, AuthorityFactEnvelopeV1 dispositionEnvelope,
            CurrentAuthorityVectorSnapshotV1 current, long snapshotThrough)
        {
            Claim = claim ?? throw new ArgumentNullException(nameof(claim));
            DispositionEnvelope = dispositionEnvelope ?? throw new ArgumentNullException(nameof(dispositionEnvelope));
            Current = current ?? throw new ArgumentNullException(nameof(current));
            var dispositionPosition = dispositionEnvelope.Position;
            if (!dispositionPosition.IsValid || dispositionPosition.Session != current.Session ||
                claim.Authority.Session != current.Session || dispositionPosition.Sequence > snapshotThrough ||
                claim.SourcePosition.Sequence >= dispositionPosition.Sequence || current.ThroughPosition != snapshotThrough ||
                claim.Disposition != SubmissionDispositionV1.SubmissionClaimed || dispositionEnvelope.Owner != OwnerSliceId.S1 ||
                dispositionEnvelope.PayloadSchema != SubmissionDispositionChosenV1Codec.Schema ||
                !SubmissionDispositionChosenV1Codec.TryDecode(dispositionEnvelope.PayloadMemory, out var decoded) || decoded != claim ||
                dispositionEnvelope.PayloadHash != SubmissionDispositionChosenV1Codec.ComputeIntegrityHash(claim))
                throw new ArgumentException("A proof must bind one eligible disposition to its complete current snapshot.");
            SnapshotThrough = snapshotThrough;
        }

        internal SubmissionDispositionChosenV1 Claim { get; }
        internal AuthorityFactEnvelopeV1 DispositionEnvelope { get; }
        internal JournalPositionV1 DispositionPosition => DispositionEnvelope.Position;
        internal CurrentAuthorityVectorSnapshotV1 Current { get; }
        internal long SnapshotThrough { get; }
    }

    internal sealed record AlreadyAccepted : SemanticAcceptanceProofResultV1
    {
        internal AlreadyAccepted(AuthorityFactEnvelopeV1 envelope, SemanticInputAcceptedV1 fact,
            AuthorityFactEnvelopeV1 dispositionEnvelope, SubmissionDispositionChosenV1 claim)
        {
            Envelope = envelope ?? throw new ArgumentNullException(nameof(envelope));
            Fact = fact ?? throw new ArgumentNullException(nameof(fact));
            ArgumentNullException.ThrowIfNull(dispositionEnvelope);
            ArgumentNullException.ThrowIfNull(claim);
            var source = dispositionEnvelope.Correlation;
            var expectedCorrelation = new CorrelationEnvelopeV1(
                source.TenantId, source.PrincipalId, source.SessionId, source.ThreadId,
                source.ParticipantId, claim.OperationId);
            if (envelope.Owner != OwnerSliceId.AgentCore || envelope.PayloadSchema != SemanticInputAcceptedV1Codec.Schema ||
                !SemanticInputAcceptedV1Codec.TryDecode(envelope.PayloadMemory, out var decoded) || decoded != fact ||
                envelope.PayloadHash != SemanticInputAcceptedV1Codec.ComputeIntegrityHash(fact) ||
                envelope.FactId != SemanticInputAcceptedFactIdV1.Derive(envelope.PayloadHash) ||
                envelope.ThreadScope is not null || envelope.Position.Session != dispositionEnvelope.Position.Session ||
                envelope.Position.Sequence <= dispositionEnvelope.Position.Sequence ||
                envelope.Correlation != expectedCorrelation || envelope.ObservedAt != dispositionEnvelope.AdmittedAt ||
                fact.SourcePosition != dispositionEnvelope.Position || fact.OperationId != claim.OperationId ||
                fact.Authority != claim.Authority)
                throw new ArgumentException("An existing acceptance must exactly match its admitted envelope.");
        }
        internal AuthorityFactEnvelopeV1 Envelope { get; }
        internal SemanticInputAcceptedV1 Fact { get; }
    }

    internal sealed record Ineligible(SubmissionDispositionV1 Disposition, long SnapshotThrough) : SemanticAcceptanceProofResultV1;
    internal sealed record StaleAuthority(long SnapshotThrough) : SemanticAcceptanceProofResultV1;
    internal sealed record NotObservedThrough(long SnapshotThrough) : SemanticAcceptanceProofResultV1;
    internal sealed record InvalidHistory(long LastVerifiedPosition, long SnapshotThrough) : SemanticAcceptanceProofResultV1;
    internal sealed record GenerationReplaced(RuntimeGenerationId ReplacedBy, long SnapshotThrough) : SemanticAcceptanceProofResultV1;
    internal sealed record OutcomeUnknown(BoundedAscii SafeCode, long LastVerifiedPosition) : SemanticAcceptanceProofResultV1;
}

internal static class SemanticAcceptanceProofReaderV1
{
    private static readonly SchemaReferenceV1 DispositionSchema = SubmissionDispositionChosenV1Codec.Schema;

    internal static async ValueTask<SemanticAcceptanceProofResultV1> ReadAsync(
        IAuthorityJournalV1 journal,
        JournalPositionV1 dispositionPosition,
        ushort maximumFacts = AppendAuthorityBatchV1.MaximumItems,
        uint maximumEncodedBytes = ProposedAuthorityFactV1.MaximumPayloadBytes,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(journal);
        if (!dispositionPosition.IsValid) throw new ArgumentException("A valid disposition position is required.", nameof(dispositionPosition));
        if (maximumFacts is 0 or > AppendAuthorityBatchV1.MaximumItems) throw new ArgumentOutOfRangeException(nameof(maximumFacts));
        if (maximumEncodedBytes is 0 or > ProposedAuthorityFactV1.MaximumPayloadBytes) throw new ArgumentOutOfRangeException(nameof(maximumEncodedBytes));
        var session = dispositionPosition.Session;
        var accumulator = AuthorityVectorReplayFoldV1.CreateAccumulator(session);
        SubmissionDispositionChosenV1? claim = null;
        AuthorityFactEnvelopeV1? dispositionEnvelope = null;
        var cursor = 0L;
        long? snapshotThrough = null;
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ReadAuthorityRangeResultV1 result;
            try
            {
                result = await journal.ReadAsync(new ReadAuthorityRangeV1(
                    session, cursor, snapshotThrough ?? long.MaxValue, maximumFacts, maximumEncodedBytes), cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
            catch (Exception) { return Unknown("store-exception", accumulator.LastVerifiedPosition); }

            switch (result)
            {
                case ReadAuthorityRangeResultV1.StoreUnavailable unavailable:
                    return new SemanticAcceptanceProofResultV1.OutcomeUnknown(unavailable.SafeCode, accumulator.LastVerifiedPosition);
                case ReadAuthorityRangeResultV1.ItemTooLarge:
                    return Unknown("item-too-large", accumulator.LastVerifiedPosition);
                case ReadAuthorityRangeResultV1.Batch batch:
                    snapshotThrough ??= batch.SnapshotThrough;
                    if (batch.Session != session || batch.AfterExclusive != cursor || batch.SnapshotThrough != snapshotThrough)
                        return Unknown("snapshot-drift", accumulator.LastVerifiedPosition);
                    if (batch.Facts.Count > maximumFacts)
                        return Unknown("count-bound-violated", accumulator.LastVerifiedPosition);
                    foreach (var fact in batch.Facts)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        if (fact.Position.Sequence == dispositionPosition.Sequence)
                        {
                            if (fact.Owner != OwnerSliceId.S1 || fact.PayloadSchema != DispositionSchema ||
                                !SubmissionDispositionChosenV1Codec.TryDecode(fact.PayloadMemory, out claim) || claim is null ||
                                claim.SourcePosition.Sequence >= fact.Position.Sequence)
                                return new SemanticAcceptanceProofResultV1.InvalidHistory(accumulator.LastVerifiedPosition, snapshotThrough.Value);
                            dispositionEnvelope = fact;
                        }
                        if (fact.PayloadSchema == SemanticInputAcceptedV1Codec.Schema)
                        {
                            if (fact.Owner != OwnerSliceId.AgentCore || !SemanticInputAcceptedV1Codec.TryDecode(fact.PayloadMemory, out var accepted) || accepted is null)
                                return new SemanticAcceptanceProofResultV1.InvalidHistory(accumulator.LastVerifiedPosition, snapshotThrough.Value);
                            if (accepted.SourcePosition == dispositionPosition)
                            {
                                if (claim is null || dispositionEnvelope is null ||
                                    accepted.OperationId != claim.OperationId || accepted.Authority != claim.Authority ||
                                    fact.PayloadHash != SemanticInputAcceptedV1Codec.ComputeIntegrityHash(accepted) ||
                                    fact.FactId != SemanticInputAcceptedFactIdV1.Derive(fact.PayloadHash))
                                    return new SemanticAcceptanceProofResultV1.InvalidHistory(accumulator.LastVerifiedPosition, snapshotThrough.Value);
                                var prefix = accumulator.Complete();
                                if (prefix is AuthorityVectorReplayResultV1.InvalidHistory invalidPrefix)
                                    return new SemanticAcceptanceProofResultV1.InvalidHistory(
                                        invalidPrefix.LastPosition, snapshotThrough.Value);
                                if (prefix is AuthorityVectorReplayResultV1.GenerationReplaced replacedPrefix)
                                    return new SemanticAcceptanceProofResultV1.GenerationReplaced(
                                        replacedPrefix.ReplacedBy, snapshotThrough.Value);
                                var currentPrefix = ((AuthorityVectorReplayResultV1.Current)prefix).Snapshot;
                                var currentByAxis = currentPrefix.Axes.ToDictionary(static entry => entry.AxisId);
                                foreach (var expected in claim.Authority.Axes)
                                {
                                    if (!currentByAxis.TryGetValue(expected.AxisId, out var actual) || actual != expected)
                                        return new SemanticAcceptanceProofResultV1.StaleAuthority(snapshotThrough.Value);
                                }
                                try
                                {
                                    return new SemanticAcceptanceProofResultV1.AlreadyAccepted(
                                        fact, accepted, dispositionEnvelope, claim);
                                }
                                catch (ArgumentException)
                                {
                                    return new SemanticAcceptanceProofResultV1.InvalidHistory(
                                        accumulator.LastVerifiedPosition, snapshotThrough.Value);
                                }
                            }
                        }
                        accumulator.Apply(fact);
                        cursor = fact.Position.Sequence;
                    }
                    if (batch.HasMore)
                    {
                        if (batch.Facts.Count == 0) return Unknown("empty-continuation", accumulator.LastVerifiedPosition);
                        break;
                    }
                    if (cursor != snapshotThrough.Value) return Unknown("incomplete-snapshot", accumulator.LastVerifiedPosition);
                    return Evaluate(accumulator.Complete(), claim, dispositionEnvelope, dispositionPosition, snapshotThrough.Value);
                default:
                    return Unknown("unknown-read-result", accumulator.LastVerifiedPosition);
            }
        }
    }

    private static SemanticAcceptanceProofResultV1 Evaluate(
        AuthorityVectorReplayResultV1 replay,
        SubmissionDispositionChosenV1? claim,
        AuthorityFactEnvelopeV1? dispositionEnvelope,
        JournalPositionV1 dispositionPosition,
        long snapshotThrough)
    {
        if (claim is null || dispositionPosition.Sequence > snapshotThrough)
            return new SemanticAcceptanceProofResultV1.NotObservedThrough(snapshotThrough);
        if (replay is AuthorityVectorReplayResultV1.InvalidHistory invalid)
            return new SemanticAcceptanceProofResultV1.InvalidHistory(invalid.LastPosition, snapshotThrough);
        if (replay is AuthorityVectorReplayResultV1.GenerationReplaced replaced)
            return new SemanticAcceptanceProofResultV1.GenerationReplaced(replaced.ReplacedBy, snapshotThrough);
        var current = ((AuthorityVectorReplayResultV1.Current)replay).Snapshot;
        if (claim.Disposition != SubmissionDispositionV1.SubmissionClaimed)
            return new SemanticAcceptanceProofResultV1.Ineligible(claim.Disposition, snapshotThrough);
        var currentByAxis = current.Axes.ToDictionary(static entry => entry.AxisId);
        foreach (var expected in claim.Authority.Axes)
        {
            if (!currentByAxis.TryGetValue(expected.AxisId, out var actual) || actual != expected)
                return new SemanticAcceptanceProofResultV1.StaleAuthority(snapshotThrough);
        }
        return new SemanticAcceptanceProofResultV1.Proven(claim, dispositionEnvelope!, current, snapshotThrough);
    }

    private static SemanticAcceptanceProofResultV1.OutcomeUnknown Unknown(string code, long last) =>
        new(new BoundedAscii(code), last);
}
