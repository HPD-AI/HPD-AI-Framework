using HPD.Agent.Authority;

namespace HPD.Agent.Audio.Graph;

internal abstract record GraphRuntimeCurrentGraphEvidenceV1
{
    private static readonly object Issuer=new();
    private GraphRuntimeCurrentGraphEvidenceV1() { }
    internal sealed record Current : GraphRuntimeCurrentGraphEvidenceV1
    { internal Current(object issuer,GraphReplacementJournalFoldResultV1.Current fold,long through){_issuer=issuer;Fold=fold;SnapshotThrough=through;}private readonly object _issuer;internal bool IssuedBy(object issuer)=>ReferenceEquals(_issuer,issuer);internal GraphReplacementJournalFoldResultV1.Current Fold{get;}internal long SnapshotThrough{get;} }
    internal sealed record RuntimeReplaced : GraphRuntimeCurrentGraphEvidenceV1
    { internal RuntimeReplaced(object issuer,RuntimeGenerationId replacement,long through){_issuer=issuer;Replacement=replacement;SnapshotThrough=through;}private readonly object _issuer;internal bool IssuedBy(object issuer)=>ReferenceEquals(_issuer,issuer);internal RuntimeGenerationId Replacement{get;}internal long SnapshotThrough{get;} }
    internal static GraphRuntimeCurrentGraphEvidenceV1 From(GraphReplacementSnapshotReadResultV1.Verified verified)
    { ArgumentNullException.ThrowIfNull(verified); return verified.Fold switch {
        GraphReplacementJournalFoldResultV1.Current current when current.SnapshotThrough == verified.SnapshotThrough&&current.Authority.ThroughPosition==verified.SnapshotThrough => new Current(Issuer,current, verified.SnapshotThrough),
        GraphReplacementJournalFoldResultV1.RuntimeReplaced replaced when replaced.LastPosition == verified.SnapshotThrough => new RuntimeReplaced(Issuer,replaced.Replacement, verified.SnapshotThrough),
        _ => throw new ArgumentException("Exact verified graph evidence is required.", nameof(verified)),
    }; }
    internal bool IsAuthentic()=>this switch{Current c=>c.IssuedBy(Issuer),RuntimeReplaced r=>r.IssuedBy(Issuer),_=>false};
}

internal abstract record GraphRuntimeEvaluationV1
{
    private protected GraphRuntimeEvaluationV1() { }
    internal sealed record Rejected : GraphRuntimeEvaluationV1 { internal Rejected(GraphRuntimeSnapshotV1? snapshot,BoundedAscii code){if(!code.IsValid)throw new ArgumentException();Snapshot=snapshot;SafeCode=code;}internal GraphRuntimeSnapshotV1? Snapshot{get;}internal BoundedAscii SafeCode{get;} }
    internal sealed record Conflict : GraphRuntimeEvaluationV1 { internal Conflict(GraphRuntimeSnapshotV1? snapshot,JournalPositionV1 actual){if(!actual.IsValid)throw new ArgumentException();Snapshot=snapshot;ActualPredecessor=actual;}internal GraphRuntimeSnapshotV1? Snapshot{get;}internal JournalPositionV1 ActualPredecessor{get;} }
    internal sealed record GenerationReplaced : GraphRuntimeEvaluationV1 { internal GenerationReplaced(GraphRuntimeSnapshotV1? snapshot){Snapshot=snapshot;}internal GraphRuntimeSnapshotV1? Snapshot{get;} }
}

internal abstract record GraphRuntimeEffectResolutionV1
{
    private GraphRuntimeEffectResolutionV1() { }
    internal sealed record Completed : GraphRuntimeEffectResolutionV1
    { internal Completed(Hash256 receiptHash) { if (receiptHash == default) throw new ArgumentException("A receipt hash is required."); ReceiptHash = receiptHash; } internal Hash256 ReceiptHash { get; } }
    internal sealed record Refused : GraphRuntimeEffectResolutionV1
    { internal Refused(BoundedAscii safeCode) { if (!safeCode.IsValid) throw new ArgumentException("A refusal code is required."); SafeCode = safeCode; } internal BoundedAscii SafeCode { get; } }
}

internal abstract record GraphRuntimeResolutionV1
{
    private GraphRuntimeResolutionV1() { }
    internal sealed record Applied : GraphRuntimeResolutionV1
    { internal Applied(GraphRuntimeSnapshotV1 snapshot, GraphRuntimeOutcomeV1 outcome, Hash256 receiptHash) { if (snapshot is null || receiptHash == default || outcome is not (GraphRuntimeOutcomeV1.Activated or GraphRuntimeOutcomeV1.Retired)) throw new ArgumentException(); Snapshot=snapshot;Outcome=outcome;ReceiptHash=receiptHash;} internal GraphRuntimeSnapshotV1 Snapshot{get;}internal GraphRuntimeOutcomeV1 Outcome{get;}internal Hash256 ReceiptHash{get;} }
    internal sealed record Rejected : GraphRuntimeResolutionV1
    { internal Rejected(GraphRuntimeSnapshotV1? snapshot,BoundedAscii safeCode){if(!safeCode.IsValid)throw new ArgumentException();Snapshot=snapshot;SafeCode=safeCode;}internal GraphRuntimeSnapshotV1? Snapshot{get;}internal BoundedAscii SafeCode{get;} }
}

internal static class GraphRuntimeReducerV1
{
    private static readonly object Issuer = new();
    internal sealed record EffectRequired : GraphRuntimeEvaluationV1
    {
        internal EffectRequired(object issuer,GraphRuntimeCommandV1 command, GraphRuntimeSnapshotV1? prior, ExpectedAuthorityVectorV1 authority,
            JournalPositionV1 admittedCommand, JournalPositionV1 actualPredecessor)
        { _issuer=issuer;Command=command;Prior=prior;Authority=authority;AdmittedCommand=admittedCommand;ActualPredecessor=actualPredecessor; }
        private readonly object _issuer;
        internal bool IssuedBy(object issuer) => ReferenceEquals(_issuer,issuer);
        internal GraphRuntimeCommandV1 Command{get;} internal GraphRuntimeSnapshotV1? Prior{get;} internal ExpectedAuthorityVectorV1 Authority{get;}
        internal JournalPositionV1 AdmittedCommand{get;} internal JournalPositionV1 ActualPredecessor{get;}
    }

    internal static GraphRuntimeEvaluationV1 Evaluate(GraphRuntimeSnapshotV1? current, GraphRuntimeCommandV1 command,
        JournalPositionV1 admitted, ExpectedAuthorityVectorV1 outerAuthority, GraphRuntimeCurrentGraphEvidenceV1 evidence,
        CapacityGrantSnapshotV1? activationGrant = null)
    {
        if (command is null || outerAuthority is null || evidence is null || !evidence.IsAuthentic() || !admitted.IsValid || outerAuthority.Session != admitted.Session ||
            command.ExpectedPredecessor.Session != admitted.Session || command.ExpectedPredecessor.Sequence >= admitted.Sequence)
            return Reject(current,"runtime-command-invalid");
        var covered=evidence switch{GraphRuntimeCurrentGraphEvidenceV1.Current c=>c.SnapshotThrough,GraphRuntimeCurrentGraphEvidenceV1.RuntimeReplaced r=>r.SnapshotThrough,_=>-1};
        if(covered<admitted.Sequence)return Reject(current,"runtime-graph-evidence-stale");
        if (evidence is GraphRuntimeCurrentGraphEvidenceV1.RuntimeReplaced || evidence is not GraphRuntimeCurrentGraphEvidenceV1.Current graph)
            return new GraphRuntimeEvaluationV1.GenerationReplaced(current);
        if (!TryGraph(outerAuthority,out var claimedGraph)||!TryGraph(graph.Fold.Authority,out var liveGraph)||claimedGraph!=liveGraph||
            current is not null && current.GraphGeneration != liveGraph)
            return new GraphRuntimeEvaluationV1.GenerationReplaced(current);
        if (!SessionLifecycleJournalFoldV1.Matches(outerAuthority,graph.Fold.Authority))
            return Reject(current,"authority-vector-stale");

        if (command is GraphRuntimeCommandV1.Activate activate)
        {
            if (current is not null) return new GraphRuntimeEvaluationV1.Conflict(current,current.LastRuntimeFact);
            if (graph.Fold.State is not{} graphState||!ValidActivation(graph.Fold,graphState,activate,outerAuthority,activationGrant)) return Reject(null,"runtime-activation-proof-invalid");
            if (activate.ExpectedPredecessor != activate.GraphAuthorityFact) return new GraphRuntimeEvaluationV1.Conflict(null,activate.GraphAuthorityFact);
            return new EffectRequired(Issuer,activate,null,outerAuthority,admitted,activate.GraphAuthorityFact);
        }
        if (command is GraphRuntimeCommandV1.Retire retire)
        {
            if (activationGrant is not null) return Reject(current,"runtime-command-invalid");
            if (current is null || current.Phase != GraphRuntimePhaseV1.Active || current.CurrentAuthority != outerAuthority) return Reject(current,"runtime-not-active");
            if (retire.EffectRequestHash != GraphRuntimeEffectHashesV1.Retire(admitted.Session,retire.OperationId,retire.ActiveRuntimeFact)) return Reject(current,"runtime-effect-request-hash-invalid");
            if (retire.ExpectedPredecessor != current.LastRuntimeFact || retire.ActiveRuntimeFact != current.ActivationFact) return new GraphRuntimeEvaluationV1.Conflict(current,current.LastRuntimeFact);
            return new EffectRequired(Issuer,retire,current,outerAuthority,admitted,current.LastRuntimeFact);
        }
        return Reject(current,"runtime-command-invalid");
    }

    internal static GraphRuntimeResolutionV1 Resolve(EffectRequired capability,GraphRuntimeEffectResolutionV1 effect,JournalPositionV1 admitted)
    {
        if(capability is null||!capability.IssuedBy(Issuer)||effect is null||!admitted.IsValid||admitted.Session!=capability.AdmittedCommand.Session||admitted.Sequence<=capability.AdmittedCommand.Sequence)return new GraphRuntimeResolutionV1.Rejected(capability?.Prior,Code("runtime-result-position-invalid"));
        if(effect is GraphRuntimeEffectResolutionV1.Refused refused)return new GraphRuntimeResolutionV1.Rejected(capability.Prior,refused.SafeCode);
        if(effect is not GraphRuntimeEffectResolutionV1.Completed completed)return new GraphRuntimeResolutionV1.Rejected(capability.Prior,Code("runtime-effect-result-invalid"));
        if(capability.Command is GraphRuntimeCommandV1.Activate a)return new GraphRuntimeResolutionV1.Applied(new(GraphRuntimePhaseV1.Active,a.GraphGeneration,a.TopologyFingerprint,a.CapacityGrantFact,capability.Authority,a.OperationId,admitted,admitted,null),GraphRuntimeOutcomeV1.Activated,completed.ReceiptHash);
        if(capability.Command is GraphRuntimeCommandV1.Retire r&&capability.Prior is{} p)return new GraphRuntimeResolutionV1.Applied(new(GraphRuntimePhaseV1.Retired,p.GraphGeneration,p.TopologyFingerprint,p.CapacityGrantFact,p.CurrentAuthority,p.ActivationOperationId,p.ActivationFact,admitted,new(r.OperationId,capability.AdmittedCommand)),GraphRuntimeOutcomeV1.Retired,completed.ReceiptHash);
        return new GraphRuntimeResolutionV1.Rejected(capability.Prior,Code("runtime-capability-invalid"));
    }

    private static bool ValidActivation(GraphReplacementJournalFoldResultV1.Current fold,GraphReplacementStateV1 state,GraphRuntimeCommandV1.Activate a,ExpectedAuthorityVectorV1 authority,CapacityGrantSnapshotV1? grant)
    {
        if(state.Phase!=GraphReplacementPhaseV1.None||fold.InstallationFact is not{} installation||installation.Position!=a.GraphAuthorityFact||
            !GraphReplacementCodecsV1.TryDecodeOuter(installation.PayloadMemory,out var outer)||!GraphReplacementCodecsV1.TryDecodeInstalled(outer!.Body,out var body)||
            body!.TopologyFingerprint!=a.TopologyFingerprint||body.Topology.GraphGeneration!=a.GraphGeneration||body.CurrentAuthority!=authority||
            body.ActiveSourceGrantFact!=a.CapacityGrantFact||grant?.CurrentFact!=a.CapacityGrantFact||!GraphReplacementReducerV1.GrantMatches(grant,state.SourcePlan,authority))return false;
        return a.EffectRequestHash==GraphRuntimeEffectHashesV1.Activate(a.ExpectedPredecessor.Session,a.OperationId,a.GraphAuthorityFact,a.TopologyFingerprint,a.GraphGeneration,a.CapacityGrantFact);
    }
    private static bool TryGraph(ExpectedAuthorityVectorV1 authority,out GraphGenerationId generation){var values=authority.Axes.Where(x=>x.AxisId==AuthorityAxisId.Graph).ToArray();if(values.Length==1&&values[0].Value is AuthorityAxisValueV1.Graph graph){generation=graph.Value;return generation.IsValid;}generation=default;return false;}
    private static bool TryGraph(CurrentAuthorityVectorSnapshotV1 authority,out GraphGenerationId generation){var values=authority.Axes.Where(x=>x.AxisId==AuthorityAxisId.Graph).ToArray();if(values.Length==1&&values[0].Value is AuthorityAxisValueV1.Graph graph){generation=graph.Value;return generation.IsValid;}generation=default;return false;}
    private static GraphRuntimeEvaluationV1.Rejected Reject(GraphRuntimeSnapshotV1? s,string c)=>new(s,Code(c));private static BoundedAscii Code(string c)=>new(c);
}
