using HPD.Agent.Authority;

namespace HPD.Agent.Audio.Graph;

internal sealed record GraphMediaWorkEffectRequestV1(OperationId OperationId,
    GraphMediaWorkAuthorityV1 Work, IReadOnlyList<GraphMediaCleanupRegistrationV1> Cleanups);

internal abstract record GraphMediaWorkEffectResultV1
{
    private GraphMediaWorkEffectResultV1() { }
    internal sealed record Completed(Hash256 EvidenceHash) : GraphMediaWorkEffectResultV1;
    internal sealed record Rejected(BoundedAscii SafeCode) : GraphMediaWorkEffectResultV1;
    internal sealed record Unknown : GraphMediaWorkEffectResultV1;
}

internal abstract record GraphMediaWorkEffectQueryResultV1
{
    private GraphMediaWorkEffectQueryResultV1() { }
    internal sealed record Completed(Hash256 EvidenceHash) : GraphMediaWorkEffectQueryResultV1;
    internal sealed record Rejected(BoundedAscii SafeCode) : GraphMediaWorkEffectQueryResultV1;
    internal sealed record NotObserved : GraphMediaWorkEffectQueryResultV1;
    internal sealed record Unknown : GraphMediaWorkEffectQueryResultV1;
}

internal interface IGraphMediaWorkExecutionPortV1
{
    ValueTask<GraphMediaWorkEffectResultV1> ExecuteAsync(GraphMediaWorkEffectRequestV1 request,
        CancellationToken cancellationToken);
    ValueTask<GraphMediaWorkEffectQueryResultV1> QueryAsync(GraphMediaWorkEffectRequestV1 request,
        CancellationToken cancellationToken);
}

internal sealed record GraphMediaWorkExecutionRequestV1
{
    internal GraphMediaWorkExecutionRequestV1(OperationId operationId,
        GraphMediaWorkRegistrationV1 registration, GraphMediaResidenceLedgerV1 residences,
        GraphMediaOwnershipLedgerV1 ownership, GraphMediaWorkLedgerV1 work,
        ExpectedAuthorityVectorV1 expectedAuthority, CorrelationEnvelopeV1 correlation,
        MonotonicStampV1 effectObservedAt, UtcInstant observedAt,
        ulong maximumSessionRecords = 65_536, ulong maximumSessionCanonicalBytes = 67_108_864)
    {
        if (!operationId.IsValid || registration is null || residences is null || ownership is null || work is null ||
            expectedAuthority is null || !correlation.IsValid || correlation.OperationId != operationId ||
            correlation.ParticipantId is not null || !effectObservedAt.IsValid ||
            maximumSessionRecords is 0 or > 65_536 || maximumSessionCanonicalBytes is 0 or > 67_108_864)
            throw new ArgumentException("A valid bounded durable-work request is required.");
        OperationId = operationId; Registration = registration; Residences = residences; Ownership = ownership;
        Work = work; ExpectedAuthority = expectedAuthority; Correlation = correlation;
        EffectObservedAt = effectObservedAt; ObservedAt = observedAt;
        MaximumSessionRecords = maximumSessionRecords; MaximumSessionCanonicalBytes = maximumSessionCanonicalBytes;
    }
    internal OperationId OperationId { get; }
    internal GraphMediaWorkRegistrationV1 Registration { get; }
    internal GraphMediaResidenceLedgerV1 Residences { get; }
    internal GraphMediaOwnershipLedgerV1 Ownership { get; }
    internal GraphMediaWorkLedgerV1 Work { get; }
    internal ExpectedAuthorityVectorV1 ExpectedAuthority { get; }
    internal CorrelationEnvelopeV1 Correlation { get; }
    internal MonotonicStampV1 EffectObservedAt { get; }
    internal UtcInstant ObservedAt { get; }
    internal ulong MaximumSessionRecords { get; }
    internal ulong MaximumSessionCanonicalBytes { get; }
}

internal abstract record GraphMediaWorkExecutionResultV1
{
    private GraphMediaWorkExecutionResultV1() { }
    internal sealed record Completed(AuthorityFactEnvelopeV1 Command, AuthorityFactEnvelopeV1 Fact,
        Hash256 EvidenceHash, GraphMediaWorkLedgerV1 Ledger) : GraphMediaWorkExecutionResultV1;
    internal sealed record Unknown(AuthorityFactEnvelopeV1 Command, AuthorityFactEnvelopeV1 Fact,
        GraphMediaWorkLedgerV1 Ledger) : GraphMediaWorkExecutionResultV1;
    internal sealed record Rejected(AuthorityFactEnvelopeV1 Command, AuthorityFactEnvelopeV1 Fact,
        BoundedAscii SafeCode, GraphMediaWorkLedgerV1 Ledger) : GraphMediaWorkExecutionResultV1;
    internal sealed record RetryRequired(BoundedAscii SafeCode) : GraphMediaWorkExecutionResultV1;
    internal sealed record StoreUnavailable(BoundedAscii SafeCode) : GraphMediaWorkExecutionResultV1;
    internal sealed record Quarantined(BoundedAscii SafeCode) : GraphMediaWorkExecutionResultV1;
}

internal sealed class GraphMediaWorkExecutionCoordinatorV1
{
    private const int MaximumAppendAttempts = 3;
    private readonly IAuthorityJournalV1 _journal;
    private readonly IGraphMediaWorkExecutionPortV1 _effects;
    private readonly AuthorityPayloadAdmissionRegistryV1 _registry;
    private readonly SemaphoreSlim _mutex = new(1, 1);

    internal GraphMediaWorkExecutionCoordinatorV1(IAuthorityJournalV1 journal,
        IGraphMediaWorkExecutionPortV1 effects, AuthorityPayloadAdmissionRegistryV1 registry)
    { _journal = journal ?? throw new ArgumentNullException(nameof(journal)); _effects = effects ?? throw new ArgumentNullException(nameof(effects)); _registry = registry ?? throw new ArgumentNullException(nameof(registry)); }

    internal async ValueTask<GraphMediaWorkExecutionResultV1> ExecuteAsync(
        GraphMediaWorkExecutionRequestV1 request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request); cancellationToken.ThrowIfCancellationRequested();
        await _mutex.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var authenticated = Authenticate(request);
            if (authenticated.Error is not null) return authenticated.Error;
            var body = authenticated.Body!; var projection = authenticated.Ledger!;
            var session = request.Residences.Session;
            var history = await ReadAsync(session, request, cancellationToken).ConfigureAwait(false);
            if (history.Error is not null) return history.Error;
            var priorResult = ToResult(history.Result!, projection); if (priorResult is not null) return priorResult;
            if (history.Result is GraphMediaWorkExecutionFoldResultV1.CommandOnly pending)
            {
                if (pending.Body.OperationId != request.OperationId || !SameCommand(pending.Body, body) ||
                    !GraphMediaWorkExecutionCodecsV1.TryDecodeOuter(pending.Command.PayloadMemory, out var outer) ||
                    outer is null || outer.ExpectedAuthority != request.ExpectedAuthority)
                    return Quarantine("contradictory-duplicate");
                projection = EnsureRunning(projection, body.Work);
                if (projection is null) return Quarantine("work-authority-stale");
                return await ResolveCommandOnlyAsync(pending.Command, pending.Body, projection, request, cancellationToken).ConfigureAwait(false);
            }
            if (history.Latest is GraphMediaWorkExecutionFoldResultV1.CommandOnly)
                return new GraphMediaWorkExecutionResultV1.RetryRequired(new("work-predecessor-conflict"));
            if (TerminalFact(history.Latest!) is { } predecessor)
                body = WithPredecessor(body, predecessor.Position);
            if (!projection.Work.TryGetValue(body.Work.WorkId, out var projectedWork) ||
                projectedWork.State is not (GraphMediaWorkStateV1.Registered or GraphMediaWorkStateV1.Running))
                return Quarantine("work-authority-stale");

            var commandProposal = Proposal(GraphMediaWorkExecutionPayloadRegistrationsV1.Command,
                GraphMediaWorkExecutionFactIdsV1.Command(session, request.OperationId),
                EncodeCommand(session, request.ExpectedAuthority, body), request.Correlation, request.ObservedAt);
            var appended = await AppendAsync(session, history.Through, commandProposal, request, cancellationToken).ConfigureAwait(false);
            if (appended.Error is not null) return appended.Error;
            var command = appended.Envelope!;
            projection = EnsureRunning(projection, body.Work);
            if (projection is null) return Quarantine("work-authority-stale");

            GraphMediaWorkEffectResultV1 effect;
            var invoked = false;
            try
            {
                cancellationToken.ThrowIfCancellationRequested(); invoked = true;
                effect = await _effects.ExecuteAsync(Effect(body), cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (invoked)
            { return await ResolveAmbiguousEffectAsync(command, body, projection, request).ConfigureAwait(false); }
            catch (Exception) when (invoked)
            { return await ResolveAmbiguousEffectAsync(command, body, projection, request).ConfigureAwait(false); }
            if (!Valid(effect)) return Quarantine("work-effect-invalid");
            return await RecordEffectAsync(command, body, effect, projection, request, CancellationToken.None).ConfigureAwait(false);
        }
        finally { _mutex.Release(); }
    }

    private (GraphMediaWorkExecutionCommandBodyV1? Body, GraphMediaWorkLedgerV1? Ledger,
        GraphMediaWorkExecutionResultV1? Error) Authenticate(GraphMediaWorkExecutionRequestV1 request)
    {
        var session = request.Residences.Session;
        if (request.Ownership.Session != session || request.Work.Session != session ||
            request.ExpectedAuthority.Session != session || request.Residences.GraphGeneration != request.Ownership.GraphGeneration ||
            request.Residences.GraphGeneration != request.Work.GraphGeneration)
            return (null, null, Quarantine("work-authority-stale"));
        var graphs = request.ExpectedAuthority.Axes.Where(x => x.AxisId == AuthorityAxisId.Graph && x.Value is AuthorityAxisValueV1.Graph)
            .Select(x => ((AuthorityAxisValueV1.Graph)x.Value).Value).ToArray();
        if (graphs.Length != 1 || graphs[0] != request.Residences.GraphGeneration)
            return (null, null, Quarantine("work-authority-stale"));
        var registered = request.Work.Register(request.Registration, request.Residences, request.Ownership);
        GraphMediaWorkLedgerV1 ledger;
        if (registered.Result == GraphMediaWorkResultV1.Registered) ledger = registered.Ledger;
        else if (registered.Result == GraphMediaWorkResultV1.IdempotentRegistered) ledger = request.Work;
        else return (null, null, Quarantine(registered.Result switch
        {
            GraphMediaWorkResultV1.OwnerMismatch => "work-owner-mismatch",
            GraphMediaWorkResultV1.ResidenceNotFound or GraphMediaWorkResultV1.ResidenceNotVisible => "work-residence-mismatch",
            _ => "work-authority-stale"
        }));
        if (!ledger.Work.TryGetValue(request.Registration.WorkId, out var work) ||
            work.RequestHash != request.Registration.RequestHash || !work.ResidenceId.Equals(request.Registration.ResidenceId))
            return (null, null, Quarantine("work-authority-stale"));
        var cleanups = ledger.Cleanup.Values.Where(x => x.WorkId.Equals(work.WorkId)).OrderBy(x => x.RegistrationOrdinal)
            .Select(x => new GraphMediaCleanupRegistrationV1(x.CleanupId, x.RequestHash)).ToArray();
        if (cleanups.Length != request.Registration.Cleanups.Count ||
            !cleanups.SequenceEqual(request.Registration.Cleanups))
            return (null, null, Quarantine("work-authority-stale"));
        return (new(request.OperationId, GraphMediaWorkAuthorityV1.FromRecord(work), cleanups, null, request.EffectObservedAt), ledger, null);
    }

    private async ValueTask<GraphMediaWorkExecutionResultV1> ResolveCommandOnlyAsync(
        AuthorityFactEnvelopeV1 command, GraphMediaWorkExecutionCommandBodyV1 body,
        GraphMediaWorkLedgerV1 projection, GraphMediaWorkExecutionRequestV1 request,
        CancellationToken cancellationToken)
    {
        GraphMediaWorkEffectQueryResultV1 query;
        try { query = await _effects.QueryAsync(Effect(body), cancellationToken).ConfigureAwait(false); }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch { return new GraphMediaWorkExecutionResultV1.StoreUnavailable(new("work-query-unavailable")); }
        if (!Valid(query)) return Quarantine("work-effect-invalid");
        if (query is GraphMediaWorkEffectQueryResultV1.NotObserved)
        {
            GraphMediaWorkEffectResultV1 effect;
            var invoked = false;
            try { cancellationToken.ThrowIfCancellationRequested(); invoked = true; effect = await _effects.ExecuteAsync(Effect(body), cancellationToken).ConfigureAwait(false); }
            catch (OperationCanceledException) when (invoked) { return await ResolveAmbiguousEffectAsync(command, body, projection, request).ConfigureAwait(false); }
            catch (Exception) when (invoked) { return await ResolveAmbiguousEffectAsync(command, body, projection, request).ConfigureAwait(false); }
            if (!Valid(effect)) return Quarantine("work-effect-invalid");
            return await RecordEffectAsync(command, body, effect, projection, request, CancellationToken.None).ConfigureAwait(false);
        }
        return await RecordEffectAsync(command, body, FromQuery(query), projection, request, CancellationToken.None).ConfigureAwait(false);
    }

    private async ValueTask<GraphMediaWorkExecutionResultV1> ResolveAmbiguousEffectAsync(
        AuthorityFactEnvelopeV1 command, GraphMediaWorkExecutionCommandBodyV1 body,
        GraphMediaWorkLedgerV1 projection, GraphMediaWorkExecutionRequestV1 request)
    {
        GraphMediaWorkEffectQueryResultV1 query;
        try { query = await _effects.QueryAsync(Effect(body), CancellationToken.None).ConfigureAwait(false); }
        catch { query = new GraphMediaWorkEffectQueryResultV1.Unknown(); }
        if (!Valid(query) || query is GraphMediaWorkEffectQueryResultV1.NotObserved)
            query = new GraphMediaWorkEffectQueryResultV1.Unknown();
        return await RecordEffectAsync(command, body, FromQuery(query), projection, request, CancellationToken.None).ConfigureAwait(false);
    }

    private async ValueTask<GraphMediaWorkExecutionResultV1> RecordEffectAsync(
        AuthorityFactEnvelopeV1 command, GraphMediaWorkExecutionCommandBodyV1 body,
        GraphMediaWorkEffectResultV1 effect, GraphMediaWorkLedgerV1 projection,
        GraphMediaWorkExecutionRequestV1 request, CancellationToken cancellationToken)
    {
        var outcome = effect switch
        {
            GraphMediaWorkEffectResultV1.Completed => GraphMediaWorkExecutionOutcomeV1.Completed,
            GraphMediaWorkEffectResultV1.Rejected => GraphMediaWorkExecutionOutcomeV1.Rejected,
            _ => GraphMediaWorkExecutionOutcomeV1.Unknown
        };
        var evidence = (effect as GraphMediaWorkEffectResultV1.Completed)?.EvidenceHash;
        var code = (effect as GraphMediaWorkEffectResultV1.Rejected)?.SafeCode;
        if (outcome == GraphMediaWorkExecutionOutcomeV1.Rejected && code?.ToString() is not
            ("work-authority-stale" or "work-residence-mismatch" or "work-owner-mismatch" or
             "work-predecessor-conflict" or "work-effect-rejected"))
        { outcome = GraphMediaWorkExecutionOutcomeV1.Unknown; code = null; }
        var fact = new GraphMediaWorkExecutionFactBodyV1(command.Position, body.Work.WorkId,
            body.Work.RequestHash, outcome, evidence, code, body.ObservedAt);
        var proposal = Proposal(GraphMediaWorkExecutionPayloadRegistrationsV1.Fact,
            GraphMediaWorkExecutionFactIdsV1.Fact(command.Position),
            EncodeFact(command.Position.Session, request.ExpectedAuthority, fact), request.Correlation, request.ObservedAt);
        var history = await ReadAsync(command.Position.Session, request, CancellationToken.None).ConfigureAwait(false);
        if (history.Error is not null) return history.Error;
        var existing = ToResult(history.Result!, projection); if (existing is not null) return existing;
        var appended = await AppendAsync(command.Position.Session, history.Through, proposal, request, cancellationToken).ConfigureAwait(false);
        if (appended.Error is not null) return appended.Error;
        var final = await ReadAsync(command.Position.Session, request, CancellationToken.None).ConfigureAwait(false);
        return final.Error ?? ToResult(final.Result!, projection) ?? Quarantine("work-history-invalid");
    }

    private async ValueTask<(GraphMediaWorkExecutionFoldResultV1? Result,
        GraphMediaWorkExecutionFoldResultV1? Latest, long Through,
        GraphMediaWorkExecutionResultV1? Error)> ReadAsync(SessionAuthorityStampV1 session,
        GraphMediaWorkExecutionRequestV1 request, CancellationToken cancellationToken)
    {
        var fold = GraphMediaWorkExecutionFoldV1.Create(session, request.Registration.ResidenceId, _registry);
        long cursor = 0, through = long.MaxValue; ulong records = 0, bytes = 0;
        while (cursor < through)
        {
            ReadAuthorityRangeResultV1 read;
            try { read = await _journal.ReadAsync(new(session, cursor, through, 256, 1_048_576), cancellationToken).ConfigureAwait(false); }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
            catch { return (null, null, cursor, new GraphMediaWorkExecutionResultV1.StoreUnavailable(new("work-journal-unavailable"))); }
            if (read is not ReadAuthorityRangeResultV1.Batch batch || batch.AfterExclusive != cursor ||
                through != long.MaxValue && batch.SnapshotThrough != through)
                return (null, null, cursor, new GraphMediaWorkExecutionResultV1.StoreUnavailable(new("work-journal-unavailable")));
            through = batch.SnapshotThrough;
            if (batch.Facts.Count == 0) { if (batch.HasMore) return (null, null, cursor, Quarantine("work-history-invalid")); break; }
            foreach (var envelope in batch.Facts)
            {
                records++; bytes += (ulong)AuthorityCanonicalCborV1.GetEnvelopeEncodedLength(envelope);
                if (records > request.MaximumSessionRecords || bytes > request.MaximumSessionCanonicalBytes)
                    return (null, null, cursor, Quarantine("work-history-invalid"));
                if (fold.Apply(envelope) is GraphMediaWorkExecutionFoldApplyResultV1.InvalidHistory invalid)
                    return (null, null, cursor, Quarantine(invalid.SafeCode.ToString()));
                cursor = envelope.Position.Sequence;
            }
            if (!batch.HasMore) break;
        }
        var latest = fold.Complete();
        return (fold.Query(request.OperationId), latest, cursor, null);
    }

    private async ValueTask<(AuthorityFactEnvelopeV1? Envelope, GraphMediaWorkExecutionResultV1? Error)> AppendAsync(
        SessionAuthorityStampV1 session, long expectedHead, ProposedAuthorityFactV1 proposal,
        GraphMediaWorkExecutionRequestV1 request, CancellationToken cancellationToken)
    {
        for (var attempt = 0; attempt < MaximumAppendAttempts; attempt++)
        {
            AppendAuthorityResultV1 result;
            cancellationToken.ThrowIfCancellationRequested();
            var invoked = false;
            try
            {
                invoked = true;
                result = await _journal.AppendAsync(new(session, expectedHead, [], [proposal], 1_048_576), cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (invoked)
            { result = new AppendAuthorityResultV1.OutcomeUnknown(request.OperationId); }
            catch (Exception) when (invoked) { result = new AppendAuthorityResultV1.OutcomeUnknown(request.OperationId); }
            if (result is AppendAuthorityResultV1.Committed committed && committed.Envelopes.Count == 1)
                return EnvelopeMatches(committed.Envelopes[0], proposal) ? (committed.Envelopes[0], null) : (null, Quarantine("work-append-invalid"));
            if (result is AppendAuthorityResultV1.AlreadyCommitted already && already.Envelopes.Count == 1)
                return EnvelopeMatches(already.Envelopes[0], proposal) ? (already.Envelopes[0], null) : (null, Quarantine("work-append-invalid"));
            if (result is AppendAuthorityResultV1.InvalidPayload or AppendAuthorityResultV1.UnknownSchema or
                AppendAuthorityResultV1.ThreadConflict or AppendAuthorityResultV1.ContradictoryDuplicate)
                return (null, Quarantine("work-append-invalid"));
            if (result is AppendAuthorityResultV1.CapacityRefused)
                return (null, new GraphMediaWorkExecutionResultV1.StoreUnavailable(new("work-journal-capacity")));
            var reread = await ReadAsync(session, request, CancellationToken.None).ConfigureAwait(false);
            if (reread.Error is not null) return (null, reread.Error);
            var found = Find(reread.Result!, proposal.FactId); if (found is not null) return (found, null);
            cancellationToken.ThrowIfCancellationRequested();
            expectedHead = reread.Through;
            if (result is AppendAuthorityResultV1.StoreUnavailable)
                return (null, new GraphMediaWorkExecutionResultV1.StoreUnavailable(new("work-journal-unavailable")));
        }
        return (null, new GraphMediaWorkExecutionResultV1.RetryRequired(new("work-predecessor-conflict")));
    }

    private static GraphMediaWorkLedgerV1? EnsureRunning(GraphMediaWorkLedgerV1 ledger, GraphMediaWorkAuthorityV1 work)
    {
        if (!ledger.Work.TryGetValue(work.WorkId, out var row)) return null;
        if (row.State == GraphMediaWorkStateV1.Running) return ledger;
        if (row.State != GraphMediaWorkStateV1.Registered) return null;
        var transition = ledger.StartWork(work.WorkId, work.RequestHash);
        return transition.Result == GraphMediaWorkResultV1.Running ? transition.Ledger : null;
    }

    private static GraphMediaWorkExecutionResultV1 Project(GraphMediaWorkExecutionFoldResultV1.Rejected x,
        GraphMediaWorkLedgerV1 ledger)
    {
        if (!ledger.Work.TryGetValue(x.CommandBody.Work.WorkId, out var row)) return Quarantine("work-authority-stale");
        if (row.State == GraphMediaWorkStateV1.Running)
            ledger = ledger.RejectWork(row.WorkId, row.RequestHash).Ledger;
        else if (row.State != GraphMediaWorkStateV1.Registered)
            return Quarantine("work-authority-stale");
        return new GraphMediaWorkExecutionResultV1.Rejected(x.Command, x.Fact, x.SafeCode, ledger);
    }

    private static GraphMediaWorkExecutionResultV1? ToResult(GraphMediaWorkExecutionFoldResultV1 result,
        GraphMediaWorkLedgerV1 ledger) => result switch
        {
        GraphMediaWorkExecutionFoldResultV1.Completed x => CompletedResult(x, ledger),
        GraphMediaWorkExecutionFoldResultV1.Unknown x => UnknownResult(x, ledger),
            GraphMediaWorkExecutionFoldResultV1.Rejected x => Project(x, ledger),
            GraphMediaWorkExecutionFoldResultV1.InvalidHistory x => Quarantine(x.SafeCode.ToString()),
            _ => null
        };

    private static AuthorityFactEnvelopeV1? TerminalFact(GraphMediaWorkExecutionFoldResultV1 result) => result switch
    {
        GraphMediaWorkExecutionFoldResultV1.Completed x => x.Fact,
        GraphMediaWorkExecutionFoldResultV1.Unknown x => x.Fact,
        GraphMediaWorkExecutionFoldResultV1.Rejected x => x.Fact,
        _ => null
    };

    private static GraphMediaWorkExecutionResultV1 CompletedResult(
        GraphMediaWorkExecutionFoldResultV1.Completed result, GraphMediaWorkLedgerV1 ledger)
    {
        var projected = ProjectCompleted(ledger, result.CommandBody.Work, result.EvidenceHash);
        return projected is null ? Quarantine("work-authority-stale") :
            new GraphMediaWorkExecutionResultV1.Completed(result.Command, result.Fact, result.EvidenceHash, projected);
    }

    private static GraphMediaWorkLedgerV1? ProjectCompleted(GraphMediaWorkLedgerV1 ledger,
        GraphMediaWorkAuthorityV1 work, Hash256 evidence)
    {
        if (!ledger.Work.TryGetValue(work.WorkId, out var row) || row.RequestHash != work.RequestHash) return null;
        if (row.State == GraphMediaWorkStateV1.Terminal)
            return row.OutcomeHash == evidence ? ledger : null;
        if (row.State == GraphMediaWorkStateV1.Unknown)
        {
            var reconciled = ledger.ReconcileWork(work.WorkId, work.RequestHash, true, evidence);
            return reconciled.Result == GraphMediaWorkResultV1.ReconciledTerminal ? reconciled.Ledger : null;
        }
        var running = EnsureRunning(ledger, work); if (running is null) return null;
        var finished = running.FinishWork(work.WorkId, work.RequestHash, evidence);
        return finished.Result == GraphMediaWorkResultV1.Terminal ? finished.Ledger : null;
    }

    private static GraphMediaWorkExecutionResultV1 UnknownResult(
        GraphMediaWorkExecutionFoldResultV1.Unknown result, GraphMediaWorkLedgerV1 ledger)
    {
        var projected = ProjectUnknown(ledger, result.CommandBody.Work);
        return projected is null ? Quarantine("work-authority-stale") :
            new GraphMediaWorkExecutionResultV1.Unknown(result.Command, result.Fact, projected);
    }

    private static GraphMediaWorkLedgerV1? ProjectUnknown(GraphMediaWorkLedgerV1 ledger,
        GraphMediaWorkAuthorityV1 work)
    {
        if (!ledger.Work.TryGetValue(work.WorkId, out var row) || row.RequestHash != work.RequestHash) return null;
        if (row.State == GraphMediaWorkStateV1.Unknown) return ledger;
        if (row.State == GraphMediaWorkStateV1.Terminal) return null;
        var running = EnsureRunning(ledger, work); if (running is null) return null;
        var unknown = running.LoseWorkOutcome(work.WorkId, work.RequestHash);
        return unknown.Result == GraphMediaWorkResultV1.OutcomeUnknown ? unknown.Ledger : null;
    }

    private static AuthorityFactEnvelopeV1? Find(GraphMediaWorkExecutionFoldResultV1 result, JournalFactId id) => result switch
    {
        GraphMediaWorkExecutionFoldResultV1.CommandOnly x when x.Command.FactId == id => x.Command,
        GraphMediaWorkExecutionFoldResultV1.Completed x => x.Command.FactId == id ? x.Command : x.Fact.FactId == id ? x.Fact : null,
        GraphMediaWorkExecutionFoldResultV1.Unknown x => x.Command.FactId == id ? x.Command : x.Fact.FactId == id ? x.Fact : null,
        GraphMediaWorkExecutionFoldResultV1.Rejected x => x.Command.FactId == id ? x.Command : x.Fact.FactId == id ? x.Fact : null,
        _ => null
    };

    private static GraphMediaWorkEffectRequestV1 Effect(GraphMediaWorkExecutionCommandBodyV1 body) =>
        new(body.OperationId, body.Work, body.Cleanups);
    private static GraphMediaWorkEffectResultV1 FromQuery(GraphMediaWorkEffectQueryResultV1 query) => query switch
    {
        GraphMediaWorkEffectQueryResultV1.Completed x => new GraphMediaWorkEffectResultV1.Completed(x.EvidenceHash),
        GraphMediaWorkEffectQueryResultV1.Rejected x => new GraphMediaWorkEffectResultV1.Rejected(x.SafeCode),
        _ => new GraphMediaWorkEffectResultV1.Unknown()
    };
    private static bool Valid(GraphMediaWorkEffectResultV1? result) => result is not null &&
        (result is not GraphMediaWorkEffectResultV1.Completed completed || completed.EvidenceHash != default) &&
        (result is not GraphMediaWorkEffectResultV1.Rejected rejected || rejected.SafeCode.ToString() is
            "work-authority-stale" or "work-residence-mismatch" or "work-owner-mismatch" or "work-predecessor-conflict" or "work-effect-rejected");
    private static bool Valid(GraphMediaWorkEffectQueryResultV1? result) => result is not null &&
        (result is not GraphMediaWorkEffectQueryResultV1.Completed completed || completed.EvidenceHash != default) &&
        (result is not GraphMediaWorkEffectQueryResultV1.Rejected rejected || rejected.SafeCode.ToString() is
            "work-authority-stale" or "work-residence-mismatch" or "work-owner-mismatch" or "work-predecessor-conflict" or "work-effect-rejected");
    private static GraphMediaWorkExecutionCommandBodyV1 WithPredecessor(GraphMediaWorkExecutionCommandBodyV1 body,
        JournalPositionV1 predecessor) => new(body.OperationId, body.Work, body.Cleanups, predecessor, body.ObservedAt);
    private static bool SameCommand(GraphMediaWorkExecutionCommandBodyV1 left, GraphMediaWorkExecutionCommandBodyV1 right) =>
        GraphMediaWorkExecutionCodecsV1.EncodeCommandBody(left).SequenceEqual(GraphMediaWorkExecutionCodecsV1.EncodeCommandBody(right));
    private static byte[] EncodeCommand(SessionAuthorityStampV1 session, ExpectedAuthorityVectorV1 authority,
        GraphMediaWorkExecutionCommandBodyV1 body) => GraphMediaWorkExecutionCodecsV1.EncodeOuter(
            new(session, authority, GraphMediaWorkExecutionCodecsV1.EncodeCommandBody(body)));
    private static byte[] EncodeFact(SessionAuthorityStampV1 session, ExpectedAuthorityVectorV1 authority,
        GraphMediaWorkExecutionFactBodyV1 body) => GraphMediaWorkExecutionCodecsV1.EncodeOuter(
            new(session, authority, GraphMediaWorkExecutionCodecsV1.EncodeFactBody(body)));
    private static ProposedAuthorityFactV1 Proposal(AuthorityPayloadRegistrationV1 registration, JournalFactId factId,
        byte[] payload, CorrelationEnvelopeV1 correlation, UtcInstant observedAt) => new(factId, null, OwnerSliceId.S1,
            registration.Schema, payload, AuthorityPayloadHashV1.Compute(registration.SchemaToken, registration.Schema, payload), correlation, observedAt);
    private static bool EnvelopeMatches(AuthorityFactEnvelopeV1 envelope, ProposedAuthorityFactV1 proposal) =>
        envelope.FactId == proposal.FactId && envelope.ThreadScope is null && envelope.Owner == proposal.Owner &&
        envelope.PayloadSchema == proposal.PayloadSchema && envelope.PayloadHash == proposal.PayloadHash &&
        envelope.Correlation == proposal.Correlation && envelope.ObservedAt == proposal.ObservedAt &&
        envelope.PayloadBytes.SequenceEqual(proposal.PayloadBytes);
    private static GraphMediaWorkExecutionResultV1.Quarantined Quarantine(string code) => new(new(code));
}
