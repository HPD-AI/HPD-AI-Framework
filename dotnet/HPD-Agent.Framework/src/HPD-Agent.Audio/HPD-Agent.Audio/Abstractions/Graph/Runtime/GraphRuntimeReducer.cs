using HPD.Agent.Authority;

namespace HPD.Agent.Audio.Graph;

internal abstract record GraphRuntimeCurrentGraphEvidence
{
    private static readonly object Issuer=new();
    private GraphRuntimeCurrentGraphEvidence() { }
    internal sealed record Current : GraphRuntimeCurrentGraphEvidence
    { internal Current(object issuer,GraphReplacementJournalFoldResult.Current fold,long through){_issuer=issuer;Fold=fold;SnapshotThrough=through;}private readonly object _issuer;internal bool IssuedBy(object issuer)=>ReferenceEquals(_issuer,issuer);internal GraphReplacementJournalFoldResult.Current Fold{get;}internal long SnapshotThrough{get;} }
    internal sealed record RuntimeReplaced : GraphRuntimeCurrentGraphEvidence
    { internal RuntimeReplaced(object issuer,RuntimeGenerationId replacement,long through){_issuer=issuer;Replacement=replacement;SnapshotThrough=through;}private readonly object _issuer;internal bool IssuedBy(object issuer)=>ReferenceEquals(_issuer,issuer);internal RuntimeGenerationId Replacement{get;}internal long SnapshotThrough{get;} }
    internal static GraphRuntimeCurrentGraphEvidence From(GraphReplacementSnapshotReadResult.Verified verified)
    { ArgumentNullException.ThrowIfNull(verified); return verified.Fold switch {
        GraphReplacementJournalFoldResult.Current current when current.SnapshotThrough == verified.SnapshotThrough&&current.Authority.ThroughPosition==verified.SnapshotThrough => new Current(Issuer,current, verified.SnapshotThrough),
        GraphReplacementJournalFoldResult.RuntimeReplaced replaced when replaced.LastPosition == verified.SnapshotThrough => new RuntimeReplaced(Issuer,replaced.Replacement, verified.SnapshotThrough),
        _ => throw new ArgumentException("Exact verified graph evidence is required.", nameof(verified)),
    }; }
    internal bool IsAuthentic()=>this switch{Current c=>c.IssuedBy(Issuer),RuntimeReplaced r=>r.IssuedBy(Issuer),_=>false};
}

internal abstract record GraphRuntimeEvaluation
{
    private protected GraphRuntimeEvaluation() { }
    internal sealed record Rejected : GraphRuntimeEvaluation { internal Rejected(GraphRuntimeSnapshot? snapshot,BoundedAscii code){if(!code.IsValid)throw new ArgumentException();Snapshot=snapshot;SafeCode=code;}internal GraphRuntimeSnapshot? Snapshot{get;}internal BoundedAscii SafeCode{get;} }
    internal sealed record Conflict : GraphRuntimeEvaluation { internal Conflict(GraphRuntimeSnapshot? snapshot,JournalPositionV1 actual){if(!actual.IsValid)throw new ArgumentException();Snapshot=snapshot;ActualPredecessor=actual;}internal GraphRuntimeSnapshot? Snapshot{get;}internal JournalPositionV1 ActualPredecessor{get;} }
    internal sealed record GenerationReplaced : GraphRuntimeEvaluation { internal GenerationReplaced(GraphRuntimeSnapshot? snapshot){Snapshot=snapshot;}internal GraphRuntimeSnapshot? Snapshot{get;} }
}

internal abstract record GraphRuntimeEffectResolution
{
    private GraphRuntimeEffectResolution() { }
    internal sealed record Completed : GraphRuntimeEffectResolution
    { internal Completed(Hash256 receiptHash) { if (receiptHash == default) throw new ArgumentException("A receipt hash is required."); ReceiptHash = receiptHash; } internal Hash256 ReceiptHash { get; } }
    internal sealed record Refused : GraphRuntimeEffectResolution
    { internal Refused(BoundedAscii safeCode) { if (!safeCode.IsValid) throw new ArgumentException("A refusal code is required."); SafeCode = safeCode; } internal BoundedAscii SafeCode { get; } }
}

internal abstract record GraphRuntimeResolution
{
    private GraphRuntimeResolution() { }
    internal sealed record Applied : GraphRuntimeResolution
    { internal Applied(GraphRuntimeSnapshot snapshot, GraphRuntimeOutcome outcome, Hash256 receiptHash) { if (snapshot is null || receiptHash == default || outcome is not (GraphRuntimeOutcome.Activated or GraphRuntimeOutcome.Retired)) throw new ArgumentException(); Snapshot=snapshot;Outcome=outcome;ReceiptHash=receiptHash;} internal GraphRuntimeSnapshot Snapshot{get;}internal GraphRuntimeOutcome Outcome{get;}internal Hash256 ReceiptHash{get;} }
    internal sealed record Rejected : GraphRuntimeResolution
    { internal Rejected(GraphRuntimeSnapshot? snapshot,BoundedAscii safeCode){if(!safeCode.IsValid)throw new ArgumentException();Snapshot=snapshot;SafeCode=safeCode;}internal GraphRuntimeSnapshot? Snapshot{get;}internal BoundedAscii SafeCode{get;} }
}

internal static class GraphRuntimeReducer
{
    private static readonly object Issuer = new();
    internal sealed record EffectRequired : GraphRuntimeEvaluation
    {
        internal EffectRequired(object issuer,GraphRuntimeCommand command, GraphRuntimeSnapshot? prior, ExpectedAuthorityVectorV1 authority,
            JournalPositionV1 admittedCommand, JournalPositionV1 actualPredecessor)
        { _issuer=issuer;Command=command;Prior=prior;Authority=authority;AdmittedCommand=admittedCommand;ActualPredecessor=actualPredecessor; }
        private readonly object _issuer;
        internal bool IssuedBy(object issuer) => ReferenceEquals(_issuer,issuer);
        internal bool IsAuthentic => IssuedBy(Issuer);
        internal GraphRuntimeCommand Command{get;} internal GraphRuntimeSnapshot? Prior{get;} internal ExpectedAuthorityVectorV1 Authority{get;}
        internal JournalPositionV1 AdmittedCommand{get;} internal JournalPositionV1 ActualPredecessor{get;}
    }

    internal static GraphRuntimeEvaluation Evaluate(GraphRuntimeSnapshot? current, GraphRuntimeCommand command,
        JournalPositionV1 admitted, ExpectedAuthorityVectorV1 outerAuthority, GraphRuntimeCurrentGraphEvidence evidence,
        CapacityGrantSnapshotV1? activationGrant = null)
    {
        if (command is null || outerAuthority is null || evidence is null || !evidence.IsAuthentic() || !admitted.IsValid || outerAuthority.Session != admitted.Session ||
            command.ExpectedPredecessor.Session != admitted.Session || command.ExpectedPredecessor.Sequence >= admitted.Sequence)
            return Reject(current,"runtime-command-invalid");
        var covered=evidence switch{GraphRuntimeCurrentGraphEvidence.Current c=>c.SnapshotThrough,GraphRuntimeCurrentGraphEvidence.RuntimeReplaced r=>r.SnapshotThrough,_=>-1};
        if(covered<admitted.Sequence)return Reject(current,"runtime-graph-evidence-stale");
        if (evidence is GraphRuntimeCurrentGraphEvidence.RuntimeReplaced || evidence is not GraphRuntimeCurrentGraphEvidence.Current graph)
            return new GraphRuntimeEvaluation.GenerationReplaced(current);
        if (!TryGraph(outerAuthority,out var claimedGraph)||!TryGraph(graph.Fold.Authority,out var liveGraph)||claimedGraph!=liveGraph||
            current is not null && current.GraphGeneration != liveGraph)
            return new GraphRuntimeEvaluation.GenerationReplaced(current);
        if (!SessionLifecycleJournalFoldV1.Matches(outerAuthority,graph.Fold.Authority))
            return Reject(current,"authority-vector-stale");

        if (command is GraphRuntimeCommand.Activate activate)
        {
            if (current is not null) return new GraphRuntimeEvaluation.Conflict(current,current.LastRuntimeFact);
            if (graph.Fold.State is not{} graphState||!ValidActivation(graph.Fold,graphState,activate,outerAuthority,activationGrant)) return Reject(null,"runtime-activation-proof-invalid");
            if (activate.ExpectedPredecessor != activate.GraphAuthorityFact) return new GraphRuntimeEvaluation.Conflict(null,activate.GraphAuthorityFact);
            return new EffectRequired(Issuer,activate,null,outerAuthority,admitted,activate.GraphAuthorityFact);
        }
        if (command is GraphRuntimeCommand.Retire retire)
        {
            if (activationGrant is not null) return Reject(current,"runtime-command-invalid");
            if (current is null || current.Phase != GraphRuntimePhase.Active || current.CurrentAuthority != outerAuthority) return Reject(current,"runtime-not-active");
            if (retire.EffectRequestHash != GraphRuntimeEffectHashes.Retire(admitted.Session,retire.OperationId,retire.ActiveRuntimeFact)) return Reject(current,"runtime-effect-request-hash-invalid");
            if (retire.ExpectedPredecessor != current.LastRuntimeFact || retire.ActiveRuntimeFact != current.ActivationFact) return new GraphRuntimeEvaluation.Conflict(current,current.LastRuntimeFact);
            return new EffectRequired(Issuer,retire,current,outerAuthority,admitted,current.LastRuntimeFact);
        }
        return Reject(current,"runtime-command-invalid");
    }

    internal static GraphRuntimeResolution Resolve(EffectRequired capability,GraphRuntimeEffectResolution effect,JournalPositionV1 admitted)
    {
        if(capability is null||!capability.IssuedBy(Issuer)||effect is null||!admitted.IsValid||admitted.Session!=capability.AdmittedCommand.Session||admitted.Sequence<=capability.AdmittedCommand.Sequence)return new GraphRuntimeResolution.Rejected(capability?.Prior,Code("runtime-result-position-invalid"));
        if(effect is GraphRuntimeEffectResolution.Refused refused)return new GraphRuntimeResolution.Rejected(capability.Prior,refused.SafeCode);
        if(effect is not GraphRuntimeEffectResolution.Completed completed)return new GraphRuntimeResolution.Rejected(capability.Prior,Code("runtime-effect-result-invalid"));
        if(capability.Command is GraphRuntimeCommand.Activate a)return new GraphRuntimeResolution.Applied(new(GraphRuntimePhase.Active,a.GraphGeneration,a.TopologyFingerprint,a.CapacityGrantFact,capability.Authority,a.OperationId,admitted,admitted,null),GraphRuntimeOutcome.Activated,completed.ReceiptHash);
        if(capability.Command is GraphRuntimeCommand.Retire r&&capability.Prior is{} p)return new GraphRuntimeResolution.Applied(new(GraphRuntimePhase.Retired,p.GraphGeneration,p.TopologyFingerprint,p.CapacityGrantFact,p.CurrentAuthority,p.ActivationOperationId,p.ActivationFact,admitted,new(r.OperationId,capability.AdmittedCommand)),GraphRuntimeOutcome.Retired,completed.ReceiptHash);
        return new GraphRuntimeResolution.Rejected(capability.Prior,Code("runtime-capability-invalid"));
    }

    private static bool ValidActivation(GraphReplacementJournalFoldResult.Current fold,GraphReplacementState state,GraphRuntimeCommand.Activate a,ExpectedAuthorityVectorV1 authority,CapacityGrantSnapshotV1? grant)
    {
        if(state.Phase!=GraphReplacementPhase.None||fold.InstallationFact is not{} installation||installation.Position!=a.GraphAuthorityFact||
            !GraphReplacementCodecs.TryDecodeOuter(installation.PayloadMemory,out var outer)||!GraphReplacementCodecs.TryDecodeInstalled(outer!.Body,out var body)||
            body!.TopologyFingerprint!=a.TopologyFingerprint||body.Topology.GraphGeneration!=a.GraphGeneration||body.CurrentAuthority!=authority||
            body.ActiveSourceGrantFact!=a.CapacityGrantFact||grant?.CurrentFact!=a.CapacityGrantFact||!GraphReplacementReducer.GrantMatches(grant,state.SourcePlan,authority))return false;
        return a.EffectRequestHash==GraphRuntimeEffectHashes.Activate(a.ExpectedPredecessor.Session,a.OperationId,a.GraphAuthorityFact,a.TopologyFingerprint,a.GraphGeneration,a.CapacityGrantFact);
    }
    private static bool TryGraph(ExpectedAuthorityVectorV1 authority,out GraphGenerationId generation){var values=authority.Axes.Where(x=>x.AxisId==AuthorityAxisId.Graph).ToArray();if(values.Length==1&&values[0].Value is AuthorityAxisValueV1.Graph graph){generation=graph.Value;return generation.IsValid;}generation=default;return false;}
    private static bool TryGraph(CurrentAuthorityVectorSnapshotV1 authority,out GraphGenerationId generation){var values=authority.Axes.Where(x=>x.AxisId==AuthorityAxisId.Graph).ToArray();if(values.Length==1&&values[0].Value is AuthorityAxisValueV1.Graph graph){generation=graph.Value;return generation.IsValid;}generation=default;return false;}
    private static GraphRuntimeEvaluation.Rejected Reject(GraphRuntimeSnapshot? s,string c)=>new(s,Code(c));private static BoundedAscii Code(string c)=>new(c);
}
