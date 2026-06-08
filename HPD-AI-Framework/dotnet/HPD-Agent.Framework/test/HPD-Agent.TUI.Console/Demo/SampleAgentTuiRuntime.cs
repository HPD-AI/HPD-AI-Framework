using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Threading.Channels;
using HPD.Agent.TUI.Runtime;

namespace HPD.Agent.TUI.Console.Demo;

internal sealed class SampleAgentTuiRuntime : IHpdAgentTuiRuntime, IAsyncDisposable
{
    private readonly Channel<AgentEvent> _events = Channel.CreateUnbounded<AgentEvent>();
    private readonly List<AgentEvent> _history = [];
    private readonly object _gate = new();
    private AgentTuiBranchRun? _activeRun;

    public Task<AgentTuiRuntimeScope> EnsureScopeAsync(
        AgentTuiRuntimeScope? requested,
        CancellationToken cancellationToken = default)
        => Task.FromResult(requested ?? new AgentTuiRuntimeScope("sample-agent", "local-session", "main"));

    public async IAsyncEnumerable<AgentEvent> ObserveAsync(
        AgentTuiRuntimeScope scope,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await foreach (var evt in _events.Reader.ReadAllAsync(cancellationToken))
        {
            yield return evt;
        }
    }

    public Task SubmitInputAsync(
        AgentTuiRuntimeScope scope,
        AgentInputEvent input,
        CancellationToken cancellationToken = default)
    {
        _ = Task.Run(() => RunSampleTurnAsync(scope, input, cancellationToken), CancellationToken.None);
        return Task.CompletedTask;
    }

    public Task RespondAsync(
        AgentTuiRuntimeScope scope,
        AgentEvent response,
        CancellationToken cancellationToken = default)
        => Task.CompletedTask;

    public Task<IReadOnlyList<AgentEvent>> GetBranchEventsAsync(
        AgentTuiRuntimeScope scope,
        CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            return Task.FromResult<IReadOnlyList<AgentEvent>>(_history.ToArray());
        }
    }

    public Task<AgentTuiBranchRun?> GetActiveRunAsync(
        AgentTuiRuntimeScope scope,
        CancellationToken cancellationToken = default)
        => Task.FromResult(_activeRun);

    public ValueTask DisposeAsync()
    {
        _events.Writer.TryComplete();
        return ValueTask.CompletedTask;
    }

    private async Task RunSampleTurnAsync(
        AgentTuiRuntimeScope scope,
        AgentInputEvent input,
        CancellationToken cancellationToken)
    {
        var runId = Guid.NewGuid().ToString("N");
        var startedAt = DateTimeOffset.UtcNow;
        _activeRun = new AgentTuiBranchRun(runId, scope.AgentId, scope.SessionId, scope.BranchId, "running", startedAt);

        await PublishAsync(new BranchRunStartedEvent(runId, scope.AgentId, startedAt)
        {
            SessionId = scope.SessionId,
            BranchId = scope.BranchId
        }, cancellationToken);

        var messageId = Guid.NewGuid().ToString("N");
        await PublishAsync(new TextMessageStartEvent(messageId, "assistant")
        {
            SessionId = scope.SessionId,
            BranchId = scope.BranchId,
            Metadata = AgentMetadata(scope, "sample-agent")
        }, cancellationToken);

        var userText = input is UserTextInputEvent text ? text.Text : "input";
        await Delay(cancellationToken);
        await PublishAsync(new TextDeltaEvent($"I received: **{EscapeMarkdown(userText)}**\n\n", messageId)
        {
            SessionId = scope.SessionId,
            BranchId = scope.BranchId,
            Metadata = AgentMetadata(scope, "sample-agent")
        }, cancellationToken);

        await Delay(cancellationToken);
        var callId = Guid.NewGuid().ToString("N");
        await PublishAsync(new ToolCallStartEvent(callId, "sample.inspect", messageId, "SampleHarness")
        {
            SessionId = scope.SessionId,
            BranchId = scope.BranchId,
            Metadata = AgentMetadata(scope, "tool", scope.AgentId, 1)
        }, cancellationToken);

        await Delay(cancellationToken);
        await PublishAsync(new ToolCallArgsEvent(callId, JsonSerializer.Serialize(new { path = "Program.cs" }))
        {
            SessionId = scope.SessionId,
            BranchId = scope.BranchId,
            Metadata = AgentMetadata(scope, "tool", scope.AgentId, 1)
        }, cancellationToken);

        await Delay(cancellationToken);
        await PublishAsync(new ToolCallEndEvent(callId)
        {
            SessionId = scope.SessionId,
            BranchId = scope.BranchId,
            Metadata = AgentMetadata(scope, "tool", scope.AgentId, 1)
        }, cancellationToken);

        await Delay(cancellationToken);
        await PublishAsync(new TextDeltaEvent("The shell updated a keyed tool row and kept the assistant markdown row alive.", messageId)
        {
            SessionId = scope.SessionId,
            BranchId = scope.BranchId,
            Metadata = AgentMetadata(scope, "sample-agent")
        }, cancellationToken);

        await PublishAsync(new TextMessageEndEvent(messageId)
        {
            SessionId = scope.SessionId,
            BranchId = scope.BranchId,
            Metadata = AgentMetadata(scope, "sample-agent")
        }, cancellationToken);

        await PublishAsync(new BranchRunCompletedEvent(runId, scope.AgentId, Cancelled: false)
        {
            SessionId = scope.SessionId,
            BranchId = scope.BranchId
        }, cancellationToken);

        _activeRun = null;
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
