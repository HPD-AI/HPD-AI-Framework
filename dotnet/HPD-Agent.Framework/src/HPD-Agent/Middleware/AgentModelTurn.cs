using HPD.Events;
using Microsoft.Extensions.AI;

namespace HPD.Agent.Middleware;

/// <summary>
/// Model transport used for an agent model turn.
/// </summary>
public enum AgentModelTransport
{
    Chat = 0,
    Realtime = 1
}

/// <summary>
/// Transport-neutral request for one model turn inside the agent loop.
/// </summary>
public sealed record AgentModelTurnRequest
{
    public required AgentModelTransport Transport { get; init; }

    public IChatClient? ChatModel { get; init; }

    public IRealtimeClient? RealtimeModel { get; init; }

    public required IReadOnlyList<ChatMessage> Messages { get; init; }

    public required ChatOptions Options { get; init; }

    public required AgentLoopState State { get; init; }

    public required int Iteration { get; init; }

    public IEventFlowRegistry? EventFlows { get; init; }

    public Session? Session { get; init; }

    public IContentStore? ContentStore { get; init; }

    public AgentClientSet? ClientSet { get; init; }

    public AgentRunConfig? RunConfig { get; init; }

    public IEventCoordinator? EventCoordinator { get; init; }

    public Func<AgentEvent, CancellationToken, ValueTask<AgentEvent>>? EventPublisher { get; init; }

    public HPD.Events.Struct.IStructEventHub? StructEvents { get; init; }

    public AgentModelTurnRequest Override(
        IChatClient? chatModel = null,
        IRealtimeClient? realtimeModel = null,
        IReadOnlyList<ChatMessage>? messages = null,
        ChatOptions? options = null)
    {
        return this with
        {
            ChatModel = chatModel ?? ChatModel,
            RealtimeModel = realtimeModel ?? RealtimeModel,
            Messages = messages ?? Messages,
            Options = options ?? Options
        };
    }
}

/// <summary>
/// Transport-neutral model update emitted by an agent model turn executor.
/// </summary>
public abstract record AgentModelUpdate
{
    public virtual ChatResponseUpdate? ChatUpdate => null;
}

/// <summary>
/// Chat transport update. This preserves existing chat behavior while the agent loop becomes transport-aware.
/// </summary>
public sealed record AgentChatModelUpdate(ChatResponseUpdate Update) : AgentModelUpdate
{
    public override ChatResponseUpdate ChatUpdate => Update;
}

/// <summary>
/// Text emitted by a model transport in normalized form.
/// </summary>
/// <param name="Text">The emitted text delta or final text.</param>
/// <param name="ResponseId">Optional provider response identifier.</param>
/// <param name="IsFinal">Whether this text item is complete.</param>
public sealed record AgentTextDeltaUpdate(
    string Text,
    string? ResponseId = null,
    bool IsFinal = false) : AgentModelUpdate;

/// <summary>
/// Reasoning text emitted by a model transport in normalized form.
/// </summary>
/// <param name="Text">The emitted reasoning delta or final reasoning text.</param>
/// <param name="ResponseId">Optional provider response identifier.</param>
/// <param name="IsFinal">Whether this reasoning item is complete.</param>
public sealed record AgentReasoningDeltaUpdate(
    string Text,
    string? ResponseId = null,
    bool IsFinal = false) : AgentModelUpdate;

/// <summary>
/// Audio emitted by a model transport in normalized form.
/// </summary>
/// <param name="Audio">The emitted audio bytes.</param>
/// <param name="MediaType">The media type, when known.</param>
/// <param name="ResponseId">Optional provider response identifier.</param>
/// <param name="IsFinal">Whether this audio item is complete.</param>
public sealed record AgentAudioDeltaUpdate(
    ReadOnlyMemory<byte> Audio,
    string? MediaType,
    string? ResponseId = null,
    bool IsFinal = false) : AgentModelUpdate;

/// <summary>
/// User input transcript emitted by a realtime model transport in normalized form.
/// </summary>
/// <param name="Text">The transcript delta or final transcript text.</param>
/// <param name="Stage">The transcript lifecycle stage.</param>
/// <param name="ItemId">Optional provider conversation item identifier.</param>
/// <param name="ContentIndex">Optional provider content index for the transcribed input.</param>
/// <param name="Error">Optional provider transcription error.</param>
public sealed record AgentInputTranscriptUpdate(
    string Text,
    AgentInputTranscriptStage Stage,
    string? ItemId = null,
    int? ContentIndex = null,
    Exception? Error = null,
    UsageDetails? Usage = null) : AgentModelUpdate
{
    public bool IsFinal => Stage is AgentInputTranscriptStage.Final;
}

public enum AgentInputTranscriptStage
{
    Partial = 0,
    Final = 1,
    Failed = 2
}

/// <summary>
/// Function/tool call emitted by a model transport in normalized form.
/// </summary>
/// <param name="Call">The function call content to execute.</param>
/// <param name="IsFinal">Whether this call is complete enough to execute.</param>
/// <param name="ResponseId">Optional provider response identifier.</param>
public sealed record AgentToolCallUpdate(
    FunctionCallContent Call,
    bool IsFinal,
    string? ResponseId = null) : AgentModelUpdate;

/// <summary>
/// Lifecycle state for a normalized model response.
/// </summary>
public enum AgentModelResponseState
{
    Created = 0,
    InProgress = 1,
    Completed = 2,
    Failed = 3,
    Cancelled = 4,
    Incomplete = 5
}

/// <summary>
/// Response lifecycle update emitted by a model transport.
/// </summary>
/// <param name="State">The response lifecycle state.</param>
/// <param name="ResponseId">Optional provider response identifier.</param>
/// <param name="Error">Optional transport/provider error.</param>
public sealed record AgentResponseLifecycleUpdate(
    AgentModelResponseState State,
    string? ResponseId = null,
    Exception? Error = null) : AgentModelUpdate;

/// <summary>
/// Usage emitted by a model transport.
/// </summary>
/// <param name="Usage">The usage details, when available.</param>
public sealed record AgentUsageUpdate(UsageDetails? Usage) : AgentModelUpdate;

/// <summary>
/// Executes one model turn for a concrete transport.
/// </summary>
public interface IAgentModelTurnExecutor
{
    AgentModelTransport Transport { get; }

    IAsyncEnumerable<AgentModelUpdate> RunAsync(
        AgentModelTurnRequest request,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Model turn executor that can receive tool results while the same model turn remains active.
/// </summary>
public interface IAgentInteractiveModelTurnExecutor : IAgentModelTurnExecutor
{
    ValueTask SubmitToolResultsAsync(
        IReadOnlyList<FunctionResultContent> results,
        AgentModelTurnRequest request,
        CancellationToken cancellationToken = default);
}
