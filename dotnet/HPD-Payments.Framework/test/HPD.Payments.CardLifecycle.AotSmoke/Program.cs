using HPD.Payments.Primitives.Identity;
using HPD.Payments.Runtime.Card;

ScopeId scope = ScopeId.Create("tenant", "aot", "value-movement");
SemanticId Id(string local) => SemanticId.Create(scope, "card", "operation", local);
CardLifecycleState state = CardLifecycleState.Authorize(Id("lifecycle"), 100m, "usd", OwnerGeneration.Create(1), Id("authorize"))
    .Apply(CardLifecycleChangeKind.Capture, 70m, Id("capture"))
    .Apply(CardLifecycleChangeKind.Void, 30m, Id("void"))
    .Apply(CardLifecycleChangeKind.Refund, 10m, Id("refund"))
    .Apply(CardLifecycleChangeKind.OpenDispute, 20m, Id("dispute"))
    .Apply(CardLifecycleChangeKind.Chargeback, 5m, Id("chargeback"));
if (state.Capturable != 0m || state.UnencumberedCaptured != 40m || state.Disputed != 15m || state.ChargedBack != 5m)
    return 1;
Console.WriteLine("PASS card lifecycle Native AOT conservation graph");
return 0;
