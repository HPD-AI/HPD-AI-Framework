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
