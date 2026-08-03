using FluentAssertions;
using Xunit;

namespace HPD.Agent.Tests.MultiAgents;

public class MultiAgentRuntimeTests
{
    [Fact]
    public async Task InvokeAsync_BackgroundOnly_WithoutRuntime_ReturnsUnavailableReceipt()
    {
        var result = await MultiAgentRuntime.InvokeAsync(
            new MultiAgentRuntime.MultiAgentInvocationRequest
            {
                Workflow = new NoopWorkflow(),
                Name = "draft_and_review",
                Input = "draft this",
                ParentContext = null,
                InvocationModePolicy = AgentInvocationModePolicy.BackgroundOnly
            },
            CancellationToken.None);

        result.Mode.Should().Be(AgentInvocationMode.Background);
        result.Background.Should().NotBeNull();
        result.Background!.Status.Should().Be("background_unavailable");
        result.Background.SourceKind.Should().Be(BackgroundTaskSourceKind.MultiAgent);
        result.Background.Name.Should().Be("draft_and_review");
    }

    private sealed class NoopWorkflow : IMultiAgentWorkflow
    {
        public async IAsyncEnumerable<HPD.Events.Event> ExecuteStreamingAsync(
            string input,
            global::HPD.Agent.Middleware.FunctionExecutionContext? parentContext,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            await Task.CompletedTask;
            yield break;
        }

        public Task<string> RunAsync(
            string input,
            global::HPD.Agent.Middleware.FunctionExecutionContext? parentContext,
            CancellationToken cancellationToken = default)
            => Task.FromResult(string.Empty);
    }
}
