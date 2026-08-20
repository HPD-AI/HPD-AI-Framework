using System.Collections.ObjectModel;
using HPD.Agent.Authority;

namespace HPD.Agent.Audio.Runtime.Providers;

internal sealed record ProviderParticipantPlanV1
{
    internal ProviderParticipantPlanV1(ParticipantId participantId, ProviderId providerId,
        ProviderGenerationId generation, RouteGenerationId routeGeneration,
        ExpectedAuthorityVectorV1 authority, Hash256 catalogFingerprint, ushort maximumInflight)
    {
        if (!participantId.IsValid || !providerId.IsValid || !generation.IsValid || !routeGeneration.IsValid ||
            authority is null || catalogFingerprint == default || maximumInflight == 0)
            throw new ArgumentException("A provider plan requires complete generation-fenced authority.");
        ParticipantId = participantId; ProviderId = providerId; Generation = generation;
        RouteGeneration = routeGeneration; Authority = authority; CatalogFingerprint = catalogFingerprint;
        MaximumInflight = maximumInflight;
    }
    internal ParticipantId ParticipantId { get; }
    internal ProviderId ProviderId { get; }
    internal ProviderGenerationId Generation { get; }
    internal RouteGenerationId RouteGeneration { get; }
    internal ExpectedAuthorityVectorV1 Authority { get; }
    internal Hash256 CatalogFingerprint { get; }
    internal ushort MaximumInflight { get; }
}

internal enum ProviderParticipantPhaseV1 : ushort
{
    None = 1, Prepared = 2, Effective = 3, Draining = 4, Stopped = 5, Quarantined = 6,
}

internal abstract record ProviderParticipantCommandV1
{
    private protected ProviderParticipantCommandV1(OperationId operationId, ulong expectedRevision)
    { if (!operationId.IsValid) throw new ArgumentException("An operation is required."); OperationId=operationId; ExpectedRevision=expectedRevision; }
    internal OperationId OperationId { get; }
    internal ulong ExpectedRevision { get; }
    internal sealed record Prepare : ProviderParticipantCommandV1
    { internal Prepare(OperationId o,ulong r,ProviderParticipantPlanV1 p):base(o,r)=>Plan=p??throw new ArgumentNullException(nameof(p)); internal ProviderParticipantPlanV1 Plan{get;} }
    internal sealed record Activate(OperationId O,ulong R):ProviderParticipantCommandV1(O,R);
    internal sealed record BeginEffect : ProviderParticipantCommandV1
    { internal BeginEffect(OperationId o,ulong r,OperationId effect):base(o,r){if(!effect.IsValid)throw new ArgumentException("Effect operation required.");EffectOperation=effect;} internal OperationId EffectOperation{get;} }
    internal sealed record SettleEffect : ProviderParticipantCommandV1
    { internal SettleEffect(OperationId o,ulong r,OperationId effect):base(o,r){if(!effect.IsValid)throw new ArgumentException("Effect operation required.");EffectOperation=effect;} internal OperationId EffectOperation{get;} }
    internal sealed record Drain(OperationId O,ulong R):ProviderParticipantCommandV1(O,R);
    internal sealed record Stop(OperationId O,ulong R):ProviderParticipantCommandV1(O,R);
    internal sealed record Quarantine(OperationId O,ulong R,BoundedAscii SafeCode):ProviderParticipantCommandV1(O,R);
}

internal sealed record ProviderParticipantSnapshotV1(ulong Revision, ProviderParticipantPhaseV1 Phase,
    ProviderParticipantPlanV1? Plan, ushort Inflight, BoundedAscii? SafeCode);
internal sealed record ProviderParticipantReceiptV1(ProviderParticipantCommandV1 Command,ProviderParticipantSnapshotV1 Snapshot);

internal sealed class ProviderParticipantStateV1
{
    private readonly ReadOnlyDictionary<OperationId,ProviderParticipantReceiptV1> _receipts;
    private readonly ReadOnlyDictionary<OperationId,byte> _effects;
    internal ProviderParticipantStateV1(ProviderParticipantSnapshotV1 snapshot,
        IDictionary<OperationId,ProviderParticipantReceiptV1>? receipts=null,IDictionary<OperationId,byte>? effects=null)
    { Snapshot=snapshot??throw new ArgumentNullException(nameof(snapshot));_receipts=new(receipts is null?new Dictionary<OperationId,ProviderParticipantReceiptV1>():new Dictionary<OperationId,ProviderParticipantReceiptV1>(receipts));_effects=new(effects is null?new Dictionary<OperationId,byte>():new Dictionary<OperationId,byte>(effects)); }
    internal ProviderParticipantSnapshotV1 Snapshot{get;}
    internal IReadOnlyDictionary<OperationId,ProviderParticipantReceiptV1> Receipts=>_receipts;
    internal IReadOnlyDictionary<OperationId,byte> Effects=>_effects;
}

internal abstract record ProviderParticipantResultV1
{
    private ProviderParticipantResultV1(){}
    internal sealed record Applied(ProviderParticipantStateV1 State,ProviderParticipantReceiptV1 Receipt):ProviderParticipantResultV1;
    internal sealed record Duplicate(ProviderParticipantStateV1 State,ProviderParticipantReceiptV1 Receipt):ProviderParticipantResultV1;
    internal sealed record Rejected(ProviderParticipantStateV1 State,BoundedAscii SafeCode):ProviderParticipantResultV1;
}

internal static class ProviderParticipantSupervisorV1
{
    internal static ProviderParticipantStateV1 Create()=>new(new(0,ProviderParticipantPhaseV1.None,null,0,null));

    internal static ProviderParticipantResultV1 Apply(ProviderParticipantStateV1 state,ProviderParticipantCommandV1 command,ushort maximumReceipts)
    {
        ArgumentNullException.ThrowIfNull(state);ArgumentNullException.ThrowIfNull(command);
        if(maximumReceipts==0)throw new ArgumentOutOfRangeException(nameof(maximumReceipts));
        if(state.Receipts.TryGetValue(command.OperationId,out var prior))return prior.Command==command
            ?new ProviderParticipantResultV1.Duplicate(state,prior):Reject(state,"provider-operation-contradiction");
        if(state.Receipts.Count>=maximumReceipts)return Reject(state,"provider-receipt-capacity-refused");
        if(command.ExpectedRevision!=state.Snapshot.Revision)return Reject(state,"provider-revision-conflict");
        var snapshot=state.Snapshot;var effects=state.Effects.ToDictionary(static x=>x.Key,static x=>x.Value);
        ProviderParticipantSnapshotV1? next=command switch
        {
            ProviderParticipantCommandV1.Prepare prepare when snapshot.Phase==ProviderParticipantPhaseV1.None && Applicable(prepare.Plan)=>
                new(snapshot.Revision+1,ProviderParticipantPhaseV1.Prepared,prepare.Plan,0,null),
            ProviderParticipantCommandV1.Activate when snapshot.Phase==ProviderParticipantPhaseV1.Prepared=>snapshot with{Revision=snapshot.Revision+1,Phase=ProviderParticipantPhaseV1.Effective},
            ProviderParticipantCommandV1.BeginEffect begin when snapshot.Phase==ProviderParticipantPhaseV1.Effective && !effects.ContainsKey(begin.EffectOperation) && snapshot.Inflight<snapshot.Plan!.MaximumInflight=>
                Begin(snapshot,effects,begin.EffectOperation),
            ProviderParticipantCommandV1.SettleEffect settle when snapshot.Phase is ProviderParticipantPhaseV1.Effective or ProviderParticipantPhaseV1.Draining && effects.TryGetValue(settle.EffectOperation,out var status)&&status==1=>
                Settle(snapshot,effects,settle.EffectOperation),
            ProviderParticipantCommandV1.Drain when snapshot.Phase==ProviderParticipantPhaseV1.Effective=>snapshot with{Revision=snapshot.Revision+1,Phase=ProviderParticipantPhaseV1.Draining},
            ProviderParticipantCommandV1.Stop when snapshot.Phase==ProviderParticipantPhaseV1.Draining&&snapshot.Inflight==0=>snapshot with{Revision=snapshot.Revision+1,Phase=ProviderParticipantPhaseV1.Stopped,Plan=null},
            ProviderParticipantCommandV1.Quarantine quarantine when quarantine.SafeCode.IsValid&&snapshot.Phase!=ProviderParticipantPhaseV1.Stopped=>snapshot with{Revision=snapshot.Revision+1,Phase=ProviderParticipantPhaseV1.Quarantined,SafeCode=quarantine.SafeCode},
            _=>null,
        };
        if(next is null)return Reject(state,command is ProviderParticipantCommandV1.BeginEffect&&snapshot.Plan is not null&&snapshot.Inflight>=snapshot.Plan.MaximumInflight
            ?"provider-inflight-capacity-refused":"provider-transition-invalid");
        var receipt=new ProviderParticipantReceiptV1(command,next);var receipts=state.Receipts.ToDictionary(static x=>x.Key,static x=>x.Value);receipts.Add(command.OperationId,receipt);
        return new ProviderParticipantResultV1.Applied(new ProviderParticipantStateV1(next,receipts,effects),receipt);
    }

    private static bool Applicable(ProviderParticipantPlanV1 plan)=>
        plan.Authority.Axes.Select(static axis=>axis.Value).OfType<AuthorityAxisValueV1.Provider>().SingleOrDefault()?.Value==plan.Generation&&
        plan.Authority.Axes.Select(static axis=>axis.Value).OfType<AuthorityAxisValueV1.Route>().SingleOrDefault()?.Value==plan.RouteGeneration;
    private static ProviderParticipantSnapshotV1 Begin(ProviderParticipantSnapshotV1 snapshot,Dictionary<OperationId,byte> effects,OperationId operation)
    {effects.Add(operation,1);return snapshot with{Revision=snapshot.Revision+1,Inflight=checked((ushort)(snapshot.Inflight+1))};}
    private static ProviderParticipantSnapshotV1 Settle(ProviderParticipantSnapshotV1 snapshot,Dictionary<OperationId,byte> effects,OperationId operation)
    {effects[operation]=2;return snapshot with{Revision=snapshot.Revision+1,Inflight=checked((ushort)(snapshot.Inflight-1))};}
    private static ProviderParticipantResultV1.Rejected Reject(ProviderParticipantStateV1 state,string code)=>new(state,new BoundedAscii(code));
}
