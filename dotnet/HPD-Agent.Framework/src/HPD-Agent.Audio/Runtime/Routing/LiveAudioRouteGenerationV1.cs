using HPD.Agent.Authority;

namespace HPD.Agent.Audio.Runtime.Routing;

internal abstract record LiveAudioRouteActivationResultV1
{
    private LiveAudioRouteActivationResultV1(){}
    internal sealed record Activated(RouteCutoverReceiptV1 Receipt):LiveAudioRouteActivationResultV1;
    internal sealed record Duplicate(RouteCutoverReceiptV1 Receipt):LiveAudioRouteActivationResultV1;
    internal sealed record Rejected(BoundedAscii SafeCode):LiveAudioRouteActivationResultV1;
}

internal sealed class LiveAudioRouteGenerationV1
{
    private readonly object _gate=new();private readonly ushort _maximumReceipts;
    private readonly Dictionary<OperationId,(RouteCutoverEvidenceV1 Evidence,RouteCutoverReceiptV1 Receipt)> _receipts=[];
    internal LiveAudioRouteGenerationV1(ExpectedAuthorityVectorV1 authority,ushort maximumReceipts=64)
    {Authority=authority??throw new ArgumentNullException(nameof(authority));if(maximumReceipts==0)throw new ArgumentOutOfRangeException(nameof(maximumReceipts));var routes=authority.Axes.Select(static x=>x.Value).OfType<AuthorityAxisValueV1.Route>().ToArray();var providers=authority.Axes.Select(static x=>x.Value).OfType<AuthorityAxisValueV1.Provider>().ToArray();if(routes.Length!=1||providers.Length!=1)throw new ArgumentException("S8 activation requires exact Route and Provider axes.",nameof(authority));RouteGeneration=routes[0].Value;ProviderGeneration=providers[0].Value;_maximumReceipts=maximumReceipts;}
    internal ExpectedAuthorityVectorV1 Authority{get;}internal RouteGenerationId RouteGeneration{get;}internal ProviderGenerationId ProviderGeneration{get;}
    internal static LiveAudioRouteGenerationV1? TryCreate(ExpectedAuthorityVectorV1 authority)
    {ArgumentNullException.ThrowIfNull(authority);var hasRoute=authority.Axes.Any(static x=>x.Value is AuthorityAxisValueV1.Route);var hasProvider=authority.Axes.Any(static x=>x.Value is AuthorityAxisValueV1.Provider);return !hasRoute&&!hasProvider?null:new(authority);}
    internal LiveAudioRouteActivationResultV1 Activate(OperationId operationId,RouteCutoverEvidenceV1 evidence)
    {if(!operationId.IsValid)throw new ArgumentException("Operation required.",nameof(operationId));ArgumentNullException.ThrowIfNull(evidence);lock(_gate){if(_receipts.TryGetValue(operationId,out var prior))return prior.Evidence==evidence?new LiveAudioRouteActivationResultV1.Duplicate(prior.Receipt):Reject("route-operation-contradiction");if(_receipts.Count>=_maximumReceipts)return Reject("route-receipt-capacity-refused");if(!Same(Authority,evidence.Admission.Authority)||evidence.Admission.Route.ProposedGeneration!=RouteGeneration||evidence.Provider.Plan?.Generation!=ProviderGeneration)return Reject("route-generation-stale");var result=RouteCutoverCoordinatorV1.Commit(operationId,evidence);if(result is RouteCutoverResultV1.Rejected rejected)return new LiveAudioRouteActivationResultV1.Rejected(rejected.SafeCode);var receipt=((RouteCutoverResultV1.Committed)result).Receipt;_receipts.Add(operationId,(evidence,receipt));return new LiveAudioRouteActivationResultV1.Activated(receipt);}}
    private static bool Same(ExpectedAuthorityVectorV1 x,ExpectedAuthorityVectorV1 y)=>x.Session==y.Session&&x.Axes.SequenceEqual(y.Axes);
    private static LiveAudioRouteActivationResultV1.Rejected Reject(string code)=>new(new BoundedAscii(code));
}
