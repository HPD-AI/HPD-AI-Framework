// Copyright (c) 2025 Einstein Essibu. All rights reserved.

using HPD.Agent.Audio;
using Xunit;

namespace HPD.Agent.Tests.Audio;

/// <summary>
/// Unit tests for AudioPipelineConfig.
/// TODO: Implement once runtime audio architecture is finalized.
/// </summary>
public class AudioPipelineConfigTests
{
    /*
    [Fact]
    public void DefaultConstructor_SetsExpectedDefaults()
    {
        // Arrange & Act
        var config = new AudioPipelineConfig();

        // Assert - Core TTS/STT defaults
        Assert.Equal("alloy", config.Voice);
        Assert.Equal("tts-1", config.Model);
        Assert.Equal("mp3", config.OutputFormat);
        Assert.Equal(24000, config.SampleRate);
        Assert.Null(config.Speed);
        Assert.Null(config.Disabled);

        // Assert - VAD defaults
        Assert.Equal(0.05f, config.VadMinSpeechDuration);
        Assert.Equal(0.55f, config.VadMinSilenceDuration);
        Assert.Equal(0.5f, config.VadPrefixPaddingDuration);
        Assert.Equal(0.5f, config.VadActivationThreshold);

        // Assert - Turn detection defaults
        Assert.Equal(TurnDetectionStrategy.FastPath, config.SilenceStrategy);
        Assert.Equal(TurnDetectionStrategy.OnAmbiguous, config.MlStrategy);
        Assert.Equal(1.5f, config.SilenceFastPathThreshold);
        Assert.Equal(0.3f, config.MinEndpointingDelay);
        Assert.Equal(1.5f, config.MaxEndpointingDelay);

        // Assert - Silence boost defaults
        Assert.Equal(0.7f, config.SilenceBoostMultiplier);
        Assert.True(config.UseCombinedProbability);

        // Assert - Trailing words defaults
        Assert.Null(config.CustomTrailingWords);
        Assert.Equal(0.6f, config.TrailingWordPenalty);

        // Assert - Feature flags
        Assert.True(config.EnableQuickAnswer);
        Assert.True(config.EnableSpeedAdaptation);

        // Assert - Backchannel defaults
        Assert.Equal(BackchannelStrategy.IgnoreShortUtterances, config.BackchannelStrategy);
        Assert.Equal(2, config.MinWordsForInterruption);

        // Assert - False interruption recovery defaults
        Assert.True(config.EnableFalseInterruptionRecovery);
        Assert.Equal(2.0f, config.FalseInterruptionTimeout);
        Assert.True(config.ResumeFalseInterruption);
        Assert.Equal(100, config.MaxBufferedChunksDuringPause);

        // Assert - Filler audio defaults
        Assert.False(config.EnableFillerAudio);
        Assert.Equal(1.5f, config.FillerSilenceThreshold);
        Assert.NotNull(config.FillerPhrases);
        Assert.Equal(3, config.FillerPhrases!.Length);
        Assert.Contains("Um...", config.FillerPhrases);
        Assert.Equal(FillerStrategy.Random, config.FillerSelectionStrategy);
        Assert.Equal(1, config.MaxFillerPlaysPerTurn);
        Assert.Null(config.FillerVoice);
        Assert.Equal(0.95f, config.FillerSpeed);

        // Assert - Text filtering defaults
        Assert.True(config.EnableTextFiltering);
        Assert.True(config.FilterCodeBlocks);
        Assert.True(config.FilterTables);
        Assert.True(config.FilterUrls);
        Assert.True(config.FilterMarkdownFormatting);
        Assert.True(config.FilterEmoji);

        // Assert - Preemptive generation defaults
        Assert.False(config.EnablePreemptiveGeneration);
        Assert.Equal(0.7f, config.PreemptiveGenerationThreshold);
    }

    [Fact]
    public void CreateDisabled_SetsDisabledTrue()
    {
        // Act
        var config = AudioPipelineConfig.CreateDisabled();

        // Assert
        Assert.True(config.Disabled);
    }

    [Fact]
    public void Basic_DisablesAdvancedFeatures()
    {
        // Act
        var config = AudioPipelineConfig.Basic();

        // Assert
        Assert.False(config.EnableQuickAnswer);
        Assert.False(config.EnableSpeedAdaptation);
        Assert.False(config.EnableFalseInterruptionRecovery);
        Assert.False(config.EnableFillerAudio);
        Assert.False(config.EnableTextFiltering);
        Assert.False(config.EnablePreemptiveGeneration);
    }

    [Fact]
    public void MergeWith_NullOverrides_ReturnsOriginalConfig()
    {
        // Arrange
        var original = new AudioPipelineConfig
        {
            Voice = "nova",
            EnableQuickAnswer = false
        };

        // Act
        var merged = original.MergeWith(null);

        // Assert
        Assert.Equal("nova", merged.Voice);
        Assert.False(merged.EnableQuickAnswer);
    }

    [Fact]
    public void MergeWith_OverridesSpecificValues_KeepsOriginalForOthers()
    {
        // Arrange
        var original = new AudioPipelineConfig
        {
            Voice = "alloy",
            Model = "tts-1",
            EnableQuickAnswer = true,
            EnableFillerAudio = false,
            VadMinSilenceDuration = 0.55f
        };

        var overrides = new AudioPipelineConfig
        {
            Voice = "nova",           // Override
            EnableFillerAudio = true  // Override
            // Model, EnableQuickAnswer, VadMinSilenceDuration not set (null)
        };

        // Act
        var merged = original.MergeWith(overrides);

        // Assert - Overridden values
        Assert.Equal("nova", merged.Voice);
        Assert.True(merged.EnableFillerAudio);

        // Assert - Original values preserved
        Assert.Equal("tts-1", merged.Model);
        Assert.True(merged.EnableQuickAnswer);
        Assert.Equal(0.55f, merged.VadMinSilenceDuration);
    }

    [Fact]
    public void MergeWith_OverridesAllCoreSettings()
    {
        // Arrange
        var original = new AudioPipelineConfig
        {
            Voice = "alloy",
            Model = "tts-1",
            OutputFormat = "mp3",
            SampleRate = 24000,
            Speed = null,
            Disabled = false
        };

        var overrides = new AudioPipelineConfig
        {
            Voice = "nova",
            Model = "tts-1-hd",
            OutputFormat = "opus",
            SampleRate = 48000,
            Speed = 1.2f,
            Disabled = true
        };

        // Act
        var merged = original.MergeWith(overrides);

        // Assert
        Assert.Equal("nova", merged.Voice);
        Assert.Equal("tts-1-hd", merged.Model);
        Assert.Equal("opus", merged.OutputFormat);
        Assert.Equal(48000, merged.SampleRate);
        Assert.Equal(1.2f, merged.Speed);
        Assert.True(merged.Disabled);
    }

    [Fact]
    public void MergeWith_OverridesVadSettings()
    {
        // Arrange
        var original = new AudioPipelineConfig
        {
            VadMinSpeechDuration = 0.05f,
            VadMinSilenceDuration = 0.55f,
            VadPrefixPaddingDuration = 0.5f,
            VadActivationThreshold = 0.5f
        };

        var overrides = new AudioPipelineConfig
        {
            VadMinSpeechDuration = 0.1f,
            VadMinSilenceDuration = 0.8f
            // Other VAD settings not overridden
        };

        // Act
        var merged = original.MergeWith(overrides);

        // Assert
        Assert.Equal(0.1f, merged.VadMinSpeechDuration);
        Assert.Equal(0.8f, merged.VadMinSilenceDuration);
        Assert.Equal(0.5f, merged.VadPrefixPaddingDuration);  // Original preserved
        Assert.Equal(0.5f, merged.VadActivationThreshold);     // Original preserved
    }

    [Fact]
    public void MergeWith_OverridesTurnDetectionSettings()
    {
        // Arrange
        var original = new AudioPipelineConfig
        {
            SilenceStrategy = TurnDetectionStrategy.FastPath,
            MlStrategy = TurnDetectionStrategy.OnAmbiguous,
            SilenceFastPathThreshold = 1.5f,
            MinEndpointingDelay = 0.3f,
            MaxEndpointingDelay = 1.5f
        };

        var overrides = new AudioPipelineConfig
        {
            SilenceStrategy = TurnDetectionStrategy.Disabled,
            MinEndpointingDelay = 0.5f
        };

        // Act
        var merged = original.MergeWith(overrides);

        // Assert
        Assert.Equal(TurnDetectionStrategy.Disabled, merged.SilenceStrategy);
        Assert.Equal(0.5f, merged.MinEndpointingDelay);
        Assert.Equal(TurnDetectionStrategy.OnAmbiguous, merged.MlStrategy);  // Original preserved
        Assert.Equal(1.5f, merged.SilenceFastPathThreshold);                  // Original preserved
        Assert.Equal(1.5f, merged.MaxEndpointingDelay);                       // Original preserved
    }

    [Fact]
    public void MergeWith_OverridesSilenceBoostSettings()
    {
        // Arrange
        var original = new AudioPipelineConfig
        {
            SilenceBoostMultiplier = 0.7f,
            UseCombinedProbability = true
        };

        var overrides = new AudioPipelineConfig
        {
            SilenceBoostMultiplier = 0.9f,
            UseCombinedProbability = false
        };

        // Act
        var merged = original.MergeWith(overrides);

        // Assert
        Assert.Equal(0.9f, merged.SilenceBoostMultiplier);
        Assert.False(merged.UseCombinedProbability);
    }

    [Fact]
    public void MergeWith_OverridesTrailingWordsSettings()
    {
        // Arrange
        var original = new AudioPipelineConfig
        {
            CustomTrailingWords = null,
            TrailingWordPenalty = 0.6f
        };

        var customWords = new HashSet<string> { "um", "uh", "like" };
        var overrides = new AudioPipelineConfig
        {
            CustomTrailingWords = customWords,
            TrailingWordPenalty = 0.8f
        };

        // Act
        var merged = original.MergeWith(overrides);

        // Assert
        Assert.Equal(customWords, merged.CustomTrailingWords);
        Assert.Equal(0.8f, merged.TrailingWordPenalty);
    }

    [Fact]
    public void MergeWith_OverridesFeatureFlags()
    {
        // Arrange
        var original = new AudioPipelineConfig
        {
            EnableQuickAnswer = true,
            EnableSpeedAdaptation = true
        };

        var overrides = new AudioPipelineConfig
        {
            EnableQuickAnswer = false,
            EnableSpeedAdaptation = false
        };

        // Act
        var merged = original.MergeWith(overrides);

        // Assert
        Assert.False(merged.EnableQuickAnswer);
        Assert.False(merged.EnableSpeedAdaptation);
    }

    [Fact]
    public void MergeWith_OverridesBackchannelSettings()
    {
        // Arrange
        var original = new AudioPipelineConfig
        {
            BackchannelStrategy = BackchannelStrategy.IgnoreShortUtterances,
            MinWordsForInterruption = 2
        };

        var overrides = new AudioPipelineConfig
        {
            BackchannelStrategy = BackchannelStrategy.InterruptImmediately,
            MinWordsForInterruption = 5
        };

        // Act
        var merged = original.MergeWith(overrides);

        // Assert
        Assert.Equal(BackchannelStrategy.InterruptImmediately, merged.BackchannelStrategy);
        Assert.Equal(5, merged.MinWordsForInterruption);
    }

    [Fact]
    public void MergeWith_OverridesFalseInterruptionRecoverySettings()
    {
        // Arrange
        var original = new AudioPipelineConfig
        {
            EnableFalseInterruptionRecovery = true,
            FalseInterruptionTimeout = 2.0f,
            ResumeFalseInterruption = true,
            MaxBufferedChunksDuringPause = 100
        };

        var overrides = new AudioPipelineConfig
        {
            EnableFalseInterruptionRecovery = false,
            FalseInterruptionTimeout = 3.0f
        };

        // Act
        var merged = original.MergeWith(overrides);

        // Assert
        Assert.False(merged.EnableFalseInterruptionRecovery);
        Assert.Equal(3.0f, merged.FalseInterruptionTimeout);
        Assert.True(merged.ResumeFalseInterruption);        // Original preserved
        Assert.Equal(100, merged.MaxBufferedChunksDuringPause); // Original preserved
    }

    [Fact]
    public void MergeWith_OverridesFillerAudioSettings()
    {
        // Arrange
        var original = new AudioPipelineConfig
        {
            EnableFillerAudio = false,
            FillerSilenceThreshold = 1.5f,
            FillerPhrases = ["Um...", "Let me see..."],
            FillerSelectionStrategy = FillerStrategy.Random,
            MaxFillerPlaysPerTurn = 1,
            FillerVoice = null,
            FillerSpeed = 0.95f
        };

        var newPhrases = new[] { "Hmm...", "Hold on..." };
        var overrides = new AudioPipelineConfig
        {
            EnableFillerAudio = true,
            FillerPhrases = newPhrases,
            FillerSelectionStrategy = FillerStrategy.RoundRobin,
            FillerVoice = "nova"
        };

        // Act
        var merged = original.MergeWith(overrides);

        // Assert
        Assert.True(merged.EnableFillerAudio);
        Assert.Equal(newPhrases, merged.FillerPhrases);
        Assert.Equal(FillerStrategy.RoundRobin, merged.FillerSelectionStrategy);
        Assert.Equal("nova", merged.FillerVoice);
        Assert.Equal(1.5f, merged.FillerSilenceThreshold);  // Original preserved
        Assert.Equal(1, merged.MaxFillerPlaysPerTurn);      // Original preserved
        Assert.Equal(0.95f, merged.FillerSpeed);             // Original preserved
    }

    [Fact]
    public void MergeWith_OverridesTextFilteringSettings()
    {
        // Arrange
        var original = new AudioPipelineConfig
        {
            EnableTextFiltering = true,
            FilterCodeBlocks = true,
            FilterTables = true,
            FilterUrls = true,
            FilterMarkdownFormatting = true,
            FilterEmoji = true
        };

        var overrides = new AudioPipelineConfig
        {
            EnableTextFiltering = false,
            FilterCodeBlocks = false
        };

        // Act
        var merged = original.MergeWith(overrides);

        // Assert
        Assert.False(merged.EnableTextFiltering);
        Assert.False(merged.FilterCodeBlocks);
        Assert.True(merged.FilterTables);              // Original preserved
        Assert.True(merged.FilterUrls);                // Original preserved
        Assert.True(merged.FilterMarkdownFormatting);  // Original preserved
        Assert.True(merged.FilterEmoji);               // Original preserved
    }

    [Fact]
    public void MergeWith_OverridesPreemptiveGenerationSettings()
    {
        // Arrange
        var original = new AudioPipelineConfig
        {
            EnablePreemptiveGeneration = false,
            PreemptiveGenerationThreshold = 0.7f
        };

        var overrides = new AudioPipelineConfig
        {
            EnablePreemptiveGeneration = true,
            PreemptiveGenerationThreshold = 0.85f
        };

        // Act
        var merged = original.MergeWith(overrides);

        // Assert
        Assert.True(merged.EnablePreemptiveGeneration);
        Assert.Equal(0.85f, merged.PreemptiveGenerationThreshold);
    }

    [Fact]
    public void MergeWith_ComplexScenario_MergesCorrectly()
    {
        // Arrange - Middleware defaults
        var middlewareDefaults = new AudioPipelineConfig
        {
            Voice = "alloy",
            Model = "tts-1",
            EnableQuickAnswer = true,
            EnableFillerAudio = false,
            VadMinSilenceDuration = 0.55f,
            SilenceStrategy = TurnDetectionStrategy.FastPath
        };

        // Per-request overrides
        var perRequestOverrides = new AudioPipelineConfig
        {
            Voice = "nova",              // Override
            EnableFillerAudio = true,    // Override
            FillerPhrases = ["Thinking..."] // Override
            // Model, EnableQuickAnswer, VadMinSilenceDuration, SilenceStrategy not set
        };

        // Act
        var effectiveConfig = middlewareDefaults.MergeWith(perRequestOverrides);

        // Assert - Overridden values
        Assert.Equal("nova", effectiveConfig.Voice);
        Assert.True(effectiveConfig.EnableFillerAudio);
        Assert.Single(effectiveConfig.FillerPhrases!);
        Assert.Equal("Thinking...", effectiveConfig.FillerPhrases![0]);

        // Assert - Middleware defaults preserved
        Assert.Equal("tts-1", effectiveConfig.Model);
        Assert.True(effectiveConfig.EnableQuickAnswer);
        Assert.Equal(0.55f, effectiveConfig.VadMinSilenceDuration);
        Assert.Equal(TurnDetectionStrategy.FastPath, effectiveConfig.SilenceStrategy);
    }

    [Fact]
    public void MergeWith_DoesNotMutateOriginal()
    {
        // Arrange
        var original = new AudioPipelineConfig
        {
            Voice = "alloy",
            EnableQuickAnswer = true
        };

        var overrides = new AudioPipelineConfig
        {
            Voice = "nova",
            EnableQuickAnswer = false
        };

        // Act
        var merged = original.MergeWith(overrides);

        // Assert - Original unchanged
        Assert.Equal("alloy", original.Voice);
        Assert.True(original.EnableQuickAnswer);

        // Assert - Merged has new values
        Assert.Equal("nova", merged.Voice);
        Assert.False(merged.EnableQuickAnswer);
    }

    [Fact]
    public void FillerStrategy_HasExpectedValues()
    {
        // Assert
        Assert.Equal(0, (int)FillerStrategy.Random);
        Assert.Equal(1, (int)FillerStrategy.RoundRobin);
        Assert.Equal(2, (int)FillerStrategy.ShortestFirst);
    }
    */
}
