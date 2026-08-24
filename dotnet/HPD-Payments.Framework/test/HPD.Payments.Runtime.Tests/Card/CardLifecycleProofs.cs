using HPD.Payments.Primitives.Identity;
using HPD.Payments.Runtime.Card;

namespace HPD.Payments.Runtime.Tests.Card;

internal static class CardLifecycleProofs
{
    internal static void Run(List<string> failures)
    {
        void Check(bool condition, string message) { if (!condition) failures.Add(message); }
        ScopeId scope = ScopeId.Create("tenant", "runtime", "value-movement");
        SemanticId Id(string local) => SemanticId.Create(scope, "card", "operation", local);
        CardLifecycleState state = CardLifecycleState.Authorize(Id("lifecycle"), 100m, "usd", OwnerGeneration.Create(1), Id("authorize"))
            .Apply(CardLifecycleChangeKind.Capture, 70m, Id("capture-one"))
            .Apply(CardLifecycleChangeKind.Void, 30m, Id("void-remainder"))
            .Apply(CardLifecycleChangeKind.Refund, 10m, Id("refund-one"))
            .Apply(CardLifecycleChangeKind.OpenDispute, 20m, Id("dispute-one"))
            .Apply(CardLifecycleChangeKind.Chargeback, 5m, Id("chargeback-one"))
            .Apply(CardLifecycleChangeKind.ResolveDispute, 15m, Id("resolve-rest"));
        Check(state.Capturable == 0m && state.UnencumberedCaptured == 55m && state.Refunded == 10m && state.ChargedBack == 5m && state.Disputed == 0m,
            "card lifecycle did not conserve authorization/capture/refund/dispute/chargeback capacity");
        Throws<InvalidOperationException>(() => state.Apply(CardLifecycleChangeKind.Capture, 1m, Id("overcapture")), failures, "card lifecycle overcaptured");
        Throws<InvalidOperationException>(() => state.Apply(CardLifecycleChangeKind.Refund, 56m, Id("overrefund")), failures, "card lifecycle overrefunded");
        Throws<InvalidOperationException>(() => state.Apply(CardLifecycleChangeKind.Chargeback, 1m, Id("undisputed-chargeback")), failures, "chargeback consumed undisputed capacity");
    }

    private static void Throws<T>(Action action, List<string> failures, string message) where T : Exception
    { try { action(); } catch (T) { return; } failures.Add(message); }
}
