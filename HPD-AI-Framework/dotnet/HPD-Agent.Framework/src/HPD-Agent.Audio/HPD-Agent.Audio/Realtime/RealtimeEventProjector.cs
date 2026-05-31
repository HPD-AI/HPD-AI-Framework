// Copyright 2026 Einstein Essibu
// SPDX-License-Identifier: AGPL-3.0-only

using HPD.Events;
using HPD.Events.Struct;
using Microsoft.Extensions.AI;

namespace HPD.Agent.Audio.Realtime;

/// <summary>
/// Projects Microsoft.Extensions.AI realtime server messages into HPD runtime events.
/// </summary>
internal sealed class RealtimeEventProjector
{
    private readonly IEventCoordinator _events;
    private readonly SequencedStructEventEmitter<AudioOutputFrame> _audioFrames;
    private readonly string _agentId;
    private readonly Func<RealtimeProjectionScope> _scopeProvider;
    private readonly Func<AgentEvent, CancellationToken, ValueTask>? _afterEmitAsync;
    private readonly Func<string?, IContentStore?>? _contentStoreProvider;
    private readonly string? _provider;
    private readonly string? _model;
    private readonly Dictionary<string, ResponseState> _responses = new(StringComparer.Ordinal);

    public RealtimeEventProjector(
        IEventCoordinator events,
        IStructEventHub structEvents,
        string agentId,
        string? sessionId = null,
        string? branchId = null,
        Func<RealtimeProjectionScope>? scopeProvider = null,
        Func<AgentEvent, CancellationToken, ValueTask>? afterEmitAsync = null,
        Func<string?, IContentStore?>? contentStoreProvider = null,
        string? provider = null,
        string? model = null)
    {
        _events = events ?? throw new ArgumentNullException(nameof(events));
        ArgumentNullException.ThrowIfNull(structEvents);
        _agentId = string.IsNullOrWhiteSpace(agentId) ? "Agent" : agentId;
        _scopeProvider = scopeProvider ?? (() => new RealtimeProjectionScope(sessionId, branchId));
        _afterEmitAsync = afterEmitAsync;
        _contentStoreProvider = contentStoreProvider;
        _provider = provider;
        _model = model;
        _audioFrames = structEvents.Route<AudioOutputFrame>().CreateSequencedEmitter();
    }

    public void Project(RealtimeServerMessage message)
        => ProjectAsync(message, CancellationToken.None).AsTask().GetAwaiter().GetResult();

    public async ValueTask ProjectAsync(
        RealtimeServerMessage message,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(message);

        switch (message)
        {
            case InputAudioTranscriptionRealtimeServerMessage transcription:
                await ProjectInputTranscriptionAsync(transcription, cancellationToken).ConfigureAwait(false);
                break;

            case OutputTextAudioRealtimeServerMessage output:
                await ProjectOutputAsync(output, cancellationToken).ConfigureAwait(false);
                break;

            case ResponseCreatedRealtimeServerMessage response:
                await ProjectResponseAsync(response, cancellationToken).ConfigureAwait(false);
                break;

            case ErrorRealtimeServerMessage error:
                await ProjectErrorAsync(error, cancellationToken).ConfigureAwait(false);
                break;
        }
    }

    private async ValueTask ProjectErrorAsync(
        ErrorRealtimeServerMessage message,
        CancellationToken cancellationToken)
    {
        var errorCode = string.IsNullOrWhiteSpace(message.Error?.ErrorCode)
            ? "realtime_error"
            : message.Error.ErrorCode!;
        var errorMessage = message.Error?.Message;

        await EmitAsync(new AudioPipelineMetricsEvent(
            "error",
            errorCode,
            1,
            "count")
        {
            Channel = EventChannel.Streaming
        }, cancellationToken).ConfigureAwait(false);

        foreach (var response in _responses.Values.ToArray())
        {
            if (!response.BranchRunStarted)
                continue;

            if (response.AudioStarted)
                await CompleteAudioAsync(response, cancellationToken).ConfigureAwait(false);

            await EmitAsync(new BranchRunCompletedEvent(
                response.RuntimeRunId,
                _agentId,
                false,
                errorCode,
                errorMessage)
            {
                EventFlowId = response.RuntimeRunId
            }, cancellationToken).ConfigureAwait(false);

            _responses.Remove(response.ResponseKey);
        }
    }

    private async ValueTask ProjectInputTranscriptionAsync(
        InputAudioTranscriptionRealtimeServerMessage message,
        CancellationToken cancellationToken)
    {
        var transcriptionId = message.ItemId ?? message.MessageId ?? Guid.NewGuid().ToString("N");

        if (message.Type == RealtimeServerMessageType.InputAudioTranscriptionDelta)
        {
            await EmitAsync(new TranscriptionDeltaEvent(
                transcriptionId,
                message.Transcription ?? string.Empty,
                false,
                null)
            {
                Channel = EventChannel.Streaming
            }, cancellationToken).ConfigureAwait(false);
            return;
        }

        if (message.Type == RealtimeServerMessageType.InputAudioTranscriptionCompleted)
        {
            var text = message.Transcription ?? string.Empty;
            await EmitAsync(new TranscriptionDeltaEvent(transcriptionId, text, true, null)
            {
                Channel = EventChannel.Streaming
            }, cancellationToken).ConfigureAwait(false);
            await EmitAsync(new TranscriptionCompletedEvent(transcriptionId, text, TimeSpan.Zero)
            {
                Channel = EventChannel.Synchronous
            }, cancellationToken).ConfigureAwait(false);
            return;
        }

        if (message.Type == RealtimeServerMessageType.InputAudioTranscriptionFailed)
        {
            await EmitAsync(new AudioPipelineMetricsEvent(
                "error",
                string.IsNullOrWhiteSpace(message.Error?.ErrorCode)
                    ? "realtime_transcription_failed"
                    : message.Error.ErrorCode!,
                1,
                "count")
            {
                Channel = EventChannel.Streaming
            }, cancellationToken).ConfigureAwait(false);
        }
    }

    private async ValueTask ProjectOutputAsync(
        OutputTextAudioRealtimeServerMessage message,
        CancellationToken cancellationToken)
    {
        var response = GetOrCreateResponse(message.ResponseId);
        await EnsureBranchRunStartedAsync(response, cancellationToken).ConfigureAwait(false);
        var messageId = message.ItemId ?? response.TextMessageId;

        if (message.Type == RealtimeServerMessageType.OutputTextDelta)
        {
            if (!response.TextStarted)
            {
                await EmitAsync(new TextMessageStartEvent(messageId, ChatRole.Assistant.Value)
                {
                    Channel = EventChannel.Streaming,
                    EventFlowId = response.RuntimeRunId
                }, cancellationToken).ConfigureAwait(false);
                response.TextStarted = true;
            }

            await EmitAsync(new TextDeltaEvent(message.Text ?? string.Empty, messageId)
            {
                Channel = EventChannel.Streaming,
                EventFlowId = response.RuntimeRunId
            }, cancellationToken).ConfigureAwait(false);
            return;
        }

        if (message.Type == RealtimeServerMessageType.OutputTextDone)
        {
            if (response.TextStarted)
            {
                await EmitAsync(new TextMessageEndEvent(messageId)
                {
                    Channel = EventChannel.Streaming,
                    EventFlowId = response.RuntimeRunId
                }, cancellationToken).ConfigureAwait(false);
            }
            return;
        }

        if (message.Type == RealtimeServerMessageType.OutputAudioTranscriptionDelta)
        {
            var text = message.Text ?? string.Empty;
            response.OutputAudioTranscript.Append(text);

            await EmitAsync(new TranscriptionDeltaEvent(
                response.OutputAudioTranscriptionId,
                text,
                false,
                null)
            {
                Channel = EventChannel.Streaming,
                EventFlowId = response.RuntimeRunId
            }, cancellationToken).ConfigureAwait(false);
            return;
        }

        if (message.Type == RealtimeServerMessageType.OutputAudioTranscriptionDone)
        {
            var finalText = message.Text ?? response.OutputAudioTranscript.ToString();
            if (message.Text is { Length: > 0 })
            {
                response.OutputAudioTranscript.Clear();
                response.OutputAudioTranscript.Append(message.Text);
            }

            await EmitAsync(new TranscriptionDeltaEvent(
                response.OutputAudioTranscriptionId,
                finalText,
                true,
                null)
            {
                Channel = EventChannel.Streaming,
                EventFlowId = response.RuntimeRunId
            }, cancellationToken).ConfigureAwait(false);
            await EmitAsync(new TranscriptionCompletedEvent(
                response.OutputAudioTranscriptionId,
                finalText,
                TimeSpan.Zero)
            {
                Channel = EventChannel.Synchronous,
                EventFlowId = response.RuntimeRunId
            }, cancellationToken).ConfigureAwait(false);
            return;
        }

        if (message.Type == RealtimeServerMessageType.OutputAudioDelta)
        {
            if (!response.AudioStarted)
            {
                await EmitAsync(new SynthesisStartedEvent(response.SynthesisId, null, null)
                {
                    Channel = EventChannel.Synchronous,
                    EventFlowId = response.RuntimeRunId
                }, cancellationToken).ConfigureAwait(false);
                response.AudioStarted = true;
            }

            if (string.IsNullOrWhiteSpace(message.Audio))
                return;

            var audioBytes = Convert.FromBase64String(message.Audio);
            var mimeType = message.RawRepresentation is DataContent data && !string.IsNullOrWhiteSpace(data.MediaType)
                ? data.MediaType!
                : "audio/pcm";
            response.AudioMimeType ??= mimeType;
            response.AudioBytes.Add(audioBytes);
            var chunk = new AudioChunkEvent(
                response.SynthesisId,
                message.Audio,
                mimeType,
                response.AudioChunkIndex++,
                TimeSpan.Zero,
                false)
            {
                Channel = EventChannel.Streaming,
                EventFlowId = response.RuntimeRunId,
                CanInterrupt = true
            };

            await EmitAsync(chunk, cancellationToken).ConfigureAwait(false);
            _audioFrames.Emit(new AudioOutputFrame(
                response.SynthesisId,
                audioBytes,
                mimeType,
                chunk.ChunkIndex,
                TimeSpan.Zero,
                false));
            return;
        }

        if (message.Type == RealtimeServerMessageType.OutputAudioDone && response.AudioStarted)
        {
            await CompleteAudioAsync(response, cancellationToken).ConfigureAwait(false);
        }
    }

    private async ValueTask ProjectResponseAsync(
        ResponseCreatedRealtimeServerMessage message,
        CancellationToken cancellationToken)
    {
        var response = GetOrCreateResponse(message.ResponseId);

        if (message.Type == RealtimeServerMessageType.ResponseCreated)
        {
            await EnsureBranchRunStartedAsync(response, cancellationToken).ConfigureAwait(false);
            return;
        }

        if (message.Type == RealtimeServerMessageType.ResponseDone)
        {
            response.Cancelled = string.Equals(
                message.Status,
                RealtimeResponseStatus.Cancelled,
                StringComparison.OrdinalIgnoreCase);

            if (response.Cancelled)
                await EmitInterruptionAsync(response, message.Error?.Message, cancellationToken).ConfigureAwait(false);

            if (response.AudioStarted)
            {
                await CompleteAudioAsync(response, cancellationToken).ConfigureAwait(false);
                await UploadRealtimeAudioArtifactAsync(response, cancellationToken).ConfigureAwait(false);
            }

            await EmitAsync(new BranchRunCompletedEvent(
                response.RuntimeRunId,
                _agentId,
                response.Cancelled,
                ResolveResponseErrorType(message),
                message.Error?.Message)
            {
                EventFlowId = response.RuntimeRunId
            }, cancellationToken).ConfigureAwait(false);

            _responses.Remove(response.ResponseKey);
        }
    }

    private async ValueTask EnsureBranchRunStartedAsync(
        ResponseState response,
        CancellationToken cancellationToken)
    {
        if (response.BranchRunStarted)
            return;

        response.BranchRunStarted = true;
        await EmitAsync(new BranchRunStartedEvent(response.RuntimeRunId, _agentId, DateTimeOffset.UtcNow)
        {
            EventFlowId = response.RuntimeRunId
        }, cancellationToken).ConfigureAwait(false);
    }

    private async ValueTask EmitInterruptionAsync(
        ResponseState response,
        string? transcribedText,
        CancellationToken cancellationToken)
    {
        if (response.InterruptionEmitted)
            return;

        response.InterruptionEmitted = true;
        await EmitAsync(new UserInterruptedEvent(transcribedText)
        {
            Channel = EventChannel.Control,
            EventFlowId = response.RuntimeRunId
        }, cancellationToken).ConfigureAwait(false);
    }

    private static string? ResolveResponseErrorType(ResponseCreatedRealtimeServerMessage message)
    {
        if (!string.IsNullOrWhiteSpace(message.Error?.ErrorCode))
            return message.Error.ErrorCode;

        if (string.Equals(message.Status, RealtimeResponseStatus.Failed, StringComparison.OrdinalIgnoreCase))
            return "realtime_response_failed";

        if (string.Equals(message.Status, RealtimeResponseStatus.Incomplete, StringComparison.OrdinalIgnoreCase))
            return "realtime_response_incomplete";

        return null;
    }

    private async ValueTask CompleteAudioAsync(
        ResponseState response,
        CancellationToken cancellationToken)
    {
        if (response.AudioCompleted)
            return;

        response.AudioCompleted = true;
        await EmitAsync(new SynthesisCompletedEvent(
            response.SynthesisId,
            response.Cancelled,
            response.AudioChunkIndex,
            response.AudioChunkIndex)
        {
            Channel = EventChannel.Control,
            EventFlowId = response.RuntimeRunId,
            CanInterrupt = false
        }, cancellationToken).ConfigureAwait(false);
    }

    private async ValueTask UploadRealtimeAudioArtifactAsync(
        ResponseState response,
        CancellationToken cancellationToken)
    {
        if (response.AudioBytes.Count == 0 ||
            response.ArtifactUploaded ||
            _contentStoreProvider == null)
        {
            return;
        }

        var scope = _scopeProvider();
        if (string.IsNullOrWhiteSpace(scope.SessionId))
            return;

        var contentStore = _contentStoreProvider(scope.SessionId);
        if (contentStore == null)
            return;

        try
        {
            response.ArtifactUploaded = true;
            var totalLength = response.AudioBytes.Sum(static bytes => bytes.Length);
            var assembled = new byte[totalLength];
            var offset = 0;
            foreach (var chunk in response.AudioBytes)
            {
                Buffer.BlockCopy(chunk, 0, assembled, offset, chunk.Length);
                offset += chunk.Length;
            }

            await contentStore.WriteBytesAsync(
                scope: scope.SessionId,
                data: assembled,
                metadata: new ContentMetadata
                {
                    ContentType = response.AudioMimeType ?? "audio/pcm",
                    Origin = ContentSource.Agent,
                    Tags = new Dictionary<string, string>
                    {
                        ["folder"] = "/artifacts",
                        ["audio-role"] = "realtime",
                        ["provider"] = _provider ?? string.Empty,
                        ["model"] = _model ?? string.Empty,
                        ["response-id"] = response.ResponseKey,
                        ["synthesis-id"] = response.SynthesisId,
                        ["interrupted"] = response.Cancelled ? "true" : "false"
                    }
                },
                options: new ContentWriteOptions { Mode = ContentWriteMode.Create },
                cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            // Realtime audio output is still emitted live if artifact persistence fails.
        }
    }

    private ResponseState GetOrCreateResponse(string? responseId)
    {
        var key = string.IsNullOrWhiteSpace(responseId)
            ? "default"
            : responseId;

        if (_responses.TryGetValue(key, out var response))
            return response;

        response = new ResponseState(
            key,
            key == "default" ? Guid.NewGuid().ToString("N") : key,
            Guid.NewGuid().ToString("N"),
            Guid.NewGuid().ToString("N")[..8]);
        _responses[key] = response;
        return response;
    }

    private async ValueTask EmitAsync(AgentEvent evt, CancellationToken cancellationToken)
    {
        var scope = _scopeProvider();
        var scoped = evt with
        {
            SessionId = evt.SessionId ?? scope.SessionId,
            BranchId = evt.BranchId ?? scope.BranchId
        };

        _events.Emit(scoped);

        if (_afterEmitAsync != null)
            await _afterEmitAsync(scoped, cancellationToken).ConfigureAwait(false);
    }

    private sealed class ResponseState(
        string responseKey,
        string runtimeRunId,
        string textMessageId,
        string synthesisId)
    {
        public string ResponseKey { get; } = responseKey;
        public string RuntimeRunId { get; } = runtimeRunId;
        public string TextMessageId { get; } = textMessageId;
        public string SynthesisId { get; } = synthesisId;
        public string OutputAudioTranscriptionId { get; } = $"{synthesisId}:transcript";
        public System.Text.StringBuilder OutputAudioTranscript { get; } = new();
        public bool BranchRunStarted { get; set; }
        public bool TextStarted { get; set; }
        public bool AudioStarted { get; set; }
        public bool AudioCompleted { get; set; }
        public bool ArtifactUploaded { get; set; }
        public bool Cancelled { get; set; }
        public bool InterruptionEmitted { get; set; }
        public string? AudioMimeType { get; set; }
        public List<byte[]> AudioBytes { get; } = [];
        public int AudioChunkIndex { get; set; }
    }
}

internal readonly record struct RealtimeProjectionScope(string? SessionId, string? BranchId);
