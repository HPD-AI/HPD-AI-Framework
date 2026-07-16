using HPD.Agent;
using HPD.Agent.Audio;
using HPD.Agent.Audio.Output;
using HPD.Agent.Audio.Ledger;
using HPD.Agent.Audio.Runtime.Output;
using HPD.Agent.Audio.Trace;
using Microsoft.Extensions.AI;

namespace HPD.Agent.Audio.AgentIntegration.Output;

#pragma warning disable MEAI001

internal sealed class ProgressiveOutputCoordinator
{
    private readonly IProgressiveTextToSpeechEngine _engine;
    private readonly IOutputFlow _flow;
    private readonly ProgressiveOutputCoordinatorOptions _options;
    private readonly OutputLedgerTraceWriter _ledgerTraceWriter = new();

    public ProgressiveOutputCoordinator(ProgressiveOutputCoordinatorOptions options)
        : this(
            options,
            new InMemoryOutputFlow(options.OutputFlowId),
            new ProgressiveTextToSpeechEngineFactory())
    {
    }

    internal ProgressiveOutputCoordinator(
        ProgressiveOutputCoordinatorOptions options,
        IOutputFlow flow,
        ProgressiveTextToSpeechEngineFactory engineFactory)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _flow = flow ?? throw new ArgumentNullException(nameof(flow));
        _engine = (engineFactory ?? throw new ArgumentNullException(nameof(engineFactory)))
            .Create(options, flow);
    }

    public OutputFlowId OutputFlowId => _engine.OutputFlowId;

    public void Start(CancellationToken cancellationToken)
    {
        _engine.StartAsync(cancellationToken).AsTask().GetAwaiter().GetResult();
    }

    public ValueTask WriteTextDeltaAsync(
        string textDelta,
        ResponseId responseId,
        CancellationToken cancellationToken)
    {
        return _engine.PushTextAsync(textDelta, responseId, cancellationToken);
    }

    public async ValueTask<ProgressiveOutputCompletion> CompleteInputAsync(
        ResponseId responseId,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var engineCompletion = await _engine.CompleteAsync(responseId, cancellationToken)
            .ConfigureAwait(false);
        OutputCommitRecord? commit = null;
        var ledger = new List<RealtimeLedgerRecord>();
        var trace = new List<RealtimeAudioTraceRecord>();
        var results = engineCompletion.Results;
        var hasGeneratedText = !string.IsNullOrWhiteSpace(_flow.Snapshot.Text);

        if (hasGeneratedText)
        {
            var synthesized = results.Any(result => result.Status == AssistantTextToSpeechOutputStatus.SynthesizedNotPlayed);
            if (synthesized &&
                _options.EnablePlayback &&
                _options.OutputSink is not null)
            {
                var playback = new OutputPlaybackCoordinator(new OutputPlaybackCoordinatorOptions
                {
                    SessionId = _options.SessionId,
                    Sink = _options.OutputSink,
                    EventFlowHandle = _options.EventFlowHandle,
                    PublishEventAsync = _options.PublishEventAsync,
                    StructEvents = _options.StructEvents,
                    CaptureStructEventSamplesInTrace = _options.CaptureStructEventSamplesInTrace
                }, _flow);

                commit = await playback.DrainPlaybackEventsAsync(cancellationToken).ConfigureAwait(false);
                if (commit is null)
                {
                    foreach (var failure in engineCompletion.PlaybackStartFailures)
                    {
                        commit = await playback.ApplyPlaybackEventAsync(failure, cancellationToken)
                            .ConfigureAwait(false);
                        if (commit is not null)
                        {
                            break;
                        }
                    }
                }

                ledger.AddRange(playback.Ledger);
                trace.AddRange(playback.Trace);
            }

            commit ??= synthesized
                ? await _flow.CompleteSynthesizedNotPlayedAsync(cancellationToken).ConfigureAwait(false)
                : await _flow.CompleteTextOnlyAsync(
                    "Progressive TTS produced no synthesized audio; assistant text remains available.",
                    cancellationToken).ConfigureAwait(false);

            _ledgerTraceWriter.AppendAssistantOutput(
                ledger,
                trace,
                _options.SessionId,
                CreateCorrelation(),
                _flow.Id,
                responseId,
                commit.Text,
                ToOutputDisposition(commit.Disposition));
        }

        await PublishCompletedAsync(responseId, results, commit, cancellationToken).ConfigureAwait(false);
        return new ProgressiveOutputCompletion
        {
            SessionId = _options.SessionId,
            OutputFlowId = _flow.Id,
            ResponseId = responseId,
            Commit = commit,
            Ledger = ledger,
            Trace = trace,
            Results = results
        };
    }

    public void Cancel(Exception? exception = null)
    {
        _engine.Cancel(exception);
    }

    private AudioCorrelation CreateCorrelation()
    {
        return new AudioCorrelation
        {
            ConversationId = _options.Thread.SessionId,
            RequestId = _options.RequestId,
            SessionId = _options.SessionId,
            OutputFlowId = _flow.Id
        };
    }

    private async ValueTask PublishCompletedAsync(
        ResponseId responseId,
        IReadOnlyList<AssistantTextToSpeechOutputResult> results,
        OutputCommitRecord? commit,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(_flow.Snapshot.Text) && results.Count == 0)
        {
            return;
        }

        var synthesized = results.Count(result =>
            result.Status == AssistantTextToSpeechOutputStatus.SynthesizedNotPlayed);
        var disposition = commit?.Disposition.ToString() ??
            (synthesized > 0
                ? nameof(AssistantTextToSpeechOutputStatus.SynthesizedNotPlayed)
                : nameof(AssistantTextToSpeechOutputStatus.SynthesisFailedTextOnly));
        var played = OutputWasHeard(commit);

        if (_options.PublishEventAsync is { } publish)
        {
            await publish(new AssistantAudioOutputCompletedEvent(
            _options.SessionId.Value,
            _flow.Id.Value,
            responseId.Value,
            disposition,
            synthesized,
            Played: played,
            HeardByUser: played), cancellationToken).ConfigureAwait(false);
        }
    }

    private static bool OutputWasHeard(OutputCommitRecord? commit)
    {
        if (commit is null)
        {
            return false;
        }

        return commit.Disposition switch
        {
            OutputCommitDisposition.PlayedComplete or OutputCommitDisposition.PlayedPartial => true,
            OutputCommitDisposition.Interrupted =>
                commit.PlaybackBoundary?.PlayedTextLength > 0 ||
                commit.PlaybackBoundary?.PlayedDuration > TimeSpan.Zero,
            _ => false
        };
    }

    private static OutputDisposition ToOutputDisposition(OutputCommitDisposition disposition)
    {
        return disposition switch
        {
            OutputCommitDisposition.Interrupted => OutputDisposition.Interrupted,
            OutputCommitDisposition.Canceled => OutputDisposition.Canceled,
            OutputCommitDisposition.Failed => OutputDisposition.Failed,
            OutputCommitDisposition.TextOnly => OutputDisposition.TextOnly,
            OutputCommitDisposition.SynthesizedNotPlayed => OutputDisposition.SynthesizedNotPlayed,
            OutputCommitDisposition.SynthesisFailedTextOnly => OutputDisposition.SynthesisFailedTextOnly,
            OutputCommitDisposition.SegmentSynthesized => OutputDisposition.SegmentSynthesized,
            OutputCommitDisposition.SegmentFailedTextOnly => OutputDisposition.SegmentFailedTextOnly,
            OutputCommitDisposition.PlayedPartial => OutputDisposition.PlayedPartial,
            OutputCommitDisposition.PlayedComplete => OutputDisposition.PlayedComplete,
            OutputCommitDisposition.QueuedUnplayed => OutputDisposition.QueuedUnplayed,
            OutputCommitDisposition.PlaybackFailed => OutputDisposition.PlaybackFailed,
            _ => OutputDisposition.Failed
        };
    }
}

internal sealed record ProgressiveOutputCoordinatorOptions
{
    public required AudioSessionId SessionId { get; init; }

    public required ThreadRef Thread { get; init; }

    public required OutputFlowId OutputFlowId { get; init; }

    public required ResponseId InitialResponseId { get; init; }

    public required TextToSpeechPacingOptions PacingOptions { get; init; }

    public required AssistantTextToSpeechOutputOptions? OutputOptions { get; init; }

    public ProgressiveTextToSpeechRouteMode RouteMode { get; init; }

    public PushTextInputAggregationMode PushTextAggregationMode { get; init; } =
        PushTextInputAggregationMode.ProviderDefault;

    public string? RequestId { get; init; }

    public Func<AgentEvent, CancellationToken, ValueTask<AgentEvent>>? PublishEventAsync { get; init; }

    public IAudioOutputSink? OutputSink { get; init; }

    public bool EnablePlayback { get; init; }

    public HPD.Events.IEventFlowHandle? EventFlowHandle { get; init; }

    public HPD.Events.Struct.IStructEventHub? StructEvents { get; init; }

    public bool CaptureStructEventSamplesInTrace { get; init; }
}

internal sealed record ProgressiveOutputCompletion
{
    public required AudioSessionId SessionId { get; init; }

    public required OutputFlowId OutputFlowId { get; init; }

    public required ResponseId ResponseId { get; init; }

    public OutputCommitRecord? Commit { get; init; }

    public IReadOnlyList<RealtimeLedgerRecord> Ledger { get; init; } = [];

    public IReadOnlyList<RealtimeAudioTraceRecord> Trace { get; init; } = [];

    public required IReadOnlyList<AssistantTextToSpeechOutputResult> Results { get; init; }
}
