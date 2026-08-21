using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Threading.Channels;
using HPD.Agent.TUI.Runtime;
using Microsoft.Extensions.AI;

namespace HPD.Agent.TUI.Console.Demo;

internal sealed class SampleAgentTuiRuntime : IHpdAgentTuiRuntime, IAsyncDisposable
{
    private readonly Channel<AgentEvent> _events = Channel.CreateUnbounded<AgentEvent>();
    private readonly List<AgentEvent> _history = [];
    private readonly object _gate = new();
    private AgentTuiThreadExecution? _activeExecution;

    public Task<AgentTuiScopeResolution> ResolveInitialScopeAsync(
        AgentTuiRuntimeScope? requested,
        CancellationToken cancellationToken = default)
        => Task.FromResult(new AgentTuiScopeResolution(
            requested ?? new AgentTuiRuntimeScope("sample-agent", "local-session", "main"),
            IsDurable: true));

    public Task<AgentTuiRuntimeScope> EnsureDurableScopeAsync(
        AgentTuiRuntimeScope scope,
        CancellationToken cancellationToken = default)
        => Task.FromResult(scope);

    public async IAsyncEnumerable<AgentTuiEventBatch> ObserveAsync(
        AgentTuiRuntimeScope scope,
        ThreadJournalCursor after,
        ThreadJournalCursor initialObservedCursor,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await foreach (var evt in _events.Reader.ReadAllAsync(cancellationToken))
        {
            yield return new AgentTuiEventBatch(
                [evt],
                AgentTuiEventDeliveryMode.Live,
                initialObservedCursor,
                new ThreadJournalCursor(initialObservedCursor.Generation, evt.ThreadSequenceNumber),
                new ThreadJournalCursor(initialObservedCursor.Generation, evt.ThreadSequenceNumber));
        }
    }

    public Task<AgentTuiSubmitResult> SubmitInputAsync(
        AgentTuiRuntimeScope scope,
        AgentInputEvent input,
        CancellationToken cancellationToken = default)
    {
        var executionId = Guid.NewGuid().ToString("N");
        var startedAt = DateTimeOffset.UtcNow;
        _activeExecution = new AgentTuiThreadExecution(executionId, scope.AgentId, scope.SessionId, scope.ThreadId, "active", startedAt);
        _ = Task.Run(
            () => RunSampleTurnAsync(scope, input, executionId, startedAt, cancellationToken),
            CancellationToken.None);
        return Task.FromResult(new AgentTuiSubmitResult(
            AgentInputDisposition.Queued,
            executionId,
            _activeExecution));
    }

    public Task<AgentTuiSubmitResult> CancelExecutionAsync(
        AgentTuiRuntimeScope scope,
        string threadExecutionId,
        CancellationToken cancellationToken = default)
    {
        _activeExecution = null;
        return Task.FromResult(new AgentTuiSubmitResult(AgentInputDisposition.Accepted, threadExecutionId));
    }

    public Task<AgentRespondResult> AnswerRequestAsync(
        AgentTuiRuntimeScope scope,
        AgentEvent response,
        CancellationToken cancellationToken = default)
        => Task.FromResult(new AgentRespondResult(
            AgentRespondStatus.Accepted,
            response.EventId));

    public Task<AgentTuiThreadState> GetThreadStateAsync(
        AgentTuiRuntimeScope scope,
        CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            return Task.FromResult(new AgentTuiThreadState(
                _history.Count == 0
                    ? new ThreadJournalCursor(1, 0)
                    : new ThreadJournalCursor(1, _history.Max(static evt => evt.ThreadSequenceNumber)),
                _activeExecution,
                []));
        }
    }

    public ValueTask DisposeAsync()
    {
        _events.Writer.TryComplete();
        return ValueTask.CompletedTask;
    }

    private async Task RunSampleTurnAsync(
        AgentTuiRuntimeScope scope,
        AgentInputEvent input,
        string executionId,
        DateTimeOffset startedAt,
        CancellationToken cancellationToken)
    {
        await PublishAsync(new ThreadExecutionStartedEvent(executionId, scope.AgentId, startedAt)
        {
            SessionId = scope.SessionId,
            ThreadId = scope.ThreadId
        }, cancellationToken);

        var messageId = Guid.NewGuid().ToString("N");
        await PublishAsync(new TextMessageStartEvent(messageId, "assistant")
        {
            SessionId = scope.SessionId,
            ThreadId = scope.ThreadId,
            Metadata = AgentMetadata(scope, "sample-agent")
        }, cancellationToken);

        var userText = input is UserMessagesInputEvent messages
            ? FirstText(messages.Messages)
            : "input";
        await Delay(cancellationToken);
        await PublishAsync(new TextDeltaEvent($"I received: **{EscapeMarkdown(userText)}**\n\n", messageId)
        {
            SessionId = scope.SessionId,
            ThreadId = scope.ThreadId,
            Metadata = AgentMetadata(scope, "sample-agent")
        }, cancellationToken);

        await Delay(cancellationToken);
        var callId = Guid.NewGuid().ToString("N");
        const string toolName = "sample.inspect";
        await PublishAsync(new ToolCallStartEvent(callId, toolName, messageId, "SampleHarness")
        {
            SessionId = scope.SessionId,
            ThreadId = scope.ThreadId,
            Metadata = AgentMetadata(scope, "tool", scope.AgentId, 1)
        }, cancellationToken);

        await Delay(cancellationToken);
        var argsJson = JsonSerializer.Serialize(new { path = "Program.cs" });
        await PublishAsync(new ToolCallArgsEvent(callId, argsJson)
        {
            SessionId = scope.SessionId,
            ThreadId = scope.ThreadId,
            Metadata = AgentMetadata(scope, "tool", scope.AgentId, 1)
        }, cancellationToken);

        await Delay(cancellationToken);
        await PublishAsync(new ToolCallEndEvent(callId, messageId, toolName, argsJson)
        {
            SessionId = scope.SessionId,
            ThreadId = scope.ThreadId,
            Metadata = AgentMetadata(scope, "tool", scope.AgentId, 1)
        }, cancellationToken);

        await Delay(cancellationToken);
        await PublishAsync(new TextDeltaEvent("The shell updated a keyed tool row and kept the assistant markdown row alive.", messageId)
        {
            SessionId = scope.SessionId,
            ThreadId = scope.ThreadId,
            Metadata = AgentMetadata(scope, "sample-agent")
        }, cancellationToken);

        await PublishAsync(new TextMessageEndEvent(messageId)
        {
            SessionId = scope.SessionId,
            ThreadId = scope.ThreadId,
            Metadata = AgentMetadata(scope, "sample-agent")
        }, cancellationToken);

        await PublishAsync(new ThreadExecutionFinishedEvent(executionId, scope.AgentId, ThreadExecutionOutcome.Succeeded, DateTimeOffset.UtcNow)
        {
            SessionId = scope.SessionId,
            ThreadId = scope.ThreadId
        }, cancellationToken);

        _activeExecution = null;
    }

    private static string FirstText(IEnumerable<ChatMessage> messages)
    {
        foreach (var message in messages)
        {
            foreach (var content in message.Contents)
            {
                if (content is TextContent text)
                    return text.Text;
            }
        }

        return "input";
    }

    private async Task PublishAsync(AgentEvent evt, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            _history.Add(evt);
        }

        await _events.Writer.WriteAsync(evt, cancellationToken);
    }

    private static Task Delay(CancellationToken cancellationToken)
        => Task.Delay(TimeSpan.FromMilliseconds(250), cancellationToken);

    private static AgentMetadata AgentMetadata(
        AgentTuiRuntimeScope scope,
        string name,
        string? parentAgentId = null,
        int depth = 0)
        => new()
        {
            AgentName = name,
            AgentId = depth == 0 ? scope.AgentId : $"{scope.AgentId}/{name}",
            ParentAgentId = parentAgentId,
            AgentChain = depth == 0 ? [name] : ["sample-agent", name],
            Depth = depth
        };

    private static string EscapeMarkdown(string value)
        => value.Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("*", "\\*", StringComparison.Ordinal)
            .Replace("_", "\\_", StringComparison.Ordinal)
            .Replace("`", "\\`", StringComparison.Ordinal);
}
