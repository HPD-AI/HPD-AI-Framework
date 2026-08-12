using HPD.Agent.Authority;

namespace HPD.Agent.Audio.Graph;

internal sealed record GraphRuntimeAdmissionRequestV1
{
    internal GraphRuntimeAdmissionRequestV1(GraphRuntimeCommandV1 command,
        ExpectedAuthorityVectorV1 expectedAuthority, CorrelationEnvelopeV1 correlation, UtcInstant observedAt)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(expectedAuthority);
        if (!command.OperationId.IsValid || !command.ExpectedPredecessor.IsValid ||
            command.EffectRequestHash == default || expectedAuthority.Session != command.ExpectedPredecessor.Session ||
            !correlation.IsValid || !TryExactGraph(expectedAuthority, out var graph) || command is GraphRuntimeCommandV1.Activate activate &&
            (!GraphReplacementReducerV1.HasExactGraph(expectedAuthority, activate.GraphGeneration) ||
             activate.GraphAuthorityFact.Session != expectedAuthority.Session ||
             activate.CapacityGrantFact.Session != expectedAuthority.Session ||
             activate.EffectRequestHash != GraphRuntimeEffectHashesV1.Activate(expectedAuthority.Session,activate.OperationId,
                 activate.GraphAuthorityFact,activate.TopologyFingerprint,activate.GraphGeneration,activate.CapacityGrantFact)) ||
            command is GraphRuntimeCommandV1.Retire retire &&
            (retire.ActiveRuntimeFact.Session != expectedAuthority.Session ||
             retire.EffectRequestHash != GraphRuntimeEffectHashesV1.Retire(expectedAuthority.Session,retire.OperationId,retire.ActiveRuntimeFact)))
            throw new ArgumentException("Runtime admission requires one complete graph-scoped request.");

        Command = command;
        ExpectedAuthority = expectedAuthority;
        Correlation = correlation;
        ObservedAt = observedAt;
    }

    private static bool TryExactGraph(ExpectedAuthorityVectorV1 authority,out GraphGenerationId graph)
    { var values=authority.Axes.Where(x=>x.AxisId==AuthorityAxisId.Graph&&x.Value is AuthorityAxisValueV1.Graph).Select(x=>((AuthorityAxisValueV1.Graph)x.Value).Value).ToArray();graph=values.Length==1?values[0]:default;return values.Length==1&&graph.IsValid; }

    internal GraphRuntimeCommandV1 Command { get; }
    internal ExpectedAuthorityVectorV1 ExpectedAuthority { get; }
    internal CorrelationEnvelopeV1 Correlation { get; }
    internal UtcInstant ObservedAt { get; }
}

internal abstract record GraphRuntimeAdmissionResultV1
{
    private GraphRuntimeAdmissionResultV1() { }

    internal sealed record Applied : GraphRuntimeAdmissionResultV1
    {
        internal Applied(GraphRuntimeSnapshotV1 snapshot, AuthorityFactEnvelopeV1 command,
            AuthorityFactEnvelopeV1 result, Hash256 receiptHash)
        {
            ArgumentNullException.ThrowIfNull(snapshot);
            var fact = ValidatePair(command, result);
            if (fact.Outcome is not (GraphRuntimeOutcomeV1.Activated or GraphRuntimeOutcomeV1.Retired) ||
                fact.ResultingSnapshot != snapshot || fact.EffectReceiptHash != receiptHash || receiptHash == default)
                throw new ArgumentException("Applied evidence must be the exact successful C/F tuple.");
            Snapshot=snapshot; CommandFact=command; ResultFact=result; ReceiptHash=receiptHash;
        }
        internal GraphRuntimeSnapshotV1 Snapshot { get; }
        internal AuthorityFactEnvelopeV1 CommandFact { get; }
        internal AuthorityFactEnvelopeV1 ResultFact { get; }
        internal Hash256 ReceiptHash { get; }
    }

    internal sealed record Rejected : GraphRuntimeAdmissionResultV1
    {
        internal Rejected(GraphRuntimeSnapshotV1? snapshot, BoundedAscii code, AuthorityFactEnvelopeV1 command,
            AuthorityFactEnvelopeV1 result)
        {
            var fact=ValidatePair(command,result);
            if(!code.IsValid||fact.Outcome!=GraphRuntimeOutcomeV1.Rejected||fact.ResultingSnapshot!=snapshot||fact.SafeCode!=code)
                throw new ArgumentException("Rejected evidence must be the exact C/F tuple.");
            Snapshot=snapshot;SafeCode=code;CommandFact=command;ResultFact=result;
        }
        internal GraphRuntimeSnapshotV1? Snapshot{get;} internal BoundedAscii SafeCode{get;}
        internal AuthorityFactEnvelopeV1 CommandFact{get;} internal AuthorityFactEnvelopeV1 ResultFact{get;}
    }

    internal sealed record Conflict : GraphRuntimeAdmissionResultV1
    {
        internal Conflict(GraphRuntimeSnapshotV1? snapshot, JournalPositionV1 actual, AuthorityFactEnvelopeV1 command,
            AuthorityFactEnvelopeV1 result)
        {
            var fact=ValidatePair(command,result);
            if(!actual.IsValid||actual.Session!=command.Position.Session||fact.Outcome!=GraphRuntimeOutcomeV1.Conflict||
               fact.ResultingSnapshot!=snapshot||fact.ActualPredecessor!=actual)
                throw new ArgumentException("Conflict evidence must be the exact C/F tuple.");
            Snapshot=snapshot;ActualPredecessor=actual;CommandFact=command;ResultFact=result;
        }
        internal GraphRuntimeSnapshotV1? Snapshot{get;} internal JournalPositionV1 ActualPredecessor{get;}
        internal AuthorityFactEnvelopeV1 CommandFact{get;} internal AuthorityFactEnvelopeV1 ResultFact{get;}
    }

    internal sealed record AlreadyAdmitted : GraphRuntimeAdmissionResultV1
    {
        internal AlreadyAdmitted(AuthorityFactEnvelopeV1 command,AuthorityFactEnvelopeV1 result)
        { ValidatePair(command,result);CommandFact=command;ResultFact=result; }
        internal AuthorityFactEnvelopeV1 CommandFact{get;} internal AuthorityFactEnvelopeV1 ResultFact{get;}
    }

    internal sealed record RuntimeReplaced : GraphRuntimeAdmissionResultV1
    {
        internal RuntimeReplaced(RuntimeGenerationId next,long terminatedAt,long snapshotThrough,
            GraphRuntimeSnapshotV1? snapshot,PendingGraphRuntimeCommandV1? pending,AuthorityFactEnvelopeV1? terminalCommandFact,AuthorityFactEnvelopeV1? terminalResultFact)
        { ValidateTerminal(terminatedAt,snapshotThrough,snapshot,pending,terminalCommandFact,terminalResultFact);if(!next.IsValid||terminalResultFact is not null)throw new ArgumentException("Runtime replacement cannot admit an old-session result.",nameof(next));Next=next;TerminatedAt=terminatedAt;SnapshotThrough=snapshotThrough;Snapshot=snapshot;Pending=pending;TerminalCommandFact=terminalCommandFact;TerminalResultFact=terminalResultFact; }
        internal RuntimeGenerationId Next{get;} internal long TerminatedAt{get;} internal long SnapshotThrough{get;}
        internal GraphRuntimeSnapshotV1? Snapshot{get;} internal PendingGraphRuntimeCommandV1? Pending{get;}
        internal AuthorityFactEnvelopeV1? TerminalCommandFact{get;}
        internal AuthorityFactEnvelopeV1? TerminalResultFact{get;}
    }

    internal sealed record AuthorityGenerationReplaced : GraphRuntimeAdmissionResultV1
    {
        internal AuthorityGenerationReplaced(AuthorityAxisId axis,StableId128 next,long terminatedAt,long snapshotThrough,
            GraphRuntimeSnapshotV1? snapshot,PendingGraphRuntimeCommandV1? pending,AuthorityFactEnvelopeV1? terminalCommandFact,AuthorityFactEnvelopeV1? terminalResultFact)
        { ValidateTerminal(terminatedAt,snapshotThrough,snapshot,pending,terminalCommandFact,terminalResultFact);if(!Enum.IsDefined(axis)||axis==AuthorityAxisId.Runtime||next.Equals(default(StableId128))||!IsClaimed(axis,snapshot,pending,terminalCommandFact))throw new ArgumentException("A valid claimed-axis replacement is required.");Axis=axis;Next=next;TerminatedAt=terminatedAt;SnapshotThrough=snapshotThrough;Snapshot=snapshot;Pending=pending;TerminalCommandFact=terminalCommandFact;TerminalResultFact=terminalResultFact; }
        internal AuthorityAxisId Axis{get;} internal StableId128 Next{get;} internal long TerminatedAt{get;} internal long SnapshotThrough{get;}
        internal GraphRuntimeSnapshotV1? Snapshot{get;} internal PendingGraphRuntimeCommandV1? Pending{get;}
        internal AuthorityFactEnvelopeV1? TerminalCommandFact{get;}
        internal AuthorityFactEnvelopeV1? TerminalResultFact{get;}
    }

    internal sealed record NotAdmitted : GraphRuntimeAdmissionResultV1
    { internal NotAdmitted(BoundedAscii code,long lastVerified){ValidateScalar(code,lastVerified);Code=code;LastVerified=lastVerified;}internal BoundedAscii Code{get;}internal long LastVerified{get;} }
    internal sealed record OutcomeUnknown : GraphRuntimeAdmissionResultV1
    { internal OutcomeUnknown(BoundedAscii code,long lastVerified,PendingGraphRuntimeCommandV1? pending){ValidateScalar(code,lastVerified);if(pending is not null&&(pending.Operation.CommandEnvelope.Position.Sequence>lastVerified||!ValidPending(pending)))throw new ArgumentException("Pending evidence must be unresolved and within the verified prefix.",nameof(pending));Code=code;LastVerified=lastVerified;Pending=pending;}internal BoundedAscii Code{get;}internal long LastVerified{get;}internal PendingGraphRuntimeCommandV1? Pending{get;} }
    internal sealed record ContradictoryDuplicate : GraphRuntimeAdmissionResultV1
    { internal ContradictoryDuplicate(BoundedAscii code,long lastVerified){ValidateScalar(code,lastVerified);Code=code;LastVerified=lastVerified;}internal BoundedAscii Code{get;}internal long LastVerified{get;} }
    internal sealed record InvalidHistory : GraphRuntimeAdmissionResultV1
    { internal InvalidHistory(BoundedAscii code,long lastVerified){ValidateScalar(code,lastVerified);Code=code;LastVerified=lastVerified;}internal BoundedAscii Code{get;}internal long LastVerified{get;} }
    internal sealed record RetryRequired : GraphRuntimeAdmissionResultV1
    { internal RetryRequired(long lastVerified){if(lastVerified<0)throw new ArgumentOutOfRangeException(nameof(lastVerified));LastVerified=lastVerified;}internal long LastVerified{get;} }

    private static GraphRuntimeFactV1 ValidatePair(AuthorityFactEnvelopeV1 command,AuthorityFactEnvelopeV1 result)
    {
        if(!TryCommand(command,out var commandOuter,out var commandBody)||!TryFact(result,out var resultOuter,out var fact)||
           command.Position.Session!=result.Position.Session||command.Position.Sequence>=result.Position.Sequence||
           result.FactId!=GraphRuntimeFactIdsV1.Result(command.Position)||fact!.CommandFact!=command.Position||
           fact.ExpectedPredecessor!=commandBody!.ExpectedPredecessor||resultOuter!.ExpectedAuthority!=commandOuter!.ExpectedAuthority)
            throw new ArgumentException("A valid joined graph-runtime C/F tuple is required.");
        var success=fact.Outcome is GraphRuntimeOutcomeV1.Activated or GraphRuntimeOutcomeV1.Retired;
        if(success&&(commandBody.Kind==GraphRuntimeCommandKindV1.Activate)!=(fact.Outcome==GraphRuntimeOutcomeV1.Activated)||
           fact.ActualPredecessor.Sequence>=command.Position.Sequence||success&&!SuccessSnapshotMatches(commandBody,fact,result.Position,commandOuter.ExpectedAuthority))
            throw new ArgumentException("The graph-runtime F does not exactly resolve its C.");
        return fact;
    }

    private static bool SuccessSnapshotMatches(GraphRuntimeCommandV1 command,GraphRuntimeFactV1 fact,JournalPositionV1 result,
        ExpectedAuthorityVectorV1 authority)=> (command,fact.ResultingSnapshot) switch
    {
        (GraphRuntimeCommandV1.Activate c,{ } s)=>s.Phase==GraphRuntimePhaseV1.Active&&s.ActivationOperationId==c.OperationId&&
            s.GraphGeneration==c.GraphGeneration&&s.TopologyFingerprint==c.TopologyFingerprint&&s.CapacityGrantFact==c.CapacityGrantFact&&
            s.ActivationFact==result&&s.LastRuntimeFact==result&&s.CurrentAuthority==authority,
        (GraphRuntimeCommandV1.Retire c,{ } s)=>s.Phase==GraphRuntimePhaseV1.Retired&&s.Retirement?.OperationId==c.OperationId&&
            s.Retirement.RetireCommandFact==fact.CommandFact&&s.LastRuntimeFact==result&&s.CurrentAuthority==authority,
        _=>false
    };

    private static void ValidateTerminal(long terminatedAt,long snapshotThrough,GraphRuntimeSnapshotV1? snapshot,
        PendingGraphRuntimeCommandV1? pending,AuthorityFactEnvelopeV1? terminalCommandFact,AuthorityFactEnvelopeV1? terminalResultFact)
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

    private static bool IsClaimed(AuthorityAxisId axis,GraphRuntimeSnapshotV1? snapshot,
        PendingGraphRuntimeCommandV1? pending,AuthorityFactEnvelopeV1? command)
    {
        if(axis==AuthorityAxisId.Graph)return true;
        if(snapshot?.CurrentAuthority.Axes.Any(x=>x.AxisId==axis)==true||
           pending?.Operation.CommandOuter.ExpectedAuthority.Axes.Any(x=>x.AxisId==axis)==true)return true;
        return TryCommand(command,out var outer,out _)&&outer!.ExpectedAuthority.Axes.Any(x=>x.AxisId==axis);
    }

    private static bool ValidPending(PendingGraphRuntimeCommandV1 pending)=>pending.Operation.ResultEnvelope is null&&
        TryCommand(pending.Operation.CommandEnvelope,out var outer,out var command)&&command==pending.Operation.Command&&
        outer!.Session==pending.Operation.CommandOuter.Session&&outer.ExpectedAuthority==pending.Operation.CommandOuter.ExpectedAuthority&&
        pending.Operation.OperationId==command!.OperationId&&pending.Operation.Kind==command.Kind&&
        pending.Operation.RequestHash==command.EffectRequestHash&&pending.Operation.CommandEnvelope.Position.Session==outer!.Session&&
        pending.Evaluation switch
        {
            GraphRuntimeReducerV1.EffectRequired e=>e.IsAuthentic&&e.Command==command&&e.Authority==outer.ExpectedAuthority&&
                e.AdmittedCommand==pending.Operation.CommandEnvelope.Position,
            GraphRuntimeEvaluationV1.Rejected e=>e.SafeCode.IsValid,
            GraphRuntimeEvaluationV1.Conflict e=>e.ActualPredecessor.Session==outer.Session&&
                e.ActualPredecessor.Sequence<pending.Operation.CommandEnvelope.Position.Sequence,
            GraphRuntimeEvaluationV1.GenerationReplaced=>true,
            _=>false
        };

    private static bool TryCommand(AuthorityFactEnvelopeV1? envelope,out GraphRuntimeOwnerPayloadV1? outer,out GraphRuntimeCommandV1? body)
    { outer=null;body=null;return envelope is not null&&GraphRuntimePayloadRegistrationsV1.ValidateCommandEnvelope(envelope)&&GraphRuntimeCodecsV1.TryDecodeOuter(envelope.PayloadMemory,out outer)&&GraphRuntimeCodecsV1.TryDecodeCommand(outer!.Body,out body); }
    private static bool TryFact(AuthorityFactEnvelopeV1? envelope,out GraphRuntimeOwnerPayloadV1? outer,out GraphRuntimeFactV1? body)
    { outer=null;body=null;return envelope is not null&&GraphRuntimePayloadRegistrationsV1.ValidateFactEnvelope(envelope)&&GraphRuntimeCodecsV1.TryDecodeOuter(envelope.PayloadMemory,out outer)&&GraphRuntimeCodecsV1.TryDecodeFact(outer!.Body,out body); }
    private static void ValidateScalar(BoundedAscii code,long lastVerified)
    { if(!code.IsValid||lastVerified<0)throw new ArgumentException("A valid code and verified position are required."); }
}
