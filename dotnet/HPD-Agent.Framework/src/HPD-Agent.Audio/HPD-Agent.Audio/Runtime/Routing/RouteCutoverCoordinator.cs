using HPD.Agent.Audio.Runtime.Providers;
using HPD.Agent.Audio.Runtime.Tools;
using HPD.Agent.Authority;

namespace HPD.Agent.Audio.Runtime.Routing;

internal sealed record AdmittedRouteAuthority(CompiledRoute Route,ExpectedAuthorityVectorV1 Authority,JournalPositionV1 AdmissionPosition);
internal static class RouteAuthorityAdmission
{
    internal static AdmittedRouteAuthority Admit(CompiledRoute route,JournalPositionV1 admissionPosition)
    {
        ArgumentNullException.ThrowIfNull(route);if(!admissionPosition.IsValid||admissionPosition.Session!=route.Authority.Session)throw new ArgumentException("Route admission position is invalid.");
        var axes=route.Authority.Axes.Select(static x=>x.Value).Where(static x=>x is not AuthorityAxisValueV1.Route).Append<AuthorityAxisValueV1>(new AuthorityAxisValueV1.Route(route.ProposedGeneration)).ToArray();
        return new(route,ExpectedAuthorityVectorV1.Create(route.Authority.Session,axes),admissionPosition);
    }
}
internal sealed record RouteCutoverEvidence(RoutePreparationState Preparation,ProviderParticipantSnapshot Provider,AdmittedRouteAuthority Admission);
internal sealed record RouteCutoverReceipt(OperationId OperationId,CompiledRoute Route,JournalPositionV1 ReceiptPosition);
internal abstract record RouteCutoverResult
{
    private RouteCutoverResult(){} internal sealed record Committed(RouteCutoverReceipt Receipt):RouteCutoverResult;internal sealed record Rejected(BoundedAscii SafeCode):RouteCutoverResult;
}
internal static class RouteCutoverCoordinator
{
    internal static RouteCutoverResult Commit(OperationId operationId,RouteCutoverEvidence evidence)
    {
        if(!operationId.IsValid)throw new ArgumentException("Operation required.");ArgumentNullException.ThrowIfNull(evidence);
        var prep=evidence.Preparation;var provider=evidence.Provider;var plan=provider.Plan;
        if(prep.Snapshot.Phase!=RoutePreparationPhase.CutoverAuthorized||prep.Snapshot.PreparationOwner!=OwnerSliceId.S5)return Reject("route-cutover-not-authorized");
        if(provider.Phase!=ProviderParticipantPhase.Effective||plan is null)return Reject("route-provider-not-effective");
        if(evidence.Admission.Route!=prep.Route)return Reject("route-admission-mismatch");
        if(plan.ProviderId!=prep.Route.ProviderId||plan.RouteGeneration!=prep.Route.ProposedGeneration)return Reject("route-provider-evidence-mismatch");
        if(!Same(plan.Authority,evidence.Admission.Authority)||evidence.Admission.AdmissionPosition.Session!=prep.Route.Authority.Session)return Reject("route-authority-mismatch");
        return new RouteCutoverResult.Committed(new(operationId,prep.Route,evidence.Admission.AdmissionPosition));
    }
    private static bool Same(ExpectedAuthorityVectorV1 x,ExpectedAuthorityVectorV1 y)=>x.Session==y.Session&&x.Axes.SequenceEqual(y.Axes);
    private static RouteCutoverResult.Rejected Reject(string code)=>new(new BoundedAscii(code));
}

internal static class ToolCompositeResume
{
    internal static ToolTransactionResult Resume(ToolTransactionState state,OperationId operationId,RouteCutoverReceipt route,ushort maximumReceipts)
    {
        ArgumentNullException.ThrowIfNull(state);ArgumentNullException.ThrowIfNull(route);if(!operationId.IsValid||maximumReceipts==0)throw new ArgumentException("Resume request invalid.");
        if(state.Receipts.TryGetValue(operationId,out var prior))return prior.Command is ToolTransactionCommand.AuthorizeContinuation c&&c.RouteReceipt==route.ReceiptPosition?new ToolTransactionResult.Duplicate(state,prior):new ToolTransactionResult.Rejected(state,new BoundedAscii("tool-operation-contradiction"));
        if(state.Receipts.Count>=maximumReceipts)return new ToolTransactionResult.Rejected(state,new BoundedAscii("tool-receipt-capacity-refused"));
        if(state.Snapshot.Phase!=ToolTransactionPhase.ToolResultProjected)return new ToolTransactionResult.Rejected(state,new BoundedAscii("tool-resume-phase-invalid"));
        if(route.Route.Authority.Session!=state.Plan.Authority.Session||route.Route.ProposedGeneration!=state.Plan.Authority.Axes.Select(static x=>x.Value).OfType<AuthorityAxisValueV1.Route>().SingleOrDefault()?.Value)return new ToolTransactionResult.Rejected(state,new BoundedAscii("tool-route-authority-mismatch"));
        var command=new ToolTransactionCommand.AuthorizeContinuation(operationId,state.Snapshot.Revision,route.ReceiptPosition);var snapshot=state.Snapshot with{Revision=state.Snapshot.Revision+1,Phase=ToolTransactionPhase.ToolContinuationAuthorized};var receipt=new ToolTransactionReceipt(command,snapshot);var receipts=state.Receipts.ToDictionary(static x=>x.Key,static x=>x.Value);receipts.Add(operationId,receipt);return new ToolTransactionResult.Applied(new(state.Plan,snapshot,receipts),receipt);
    }
}
