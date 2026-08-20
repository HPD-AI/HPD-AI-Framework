using HPD.Agent.Audio.Runtime.Providers;
using HPD.Agent.Audio.Runtime.Tools;
using HPD.Agent.Authority;

namespace HPD.Agent.Audio.Runtime.Routing;

internal sealed record RouteCutoverEvidenceV1(RoutePreparationStateV1 Preparation,ProviderParticipantSnapshotV1 Provider,JournalPositionV1 ReceiptPosition);
internal sealed record RouteCutoverReceiptV1(OperationId OperationId,CompiledRouteV1 Route,JournalPositionV1 ReceiptPosition);
internal abstract record RouteCutoverResultV1
{
    private RouteCutoverResultV1(){} internal sealed record Committed(RouteCutoverReceiptV1 Receipt):RouteCutoverResultV1;internal sealed record Rejected(BoundedAscii SafeCode):RouteCutoverResultV1;
}
internal static class RouteCutoverCoordinatorV1
{
    internal static RouteCutoverResultV1 Commit(OperationId operationId,RouteCutoverEvidenceV1 evidence)
    {
        if(!operationId.IsValid)throw new ArgumentException("Operation required.");ArgumentNullException.ThrowIfNull(evidence);
        var prep=evidence.Preparation;var provider=evidence.Provider;var plan=provider.Plan;
        if(prep.Snapshot.Phase!=RoutePreparationPhaseV1.CutoverAuthorized||prep.Snapshot.PreparationOwner!=OwnerSliceId.S5)return Reject("route-cutover-not-authorized");
        if(provider.Phase!=ProviderParticipantPhaseV1.Effective||plan is null)return Reject("route-provider-not-effective");
        if(plan.ProviderId!=prep.Route.ProviderId||plan.RouteGeneration!=prep.Route.ProposedGeneration)return Reject("route-provider-evidence-mismatch");
        if(!Same(plan.Authority,prep.Route.Authority)||evidence.ReceiptPosition.Session!=prep.Route.Authority.Session)return Reject("route-authority-mismatch");
        return new RouteCutoverResultV1.Committed(new(operationId,prep.Route,evidence.ReceiptPosition));
    }
    private static bool Same(ExpectedAuthorityVectorV1 x,ExpectedAuthorityVectorV1 y)=>x.Session==y.Session&&x.Axes.SequenceEqual(y.Axes);
    private static RouteCutoverResultV1.Rejected Reject(string code)=>new(new BoundedAscii(code));
}

internal static class ToolCompositeResumeV1
{
    internal static ToolTransactionResultV1 Resume(ToolTransactionStateV1 state,OperationId operationId,RouteCutoverReceiptV1 route,ushort maximumReceipts)
    {
        ArgumentNullException.ThrowIfNull(state);ArgumentNullException.ThrowIfNull(route);if(!operationId.IsValid||maximumReceipts==0)throw new ArgumentException("Resume request invalid.");
        if(state.Receipts.TryGetValue(operationId,out var prior))return prior.Command is ToolTransactionCommandV1.AuthorizeContinuation c&&c.RouteReceipt==route.ReceiptPosition?new ToolTransactionResultV1.Duplicate(state,prior):new ToolTransactionResultV1.Rejected(state,new BoundedAscii("tool-operation-contradiction"));
        if(state.Receipts.Count>=maximumReceipts)return new ToolTransactionResultV1.Rejected(state,new BoundedAscii("tool-receipt-capacity-refused"));
        if(state.Snapshot.Phase!=ToolTransactionPhaseV1.ToolResultProjected)return new ToolTransactionResultV1.Rejected(state,new BoundedAscii("tool-resume-phase-invalid"));
        if(route.Route.Authority.Session!=state.Plan.Authority.Session||route.Route.ProposedGeneration!=state.Plan.Authority.Axes.Select(static x=>x.Value).OfType<AuthorityAxisValueV1.Route>().SingleOrDefault()?.Value)return new ToolTransactionResultV1.Rejected(state,new BoundedAscii("tool-route-authority-mismatch"));
        var command=new ToolTransactionCommandV1.AuthorizeContinuation(operationId,state.Snapshot.Revision,route.ReceiptPosition);var snapshot=state.Snapshot with{Revision=state.Snapshot.Revision+1,Phase=ToolTransactionPhaseV1.ToolContinuationAuthorized};var receipt=new ToolTransactionReceiptV1(command,snapshot);var receipts=state.Receipts.ToDictionary(static x=>x.Key,static x=>x.Value);receipts.Add(operationId,receipt);return new ToolTransactionResultV1.Applied(new(state.Plan,snapshot,receipts),receipt);
    }
}
