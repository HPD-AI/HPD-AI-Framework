using HPD.Agent.Authority;
using System.Collections.Concurrent;
using System.Runtime.CompilerServices;

namespace HPD.Agent.Audio.Graph;

internal sealed record GraphRuntimeAdmissionRequest
{
    internal GraphRuntimeAdmissionRequest(GraphRuntimeCommand command,
        ExpectedAuthorityVectorV1 expectedAuthority, CorrelationEnvelopeV1 correlation, UtcInstant observedAt)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(expectedAuthority);
        if (!command.OperationId.IsValid || !command.ExpectedPredecessor.IsValid ||
            command.EffectRequestHash == default || expectedAuthority.Session != command.ExpectedPredecessor.Session ||
            !correlation.IsValid || !TryExactGraph(expectedAuthority, out var graph) || command is GraphRuntimeCommand.Activate activate &&
            (!GraphReplacementReducer.HasExactGraph(expectedAuthority, activate.GraphGeneration) ||
             activate.GraphAuthorityFact.Session != expectedAuthority.Session ||
             activate.CapacityGrantFact.Session != expectedAuthority.Session ||
             activate.EffectRequestHash != GraphRuntimeEffectHashes.Activate(expectedAuthority.Session,activate.OperationId,
                 activate.GraphAuthorityFact,activate.TopologyFingerprint,activate.GraphGeneration,activate.CapacityGrantFact)) ||
            command is GraphRuntimeCommand.Retire retire &&
            (retire.ActiveRuntimeFact.Session != expectedAuthority.Session ||
             retire.EffectRequestHash != GraphRuntimeEffectHashes.Retire(expectedAuthority.Session,retire.OperationId,retire.ActiveRuntimeFact)))
            throw new ArgumentException("Runtime admission requires one complete graph-scoped request.");

        Command = command;
        ExpectedAuthority = expectedAuthority;
        Correlation = correlation;
        ObservedAt = observedAt;
    }

    private static bool TryExactGraph(ExpectedAuthorityVectorV1 authority,out GraphGenerationId graph)
    { var values=authority.Axes.Where(x=>x.AxisId==AuthorityAxisId.Graph&&x.Value is AuthorityAxisValueV1.Graph).Select(x=>((AuthorityAxisValueV1.Graph)x.Value).Value).ToArray();graph=values.Length==1?values[0]:default;return values.Length==1&&graph.IsValid; }

    internal GraphRuntimeCommand Command { get; }
    internal ExpectedAuthorityVectorV1 ExpectedAuthority { get; }
    internal CorrelationEnvelopeV1 Correlation { get; }
    internal UtcInstant ObservedAt { get; }
}

internal abstract record GraphRuntimeAdmissionResult
{
    private GraphRuntimeAdmissionResult() { }

    internal sealed record Applied : GraphRuntimeAdmissionResult
    {
        internal Applied(GraphRuntimeSnapshot snapshot, AuthorityFactEnvelopeV1 command,
            AuthorityFactEnvelopeV1 result, Hash256 receiptHash)
        {
            ArgumentNullException.ThrowIfNull(snapshot);
            var fact = ValidatePair(command, result);
            if (fact.Outcome is not (GraphRuntimeOutcome.Activated or GraphRuntimeOutcome.Retired) ||
                fact.ResultingSnapshot != snapshot || fact.EffectReceiptHash != receiptHash || receiptHash == default)
                throw new ArgumentException("Applied evidence must be the exact successful C/F tuple.");
            Snapshot=snapshot; CommandFact=command; ResultFact=result; ReceiptHash=receiptHash;
        }
        internal GraphRuntimeSnapshot Snapshot { get; }
        internal AuthorityFactEnvelopeV1 CommandFact { get; }
        internal AuthorityFactEnvelopeV1 ResultFact { get; }
        internal Hash256 ReceiptHash { get; }
    }

    internal sealed record Rejected : GraphRuntimeAdmissionResult
    {
        internal Rejected(GraphRuntimeSnapshot? snapshot, BoundedAscii code, AuthorityFactEnvelopeV1 command,
            AuthorityFactEnvelopeV1 result)
        {
            var fact=ValidatePair(command,result);
            if(!code.IsValid||fact.Outcome!=GraphRuntimeOutcome.Rejected||fact.ResultingSnapshot!=snapshot||fact.SafeCode!=code)
                throw new ArgumentException("Rejected evidence must be the exact C/F tuple.");
            Snapshot=snapshot;SafeCode=code;CommandFact=command;ResultFact=result;
        }
        internal GraphRuntimeSnapshot? Snapshot{get;} internal BoundedAscii SafeCode{get;}
        internal AuthorityFactEnvelopeV1 CommandFact{get;} internal AuthorityFactEnvelopeV1 ResultFact{get;}
    }

    internal sealed record Conflict : GraphRuntimeAdmissionResult
    {
        internal Conflict(GraphRuntimeSnapshot? snapshot, JournalPositionV1 actual, AuthorityFactEnvelopeV1 command,
            AuthorityFactEnvelopeV1 result)
        {
            var fact=ValidatePair(command,result);
            if(!actual.IsValid||actual.Session!=command.Position.Session||fact.Outcome!=GraphRuntimeOutcome.Conflict||
               fact.ResultingSnapshot!=snapshot||fact.ActualPredecessor!=actual)
                throw new ArgumentException("Conflict evidence must be the exact C/F tuple.");
            Snapshot=snapshot;ActualPredecessor=actual;CommandFact=command;ResultFact=result;
        }
        internal GraphRuntimeSnapshot? Snapshot{get;} internal JournalPositionV1 ActualPredecessor{get;}
        internal AuthorityFactEnvelopeV1 CommandFact{get;} internal AuthorityFactEnvelopeV1 ResultFact{get;}
    }

    internal sealed record AlreadyAdmitted : GraphRuntimeAdmissionResult
    {
        internal AlreadyAdmitted(AuthorityFactEnvelopeV1 command,AuthorityFactEnvelopeV1 result)
        { ValidatePair(command,result);CommandFact=command;ResultFact=result; }
        internal AuthorityFactEnvelopeV1 CommandFact{get;} internal AuthorityFactEnvelopeV1 ResultFact{get;}
    }

    internal sealed record RuntimeReplaced : GraphRuntimeAdmissionResult
    {
        internal RuntimeReplaced(RuntimeGenerationId next,long terminatedAt,long snapshotThrough,
            GraphRuntimeSnapshot? snapshot,PendingGraphRuntimeCommand? pending,AuthorityFactEnvelopeV1? terminalCommandFact,AuthorityFactEnvelopeV1? terminalResultFact)
        { ValidateTerminal(terminatedAt,snapshotThrough,snapshot,pending,terminalCommandFact,terminalResultFact);if(!next.IsValid||terminalResultFact is not null)throw new ArgumentException("Runtime replacement cannot admit an old-session result.",nameof(next));Next=next;TerminatedAt=terminatedAt;SnapshotThrough=snapshotThrough;Snapshot=snapshot;Pending=pending;TerminalCommandFact=terminalCommandFact;TerminalResultFact=terminalResultFact; }
        internal RuntimeGenerationId Next{get;} internal long TerminatedAt{get;} internal long SnapshotThrough{get;}
        internal GraphRuntimeSnapshot? Snapshot{get;} internal PendingGraphRuntimeCommand? Pending{get;}
        internal AuthorityFactEnvelopeV1? TerminalCommandFact{get;}
        internal AuthorityFactEnvelopeV1? TerminalResultFact{get;}
    }

    internal sealed record AuthorityGenerationReplaced : GraphRuntimeAdmissionResult
    {
        internal AuthorityGenerationReplaced(AuthorityAxisId axis,StableId128 next,long terminatedAt,long snapshotThrough,
            GraphRuntimeSnapshot? snapshot,PendingGraphRuntimeCommand? pending,AuthorityFactEnvelopeV1? terminalCommandFact,AuthorityFactEnvelopeV1? terminalResultFact)
        { ValidateTerminal(terminatedAt,snapshotThrough,snapshot,pending,terminalCommandFact,terminalResultFact);if(!Enum.IsDefined(axis)||axis==AuthorityAxisId.Runtime||next.Equals(default(StableId128))||!IsClaimed(axis,snapshot,pending,terminalCommandFact))throw new ArgumentException("A valid claimed-axis replacement is required.");Axis=axis;Next=next;TerminatedAt=terminatedAt;SnapshotThrough=snapshotThrough;Snapshot=snapshot;Pending=pending;TerminalCommandFact=terminalCommandFact;TerminalResultFact=terminalResultFact; }
        internal AuthorityAxisId Axis{get;} internal StableId128 Next{get;} internal long TerminatedAt{get;} internal long SnapshotThrough{get;}
        internal GraphRuntimeSnapshot? Snapshot{get;} internal PendingGraphRuntimeCommand? Pending{get;}
        internal AuthorityFactEnvelopeV1? TerminalCommandFact{get;}
        internal AuthorityFactEnvelopeV1? TerminalResultFact{get;}
    }

    internal sealed record NotAdmitted : GraphRuntimeAdmissionResult
    { internal NotAdmitted(BoundedAscii code,long lastVerified){ValidateScalar(code,lastVerified);Code=code;LastVerified=lastVerified;}internal BoundedAscii Code{get;}internal long LastVerified{get;} }
    internal sealed record OutcomeUnknown : GraphRuntimeAdmissionResult
    { internal OutcomeUnknown(BoundedAscii code,long lastVerified,PendingGraphRuntimeCommand? pending,OperationId? operationId=null,GraphRuntimeCommandKind? kind=null,Hash256? requestHash=null){ValidateScalar(code,lastVerified);if(pending is not null&&(pending.Operation.CommandEnvelope.Position.Sequence>lastVerified||!ValidPending(pending)))throw new ArgumentException("Pending evidence must be unresolved and within the verified prefix.",nameof(pending));if(pending is not null){if(operationId is null&&kind is null&&requestHash is null){operationId=pending.Operation.OperationId;kind=pending.Operation.Kind;requestHash=pending.Operation.RequestHash;}else if(pending.Operation.OperationId!=operationId||pending.Operation.Kind!=kind||pending.Operation.RequestHash!=requestHash)throw new ArgumentException("Explicit and pending effect identities must agree.");}var identityPresent=operationId is not null||kind is not null||requestHash is not null;if(identityPresent&&(operationId is not { IsValid:true }||kind is null||!Enum.IsDefined(kind.Value)||requestHash is null||requestHash==default))throw new ArgumentException("A complete effect identity is required.");Code=code;LastVerified=lastVerified;Pending=pending;OperationId=operationId;Kind=kind;RequestHash=requestHash;}internal BoundedAscii Code{get;}internal long LastVerified{get;}internal PendingGraphRuntimeCommand? Pending{get;}internal OperationId? OperationId{get;}internal GraphRuntimeCommandKind? Kind{get;}internal Hash256? RequestHash{get;} }
    internal sealed record ContradictoryDuplicate : GraphRuntimeAdmissionResult
    { internal ContradictoryDuplicate(BoundedAscii code,long lastVerified){ValidateScalar(code,lastVerified);Code=code;LastVerified=lastVerified;}internal BoundedAscii Code{get;}internal long LastVerified{get;} }
    internal sealed record InvalidHistory : GraphRuntimeAdmissionResult
    { internal InvalidHistory(BoundedAscii code,long lastVerified){ValidateScalar(code,lastVerified);Code=code;LastVerified=lastVerified;}internal BoundedAscii Code{get;}internal long LastVerified{get;} }
    internal sealed record RetryRequired : GraphRuntimeAdmissionResult
    { internal RetryRequired(long lastVerified){if(lastVerified<0)throw new ArgumentOutOfRangeException(nameof(lastVerified));LastVerified=lastVerified;}internal long LastVerified{get;} }

    private static GraphRuntimeFact ValidatePair(AuthorityFactEnvelopeV1 command,AuthorityFactEnvelopeV1 result)
    {
        if(!TryCommand(command,out var commandOuter,out var commandBody)||!TryFact(result,out var resultOuter,out var fact)||
           command.Position.Session!=result.Position.Session||command.Position.Sequence>=result.Position.Sequence||
           result.FactId!=GraphRuntimeFactIds.Result(command.Position)||fact!.CommandFact!=command.Position||
           fact.ExpectedPredecessor!=commandBody!.ExpectedPredecessor||resultOuter!.ExpectedAuthority!=commandOuter!.ExpectedAuthority)
            throw new ArgumentException("A valid joined graph-runtime C/F tuple is required.");
        var success=fact.Outcome is GraphRuntimeOutcome.Activated or GraphRuntimeOutcome.Retired;
        if(success&&(commandBody.Kind==GraphRuntimeCommandKind.Activate)!=(fact.Outcome==GraphRuntimeOutcome.Activated)||
           fact.ActualPredecessor.Sequence>=command.Position.Sequence||success&&!SuccessSnapshotMatches(commandBody,fact,result.Position,commandOuter.ExpectedAuthority))
            throw new ArgumentException("The graph-runtime F does not exactly resolve its C.");
        return fact;
    }

    private static bool SuccessSnapshotMatches(GraphRuntimeCommand command,GraphRuntimeFact fact,JournalPositionV1 result,
        ExpectedAuthorityVectorV1 authority)=> (command,fact.ResultingSnapshot) switch
    {
        (GraphRuntimeCommand.Activate c,{ } s)=>s.Phase==GraphRuntimePhase.Active&&s.ActivationOperationId==c.OperationId&&
            s.GraphGeneration==c.GraphGeneration&&s.TopologyFingerprint==c.TopologyFingerprint&&s.CapacityGrantFact==c.CapacityGrantFact&&
            s.ActivationFact==result&&s.LastRuntimeFact==result&&s.CurrentAuthority==authority,
        (GraphRuntimeCommand.Retire c,{ } s)=>s.Phase==GraphRuntimePhase.Retired&&s.Retirement?.OperationId==c.OperationId&&
            s.Retirement.RetireCommandFact==fact.CommandFact&&s.LastRuntimeFact==result&&s.CurrentAuthority==authority,
        _=>false
    };

    private static void ValidateTerminal(long terminatedAt,long snapshotThrough,GraphRuntimeSnapshot? snapshot,
        PendingGraphRuntimeCommand? pending,AuthorityFactEnvelopeV1? terminalCommandFact,AuthorityFactEnvelopeV1? terminalResultFact)
    {
        if(terminatedAt<=0||snapshotThrough<terminatedAt||snapshot is not null&&(snapshot.LastRuntimeFact.Sequence>snapshotThrough||snapshot.CurrentAuthority.Session!=snapshot.LastRuntimeFact.Session)||
           pending is not null&&(pending.Operation.CommandEnvelope.Position.Sequence>=terminatedAt||!ValidPending(pending))||
           (terminalCommandFact is null)!=(terminalResultFact is null))
            throw new ArgumentException("Terminal evidence is not internally consistent.");
        if(terminalResultFact is null)return;
        var fact=ValidatePair(terminalCommandFact!,terminalResultFact);
        if(pending is not null||terminalCommandFact!.Position.Sequence>=terminatedAt||terminalResultFact.Position.Sequence<=terminatedAt||terminalResultFact.Position.Sequence>snapshotThrough||
           terminalResultFact.Position.Session!=terminalCommandFact!.Position.Session||
           fact.ResultingSnapshot!=snapshot||snapshot is not null&&snapshot.CurrentAuthority.Session!=terminalResultFact.Position.Session)
            throw new ArgumentException("The terminal result evidence is invalid.",nameof(terminalResultFact));
    }

    private static bool IsClaimed(AuthorityAxisId axis,GraphRuntimeSnapshot? snapshot,
        PendingGraphRuntimeCommand? pending,AuthorityFactEnvelopeV1? command)
    {
        if(axis==AuthorityAxisId.Graph)return true;
        if(snapshot?.CurrentAuthority.Axes.Any(x=>x.AxisId==axis)==true||
           pending?.Operation.CommandOuter.ExpectedAuthority.Axes.Any(x=>x.AxisId==axis)==true)return true;
        return TryCommand(command,out var outer,out _)&&outer!.ExpectedAuthority.Axes.Any(x=>x.AxisId==axis);
    }

    private static bool ValidPending(PendingGraphRuntimeCommand pending)=>pending.Operation.ResultEnvelope is null&&
        TryCommand(pending.Operation.CommandEnvelope,out var outer,out var command)&&command==pending.Operation.Command&&
        outer!.Session==pending.Operation.CommandOuter.Session&&outer.ExpectedAuthority==pending.Operation.CommandOuter.ExpectedAuthority&&
        pending.Operation.OperationId==command!.OperationId&&pending.Operation.Kind==command.Kind&&
        pending.Operation.RequestHash==command.EffectRequestHash&&pending.Operation.CommandEnvelope.Position.Session==outer!.Session&&
        pending.Evaluation switch
        {
            GraphRuntimeReducer.EffectRequired e=>e.IsAuthentic&&e.Command==command&&e.Authority==outer.ExpectedAuthority&&
                e.AdmittedCommand==pending.Operation.CommandEnvelope.Position,
            GraphRuntimeEvaluation.Rejected e=>e.SafeCode.IsValid,
            GraphRuntimeEvaluation.Conflict e=>e.ActualPredecessor.Session==outer.Session&&
                e.ActualPredecessor.Sequence<pending.Operation.CommandEnvelope.Position.Sequence,
            GraphRuntimeEvaluation.GenerationReplaced=>true,
            _=>false
        };

    private static bool TryCommand(AuthorityFactEnvelopeV1? envelope,out GraphRuntimeOwnerPayload? outer,out GraphRuntimeCommand? body)
    { outer=null;body=null;return envelope is not null&&GraphRuntimePayloadRegistrations.ValidateCommandEnvelope(envelope)&&GraphRuntimeCodecs.TryDecodeOuter(envelope.PayloadMemory,out outer)&&GraphRuntimeCodecs.TryDecodeCommand(outer!.Body,out body); }
    private static bool TryFact(AuthorityFactEnvelopeV1? envelope,out GraphRuntimeOwnerPayload? outer,out GraphRuntimeFact? body)
    { outer=null;body=null;return envelope is not null&&GraphRuntimePayloadRegistrations.ValidateFactEnvelope(envelope)&&GraphRuntimeCodecs.TryDecodeOuter(envelope.PayloadMemory,out outer)&&GraphRuntimeCodecs.TryDecodeFact(outer!.Body,out body); }
    private static void ValidateScalar(BoundedAscii code,long lastVerified)
    { if(!code.IsValid||lastVerified<0)throw new ArgumentException("A valid code and verified position are required."); }
}

internal static class GraphRuntimeAdmissionCoordinator
{
    internal const int MaximumAppendAttempts = 8;
    internal static readonly TimeSpan RecoveryReadTimeout = TimeSpan.FromSeconds(30);
    private static readonly GraphRuntimeRecoveryReadSupervisor RecoveryReads = new(TimeProvider.System);
    private const uint MaximumAppendBytes = ProposedAuthorityFactV1.MaximumPayloadBytes;

    internal delegate ValueTask<GraphRuntimeSnapshotReadResult> SnapshotReader(
        IAuthorityJournalV1 journal, SessionAuthorityStampV1 session, CancellationToken cancellationToken);
    internal delegate ValueTask<BoundedAscii?> ActivationPreflightReader(
        IAuthorityJournalV1 journal, GraphRuntimeCommand.Activate command,
        ExpectedAuthorityVectorV1 authority, GraphRuntimeJournalFoldResult.Current current,
        CancellationToken cancellationToken);

    internal static ValueTask<GraphRuntimeAdmissionResult> AdmitAsync(
        IAuthorityJournalV1 journal, IGraphRuntimeEffectPort effects,
        GraphRuntimeAdmissionRequest request, CancellationToken cancellationToken = default) =>
        AdmitAsync(journal, effects, request, GraphRuntimeSnapshotReader.ReadAsync,
            PreflightActivationAsync, RecoveryReads, cancellationToken);

    internal static async ValueTask<GraphRuntimeAdmissionResult> AdmitAsync(
        IAuthorityJournalV1 journal, IGraphRuntimeEffectPort effects,
        GraphRuntimeAdmissionRequest request, SnapshotReader reader,
        ActivationPreflightReader preflight, CancellationToken cancellationToken = default) =>
        await AdmitAsync(journal,effects,request,reader,preflight,RecoveryReads,cancellationToken).ConfigureAwait(false);

    internal static async ValueTask<GraphRuntimeAdmissionResult> AdmitAsync(
        IAuthorityJournalV1 journal, IGraphRuntimeEffectPort effects,
        GraphRuntimeAdmissionRequest request, SnapshotReader reader,
        ActivationPreflightReader preflight, GraphRuntimeRecoveryReadSupervisor recoveryReads,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(journal); ArgumentNullException.ThrowIfNull(effects);
        ArgumentNullException.ThrowIfNull(request); ArgumentNullException.ThrowIfNull(reader);
        ArgumentNullException.ThrowIfNull(preflight);
        ArgumentNullException.ThrowIfNull(recoveryReads);
        var session = request.ExpectedAuthority.Session;
        var commandId = GraphRuntimeFactIds.Command(session, request.Command.OperationId, request.Command.Kind);
        var commandProposal = Proposal(commandId, GraphRuntimePayloadRegistrations.Command,
            GraphRuntimeCodecs.EncodeOuter(new(session, request.ExpectedAuthority,
                GraphRuntimeCodecs.EncodeCommand(request.Command))), request);
        var appendInvoked = false; var attempts = 0; var freshCommand = false; var freshResult = false;
        GraphRuntimeEffectResolution? knownEffect = null;
        var mustQuery = false;
        var executed = false; var queried = false;
        PendingGraphRuntimeCommand? lastMatchedPending = null;
        long lastVerifiedPin = 0;

        if (cancellationToken.IsCancellationRequested)
            return NotAdmitted("runtime-cancelled-before-command", 0);

        while (attempts < MaximumAppendAttempts)
        {
            var recoveryToken = appendInvoked ? CancellationToken.None : cancellationToken;
            var read = await SafeRead(reader, journal, session, recoveryToken, recoveryReads).ConfigureAwait(false);
            if (!appendInvoked && cancellationToken.IsCancellationRequested)
                return NotAdmitted("runtime-cancelled-before-command", LastVerified(read));
            if (read is not GraphRuntimeSnapshotReadResult.Verified verified)
                return MapRead(read,request,lastMatchedPending,lastVerifiedPin);
            lastVerifiedPin = verified.SnapshotThrough;
            if (verified.Fold is GraphRuntimeJournalFoldResult.RuntimeReplaced runtime)
                return Runtime(runtime);
            if (verified.Fold is GraphRuntimeJournalFoldResult.AuthorityGenerationReplaced terminal)
            {
                if (terminal.TerminalResultFact is not null || terminal.Pending is null) return Authority(terminal);
                mustQuery = true;
            }
            var reconciled = Reconcile(verified, request, freshResult);
            if (reconciled is not null) return reconciled;

            var pending = Pending(verified.Fold);
            if (pending is null)
            {
                // This verified pin proves the target C is absent. Any earlier ambiguous
                // append did not commit, so a C committed below is fresh and may Execute.
                mustQuery = false;
                freshCommand = false;
                if (verified.Fold is not GraphRuntimeJournalFoldResult.Current current)
                    return Unknown("runtime-current-fold-required", verified.SnapshotThrough, null, appendInvoked ? request : null);
                if (current.Operations.Count >= GraphRuntimeJournalFold.MaximumOperations)
                    return NotAdmitted("runtime-operation-bound", verified.SnapshotThrough);
                if (!AuthorityMatches(request.ExpectedAuthority, current.Authority))
                    return NotAdmitted("authority-vector-stale", verified.SnapshotThrough);
                if (request.Command.ExpectedPredecessor.Sequence > verified.SnapshotThrough)
                    return NotAdmitted("runtime-predecessor-unverified", verified.SnapshotThrough);
                if (request.Command is GraphRuntimeCommand.Activate activate)
                {
                    BoundedAscii? preflightFailure;
                    try { preflightFailure = await preflight(journal, activate, request.ExpectedAuthority,
                        current, recoveryToken).ConfigureAwait(false); }
                    catch (Exception) { preflightFailure = Code("runtime-activation-preflight-unknown"); }
                    if (preflightFailure is { } failure)
                        return new GraphRuntimeAdmissionResult.NotAdmitted(failure, verified.SnapshotThrough);
                }
                if (!appendInvoked && cancellationToken.IsCancellationRequested)
                    return NotAdmitted("runtime-cancelled-before-command", verified.SnapshotThrough);
                appendInvoked = true; attempts++;
                var append = await SafeAppend(journal, new(session, verified.SnapshotThrough, [],
                    [commandProposal], MaximumAppendBytes), cancellationToken).ConfigureAwait(false);
                if (append is AppendAuthorityResultV1.Committed committed &&
                    Exact(committed.Envelopes, commandProposal, session)) freshCommand = true;
                else if (append is AppendAuthorityResultV1.AlreadyCommitted already &&
                    Exact(already.Envelopes, commandProposal, session)) mustQuery = true;
                else mustQuery = true;
                // Once C Append is invoked no store result proves absence. Every arm,
                // including contradictory/rejected results, is reconciled by identity.
                continue;
            }

            if (pending.Operation.OperationId != request.Command.OperationId)
                return appendInvoked
                    ? Unknown("runtime-command-pending",verified.SnapshotThrough,null,request)
                    : Unknown("runtime-command-pending", verified.SnapshotThrough, pending);
            if (!SameIdentity(pending, request))
                return Contradictory("runtime-operation-identity-reuse", verified.SnapshotThrough);
            lastMatchedPending = pending;
            if (pending.Operation.ResultEnvelope is not null)
                return new GraphRuntimeAdmissionResult.AlreadyAdmitted(pending.Operation.CommandEnvelope,
                    pending.Operation.ResultEnvelope);

            if (pending.Evaluation is GraphRuntimeReducer.EffectRequired capability)
            {
                if (knownEffect is null)
                {
                    if (mustQuery || !freshCommand)
                    {
                        if (queried) return Unknown("runtime-effect-recovery-exhausted", verified.SnapshotThrough, pending);
                        queried = true;
                        GraphRuntimeEffectQueryResult query;
                        try { query = await effects.QueryAsync(new(request.Command.OperationId, request.Command.Kind,
                            request.Command.EffectRequestHash), CancellationToken.None).ConfigureAwait(false); }
                        catch (Exception) { return Unknown("runtime-effect-query-unknown", verified.SnapshotThrough, pending); }
                        switch (query)
                        {
                            case GraphRuntimeEffectQueryResult.Completed completed:
                                knownEffect = Completed(session, request.Command, completed.ReceiptBytes.Span); break;
                            case GraphRuntimeEffectQueryResult.NotObserved when verified.Fold is GraphRuntimeJournalFoldResult.AuthorityGenerationReplaced:
                                if (await AppendRendered(null, linearizableNotObserved: true).ConfigureAwait(false) is { } terminalResult)
                                    return terminalResult;
                                continue;
                            case GraphRuntimeEffectQueryResult.NotObserved:
                                if (executed) return Unknown("runtime-effect-not-observed-after-execute", verified.SnapshotThrough, pending);
                                mustQuery = false; freshCommand = true; continue;
                            case GraphRuntimeEffectQueryResult.Contradictory:
                                return Contradictory("runtime-effect-contradictory", verified.SnapshotThrough);
                            case GraphRuntimeEffectQueryResult.OutcomeUnknown unknown:
                                return new GraphRuntimeAdmissionResult.OutcomeUnknown(unknown.SafeCode,
                                    verified.SnapshotThrough, pending);
                            default: return Unknown("runtime-effect-query-unknown", verified.SnapshotThrough, pending);
                        }
                    }
                    else
                    {
                        if (executed) return Unknown("runtime-effect-execute-exhausted", verified.SnapshotThrough, pending);
                        executed = true;
                        GraphRuntimeEffectExecutionResult execution;
                        try { execution = await effects.ExecuteAsync(GraphRuntimeEffectRequest.From(capability),
                            cancellationToken).ConfigureAwait(false); }
                        catch (Exception) { mustQuery = true; continue; }
                        switch (execution)
                        {
                            case GraphRuntimeEffectExecutionResult.Completed completed:
                                knownEffect = Completed(session, request.Command, completed.ReceiptBytes.Span); break;
                            case GraphRuntimeEffectExecutionResult.Refused refused:
                                knownEffect = new GraphRuntimeEffectResolution.Refused(refused.SafeCode); break;
                            case GraphRuntimeEffectExecutionResult.OutcomeUnknown:
                                mustQuery = true; continue;
                            default: mustQuery = true; continue;
                        }
                    }
                }
                if (await AppendRendered(knownEffect, false).ConfigureAwait(false) is { } effectResult)
                    return effectResult;
                continue;
            }
            if (await AppendRendered(null, false).ConfigureAwait(false) is { } deterministicResult)
                return deterministicResult;
            continue;

            async ValueTask<GraphRuntimeAdmissionResult?> AppendRendered(
                GraphRuntimeEffectResolution? effect, bool linearizableNotObserved)
            {
                var rendered = GraphRuntimeJournalFold.RenderFact(verified.Fold, pending,
                    new(session, verified.SnapshotThrough + 1), effect, linearizableNotObserved);
                if (rendered is null) return Invalid("runtime-result-render-failed", verified.SnapshotThrough);
                var resultProposal = Proposal(GraphRuntimeFactIds.Result(pending.Operation.CommandEnvelope.Position),
                    GraphRuntimePayloadRegistrations.Fact,
                    GraphRuntimeCodecs.EncodeOuter(new(session, pending.Operation.CommandOuter.ExpectedAuthority,
                        GraphRuntimeCodecs.EncodeFact(rendered.Body))), request);
                attempts++;
                var append = await SafeAppend(journal, new(session, verified.SnapshotThrough, [],
                    [resultProposal], MaximumAppendBytes), CancellationToken.None).ConfigureAwait(false);
                freshResult = append is AppendAuthorityResultV1.Committed committed &&
                    Exact(committed.Envelopes, resultProposal, session);
                // CAS drift, lost acknowledgement, and even a duplicate report are all
                // reconciled from a fresh pin. The known effect is retained and is never
                // executed again. Only attempt eight falls through to the mandatory read.
                return null;
            }
        }

        var finalRead = await SafeRead(reader, journal, session, CancellationToken.None, recoveryReads).ConfigureAwait(false);
        if (finalRead is not GraphRuntimeSnapshotReadResult.Verified finalVerifiedRead)
            return MapRead(finalRead,request,lastMatchedPending,lastVerifiedPin);
        if (Reconcile(finalVerifiedRead, request, freshResult) is { } finalReconciled) return finalReconciled;
        return finalVerifiedRead.Fold switch
        {
            GraphRuntimeJournalFoldResult.RuntimeReplaced runtime => Runtime(runtime),
            GraphRuntimeJournalFoldResult.AuthorityGenerationReplaced authority => Authority(authority),
            _ => new GraphRuntimeAdmissionResult.RetryRequired(finalVerifiedRead.SnapshotThrough),
        };
    }

    private static async ValueTask<BoundedAscii?> PreflightActivationAsync(IAuthorityJournalV1 journal,
        GraphRuntimeCommand.Activate command, ExpectedAuthorityVectorV1 authority,
        GraphRuntimeJournalFoldResult.Current current,
        CancellationToken cancellationToken)
    {
        if (command.CapacityGrantFact.Sequence > current.SnapshotThrough)
            return Code("runtime-activation-proof-noncausal");
        if (current.Graph is not { State: { } state } graph ||
            graph.SnapshotThrough != current.SnapshotThrough ||
            graph.InstallationFact?.Position != command.GraphAuthorityFact ||
            state.SourcePlan.CapacityGrantId.IsValid is false)
            return Code("runtime-activation-proof-invalid");
        var capacity = await CapacityGrantSnapshotReaderV1.ReadAtAsync(journal, authority.Session,
            state.SourcePlan.CapacityGrantId, command.CapacityGrantFact, cancellationToken).ConfigureAwait(false);
        return capacity is CapacityGrantSnapshotAtResultV1.Exact exact &&
            exact.Grant.CurrentFact == command.CapacityGrantFact &&
            GraphReplacementReducer.GrantMatches(exact.Grant, state.SourcePlan, authority)
                ? null : Code("runtime-activation-proof-invalid");
    }

    private static ProposedAuthorityFactV1 Proposal(JournalFactId id, AuthorityPayloadRegistrationV1 registration,
        byte[] payload, GraphRuntimeAdmissionRequest request) => new(id, null, registration.Owner,
            registration.Schema, payload, AuthorityPayloadHashV1.Compute(registration.SchemaToken,
                registration.Schema, payload), request.Correlation, request.ObservedAt);

    private static async ValueTask<AppendAuthorityResultV1> SafeAppend(IAuthorityJournalV1 journal,
        AppendAuthorityBatchV1 batch, CancellationToken cancellationToken)
    {
        try { return await journal.AppendAsync(batch, cancellationToken).ConfigureAwait(false); }
        catch (Exception) { return new AppendAuthorityResultV1.OutcomeUnknown(batch.Facts[0].Correlation.OperationId ?? OperationId.Create()); }
    }

    private static async ValueTask<GraphRuntimeSnapshotReadResult> SafeRead(SnapshotReader reader,
        IAuthorityJournalV1 journal, SessionAuthorityStampV1 session, CancellationToken cancellationToken,
        GraphRuntimeRecoveryReadSupervisor recoveryReads)
    {
        try { return cancellationToken == CancellationToken.None
            ? await recoveryReads.ReadAsync(reader,journal,session).ConfigureAwait(false)
            : await reader(journal,session,cancellationToken).ConfigureAwait(false); }
        catch (Exception) { return new GraphRuntimeSnapshotReadResult.OutcomeUnknown(Code("runtime-read-exception"), 0, null); }
    }

    private static GraphRuntimeAdmissionResult? Reconcile(GraphRuntimeSnapshotReadResult.Verified read,
        GraphRuntimeAdmissionRequest request, bool freshResult)
    {
        var operation = Operations(read.Fold).FirstOrDefault(x => x.OperationId == request.Command.OperationId) ??
            TerminalOperation(read.Fold, request.Command.OperationId);
        if (operation is null) return null;
        if (operation.Kind != request.Command.Kind || operation.RequestHash != request.Command.EffectRequestHash)
            return Contradictory("runtime-operation-identity-reuse", read.SnapshotThrough);
        if (operation.ResultEnvelope is null) return null;
        if (!freshResult) return new GraphRuntimeAdmissionResult.AlreadyAdmitted(operation.CommandEnvelope,
            operation.ResultEnvelope);
        GraphRuntimeCodecs.TryDecodeOuter(operation.ResultEnvelope.PayloadMemory, out var outer);
        GraphRuntimeCodecs.TryDecodeFact(outer!.Body, out var fact);
        return fact!.Outcome switch
        {
            GraphRuntimeOutcome.Activated or GraphRuntimeOutcome.Retired =>
                new GraphRuntimeAdmissionResult.Applied(fact.ResultingSnapshot!, operation.CommandEnvelope,
                    operation.ResultEnvelope, fact.EffectReceiptHash!.Value),
            GraphRuntimeOutcome.Rejected => new GraphRuntimeAdmissionResult.Rejected(fact.ResultingSnapshot,
                fact.SafeCode!.Value, operation.CommandEnvelope, operation.ResultEnvelope),
            GraphRuntimeOutcome.Conflict => new GraphRuntimeAdmissionResult.Conflict(fact.ResultingSnapshot,
                fact.ActualPredecessor, operation.CommandEnvelope, operation.ResultEnvelope),
            _ => new GraphRuntimeAdmissionResult.AlreadyAdmitted(operation.CommandEnvelope, operation.ResultEnvelope),
        };
    }

    private static IReadOnlyList<GraphRuntimeJournalOperation> Operations(GraphRuntimeJournalFoldResult fold) =>
        fold is GraphRuntimeJournalFoldResult.Current current ? current.Operations :
        Pending(fold) is { } pending ? [pending.Operation] : [];
    private static GraphRuntimeJournalOperation? TerminalOperation(GraphRuntimeJournalFoldResult fold,
        OperationId requested)
    {
        var pair = fold switch
        {
            GraphRuntimeJournalFoldResult.RuntimeReplaced x => (x.TerminalCommandFact, x.TerminalResultFact),
            GraphRuntimeJournalFoldResult.AuthorityGenerationReplaced x => (x.TerminalCommandFact, x.TerminalResultFact),
            _ => (null, null),
        };
        if (pair.Item1 is null || pair.Item2 is null ||
            !GraphRuntimeCodecs.TryDecodeOuter(pair.Item1.PayloadMemory, out var outer) ||
            !GraphRuntimeCodecs.TryDecodeCommand(outer!.Body, out var command) || command!.OperationId != requested) return null;
        return new(command!.OperationId, command.Kind, command.EffectRequestHash, pair.Item1, outer,
            command, pair.Item2);
    }
    private static PendingGraphRuntimeCommand? Pending(GraphRuntimeJournalFoldResult fold) => fold switch
    { GraphRuntimeJournalFoldResult.Current x=>x.Pending, GraphRuntimeJournalFoldResult.RuntimeReplaced x=>x.Pending,
      GraphRuntimeJournalFoldResult.AuthorityGenerationReplaced x=>x.Pending, _=>null };
    private static bool SameIdentity(PendingGraphRuntimeCommand p, GraphRuntimeAdmissionRequest r) =>
        p.Operation.OperationId==r.Command.OperationId&&p.Operation.Kind==r.Command.Kind&&p.Operation.RequestHash==r.Command.EffectRequestHash;
    internal static bool AuthorityMatches(ExpectedAuthorityVectorV1 expected, CurrentAuthorityVectorSnapshotV1 current) =>
        SessionLifecycleJournalFoldV1.Matches(expected,current) &&
        expected.Axes.All(x=>current.Axes.Any(y=>x.AxisId==y.AxisId&&x.Value==y.Value));
    private static bool Exact(IReadOnlyList<AuthorityFactEnvelopeV1> envelopes, ProposedAuthorityFactV1 proposal,
        SessionAuthorityStampV1 session) => envelopes.Count==1&&envelopes[0].Position.Session==session&&
        envelopes[0].FactId==proposal.FactId&&envelopes[0].Owner==proposal.Owner&&envelopes[0].PayloadSchema==proposal.PayloadSchema&&
        envelopes[0].PayloadHash==proposal.PayloadHash&&envelopes[0].Payload.SequenceEqual(proposal.Payload);
    private static GraphRuntimeEffectResolution.Completed Completed(SessionAuthorityStampV1 session,
        GraphRuntimeCommand command, ReadOnlySpan<byte> receipt) => new(GraphRuntimeEffectHashes.Receipt(session,
            command.Kind, command.OperationId, command.EffectRequestHash, receipt));
    private static GraphRuntimeAdmissionResult MapRead(GraphRuntimeSnapshotReadResult read,
        GraphRuntimeAdmissionRequest? identity = null) => read switch
    { GraphRuntimeSnapshotReadResult.InvalidHistory x=>new GraphRuntimeAdmissionResult.InvalidHistory(x.Code,x.LastVerified),
      GraphRuntimeSnapshotReadResult.OutcomeUnknown x=>new GraphRuntimeAdmissionResult.OutcomeUnknown(x.Code,x.LastVerified,
          identity is null||x.Pending is null||SameIdentity(x.Pending,identity)?x.Pending:null,
          identity?.Command.OperationId,identity?.Command.Kind,identity?.Command.EffectRequestHash),
      _=>identity is null?Unknown("runtime-read-result-unknown",0,null):new GraphRuntimeAdmissionResult.OutcomeUnknown(
          Code("runtime-read-result-unknown"),0,null,identity.Command.OperationId,identity.Command.Kind,identity.Command.EffectRequestHash) };
    private static GraphRuntimeAdmissionResult MapRead(GraphRuntimeSnapshotReadResult read,
        GraphRuntimeAdmissionRequest identity,PendingGraphRuntimeCommand? lastMatchedPending,long lastVerifiedPin) =>
        read is GraphRuntimeSnapshotReadResult.OutcomeUnknown unknown
            ? new GraphRuntimeAdmissionResult.OutcomeUnknown(unknown.Code,
                Math.Max(unknown.LastVerified,lastVerifiedPin),
                unknown.Pending is { } reported && SameIdentity(reported,identity)
                    ? reported
                    : unknown.LastVerified <= lastVerifiedPin ? lastMatchedPending : null,
                identity.Command.OperationId,identity.Command.Kind,identity.Command.EffectRequestHash)
            : MapRead(read,identity);
    private static long LastVerified(GraphRuntimeSnapshotReadResult read) => read switch
    { GraphRuntimeSnapshotReadResult.Verified x=>x.SnapshotThrough,
      GraphRuntimeSnapshotReadResult.InvalidHistory x=>x.LastVerified,
      GraphRuntimeSnapshotReadResult.OutcomeUnknown x=>x.LastVerified, _=>0 };
    private static GraphRuntimeAdmissionResult Runtime(GraphRuntimeJournalFoldResult.RuntimeReplaced x) =>
        new GraphRuntimeAdmissionResult.RuntimeReplaced(x.Next,x.TerminatedAt,x.SnapshotThrough,x.Snapshot,x.Pending,
            x.TerminalResultFact is null ? null : x.TerminalCommandFact,x.TerminalResultFact);
    private static GraphRuntimeAdmissionResult Authority(GraphRuntimeJournalFoldResult.AuthorityGenerationReplaced x) =>
        new GraphRuntimeAdmissionResult.AuthorityGenerationReplaced(x.Axis,x.Next,x.TerminatedAt,x.SnapshotThrough,
            x.Snapshot,x.Pending,x.TerminalResultFact is null ? null : x.TerminalCommandFact,x.TerminalResultFact);
    private static GraphRuntimeAdmissionResult.NotAdmitted NotAdmitted(string c,long p)=>new(Code(c),p);
    private static GraphRuntimeAdmissionResult.OutcomeUnknown Unknown(string c,long p,
        PendingGraphRuntimeCommand? q,GraphRuntimeAdmissionRequest? identity=null)=>new(Code(c),p,q,
            identity?.Command.OperationId,identity?.Command.Kind,identity?.Command.EffectRequestHash);
    private static GraphRuntimeAdmissionResult.ContradictoryDuplicate Contradictory(string c,long p)=>new(Code(c),p);
    private static GraphRuntimeAdmissionResult.InvalidHistory Invalid(string c,long p)=>new(Code(c),p);
    private static BoundedAscii Code(string value)=>new(value);
}

internal sealed class GraphRuntimeRecoveryReadSupervisor
{
    private readonly TimeProvider _timeProvider;
    private readonly ConditionalWeakTable<IAuthorityJournalV1,
        ConcurrentDictionary<SessionAuthorityStampV1, Slot>> _journals = new();

    internal GraphRuntimeRecoveryReadSupervisor(TimeProvider timeProvider) =>
        _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));

    internal async ValueTask<GraphRuntimeSnapshotReadResult> ReadAsync(
        GraphRuntimeAdmissionCoordinator.SnapshotReader reader, IAuthorityJournalV1 journal,
        SessionAuthorityStampV1 session)
    {
        ArgumentNullException.ThrowIfNull(reader); ArgumentNullException.ThrowIfNull(journal);
        if (!session.IsValid) throw new ArgumentException("A valid session is required.",nameof(session));
        var sessions = _journals.GetValue(journal, static _ => new());
        var slot = new Slot();
        if (!sessions.TryAdd(session,slot))
            return Unknown("runtime-recovery-read-occupied");
        // Scheduler delay is outside the frozen recovery budget. The worker signals
        // immediately before invoking ReadAsync; only then does the caller arm 30s.
        var started = new TaskCompletionSource<(Task<GraphRuntimeSnapshotReadResult> Read,
            Task Deadline)>(TaskCreationOptions.RunContinuationsAsynchronously);
        _ = Task.Run(() =>
        {
            var deadline = Task.Delay(GraphRuntimeAdmissionCoordinator.RecoveryReadTimeout,_timeProvider);
            Task<GraphRuntimeSnapshotReadResult> read;
            try
            {
                read = reader(journal,session,CancellationToken.None).AsTask();
            }
            catch (Exception error)
            {
                read = Task.FromException<GraphRuntimeSnapshotReadResult>(error);
            }
            started.SetResult((read,deadline));
        });
        var (task,deadline) = await started.Task.ConfigureAwait(false);
        slot.Task=task;
        _ = task.ContinueWith(static (_,state) =>
        {
            var release = (Release)state!;
            release.Sessions.TryRemove(new KeyValuePair<SessionAuthorityStampV1,Slot>(release.Session,release.Slot));
        },new Release(sessions,session,slot),CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,TaskScheduler.Default);
        if (task.IsCompleted) return await Complete(task).ConfigureAwait(false);
        await Task.WhenAny(task,deadline).ConfigureAwait(false);
        // Completion wins when both signals are observable at the deadline.
        return task.IsCompleted ? await Complete(task).ConfigureAwait(false) : Unknown("runtime-recovery-read-timeout");
    }

    private static async Task<GraphRuntimeSnapshotReadResult> Complete(Task<GraphRuntimeSnapshotReadResult> task)
    { try{return await task.ConfigureAwait(false);}catch(Exception){return Unknown("runtime-read-exception");} }
    private static GraphRuntimeSnapshotReadResult.OutcomeUnknown Unknown(string code) =>
        new(new BoundedAscii(code),0,null);
    private sealed class Slot { internal Task<GraphRuntimeSnapshotReadResult>? Task { get; set; } }
    private sealed record Release(ConcurrentDictionary<SessionAuthorityStampV1,Slot> Sessions,
        SessionAuthorityStampV1 Session, Slot Slot);
}
