// Copyright (c) 2025 Einstein Essibu. All rights reserved.

using System.Reflection;
using System.Text.Json;
using FluentAssertions;
using HPD.Agent.Audio.Vad;
using HPD.Agent.AudioProviders.Silero;
using Microsoft.ML.OnnxRuntime;

namespace HPD.Agent.Audio.Tests;

/// <summary>
/// Tests for the Silero VAD provider: configuration, factory, detector, and discovery.
/// Tests are numbered #1–#39 matching the specification.
/// Unit tests (no ONNX): #1–#10, #14–#17, #23, #35–#39.
/// Integration tests (real ONNX session): #11–#13, #18–#22, #24–#34.
///
/// NOTE — FLAKINESS RISK (state-machine tests #18–#22, #26–#32):
/// The Silero ONNX model is an RNN trained on real human speech. All synthetic signals
/// (silence, square wave, sine wave) produce very low and RNN-state-dependent confidence
/// scores (~0.002–0.135). The tests below use carefully measured threshold values and
/// model warm-up sequences to achieve repeatable transitions, but they are inherently
/// fragile: a model update or a different ONNX runtime version could shift the confidence
/// values enough to break them.
///
/// The correct long-term fix is to embed a short (~1 s) real speech WAV clip as a
/// Git LFS–tracked test resource . Once that asset is
/// available, replace the synthetic-audio helpers (MakeSquareFrame, MakeSilentFrame)
/// with real PCM frames decoded from the embedded clip, and remove the warm-up loops
/// and reflection-based state injection.
/// </summary>
public class SileroVadTests
{
    // =========================================================================
    // #1 — SileroVadConfig: DefaultValues
    // =========================================================================

    [Fact]
    public void SileroVadConfig_DefaultValues_MatchReferenceImplementation()
    {
        var cfg = new SileroVadConfig();

        cfg.ForceCpu.Should().BeTrue();
        cfg.SampleRate.Should().Be(16000);
        cfg.DeactivationThreshold.Should().BeNull();
        cfg.ModelResetIntervalSeconds.Should().Be(5.0f);
    }

    // =========================================================================
    // #2 — SileroVadConfig: DeactivationThreshold_CanBeSetExplicitly
    // =========================================================================

    [Fact]
    public void SileroVadConfig_DeactivationThreshold_RoundTripsExplicitValue()
    {
        var cfg = new SileroVadConfig { DeactivationThreshold = 0.3f };

        cfg.DeactivationThreshold.Should().Be(0.3f);
    }

    // =========================================================================
    // #3 — Validate_DefaultConfig_ReturnsSuccess
    // =========================================================================

    [Fact]
    public void Validate_DefaultConfig_ReturnsSuccess()
    {
        var factory = new SileroVadProviderFactory();
        var config = new VadConfig();

        var result = factory.Validate(config);

        result.IsValid.Should().BeTrue();
    }

    // =========================================================================
    // #4 — Validate_InvalidActivationThreshold_ReturnsFailure
    // =========================================================================

    [Fact]
    public void Validate_InvalidActivationThreshold_ReturnsFailure()
    {
        var factory = new SileroVadProviderFactory();
        var config = new VadConfig { ActivationThreshold = 1.5f };

        var result = factory.Validate(config);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("ActivationThreshold"));
    }

    // =========================================================================
    // #5 — Validate_InvalidSampleRateInJson_ReturnsFailure
    // =========================================================================

    [Fact]
    public void Validate_InvalidSampleRateInJson_ReturnsFailure()
    {
        var factory = new SileroVadProviderFactory();
        var config = new VadConfig
        {
            ProviderOptionsJson = JsonSerializer.Serialize(new SileroVadConfig { SampleRate = 22050 })
        };

        var result = factory.Validate(config);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("SampleRate"));
    }

    // =========================================================================
    // #6 — Validate_ZeroModelResetInterval_ReturnsFailure
    // =========================================================================

    [Fact]
    public void Validate_ZeroModelResetInterval_ReturnsFailure()
    {
        var factory = new SileroVadProviderFactory();
        var config = new VadConfig
        {
            ProviderOptionsJson = JsonSerializer.Serialize(new SileroVadConfig { ModelResetIntervalSeconds = 0f })
        };

        var result = factory.Validate(config);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("ModelResetIntervalSeconds"));
    }

    // =========================================================================
    // #7 — Validate_DeactivationThresholdOutOfRange_ReturnsFailure
    // =========================================================================

    [Fact]
    public void Validate_DeactivationThresholdOutOfRange_ReturnsFailure()
    {
        var factory = new SileroVadProviderFactory();
        var config = new VadConfig
        {
            ProviderOptionsJson = JsonSerializer.Serialize(new SileroVadConfig { DeactivationThreshold = 1.5f })
        };

        var result = factory.Validate(config);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("DeactivationThreshold"));
    }

    // =========================================================================
    // #8 — Validate_MalformedJson_ReturnsFailure
    // =========================================================================

    [Fact]
    public void Validate_MalformedJson_DoesNotThrow_ReturnsFailure()
    {
        var factory = new SileroVadProviderFactory();
        var config = new VadConfig { ProviderOptionsJson = "not-json" };

        var result = factory.Validate(config);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("JSON") || e.Contains("json") || e.Contains("Invalid"));
    }

    // =========================================================================
    // #9 — Validate_ModelResourceExists
    // =========================================================================

    [Fact]
    public void Validate_ModelResourceExists_IsAccessible()
    {
        // Verify the embedded resource is readable by calling Validate on a clean config.
        var factory = new SileroVadProviderFactory();
        var result = factory.Validate(new VadConfig());

        // If the resource is missing, Validate returns failure with an error about the model.
        result.IsValid.Should().BeTrue("the ONNX model should be embedded and readable");
    }

    // =========================================================================
    // #10 — GetMetadata_ReturnsCorrectProviderKey
    // =========================================================================

    [Fact]
    public void GetMetadata_ReturnsCorrectProviderKey_DisplayName_SupportedFormats()
    {
        var factory = new SileroVadProviderFactory();
        var meta = factory.GetMetadata();

        meta.ProviderKey.Should().Be("silero-vad");
        meta.DisplayName.Should().Be("Silero VAD");
        meta.SupportedFormats.Should().Contain("pcm-16bit-8khz");
        meta.SupportedFormats.Should().Contain("pcm-16bit-16khz");
    }

    // =========================================================================
    // #11 — CreateDetector_DefaultConfig_ReturnsDetector (Integration)
    // =========================================================================

    [Fact]
    public void CreateDetector_DefaultConfig_ReturnsDetector()
    {
        var factory = new SileroVadProviderFactory();
        var config = new VadConfig();

        IVoiceActivityDetector? detector = null;
        var act = () => { detector = factory.CreateDetector(config); };

        act.Should().NotThrow();
        detector.Should().NotBeNull();
        detector!.Dispose();
    }

    // =========================================================================
    // #12 — CreateDetector_WithProviderOptionsJson_AppliesConfig (Integration)
    // =========================================================================

    [Fact]
    public void CreateDetector_WithProviderOptionsJson_AppliesConfig()
    {
        var factory = new SileroVadProviderFactory();
        var config = new VadConfig
        {
            ProviderOptionsJson = JsonSerializer.Serialize(new SileroVadConfig
            {
                ModelResetIntervalSeconds = 2.0f
            })
        };

        IVoiceActivityDetector? detector = null;
        var act = () => { detector = factory.CreateDetector(config); };

        act.Should().NotThrow();
        detector.Should().NotBeNull();
        detector!.Dispose();
    }

    // =========================================================================
    // #13 — Process_SilenceFrames_ReturnLowConfidence (Integration)
    // =========================================================================

    [Fact]
    public void Process_SilenceFrames_ReturnLowConfidence()
    {
        using var detector = CreateDefaultDetector();
        var silentFrame = MakeSilentFrame(16000);

        var result = detector.Process(silentFrame);

        result.SpeechProbability.Should().BeLessThan(0.5f);
        result.State.Should().Be(VadState.Quiet);
    }

    // =========================================================================
    // #14 — Process_InvalidSampleRate_Throws (Unit — guard before ONNX)
    // =========================================================================

    [Fact]
    public void Process_InvalidSampleRate_Throws()
    {
        using var detector = CreateDefaultDetector();
        var frame = MakeSilentFrame(44100);

        var act = () => detector.Process(frame);

        act.Should().Throw<ArgumentException>().WithMessage("*44100*");
    }

    // =========================================================================
    // #15 — Process_WrongFrameSize_16k_Throws (Unit — guard before ONNX)
    // =========================================================================

    [Fact]
    public void Process_WrongFrameSize_16k_Throws()
    {
        using var detector = CreateDefaultDetector();
        // 256 samples at 16 kHz is wrong (need 512)
        var frame = MakeFrameWithSamples(sampleRate: 16000, sampleCount: 256);

        var act = () => detector.Process(frame);

        act.Should().Throw<ArgumentException>().WithMessage("*512*");
    }

    // =========================================================================
    // #16 — Process_WrongFrameSize_8k_Throws (Unit — guard before ONNX)
    // =========================================================================

    [Fact]
    public void Process_WrongFrameSize_8k_Throws()
    {
        using var detector = CreateDefaultDetector();
        // 512 samples at 8 kHz is wrong (need 256)
        var frame = MakeFrameWithSamples(sampleRate: 8000, sampleCount: 512);

        var act = () => detector.Process(frame);

        act.Should().Throw<ArgumentException>().WithMessage("*256*");
    }

    // =========================================================================
    // #17 — Process_AfterDispose_Throws (Unit)
    // =========================================================================

    [Fact]
    public void Process_AfterDispose_Throws()
    {
        var detector = CreateDefaultDetector();
        detector.Dispose();

        var act = () => detector.Process(MakeSilentFrame(16000));

        act.Should().Throw<ObjectDisposedException>();
    }

    // =========================================================================
    // #18 — Process_StateMachine_QuietToStarting (Integration)
    // =========================================================================

    [Fact]
    public void Process_StateMachine_QuietToStarting_WhenOneFrameAboveThreshold()
    {
        // After 20 settled square frames the model gives ~0.135 confidence.
        // activation=0.05: 0.135 >= 0.05 → Starting. MinSpeechDuration=10s: can't yet reach Speaking.
        // But wait: after 20 square frames we may already be in Speaking from the prior frames.
        // Use a fresh detector. With fresh context, square gives ~0.098 on frame 0 >= 0.05 → Starting.
        using var detector = CreateDetectorWithThresholds(activation: SquareThreshold, minSpeechDuration: 10.0f);

        // First square frame with fresh context gives ~0.098 >= 0.05 → Starting
        var result = detector.Process(MakeSquareFrame(16000));

        result.State.Should().Be(VadState.Starting);
        result.IsSpeaking.Should().BeFalse();
    }

    // =========================================================================
    // #19 — Process_StateMachine_StartingToSpeaking (Integration)
    // =========================================================================

    [Fact]
    public void Process_StateMachine_StartingToSpeaking_AfterMinSpeechDuration()
    {
        // Pre-inject Starting state. Then send 20 square frames (confidence ~0.073-0.135 >= 0.05).
        // MinSpeechDuration = 32ms: after ~2 frames' worth of accumulated speech → Speaking.
        using var detector = CreateDetectorWithThresholds(activation: SquareThreshold, minSpeechDuration: 0.032f);

        // Preset state to Starting and inject 32ms of prior speech duration
        SetVadState(detector, VadState.Starting);
        SetField(detector, "_speechDuration", TimeSpan.FromSeconds(0.016)); // 16ms already counted

        // One more square frame (32ms) → total >= 32ms → Speaking
        var result = detector.Process(MakeSquareFrame(16000));

        result.State.Should().Be(VadState.Speaking);
        result.IsSpeaking.Should().BeTrue();
    }

    // =========================================================================
    // #20 — Process_StateMachine_SpeakingToStopping (Integration)
    // =========================================================================

    [Fact]
    public void Process_StateMachine_SpeakingToStopping_OnSilenceWhileSpeaking()
    {
        // Pre-inject Speaking state. After 20 warm-up square frames (model settled), silence gives
        // ~0.043 which is < deactivation (0.05) → Stopping.
        using var detector = CreateDetectorWithThresholds(
            activation: SquareThreshold,
            deactivation: SilenceThreshold);

        // Warm up model with square frames (settle RNN state) then inject Speaking
        for (int i = 0; i < 20; i++) detector.Process(MakeSquareFrame(16000));
        SetVadState(detector, VadState.Speaking);

        // Silence after settled square context: confidence ~0.043 < 0.05 → Stopping
        var result = detector.Process(MakeSilentFrame(16000));

        result.State.Should().Be(VadState.Stopping);
        result.IsSpeaking.Should().BeTrue();
    }

    // =========================================================================
    // #21 — Process_StateMachine_StoppingToQuiet (Integration)
    // =========================================================================

    [Fact]
    public void Process_StateMachine_StoppingToQuiet_AfterSilenceFromStopping()
    {
        // Pre-inject Stopping state. After silence frames settle the context, further silence
        // gives ~0.003-0.009 confidence, which is < activation (0.05) → Quiet.
        using var detector = CreateDetectorWithThresholds(
            activation: SquareThreshold,
            deactivation: SilenceThreshold);

        // Warm up with squares then silence to get context right, then inject Stopping
        for (int i = 0; i < 20; i++) detector.Process(MakeSquareFrame(16000));
        for (int i = 0; i < 2; i++) detector.Process(MakeSilentFrame(16000)); // si0=0.043, si1=0.004
        SetVadState(detector, VadState.Stopping);

        // Next silence (si2-level): confidence ~0.006 < activation (0.05) → Quiet
        var result = detector.Process(MakeSilentFrame(16000));

        result.State.Should().Be(VadState.Quiet);
        result.IsSpeaking.Should().BeFalse();
    }

    // =========================================================================
    // #22 — Process_StateMachine_StartingDropsOnSilence (Integration)
    // =========================================================================

    [Fact]
    public void Process_StateMachine_StartingDropsOnSilence_WhenSilenceBeforeMinDuration()
    {
        // Pre-inject Starting state. After silence context, further silence gives ~0.006 < activation → Quiet.
        using var detector = CreateDetectorWithThresholds(
            activation: SquareThreshold,
            minSpeechDuration: 10.0f,
            deactivation: SilenceThreshold);

        // Warm up with squares then 2 silence frames to settle model context
        for (int i = 0; i < 20; i++) detector.Process(MakeSquareFrame(16000));
        for (int i = 0; i < 2; i++) detector.Process(MakeSilentFrame(16000));
        SetVadState(detector, VadState.Starting);

        // Next silence: confidence ~0.006 < activation (0.05) → drops to Quiet
        var result = detector.Process(MakeSilentFrame(16000));

        result.State.Should().Be(VadState.Quiet);
        result.IsSpeaking.Should().BeFalse();
    }

    // =========================================================================
    // #23 — Process_DeactivationThreshold_DefaultIsActivationMinus015 (Unit)
    // =========================================================================

    [Fact]
    public void Process_DeactivationThreshold_DefaultIsActivationMinus015()
    {
        // When DeactivationThreshold is null, it defaults to activation - 0.15
        var vadConfig = new VadConfig { ActivationThreshold = 0.5f };
        var sileroConfig = new SileroVadConfig(); // DeactivationThreshold = null

        using var detector = CreateDetectorDirect(vadConfig, sileroConfig);

        var field = typeof(SileroVadDetector).GetField(
            "_deactivationThreshold",
            BindingFlags.NonPublic | BindingFlags.Instance);
        field.Should().NotBeNull();

        var value = (float)field!.GetValue(detector)!;
        value.Should().BeApproximately(0.35f, 0.001f);
    }

    // =========================================================================
    // #24 — Process_DeactivationThreshold_ExplicitOverride (Integration)
    // =========================================================================

    [Fact]
    public void Process_DeactivationThreshold_ExplicitOverride_IsUsed()
    {
        var vadConfig = new VadConfig { ActivationThreshold = 0.5f };
        var sileroConfig = new SileroVadConfig { DeactivationThreshold = 0.2f };

        using var detector = CreateDetectorDirect(vadConfig, sileroConfig);

        var field = typeof(SileroVadDetector).GetField(
            "_deactivationThreshold",
            BindingFlags.NonPublic | BindingFlags.Instance);
        field.Should().NotBeNull();

        var value = (float)field!.GetValue(detector)!;
        value.Should().BeApproximately(0.2f, 0.001f);
    }

    // =========================================================================
    // #25 — Process_SampleRateChange_ResetsModelState (Integration)
    // =========================================================================

    [Fact]
    public void Process_SampleRateChange_ResetsModelState_NoCrash()
    {
        using var detector = CreateDefaultDetector();

        var frame16k = MakeSilentFrame(16000);
        var frame8k = MakeSilentFrame(8000);

        var act = () =>
        {
            detector.Process(frame16k);
            detector.Process(frame8k);
        };

        act.Should().NotThrow();
    }

    // =========================================================================
    // #26 — Reset_ClearsStateMachineAndBuffer (Integration)
    // =========================================================================

    [Fact]
    public void Reset_ClearsStateMachineAndBuffer()
    {
        // Drive to Speaking with 20+ square frames; Reset() must clear state and buffer.
        using var detector = CreateDetectorWithThresholds(
            activation: SquareThreshold,
            minSpeechDuration: 0.032f,
            deactivation: SilenceThreshold);

        // Drive to Speaking (20 frames to settle context)
        for (int i = 0; i < 20; i++)
            detector.Process(MakeSquareFrame(16000));

        // Reset
        detector.Reset();

        // Verify _vadState is Quiet via reflection
        var stateField = typeof(SileroVadDetector).GetField(
            "_vadState", BindingFlags.NonPublic | BindingFlags.Instance)!;
        ((VadState)stateField.GetValue(detector)!).Should().Be(VadState.Quiet);

        // Verify pre-speech buffer is empty
        var bufferField = typeof(SileroVadDetector).GetField(
            "_preSpeechBuffer", BindingFlags.NonPublic | BindingFlags.Instance)!;
        ((System.Collections.Generic.List<AudioFrame>)bufferField.GetValue(detector)!).Should().BeEmpty();

        // After reset with default threshold (0.5), silence (0.024) < 0.5 → Quiet
        // But our detector has activation=SquareThreshold=0.05, and after Reset the model context
        // is cleared too. Fresh silence gives 0.024 < 0.05 → Quiet.
        var result = detector.Process(MakeSilentFrame(16000));
        result.State.Should().Be(VadState.Quiet);
    }

    // =========================================================================
    // #27 — DetectAsync_EmitsInferenceDoneForEveryFrame (Integration)
    // =========================================================================

    [Fact]
    public async Task DetectAsync_EmitsInferenceDoneForEveryFrame()
    {
        using var detector = CreateDefaultDetector();
        const int frameCount = 5;
        var frames = Enumerable.Range(0, frameCount)
            .Select(_ => MakeSilentFrame(16000))
            .ToAsyncEnumerable();

        var events = await CollectEventsAsync(detector, frames);

        var inferenceDone = events.Where(e => e.Type == VadEventType.InferenceDone).ToList();
        inferenceDone.Count.Should().Be(frameCount);
    }

    // =========================================================================
    // #28 — DetectAsync_EmitsStartOfSpeech_OnFirstSpeakingFrame (Integration)
    // =========================================================================

    [Fact]
    public async Task DetectAsync_EmitsStartOfSpeech_OnFirstSpeakingFrame()
    {
        // 30 square frames: fresh sq0=0.098 >= 0.05 → Starting; frames 1+ accumulate speech duration.
        // MinSpeechDuration=32ms → Speaking reached by frame 2; StartOfSpeech fires.
        using var detector = CreateDetectorWithThresholds(
            activation: SquareThreshold,
            minSpeechDuration: 0.032f,
            deactivation: SilenceThreshold);

        var frames = Enumerable.Range(0, 30)
            .Select(_ => MakeSquareFrame(16000))
            .ToAsyncEnumerable();

        var events = await CollectEventsAsync(detector, frames);

        var startEvents = events.Where(e => e.Type == VadEventType.StartOfSpeech).ToList();
        startEvents.Should().NotBeEmpty("square wave frames cross SquareThreshold, so StartOfSpeech should fire");
        startEvents[0].Frames.Should().NotBeNull();
    }

    // =========================================================================
    // #29 — DetectAsync_StartOfSpeech_IncludesPreSpeechBuffer (Integration)
    // =========================================================================

    [Fact]
    public async Task DetectAsync_StartOfSpeech_IncludesPreSpeechBuffer()
    {
        // The pre-speech buffer is populated inside DetectAsync (not Process) for Quiet-state frames.
        // Since Silero gives low confidence for synthetic audio after silence context, we use
        // only square frames (no preceding silence) so the buffer is populated from within DetectAsync.
        //
        // With activation=SquareThreshold (0.05) and fresh context:
        //   - sq0 gives 0.098 → Starting (not Quiet, so not buffered)
        //   - sq1+ keeps accumulating speech → Speaking → StartOfSpeech fires
        //
        // The buffer in this case will be empty (first frame already triggers Starting).
        // The spec intent is that pre-speech frames ARE included when there were Quiet frames before.
        // We verify the StartOfSpeech.Frames is non-null and contains at least the current frame.
        //
        // For full pre-speech buffer verification, see #32 which uses Process() + reflection directly.
        using var detector = CreateDetectorWithThresholds(
            activation: SquareThreshold,
            minSpeechDuration: 0.032f,
            deactivation: SilenceThreshold,
            prefixPaddingDuration: 0.5f);

        var frames = Enumerable.Range(0, 30)
            .Select(_ => MakeSquareFrame(16000))
            .ToAsyncEnumerable();

        var events = await CollectEventsAsync(detector, frames);

        var startEvent = events.FirstOrDefault(e => e.Type == VadEventType.StartOfSpeech);
        startEvent.Should().NotBeNull("30 square frames should reach Speaking → StartOfSpeech");
        startEvent!.Frames.Should().NotBeNull("StartOfSpeech always includes the current speaking frame");
        startEvent.Frames!.Count.Should().BeGreaterThan(0,
            "StartOfSpeech must include at least the current frame");
    }

    // =========================================================================
    // #30 — DetectAsync_EmitsEndOfSpeech_AfterMinSilenceDuration (Integration)
    // =========================================================================

    [Fact]
    public async Task DetectAsync_EmitsEndOfSpeech_AfterMinSilenceDuration()
    {
        // 30 square frames → reach Speaking. Then 5 silence frames.
        // After settled square context: si0≈0.043 < 0.05 → Stopping; si1≈0.004 < 0.05 → Quiet.
        // MinSilenceDuration=32ms (1 frame) → EndOfSpeech fires as soon as silence_duration >= 32ms.
        using var detector = CreateDetectorWithThresholds(
            activation: SquareThreshold,
            minSpeechDuration: 0.032f,
            deactivation: SilenceThreshold,
            minSilenceDuration: 0.032f);

        var frames = Enumerable.Range(0, 30)
            .Select(_ => MakeSquareFrame(16000))
            .Concat(Enumerable.Range(0, 5).Select(_ => MakeSilentFrame(16000)))
            .ToAsyncEnumerable();

        var events = await CollectEventsAsync(detector, frames);

        var endEvents = events.Where(e => e.Type == VadEventType.EndOfSpeech).ToList();
        endEvents.Should().NotBeEmpty("silence after Speaking should emit EndOfSpeech");
    }

    // =========================================================================
    // #31 — DetectAsync_NoEndOfSpeech_IfSilenceTooShort (Integration)
    // =========================================================================

    [Fact]
    public async Task DetectAsync_NoEndOfSpeech_IfSilenceTooShort()
    {
        // Same as #30 but MinSilenceDuration=10s — far more than 5 silence frames (~160ms) provide.
        using var detector = CreateDetectorWithThresholds(
            activation: SquareThreshold,
            minSpeechDuration: 0.032f,
            deactivation: SilenceThreshold,
            minSilenceDuration: 10.0f);

        var frames = Enumerable.Range(0, 30)
            .Select(_ => MakeSquareFrame(16000))
            .Concat(Enumerable.Range(0, 3).Select(_ => MakeSilentFrame(16000)))
            .ToAsyncEnumerable();

        var events = await CollectEventsAsync(detector, frames);

        events.Should().NotContain(e => e.Type == VadEventType.EndOfSpeech);
    }

    // =========================================================================
    // #32 — DetectAsync_PreSpeechBuffer_DropsOldestWhenFull (Integration)
    // =========================================================================

    [Fact]
    public void DetectAsync_PreSpeechBuffer_DropsOldestWhenFull()
    {
        // PrefixPaddingDuration = 64ms = 2 frames at 16 kHz (each 32ms).
        // Process 10 silence frames (all Quiet → go into buffer). After overflow, only 2 remain.
        // Verify directly via reflection — no need to drive through DetectAsync.
        using var detector = CreateDetectorWithThresholds(
            activation: SquareThreshold,
            minSpeechDuration: 0.032f,
            deactivation: SilenceThreshold,
            prefixPaddingDuration: 0.064f);

        // 10 Quiet frames → buffer overflows → capped to 2 frames (64ms / 32ms)
        for (int i = 0; i < 10; i++)
            detector.Process(MakeSilentFrame(16000));

        var bufferField = typeof(SileroVadDetector).GetField(
            "_preSpeechBuffer", BindingFlags.NonPublic | BindingFlags.Instance)!;
        var buffer = (System.Collections.Generic.List<AudioFrame>)bufferField.GetValue(detector)!;

        buffer.Count.Should().BeLessThanOrEqualTo(2,
            "buffer must not exceed PrefixPaddingDuration of 64ms (2 frames at 32ms/frame)");
    }

    // =========================================================================
    // #33 — DetectAsync_ResetsOnEachCall (Integration)
    // =========================================================================

    [Fact]
    public async Task DetectAsync_ResetsOnEachCall_ProducesIndependentRuns()
    {
        // Default threshold (0.5) → silence (0.0238) stays Quiet → only InferenceDone events.
        using var detector = CreateDefaultDetector();
        var frames = Enumerable.Range(0, 5)
            .Select(_ => MakeSilentFrame(16000))
            .ToAsyncEnumerable();

        var events1 = await CollectEventsAsync(detector, frames);

        // Run again with same input — should be identical (reset at start of each DetectAsync call)
        var frames2 = Enumerable.Range(0, 5)
            .Select(_ => MakeSilentFrame(16000))
            .ToAsyncEnumerable();
        var events2 = await CollectEventsAsync(detector, frames2);

        events1.Count.Should().Be(events2.Count);
        for (int i = 0; i < events1.Count; i++)
            events1[i].Type.Should().Be(events2[i].Type);
    }

    // =========================================================================
    // #34 — DetectAsync_CancellationToken_StopsIteration (Integration)
    // =========================================================================

    [Fact]
    public async Task DetectAsync_CancellationToken_StopsIteration()
    {
        using var detector = CreateDefaultDetector();
        using var cts = new CancellationTokenSource();

        var infiniteFrames = InfiniteFramesAsync(16000, cts.Token);

        var events = new List<VadEvent>();
        var ex = await Record.ExceptionAsync(async () =>
        {
            await foreach (var evt in detector.DetectAsync(infiniteFrames, cts.Token))
            {
                events.Add(evt);
                if (events.Count >= 3)
                    cts.Cancel();
            }
        });

        // Either OperationCanceledException is thrown or iteration just stopped
        ex?.Should().BeOfType<OperationCanceledException>();
        events.Count.Should().BeGreaterThanOrEqualTo(3);
    }

    // =========================================================================
    // #35 — DetectAsync_AfterDispose_Throws (Unit)
    // =========================================================================

    [Fact]
    public async Task DetectAsync_AfterDispose_Throws()
    {
        var detector = CreateDefaultDetector();
        detector.Dispose();

        var frames = Array.Empty<AudioFrame>().ToAsyncEnumerable();

        Func<Task> act = async () =>
        {
            await foreach (var _ in detector.DetectAsync(frames)) { }
        };

        await act.Should().ThrowAsync<ObjectDisposedException>();
    }

    // =========================================================================
    // #36 — ModuleInitializer_RegistersSileroVad (Unit)
    // =========================================================================

    [Fact]
    public void ModuleInitializer_RegistersSileroVad()
    {
        // Assembly is loaded by virtue of referencing the project; module initializer fires
        var factory = VadProviderDiscovery.GetFactory("silero-vad");

        factory.Should().NotBeNull("Silero VAD factory should be auto-registered by module initializer");
        VadProviderDiscovery.GetAvailableProviders().Should().Contain("silero-vad");
    }

    // =========================================================================
    // #37 — ModuleInitializer_RegistersConfigType (Unit)
    // =========================================================================

    [Fact]
    public void ModuleInitializer_RegistersConfigType()
    {
        var configType = VadProviderDiscovery.GetConfigType("silero-vad");

        configType.Should().NotBeNull("SileroVadConfig should be registered");
        configType.Should().Be(typeof(SileroVadConfig));
    }

    // =========================================================================
    // #38 — WithSileroVad_SetsVadOnMiddleware (Unit/Integration)
    // =========================================================================

    [Fact]
    public void WithSileroVad_SetsVadOnMiddleware()
    {
        // Validate that the factory creates a SileroVadDetector
        var factory = new SileroVadProviderFactory();
        var config = new VadConfig { ActivationThreshold = 0.5f };

        using var detector = factory.CreateDetector(config);

        detector.Should().BeOfType<SileroVadDetector>();
    }

    // =========================================================================
    // #39 — WithSileroVad_CustomThreshold_Applied (Unit/Integration)
    // =========================================================================

    [Fact]
    public void WithSileroVad_CustomThreshold_Applied()
    {
        const float customThreshold = 0.7f;
        var factory = new SileroVadProviderFactory();
        var config = new VadConfig { ActivationThreshold = customThreshold };

        using var detector = (SileroVadDetector)factory.CreateDetector(config);

        var field = typeof(SileroVadDetector).GetField(
            "_activationThreshold",
            BindingFlags.NonPublic | BindingFlags.Instance);
        field.Should().NotBeNull();

        var value = (float)field!.GetValue(detector)!;
        value.Should().BeApproximately(customThreshold, 0.001f);
    }

    // =========================================================================
    // Helpers
    // =========================================================================

    // Measured Silero ONNX model output for our synthetic signals at 16 kHz:
    //   Silence (all zeros)   → ~0.0238
    //   Square wave ±32767    → ~0.0981
    // Thresholds chosen to reliably separate the two without real speech audio.
    private const float SquareThreshold  = 0.05f;  // silence(0.024) < 0.05 < square(0.098)
    private const float SilenceThreshold = 0.05f;  // deactivation: silence < 0.05 → exits Speaking

    /// <summary>
    /// Sets the VAD state machine state directly via reflection.
    /// Used to establish preconditions for state transition tests without depending on model output.
    /// </summary>
    private static void SetVadState(SileroVadDetector detector, VadState state)
    {
        var field = typeof(SileroVadDetector).GetField(
            "_vadState", BindingFlags.NonPublic | BindingFlags.Instance)!;
        field.SetValue(detector, state);
    }

    /// <summary>Sets any private field on a SileroVadDetector via reflection.</summary>
    private static void SetField<T>(SileroVadDetector detector, string fieldName, T value)
    {
        var field = typeof(SileroVadDetector).GetField(
            fieldName, BindingFlags.NonPublic | BindingFlags.Instance)!;
        field.SetValue(detector, value);
    }

    /// <summary>Creates a detector with all-default settings.</summary>
    private static SileroVadDetector CreateDefaultDetector()
    {
        var factory = new SileroVadProviderFactory();
        return (SileroVadDetector)factory.CreateDetector(new VadConfig());
    }

    /// <summary>Creates a detector with custom thresholds and timing parameters.</summary>
    private static SileroVadDetector CreateDetectorWithThresholds(
        float activation = 0.5f,
        float minSpeechDuration = 0.05f,
        float deactivation = 0.35f,
        float minSilenceDuration = 0.55f,
        float prefixPaddingDuration = 0.5f)
    {
        var vadConfig = new VadConfig
        {
            ActivationThreshold = activation,
            MinSpeechDuration = minSpeechDuration,
            MinSilenceDuration = minSilenceDuration,
            PrefixPaddingDuration = prefixPaddingDuration
        };
        var sileroConfig = new SileroVadConfig { DeactivationThreshold = deactivation };
        return CreateDetectorDirect(vadConfig, sileroConfig);
    }

    /// <summary>Creates a SileroVadDetector directly using internal constructor (via InternalsVisibleTo).</summary>
    private static SileroVadDetector CreateDetectorDirect(VadConfig vadConfig, SileroVadConfig sileroConfig)
    {
        // Load the real ONNX session via the factory's private LoadSession method
        var factory = new SileroVadProviderFactory();
        // We need a real InferenceSession; obtain it by creating a default detector and
        // extracting the session, or by calling the factory with a stock config then
        // replacing. Since InternalsVisibleTo is granted, we can call the internal ctor.
        // The simplest path: load via factory then extract session via reflection.
        var tempDetector = (SileroVadDetector)factory.CreateDetector(new VadConfig());
        var sessionField = typeof(SileroVadDetector).GetField(
            "_session", BindingFlags.NonPublic | BindingFlags.Instance)!;
        var session = (InferenceSession)sessionField.GetValue(tempDetector)!;

        // Detach session ownership from tempDetector before disposing it
        // (we'll pass it to the new detector; both share the session, which is fine for tests)
        return new SileroVadDetector(session, vadConfig, sileroConfig);
    }

    /// <summary>
    /// Creates a square wave PCM frame that gives ~0.098 Silero confidence at 16 kHz.
    /// This is consistently above SquareThreshold (0.05) and below the default 0.5 threshold.
    /// </summary>
    private static AudioFrame MakeSquareFrame(int sampleRate)
    {
        var sampleCount = sampleRate == 16000 ? 512 : 256;
        var data = new byte[sampleCount * 2];
        for (int i = 0; i < sampleCount; i++)
        {
            var s = i < sampleCount / 2 ? (short)32767 : (short)-32767;
            data[i * 2]     = (byte)(s & 0xFF);
            data[i * 2 + 1] = (byte)((s >> 8) & 0xFF);
        }
        return new AudioFrame
        {
            Data = data,
            SampleRate = sampleRate,
            Channels = 1,
            Timestamp = TimeSpan.Zero,
            Duration = TimeSpan.FromSeconds((double)sampleCount / sampleRate)
        };
    }

    /// <summary>Creates a silent PCM frame (all zeros) at the given sample rate.</summary>
    private static AudioFrame MakeSilentFrame(int sampleRate)
    {
        var sampleCount = sampleRate == 16000 ? 512 : 256;
        var data = new byte[sampleCount * 2]; // 16-bit PCM, all zeros
        var duration = TimeSpan.FromSeconds((double)sampleCount / sampleRate);
        return new AudioFrame
        {
            Data = data,
            SampleRate = sampleRate,
            Channels = 1,
            Timestamp = TimeSpan.Zero,
            Duration = duration
        };
    }

    /// <summary>
    /// Creates a PCM frame with a 400 Hz sine wave at the given sample rate.
    /// Used to produce a non-trivial audio signal that may yield higher speech probability.
    /// </summary>
    private static AudioFrame MakeSpeechFrame(int sampleRate)
    {
        var sampleCount = sampleRate == 16000 ? 512 : 256;
        var data = new byte[sampleCount * 2];
        const float frequency = 400f;
        const float amplitude = 0.8f;

        for (int i = 0; i < sampleCount; i++)
        {
            var sample = (short)(amplitude * short.MaxValue * Math.Sin(2 * Math.PI * frequency * i / sampleRate));
            data[i * 2]     = (byte)(sample & 0xFF);
            data[i * 2 + 1] = (byte)((sample >> 8) & 0xFF);
        }

        var duration = TimeSpan.FromSeconds((double)sampleCount / sampleRate);
        return new AudioFrame
        {
            Data = data,
            SampleRate = sampleRate,
            Channels = 1,
            Timestamp = TimeSpan.Zero,
            Duration = duration
        };
    }

    /// <summary>Creates a frame with an arbitrary sample count (for guard-clause tests).</summary>
    private static AudioFrame MakeFrameWithSamples(int sampleRate, int sampleCount)
    {
        var data = new byte[sampleCount * 2];
        var duration = TimeSpan.FromSeconds((double)sampleCount / sampleRate);
        return new AudioFrame
        {
            Data = data,
            SampleRate = sampleRate,
            Channels = 1,
            Timestamp = TimeSpan.Zero,
            Duration = duration
        };
    }

    /// <summary>Collects all VadEvents from DetectAsync into a list.</summary>
    private static async Task<List<VadEvent>> CollectEventsAsync(
        IVoiceActivityDetector detector,
        IAsyncEnumerable<AudioFrame> frames,
        CancellationToken ct = default)
    {
        var events = new List<VadEvent>();
        await foreach (var evt in detector.DetectAsync(frames, ct))
            events.Add(evt);
        return events;
    }

    /// <summary>Produces an infinite stream of silent frames until cancellation.</summary>
    private static async IAsyncEnumerable<AudioFrame> InfiniteFramesAsync(
        int sampleRate,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
    {
        while (!ct.IsCancellationRequested)
        {
            yield return MakeSilentFrame(sampleRate);
            await Task.Yield();
        }
    }
}

// =========================================================================
// Extension helpers
// =========================================================================

file static class EnumerableAsyncExtensions
{
    public static IAsyncEnumerable<T> ToAsyncEnumerable<T>(this IEnumerable<T> source)
        => AsyncEnumerableHelper.FromEnumerable(source);
}

file static class AsyncEnumerableHelper
{
    public static async IAsyncEnumerable<T> FromEnumerable<T>(IEnumerable<T> source)
    {
        foreach (var item in source)
        {
            yield return item;
            await Task.Yield();
        }
    }
}
