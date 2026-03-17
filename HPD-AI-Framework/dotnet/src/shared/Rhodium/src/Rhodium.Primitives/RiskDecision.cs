namespace Rhodium.Primitives;

/// <summary>
/// Discriminated union for risk approval/rejection with audit trail.
/// Captures the outcome of risk evaluation explicitly.
/// </summary>
public abstract record RiskDecision<T>
{
    private RiskDecision() { } // Sealed hierarchy

    public sealed record Approved(T Value) : RiskDecision<T>;

    public sealed record Refused(T Value, string Reason, string? RuleId = null) : RiskDecision<T>;

    public bool IsApproved => this is Approved;
    public bool IsRefused => this is Refused;

    public TResult Match<TResult>(
        Func<T, TResult> onApproved,
        Func<T, string, TResult> onRefused) => this switch
    {
        Approved a => onApproved(a.Value),
        Refused r => onRefused(r.Value, r.Reason),
        _ => throw new InvalidOperationException()
    };
}

/// <summary>
/// Extension methods for working with risk decisions.
/// </summary>
public static class RiskDecisionExtensions
{
    public static IEnumerable<T> WhereApproved<T>(this IEnumerable<RiskDecision<T>> decisions) =>
        decisions.OfType<RiskDecision<T>.Approved>().Select(a => a.Value);

    public static IEnumerable<(T Value, string Reason)> WhereRefused<T>(this IEnumerable<RiskDecision<T>> decisions) =>
        decisions.OfType<RiskDecision<T>.Refused>().Select(r => (r.Value, r.Reason));
}
