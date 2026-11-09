using HPD.Agent.Authority;

namespace HPD.Agent.Audio.Graph;

internal sealed record GraphTopologyInstallationRequest
{
    internal GraphTopologyInstallationRequest(SessionAuthorityStampV1 session, GraphTopologyPlan topology,
        JournalPositionV1 activeSourceGrantFact, ExpectedAuthorityVectorV1 currentAuthority,
        CorrelationEnvelopeV1 correlation, UtcInstant observedAt)
    {
        if (!session.IsValid || topology is null || topology.Session != session || !activeSourceGrantFact.IsValid ||
            activeSourceGrantFact.Session != session || currentAuthority is null || currentAuthority.Session != session ||
            !GraphReplacementReducer.HasExactGraph(currentAuthority, topology.GraphGeneration) || !correlation.IsValid)
            throw new ArgumentException("Topology installation requires one exact graph-scoped request.");
        Session = session; Topology = topology; ActiveSourceGrantFact = activeSourceGrantFact;
        CurrentAuthority = currentAuthority; Correlation = correlation; ObservedAt = observedAt;
    }

    internal SessionAuthorityStampV1 Session { get; }
    internal GraphTopologyPlan Topology { get; }
    internal JournalPositionV1 ActiveSourceGrantFact { get; }
    internal ExpectedAuthorityVectorV1 CurrentAuthority { get; }
    internal CorrelationEnvelopeV1 Correlation { get; }
    internal UtcInstant ObservedAt { get; }
}

internal abstract record GraphTopologyInstallationAdmissionResult
{
    private GraphTopologyInstallationAdmissionResult() { }
    internal sealed record Installed : GraphTopologyInstallationAdmissionResult
    { internal Installed(AuthorityFactEnvelopeV1 envelope) { ArgumentNullException.ThrowIfNull(envelope); Envelope = envelope; } internal AuthorityFactEnvelopeV1 Envelope { get; } }
    internal sealed record AlreadyInstalled : GraphTopologyInstallationAdmissionResult
    { internal AlreadyInstalled(AuthorityFactEnvelopeV1 envelope) { ArgumentNullException.ThrowIfNull(envelope); Envelope = envelope; } internal AuthorityFactEnvelopeV1 Envelope { get; } }
    internal sealed record Conflict : GraphTopologyInstallationAdmissionResult
    { internal Conflict(AuthorityFactEnvelopeV1 envelope) { ArgumentNullException.ThrowIfNull(envelope); Envelope = envelope; } internal AuthorityFactEnvelopeV1 Envelope { get; } }
    internal sealed record RuntimeReplaced : GraphTopologyInstallationAdmissionResult
    { internal RuntimeReplaced(RuntimeGenerationId replacement) { if (!replacement.IsValid) throw new ArgumentException("A replacement is required.", nameof(replacement)); Replacement = replacement; } internal RuntimeGenerationId Replacement { get; } }
    internal sealed record Rejected : GraphTopologyInstallationAdmissionResult
    { internal Rejected(BoundedAscii safeCode) { if (!safeCode.IsValid) throw new ArgumentException("A safe code is required.", nameof(safeCode)); SafeCode = safeCode; } internal BoundedAscii SafeCode { get; } }
    internal sealed record InvalidHistory : GraphTopologyInstallationAdmissionResult
    { internal InvalidHistory(BoundedAscii safeCode, long lastVerifiedPosition) { if (!safeCode.IsValid) throw new ArgumentException("A safe code is required.", nameof(safeCode)); if (lastVerifiedPosition < 0) throw new ArgumentOutOfRangeException(nameof(lastVerifiedPosition)); SafeCode = safeCode; LastVerifiedPosition = lastVerifiedPosition; } internal BoundedAscii SafeCode { get; } internal long LastVerifiedPosition { get; } }
    internal sealed record RetryRequired : GraphTopologyInstallationAdmissionResult
    { internal RetryRequired(long observedHead) { if (observedHead < 0) throw new ArgumentOutOfRangeException(nameof(observedHead)); ObservedHead = observedHead; } internal long ObservedHead { get; } }
    internal sealed record OutcomeUnknown : GraphTopologyInstallationAdmissionResult
    { internal OutcomeUnknown(JournalFactId factId, BoundedAscii safeCode, long lastVerifiedPosition) { if (!factId.IsValid || !safeCode.IsValid) throw new ArgumentException("A fact identity and safe code are required."); if (lastVerifiedPosition < 0) throw new ArgumentOutOfRangeException(nameof(lastVerifiedPosition)); FactId = factId; SafeCode = safeCode; LastVerifiedPosition = lastVerifiedPosition; } internal JournalFactId FactId { get; } internal BoundedAscii SafeCode { get; } internal long LastVerifiedPosition { get; } }
}

internal static class GraphTopologyInstallationAdmission
{
    private const int MaximumAttempts = 8;
    private static readonly AuthorityPayloadRegistrationV1 Registration = GraphReplacementPayloadRegistrations.Installed;

    internal static async ValueTask<GraphTopologyInstallationAdmissionResult> InstallAsync(
        IAuthorityJournalV1 journal, GraphTopologyInstallationRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(journal); ArgumentNullException.ThrowIfNull(request);
        var body = new GraphTopologyInstalled(request.Topology, request.Topology.Fingerprint,
            request.ActiveSourceGrantFact, request.CurrentAuthority);
        var payload = GraphReplacementCodecs.EncodeOuter(new GraphTopologyInstalledFact(request.Session,
            request.CurrentAuthority, GraphReplacementCodecs.EncodeInstalled(body)));
        var factId = GraphReplacementFactIds.Installed(request.Session, request.Topology.Fingerprint);
        var hash = AuthorityPayloadHashV1.Compute(Registration.SchemaToken, Registration.Schema, payload);
        var proposal = new ProposedAuthorityFactV1(factId, null, OwnerSliceId.S2, Registration.Schema,
            payload, hash, request.Correlation, request.ObservedAt);
        var appendObserved = false;
        var lastHead = 0L;

        for (var attempt = 0; attempt < MaximumAttempts; attempt++)
        {
            var read = await GraphReplacementSnapshotReader.ReadAsync(journal, request.Session,
                cancellationToken: cancellationToken).ConfigureAwait(false);
            if (read is GraphReplacementSnapshotReadResult.OutcomeUnknown unknown)
                return Unknown(factId, unknown.SafeCode.ToString(), unknown.LastVerifiedPosition);
            if (read is not GraphReplacementSnapshotReadResult.Verified verified)
                return Unknown(factId, "graph-install-snapshot-unknown", lastHead);
            if (verified.Fold is GraphReplacementJournalFoldResult.RuntimeReplaced replaced)
                return new GraphTopologyInstallationAdmissionResult.RuntimeReplaced(replaced.Replacement);
            if (verified.Fold is GraphReplacementJournalFoldResult.InvalidHistory invalidHistory)
                return new GraphTopologyInstallationAdmissionResult.InvalidHistory(
                    invalidHistory.SafeCode, invalidHistory.LastVerifiedPosition);
            if (verified.Fold is not GraphReplacementJournalFoldResult.Current current)
                return Unknown(factId, "graph-install-snapshot-unknown", lastHead);
            lastHead = verified.SnapshotThrough;

            if (current.State is { } existing)
            {
                if (current.InstallationFact is not { } installation)
                    return new GraphTopologyInstallationAdmissionResult.InvalidHistory(
                        new BoundedAscii("graph-install-envelope-missing"), current.SnapshotThrough);
                if (!Matches(installation, proposal, request.Session))
                    return new GraphTopologyInstallationAdmissionResult.Conflict(installation);
                return appendObserved
                    ? new GraphTopologyInstallationAdmissionResult.Installed(installation)
                    : new GraphTopologyInstallationAdmissionResult.AlreadyInstalled(installation);
            }
            if (current.PendingCommands.Count != 0)
                return Unknown(factId, "graph-traffic-before-install", current.SnapshotThrough);
            if (!Matches(request.CurrentAuthority, current.Authority) ||
                !HasGraph(current.Authority, request.Topology.GraphGeneration))
                return new GraphTopologyInstallationAdmissionResult.Rejected(new BoundedAscii("authority-vector-stale"));

            var capacity = await CapacityGrantSnapshotReaderV1.ReadAtAsync(journal, request.Session,
                request.Topology.CapacityGrantId, request.ActiveSourceGrantFact, cancellationToken).ConfigureAwait(false);
            if (capacity is not CapacityGrantSnapshotAtResultV1.Exact exactGrant ||
                !GraphReplacementReducer.GrantMatches(exactGrant.Grant, request.Topology, request.CurrentAuthority))
                return Unknown(factId, "graph-source-capacity-proof-unknown", current.SnapshotThrough);

            AppendAuthorityResultV1 appended;
            try
            {
                appended = await journal.AppendAsync(new AppendAuthorityBatchV1(request.Session,
                    current.SnapshotThrough, [], [proposal], ProposedAuthorityFactV1.MaximumPayloadBytes),
                    cancellationToken).ConfigureAwait(false);
                cancellationToken.ThrowIfCancellationRequested();
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
            catch (Exception) { appendObserved = true; continue; }

            switch (appended)
            {
                case AppendAuthorityResultV1.Committed committed when committed.PreviousHead == current.SnapshotThrough &&
                    committed.Envelopes.Count == 1 && Matches(committed.Envelopes[0], proposal, request.Session):
                    appendObserved = true; continue;
                case AppendAuthorityResultV1.AlreadyCommitted already when already.Envelopes.Count == 1 &&
                    Matches(already.Envelopes[0], proposal, request.Session):
                    continue;
                case AppendAuthorityResultV1.SessionConflict sessionConflict:
                    lastHead = sessionConflict.Actual; continue;
                case AppendAuthorityResultV1.ContradictoryDuplicate duplicate when duplicate.FactId == factId:
                    return new GraphTopologyInstallationAdmissionResult.Rejected(
                        new BoundedAscii("graph-install-contradictory-duplicate"));
                case AppendAuthorityResultV1.InvalidPayload invalidPayload:
                    return new GraphTopologyInstallationAdmissionResult.Rejected(invalidPayload.SafeCode);
                case AppendAuthorityResultV1.StoreUnavailable unavailable:
                    appendObserved = true; continue;
                default:
                    appendObserved = true; continue;
            }
        }
        var finalRead = await GraphReplacementSnapshotReader.ReadAsync(journal, request.Session,
            cancellationToken: cancellationToken).ConfigureAwait(false);
        if (finalRead is GraphReplacementSnapshotReadResult.Verified
            { Fold: GraphReplacementJournalFoldResult.Current { State: not null, InstallationFact: { } installed } final } &&
            Matches(installed, proposal, request.Session))
            return appendObserved
                ? new GraphTopologyInstallationAdmissionResult.Installed(installed)
                : new GraphTopologyInstallationAdmissionResult.AlreadyInstalled(installed);
        if (finalRead is GraphReplacementSnapshotReadResult.Verified
            { Fold: GraphReplacementJournalFoldResult.Current { State: not null, InstallationFact: { } finalConflict } })
            return new GraphTopologyInstallationAdmissionResult.Conflict(finalConflict);
        if (finalRead is GraphReplacementSnapshotReadResult.Verified
            { Fold: GraphReplacementJournalFoldResult.RuntimeReplaced finalReplaced })
            return new GraphTopologyInstallationAdmissionResult.RuntimeReplaced(finalReplaced.Replacement);
        if (finalRead is GraphReplacementSnapshotReadResult.Verified
            { Fold: GraphReplacementJournalFoldResult.InvalidHistory finalInvalid })
            return new GraphTopologyInstallationAdmissionResult.InvalidHistory(
                finalInvalid.SafeCode, finalInvalid.LastVerifiedPosition);
        if (finalRead is GraphReplacementSnapshotReadResult.OutcomeUnknown finalUnknown)
            return Unknown(factId, finalUnknown.SafeCode.ToString(), finalUnknown.LastVerifiedPosition);
        return appendObserved ? Unknown(factId, "graph-install-reconcile-exhausted", lastHead)
            : new GraphTopologyInstallationAdmissionResult.RetryRequired(lastHead);
    }

    private static bool Matches(ExpectedAuthorityVectorV1 expected, CurrentAuthorityVectorSnapshotV1 current) =>
        expected.Session == current.Session && expected.Axes.All(claimed => current.Axes.Any(actual => actual == claimed));

    private static bool HasGraph(CurrentAuthorityVectorSnapshotV1 current, GraphGenerationId generation) =>
        current.Axes.Count(axis => axis.AxisId == AuthorityAxisId.Graph &&
            axis.Value is AuthorityAxisValueV1.Graph graph && graph.Value == generation) == 1;

    private static bool Matches(AuthorityFactEnvelopeV1 envelope, ProposedAuthorityFactV1 proposal,
        SessionAuthorityStampV1 session) => envelope.Position.Session == session && envelope.FactId == proposal.FactId &&
        envelope.ThreadScope is null && envelope.Owner == proposal.Owner && envelope.PayloadSchema == proposal.PayloadSchema &&
        envelope.PayloadHash == proposal.PayloadHash && envelope.Payload.SequenceEqual(proposal.Payload);

    private static GraphTopologyInstallationAdmissionResult.OutcomeUnknown Unknown(
        JournalFactId factId, string code, long position) => new(factId, new BoundedAscii(code), position);
}
