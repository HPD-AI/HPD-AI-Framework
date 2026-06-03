using System.Runtime.CompilerServices;
using HPD.Agent.Middleware;
using Microsoft.Extensions.AI;

namespace HPD.Agent;

/// <summary>
/// Executes one agent model turn against a Microsoft.Extensions.AI realtime client.
/// </summary>
internal sealed class RealtimeModelTurnExecutor : IAgentInteractiveModelTurnExecutor, IAsyncDisposable
{
    private static readonly TimeSpan InputTranscriptDrainTimeout = TimeSpan.FromSeconds(2);

    private IRealtimeClientSession? _session;
    private bool _responseRequested;
    private readonly HashSet<string> _submittedUserMessageKeys = new(StringComparer.Ordinal);

    public AgentModelTransport Transport => AgentModelTransport.Realtime;

    public async IAsyncEnumerable<AgentModelUpdate> RunAsync(
        AgentModelTurnRequest request,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        if (request.Transport is not AgentModelTransport.Realtime)
        {
            throw new InvalidOperationException(
                $"Realtime model turn executor cannot run '{request.Transport}' transport.");
        }

        if (request.RealtimeModel is null)
        {
            throw new InvalidOperationException(
                "No realtime model is configured for this agent run. Configure a realtime provider/model or pass a realtime client in AgentClientSet.");
        }

        if (_session is null)
        {
            _session = await request.RealtimeModel.CreateSessionAsync(
                    CreateSessionOptions(request),
                    cancellationToken)
                .ConfigureAwait(false);
        }

        var newUserMessages = CollectNewUserMessages(request);
        var realtimeAudioInputs = CollectRealtimeAudioInputs(newUserMessages);
        var responseItems = CollectNewUserMessageItems(newUserMessages);
        var pendingInputTranscripts = request.RunConfig?.RealtimeTranscriptionOptions is not null
            ? realtimeAudioInputs.Count
            : 0;

        if (!_responseRequested)
        {
            await SendRealtimeAudioInputsAsync(
                    realtimeAudioInputs,
                    cancellationToken)
                .ConfigureAwait(false);

            await _session.SendAsync(
                    CreateResponseMessage(request, responseItems),
                    cancellationToken)
                .ConfigureAwait(false);

            _responseRequested = true;
        }

        try
        {
            using var streamCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            await using var enumerator = _session
                .GetStreamingResponseAsync(streamCancellation.Token)
                .GetAsyncEnumerator(streamCancellation.Token);
            var terminalResponseSeen = false;

            while (true)
            {
                bool hasMessage;
                try
                {
                    hasMessage = await enumerator.MoveNextAsync().ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (
                    terminalResponseSeen &&
                    !cancellationToken.IsCancellationRequested)
                {
                    yield break;
                }

                if (!hasMessage)
                {
                    yield break;
                }

                var message = enumerator.Current;
                var shouldReturnControl = false;
                foreach (var update in MapServerMessage(message))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    yield return update;

                    if (update is AgentInputTranscriptUpdate
                        {
                            Stage: AgentInputTranscriptStage.Final or AgentInputTranscriptStage.Failed
                        } &&
                        pendingInputTranscripts > 0)
                    {
                        pendingInputTranscripts--;
                    }

                    if (update is AgentToolCallUpdate { IsFinal: true })
                    {
                        shouldReturnControl = true;
                    }

                    if (update is AgentResponseLifecycleUpdate
                        {
                            State: AgentModelResponseState.Completed or
                                AgentModelResponseState.Failed or
                                AgentModelResponseState.Cancelled
                        })
                    {
                        terminalResponseSeen = true;
                        if (pendingInputTranscripts > 0)
                        {
                            streamCancellation.CancelAfter(InputTranscriptDrainTimeout);
                        }
                        else
                        {
                            shouldReturnControl = true;
                        }
                    }
                }

                if (terminalResponseSeen && pendingInputTranscripts == 0)
                {
                    shouldReturnControl = true;
                }

                if (shouldReturnControl)
                {
                    _responseRequested = false;
                    yield break;
                }
            }
        }
        finally
        {
            _responseRequested = false;
        }
    }

    public async ValueTask SubmitToolResultsAsync(
        IReadOnlyList<FunctionResultContent> results,
        AgentModelTurnRequest request,
        CancellationToken cancellationToken = default)
    {
        if (_session is null)
        {
            throw new InvalidOperationException(
                "Cannot submit realtime tool results before the realtime session has been opened by RunAsync.");
        }

        foreach (var result in results)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await _session.SendAsync(
                    new CreateConversationItemRealtimeClientMessage(
                        new RealtimeConversationItem([result])),
                    cancellationToken)
                .ConfigureAwait(false);
        }

        await _session.SendAsync(
                CreateResponseMessage(request),
                cancellationToken)
            .ConfigureAwait(false);
        _responseRequested = true;
    }

    public async ValueTask DisposeAsync()
    {
        if (_session is not null)
        {
            await _session.DisposeAsync().ConfigureAwait(false);
            _session = null;
            _responseRequested = false;
            _submittedUserMessageKeys.Clear();
        }
    }

    private IReadOnlyList<ChatMessage> CollectNewUserMessages(AgentModelTurnRequest request)
    {
        var messages = new List<ChatMessage>();
        foreach (var message in request.Messages)
        {
            if (!string.Equals(message.Role.Value, ChatRole.User.Value, StringComparison.Ordinal))
            {
                continue;
            }

            var key = CreateUserMessageKey(message);
            if (!_submittedUserMessageKeys.Add(key))
            {
                continue;
            }

            messages.Add(message);
        }

        return messages;
    }

    private static IReadOnlyList<RealtimeConversationItem> CollectNewUserMessageItems(
        IReadOnlyList<ChatMessage> messages)
    {
        var items = new List<RealtimeConversationItem>();
        foreach (var message in messages)
        {
            var item = ToConversationItem(message);
            if (item.Contents.Count > 0)
            {
                items.Add(item);
            }
        }

        return items;
    }

    private static IReadOnlyList<DataContent> CollectRealtimeAudioInputs(
        IReadOnlyList<ChatMessage> messages)
    {
        var inputs = new List<DataContent>();
        foreach (var content in messages.SelectMany(message => message.Contents))
        {
            if (content is not DataContent data || !AudioContent.IsAudioMediaType(data.MediaType))
            {
                continue;
            }

            inputs.Add(PrepareRealtimeAudioInput(data));
        }

        return inputs;
    }

    private async Task SendRealtimeAudioInputsAsync(
        IReadOnlyList<DataContent> audioInputs,
        CancellationToken cancellationToken)
    {
        if (_session is null || audioInputs.Count == 0)
        {
            return;
        }

        foreach (var audioInput in audioInputs)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await _session.SendAsync(
                    new InputAudioBufferAppendRealtimeClientMessage(audioInput),
                    cancellationToken)
                .ConfigureAwait(false);
        }

        await _session.SendAsync(
                new InputAudioBufferCommitRealtimeClientMessage(),
                cancellationToken)
            .ConfigureAwait(false);
    }

    private static RealtimeSessionOptions CreateSessionOptions(AgentModelTurnRequest request)
    {
        var inputAudioFormat = request.Messages.Any(ContainsRealtimeAudioInput)
            ? ResolveRealtimeInputAudioFormat(request)
            : null;

        return new RealtimeSessionOptions
        {
            SessionKind = RealtimeSessionKind.Conversation,
            Instructions = request.Options.Instructions,
            InputAudioFormat = inputAudioFormat,
            TranscriptionOptions = request.RunConfig?.RealtimeTranscriptionOptions,
            ToolMode = request.Options.ToolMode,
            Tools = request.Options.Tools?.ToArray()
        };
    }

    private static CreateResponseRealtimeClientMessage CreateResponseMessage(
        AgentModelTurnRequest request,
        IReadOnlyList<RealtimeConversationItem>? items = null)
        => new()
        {
            Items = items is { Count: > 0 } ? items.ToList() : null,
            Instructions = request.Options.Instructions,
            ToolMode = request.Options.ToolMode,
            Tools = request.Options.Tools
        };

    private static RealtimeConversationItem ToConversationItem(ChatMessage message)
    {
        var contents = message.Contents.Count > 0
            ? message.Contents
                .Where(content => !AudioContent.IsAudioMediaType((content as DataContent)?.MediaType))
                .ToList()
            : [];

        return new RealtimeConversationItem(
            contents,
            id: message.MessageId,
            role: message.Role);
    }

    private static bool ContainsRealtimeAudioInput(ChatMessage message)
        => message.Contents
            .OfType<DataContent>()
            .Any(content => AudioContent.IsAudioMediaType(content.MediaType));

    private static DataContent PrepareRealtimeAudioInput(DataContent content)
    {
        var normalizedMediaType = AudioContent.GetRealtimeInputAudioFormatMediaType(content.MediaType);
        if (normalizedMediaType is null)
        {
            throw new NotSupportedException(
                $"Native realtime input currently requires audio/pcm, audio/pcmu, or audio/pcma content. " +
                $"Received '{content.MediaType ?? "<unknown>"}'. Decode or transcode submitted audio before using realtime transport.");
        }

        return new DataContent(content.Data, normalizedMediaType)
        {
            Name = content.Name
        };
    }

    private static RealtimeAudioFormat ResolveRealtimeInputAudioFormat(AgentModelTurnRequest request)
    {
        var firstAudio = request.Messages
            .SelectMany(message => message.Contents)
            .OfType<DataContent>()
            .FirstOrDefault(content => AudioContent.IsAudioMediaType(content.MediaType));
        var mediaType = AudioContent.GetRealtimeInputAudioFormatMediaType(firstAudio?.MediaType)
            ?? AudioContent.PcmMediaType;
        var sampleRate = AudioContent.GetSampleRate(firstAudio?.MediaType)
            ?? AudioContent.DefaultRealtimeInputSampleRate;

        return new RealtimeAudioFormat(mediaType, sampleRate);
    }

    private static string CreateUserMessageKey(ChatMessage message)
    {
        if (!string.IsNullOrWhiteSpace(message.MessageId))
        {
            return $"id:{message.MessageId}";
        }

        var builder = new System.Text.StringBuilder(message.Role.Value);
        foreach (var content in message.Contents)
        {
            builder.Append('|').Append(content.GetType().FullName);
            switch (content)
            {
                case TextContent text:
                    builder.Append(':').Append(text.Text);
                    break;
                case DataContent data:
                    builder.Append(':')
                        .Append(data.MediaType)
                        .Append(':')
                        .Append(data.Name)
                        .Append(':')
                        .Append(data.Data.Length);
                    break;
            }
        }

        return builder.ToString();
    }

    private static IEnumerable<AgentModelUpdate> MapServerMessage(RealtimeServerMessage message)
    {
        if (message is InputAudioTranscriptionRealtimeServerMessage transcription)
        {
            yield return MapInputTranscription(transcription);
            yield break;
        }

        if (message is OutputTextAudioRealtimeServerMessage output)
        {
            foreach (var update in MapOutput(output))
            {
                yield return update;
            }

            yield break;
        }

        if (message is ResponseOutputItemRealtimeServerMessage itemMessage)
        {
            var yieldedFinalToolCall = false;
            foreach (var content in itemMessage.Item?.Contents ?? [])
            {
                if (content is FunctionCallContent functionCall)
                {
                    var isFinal = itemMessage.Type == RealtimeServerMessageType.ResponseOutputItemDone;
                    yield return new AgentToolCallUpdate(
                        functionCall,
                        IsFinal: isFinal,
                        ResponseId: itemMessage.ResponseId);
                    yieldedFinalToolCall |= isFinal;
                }
            }

            if (yieldedFinalToolCall)
            {
                yield break;
            }

            yield break;
        }

        if (message is ResponseCreatedRealtimeServerMessage response)
        {
            yield return new AgentResponseLifecycleUpdate(
                MapResponseState(response),
                response.ResponseId,
                ToException(response.Error));

            if (response.Usage is not null)
            {
                yield return new AgentUsageUpdate(response.Usage);
            }

            yield break;
        }

        if (message is ErrorRealtimeServerMessage error)
        {
            yield return new AgentResponseLifecycleUpdate(
                AgentModelResponseState.Failed,
                error.OriginatingMessageId,
                ToException(error.Error) ?? new InvalidOperationException("Realtime provider error."));
        }
    }

    private static AgentInputTranscriptUpdate MapInputTranscription(
        InputAudioTranscriptionRealtimeServerMessage message)
        => new(
            message.Transcription ?? string.Empty,
            message.Type == RealtimeServerMessageType.InputAudioTranscriptionCompleted
                ? AgentInputTranscriptStage.Final
                : message.Type == RealtimeServerMessageType.InputAudioTranscriptionFailed
                    ? AgentInputTranscriptStage.Failed
                    : AgentInputTranscriptStage.Partial,
            message.ItemId,
            message.ContentIndex,
            ToException(message.Error));

    private static IEnumerable<AgentModelUpdate> MapOutput(OutputTextAudioRealtimeServerMessage message)
    {
        if (message.Type == RealtimeServerMessageType.OutputTextDelta ||
            message.Type == RealtimeServerMessageType.OutputAudioTranscriptionDelta)
        {
            yield return new AgentTextDeltaUpdate(
                message.Text ?? string.Empty,
                message.ResponseId);
            yield break;
        }

        if (message.Type == RealtimeServerMessageType.OutputTextDone ||
            message.Type == RealtimeServerMessageType.OutputAudioTranscriptionDone)
        {
            yield return new AgentTextDeltaUpdate(
                string.Empty,
                message.ResponseId,
                IsFinal: true);
            yield break;
        }

        if (message.Type == RealtimeServerMessageType.OutputAudioDelta ||
            message.Type == RealtimeServerMessageType.OutputAudioDone)
        {
            var bytes = string.IsNullOrWhiteSpace(message.Audio)
                ? ReadOnlyMemory<byte>.Empty
                : Convert.FromBase64String(message.Audio);

            yield return new AgentAudioDeltaUpdate(
                bytes,
                MediaType: null,
                message.ResponseId,
                IsFinal: message.Type == RealtimeServerMessageType.OutputAudioDone);
        }
    }

    private static AgentModelResponseState MapResponseState(ResponseCreatedRealtimeServerMessage response)
    {
        if (response.Type == RealtimeServerMessageType.ResponseCreated)
        {
            return AgentModelResponseState.Created;
        }

        if (response.Type != RealtimeServerMessageType.ResponseDone)
        {
            return AgentModelResponseState.InProgress;
        }

        return response.Status switch
        {
            null or "" => AgentModelResponseState.Completed,
            var status when string.Equals(status, RealtimeResponseStatus.Completed, StringComparison.OrdinalIgnoreCase)
                => AgentModelResponseState.Completed,
            var status when string.Equals(status, RealtimeResponseStatus.Cancelled, StringComparison.OrdinalIgnoreCase)
                => AgentModelResponseState.Cancelled,
            var status when string.Equals(status, RealtimeResponseStatus.Failed, StringComparison.OrdinalIgnoreCase)
                => AgentModelResponseState.Failed,
            var status when string.Equals(status, RealtimeResponseStatus.Incomplete, StringComparison.OrdinalIgnoreCase)
                => AgentModelResponseState.Incomplete,
            _ => AgentModelResponseState.Completed
        };
    }

    private static Exception? ToException(ErrorContent? error)
    {
        if (error is null)
        {
            return null;
        }

        var message = string.IsNullOrWhiteSpace(error.ErrorCode)
            ? error.Message
            : $"{error.ErrorCode}: {error.Message}";

        if (!string.IsNullOrWhiteSpace(error.Details))
        {
            message = $"{message} {error.Details}";
        }

        return new InvalidOperationException(message);
    }
}
