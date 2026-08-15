using HPD.Agent.Authority;
using HPD.Agent.Middleware;

namespace HPD.Agent.Tests.Middleware;

public sealed class AgentToolAuthorizationGateTests
{
    [Fact]
    public void RequestRequiresToolGenerationAndOneSession()
    {
        var session = Session();
        var claim = new JournalPositionV1(session, 4);
        Assert.Throws<ArgumentException>(() => new AgentToolAuthorizationRequest(
            OperationId.Create(), claim, ExpectedAuthorityVectorV1.Create(session, []), Hash256.Compute([1]),
            new BoundedAscii("tool")));
        Assert.Throws<ArgumentException>(() => new AgentToolAuthorizationRequest(
            OperationId.Create(), claim, Authority(OtherSession()), Hash256.Compute([1]),
            new BoundedAscii("tool")));
    }

    [Fact]
    public void ExactLaterProofMatchesAndEveryIdentityMutationFails()
    {
        var session = Session();
        var request = new AgentToolAuthorizationRequest(
            OperationId.Create(), new JournalPositionV1(session, 4), Authority(session), Hash256.Compute([1]),
            new BoundedAscii("tool"));
        var exact = new AgentToolAuthorizationResult.Authorized(
            request.OperationId, request.DispatchFingerprint, request.OwnerClaimPosition, request.Authority,
            new JournalPositionV1(session, 5));
        Assert.True(AgentToolAuthorizationProof.Matches(request, exact));

        Assert.False(AgentToolAuthorizationProof.Matches(request, new AgentToolAuthorizationResult.Authorized(
            OperationId.Create(), request.DispatchFingerprint, request.OwnerClaimPosition, request.Authority, new JournalPositionV1(session, 5))));
        Assert.False(AgentToolAuthorizationProof.Matches(request, new AgentToolAuthorizationResult.Authorized(
            request.OperationId, Hash256.Compute([2]), request.OwnerClaimPosition, request.Authority, new JournalPositionV1(session, 5))));
        Assert.False(AgentToolAuthorizationProof.Matches(request, new AgentToolAuthorizationResult.Authorized(
            request.OperationId, request.DispatchFingerprint, request.OwnerClaimPosition,
            ExpectedAuthorityVectorV1.Create(session,
                [new AuthorityAxisValueV1.Tool(ToolGenerationId.Create())]),
            new JournalPositionV1(session, 5))));
        Assert.Throws<ArgumentException>(() => new AgentToolAuthorizationResult.Authorized(
            request.OperationId, request.DispatchFingerprint, request.OwnerClaimPosition,
            request.Authority, new JournalPositionV1(session, 4)));
        Assert.False(AgentToolAuthorizationProof.Matches(request, new AgentToolAuthorizationResult.Authorized(
            request.OperationId, request.DispatchFingerprint, new JournalPositionV1(session, 3),
            request.Authority, new JournalPositionV1(session, 5))));
        Assert.False(AgentToolAuthorizationProof.Matches(request, new AgentToolAuthorizationResult.Authorized(
            request.OperationId, request.DispatchFingerprint, new JournalPositionV1(session, 5),
            request.Authority, new JournalPositionV1(session, 6))));
    }

    [Fact]
    public async Task AdvisoryObservationCannotSatisfyTheDistinctAuthorizationGate()
    {
        var observed = await new CompositeAgentControlHook([
            new AgentControlParticipant(new BoundedAscii("observer"), 0, new Observer()),
        ]).ObserveAsync(new AgentControlEnvelope(
            OperationId.Create(), null, AgentControlKind.ToolObservation, new byte[] { 1 },
            new BoundedAscii("tool.observation.v1"), 1));
        var unavailable = await new UnavailableGate().AuthorizeAsync(Request());

        Assert.IsType<AgentControlObservationResult.Observed>(observed);
        Assert.IsType<AgentToolAuthorizationResult.Unavailable>(unavailable);
    }

    [Fact]
    public void ResultCodesAndStructuralProofsRejectDefaults()
    {
        Assert.Throws<ArgumentException>(() => new AgentToolAuthorizationResult.Denied(default));
        Assert.Throws<ArgumentException>(() => new AgentToolAuthorizationResult.Unavailable(default));
        Assert.Throws<ArgumentException>(() => new AgentToolAuthorizationResult.OutcomeUnknown(default));
        var request = Request();
        Assert.Throws<ArgumentException>(() => new AgentToolAuthorizationResult.Authorized(
            default, request.DispatchFingerprint, request.OwnerClaimPosition, request.Authority,
            new JournalPositionV1(request.Authority.Session, 2)));
    }

    private static AgentToolAuthorizationRequest Request()
    {
        var session = Session();
        return new AgentToolAuthorizationRequest(
            OperationId.Create(), new JournalPositionV1(session, 1), Authority(session), Hash256.Compute([1]),
            new BoundedAscii("tool"));
    }

    private static ExpectedAuthorityVectorV1 Authority(SessionAuthorityStampV1 session) =>
        ExpectedAuthorityVectorV1.Create(session,
            [new AuthorityAxisValueV1.Tool(ToolGenerationId.Create())]);

    private static SessionAuthorityStampV1 Session() =>
        new(RuntimeGenerationId.Create(), LiveSessionId.Create());

    private static SessionAuthorityStampV1 OtherSession() =>
        new(RuntimeGenerationId.Create(), LiveSessionId.Create());

    private sealed class Observer : IAgentControlHook
    {
        public ValueTask<AgentControlObservationResult> ObserveAsync(
            AgentControlEnvelope envelope, CancellationToken waitCancellation = default) =>
            ValueTask.FromResult<AgentControlObservationResult>(new AgentControlObservationResult.Observed());
    }

    private sealed class UnavailableGate : IAgentToolAuthorizationGate
    {
        public ValueTask<AgentToolAuthorizationResult> AuthorizeAsync(
            AgentToolAuthorizationRequest request, CancellationToken waitCancellation = default) =>
            ValueTask.FromResult<AgentToolAuthorizationResult>(
                new AgentToolAuthorizationResult.Unavailable(new BoundedAscii("not-installed")));
    }
}
