using System.Threading.Channels;
using Microsoft.Extensions.AI;

namespace HPD.Agent;

internal enum ActiveRuntimeInputState
{
    Accepting,
    Finishing,
    Finished
}

internal sealed record AcceptedTurnContinuation(
    IReadOnlyList<ChatMessage> Messages,
    string? ClientInputId,
    AgentOperationNotificationInputEvent? OperationNotification = null);

internal sealed class ActiveRuntimeInput
{
    private AgentInputCancellation? _cancellationInfo;
    internal AgentInputCancellation? CancellationInfo => Volatile.Read(ref _cancellationInfo);
    internal void RecordCancellation(AgentInputCancellation info)
        => Interlocked.CompareExchange(ref _cancellationInfo, info, null);

    internal ActiveRuntimeInput(AgentInputEvent input, CancellationTokenSource cancellation)
    {
        Input = input;
        Cancellation = cancellation;
        Continuations = Channel.CreateUnbounded<AcceptedTurnContinuation>(new UnboundedChannelOptions
        {
            SingleReader = true,
            SingleWriter = false,
            AllowSynchronousContinuations = false
        });
    }

    internal AgentInputEvent Input { get; }
    internal CancellationTokenSource Cancellation { get; }
    internal Channel<AcceptedTurnContinuation> Continuations { get; }
    internal ActiveRuntimeInputState State { get; set; } = ActiveRuntimeInputState.Accepting;
    internal string? ThreadExecutionId => Input.ThreadExecutionId;
}

internal sealed class PreparedAgentWorkAdmission : IDisposable
{
    private readonly Action _commit;
    private readonly Action _abort;
    private int _state;

    internal PreparedAgentWorkAdmission(AgentInputEvent input, Action commit, Action abort)
    {
        Input = input ?? throw new ArgumentNullException(nameof(input));
        _commit = commit ?? throw new ArgumentNullException(nameof(commit));
        _abort = abort ?? throw new ArgumentNullException(nameof(abort));
    }

    internal AgentInputEvent Input { get; }

    internal void CommitVisible()
    {
        if (Interlocked.CompareExchange(ref _state, 1, 0) == 0)
            _commit();
    }

    public void Dispose()
    {
        if (Interlocked.CompareExchange(ref _state, 2, 0) == 0)
            _abort();
    }
}

internal sealed class AgentWorkScheduler
{
    private readonly object _gate;
    private readonly ChannelWriter<AgentInputEvent> _writer;
    private TaskCompletionSource _drained = CompletedDrain();
    private int _preparedCount;
    private bool _stopping;

    internal AgentWorkScheduler(object gate, ChannelWriter<AgentInputEvent> writer)
    {
        _gate = gate ?? throw new ArgumentNullException(nameof(gate));
        _writer = writer ?? throw new ArgumentNullException(nameof(writer));
    }

    internal PreparedAgentWorkAdmission Prepare(
        AgentInputEvent input,
        Action? reserveCompletion = null,
        Action? abortCompletion = null,
        Func<bool>? tryCommitToActiveTurn = null)
    {
        lock (_gate)
        {
            if (_stopping)
                throw new InvalidOperationException("Agent runtime is stopping and cannot prepare queued work.");
            reserveCompletion?.Invoke();
            if (_preparedCount++ == 0)
                _drained = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        }

        return new PreparedAgentWorkAdmission(
            input,
            commit: () =>
            {
                lock (_gate)
                {
                    var admitted = tryCommitToActiveTurn?.Invoke() == true || _writer.TryWrite(input);
                    if (!admitted)
                    {
                        System.Environment.FailFast(
                            "Agent work scheduler invariant violated: a shutdown-pinned prepared admission was not writable.");
                    }
                    ReleasePreparation();
                }
            },
            abort: () =>
            {
                lock (_gate)
                {
                    abortCompletion?.Invoke();
                    ReleasePreparation();
                }
            });
    }

    internal Task StopPreparing()
    {
        lock (_gate)
        {
            _stopping = true;
            return _drained.Task;
        }
    }

    private void ReleasePreparation()
    {
        if (--_preparedCount == 0)
            _drained.TrySetResult();
    }

    private static TaskCompletionSource CompletedDrain()
    {
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        completion.SetResult();
        return completion;
    }
}
