using HPD.Payments.Runtime.History;

namespace HPD.Payments.Runtime.Tests.History;

internal static class RuntimeHistoryTraceProofs
{
    internal static void Run(List<string> failures)
    {
        void Check(bool value, string message) { if (!value) failures.Add(message); }
        var scenarios = new[]
        {
            Trace("success", "append→claim→invoke→verify", "verified owner postcondition"),
            Trace("conflict", "compare-bind conflict before invoke", "immutable conflict retained"),
            Trace("cancellation", "cancel before send boundary", "definite cancellation; retry policy applies"),
            Trace("timeout", "timeout after possible-send boundary", "Unknown; synchronize before retry"),
            Trace("process-death", "crash after claim before result append", "stale epoch fenced; work rediscoverable"),
            Trace("redelivery", "publication send then crash before acknowledgement", "reconcile exact delivery before redelivery"),
            Trace("stale-claim", "takeover then stale worker result append", "stale result rejected"),
            Trace("poison", "handler compatibility rejection before invoke", "poison terminal; governed replacement required"),
            Trace("partial-repair", "verified branch then crash before residual branch", "case remains discoverable InProgress"),
            Trace("deletion-failure", "disposition request then delete failure boundary", "per-instance Residual retained"),
            Trace("restore", "verified absence then backup restore boundary", "new generation KnownPresent; disposition reopened"),
        };

        var expected = new HashSet<string>(StringComparer.Ordinal)
        {
            "success", "conflict", "cancellation", "timeout", "process-death", "redelivery",
            "stale-claim", "poison", "partial-repair", "deletion-failure", "restore",
        };
        Check(scenarios.Length == 11 && scenarios.Select(x => x.Name).ToHashSet(StringComparer.Ordinal).SetEquals(expected),
            "runtime history scenario coverage is incomplete");
        Check(scenarios.All(x => x.Steps.Count == 14 && x.TerminalAnswers.Count == 8),
            "a runtime history is missing H0-H13 or terminal answers");
        Check(scenarios.All(x => x.Steps[7].Contains("boundary", StringComparison.Ordinal)),
            "an H7 entry lacks an exact boundary/crash statement");
        Check(scenarios.All(x => x.TerminalAnswers[4].Contains("must not", StringComparison.Ordinal)),
            "a history omits its prohibited retry/release answer");
    }

    private static RuntimeHistoryTrace Trace(string name, string boundary, string end) => new(name,
    [
        $"H0 scope: tenant/runtime/{name}; exact owner/provider collision domains",
        $"H1 identity: semantic command/source identity and digest for {name}",
        "H2 time: requested/source/observed/dispatch/record/verify coordinates remain distinct",
        "H3 preconditions: exact owner generation and current projection freshness",
        "H4 admission: dedup/conflict result retained before execution",
        "H5 decision: current authorization, policy, capability, code/config/credential revisions",
        "H6 operation: stable operation, attempt, route, claim epoch and idempotency identity",
        $"H7 boundary: {boundary}",
        "H8 evidence: authenticate before normalize; retain contradiction and typed precedence",
        "H9 economics: no movement or conservation fact inferred from runtime completion",
        "H10 durability: work/publication requirement and exact terminal postcondition named",
        "H11 correction: immutable successor/lineage only; no overwrite",
        "H12 repair: typed command, approval generation and fresh verification",
        $"H13 end: {end}; Unknown/Indeterminate/Residual remains explicit",
    ],
    [
        $"remains true: immutable admitted facts for {name}",
        "unknown/indeterminate: every unverified external or authorization consequence",
        "discoverable: owner history plus work/publication/case/custody evidence",
        "may retry: only exact identity after typed safe-retry decision",
        "must not retry, reroute, release or overwrite while ambiguity/hold/conflict remains",
        "repair owner: typed authority command under current approval and expected generation",
        "closure proof: fresh question-scoped verification receipt",
        "residue: named external/custody consequence with owner and review condition",
    ]);
}
