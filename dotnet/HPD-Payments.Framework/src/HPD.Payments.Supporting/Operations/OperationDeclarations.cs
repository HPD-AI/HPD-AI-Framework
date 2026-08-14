using HPD.Payments.Primitives.Identity;
using HPD.Payments.Primitives.Time;
using HPD.Payments.Supporting.Evidence;
using HPD.Payments.Supporting.Ownership;

namespace HPD.Payments.Supporting.Operations;

/// <summary>Names the observation state of one operation attempt without flattening external uncertainty.</summary>
public enum AttemptOutcome
{
    /// <summary>Invalid default.</summary>
    None = 0,
    /// <summary>The attempt was locally admitted but no external occurrence is yet established.</summary>
    Admitted,
    /// <summary>The named postcondition was freshly verified.</summary>
    Verified,
    /// <summary>A definite failure was established.</summary>
    DefiniteFailure,
    /// <summary>The external effect may have occurred.</summary>
    UnknownExternalEffect,
    /// <summary>Authorization or required evidence was indeterminate.</summary>
    Indeterminate,
    /// <summary>The attempt conflicts with an immutable prior declaration.</summary>
    Conflict,
}

/// <summary>Declares one stable operation routed to its frozen authority owner.</summary>
/// <remarks>An operation coordinates an attempt; it does not mutate the routed authority.</remarks>
public sealed record OperationDeclaration
{
    /// <summary>Gets the stable operation identity reused across retries.</summary>
    public SemanticId OperationId { get; }
    /// <summary>Gets the exact owner subject and generation targeted.</summary>
    public OwnerReference Owner { get; }
    /// <summary>Gets the canonical digest of the requested semantic action.</summary>
    public CanonicalDigest CommandDigest { get; }
    /// <summary>Gets current action-specific authorization evidence.</summary>
    public AuthorizationDescriptor Authorization { get; }

    /// <summary>Creates a stable operation declaration.</summary>
    /// <exception cref="ArgumentException">Identity, scope, owner, digest, or authorization subject is invalid.</exception>
    public OperationDeclaration(SemanticId operationId, OwnerReference owner, CanonicalDigest commandDigest, AuthorizationDescriptor authorization)
    {
        ArgumentNullException.ThrowIfNull(commandDigest); ArgumentNullException.ThrowIfNull(authorization);
        if (!operationId.IsValid || !owner.IsValid || operationId.Scope != owner.SubjectId.Scope || authorization.Evidence.Subject != owner)
            throw new ArgumentException("Operation, owner, and authorization must name the same scoped owner generation.");
        OperationId = operationId; Owner = owner; CommandDigest = commandDigest; Authorization = authorization;
    }
}

/// <summary>Records one immutable attempt observation for a stable operation.</summary>
public sealed record OperationAttempt
{
    /// <summary>Gets the stable operation.</summary>
    public OperationDeclaration Operation { get; }
    /// <summary>Gets the attempt identity; retries use new attempt identities while retaining the operation.</summary>
    public SemanticId AttemptId { get; }
    /// <summary>Gets the attempt outcome.</summary>
    public AttemptOutcome Outcome { get; }
    /// <summary>Gets the dispatch time when a boundary was crossed.</summary>
    public NamedTime DispatchedAt { get; }
    /// <summary>Gets optional fresh verification evidence; required only for Verified.</summary>
    public EvidenceDescriptor? Verification { get; }

    /// <summary>Creates an immutable operation-attempt observation.</summary>
    /// <exception cref="ArgumentException">Scope, outcome, time, or verification rules are invalid.</exception>
    public OperationAttempt(OperationDeclaration operation, SemanticId attemptId, AttemptOutcome outcome, NamedTime dispatchedAt, EvidenceDescriptor? verification = null)
    {
        ArgumentNullException.ThrowIfNull(operation);
        var verified = outcome == AttemptOutcome.Verified;
        if (!attemptId.IsValid || attemptId.Scope != operation.OperationId.Scope || outcome == AttemptOutcome.None || !Enum.IsDefined(outcome) ||
            !dispatchedAt.IsValid || dispatchedAt.Kind != TimeKind.Dispatch || verified != (verification is { Role: EvidenceRole.Verification }))
            throw new ArgumentException("Attempt scope, outcome, dispatch time, or verification evidence is invalid.");
        Operation = operation; AttemptId = attemptId; Outcome = outcome; DispatchedAt = dispatchedAt; Verification = verification;
    }
}
