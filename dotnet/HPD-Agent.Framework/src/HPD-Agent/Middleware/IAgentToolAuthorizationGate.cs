using HPD.Agent.Authority;

namespace HPD.Agent.Middleware;

/// <summary>Provides the sole typed T5 authorization decision before durable tool entry intent.</summary>
/// <remarks>
/// This gate is distinct from <see cref="IAgentControlHook"/>. Observing a tool
/// envelope never authorizes execution. A trusted implementation returns
/// <see cref="AgentToolAuthorizationResult.Authorized"/> only after proving the
/// matching durable S7 authorization fact.
/// T5 authorization is not permission to enter user code or an external-effect
/// boundary. The caller must next admit the separate durable T6 entry intent and
/// recheck the immediate generation, deadline, lease, and capacity fences.
/// </remarks>
public interface IAgentToolAuthorizationGate
{
    /// <summary>Authorizes or rejects one exact tool-dispatch request.</summary>
    /// <param name="request">The immutable generation-fenced dispatch request.</param>
    /// <param name="waitCancellation">Cancels only the caller's wait.</param>
    /// <returns>A closed authorization result.</returns>
    ValueTask<AgentToolAuthorizationResult> AuthorizeAsync(
        AgentToolAuthorizationRequest request,
        CancellationToken waitCancellation = default);
}

/// <summary>Contains one immutable neutral request for an S7 tool-dispatch decision.</summary>
public sealed record AgentToolAuthorizationRequest
{
    /// <summary>Initializes one authorization request.</summary>
    /// <param name="operationId">The stable tool transaction identity.</param>
    /// <param name="ownerClaimPosition">The durable S7 owner-claim position.</param>
    /// <param name="authority">The exact sparse authority vector to revalidate.</param>
    /// <param name="dispatchFingerprint">The schema-bound dispatch fingerprint.</param>
    /// <param name="functionName">The bounded declared function name.</param>
    /// <exception cref="ArgumentException">An identity, position, vector, fingerprint, function name, or session binding is invalid.</exception>
    public AgentToolAuthorizationRequest(
        OperationId operationId,
        JournalPositionV1 ownerClaimPosition,
        ExpectedAuthorityVectorV1 authority,
        Hash256 dispatchFingerprint,
        BoundedAscii functionName)
    {
        if (!operationId.IsValid) throw new ArgumentException("A tool operation identity is required.", nameof(operationId));
        if (!ownerClaimPosition.IsValid) throw new ArgumentException("A durable owner-claim position is required.", nameof(ownerClaimPosition));
        Authority = authority ?? throw new ArgumentNullException(nameof(authority));
        if (authority.Session != ownerClaimPosition.Session)
            throw new ArgumentException("The owner claim and authority vector must name the same session.", nameof(authority));
        if (!authority.Axes.Any(static axis => axis.AxisId == AuthorityAxisId.Tool))
            throw new ArgumentException("A tool authorization must fence the S7 tool generation.", nameof(authority));
        if (dispatchFingerprint == default) throw new ArgumentException("A dispatch fingerprint is required.", nameof(dispatchFingerprint));
        if (!functionName.IsValid) throw new ArgumentException("A bounded function name is required.", nameof(functionName));
        OperationId = operationId;
        OwnerClaimPosition = ownerClaimPosition;
        DispatchFingerprint = dispatchFingerprint;
        FunctionName = functionName;
    }

    /// <summary>Gets the stable tool transaction identity.</summary>
    public OperationId OperationId { get; }
    /// <summary>Gets the durable S7 owner-claim position.</summary>
    public JournalPositionV1 OwnerClaimPosition { get; }
    /// <summary>Gets the exact sparse authority vector to revalidate.</summary>
    public ExpectedAuthorityVectorV1 Authority { get; }
    /// <summary>Gets the schema-bound dispatch fingerprint.</summary>
    public Hash256 DispatchFingerprint { get; }
    /// <summary>Gets the bounded declared function name.</summary>
    public BoundedAscii FunctionName { get; }
}

/// <summary>Reports the closed result of one exact tool-dispatch authorization request.</summary>
/// <remarks>Constructors are structural. Only a trusted installed gate may establish runtime permission.</remarks>
public abstract record AgentToolAuthorizationResult
{
    private AgentToolAuthorizationResult() { }

    /// <summary>Reports a matching durable authorization proof.</summary>
    public sealed record Authorized : AgentToolAuthorizationResult
    {
        /// <summary>Initializes one structurally valid authorization proof.</summary>
        /// <param name="operationId">The authorized tool operation.</param>
        /// <param name="dispatchFingerprint">The authorized dispatch fingerprint.</param>
        /// <param name="ownerClaimPosition">The exact durable T4 owner claim authorized by this proof.</param>
        /// <param name="authority">The authority vector revalidated by S7.</param>
        /// <param name="authorizationPosition">The durable S7 authorization position.</param>
        /// <exception cref="ArgumentException">A field is invalid or session/order bindings differ.</exception>
        /// <exception cref="ArgumentNullException"><paramref name="authority"/> is null.</exception>
        public Authorized(
            OperationId operationId,
            Hash256 dispatchFingerprint,
            JournalPositionV1 ownerClaimPosition,
            ExpectedAuthorityVectorV1 authority,
            JournalPositionV1 authorizationPosition)
        {
            if (!operationId.IsValid) throw new ArgumentException("An authorized operation is required.", nameof(operationId));
            if (dispatchFingerprint == default) throw new ArgumentException("An authorized fingerprint is required.", nameof(dispatchFingerprint));
            Authority = authority ?? throw new ArgumentNullException(nameof(authority));
            if (!ownerClaimPosition.IsValid || ownerClaimPosition.Session != authority.Session)
                throw new ArgumentException("The owner claim must belong to the authority session.", nameof(ownerClaimPosition));
            if (!authorizationPosition.IsValid || authorizationPosition.Session != authority.Session ||
                authorizationPosition.Sequence <= ownerClaimPosition.Sequence)
                throw new ArgumentException("The authorization position must be later in the authority session than its owner claim.", nameof(authorizationPosition));
            OperationId = operationId;
            DispatchFingerprint = dispatchFingerprint;
            OwnerClaimPosition = ownerClaimPosition;
            AuthorizationPosition = authorizationPosition;
        }

        /// <summary>Gets the authorized tool operation.</summary>
        public OperationId OperationId { get; }
        /// <summary>Gets the authorized dispatch fingerprint.</summary>
        public Hash256 DispatchFingerprint { get; }
        /// <summary>Gets the exact durable T4 owner claim authorized by this proof.</summary>
        public JournalPositionV1 OwnerClaimPosition { get; }
        /// <summary>Gets the authority vector revalidated by S7.</summary>
        public ExpectedAuthorityVectorV1 Authority { get; }
        /// <summary>Gets the durable S7 authorization position.</summary>
        public JournalPositionV1 AuthorizationPosition { get; }
    }

    /// <summary>Reports a definite durable refusal.</summary>
    public sealed record Denied : AgentToolAuthorizationResult
    {
        /// <summary>Initializes a denied result.</summary>
        /// <param name="safeCode">The bounded non-secret refusal code.</param>
        /// <exception cref="ArgumentException"><paramref name="safeCode"/> is invalid.</exception>
        public Denied(BoundedAscii safeCode) => SafeCode = RequireCode(safeCode, nameof(safeCode));
        /// <summary>Gets the bounded non-secret refusal code.</summary>
        public BoundedAscii SafeCode { get; }
    }

    /// <summary>Reports that no authorization authority is available.</summary>
    public sealed record Unavailable : AgentToolAuthorizationResult
    {
        /// <summary>Initializes an unavailable result.</summary>
        /// <param name="safeCode">The bounded non-secret availability code.</param>
        /// <exception cref="ArgumentException"><paramref name="safeCode"/> is invalid.</exception>
        public Unavailable(BoundedAscii safeCode) => SafeCode = RequireCode(safeCode, nameof(safeCode));
        /// <summary>Gets the bounded non-secret availability code.</summary>
        public BoundedAscii SafeCode { get; }
    }

    /// <summary>Reports that the authorization outcome cannot be determined safely.</summary>
    public sealed record OutcomeUnknown : AgentToolAuthorizationResult
    {
        /// <summary>Initializes an unknown result.</summary>
        /// <param name="safeCode">The bounded non-secret ambiguity code.</param>
        /// <exception cref="ArgumentException"><paramref name="safeCode"/> is invalid.</exception>
        public OutcomeUnknown(BoundedAscii safeCode) => SafeCode = RequireCode(safeCode, nameof(safeCode));
        /// <summary>Gets the bounded non-secret ambiguity code.</summary>
        public BoundedAscii SafeCode { get; }
    }

    private static BoundedAscii RequireCode(BoundedAscii code, string parameterName) =>
        code.IsValid ? code : throw new ArgumentException("A bounded safe code is required.", parameterName);
}

/// <summary>Validates that a returned authorization proof matches one exact request.</summary>
public static class AgentToolAuthorizationProof
{
    /// <summary>Returns whether an authorization is an exact later proof for the request.</summary>
    /// <param name="request">The original request.</param>
    /// <param name="authorization">The returned structural proof.</param>
    public static bool Matches(
        AgentToolAuthorizationRequest request,
        AgentToolAuthorizationResult.Authorized authorization)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(authorization);
        return authorization.OperationId == request.OperationId &&
               authorization.DispatchFingerprint == request.DispatchFingerprint &&
               authorization.OwnerClaimPosition == request.OwnerClaimPosition &&
               authorization.Authority == request.Authority &&
               authorization.AuthorizationPosition.Sequence > request.OwnerClaimPosition.Sequence;
    }
}
