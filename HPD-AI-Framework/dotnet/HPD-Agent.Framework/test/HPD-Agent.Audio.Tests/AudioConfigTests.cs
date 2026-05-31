// Copyright (c) 2025 Einstein Essibu. All rights reserved.

using HPD.Agent.Audio;
using HPD.Agent.Audio.Eot;
using HPD.Agent.Audio.Realtime;
using HPD.Agent.Audio.Stt;
using HPD.Agent.Audio.Tts;
using HPD.Agent.Audio.Vad;
using FluentAssertions;

namespace HPD.Agent.Audio.Tests;

/// <summary>
/// Unit tests for AudioConfig - tests basic properties, defaults, and validation.
/// </summary>
public class AudioConfigTests
{
    [Fact]
    public void Constructor_SetsDefaultProcessingMode()
    {
        // Arrange & Act
        var config = new AudioConfig();

        // Assert
        config.ProcessingMode.Should().Be(AudioProcessingMode.Pipeline);
    }

    [Fact]
    public void Constructor_SetsDefaultIOMode()
    {
        // Arrange & Act
        var config = new AudioConfig();

        // Assert
        config.IOMode.Should().Be(AudioIOMode.AudioToAudioAndText);
    }

    [Fact]
    public void Constructor_SetsNullForOptionalProperties()
    {
        // Arrange & Act
        var config = new AudioConfig();

        // Assert
        config.Language.Should().BeNull();
        config.Disabled.Should().BeNull();
        config.Tts.Should().BeNull();
        config.Stt.Should().BeNull();
        config.Vad.Should().BeNull();
        config.Eot.Should().NotBeNull();
    }

    [Fact]
    public void VadDefaults_AreCorrect()
    {
        // Arrange & Act
        var vad = new VadConfig();

        // Assert - VAD properties are on VadConfig, not AudioConfig
        vad.MinSpeechDuration.Should().Be(0.05f);
        vad.MinSilenceDuration.Should().Be(0.55f);
        vad.PrefixPaddingDuration.Should().Be(0.5f);
        vad.ActivationThreshold.Should().Be(0.5f);
    }

    [Fact]
    public void EotDefaults_AreCorrect()
    {
        // Arrange & Act
        var eot = new EotConfig();

        // Assert
        eot.Provider.Should().Be("heuristic-eot");
        eot.SilenceStrategy.Should().Be(EotDetectionStrategy.FastPath);
        eot.DetectorStrategy.Should().Be(EotDetectionStrategy.OnAmbiguous);
        eot.SilenceFastPathThreshold.Should().Be(1.5f);
        eot.MinEndpointingDelay.Should().Be(0.3f);
        eot.MaxEndpointingDelay.Should().Be(1.5f);
        eot.SilenceBoostMultiplier.Should().Be(0.7f);
        eot.UseCombinedProbability.Should().Be(true);
        eot.TrailingWordPenalty.Should().Be(0.6f);
    }

    [Fact]
    public void PipelineFeatureDefaults_AreCorrect()
    {
        // Arrange & Act
        var config = new AudioConfig();

        // Assert
        config.EnableQuickAnswer.Should().Be(true);
        config.EnableSpeedAdaptation.Should().Be(true);
        config.EnablePreemptiveGeneration.Should().Be(false);
        config.PreemptiveGenerationThreshold.Should().Be(0.7f);
    }

    [Fact]
    public void InterruptionDefaults_AreCorrect()
    {
        // Arrange & Act
        var config = new AudioConfig();

        // Assert
        config.BackchannelStrategy.Should().Be(BackchannelStrategy.IgnoreShortUtterances);
        config.MinWordsForInterruption.Should().Be(2);
        config.EnableFalseInterruptionRecovery.Should().Be(true);
        config.FalseInterruptionTimeout.Should().Be(2.0f);
        config.ResumeFalseInterruption.Should().Be(true);
        config.MaxBufferedChunksDuringPause.Should().Be(100);
    }

    [Fact]
    public void FillerAudioDefaults_AreCorrect()
    {
        // Arrange & Act
        var config = new AudioConfig();

        // Assert
        config.EnableFillerAudio.Should().Be(false);
        config.FillerSilenceThreshold.Should().Be(1.5f);
        config.FillerPhrases.Should().NotBeNull();
        config.FillerPhrases.Should().HaveCount(3);
        config.FillerPhrases.Should().Contain(new[] { "Um...", "Let me see...", "One moment..." });
        config.FillerSelectionStrategy.Should().Be(FillerStrategy.Random);
        config.MaxFillerPlaysPerTurn.Should().Be(1);
        config.FillerVoice.Should().BeNull();
        config.FillerSpeed.Should().Be(0.95f);
    }

    [Fact]
    public void TextFilteringDefaults_AreCorrect()
    {
        // Arrange & Act
        var config = new AudioConfig();

        // Assert
        config.EnableTextFiltering.Should().Be(true);
        config.FilterCodeBlocks.Should().Be(true);
        config.FilterTables.Should().Be(true);
        config.FilterUrls.Should().Be(true);
        config.FilterMarkdownFormatting.Should().Be(true);
        config.FilterEmoji.Should().Be(true);
    }

    [Fact]
    public void CanSetProcessingMode()
    {
        // Arrange
        var config = new AudioConfig();

        // Act
        config.ProcessingMode = AudioProcessingMode.Realtime;

        // Assert
        config.ProcessingMode.Should().Be(AudioProcessingMode.Realtime);
    }

    [Fact]
    public void CanSetIOMode()
    {
        // Arrange
        var config = new AudioConfig();

        // Act
        config.IOMode = AudioIOMode.AudioToText;

        // Assert
        config.IOMode.Should().Be(AudioIOMode.AudioToText);
    }

    [Fact]
    public void CanSetLanguage()
    {
        // Arrange
        var config = new AudioConfig();

        // Act
        config.Language = "es";

        // Assert
        config.Language.Should().Be("es");
    }

    [Fact]
    public void CanSetDisabled()
    {
        // Arrange
        var config = new AudioConfig();

        // Act
        config.Disabled = true;

        // Assert
        config.Disabled.Should().Be(true);
    }

    [Fact]
    public void CanSetTtsConfig()
    {
        // Arrange
        var config = new AudioConfig();
        var ttsConfig = new TtsConfig { Language = "en" };

        // Act
        config.Tts = ttsConfig;

        // Assert
        config.Tts.Should().NotBeNull();
        config.Tts!.Language.Should().Be("en");
    }

    [Fact]
    public void CanSetSttConfig()
    {
        // Arrange
        var config = new AudioConfig();
        var sttConfig = new SttConfig { Language = "en" };

        // Act
        config.Stt = sttConfig;

        // Assert
        config.Stt.Should().NotBeNull();
        config.Stt!.Language.Should().Be("en");
    }

    [Fact]
    public void CanSetVadConfig()
    {
        // Arrange
        var config = new AudioConfig();
        var vadConfig = new VadConfig();

        // Act
        config.Vad = vadConfig;

        // Assert
        config.Vad.Should().NotBeNull();
    }

    [Fact]
    public void MergeWith_NullOverrides_ReturnsSameConfig()
    {
        // Arrange
        var config = new AudioConfig { Language = "en" };

        // Act
        var merged = config.MergeWith(null);

        // Assert
        merged.Should().BeSameAs(config);
    }

    [Fact]
    public void MergeWith_OverridesLanguage()
    {
        // Arrange
        var baseConfig = new AudioConfig { Language = "en" };
        var overrides = new AudioConfig { Language = "es" };

        // Act
        var merged = baseConfig.MergeWith(overrides);

        // Assert
        merged.Language.Should().Be("es");
    }

    [Fact]
    public void MergeWith_OverridesProcessingMode()
    {
        // Arrange
        var baseConfig = new AudioConfig { ProcessingMode = AudioProcessingMode.Pipeline };
        var overrides = new AudioConfig { ProcessingMode = AudioProcessingMode.Realtime };

        // Act
        var merged = baseConfig.MergeWith(overrides);

        // Assert
        merged.ProcessingMode.Should().Be(AudioProcessingMode.Realtime);
    }

    [Fact]
    public void MergeWith_PreservesBaseWhenOverrideIsNull()
    {
        // Arrange
        var baseConfig = new AudioConfig 
        { 
            Language = "en", 
            Vad = new VadConfig { MinSpeechDuration = 0.1f }
        };
        var overrides = new AudioConfig(); // Language is null

        // Act
        var merged = baseConfig.MergeWith(overrides);

        // Assert
        merged.Language.Should().Be("en");
    }

    [Fact]
    public void Validate_ThrowsOnNegativeVadMinSpeechDuration()
    {
        // Arrange
        var config = new AudioConfig 
        { 
            Vad = new VadConfig { MinSpeechDuration = -0.1f }
        };

        // Act & Assert
        var act = () => config.Validate();
        act.Should().Throw<ArgumentException>()
            .WithMessage("*MinSpeechDuration must be between 0.01 and 1.0*");
    }

    [Fact]
    public void Validate_ThrowsOnInvalidVadActivationThreshold()
    {
        // Arrange
        var config = new AudioConfig 
        { 
            Vad = new VadConfig { ActivationThreshold = 1.5f }
        };

        // Act & Assert
        var act = () => config.Validate();
        act.Should().Throw<ArgumentException>()
            .WithMessage("*ActivationThreshold must be between 0.0 and 1.0*");
    }

    [Fact]
    public void Validate_ThrowsOnNegativeMinWordsForInterruption()
    {
        // Arrange
        var config = new AudioConfig { MinWordsForInterruption = -1 };

        // Act & Assert
        var act = () => config.Validate();
        act.Should().Throw<ArgumentException>()
            .WithMessage("MinWordsForInterruption must be non-negative");
    }

    [Fact]
    public void Validate_ThrowsOnInvalidFillerSpeed()
    {
        // Arrange
        var config = new AudioConfig { FillerSpeed = 5.0f };

        // Act & Assert
        var act = () => config.Validate();
        act.Should().Throw<ArgumentException>()
            .WithMessage("FillerSpeed must be between 0.25 and 4.0");
    }

    [Fact]
    public void Validate_SucceedsOnValidConfig()
    {
        // Arrange
        var config = new AudioConfig();

        // Act & Assert
        var act = () => config.Validate();
        act.Should().NotThrow();
    }

    [Fact]
    public void Validate_Realtime_WithStt_Throws()
    {
        // Arrange
        var config = new AudioConfig
        {
            ProcessingMode = AudioProcessingMode.Realtime,
            Realtime = new RealtimeAudioConfig(),
            Stt = new SttConfig()
        };

        // Act & Assert
        var act = () => config.Validate();
        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*Stt*Realtime*");
    }

    [Fact]
    public void Validate_Realtime_WithTts_Throws()
    {
        // Arrange
        var config = new AudioConfig
        {
            ProcessingMode = AudioProcessingMode.Realtime,
            Realtime = new RealtimeAudioConfig(),
            Tts = new TtsConfig()
        };

        // Act & Assert
        var act = () => config.Validate();
        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*Tts*Realtime*");
    }

    [Fact]
    public void Validate_Realtime_WithVad_Throws()
    {
        // Arrange
        var config = new AudioConfig
        {
            ProcessingMode = AudioProcessingMode.Realtime,
            Realtime = new RealtimeAudioConfig(),
            Vad = new VadConfig()
        };

        // Act & Assert
        var act = () => config.Validate();
        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*Vad*Realtime*");
    }

    [Fact]
    public void Validate_Realtime_WithoutRealtimeConfig_Throws()
    {
        var config = new AudioConfig
        {
            ProcessingMode = AudioProcessingMode.Realtime,
            IOMode = AudioIOMode.AudioToAudio
        };

        var act = () => config.Validate();
        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*Realtime*must be set*");
    }

    [Fact]
    public void Validate_Realtime_WithRealtimeConfig_Succeeds()
    {
        var config = new AudioConfig
        {
            ProcessingMode = AudioProcessingMode.Realtime,
            IOMode = AudioIOMode.AudioToAudio,
            Realtime = new RealtimeAudioConfig()
        };

        var act = () => config.Validate();
        act.Should().NotThrow();
    }
}
