using HPD.Agent.Authority;

namespace HPD.Agent.Audio.Graph;

internal sealed record GraphMediaWorkEffectRequest(OperationId OperationId,
    GraphMediaWorkAuthority Work, IReadOnlyList<GraphMediaCleanupRegistration> Cleanups);

internal abstract record GraphMediaWorkEffectResult
{
    private GraphMediaWorkEffectResult() { }
    internal sealed record Completed(Hash256 EvidenceHash) : GraphMediaWorkEffectResult;
    internal sealed record Rejected(BoundedAscii SafeCode) : GraphMediaWorkEffectResult;
    internal sealed record Unknown : GraphMediaWorkEffectResult;
}

internal abstract record GraphMediaWorkEffectQueryResult
{
    private GraphMediaWorkEffectQueryResult() { }
    internal sealed record Completed(Hash256 EvidenceHash) : GraphMediaWorkEffectQueryResult;
    internal sealed record Rejected(BoundedAscii SafeCode) : GraphMediaWorkEffectQueryResult;
    internal sealed record NotObserved : GraphMediaWorkEffectQueryResult;
    internal sealed record Unknown : GraphMediaWorkEffectQueryResult;
}

internal interface IGraphMediaWorkExecutionPort
{
    ValueTask<GraphMediaWorkEffectResult> ExecuteAsync(GraphMediaWorkEffectRequest request,
        CancellationToken cancellationToken);
    ValueTask<GraphMediaWorkEffectQueryResult> QueryAsync(GraphMediaWorkEffectRequest request,
        CancellationToken cancellationToken);
}

internal sealed record GraphMediaWorkExecutionRequest
{
    internal GraphMediaWorkExecutionRequest(OperationId operationId,
        GraphMediaWorkRegistration registration, GraphMediaResidenceLedger residences,
        GraphMediaOwnershipLedger ownership, GraphMediaWorkLedger work,
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
    internal GraphMediaWorkRegistration Registration { get; }
    internal GraphMediaResidenceLedger Residences { get; }
    internal GraphMediaOwnershipLedger Ownership { get; }
    internal GraphMediaWorkLedger Work { get; }
    internal ExpectedAuthorityVectorV1 ExpectedAuthority { get; }
    internal CorrelationEnvelopeV1 Correlation { get; }
    internal MonotonicStampV1 EffectObservedAt { get; }
    internal UtcInstant ObservedAt { get; }
    internal ulong MaximumSessionRecords { get; }
    internal ulong MaximumSessionCanonicalBytes { get; }
}

internal abstract record GraphMediaWorkExecutionResult
{
    private GraphMediaWorkExecutionResult() { }
    internal sealed record Completed(AuthorityFactEnvelopeV1 Command, AuthorityFactEnvelopeV1 Fact,
        Hash256 EvidenceHash, GraphMediaWorkLedger Ledger) : GraphMediaWorkExecutionResult;
    internal sealed record Unknown(AuthorityFactEnvelopeV1 Command, AuthorityFactEnvelopeV1 Fact,
        GraphMediaWorkLedger Ledger) : GraphMediaWorkExecutionResult;
    internal sealed record Rejected(AuthorityFactEnvelopeV1 Command, AuthorityFactEnvelopeV1 Fact,
        BoundedAscii SafeCode, GraphMediaWorkLedger Ledger) : GraphMediaWorkExecutionResult;
    internal sealed record RetryRequired(BoundedAscii SafeCode) : GraphMediaWorkExecutionResult;
    internal sealed record StoreUnavailable(BoundedAscii SafeCode) : GraphMediaWorkExecutionResult;
    internal sealed record Quarantined(BoundedAscii SafeCode) : GraphMediaWorkExecutionResult;
}

internal sealed class GraphMediaWorkExecutionCoordinator
{
    private const int MaximumAppendAttempts = 3;
    private readonly IAuthorityJournalV1 _journal;
    private readonly IGraphMediaWorkExecutionPort _effects;
    private readonly AuthorityPayloadAdmissionRegistryV1 _registry;
    private readonly SemaphoreSlim _mutex = new(1, 1);

    internal GraphMediaWorkExecutionCoordinator(IAuthorityJournalV1 journal,
        IGraphMediaWorkExecutionPort effects, AuthorityPayloadAdmissionRegistryV1 registry)
    { _journal = journal ?? throw new ArgumentNullException(nameof(journal)); _effects = effects ?? throw new ArgumentNullException(nameof(effects)); _registry = registry ?? throw new ArgumentNullException(nameof(registry)); }

    internal async ValueTask<GraphMediaWorkExecutionResult> ExecuteAsync(
        GraphMediaWorkExecutionRequest request, CancellationToken cancellationToken)
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
            if (history.Result is GraphMediaWorkExecutionFoldResult.CommandOnly pending)
            {
                if (pending.Body.OperationId != request.OperationId || !SameCommand(pending.Body, body) ||
                    !GraphMediaWorkExecutionCodecs.TryDecodeOuter(pending.Command.PayloadMemory, out var outer) ||
                    outer is null || outer.ExpectedAuthority != request.ExpectedAuthority)
                    return Quarantine("contradictory-duplicate");
                projection = EnsureRunning(projection, body.Work);
                if (projection is null) return Quarantine("work-authority-stale");
                return await ResolveCommandOnlyAsync(pending.Command, pending.Body, projection, request, cancellationToken).ConfigureAwait(false);
            }
            if (history.Latest is GraphMediaWorkExecutionFoldResult.CommandOnly)
                return new GraphMediaWorkExecutionResult.RetryRequired(new("work-predecessor-conflict"));
            if (TerminalFact(history.Latest!) is { } predecessor)
                body = WithPredecessor(body, predecessor.Position);
            if (!projection.Work.TryGetValue(body.Work.WorkId, out var projectedWork) ||
                projectedWork.State is not (GraphMediaWorkState.Registered or GraphMediaWorkState.Running))
                return Quarantine("work-authority-stale");

            var commandProposal = Proposal(GraphMediaWorkExecutionPayloadRegistrations.Command,
                GraphMediaWorkExecutionFactIds.Command(session, request.OperationId),
                EncodeCommand(session, request.ExpectedAuthority, body), request.Correlation, request.ObservedAt);
            var appended = await AppendAsync(session, history.Through, commandProposal, request, cancellationToken).ConfigureAwait(false);
            if (appended.Error is not null) return appended.Error;
            var command = appended.Envelope!;
            projection = EnsureRunning(projection, body.Work);
            if (projection is null) return Quarantine("work-authority-stale");

            GraphMediaWorkEffectResult effect;
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

    private (GraphMediaWorkExecutionCommandBody? Body, GraphMediaWorkLedger? Ledger,
        GraphMediaWorkExecutionResult? Error) Authenticate(GraphMediaWorkExecutionRequest request)
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
        GraphMediaWorkLedger ledger;
        if (registered.Result == GraphMediaWorkResult.Registered) ledger = registered.Ledger;
        else if (registered.Result == GraphMediaWorkResult.IdempotentRegistered) ledger = request.Work;
        else return (null, null, Quarantine(registered.Result switch
        {
            GraphMediaWorkResult.OwnerMismatch => "work-owner-mismatch",
            GraphMediaWorkResult.ResidenceNotFound or GraphMediaWorkResult.ResidenceNotVisible => "work-residence-mismatch",
            _ => "work-authority-stale"
        }));
        if (!ledger.Work.TryGetValue(request.Registration.WorkId, out var work) ||
            work.RequestHash != request.Registration.RequestHash || !work.ResidenceId.Equals(request.Registration.ResidenceId))
            return (null, null, Quarantine("work-authority-stale"));
        var cleanups = ledger.Cleanup.Values.Where(x => x.WorkId.Equals(work.WorkId)).OrderBy(x => x.RegistrationOrdinal)
            .Select(x => new GraphMediaCleanupRegistration(x.CleanupId, x.RequestHash)).ToArray();
        if (cleanups.Length != request.Registration.Cleanups.Count ||
            !cleanups.SequenceEqual(request.Registration.Cleanups))
            return (null, null, Quarantine("work-authority-stale"));
        return (new(request.OperationId, GraphMediaWorkAuthority.FromRecord(work), cleanups, null, request.EffectObservedAt), ledger, null);
    }

    private async ValueTask<GraphMediaWorkExecutionResult> ResolveCommandOnlyAsync(
        AuthorityFactEnvelopeV1 command, GraphMediaWorkExecutionCommandBody body,
        GraphMediaWorkLedger projection, GraphMediaWorkExecutionRequest request,
        CancellationToken cancellationToken)
    {
        GraphMediaWorkEffectQueryResult query;
        try { query = await _effects.QueryAsync(Effect(body), cancellationToken).ConfigureAwait(false); }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch { return new GraphMediaWorkExecutionResult.StoreUnavailable(new("work-query-unavailable")); }
        if (!Valid(query)) return Quarantine("work-effect-invalid");
        if (query is GraphMediaWorkEffectQueryResult.NotObserved)
        {
            GraphMediaWorkEffectResult effect;
            var invoked = false;
            try { cancellationToken.ThrowIfCancellationRequested(); invoked = true; effect = await _effects.ExecuteAsync(Effect(body), cancellationToken).ConfigureAwait(false); }
            catch (OperationCanceledException) when (invoked) { return await ResolveAmbiguousEffectAsync(command, body, projection, request).ConfigureAwait(false); }
            catch (Exception) when (invoked) { return await ResolveAmbiguousEffectAsync(command, body, projection, request).ConfigureAwait(false); }
            if (!Valid(effect)) return Quarantine("work-effect-invalid");
            return await RecordEffectAsync(command, body, effect, projection, request, CancellationToken.None).ConfigureAwait(false);
        }
        return await RecordEffectAsync(command, body, FromQuery(query), projection, request, CancellationToken.None).ConfigureAwait(false);
    }

    private async ValueTask<GraphMediaWorkExecutionResult> ResolveAmbiguousEffectAsync(
        AuthorityFactEnvelopeV1 command, GraphMediaWorkExecutionCommandBody body,
        GraphMediaWorkLedger projection, GraphMediaWorkExecutionRequest request)
    {
        GraphMediaWorkEffectQueryResult query;
        try { query = await _effects.QueryAsync(Effect(body), CancellationToken.None).ConfigureAwait(false); }
        catch { query = new GraphMediaWorkEffectQueryResult.Unknown(); }
        if (!Valid(query) || query is GraphMediaWorkEffectQueryResult.NotObserved)
            query = new GraphMediaWorkEffectQueryResult.Unknown();
        return await RecordEffectAsync(command, body, FromQuery(query), projection, request, CancellationToken.None).ConfigureAwait(false);
    }

    private async ValueTask<GraphMediaWorkExecutionResult> RecordEffectAsync(
        AuthorityFactEnvelopeV1 command, GraphMediaWorkExecutionCommandBody body,
        GraphMediaWorkEffectResult effect, GraphMediaWorkLedger projection,
        GraphMediaWorkExecutionRequest request, CancellationToken cancellationToken)
    {
        var outcome = effect switch
        {
            GraphMediaWorkEffectResult.Completed => GraphMediaWorkExecutionOutcome.Completed,
            GraphMediaWorkEffectResult.Rejected => GraphMediaWorkExecutionOutcome.Rejected,
            _ => GraphMediaWorkExecutionOutcome.Unknown
        };
        var evidence = (effect as GraphMediaWorkEffectResult.Completed)?.EvidenceHash;
        var code = (effect as GraphMediaWorkEffectResult.Rejected)?.SafeCode;
        if (outcome == GraphMediaWorkExecutionOutcome.Rejected && code?.ToString() is not
            ("work-authority-stale" or "work-residence-mismatch" or "work-owner-mismatch" or
             "work-predecessor-conflict" or "work-effect-rejected"))
        { outcome = GraphMediaWorkExecutionOutcome.Unknown; code = null; }
        var fact = new GraphMediaWorkExecutionFactBody(command.Position, body.Work.WorkId,
            body.Work.RequestHash, outcome, evidence, code, body.ObservedAt);
        var proposal = Proposal(GraphMediaWorkExecutionPayloadRegistrations.Fact,
            GraphMediaWorkExecutionFactIds.Fact(command.Position),
            EncodeFact(command.Position.Session, request.ExpectedAuthority, fact), request.Correlation, request.ObservedAt);
        var history = await ReadAsync(command.Position.Session, request, CancellationToken.None).ConfigureAwait(false);
        if (history.Error is not null) return history.Error;
        var existing = ToResult(history.Result!, projection); if (existing is not null) return existing;
        var appended = await AppendAsync(command.Position.Session, history.Through, proposal, request, cancellationToken).ConfigureAwait(false);
        if (appended.Error is not null) return appended.Error;
        var final = await ReadAsync(command.Position.Session, request, CancellationToken.None).ConfigureAwait(false);
        return final.Error ?? ToResult(final.Result!, projection) ?? Quarantine("work-history-invalid");
    }

    private async ValueTask<(GraphMediaWorkExecutionFoldResult? Result,
        GraphMediaWorkExecutionFoldResult? Latest, long Through,
        GraphMediaWorkExecutionResult? Error)> ReadAsync(SessionAuthorityStampV1 session,
        GraphMediaWorkExecutionRequest request, CancellationToken cancellationToken)
    {
        var fold = GraphMediaWorkExecutionFold.Create(session, request.Registration.ResidenceId, _registry);
        long cursor = 0, through = long.MaxValue; ulong records = 0, bytes = 0;
        while (cursor < through)
        {
            ReadAuthorityRangeResultV1 read;
            try { read = await _journal.ReadAsync(new(session, cursor, through, 256, 1_048_576), cancellationToken).ConfigureAwait(false); }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
            catch { return (null, null, cursor, new GraphMediaWorkExecutionResult.StoreUnavailable(new("work-journal-unavailable"))); }
            if (read is not ReadAuthorityRangeResultV1.Batch batch || batch.AfterExclusive != cursor ||
                through != long.MaxValue && batch.SnapshotThrough != through)
                return (null, null, cursor, new GraphMediaWorkExecutionResult.StoreUnavailable(new("work-journal-unavailable")));
            through = batch.SnapshotThrough;
            if (batch.Facts.Count == 0) { if (batch.HasMore) return (null, null, cursor, Quarantine("work-history-invalid")); break; }
            foreach (var envelope in batch.Facts)
            {
                records++; bytes += (ulong)AuthorityCanonicalCborV1.GetEnvelopeEncodedLength(envelope);
                if (records > request.MaximumSessionRecords || bytes > request.MaximumSessionCanonicalBytes)
                    return (null, null, cursor, Quarantine("work-history-invalid"));
                if (fold.Apply(envelope) is GraphMediaWorkExecutionFoldApplyResult.InvalidHistory invalid)
                    return (null, null, cursor, Quarantine(invalid.SafeCode.ToString()));
                cursor = envelope.Position.Sequence;
            }
            if (!batch.HasMore) break;
        }
        var latest = fold.Complete();
        return (fold.Query(request.OperationId), latest, cursor, null);
    }

    private async ValueTask<(AuthorityFactEnvelopeV1? Envelope, GraphMediaWorkExecutionResult? Error)> AppendAsync(
        SessionAuthorityStampV1 session, long expectedHead, ProposedAuthorityFactV1 proposal,
        GraphMediaWorkExecutionRequest request, CancellationToken cancellationToken)
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
                return (null, new GraphMediaWorkExecutionResult.StoreUnavailable(new("work-journal-capacity")));
            var reread = await ReadAsync(session, request, CancellationToken.None).ConfigureAwait(false);
            if (reread.Error is not null) return (null, reread.Error);
            var found = Find(reread.Result!, proposal.FactId); if (found is not null) return (found, null);
            cancellationToken.ThrowIfCancellationRequested();
            expectedHead = reread.Through;
            if (result is AppendAuthorityResultV1.StoreUnavailable)
                return (null, new GraphMediaWorkExecutionResult.StoreUnavailable(new("work-journal-unavailable")));
        }
        return (null, new GraphMediaWorkExecutionResult.RetryRequired(new("work-predecessor-conflict")));
    }

    private static GraphMediaWorkLedger? EnsureRunning(GraphMediaWorkLedger ledger, GraphMediaWorkAuthority work)
    {
        if (!ledger.Work.TryGetValue(work.WorkId, out var row)) return null;
        if (row.State == GraphMediaWorkState.Running) return ledger;
        if (row.State != GraphMediaWorkState.Registered) return null;
        var transition = ledger.StartWork(work.WorkId, work.RequestHash);
        return transition.Result == GraphMediaWorkResult.Running ? transition.Ledger : null;
    }

    private static GraphMediaWorkExecutionResult Project(GraphMediaWorkExecutionFoldResult.Rejected x,
        GraphMediaWorkLedger ledger)
    {
        if (!ledger.Work.TryGetValue(x.CommandBody.Work.WorkId, out var row)) return Quarantine("work-authority-stale");
        if (row.State == GraphMediaWorkState.Running)
            ledger = ledger.RejectWork(row.WorkId, row.RequestHash).Ledger;
        else if (row.State != GraphMediaWorkState.Registered)
            return Quarantine("work-authority-stale");
        return new GraphMediaWorkExecutionResult.Rejected(x.Command, x.Fact, x.SafeCode, ledger);
    }

    private static GraphMediaWorkExecutionResult? ToResult(GraphMediaWorkExecutionFoldResult result,
        GraphMediaWorkLedger ledger) => result switch
        {
        GraphMediaWorkExecutionFoldResult.Completed x => CompletedResult(x, ledger),
        GraphMediaWorkExecutionFoldResult.Unknown x => UnknownResult(x, ledger),
            GraphMediaWorkExecutionFoldResult.Rejected x => Project(x, ledger),
            GraphMediaWorkExecutionFoldResult.InvalidHistory x => Quarantine(x.SafeCode.ToString()),
            _ => null
        };

    private static AuthorityFactEnvelopeV1? TerminalFact(GraphMediaWorkExecutionFoldResult result) => result switch
    {
        GraphMediaWorkExecutionFoldResult.Completed x => x.Fact,
        GraphMediaWorkExecutionFoldResult.Unknown x => x.Fact,
        GraphMediaWorkExecutionFoldResult.Rejected x => x.Fact,
        _ => null
    };

    private static GraphMediaWorkExecutionResult CompletedResult(
        GraphMediaWorkExecutionFoldResult.Completed result, GraphMediaWorkLedger ledger)
    {
        var projected = ProjectCompleted(ledger, result.CommandBody.Work, result.EvidenceHash);
        return projected is null ? Quarantine("work-authority-stale") :
            new GraphMediaWorkExecutionResult.Completed(result.Command, result.Fact, result.EvidenceHash, projected);
    }

    private static GraphMediaWorkLedger? ProjectCompleted(GraphMediaWorkLedger ledger,
        GraphMediaWorkAuthority work, Hash256 evidence)
    {
        if (!ledger.Work.TryGetValue(work.WorkId, out var row) || row.RequestHash != work.RequestHash) return null;
        if (row.State == GraphMediaWorkState.Terminal)
            return row.OutcomeHash == evidence ? ledger : null;
        if (row.State == GraphMediaWorkState.Unknown)
        {
            var reconciled = ledger.ReconcileWork(work.WorkId, work.RequestHash, true, evidence);
            return reconciled.Result == GraphMediaWorkResult.ReconciledTerminal ? reconciled.Ledger : null;
        }
        var running = EnsureRunning(ledger, work); if (running is null) return null;
        var finished = running.FinishWork(work.WorkId, work.RequestHash, evidence);
        return finished.Result == GraphMediaWorkResult.Terminal ? finished.Ledger : null;
    }

    private static GraphMediaWorkExecutionResult UnknownResult(
        GraphMediaWorkExecutionFoldResult.Unknown result, GraphMediaWorkLedger ledger)
    {
        var projected = ProjectUnknown(ledger, result.CommandBody.Work);
        return projected is null ? Quarantine("work-authority-stale") :
            new GraphMediaWorkExecutionResult.Unknown(result.Command, result.Fact, projected);
    }

    private static GraphMediaWorkLedger? ProjectUnknown(GraphMediaWorkLedger ledger,
        GraphMediaWorkAuthority work)
    {
        if (!ledger.Work.TryGetValue(work.WorkId, out var row) || row.RequestHash != work.RequestHash) return null;
        if (row.State == GraphMediaWorkState.Unknown) return ledger;
        if (row.State == GraphMediaWorkState.Terminal) return null;
        var running = EnsureRunning(ledger, work); if (running is null) return null;
        var unknown = running.LoseWorkOutcome(work.WorkId, work.RequestHash);
        return unknown.Result == GraphMediaWorkResult.OutcomeUnknown ? unknown.Ledger : null;
    }

    private static AuthorityFactEnvelopeV1? Find(GraphMediaWorkExecutionFoldResult result, JournalFactId id) => result switch
    {
        GraphMediaWorkExecutionFoldResult.CommandOnly x when x.Command.FactId == id => x.Command,
        GraphMediaWorkExecutionFoldResult.Completed x => x.Command.FactId == id ? x.Command : x.Fact.FactId == id ? x.Fact : null,
        GraphMediaWorkExecutionFoldResult.Unknown x => x.Command.FactId == id ? x.Command : x.Fact.FactId == id ? x.Fact : null,
        GraphMediaWorkExecutionFoldResult.Rejected x => x.Command.FactId == id ? x.Command : x.Fact.FactId == id ? x.Fact : null,
        _ => null
    };

    private static GraphMediaWorkEffectRequest Effect(GraphMediaWorkExecutionCommandBody body) =>
        new(body.OperationId, body.Work, body.Cleanups);
    private static GraphMediaWorkEffectResult FromQuery(GraphMediaWorkEffectQueryResult query) => query switch
    {
        GraphMediaWorkEffectQueryResult.Completed x => new GraphMediaWorkEffectResult.Completed(x.EvidenceHash),
        GraphMediaWorkEffectQueryResult.Rejected x => new GraphMediaWorkEffectResult.Rejected(x.SafeCode),
        _ => new GraphMediaWorkEffectResult.Unknown()
    };
    private static bool Valid(GraphMediaWorkEffectResult? result) => result is not null &&
        (result is not GraphMediaWorkEffectResult.Completed completed || completed.EvidenceHash != default) &&
        (result is not GraphMediaWorkEffectResult.Rejected rejected || rejected.SafeCode.ToString() is
            "work-authority-stale" or "work-residence-mismatch" or "work-owner-mismatch" or "work-predecessor-conflict" or "work-effect-rejected");
    private static bool Valid(GraphMediaWorkEffectQueryResult? result) => result is not null &&
        (result is not GraphMediaWorkEffectQueryResult.Completed completed || completed.EvidenceHash != default) &&
        (result is not GraphMediaWorkEffectQueryResult.Rejected rejected || rejected.SafeCode.ToString() is
            "work-authority-stale" or "work-residence-mismatch" or "work-owner-mismatch" or "work-predecessor-conflict" or "work-effect-rejected");
    private static GraphMediaWorkExecutionCommandBody WithPredecessor(GraphMediaWorkExecutionCommandBody body,
        JournalPositionV1 predecessor) => new(body.OperationId, body.Work, body.Cleanups, predecessor, body.ObservedAt);
    private static bool SameCommand(GraphMediaWorkExecutionCommandBody left, GraphMediaWorkExecutionCommandBody right) =>
        GraphMediaWorkExecutionCodecs.EncodeCommandBody(left).SequenceEqual(GraphMediaWorkExecutionCodecs.EncodeCommandBody(right));
    private static byte[] EncodeCommand(SessionAuthorityStampV1 session, ExpectedAuthorityVectorV1 authority,
        GraphMediaWorkExecutionCommandBody body) => GraphMediaWorkExecutionCodecs.EncodeOuter(
            new(session, authority, GraphMediaWorkExecutionCodecs.EncodeCommandBody(body)));
    private static byte[] EncodeFact(SessionAuthorityStampV1 session, ExpectedAuthorityVectorV1 authority,
        GraphMediaWorkExecutionFactBody body) => GraphMediaWorkExecutionCodecs.EncodeOuter(
            new(session, authority, GraphMediaWorkExecutionCodecs.EncodeFactBody(body)));
    private static ProposedAuthorityFactV1 Proposal(AuthorityPayloadRegistrationV1 registration, JournalFactId factId,
        byte[] payload, CorrelationEnvelopeV1 correlation, UtcInstant observedAt) => new(factId, null, OwnerSliceId.S1,
            registration.Schema, payload, AuthorityPayloadHashV1.Compute(registration.SchemaToken, registration.Schema, payload), correlation, observedAt);
    private static bool EnvelopeMatches(AuthorityFactEnvelopeV1 envelope, ProposedAuthorityFactV1 proposal) =>
        envelope.FactId == proposal.FactId && envelope.ThreadScope is null && envelope.Owner == proposal.Owner &&
        envelope.PayloadSchema == proposal.PayloadSchema && envelope.PayloadHash == proposal.PayloadHash &&
        envelope.Correlation == proposal.Correlation && envelope.ObservedAt == proposal.ObservedAt &&
        envelope.PayloadBytes.SequenceEqual(proposal.PayloadBytes);
    private static GraphMediaWorkExecutionResult.Quarantined Quarantine(string code) => new(new(code));
}
