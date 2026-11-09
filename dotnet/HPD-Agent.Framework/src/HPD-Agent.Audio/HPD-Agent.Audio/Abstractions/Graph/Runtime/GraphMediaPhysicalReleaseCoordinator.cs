using System.Security.Cryptography;
using HPD.Agent.Authority;

namespace HPD.Agent.Audio.Graph;

internal sealed record GraphMediaPhysicalReleaseEffectRequest(
    OperationId OperationId, StableId128 ResidenceId, Hash256 ResidenceRequestHash,
    CapacityGrantId GrantId, JournalPositionV1 CurrentFact,
    GraphMediaCapacityAssignment Assignment);

internal abstract record GraphMediaPhysicalReleaseEffectResult
{
    private GraphMediaPhysicalReleaseEffectResult() { }
    internal sealed record Released(Hash256 EvidenceHash) : GraphMediaPhysicalReleaseEffectResult;
    internal sealed record Unknown : GraphMediaPhysicalReleaseEffectResult;
    internal sealed record Rejected(BoundedAscii SafeCode) : GraphMediaPhysicalReleaseEffectResult;
}

internal abstract record GraphMediaPhysicalReleaseEffectQueryResult
{
    private GraphMediaPhysicalReleaseEffectQueryResult() { }
    internal sealed record Released(Hash256 EvidenceHash) : GraphMediaPhysicalReleaseEffectQueryResult;
    internal sealed record Unknown : GraphMediaPhysicalReleaseEffectQueryResult;
    internal sealed record Rejected(BoundedAscii SafeCode) : GraphMediaPhysicalReleaseEffectQueryResult;
    internal sealed record NotFound : GraphMediaPhysicalReleaseEffectQueryResult;
}

internal interface IGraphMediaPhysicalReleasePort
{
    ValueTask<GraphMediaPhysicalReleaseEffectResult> ReleaseAsync(
        GraphMediaPhysicalReleaseEffectRequest request, CancellationToken cancellationToken);
    ValueTask<GraphMediaPhysicalReleaseEffectQueryResult> QueryAsync(
        GraphMediaPhysicalReleaseEffectRequest request, CancellationToken cancellationToken);
}

internal sealed record GraphMediaPhysicalReleaseRequest
{
    internal GraphMediaPhysicalReleaseRequest(OperationId operationId, StableId128 residenceId,
        GraphMediaResidenceLedger residences, GraphMediaOwnershipLedger ownership,
        GraphMediaWorkLedger work, OperationId? fanoutOperationId,
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
    internal GraphMediaResidenceLedger Residences { get; }
    internal GraphMediaOwnershipLedger Ownership { get; }
    internal GraphMediaWorkLedger Work { get; }
    internal OperationId? FanoutOperationId { get; }
    internal ExpectedAuthorityVectorV1 ExpectedAuthority { get; }
    internal CorrelationEnvelopeV1 Correlation { get; }
    internal MonotonicStampV1 EffectObservedAt { get; }
    internal UtcInstant ObservedAt { get; }
    internal ulong MaximumSessionRecords { get; }
    internal ulong MaximumSessionCanonicalBytes { get; }
}

internal abstract record GraphMediaPhysicalReleaseResult
{
    private GraphMediaPhysicalReleaseResult() { }
    internal sealed record Released(AuthorityFactEnvelopeV1 Command, AuthorityFactEnvelopeV1 Fact,
        Hash256 EvidenceHash) : GraphMediaPhysicalReleaseResult;
    internal sealed record Unknown(AuthorityFactEnvelopeV1 Command, AuthorityFactEnvelopeV1 Fact) : GraphMediaPhysicalReleaseResult;
    internal sealed record Rejected(AuthorityFactEnvelopeV1 Command, AuthorityFactEnvelopeV1 Fact,
        BoundedAscii SafeCode) : GraphMediaPhysicalReleaseResult;
    internal sealed record RetryRequired(BoundedAscii SafeCode) : GraphMediaPhysicalReleaseResult;
    internal sealed record StoreUnavailable(BoundedAscii SafeCode) : GraphMediaPhysicalReleaseResult;
    internal sealed record Quarantined(BoundedAscii SafeCode) : GraphMediaPhysicalReleaseResult;
}

internal sealed class GraphMediaPhysicalReleaseCoordinator
{
    private const int MaximumAppendAttempts = 3;
    private readonly IAuthorityJournalV1 _journal;
    private readonly IGraphMediaPhysicalReleasePort _effects;
    private readonly AuthorityPayloadAdmissionRegistryV1 _registry;
    private readonly SemaphoreSlim _mutex = new(1, 1);

    internal GraphMediaPhysicalReleaseCoordinator(IAuthorityJournalV1 journal,
        IGraphMediaPhysicalReleasePort effects, AuthorityPayloadAdmissionRegistryV1 registry)
    { _journal = journal ?? throw new ArgumentNullException(nameof(journal)); _effects = effects ?? throw new ArgumentNullException(nameof(effects)); _registry = registry ?? throw new ArgumentNullException(nameof(registry)); }

    internal async ValueTask<GraphMediaPhysicalReleaseResult> ReleaseAsync(
        GraphMediaPhysicalReleaseRequest request, CancellationToken cancellationToken)
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
            if (history.Result is GraphMediaPhysicalReleaseFoldResult.Released or GraphMediaPhysicalReleaseFoldResult.Unknown)
                return ToResult(history.Result)!;
            if (history.Result is GraphMediaPhysicalReleaseFoldResult.InvalidHistory)
                return ToResult(history.Result)!;
            if (!AuthenticateWorkHistory(request, commandBody, history.WorkSnapshot!))
                return Quarantine("work-encumbered");
            if (history.Result is GraphMediaPhysicalReleaseFoldResult.Rejected prior)
            {
                if (prior.CommandBody.OperationId == request.OperationId) return ToResult(prior)!;
                commandBody = WithPredecessor(commandBody, prior.Fact.Position);
            }

            AuthorityFactEnvelopeV1 command;
            if (history.Result is GraphMediaPhysicalReleaseFoldResult.CommandOnly pending)
            {
                if (pending.Body.OperationId != request.OperationId || !SameAuthority(pending.Body, commandBody) ||
                    !GraphMediaPhysicalReleaseCodecs.TryDecodeOuter(pending.Command.PayloadMemory, out var pendingOuter) ||
                    pendingOuter is null || pendingOuter.ExpectedAuthority != request.ExpectedAuthority)
                    return Quarantine("contradictory-duplicate");
                command = pending.Command;
                return await ResolveCommandOnlyAsync(command, pending.Body, request, cancellationToken).ConfigureAwait(false);
            }

            var commandBytes = EncodeCommand(session, request.ExpectedAuthority, commandBody);
            var commandProposal = Proposal(GraphMediaPhysicalReleasePayloadRegistrations.Command,
                GraphMediaPhysicalReleaseFactIds.Command(session, request.OperationId), commandBytes,
                request.Correlation, request.ObservedAt);
            var appended = await AppendAsync(session, history.Through, commandProposal, request, cancellationToken).ConfigureAwait(false);
            if (appended.Error is not null) return appended.Error;
            command = appended.Envelope!;

            var effectRequest = Effect(commandBody);
            GraphMediaPhysicalReleaseEffectResult effect;
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
            if (effect is null || effect is GraphMediaPhysicalReleaseEffectResult.Released { EvidenceHash: var hash } && hash == default)
                return Quarantine("release-effect-invalid");
            return await RecordEffectAsync(command, commandBody, effect, request, CancellationToken.None).ConfigureAwait(false);
        }
        finally { _mutex.Release(); }
    }

    private (GraphMediaPhysicalReleaseCommandBody? Body, GraphMediaPhysicalReleaseResult? Error) Authenticate(
        GraphMediaPhysicalReleaseRequest request)
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
            residence.State != GraphMediaResidenceState.Visible || residence.Class != GraphMediaResidenceClass.Controlled)
            return (null, Quarantine("residence-mismatch"));
        if (!request.Ownership.Owners.TryGetValue(residence.OwnerId, out var owner) || owner.Key != residence.OwnerKey || owner.Media != residence.Media ||
            owner.State is not (GraphMediaOwnerState.Transferred or GraphMediaOwnerState.Disposed))
            return (null, Quarantine("owner-terminal-mismatch"));
        var receipts = request.Ownership.Receipts.Where(x => x.SourceOwnerId.Equals(owner.OwnerId) &&
            x.Result is GraphMediaOwnerTransitionResult.Transferred or GraphMediaOwnerTransitionResult.Disposed).ToArray();
        if (receipts.Length != 1 || request.Ownership.Borrows.Any(x => x.OwnerId.Equals(owner.OwnerId) && x.State != GraphMediaBorrowState.Returned))
            return (null, Quarantine("owner-terminal-mismatch"));
        var receipt = receipts[0];
        if (request.Work.QueryReleaseEligibility(request.ResidenceId) != GraphMediaReleaseEligibility.Eligible)
            return (null, Quarantine("work-encumbered"));
        var workRows = request.Work.Work.Values.Where(x => x.ResidenceId.Equals(request.ResidenceId)).ToArray();
        var cleanupRows = request.Work.Cleanup.Values.Where(x => workRows.Any(w => w.WorkId.Equals(x.WorkId))).ToArray();
        if (workRows.Length is 0 or > 64 || cleanupRows.Length is 0 or > 64)
            return (null, Quarantine("work-encumbered"));
        GraphMediaFanoutReleaseProof? fanout = null;
        var origins = request.Residences.Fanouts.Values.Where(x =>
            x.Destinations.Any(destination => destination.Residence.ResidenceId.Equals(request.ResidenceId))).ToArray();
        if (origins.Length > 1 || origins.Length == 1 && request.FanoutOperationId != origins[0].OperationId ||
            origins.Length == 0 && request.FanoutOperationId is not null)
            return (null, Quarantine("fanout-incomplete"));
        if (request.FanoutOperationId is { } fanoutId)
        {
            if (!request.Residences.Fanouts.TryGetValue(fanoutId, out var row) ||
                row.Result is not (GraphMediaFanoutResult.Committed or GraphMediaFanoutResult.Reconciled) ||
                !row.Destinations.Any(x => x.Residence.ResidenceId.Equals(request.ResidenceId)))
                return (null, Quarantine("fanout-incomplete"));
            fanout = new(row.OperationId, row.RequestHash, row.Result, request.Residences.Fingerprint);
        }
        var returned = request.Ownership.Borrows.Where(x => x.OwnerId.Equals(owner.OwnerId) && x.State == GraphMediaBorrowState.Returned)
            .OrderBy(x => StableBytes(x.TokenId), Comparer<byte[]>.Create(static (left, right) => left.AsSpan().SequenceCompareTo(right))).ToArray();
        var returnedHash = HashReturned(returned);
        var ownerProof = new GraphMediaOwnerReleaseProof(owner.OwnerId, receipt.OperationId, receipt.RequestHash,
            receipt.Result, request.Ownership.Fingerprint, checked((ushort)returned.Length), returnedHash);
        var workProof = new GraphMediaWorkReleaseProof(request.Work.Fingerprint,
            GraphMediaReleaseEligibility.Eligible, checked((ushort)workRows.Length), checked((ushort)cleanupRows.Length));
        var predecessor = default(JournalPositionV1?);
        return (new(request.OperationId, GraphMediaReleaseResidenceProof.FromResidence(residence), ownerProof,
            workProof, fanout, predecessor, request.EffectObservedAt), null);
    }

    private static bool AuthenticateWorkHistory(GraphMediaPhysicalReleaseRequest request,
        GraphMediaPhysicalReleaseCommandBody command,
        GraphMediaWorkExecutionFoldSnapshot snapshot)
    {
        if (snapshot is not GraphMediaWorkExecutionFoldSnapshot.Current current ||
            command.WorkProof.LedgerFingerprint != request.Work.Fingerprint ||
            command.WorkProof.Eligibility != GraphMediaReleaseEligibility.Eligible)
            return false;
        var rows = request.Work.Work.Values.Where(x => x.ResidenceId.Equals(request.ResidenceId))
            .OrderBy(x => StableBytes(x.WorkId), Comparer<byte[]>.Create(static (left, right) => left.AsSpan().SequenceCompareTo(right)))
            .ToArray();
        if (rows.Length != command.WorkProof.WorkCount ||
            current.Operations.Any(x => x is GraphMediaWorkExecutionFoldResult.CommandOnly or
                GraphMediaWorkExecutionFoldResult.Unknown or GraphMediaWorkExecutionFoldResult.InvalidHistory))
            return false;
        var completed = current.Operations.OfType<GraphMediaWorkExecutionFoldResult.Completed>().ToArray();
        if (completed.Length != rows.Length) return false;
        foreach (var row in rows)
        {
            if (row.State != GraphMediaWorkState.Terminal || row.OutcomeHash is not { } outcome) return false;
            var matches = completed.Where(x => x.CommandBody.Work.WorkId.Equals(row.WorkId)).ToArray();
            if (matches.Length != 1) return false;
            var match = matches[0];
            var expectedWork = GraphMediaWorkAuthority.FromRecord(row);
            var expectedCleanups = request.Work.Cleanup.Values.Where(x => x.WorkId.Equals(row.WorkId))
                .OrderBy(x => x.RegistrationOrdinal)
                .Select(x => new GraphMediaCleanupRegistration(x.CleanupId, x.RequestHash)).ToArray();
            if (match.CommandBody.Work != expectedWork || !match.CommandBody.Cleanups.SequenceEqual(expectedCleanups) ||
                !match.FactBody.WorkId.Equals(row.WorkId) || match.FactBody.WorkRequestHash != row.RequestHash ||
                match.EvidenceHash != outcome)
                return false;
        }
        return true;
    }

    private async ValueTask<GraphMediaPhysicalReleaseResult> ResolveCommandOnlyAsync(
        AuthorityFactEnvelopeV1 command, GraphMediaPhysicalReleaseCommandBody body,
        GraphMediaPhysicalReleaseRequest request, CancellationToken cancellationToken)
    {
        GraphMediaPhysicalReleaseEffectQueryResult query;
        try { query = await _effects.QueryAsync(Effect(body), cancellationToken).ConfigureAwait(false); }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception) { return new GraphMediaPhysicalReleaseResult.StoreUnavailable(new("release-query-unavailable")); }
        if (query is null || query is GraphMediaPhysicalReleaseEffectQueryResult.Released { EvidenceHash: var hash } && hash == default)
            return Quarantine("release-effect-invalid");
        GraphMediaPhysicalReleaseEffectResult effect = query switch
        {
            GraphMediaPhysicalReleaseEffectQueryResult.Released x => new GraphMediaPhysicalReleaseEffectResult.Released(x.EvidenceHash),
            GraphMediaPhysicalReleaseEffectQueryResult.Rejected x => new GraphMediaPhysicalReleaseEffectResult.Rejected(x.SafeCode),
            _ => new GraphMediaPhysicalReleaseEffectResult.Unknown()
        };
        return await RecordEffectAsync(command, body, effect, request, CancellationToken.None).ConfigureAwait(false);
    }

    private async ValueTask<GraphMediaPhysicalReleaseResult> ResolveAmbiguousEffectAsync(
        AuthorityFactEnvelopeV1 command, GraphMediaPhysicalReleaseCommandBody body,
        GraphMediaPhysicalReleaseRequest request)
    {
        GraphMediaPhysicalReleaseEffectQueryResult query;
        try { query = await _effects.QueryAsync(Effect(body), CancellationToken.None).ConfigureAwait(false); }
        catch { query = new GraphMediaPhysicalReleaseEffectQueryResult.Unknown(); }
        if (query is null || query is GraphMediaPhysicalReleaseEffectQueryResult.Released { EvidenceHash: var hash } && hash == default)
            query = new GraphMediaPhysicalReleaseEffectQueryResult.Unknown();
        GraphMediaPhysicalReleaseEffectResult effect = query switch
        {
            GraphMediaPhysicalReleaseEffectQueryResult.Released x => new GraphMediaPhysicalReleaseEffectResult.Released(x.EvidenceHash),
            GraphMediaPhysicalReleaseEffectQueryResult.Rejected x => new GraphMediaPhysicalReleaseEffectResult.Rejected(x.SafeCode),
            _ => new GraphMediaPhysicalReleaseEffectResult.Unknown()
        };
        return await RecordEffectAsync(command, body, effect, request, CancellationToken.None).ConfigureAwait(false);
    }

    private async ValueTask<GraphMediaPhysicalReleaseResult> RecordEffectAsync(
        AuthorityFactEnvelopeV1 command, GraphMediaPhysicalReleaseCommandBody body,
        GraphMediaPhysicalReleaseEffectResult effect, GraphMediaPhysicalReleaseRequest request,
        CancellationToken cancellationToken)
    {
        var outcome = effect switch { GraphMediaPhysicalReleaseEffectResult.Released => GraphMediaPhysicalReleaseOutcome.Released,
            GraphMediaPhysicalReleaseEffectResult.Rejected => GraphMediaPhysicalReleaseOutcome.Rejected, _ => GraphMediaPhysicalReleaseOutcome.Unknown };
        var evidence = (effect as GraphMediaPhysicalReleaseEffectResult.Released)?.EvidenceHash;
        var code = (effect as GraphMediaPhysicalReleaseEffectResult.Rejected)?.SafeCode;
        if (outcome == GraphMediaPhysicalReleaseOutcome.Rejected && code?.ToString() is not
            ("release-authority-stale" or "owner-terminal-mismatch" or "work-encumbered" or "fanout-incomplete" or
             "residence-mismatch" or "capacity-proof-mismatch" or "release-predecessor-conflict"))
        { outcome = GraphMediaPhysicalReleaseOutcome.Unknown; code = null; }
        var factBody = new GraphMediaPhysicalReleaseFactBody(command.Position, body.Residence.ResidenceId,
            body.Residence.RequestHash, body.Residence.GrantId, body.Residence.CurrentFact, body.Residence.Assignment,
            outcome, evidence, code, body.ObservedAt);
        var bytes = EncodeFact(command.Position.Session, request.ExpectedAuthority, factBody);
        var proposal = Proposal(GraphMediaPhysicalReleasePayloadRegistrations.Fact,
            GraphMediaPhysicalReleaseFactIds.Fact(command.Position), bytes, request.Correlation, request.ObservedAt);
        var history = await ReadAsync(command.Position.Session, request, CancellationToken.None).ConfigureAwait(false);
        if (history.Error is not null) return history.Error;
        var terminal = ToResult(history.Result!); if (terminal is not null) return terminal;
        var appended = await AppendAsync(command.Position.Session, history.Through, proposal, request, cancellationToken).ConfigureAwait(false);
        if (appended.Error is not null) return appended.Error;
        var final = await ReadAsync(command.Position.Session, request, CancellationToken.None).ConfigureAwait(false);
        return final.Error ?? ToResult(final.Result!) ?? Quarantine("release-history-invalid");
    }

    private async ValueTask<(GraphMediaPhysicalReleaseFoldResult? Result,
        GraphMediaWorkExecutionFoldSnapshot? WorkSnapshot, long Through,
        GraphMediaPhysicalReleaseResult? Error)> ReadAsync(SessionAuthorityStampV1 session,
        GraphMediaPhysicalReleaseRequest request, CancellationToken cancellationToken)
    {
        var fold = GraphMediaPhysicalReleaseFold.Create(session, request.ResidenceId, _registry);
        var workFold = GraphMediaWorkExecutionFold.Create(session, request.ResidenceId, _registry);
        long cursor = 0, through = long.MaxValue; ulong records = 0, bytes = 0;
        while (cursor < through)
        {
            ReadAuthorityRangeResultV1 read;
            try { read = await _journal.ReadAsync(new(session, cursor, through, 256, 1_048_576), cancellationToken).ConfigureAwait(false); }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
            catch { return (null, null, cursor, new GraphMediaPhysicalReleaseResult.StoreUnavailable(new("release-journal-unavailable"))); }
            if (read is not ReadAuthorityRangeResultV1.Batch batch)
                return (null, null, cursor, new GraphMediaPhysicalReleaseResult.StoreUnavailable(new("release-journal-unavailable")));
            through = batch.SnapshotThrough;
            if (batch.Facts.Count == 0) break;
            foreach (var envelope in batch.Facts)
            {
                records++; bytes += (ulong)AuthorityCanonicalCborV1.GetEnvelopeEncodedLength(envelope);
                if (records > request.MaximumSessionRecords || bytes > request.MaximumSessionCanonicalBytes)
                    return (null, null, cursor, Quarantine("release-history-invalid"));
                if (fold.Apply(envelope) is GraphMediaPhysicalReleaseFoldApplyResult.InvalidHistory invalid)
                    return (null, null, cursor, Quarantine(invalid.SafeCode.ToString()));
                if (workFold.Apply(envelope) is GraphMediaWorkExecutionFoldApplyResult.InvalidHistory workInvalid)
                    return (null, null, cursor, Quarantine(workInvalid.SafeCode.ToString()));
                cursor = envelope.Position.Sequence;
            }
            if (!batch.HasMore) break;
        }
        return (fold.Complete(), workFold.Snapshot(), cursor, null);
    }

    private async ValueTask<(AuthorityFactEnvelopeV1? Envelope, GraphMediaPhysicalReleaseResult? Error)> AppendAsync(
        SessionAuthorityStampV1 session, long expectedHead, ProposedAuthorityFactV1 proposal,
        GraphMediaPhysicalReleaseRequest request, CancellationToken cancellationToken)
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
                return (null, new GraphMediaPhysicalReleaseResult.StoreUnavailable(new("release-journal-capacity")));
            var reread = await ReadAsync(session, request, CancellationToken.None).ConfigureAwait(false);
            if (reread.Error is not null) return (null, reread.Error);
            var found = Find(reread.Result!, proposal.FactId);
            if (found is not null) return (found, null);
            if (proposal.PayloadSchema == GraphMediaPhysicalReleasePayloadRegistrations.Command.Schema)
            {
                if (reread.Result is GraphMediaPhysicalReleaseFoldResult.CommandOnly or
                    GraphMediaPhysicalReleaseFoldResult.Released or
                    GraphMediaPhysicalReleaseFoldResult.Rejected or
                    GraphMediaPhysicalReleaseFoldResult.Unknown)
                    return (null, new GraphMediaPhysicalReleaseResult.RetryRequired(new("release-predecessor-conflict")));
            }
            expectedHead = reread.Through;
            if (result is AppendAuthorityResultV1.StoreUnavailable)
                return (null, new GraphMediaPhysicalReleaseResult.StoreUnavailable(new("release-journal-unavailable")));
        }
        return (null, new GraphMediaPhysicalReleaseResult.RetryRequired(new("release-predecessor-conflict")));
    }

    private static AuthorityFactEnvelopeV1? Find(GraphMediaPhysicalReleaseFoldResult result, JournalFactId id) => result switch
    {
        GraphMediaPhysicalReleaseFoldResult.CommandOnly x when x.Command.FactId == id => x.Command,
        GraphMediaPhysicalReleaseFoldResult.Released x => x.Command.FactId == id ? x.Command : x.Fact.FactId == id ? x.Fact : null,
        GraphMediaPhysicalReleaseFoldResult.Unknown x => x.Command.FactId == id ? x.Command : x.Fact.FactId == id ? x.Fact : null,
        GraphMediaPhysicalReleaseFoldResult.Rejected x => x.Command.FactId == id ? x.Command : x.Fact.FactId == id ? x.Fact : null,
        _ => null
    };

    private static GraphMediaPhysicalReleaseResult? ToResult(GraphMediaPhysicalReleaseFoldResult result) => result switch
    {
        GraphMediaPhysicalReleaseFoldResult.Released x => new GraphMediaPhysicalReleaseResult.Released(x.Command, x.Fact, x.EvidenceHash),
        GraphMediaPhysicalReleaseFoldResult.Unknown x => new GraphMediaPhysicalReleaseResult.Unknown(x.Command, x.Fact),
        GraphMediaPhysicalReleaseFoldResult.Rejected x => new GraphMediaPhysicalReleaseResult.Rejected(x.Command, x.Fact, x.SafeCode),
        GraphMediaPhysicalReleaseFoldResult.InvalidHistory x => Quarantine(x.SafeCode.ToString()),
        _ => null
    };

    private static GraphMediaPhysicalReleaseEffectRequest Effect(GraphMediaPhysicalReleaseCommandBody body) =>
        new(body.OperationId, body.Residence.ResidenceId, body.Residence.RequestHash, body.Residence.GrantId,
            body.Residence.CurrentFact, body.Residence.Assignment);
    private static GraphMediaPhysicalReleaseCommandBody WithPredecessor(GraphMediaPhysicalReleaseCommandBody body,
        JournalPositionV1 predecessor) => new(body.OperationId, body.Residence, body.OwnerProof, body.WorkProof,
            body.FanoutProof, predecessor, body.ObservedAt);
    private static byte[] EncodeCommand(SessionAuthorityStampV1 session, ExpectedAuthorityVectorV1 authority,
        GraphMediaPhysicalReleaseCommandBody body) => GraphMediaPhysicalReleaseCodecs.EncodeOuter(
            new(session, authority, GraphMediaPhysicalReleaseCodecs.EncodeCommandBody(body)));
    private static byte[] EncodeFact(SessionAuthorityStampV1 session, ExpectedAuthorityVectorV1 authority,
        GraphMediaPhysicalReleaseFactBody body) => GraphMediaPhysicalReleaseCodecs.EncodeOuter(
            new(session, authority, GraphMediaPhysicalReleaseCodecs.EncodeFactBody(body)));
    private static ProposedAuthorityFactV1 Proposal(AuthorityPayloadRegistrationV1 registration, JournalFactId factId,
        byte[] bytes, CorrelationEnvelopeV1 correlation, UtcInstant observedAt) => new(factId, null, OwnerSliceId.S1,
            registration.Schema, bytes, AuthorityPayloadHashV1.Compute(registration.SchemaToken, registration.Schema, bytes), correlation, observedAt);
    private static bool SameAuthority(GraphMediaPhysicalReleaseCommandBody left, GraphMediaPhysicalReleaseCommandBody right) =>
        GraphMediaPhysicalReleaseCodecs.EncodeCommandBody(left).SequenceEqual(GraphMediaPhysicalReleaseCodecs.EncodeCommandBody(right));
    private static bool EnvelopeMatches(AuthorityFactEnvelopeV1 envelope, ProposedAuthorityFactV1 proposal) =>
        envelope.FactId == proposal.FactId && envelope.ThreadScope is null && envelope.Owner == proposal.Owner &&
        envelope.PayloadSchema == proposal.PayloadSchema && envelope.PayloadHash == proposal.PayloadHash &&
        envelope.Correlation == proposal.Correlation && envelope.ObservedAt == proposal.ObservedAt &&
        envelope.PayloadBytes.SequenceEqual(proposal.PayloadBytes);
    private static byte[] StableBytes(StableId128 value) { var bytes = new byte[16]; if (!value.TryWriteBytes(bytes)) throw new InvalidOperationException(); return bytes; }
    private static Hash256 HashReturned(IEnumerable<GraphMediaBorrowRecord> rows)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        hash.AppendData("hpd-s2-graph-media-returned-borrow-set-v1\0"u8);
        foreach (var row in rows) { hash.AppendData(StableBytes(row.TokenId)); if (row.ReturnHash is { } value) { var bytes = new byte[32]; value.TryWriteBytes(bytes); hash.AppendData(bytes); } }
        return Hash256.FromBytes(hash.GetHashAndReset());
    }
    private static GraphMediaPhysicalReleaseResult.Quarantined Quarantine(string code) => new(new(code));
}
