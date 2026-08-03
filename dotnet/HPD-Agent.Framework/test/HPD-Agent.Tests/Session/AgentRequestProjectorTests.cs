using FluentAssertions;

namespace HPD.Agent.Tests.Session;

public sealed class AgentRequestProjectorTests
{
    [Fact]
    public void ProjectPending_ReturnsOnlyUnresolvedRequestsOwnedByActiveExecution()
    {
        AgentEvent[] events =
        [
            Scope(new TestRequest("old", "test"), "execution-old", 1),
            Scope(new TestRequest("resolved", "test"), "execution-live", 2),
            Scope(new TestResponse("resolved", "test"), "execution-live", 3),
            Scope(new TestRequest("pending", "test"), "execution-live", 4)
        ];

        var pending = AgentRequestProjector.ProjectPending(events, "execution-live");

        pending.Should().ContainSingle()
            .Which.Should().BeOfType<TestRequest>()
            .Which.RequestId.Should().Be("pending");
    }

    [Fact]
    public void ProjectPending_TerminalFactClosesRequest()
    {
        AgentEvent[] events =
        [
            Scope(new TestRequest("cancelled", "test"), "execution-live", 1),
            Scope(new AgentRequestTerminatedEvent(
                "cancelled",
                "test",
                AgentRequestTerminalKind.Cancelled,
                "cancelled",
                DateTimeOffset.UtcNow), "execution-live", 2)
        ];

        AgentRequestProjector.ProjectPending(events, "execution-live").Should().BeEmpty();
        AgentRequestProjector.ClassifyResponseAttempt(events, "cancelled", "execution-live")
            .Status.Should().Be(AgentRespondStatus.Cancelled);
    }

    [Fact]
    public void ClassifyResponseAttempt_RejectsUnansweredRequestFromEndedExecution()
    {
        AgentEvent[] events =
        [
            Scope(new TestRequest("request", "test"), "execution-old", 1)
        ];

        AgentRequestProjector.ClassifyResponseAttempt(events, "request", activeThreadExecutionId: null)
            .Status.Should().Be(AgentRespondStatus.ExecutionEnded);
    }

    private static T Scope<T>(T evt, string executionId, long sequence)
        where T : AgentEvent => (T)(evt with
        {
            SessionId = "session",
            ThreadId = "thread",
            ThreadExecutionId = executionId,
            ThreadSequenceNumber = sequence
        });

    private sealed record TestRequest(string RequestId, string SourceName)
        : AgentEvent, IAgentRequestEvent<TestResponse>;

    private sealed record TestResponse(string RequestId, string SourceName)
        : AgentEvent, IAgentResponseEvent;
}
