using System.Collections.ObjectModel;
using HPD.Agent.Authority;

namespace HPD.Agent.Audio.Runtime.Providers;

internal sealed record ProviderParticipantPlan
{
    internal ProviderParticipantPlan(ParticipantId participantId, ProviderId providerId,
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

internal enum ProviderParticipantPhase : ushort
{
    None = 1, Prepared = 2, Effective = 3, Draining = 4, Stopped = 5, Quarantined = 6,
}

internal abstract record ProviderParticipantCommand
{
    private protected ProviderParticipantCommand(OperationId operationId, ulong expectedRevision)
    { if (!operationId.IsValid) throw new ArgumentException("An operation is required."); OperationId=operationId; ExpectedRevision=expectedRevision; }
    internal OperationId OperationId { get; }
    internal ulong ExpectedRevision { get; }
    internal sealed record Prepare : ProviderParticipantCommand
    { internal Prepare(OperationId o,ulong r,ProviderParticipantPlan p):base(o,r)=>Plan=p??throw new ArgumentNullException(nameof(p)); internal ProviderParticipantPlan Plan{get;} }
    internal sealed record Activate(OperationId O,ulong R):ProviderParticipantCommand(O,R);
    internal sealed record BeginEffect : ProviderParticipantCommand
    { internal BeginEffect(OperationId o,ulong r,OperationId effect):base(o,r){if(!effect.IsValid)throw new ArgumentException("Effect operation required.");EffectOperation=effect;} internal OperationId EffectOperation{get;} }
    internal sealed record SettleEffect : ProviderParticipantCommand
    { internal SettleEffect(OperationId o,ulong r,OperationId effect):base(o,r){if(!effect.IsValid)throw new ArgumentException("Effect operation required.");EffectOperation=effect;} internal OperationId EffectOperation{get;} }
    internal sealed record Drain(OperationId O,ulong R):ProviderParticipantCommand(O,R);
    internal sealed record Stop(OperationId O,ulong R):ProviderParticipantCommand(O,R);
    internal sealed record Quarantine(OperationId O,ulong R,BoundedAscii SafeCode):ProviderParticipantCommand(O,R);
}

internal sealed record ProviderParticipantSnapshot(ulong Revision, ProviderParticipantPhase Phase,
    ProviderParticipantPlan? Plan, ushort Inflight, BoundedAscii? SafeCode);
internal sealed record ProviderParticipantReceipt(ProviderParticipantCommand Command,ProviderParticipantSnapshot Snapshot);

internal sealed class ProviderParticipantState
{
    private readonly ReadOnlyDictionary<OperationId,ProviderParticipantReceipt> _receipts;
    private readonly ReadOnlyDictionary<OperationId,byte> _effects;
    internal ProviderParticipantState(ProviderParticipantSnapshot snapshot,
        IDictionary<OperationId,ProviderParticipantReceipt>? receipts=null,IDictionary<OperationId,byte>? effects=null)
    { Snapshot=snapshot??throw new ArgumentNullException(nameof(snapshot));_receipts=new(receipts is null?new Dictionary<OperationId,ProviderParticipantReceipt>():new Dictionary<OperationId,ProviderParticipantReceipt>(receipts));_effects=new(effects is null?new Dictionary<OperationId,byte>():new Dictionary<OperationId,byte>(effects)); }
    internal ProviderParticipantSnapshot Snapshot{get;}
    internal IReadOnlyDictionary<OperationId,ProviderParticipantReceipt> Receipts=>_receipts;
    internal IReadOnlyDictionary<OperationId,byte> Effects=>_effects;
}

internal abstract record ProviderParticipantResult
{
    private ProviderParticipantResult(){}
    internal sealed record Applied(ProviderParticipantState State,ProviderParticipantReceipt Receipt):ProviderParticipantResult;
    internal sealed record Duplicate(ProviderParticipantState State,ProviderParticipantReceipt Receipt):ProviderParticipantResult;
    internal sealed record Rejected(ProviderParticipantState State,BoundedAscii SafeCode):ProviderParticipantResult;
}

internal static class ProviderParticipantSupervisor
{
    internal static ProviderParticipantState Create()=>new(new(0,ProviderParticipantPhase.None,null,0,null));

    internal static ProviderParticipantResult Apply(ProviderParticipantState state,ProviderParticipantCommand command,ushort maximumReceipts)
    {
        ArgumentNullException.ThrowIfNull(state);ArgumentNullException.ThrowIfNull(command);
        if(maximumReceipts==0)throw new ArgumentOutOfRangeException(nameof(maximumReceipts));
        if(state.Receipts.TryGetValue(command.OperationId,out var prior))return prior.Command==command
            ?new ProviderParticipantResult.Duplicate(state,prior):Reject(state,"provider-operation-contradiction");
        if(state.Receipts.Count>=maximumReceipts)return Reject(state,"provider-receipt-capacity-refused");
        if(command.ExpectedRevision!=state.Snapshot.Revision)return Reject(state,"provider-revision-conflict");
        var snapshot=state.Snapshot;var effects=state.Effects.ToDictionary(static x=>x.Key,static x=>x.Value);
        ProviderParticipantSnapshot? next=command switch
        {
            ProviderParticipantCommand.Prepare prepare when snapshot.Phase==ProviderParticipantPhase.None && Applicable(prepare.Plan)=>
                new(snapshot.Revision+1,ProviderParticipantPhase.Prepared,prepare.Plan,0,null),
            ProviderParticipantCommand.Activate when snapshot.Phase==ProviderParticipantPhase.Prepared=>snapshot with{Revision=snapshot.Revision+1,Phase=ProviderParticipantPhase.Effective},
            ProviderParticipantCommand.BeginEffect begin when snapshot.Phase==ProviderParticipantPhase.Effective && !effects.ContainsKey(begin.EffectOperation) && snapshot.Inflight<snapshot.Plan!.MaximumInflight=>
                Begin(snapshot,effects,begin.EffectOperation),
            ProviderParticipantCommand.SettleEffect settle when snapshot.Phase is ProviderParticipantPhase.Effective or ProviderParticipantPhase.Draining && effects.TryGetValue(settle.EffectOperation,out var status)&&status==1=>
                Settle(snapshot,effects,settle.EffectOperation),
            ProviderParticipantCommand.Drain when snapshot.Phase==ProviderParticipantPhase.Effective=>snapshot with{Revision=snapshot.Revision+1,Phase=ProviderParticipantPhase.Draining},
            ProviderParticipantCommand.Stop when snapshot.Phase==ProviderParticipantPhase.Draining&&snapshot.Inflight==0=>snapshot with{Revision=snapshot.Revision+1,Phase=ProviderParticipantPhase.Stopped,Plan=null},
            ProviderParticipantCommand.Quarantine quarantine when quarantine.SafeCode.IsValid&&snapshot.Phase!=ProviderParticipantPhase.Stopped=>snapshot with{Revision=snapshot.Revision+1,Phase=ProviderParticipantPhase.Quarantined,SafeCode=quarantine.SafeCode},
            _=>null,
        };
        if(next is null)return Reject(state,command is ProviderParticipantCommand.BeginEffect&&snapshot.Plan is not null&&snapshot.Inflight>=snapshot.Plan.MaximumInflight
            ?"provider-inflight-capacity-refused":"provider-transition-invalid");
        var receipt=new ProviderParticipantReceipt(command,next);var receipts=state.Receipts.ToDictionary(static x=>x.Key,static x=>x.Value);receipts.Add(command.OperationId,receipt);
        return new ProviderParticipantResult.Applied(new ProviderParticipantState(next,receipts,effects),receipt);
    }

    private static bool Applicable(ProviderParticipantPlan plan)=>
        plan.Authority.Axes.Select(static axis=>axis.Value).OfType<AuthorityAxisValueV1.Provider>().SingleOrDefault()?.Value==plan.Generation&&
        plan.Authority.Axes.Select(static axis=>axis.Value).OfType<AuthorityAxisValueV1.Route>().SingleOrDefault()?.Value==plan.RouteGeneration;
    private static ProviderParticipantSnapshot Begin(ProviderParticipantSnapshot snapshot,Dictionary<OperationId,byte> effects,OperationId operation)
    {effects.Add(operation,1);return snapshot with{Revision=snapshot.Revision+1,Inflight=checked((ushort)(snapshot.Inflight+1))};}
    private static ProviderParticipantSnapshot Settle(ProviderParticipantSnapshot snapshot,Dictionary<OperationId,byte> effects,OperationId operation)
    {effects[operation]=2;return snapshot with{Revision=snapshot.Revision+1,Inflight=checked((ushort)(snapshot.Inflight-1))};}
    private static ProviderParticipantResult.Rejected Reject(ProviderParticipantState state,string code)=>new(state,new BoundedAscii(code));
}
