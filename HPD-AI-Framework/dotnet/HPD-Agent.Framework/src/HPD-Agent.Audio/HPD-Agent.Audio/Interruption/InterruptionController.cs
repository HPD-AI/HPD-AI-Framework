// Copyright 2026 Einstein Essibu
// SPDX-License-Identifier: AGPL-3.0-only

using System.Text.RegularExpressions;
using HPD.Agent.Audio;
using HPD.Agent.Audio.Output;
using HPD.Agent.Audio.Recognition;

namespace HPD.Agent.Audio.Interruption;

/// <summary>
/// Decides how user speech should interact with active agent speech output.
/// </summary>
public sealed partial class InterruptionController
{
    private readonly InterruptionControllerOptions _options;
    private SpeechOutputContext? _activeOutput;
    private bool _isPlaying;
    private bool _isPausedForPotentialInterruption;
    private DateTimeOffset? _pauseStartedAt;

    /// <summary>Creates an interruption controller.</summary>
    public InterruptionController(InterruptionControllerOptions? options = null)
    {
        _options = options ?? new InterruptionControllerOptions();
    }

    /// <summary>Current decision state.</summary>
    public InterruptionDecisionState State { get; private set; } = InterruptionDecisionState.NoActiveSpeech;

    /// <summary>Tracks output state relevant to interruption decisions.</summary>
    public InterruptionDecision Process(SpeechOutputEvent evt)
    {
        ArgumentNullException.ThrowIfNull(evt);

        switch (evt)
        {
            case SpeechOutputAudioQueuedEvent:
            case SpeechOutputPlaybackStartedEvent:
            case SpeechOutputPlaybackProgressEvent:
                _activeOutput = evt.Context;
                _isPlaying = true;
                State = InterruptionDecisionState.NoActiveSpeech;
                return Decision(InterruptionAction.None, InterruptionReason.NoActiveSpeech);

            case SpeechOutputPausedEvent:
                _activeOutput = evt.Context;
                _isPlaying = false;
                _isPausedForPotentialInterruption = true;
                _pauseStartedAt = evt.Context.ObservedAt;
                State = InterruptionDecisionState.PotentialInterruption;
                return Decision(InterruptionAction.None, evt is SpeechOutputPausedEvent paused ? paused.Reason : InterruptionReason.UserSpeechDuringPlayback);

            case SpeechOutputResumedEvent:
                _isPlaying = true;
                _isPausedForPotentialInterruption = false;
                _pauseStartedAt = null;
                State = InterruptionDecisionState.Recovered;
                return Decision(InterruptionAction.None, InterruptionReason.FalseInterruptionTimeout);

            case SpeechOutputPlaybackFinishedEvent:
            case SpeechOutputCompletedEvent:
            case SpeechOutputInterruptedEvent:
                _isPlaying = false;
                _isPausedForPotentialInterruption = false;
                _pauseStartedAt = null;
                State = InterruptionDecisionState.NoActiveSpeech;
                return Decision(InterruptionAction.None, InterruptionReason.OutputCompleted);

            default:
                return Decision(InterruptionAction.None, "");
        }
    }

    /// <summary>Processes recognition state relevant to interruption decisions.</summary>
    public InterruptionDecision Process(SpeechRecognitionEvent evt)
    {
        ArgumentNullException.ThrowIfNull(evt);

        return evt switch
        {
            SpeechRecognitionStartedEvent started => OnSpeechStarted(started.Context.ObservedAt),
            SpeechRecognitionInterimEvent interim => OnTranscript(interim.Transcript.Text),
            SpeechRecognitionPreflightEvent preflight => OnTranscript(preflight.Transcript.Text),
            SpeechRecognitionFinalEvent final => OnTranscript(final.Transcript.Text),
            SpeechRecognitionEndedEvent => AdvanceFalseInterruption(evt.Context.ObservedAt),
            _ => Decision(InterruptionAction.None, "")
        };
    }

    /// <summary>Advances false-interruption recovery timers.</summary>
    public InterruptionDecision AdvanceFalseInterruption(DateTimeOffset now)
    {
        if (!_isPausedForPotentialInterruption ||
            _pauseStartedAt is null ||
            now - _pauseStartedAt.Value < _options.FalseInterruptionTimeout)
        {
            return Decision(InterruptionAction.None, "");
        }

        _isPausedForPotentialInterruption = false;
        _pauseStartedAt = null;
        State = InterruptionDecisionState.FalseInterruption;

        return Decision(
            _options.ResumeFalseInterruption ? InterruptionAction.ResumeOutput : InterruptionAction.None,
            InterruptionReason.FalseInterruptionTimeout);
    }

    private InterruptionDecision OnSpeechStarted(DateTimeOffset observedAt)
    {
        if (!_isPlaying && !_isPausedForPotentialInterruption)
        {
            State = InterruptionDecisionState.NoActiveSpeech;
            return Decision(InterruptionAction.None, InterruptionReason.NoActiveSpeech);
        }

        State = InterruptionDecisionState.PotentialInterruption;
        if (_options.EnableFalseInterruptionRecovery)
        {
            _isPlaying = false;
            _isPausedForPotentialInterruption = true;
            _pauseStartedAt = observedAt;
            return Decision(InterruptionAction.PauseOutput, InterruptionReason.UserSpeechDuringPlayback);
        }

        return Confirm(null);
    }

    private InterruptionDecision OnTranscript(string text)
    {
        if (!_isPlaying && !_isPausedForPotentialInterruption)
        {
            State = InterruptionDecisionState.NoActiveSpeech;
            return Decision(InterruptionAction.None, InterruptionReason.NoActiveSpeech, text);
        }

        var backchannelReason = GetBackchannelReason(text);
        if (backchannelReason is not null)
        {
            State = InterruptionDecisionState.PotentialBackchannel;
            if (_isPausedForPotentialInterruption && _options.ResumeFalseInterruption)
            {
                _isPausedForPotentialInterruption = false;
                _isPlaying = true;
                _pauseStartedAt = null;
                return Decision(InterruptionAction.ResumeOutput, backchannelReason, text);
            }

            return Decision(InterruptionAction.None, backchannelReason, text);
        }

        return Confirm(text);
    }

    private InterruptionDecision Confirm(string? text)
    {
        _isPlaying = false;
        _isPausedForPotentialInterruption = false;
        _pauseStartedAt = null;
        State = InterruptionDecisionState.ConfirmedInterruption;
        return Decision(InterruptionAction.InterruptOutput, InterruptionReason.MeaningfulSpeech, text);
    }

    private string? GetBackchannelReason(string text)
    {
        if (_options.BackchannelStrategy == BackchannelStrategy.InterruptImmediately)
            return null;

        if (_options.BackchannelStrategy == BackchannelStrategy.IgnoreKnownBackchannels &&
            IsKnownBackchannel(text))
        {
            return InterruptionReason.KnownBackchannel;
        }

        var wordCount = text.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Length;
        return wordCount < _options.MinWordsForInterruption
            ? InterruptionReason.ShortBackchannel
            : null;
    }

    private InterruptionDecision Decision(
        InterruptionAction action,
        string reason,
        string? transcriptText = null) =>
        new()
        {
            State = State,
            Action = action,
            Reason = reason,
            TranscriptText = transcriptText,
            OutputContext = _activeOutput
        };

    private static bool IsKnownBackchannel(string text)
    {
        var cleaned = text.Trim().ToLowerInvariant();
        return BackchannelMhmRegex().IsMatch(cleaned) ||
            BackchannelFillerRegex().IsMatch(cleaned) ||
            BackchannelAffirmativeRegex().IsMatch(cleaned);
    }

    [GeneratedRegex(@"^(m+hm+|mm+|hm+|uh huh|uh-huh)$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex BackchannelMhmRegex();

    [GeneratedRegex(@"^(uh|um|er|ah|hmm+)$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex BackchannelFillerRegex();

    [GeneratedRegex(@"^(yeah|yep|yes|right|okay|ok|sure)$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex BackchannelAffirmativeRegex();
}
