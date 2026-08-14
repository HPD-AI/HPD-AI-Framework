using System.Security.Cryptography;
using HPD.Agent.Authority;

namespace HPD.Agent.Audio.Graph;

internal sealed record GraphMediaPhysicalReleaseEffectRequestV1(
    OperationId OperationId, StableId128 ResidenceId, Hash256 ResidenceRequestHash,
    CapacityGrantId GrantId, JournalPositionV1 CurrentFact,
    GraphMediaCapacityAssignmentV1 Assignment);

internal abstract record GraphMediaPhysicalReleaseEffectResultV1
{
    private GraphMediaPhysicalReleaseEffectResultV1() { }
    internal sealed record Released(Hash256 EvidenceHash) : GraphMediaPhysicalReleaseEffectResultV1;
    internal sealed record Unknown : GraphMediaPhysicalReleaseEffectResultV1;
    internal sealed record Rejected(BoundedAscii SafeCode) : GraphMediaPhysicalReleaseEffectResultV1;
}

internal abstract record GraphMediaPhysicalReleaseEffectQueryResultV1
{
    private GraphMediaPhysicalReleaseEffectQueryResultV1() { }
    internal sealed record Released(Hash256 EvidenceHash) : GraphMediaPhysicalReleaseEffectQueryResultV1;
    internal sealed record Unknown : GraphMediaPhysicalReleaseEffectQueryResultV1;
    internal sealed record Rejected(BoundedAscii SafeCode) : GraphMediaPhysicalReleaseEffectQueryResultV1;
    internal sealed record NotFound : GraphMediaPhysicalReleaseEffectQueryResultV1;
}

internal interface IGraphMediaPhysicalReleasePortV1
{
    ValueTask<GraphMediaPhysicalReleaseEffectResultV1> ReleaseAsync(
        GraphMediaPhysicalReleaseEffectRequestV1 request, CancellationToken cancellationToken);
    ValueTask<GraphMediaPhysicalReleaseEffectQueryResultV1> QueryAsync(
        GraphMediaPhysicalReleaseEffectRequestV1 request, CancellationToken cancellationToken);
}

internal sealed record GraphMediaPhysicalReleaseRequestV1
{
    internal GraphMediaPhysicalReleaseRequestV1(OperationId operationId, StableId128 residenceId,
        GraphMediaResidenceLedgerV1 residences, GraphMediaOwnershipLedgerV1 ownership,
        GraphMediaWorkLedgerV1 work, OperationId? fanoutOperationId,
        ExpectedAuthorityVectorV1 expectedAuthority, CorrelationEnvelopeV1 correlation,
        MonotonicStampV1 effectObservedAt, UtcInstant observedAt,
        ulong maximumSessionRecords = 65_536, ulong maximumSessionCanonicalBytes = 67_108_864)
    {
        if (!operationId.IsValid || residenceId.Equals(default) || residences is null || ownership is null ||
            work is null || expectedAuthority is null || !correlation.IsValid || correlation.OperationId != operationId ||
            correlation.ParticipantId is not null || !effectObservedAt.IsValid ||
            maximumSessionRecords is 0 or > 65_536 || maximumSessionCanonicalBytes is 0 or > 67_108_864)
            throw new ArgumentException("A valid bounded physical-release request is required.");
        OperationId = operationId; ResidenceId = residenceId; Residences = residences; Ownership = ownership;
        Work = work; FanoutOperationId = fanoutOperationId; ExpectedAuthority = expectedAuthority;
        Correlation = correlation; EffectObservedAt = effectObservedAt; ObservedAt = observedAt;
        MaximumSessionRecords = maximumSessionRecords; MaximumSessionCanonicalBytes = maximumSessionCanonicalBytes;
    }
    internal OperationId OperationId { get; }
    internal StableId128 ResidenceId { get; }
    internal GraphMediaResidenceLedgerV1 Residences { get; }
    internal GraphMediaOwnershipLedgerV1 Ownership { get; }
    internal GraphMediaWorkLedgerV1 Work { get; }
    internal OperationId? FanoutOperationId { get; }
    internal ExpectedAuthorityVectorV1 ExpectedAuthority { get; }
    internal CorrelationEnvelopeV1 Correlation { get; }
    internal MonotonicStampV1 EffectObservedAt { get; }
    internal UtcInstant ObservedAt { get; }
    internal ulong MaximumSessionRecords { get; }
    internal ulong MaximumSessionCanonicalBytes { get; }
}

internal abstract record GraphMediaPhysicalReleaseResultV1
{
    private GraphMediaPhysicalReleaseResultV1() { }
    internal sealed record Released(AuthorityFactEnvelopeV1 Command, AuthorityFactEnvelopeV1 Fact,
        Hash256 EvidenceHash) : GraphMediaPhysicalReleaseResultV1;
    internal sealed record Unknown(AuthorityFactEnvelopeV1 Command, AuthorityFactEnvelopeV1 Fact) : GraphMediaPhysicalReleaseResultV1;
    internal sealed record Rejected(AuthorityFactEnvelopeV1 Command, AuthorityFactEnvelopeV1 Fact,
        BoundedAscii SafeCode) : GraphMediaPhysicalReleaseResultV1;
    internal sealed record RetryRequired(BoundedAscii SafeCode) : GraphMediaPhysicalReleaseResultV1;
    internal sealed record StoreUnavailable(BoundedAscii SafeCode) : GraphMediaPhysicalReleaseResultV1;
    internal sealed record Quarantined(BoundedAscii SafeCode) : GraphMediaPhysicalReleaseResultV1;
}

internal sealed class GraphMediaPhysicalReleaseCoordinatorV1
{
    private const int MaximumAppendAttempts = 3;
    private readonly IAuthorityJournalV1 _journal;
    private readonly IGraphMediaPhysicalReleasePortV1 _effects;
    private readonly AuthorityPayloadAdmissionRegistryV1 _registry;
    private readonly SemaphoreSlim _mutex = new(1, 1);

    internal GraphMediaPhysicalReleaseCoordinatorV1(IAuthorityJournalV1 journal,
        IGraphMediaPhysicalReleasePortV1 effects, AuthorityPayloadAdmissionRegistryV1 registry)
    { _journal = journal ?? throw new ArgumentNullException(nameof(journal)); _effects = effects ?? throw new ArgumentNullException(nameof(effects)); _registry = registry ?? throw new ArgumentNullException(nameof(registry)); }

    internal async ValueTask<GraphMediaPhysicalReleaseResultV1> ReleaseAsync(
        GraphMediaPhysicalReleaseRequestV1 request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request); cancellationToken.ThrowIfCancellationRequested();
        await _mutex.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var authority = Authenticate(request);
            if (authority.Error is not null) return authority.Error;
            var commandBody = authority.Body!;
            var session = request.Residences.Session;
            var history = await ReadAsync(session, request, cancellationToken).ConfigureAwait(false);
            if (history.Error is not null) return history.Error;
            if (history.Result is GraphMediaPhysicalReleaseFoldResultV1.Released or GraphMediaPhysicalReleaseFoldResultV1.Unknown)
                return ToResult(history.Result)!;
            if (history.Result is GraphMediaPhysicalReleaseFoldResultV1.InvalidHistory)
                return ToResult(history.Result)!;
            if (history.Result is GraphMediaPhysicalReleaseFoldResultV1.Rejected prior)
            {
                if (prior.CommandBody.OperationId == request.OperationId) return ToResult(prior)!;
                commandBody = WithPredecessor(commandBody, prior.Fact.Position);
            }

            AuthorityFactEnvelopeV1 command;
            if (history.Result is GraphMediaPhysicalReleaseFoldResultV1.CommandOnly pending)
            {
                if (pending.Body.OperationId != request.OperationId || !SameAuthority(pending.Body, commandBody) ||
                    !GraphMediaPhysicalReleaseCodecsV1.TryDecodeOuter(pending.Command.PayloadMemory, out var pendingOuter) ||
                    pendingOuter is null || pendingOuter.ExpectedAuthority != request.ExpectedAuthority)
                    return Quarantine("contradictory-duplicate");
                command = pending.Command;
                return await ResolveCommandOnlyAsync(command, pending.Body, request, cancellationToken).ConfigureAwait(false);
            }

            var commandBytes = EncodeCommand(session, request.ExpectedAuthority, commandBody);
            var commandProposal = Proposal(GraphMediaPhysicalReleasePayloadRegistrationsV1.Command,
                GraphMediaPhysicalReleaseFactIdsV1.Command(session, request.OperationId), commandBytes,
                request.Correlation, request.ObservedAt);
            var appended = await AppendAsync(session, history.Through, commandProposal, request, cancellationToken).ConfigureAwait(false);
            if (appended.Error is not null) return appended.Error;
            command = appended.Envelope!;

            var effectRequest = Effect(commandBody);
            GraphMediaPhysicalReleaseEffectResultV1 effect;
            var invoked = false;
            try
            {
                cancellationToken.ThrowIfCancellationRequested(); invoked = true;
                effect = await _effects.ReleaseAsync(effectRequest, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (invoked)
            { return await ResolveAmbiguousEffectAsync(command, commandBody, request).ConfigureAwait(false); }
            catch (Exception) when (invoked)
            { return await ResolveAmbiguousEffectAsync(command, commandBody, request).ConfigureAwait(false); }
            if (effect is null || effect is GraphMediaPhysicalReleaseEffectResultV1.Released { EvidenceHash: var hash } && hash == default)
                return Quarantine("release-effect-invalid");
            return await RecordEffectAsync(command, commandBody, effect, request, CancellationToken.None).ConfigureAwait(false);
        }
        finally { _mutex.Release(); }
    }

    private (GraphMediaPhysicalReleaseCommandBodyV1? Body, GraphMediaPhysicalReleaseResultV1? Error) Authenticate(
        GraphMediaPhysicalReleaseRequestV1 request)
    {
        var session = request.Residences.Session;
        if (request.Ownership.Session != session || request.Work.Session != session ||
            request.ExpectedAuthority.Session != session || request.Residences.GraphGeneration != request.Ownership.GraphGeneration ||
            request.Residences.GraphGeneration != request.Work.GraphGeneration)
            return (null, Quarantine("release-authority-stale"));
        var graphs = request.ExpectedAuthority.Axes.Where(x => x.AxisId == AuthorityAxisId.Graph && x.Value is AuthorityAxisValueV1.Graph)
            .Select(x => ((AuthorityAxisValueV1.Graph)x.Value).Value).ToArray();
        if (graphs.Length != 1 || graphs[0] != request.Residences.GraphGeneration ||
            !request.Residences.Residences.TryGetValue(request.ResidenceId, out var residence) ||
            residence.State != GraphMediaResidenceStateV1.Visible || residence.Class != GraphMediaResidenceClassV1.Controlled)
            return (null, Quarantine("residence-mismatch"));
        if (!request.Ownership.Owners.TryGetValue(residence.OwnerId, out var owner) || owner.Key != residence.OwnerKey || owner.Media != residence.Media ||
            owner.State is not (GraphMediaOwnerStateV1.Transferred or GraphMediaOwnerStateV1.Disposed))
            return (null, Quarantine("owner-terminal-mismatch"));
        var receipts = request.Ownership.Receipts.Where(x => x.SourceOwnerId.Equals(owner.OwnerId) &&
            x.Result is GraphMediaOwnerTransitionResultV1.Transferred or GraphMediaOwnerTransitionResultV1.Disposed).ToArray();
        if (receipts.Length != 1 || request.Ownership.Borrows.Any(x => x.OwnerId.Equals(owner.OwnerId) && x.State != GraphMediaBorrowStateV1.Returned))
            return (null, Quarantine("owner-terminal-mismatch"));
        var receipt = receipts[0];
        if (request.Work.QueryReleaseEligibility(request.ResidenceId) != GraphMediaReleaseEligibilityV1.Eligible)
            return (null, Quarantine("work-encumbered"));
        var workRows = request.Work.Work.Values.Where(x => x.ResidenceId.Equals(request.ResidenceId)).ToArray();
        var cleanupRows = request.Work.Cleanup.Values.Where(x => workRows.Any(w => w.WorkId.Equals(x.WorkId))).ToArray();
        if (workRows.Length is 0 or > 64 || cleanupRows.Length is 0 or > 64)
            return (null, Quarantine("work-encumbered"));
        GraphMediaFanoutReleaseProofV1? fanout = null;
        var origins = request.Residences.Fanouts.Values.Where(x =>
            x.Destinations.Any(destination => destination.Residence.ResidenceId.Equals(request.ResidenceId))).ToArray();
        if (origins.Length > 1 || origins.Length == 1 && request.FanoutOperationId != origins[0].OperationId ||
            origins.Length == 0 && request.FanoutOperationId is not null)
            return (null, Quarantine("fanout-incomplete"));
        if (request.FanoutOperationId is { } fanoutId)
        {
            if (!request.Residences.Fanouts.TryGetValue(fanoutId, out var row) ||
                row.Result is not (GraphMediaFanoutResultV1.Committed or GraphMediaFanoutResultV1.Reconciled) ||
                !row.Destinations.Any(x => x.Residence.ResidenceId.Equals(request.ResidenceId)))
                return (null, Quarantine("fanout-incomplete"));
            fanout = new(row.OperationId, row.RequestHash, row.Result, request.Residences.Fingerprint);
        }
        var returned = request.Ownership.Borrows.Where(x => x.OwnerId.Equals(owner.OwnerId) && x.State == GraphMediaBorrowStateV1.Returned)
            .OrderBy(x => StableBytes(x.TokenId), Comparer<byte[]>.Create(static (left, right) => left.AsSpan().SequenceCompareTo(right))).ToArray();
        var returnedHash = HashReturned(returned);
        var ownerProof = new GraphMediaOwnerReleaseProofV1(owner.OwnerId, receipt.OperationId, receipt.RequestHash,
            receipt.Result, request.Ownership.Fingerprint, checked((ushort)returned.Length), returnedHash);
        var workProof = new GraphMediaWorkReleaseProofV1(request.Work.Fingerprint,
            GraphMediaReleaseEligibilityV1.Eligible, checked((ushort)workRows.Length), checked((ushort)cleanupRows.Length));
        var predecessor = default(JournalPositionV1?);
        return (new(request.OperationId, GraphMediaReleaseResidenceProofV1.FromResidence(residence), ownerProof,
            workProof, fanout, predecessor, request.EffectObservedAt), null);
    }

    private async ValueTask<GraphMediaPhysicalReleaseResultV1> ResolveCommandOnlyAsync(
        AuthorityFactEnvelopeV1 command, GraphMediaPhysicalReleaseCommandBodyV1 body,
        GraphMediaPhysicalReleaseRequestV1 request, CancellationToken cancellationToken)
    {
        GraphMediaPhysicalReleaseEffectQueryResultV1 query;
        try { query = await _effects.QueryAsync(Effect(body), cancellationToken).ConfigureAwait(false); }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception) { return new GraphMediaPhysicalReleaseResultV1.StoreUnavailable(new("release-query-unavailable")); }
        if (query is null || query is GraphMediaPhysicalReleaseEffectQueryResultV1.Released { EvidenceHash: var hash } && hash == default)
            return Quarantine("release-effect-invalid");
        GraphMediaPhysicalReleaseEffectResultV1 effect = query switch
        {
            GraphMediaPhysicalReleaseEffectQueryResultV1.Released x => new GraphMediaPhysicalReleaseEffectResultV1.Released(x.EvidenceHash),
            GraphMediaPhysicalReleaseEffectQueryResultV1.Rejected x => new GraphMediaPhysicalReleaseEffectResultV1.Rejected(x.SafeCode),
            _ => new GraphMediaPhysicalReleaseEffectResultV1.Unknown()
        };
        return await RecordEffectAsync(command, body, effect, request, CancellationToken.None).ConfigureAwait(false);
    }

    private async ValueTask<GraphMediaPhysicalReleaseResultV1> ResolveAmbiguousEffectAsync(
        AuthorityFactEnvelopeV1 command, GraphMediaPhysicalReleaseCommandBodyV1 body,
        GraphMediaPhysicalReleaseRequestV1 request)
    {
        GraphMediaPhysicalReleaseEffectQueryResultV1 query;
        try { query = await _effects.QueryAsync(Effect(body), CancellationToken.None).ConfigureAwait(false); }
        catch { query = new GraphMediaPhysicalReleaseEffectQueryResultV1.Unknown(); }
        if (query is null || query is GraphMediaPhysicalReleaseEffectQueryResultV1.Released { EvidenceHash: var hash } && hash == default)
            query = new GraphMediaPhysicalReleaseEffectQueryResultV1.Unknown();
        GraphMediaPhysicalReleaseEffectResultV1 effect = query switch
        {
            GraphMediaPhysicalReleaseEffectQueryResultV1.Released x => new GraphMediaPhysicalReleaseEffectResultV1.Released(x.EvidenceHash),
            GraphMediaPhysicalReleaseEffectQueryResultV1.Rejected x => new GraphMediaPhysicalReleaseEffectResultV1.Rejected(x.SafeCode),
            _ => new GraphMediaPhysicalReleaseEffectResultV1.Unknown()
        };
        return await RecordEffectAsync(command, body, effect, request, CancellationToken.None).ConfigureAwait(false);
    }

    private async ValueTask<GraphMediaPhysicalReleaseResultV1> RecordEffectAsync(
        AuthorityFactEnvelopeV1 command, GraphMediaPhysicalReleaseCommandBodyV1 body,
        GraphMediaPhysicalReleaseEffectResultV1 effect, GraphMediaPhysicalReleaseRequestV1 request,
        CancellationToken cancellationToken)
    {
        var outcome = effect switch { GraphMediaPhysicalReleaseEffectResultV1.Released => GraphMediaPhysicalReleaseOutcomeV1.Released,
            GraphMediaPhysicalReleaseEffectResultV1.Rejected => GraphMediaPhysicalReleaseOutcomeV1.Rejected, _ => GraphMediaPhysicalReleaseOutcomeV1.Unknown };
        var evidence = (effect as GraphMediaPhysicalReleaseEffectResultV1.Released)?.EvidenceHash;
        var code = (effect as GraphMediaPhysicalReleaseEffectResultV1.Rejected)?.SafeCode;
        if (outcome == GraphMediaPhysicalReleaseOutcomeV1.Rejected && code?.ToString() is not
            ("release-authority-stale" or "owner-terminal-mismatch" or "work-encumbered" or "fanout-incomplete" or
             "residence-mismatch" or "capacity-proof-mismatch" or "release-predecessor-conflict"))
        { outcome = GraphMediaPhysicalReleaseOutcomeV1.Unknown; code = null; }
        var factBody = new GraphMediaPhysicalReleaseFactBodyV1(command.Position, body.Residence.ResidenceId,
            body.Residence.RequestHash, body.Residence.GrantId, body.Residence.CurrentFact, body.Residence.Assignment,
            outcome, evidence, code, body.ObservedAt);
        var bytes = EncodeFact(command.Position.Session, request.ExpectedAuthority, factBody);
        var proposal = Proposal(GraphMediaPhysicalReleasePayloadRegistrationsV1.Fact,
            GraphMediaPhysicalReleaseFactIdsV1.Fact(command.Position), bytes, request.Correlation, request.ObservedAt);
        var history = await ReadAsync(command.Position.Session, request, CancellationToken.None).ConfigureAwait(false);
        if (history.Error is not null) return history.Error;
        var terminal = ToResult(history.Result!); if (terminal is not null) return terminal;
        var appended = await AppendAsync(command.Position.Session, history.Through, proposal, request, cancellationToken).ConfigureAwait(false);
        if (appended.Error is not null) return appended.Error;
        var final = await ReadAsync(command.Position.Session, request, CancellationToken.None).ConfigureAwait(false);
        return final.Error ?? ToResult(final.Result!) ?? Quarantine("release-history-invalid");
    }

    private async ValueTask<(GraphMediaPhysicalReleaseFoldResultV1? Result, long Through,
        GraphMediaPhysicalReleaseResultV1? Error)> ReadAsync(SessionAuthorityStampV1 session,
        GraphMediaPhysicalReleaseRequestV1 request, CancellationToken cancellationToken)
    {
        var fold = GraphMediaPhysicalReleaseFoldV1.Create(session, request.ResidenceId, _registry);
        long cursor = 0, through = long.MaxValue; ulong records = 0, bytes = 0;
        while (cursor < through)
        {
            ReadAuthorityRangeResultV1 read;
            try { read = await _journal.ReadAsync(new(session, cursor, through, 256, 1_048_576), cancellationToken).ConfigureAwait(false); }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
            catch { return (null, cursor, new GraphMediaPhysicalReleaseResultV1.StoreUnavailable(new("release-journal-unavailable"))); }
            if (read is not ReadAuthorityRangeResultV1.Batch batch)
                return (null, cursor, new GraphMediaPhysicalReleaseResultV1.StoreUnavailable(new("release-journal-unavailable")));
            through = batch.SnapshotThrough;
            if (batch.Facts.Count == 0) break;
            foreach (var envelope in batch.Facts)
            {
                records++; bytes += (ulong)AuthorityCanonicalCborV1.GetEnvelopeEncodedLength(envelope);
                if (records > request.MaximumSessionRecords || bytes > request.MaximumSessionCanonicalBytes)
                    return (null, cursor, Quarantine("release-history-invalid"));
                if (fold.Apply(envelope) is GraphMediaPhysicalReleaseFoldApplyResultV1.InvalidHistory invalid)
                    return (null, cursor, Quarantine(invalid.SafeCode.ToString()));
                cursor = envelope.Position.Sequence;
            }
            if (!batch.HasMore) break;
        }
        return (fold.Complete(), cursor, null);
    }

    private async ValueTask<(AuthorityFactEnvelopeV1? Envelope, GraphMediaPhysicalReleaseResultV1? Error)> AppendAsync(
        SessionAuthorityStampV1 session, long expectedHead, ProposedAuthorityFactV1 proposal,
        GraphMediaPhysicalReleaseRequestV1 request, CancellationToken cancellationToken)
    {
        for (var attempt = 0; attempt < MaximumAppendAttempts; attempt++)
        {
            AppendAuthorityResultV1 result;
            try { result = await _journal.AppendAsync(new(session, expectedHead, [], [proposal], 1_048_576), cancellationToken).ConfigureAwait(false); }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            { result = new AppendAuthorityResultV1.OutcomeUnknown(request.OperationId); }
            catch { result = new AppendAuthorityResultV1.OutcomeUnknown(request.OperationId); }
            if (result is AppendAuthorityResultV1.Committed committed && committed.Envelopes.Count == 1)
                return EnvelopeMatches(committed.Envelopes[0], proposal)
                    ? (committed.Envelopes[0], null) : (null, Quarantine("release-append-invalid"));
            if (result is AppendAuthorityResultV1.AlreadyCommitted already && already.Envelopes.Count == 1)
                return EnvelopeMatches(already.Envelopes[0], proposal)
                    ? (already.Envelopes[0], null) : (null, Quarantine("release-append-invalid"));
            if (result is AppendAuthorityResultV1.InvalidPayload or AppendAuthorityResultV1.UnknownSchema or
                AppendAuthorityResultV1.ThreadConflict or AppendAuthorityResultV1.ContradictoryDuplicate)
                return (null, Quarantine("release-append-invalid"));
            if (result is AppendAuthorityResultV1.CapacityRefused)
                return (null, new GraphMediaPhysicalReleaseResultV1.StoreUnavailable(new("release-journal-capacity")));
            var reread = await ReadAsync(session, request, CancellationToken.None).ConfigureAwait(false);
            if (reread.Error is not null) return (null, reread.Error);
            var found = Find(reread.Result!, proposal.FactId);
            if (found is not null) return (found, null);
            if (proposal.PayloadSchema == GraphMediaPhysicalReleasePayloadRegistrationsV1.Command.Schema)
            {
                if (reread.Result is GraphMediaPhysicalReleaseFoldResultV1.CommandOnly)
                    return (null, new GraphMediaPhysicalReleaseResultV1.RetryRequired(new("release-predecessor-conflict")));
                var terminal = ToResult(reread.Result!);
                if (terminal is GraphMediaPhysicalReleaseResultV1.Released or GraphMediaPhysicalReleaseResultV1.Unknown or
                    GraphMediaPhysicalReleaseResultV1.Quarantined)
                    return (null, terminal);
            }
            expectedHead = reread.Through;
            if (result is AppendAuthorityResultV1.StoreUnavailable)
                return (null, new GraphMediaPhysicalReleaseResultV1.StoreUnavailable(new("release-journal-unavailable")));
        }
        return (null, new GraphMediaPhysicalReleaseResultV1.RetryRequired(new("release-predecessor-conflict")));
    }

    private static AuthorityFactEnvelopeV1? Find(GraphMediaPhysicalReleaseFoldResultV1 result, JournalFactId id) => result switch
    {
        GraphMediaPhysicalReleaseFoldResultV1.CommandOnly x when x.Command.FactId == id => x.Command,
        GraphMediaPhysicalReleaseFoldResultV1.Released x => x.Command.FactId == id ? x.Command : x.Fact.FactId == id ? x.Fact : null,
        GraphMediaPhysicalReleaseFoldResultV1.Unknown x => x.Command.FactId == id ? x.Command : x.Fact.FactId == id ? x.Fact : null,
        GraphMediaPhysicalReleaseFoldResultV1.Rejected x => x.Command.FactId == id ? x.Command : x.Fact.FactId == id ? x.Fact : null,
        _ => null
    };

    private static GraphMediaPhysicalReleaseResultV1? ToResult(GraphMediaPhysicalReleaseFoldResultV1 result) => result switch
    {
        GraphMediaPhysicalReleaseFoldResultV1.Released x => new GraphMediaPhysicalReleaseResultV1.Released(x.Command, x.Fact, x.EvidenceHash),
        GraphMediaPhysicalReleaseFoldResultV1.Unknown x => new GraphMediaPhysicalReleaseResultV1.Unknown(x.Command, x.Fact),
        GraphMediaPhysicalReleaseFoldResultV1.Rejected x => new GraphMediaPhysicalReleaseResultV1.Rejected(x.Command, x.Fact, x.SafeCode),
        GraphMediaPhysicalReleaseFoldResultV1.InvalidHistory x => Quarantine(x.SafeCode.ToString()),
        _ => null
    };

    private static GraphMediaPhysicalReleaseEffectRequestV1 Effect(GraphMediaPhysicalReleaseCommandBodyV1 body) =>
        new(body.OperationId, body.Residence.ResidenceId, body.Residence.RequestHash, body.Residence.GrantId,
            body.Residence.CurrentFact, body.Residence.Assignment);
    private static GraphMediaPhysicalReleaseCommandBodyV1 WithPredecessor(GraphMediaPhysicalReleaseCommandBodyV1 body,
        JournalPositionV1 predecessor) => new(body.OperationId, body.Residence, body.OwnerProof, body.WorkProof,
            body.FanoutProof, predecessor, body.ObservedAt);
    private static byte[] EncodeCommand(SessionAuthorityStampV1 session, ExpectedAuthorityVectorV1 authority,
        GraphMediaPhysicalReleaseCommandBodyV1 body) => GraphMediaPhysicalReleaseCodecsV1.EncodeOuter(
            new(session, authority, GraphMediaPhysicalReleaseCodecsV1.EncodeCommandBody(body)));
    private static byte[] EncodeFact(SessionAuthorityStampV1 session, ExpectedAuthorityVectorV1 authority,
        GraphMediaPhysicalReleaseFactBodyV1 body) => GraphMediaPhysicalReleaseCodecsV1.EncodeOuter(
            new(session, authority, GraphMediaPhysicalReleaseCodecsV1.EncodeFactBody(body)));
    private static ProposedAuthorityFactV1 Proposal(AuthorityPayloadRegistrationV1 registration, JournalFactId factId,
        byte[] bytes, CorrelationEnvelopeV1 correlation, UtcInstant observedAt) => new(factId, null, OwnerSliceId.S1,
            registration.Schema, bytes, AuthorityPayloadHashV1.Compute(registration.SchemaToken, registration.Schema, bytes), correlation, observedAt);
    private static bool SameAuthority(GraphMediaPhysicalReleaseCommandBodyV1 left, GraphMediaPhysicalReleaseCommandBodyV1 right) =>
        GraphMediaPhysicalReleaseCodecsV1.EncodeCommandBody(left).SequenceEqual(GraphMediaPhysicalReleaseCodecsV1.EncodeCommandBody(right));
    private static bool EnvelopeMatches(AuthorityFactEnvelopeV1 envelope, ProposedAuthorityFactV1 proposal) =>
        envelope.FactId == proposal.FactId && envelope.ThreadScope is null && envelope.Owner == proposal.Owner &&
        envelope.PayloadSchema == proposal.PayloadSchema && envelope.PayloadHash == proposal.PayloadHash &&
        envelope.Correlation == proposal.Correlation && envelope.ObservedAt == proposal.ObservedAt &&
        envelope.PayloadBytes.SequenceEqual(proposal.PayloadBytes);
    private static byte[] StableBytes(StableId128 value) { var bytes = new byte[16]; if (!value.TryWriteBytes(bytes)) throw new InvalidOperationException(); return bytes; }
    private static Hash256 HashReturned(IEnumerable<GraphMediaBorrowRecordV1> rows)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        hash.AppendData("hpd-s2-graph-media-returned-borrow-set-v1\0"u8);
        foreach (var row in rows) { hash.AppendData(StableBytes(row.TokenId)); if (row.ReturnHash is { } value) { var bytes = new byte[32]; value.TryWriteBytes(bytes); hash.AppendData(bytes); } }
        return Hash256.FromBytes(hash.GetHashAndReset());
    }
    private static GraphMediaPhysicalReleaseResultV1.Quarantined Quarantine(string code) => new(new(code));
}
