using HPD.Agent;
using HPD.Agent.Audio;
using HPD.Agent.Audio.Output;
using HPD.Agent.Audio.Ledger;
using HPD.Agent.Audio.Runtime.Output;
using HPD.Agent.Audio.Trace;
using Microsoft.Extensions.AI;

namespace HPD.Agent.Audio.AgentIntegration.Output;

#pragma warning disable MEAI001

public sealed class AssistantFinalTextToSpeechOutputService
{
    private readonly ITextToSpeechSegmentSynthesizer _synthesizer;
    private readonly OutputLedgerTraceWriter _ledgerTraceWriter;

    public AssistantFinalTextToSpeechOutputService()
        : this(new TextToSpeechSegmentSynthesizer(), new OutputLedgerTraceWriter())
    {
    }

    internal AssistantFinalTextToSpeechOutputService(
        ITextToSpeechSegmentSynthesizer synthesizer,
        OutputLedgerTraceWriter ledgerTraceWriter)
    {
        _synthesizer = synthesizer ?? throw new ArgumentNullException(nameof(synthesizer));
        _ledgerTraceWriter = ledgerTraceWriter ?? throw new ArgumentNullException(nameof(ledgerTraceWriter));
    }

    public async ValueTask<AssistantTextToSpeechOutputResult> RunAsync(
        AssistantFinalTextToSpeechOutputRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        var outputFlowId = request.OutputFlowId ?? new OutputFlowId($"output-{Guid.NewGuid():N}");
        var responseId = request.ResponseId ?? new ResponseId($"response-{Guid.NewGuid():N}");
        var correlation = new AudioCorrelation
        {
            ConversationId = request.Thread.SessionId,
            RequestId = request.RequestId,
            SessionId = request.SessionId,
            OutputFlowId = outputFlowId
        };
        var ledger = new List<RealtimeLedgerRecord>();
        var trace = new List<RealtimeAudioTraceRecord>();
        var flow = new InMemoryOutputFlow(outputFlowId);

        if (string.IsNullOrWhiteSpace(request.Text))
        {
            return new AssistantTextToSpeechOutputResult
            {
                SessionId = request.SessionId,
                OutputFlowId = outputFlowId,
                ResponseId = responseId,
                Status = AssistantTextToSpeechOutputStatus.SkippedNoText,
                Ledger = ledger,
                Trace = trace
            };
        }

        await flow.AppendTextAsync(responseId, request.Text, isFinal: true, cancellationToken)
            .ConfigureAwait(false);
        _ledgerTraceWriter.AppendAssistantOutput(
            ledger,
            trace,
            request.SessionId,
            correlation,
            outputFlowId,
            responseId,
            request.Text,
            OutputDisposition.Draft);

        if (request.Options is null)
        {
            var commit = await flow.CompleteTextOnlyAsync(
                "Assistant output synthesis was not requested.",
                cancellationToken).ConfigureAwait(false);
            _ledgerTraceWriter.AppendAssistantOutput(
                ledger,
                trace,
                request.SessionId,
                correlation,
                outputFlowId,
                responseId,
                request.Text,
                OutputDisposition.TextOnly);

            return new AssistantTextToSpeechOutputResult
            {
                SessionId = request.SessionId,
                OutputFlowId = outputFlowId,
                ResponseId = responseId,
                Status = AssistantTextToSpeechOutputStatus.TextOnly,
                Text = request.Text,
                Commit = commit,
                Ledger = ledger,
                Trace = trace
            };
        }

        var textToSpeechRequest = CreateTextToSpeechRequest(
            outputFlowId,
            responseId,
            request.Text,
            request.Options);

        var segmentResult = await _synthesizer.SynthesizeAsync(
            flow,
            textToSpeechRequest,
            new TextToSpeechSynthesisContext
            {
                SessionId = request.SessionId,
                Thread = request.Thread,
                Correlation = correlation,
                Options = request.Options,
                EmitEvent = request.EmitEvent
            },
            cancellationToken).ConfigureAwait(false);
        var outputResult = segmentResult.ToOutputResult(request.SessionId);
        ledger.AddRange(outputResult.Ledger);
        trace.AddRange(outputResult.Trace);

        if (segmentResult.Disposition == TtsSynthesisDisposition.Synthesized)
        {
            var commit = await flow.CompleteSynthesizedNotPlayedAsync(cancellationToken)
                .ConfigureAwait(false);
            _ledgerTraceWriter.AppendAssistantOutput(
                ledger,
                trace,
                request.SessionId,
                correlation,
                outputFlowId,
                responseId,
                request.Text,
                OutputDisposition.SynthesizedNotPlayed);

            return outputResult with
            {
                Text = request.Text,
                Commit = commit,
                Ledger = ledger,
                Trace = trace
            };
        }

        var failedCommit = await flow.CompleteTextOnlyAsync(
            "TTS synthesis failed; assistant text remains available.",
            cancellationToken).ConfigureAwait(false);
        _ledgerTraceWriter.AppendAssistantOutput(
            ledger,
            trace,
            request.SessionId,
            correlation,
            outputFlowId,
            responseId,
            request.Text,
            OutputDisposition.SynthesisFailedTextOnly);

        return outputResult with
        {
            Text = request.Text,
            Commit = failedCommit with { Disposition = OutputCommitDisposition.SynthesisFailedTextOnly },
            Ledger = ledger,
            Trace = trace
        };
    }

    private static TextToSpeechSegmentRequest CreateTextToSpeechRequest(
        OutputFlowId outputFlowId,
        ResponseId responseId,
        string text,
        AssistantTextToSpeechOutputOptions options)
    {
        return new TextToSpeechSegmentRequest
        {
            ResponseId = responseId,
            Text = text,
            SegmentId = new OutputSegmentId($"{outputFlowId.Value}:tts-0000"),
            SegmentIndex = 0,
            IsFinalSegment = true,
            SourceTextStart = 0,
            SourceTextLength = text.Length,
            ProviderKey = ResolveProviderKey(options),
            ModelId = options.ModelId,
            VoiceId = options.VoiceId,
            Language = options.Language,
            OutputFormat = options.OutputFormat,
            ContentType = options.ContentType
        };
    }

    private static string ResolveProviderKey(AssistantTextToSpeechOutputOptions options)
    {
        if (!string.IsNullOrWhiteSpace(options.ProviderKey))
        {
            return options.ProviderKey!;
        }

        var metadata = options.TextToSpeechClient.GetService(typeof(TextToSpeechClientMetadata)) as TextToSpeechClientMetadata;
        return string.IsNullOrWhiteSpace(metadata?.ProviderName) ? "unknown" : metadata!.ProviderName!;
    }
}

public sealed record AssistantFinalTextToSpeechOutputRequest
{
    public required AudioSessionId SessionId { get; init; }

    public required ThreadRef Thread { get; init; }

    public required string Text { get; init; }

    public string? RequestId { get; init; }

    public OutputFlowId? OutputFlowId { get; init; }

    public ResponseId? ResponseId { get; init; }

    public AssistantTextToSpeechOutputOptions? Options { get; init; }

    public Action<AgentEvent>? EmitEvent { get; init; }
}
