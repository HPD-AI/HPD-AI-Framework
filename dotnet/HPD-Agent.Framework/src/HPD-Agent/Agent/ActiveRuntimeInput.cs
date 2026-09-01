using System.Threading.Channels;
using Microsoft.Extensions.AI;

namespace HPD.Agent;

internal enum ActiveRuntimeInputState
{
    Accepting,
    Finishing,
    Finished
}

internal sealed record AcceptedSteeringInput(
    IReadOnlyList<ChatMessage> Messages,
    string? ClientInputId);

internal sealed class ActiveRuntimeInput
{
    internal ActiveRuntimeInput(AgentInputEvent input, CancellationTokenSource cancellation)
    {
        Input = input;
        Cancellation = cancellation;
        Steering = Channel.CreateUnbounded<AcceptedSteeringInput>(new UnboundedChannelOptions
        {
            SingleReader = true,
            SingleWriter = false,
            AllowSynchronousContinuations = false
        });
    }

    internal AgentInputEvent Input { get; }
    internal CancellationTokenSource Cancellation { get; }
    internal Channel<AcceptedSteeringInput> Steering { get; }
    internal ActiveRuntimeInputState State { get; set; } = ActiveRuntimeInputState.Accepting;
    internal string? ThreadExecutionId => Input.ThreadExecutionId;
}

internal sealed class PreparedAgentWorkAdmission : IDisposable
{
    private readonly ChannelWriter<AgentInputEvent> _writer;
    private int _state;

    internal PreparedAgentWorkAdmission(AgentInputEvent input, ChannelWriter<AgentInputEvent> writer)
    {
        Input = input ?? throw new ArgumentNullException(nameof(input));
        _writer = writer ?? throw new ArgumentNullException(nameof(writer));
    }

    internal AgentInputEvent Input { get; }

    internal void CommitVisible()
    {
        if (Interlocked.CompareExchange(ref _state, 1, 0) != 0)
            throw new InvalidOperationException("Prepared work admission is no longer pending.");
        if (!_writer.TryWrite(Input))
            throw new InvalidOperationException("Prepared work could not become visible after admission.");
    }

    public void Dispose() => Interlocked.CompareExchange(ref _state, 2, 0);
}
