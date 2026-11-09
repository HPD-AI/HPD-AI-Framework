using HPD.Agent.Authority;

namespace HPD.Agent.Audio.Graph;

internal sealed record GraphReplacementAdmissionRequest
{
    internal GraphReplacementAdmissionRequest(GraphReplacementJournalCommand command,
        ExpectedAuthorityVectorV1 expectedAuthority, CorrelationEnvelopeV1 correlation, UtcInstant observedAt)
    {
        ArgumentNullException.ThrowIfNull(command); ArgumentNullException.ThrowIfNull(expectedAuthority);
        if (!command.OperationId.IsValid || !command.ExpectedPredecessor.IsValid ||
            expectedAuthority.Session != command.ExpectedPredecessor.Session ||
            !GraphReplacementReducer.HasExactGraph(expectedAuthority,
                expectedAuthority.Axes.OfType<AxisEntryV1>().Where(x => x.AxisId == AuthorityAxisId.Graph)
                    .Select(x => ((AuthorityAxisValueV1.Graph)x.Value).Value).SingleOrDefault()) || !correlation.IsValid)
            throw new ArgumentException("Replacement admission requires one exact graph-scoped request.");
        if (command is GraphReplacementJournalCommand.Prepare prepare && prepare.CurrentAuthority != expectedAuthority)
            throw new ArgumentException("Prepare authority must equal the outer expected authority.");
        Command = command; ExpectedAuthority = expectedAuthority; Correlation = correlation; ObservedAt = observedAt;
    }
    internal GraphReplacementJournalCommand Command { get; }
    internal ExpectedAuthorityVectorV1 ExpectedAuthority { get; }
    internal CorrelationEnvelopeV1 Correlation { get; }
    internal UtcInstant ObservedAt { get; }
}

internal abstract record GraphReplacementAdmissionResult
{
    private GraphReplacementAdmissionResult() { }
    internal sealed record Admitted : GraphReplacementAdmissionResult
    { internal Admitted(AuthorityFactEnvelopeV1 command, AuthorityFactEnvelopeV1 result, AuthorityFactEnvelopeV1? transition, GraphReplacementJournalOutcome outcome) { Validate(command,result,transition,outcome); Command=command;Result=result;GraphTransition=transition;Outcome=outcome; } internal AuthorityFactEnvelopeV1 Command{get;} internal AuthorityFactEnvelopeV1 Result{get;} internal AuthorityFactEnvelopeV1? GraphTransition{get;} internal GraphReplacementJournalOutcome Outcome{get;} }
    internal sealed record AlreadyAdmitted : GraphReplacementAdmissionResult
    { internal AlreadyAdmitted(AuthorityFactEnvelopeV1 command, AuthorityFactEnvelopeV1 result, AuthorityFactEnvelopeV1? transition, GraphReplacementJournalOutcome outcome) { Validate(command,result,transition,outcome); Command=command;Result=result;GraphTransition=transition;Outcome=outcome; } internal AuthorityFactEnvelopeV1 Command{get;} internal AuthorityFactEnvelopeV1 Result{get;} internal AuthorityFactEnvelopeV1? GraphTransition{get;} internal GraphReplacementJournalOutcome Outcome{get;} }
    internal sealed record Rejected : GraphReplacementAdmissionResult { internal Rejected(BoundedAscii code){if(!code.IsValid)throw new ArgumentException();SafeCode=code;} internal BoundedAscii SafeCode{get;} }
    internal sealed record ContradictoryDuplicate : GraphReplacementAdmissionResult { internal ContradictoryDuplicate(JournalFactId id){if(!id.IsValid)throw new ArgumentException();FactId=id;} internal JournalFactId FactId{get;} }
    internal sealed record RuntimeReplaced : GraphReplacementAdmissionResult { internal RuntimeReplaced(RuntimeGenerationId value){if(!value.IsValid)throw new ArgumentException();Replacement=value;} internal RuntimeGenerationId Replacement{get;} }
    internal sealed record InvalidHistory : GraphReplacementAdmissionResult { internal InvalidHistory(BoundedAscii code,long position){if(!code.IsValid||position<0)throw new ArgumentException();SafeCode=code;LastVerifiedPosition=position;} internal BoundedAscii SafeCode{get;} internal long LastVerifiedPosition{get;} }
    internal sealed record RetryRequired : GraphReplacementAdmissionResult { internal RetryRequired(long head){if(head<0)throw new ArgumentOutOfRangeException();ObservedHead=head;} internal long ObservedHead{get;} }
    internal sealed record OutcomeUnknown : GraphReplacementAdmissionResult { internal OutcomeUnknown(JournalFactId id,BoundedAscii code){if(!id.IsValid||!code.IsValid)throw new ArgumentException();FactId=id;SafeCode=code;} internal JournalFactId FactId{get;} internal BoundedAscii SafeCode{get;} }

    private static void Validate(AuthorityFactEnvelopeV1 command, AuthorityFactEnvelopeV1 result,
        AuthorityFactEnvelopeV1? transition, GraphReplacementJournalOutcome outcome)
    {
        ArgumentNullException.ThrowIfNull(command); ArgumentNullException.ThrowIfNull(result);
        var commandRegistration = GraphReplacementPayloadRegistrations.Command;
        var factRegistration = GraphReplacementPayloadRegistrations.Fact;
        if (!Enum.IsDefined(outcome) || command.Position.Session != result.Position.Session ||
            command.Position.Sequence >= result.Position.Sequence || command.ThreadScope is not null ||
            result.ThreadScope is not null ||
            command.Owner != OwnerSliceId.S2 || result.Owner != OwnerSliceId.S2 ||
            command.PayloadSchema != commandRegistration.Schema || result.PayloadSchema != factRegistration.Schema ||
            command.PayloadHash != AuthorityPayloadHashV1.Compute(commandRegistration.SchemaToken,
                commandRegistration.Schema, command.PayloadBytes) ||
            result.PayloadHash != AuthorityPayloadHashV1.Compute(factRegistration.SchemaToken,
                factRegistration.Schema, result.PayloadBytes) ||
            !GraphReplacementCodecs.TryDecodeOuter(command.PayloadMemory, out var commandOuter) ||
            !GraphReplacementCodecs.TryDecodeCommand(commandOuter!.Body, out var commandBody) ||
            commandOuter.Session != command.Position.Session ||
            command.FactId != GraphReplacementFactIds.Command(command.Position.Session,
                commandBody!.OperationId, (ushort)commandBody.Kind) ||
            result.FactId != GraphReplacementFactIds.Result(command.Position) ||
            !GraphReplacementCodecs.TryDecodeOuter(result.PayloadMemory, out var outer) ||
            !GraphReplacementCodecs.TryDecodeFact(outer!.Body, out var body) ||
            outer.Session != result.Position.Session || outer.ExpectedAuthority != commandOuter.ExpectedAuthority ||
            body!.CommandFact != command.Position || body.ExpectedPredecessor != commandBody.ExpectedPredecessor ||
            body.Outcome != outcome || (outcome == GraphReplacementJournalOutcome.Committed) != (transition is not null) ||
            transition is not null && (transition.Position.Session != result.Position.Session ||
                transition.Position.Sequence != result.Position.Sequence + 1 ||
                transition.ThreadScope is not null || transition.Owner != OwnerSliceId.S2 ||
                transition.PayloadSchema != AuthorityGenerationTransitionCodecV1.SchemaFor(AuthorityAxisId.Graph) ||
                transition.FactId != GraphReplacementFactIds.Transition(command.Position) ||
                transition.PayloadHash != AuthorityPayloadHashV1.Compute(
                    AuthorityGenerationTransitionCodecV1.SchemaTokenFor(AuthorityAxisId.Graph),
                    AuthorityGenerationTransitionCodecV1.SchemaFor(AuthorityAxisId.Graph), transition.PayloadBytes) ||
                AuthorityGenerationTransitionCodecV1.Decode(transition.PayloadSchema, transition.Owner,
                    transition.Position.Session, transition.PayloadMemory, out var decoded) != AuthorityGenerationTransitionDecodeV1.Valid ||
                decoded.Axis != AuthorityAxisId.Graph ||
                GraphGenerationId.FromValue(decoded.ExpectedPrevious) != body.ResultingSnapshot.SourceTopology.GraphGeneration ||
                body.ResultingSnapshot.Target is not { } target ||
                GraphGenerationId.FromValue(decoded.ProposedNext) != target.Topology.GraphGeneration))
            throw new ArgumentException("Replacement admission success requires one exact durable result tuple.");
    }
}

internal static class GraphReplacementAdmissionCoordinator
{
    private const int MaximumAttempts = 8;
    private const uint MaximumAppendBytes = ProposedAuthorityFactV1.MaximumPayloadBytes;
    private static readonly AuthorityPayloadRegistrationV1 CommandRegistration = GraphReplacementPayloadRegistrations.Command;
    private static readonly AuthorityPayloadRegistrationV1 FactRegistration = GraphReplacementPayloadRegistrations.Fact;

    internal static async ValueTask<GraphReplacementAdmissionResult> AdmitAsync(IAuthorityJournalV1 journal,
        GraphReplacementAdmissionRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(journal); ArgumentNullException.ThrowIfNull(request);
        var session = request.ExpectedAuthority.Session;
        var commandId = GraphReplacementFactIds.Command(session, request.Command.OperationId, (ushort)request.Command.Kind);
        var commandPayload = GraphReplacementCodecs.EncodeOuter(new GraphMutationCommand(session,
            request.ExpectedAuthority, GraphReplacementCodecs.EncodeCommand(request.Command)));
        var correlation = new CorrelationEnvelopeV1(request.Correlation.TenantId, request.Correlation.PrincipalId,
            request.Correlation.SessionId, request.Correlation.ThreadId, request.Correlation.ParticipantId,
            request.Command.OperationId);
        var commandProposal = Proposal(commandId, CommandRegistration, commandPayload, correlation, request.ObservedAt);
        var committedHere = false;
        var lastHead = 0L;

        for (var attempt = 0; attempt < MaximumAttempts; attempt++)
        {
            var read = await GraphReplacementSnapshotReader.ReadAsync(journal, session, commandId,
                cancellationToken: cancellationToken).ConfigureAwait(false);
            if (read is GraphReplacementSnapshotReadResult.OutcomeUnknown unknown)
                return Unknown(commandId, unknown.SafeCode);
            var verified = (GraphReplacementSnapshotReadResult.Verified)read;
            lastHead = verified.SnapshotThrough;
            if (verified.Fold is GraphReplacementJournalFoldResult.RuntimeReplaced replaced)
                return new GraphReplacementAdmissionResult.RuntimeReplaced(replaced.Replacement);
            if (verified.Fold is not GraphReplacementJournalFoldResult.Current current)
                return Unknown(commandId, new BoundedAscii("graph-fold-not-current"));

            if (current.TargetResultFact is { } existingResult)
            {
                if (current.TargetCommandFact is not { } existingCommand || !Matches(existingCommand, commandProposal, session) ||
                    !GraphReplacementCodecs.TryDecodeOuter(existingResult.PayloadMemory, out var resultOuter) ||
                    !GraphReplacementCodecs.TryDecodeFact(resultOuter!.Body, out var resultBody))
                    return new GraphReplacementAdmissionResult.ContradictoryDuplicate(commandId);
                var transition = resultBody!.Outcome == GraphReplacementJournalOutcome.Committed
                    ? current.TargetTransitionFact : null;
                if (resultBody.Outcome == GraphReplacementJournalOutcome.Committed && transition is null)
                    return Unknown(existingResult.FactId, new BoundedAscii("graph-transition-reconcile-unknown"));
                return committedHere
                    ? new GraphReplacementAdmissionResult.Admitted(existingCommand, existingResult, transition, resultBody.Outcome)
                    : new GraphReplacementAdmissionResult.AlreadyAdmitted(existingCommand, existingResult, transition, resultBody.Outcome);
            }

            var pending = current.PendingCommands.SingleOrDefault(x => x.Envelope.FactId == commandId);
            if (pending is null)
            {
                if (current.State is null || current.Wire is null)
                    return new GraphReplacementAdmissionResult.Rejected(new BoundedAscii("graph-installation-missing"));
                if (!SessionLifecycleJournalFoldV1.Matches(request.ExpectedAuthority, current.Authority))
                    return new GraphReplacementAdmissionResult.Rejected(new BoundedAscii("authority-vector-stale"));
                var proofFailure = await VerifyReferencedProofAsync(journal, current.State, request.Command,
                    request.ExpectedAuthority, current.SnapshotThrough,
                    cancellationToken).ConfigureAwait(false);
                if (proofFailure is not null) return Unknown(commandId, proofFailure.Value);
                if (current.SnapshotThrough == long.MaxValue)
                    return new GraphReplacementAdmissionResult.InvalidHistory(
                        new BoundedAscii("graph-journal-position-exhausted"), current.SnapshotThrough);
                var append = await AppendAsync(journal, new AppendAuthorityBatchV1(session, current.SnapshotThrough,
                    [], [commandProposal], MaximumAppendBytes), cancellationToken).ConfigureAwait(false);
                if (append is AppendAuthorityResultV1.Committed committed && committed.Envelopes.Count == 1 &&
                    Matches(committed.Envelopes[0], commandProposal, session)) { committedHere = true; continue; }
                if (append is AppendAuthorityResultV1.AlreadyCommitted already && already.Envelopes.Count == 1 &&
                    Matches(already.Envelopes[0], commandProposal, session)) continue;
                if (append is AppendAuthorityResultV1.OutcomeUnknown) { committedHere = true; continue; }
                if (append is AppendAuthorityResultV1.SessionConflict) continue;
                return MapAppend(append, commandId);
            }
            if (!Matches(pending.Envelope, commandProposal, session))
                return new GraphReplacementAdmissionResult.ContradictoryDuplicate(commandId);

            if (current.SnapshotThrough == long.MaxValue)
                return new GraphReplacementAdmissionResult.InvalidHistory(
                    new BoundedAscii("graph-journal-position-exhausted"), current.SnapshotThrough);
            var resultPosition = new JournalPositionV1(session, current.SnapshotThrough + 1);
            var rendered = GraphReplacementJournalFold.RenderFact(current, pending, resultPosition);
            if (rendered is null) return new GraphReplacementAdmissionResult.InvalidHistory(
                new BoundedAscii("graph-result-render-failed"), current.SnapshotThrough);
            var resultPayload = GraphReplacementCodecs.EncodeOuter(new GraphOwnerPayload(session,
                pending.Outer.ExpectedAuthority, GraphReplacementCodecs.EncodeFact(rendered.Body)));
            var resultProposal = Proposal(GraphReplacementFactIds.Result(pending.Envelope.Position), FactRegistration,
                resultPayload, pending.Envelope.Correlation, pending.Envelope.ObservedAt);
            var proposals = new List<ProposedAuthorityFactV1> { resultProposal };
            if (rendered.RequiresGraphTransition)
            {
                var transitionPayload = AuthorityGenerationTransitionCodecV1.Encode(session, AuthorityAxisId.Graph,
                    Stable(rendered.PreviousGraph!.Value), Stable(rendered.NextGraph!.Value));
                var transitionRegistration = new AuthorityGenerationTransitionPayloadRegistrationV1(AuthorityAxisId.Graph);
                proposals.Add(Proposal(GraphReplacementFactIds.Transition(pending.Envelope.Position),
                    transitionRegistration, transitionPayload, pending.Envelope.Correlation, pending.Envelope.ObservedAt));
            }
            var appendResult = await AppendAsync(journal, new AppendAuthorityBatchV1(session,
                current.SnapshotThrough, [], proposals, MaximumAppendBytes), cancellationToken).ConfigureAwait(false);
            if (appendResult is AppendAuthorityResultV1.Committed committedResult)
            {
                if (!BatchMatches(committedResult.Envelopes, proposals, session))
                    return new GraphReplacementAdmissionResult.ContradictoryDuplicate(resultProposal.FactId);
                committedHere = true; continue;
            }
            if (appendResult is AppendAuthorityResultV1.AlreadyCommitted alreadyResult)
            {
                if (!BatchMatches(alreadyResult.Envelopes, proposals, session))
                    return new GraphReplacementAdmissionResult.ContradictoryDuplicate(resultProposal.FactId);
                continue;
            }
            if (appendResult is AppendAuthorityResultV1.OutcomeUnknown) { committedHere = true; continue; }
            if (appendResult is AppendAuthorityResultV1.SessionConflict) continue;
            return MapAppend(appendResult, resultProposal.FactId);
        }
        var final = await GraphReplacementSnapshotReader.ReadAsync(journal, session, commandId,
            cancellationToken: cancellationToken).ConfigureAwait(false);
        if (final is GraphReplacementSnapshotReadResult.OutcomeUnknown finalUnknown)
            return Unknown(commandId, finalUnknown.SafeCode);
        var finalVerified = (GraphReplacementSnapshotReadResult.Verified)final;
        if (finalVerified.Fold is GraphReplacementJournalFoldResult.RuntimeReplaced finalReplaced)
            return new GraphReplacementAdmissionResult.RuntimeReplaced(finalReplaced.Replacement);
        if (finalVerified.Fold is GraphReplacementJournalFoldResult.Current finalCurrent &&
            finalCurrent.TargetResultFact is { } finalResult && finalCurrent.TargetCommandFact is { } finalCommand &&
            Matches(finalCommand, commandProposal, session) &&
            GraphReplacementCodecs.TryDecodeOuter(finalResult.PayloadMemory, out var finalOuter) &&
            GraphReplacementCodecs.TryDecodeFact(finalOuter!.Body, out var finalBody))
        {
            var transition = finalBody!.Outcome == GraphReplacementJournalOutcome.Committed
                ? finalCurrent.TargetTransitionFact : null;
            if (finalBody.Outcome != GraphReplacementJournalOutcome.Committed || transition is not null)
                return committedHere
                    ? new GraphReplacementAdmissionResult.Admitted(finalCommand, finalResult, transition, finalBody.Outcome)
                    : new GraphReplacementAdmissionResult.AlreadyAdmitted(finalCommand, finalResult, transition, finalBody.Outcome);
        }
        if (finalVerified.Fold is GraphReplacementJournalFoldResult.Current { TargetResultFact: not null })
            return new GraphReplacementAdmissionResult.ContradictoryDuplicate(commandId);
        if (finalVerified.Fold is GraphReplacementJournalFoldResult.Current
            { TargetCommandFact: { } unresolvedCommand } && !Matches(unresolvedCommand, commandProposal, session))
            return new GraphReplacementAdmissionResult.ContradictoryDuplicate(commandId);
        return new GraphReplacementAdmissionResult.RetryRequired(finalVerified.SnapshotThrough);
    }

    private static StableId128 Stable(GraphGenerationId value)
    { Span<byte> bytes = stackalloc byte[16]; if (!value.TryWriteBytes(bytes)) throw new ArgumentException(); return StableId128.FromBytes(bytes); }

    private static ProposedAuthorityFactV1 Proposal(JournalFactId id, AuthorityPayloadRegistrationV1 registration,
        byte[] payload, CorrelationEnvelopeV1 correlation, UtcInstant observedAt) => new(id, null, registration.Owner,
            registration.Schema, payload, AuthorityPayloadHashV1.Compute(registration.SchemaToken, registration.Schema, payload),
            correlation, observedAt);

    private static async ValueTask<AppendAuthorityResultV1> AppendAsync(IAuthorityJournalV1 journal,
        AppendAuthorityBatchV1 batch, CancellationToken cancellationToken)
    {
        try { var result = await journal.AppendAsync(batch, cancellationToken).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested(); return result; }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception) { return new AppendAuthorityResultV1.OutcomeUnknown(OperationId.Create()); }
    }

    private static async ValueTask<BoundedAscii?> VerifyReferencedProofAsync(IAuthorityJournalV1 journal,
        GraphReplacementState state, GraphReplacementJournalCommand command,
        ExpectedAuthorityVectorV1 expectedAuthority, long snapshotThrough, CancellationToken cancellationToken)
    {
        CapacityGrantId grant; JournalPositionV1 position;
        switch (command)
        {
            case GraphReplacementJournalCommand.Prepare prepare:
                grant = prepare.TargetTopology.CapacityGrantId; position = prepare.TargetGrantFact; break;
            case GraphReplacementJournalCommand.SettleSource settle:
                grant = state.SourcePlan.CapacityGrantId; position = settle.SourceSettlementFact; break;
            default: return null;
        }
        if (position.Session != expectedAuthority.Session || position.Sequence > snapshotThrough)
            return new BoundedAscii("graph-capacity-proof-noncausal");
        var proof = await CapacityGrantSnapshotReaderV1.ReadAtAsync(journal, position.Session, grant, position,
            cancellationToken).ConfigureAwait(false);
        if (proof is not CapacityGrantSnapshotAtResultV1.Exact exact)
            return new BoundedAscii("graph-capacity-proof-unknown");
        return command switch
        {
            GraphReplacementJournalCommand.Prepare prepare
                when exact.Grant.CurrentFact == prepare.TargetGrantFact &&
                    GraphReplacementReducer.GrantMatches(exact.Grant, prepare.TargetTopology, expectedAuthority) => null,
            GraphReplacementJournalCommand.SettleSource
                when GraphReplacementReducer.SettlementMatches(
                    exact.Grant, state.SourceGrant, state.SourcePlan) => null,
            _ => new BoundedAscii("graph-capacity-proof-invalid"),
        };
    }

    private static bool Matches(AuthorityFactEnvelopeV1 envelope, ProposedAuthorityFactV1 proposal,
        SessionAuthorityStampV1 session) => envelope.Position.Session == session && envelope.FactId == proposal.FactId &&
        envelope.ThreadScope is null && envelope.Owner == proposal.Owner && envelope.PayloadSchema == proposal.PayloadSchema &&
        envelope.PayloadHash == proposal.PayloadHash && envelope.Payload.SequenceEqual(proposal.Payload);

    private static bool BatchMatches(IReadOnlyList<AuthorityFactEnvelopeV1> envelopes,
        IReadOnlyList<ProposedAuthorityFactV1> proposals, SessionAuthorityStampV1 session) =>
        envelopes.Count == proposals.Count && envelopes.Zip(proposals, (envelope, proposal) =>
            Matches(envelope, proposal, session)).All(static value => value);

    private static GraphReplacementAdmissionResult MapAppend(AppendAuthorityResultV1 result, JournalFactId id) => result switch
    {
        AppendAuthorityResultV1.ContradictoryDuplicate => new GraphReplacementAdmissionResult.ContradictoryDuplicate(id),
        AppendAuthorityResultV1.InvalidPayload invalid => Unknown(id, invalid.SafeCode),
        AppendAuthorityResultV1.StoreUnavailable unavailable => Unknown(id, unavailable.SafeCode),
        _ => Unknown(id, new BoundedAscii("graph-append-outcome-unknown")),
    };

    private static GraphReplacementAdmissionResult.OutcomeUnknown Unknown(JournalFactId id, BoundedAscii code) => new(id, code);
}
