using HPD.Payments.Primitives.Identity;
using HPD.Payments.Primitives.Time;

namespace HPD.Payments.Runtime.Authorization;

/// <summary>Names the closed result of evaluating authorization for the action being attempted now.</summary>
public enum AuthorizationDisposition
{
    /// <summary>Invalid default.</summary>
    None = 0,
    /// <summary>The exact action is authorized under the named current revision.</summary>
    Authorized,
    /// <summary>The exact action is denied under the named current revision.</summary>
    Denied,
    /// <summary>The policy result cannot currently be established and admission must fail closed.</summary>
    Indeterminate,
}

/// <summary>Provides the explicit, immutable inputs needed to authorize one current action.</summary>
/// <typeparam name="TAction">The authority-owned action type.</typeparam>
public sealed record CurrentActionRequest<TAction> where TAction : notnull
{
    /// <summary>Gets the explicit tenant-scoped principal.</summary>
    public SemanticId PrincipalId { get; }
    /// <summary>Gets the exact subject affected by the action.</summary>
    public SemanticId SubjectId { get; }
    /// <summary>Gets the current authorization policy revision.</summary>
    public Revision PolicyRevision { get; }
    /// <summary>Gets the action-time instant supplied by the composition root.</summary>
    public NamedTime ActionTime { get; }
    /// <summary>Gets the authority-owned action without interpreting its semantics.</summary>
    public TAction Action { get; }

    /// <summary>Creates an explicit current-action request; construction does not authorize it.</summary>
    /// <exception cref="ArgumentException">An identity, scope, revision, time, or action is invalid.</exception>
    public CurrentActionRequest(SemanticId principalId, SemanticId subjectId, Revision policyRevision, NamedTime actionTime, TAction action)
    {
        ArgumentNullException.ThrowIfNull(action);
        if (!principalId.IsValid || !subjectId.IsValid || principalId.Scope != subjectId.Scope || !policyRevision.IsValid ||
            !actionTime.IsValid || actionTime.Kind is not (TimeKind.Requested or TimeKind.Verify))
            throw new ArgumentException("Current-action authorization requires explicit, valid, same-scope inputs.");
        PrincipalId = principalId; SubjectId = subjectId; PolicyRevision = policyRevision; ActionTime = actionTime; Action = action;
    }
}

/// <summary>Reports a bounded authorization decision for one exact current-action request.</summary>
public sealed record AuthorizationDecision
{
    /// <summary>Gets the closed decision.</summary>
    public AuthorizationDisposition Disposition { get; }
    /// <summary>Gets the exact policy revision that produced the decision.</summary>
    public Revision PolicyRevision { get; }
    /// <summary>Gets a bounded diagnostic code that is not mutation truth.</summary>
    public string Code { get; }

    /// <summary>Creates a decision whose revision must match the request before admission may proceed.</summary>
    /// <exception cref="ArgumentException">The disposition, revision, or code is invalid.</exception>
    public AuthorizationDecision(AuthorizationDisposition disposition, Revision policyRevision, string code)
    {
        if (disposition == AuthorizationDisposition.None || !Enum.IsDefined(disposition) || !policyRevision.IsValid ||
            !ScopeId.TryCreate("token", "code", code, out _))
            throw new ArgumentException("Authorization decision components are invalid.");
        Disposition = disposition; PolicyRevision = policyRevision; Code = code;
    }
}

/// <summary>Evaluates an exact action using only explicit current-action inputs.</summary>
/// <typeparam name="TAction">The authority-owned action type.</typeparam>
public interface ICurrentActionAuthorizer<TAction> where TAction : notnull
{
    /// <summary>Evaluates the request without consulting ambient principal, tenant, policy, clock, or configuration state.</summary>
    /// <param name="request">The explicit current-action request.</param>
    /// <param name="cancellationToken">Cooperative cancellation token.</param>
    /// <returns>A closed decision at an exact policy revision.</returns>
    ValueTask<AuthorizationDecision> AuthorizeAsync(CurrentActionRequest<TAction> request, CancellationToken cancellationToken = default);
}
