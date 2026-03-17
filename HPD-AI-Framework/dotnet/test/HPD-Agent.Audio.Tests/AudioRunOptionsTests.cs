// Copyright (c) 2025 Einstein Essibu. All rights reserved.

using HPD.Agent.Audio;
using HPD.Agent.Audio.Tts;
using HPD.Agent.Audio.Stt;
using HPD.Agent.Audio.Vad;
using System.Text.Json;
using Xunit;

namespace HPD.Agent.Tests.Audio;

/// <summary>
/// Unit tests for AudioRunConfig (slim runtime API).
/// </summary>
public class AudioRunConfigTests
{
    [Fact]
    public void AllPipelinePropertiesCanBeSet()
    {
        // Pipeline mode — all role configs and shortcuts are valid together
        var options = new AudioRunConfig
        {
            ProcessingMode = AudioProcessingMode.Pipeline,
            IOMode = AudioIOMode.AudioToAudio,
            Voice = "alloy",
            TtsModel = "tts-1-hd",
            TtsSpeed = 1.5f,
            Language = "en",
            Disabled = false,
            Tts = new TtsConfig { Provider = "openai" },
            Stt = new SttConfig { Provider = "openai" },
            Vad = new VadConfig { Provider = "silero" }
        };

        // Assert
        Assert.Equal(AudioProcessingMode.Pipeline, options.ProcessingMode);
        Assert.Equal(AudioIOMode.AudioToAudio, options.IOMode);
        Assert.Equal("alloy", options.Voice);
        Assert.Equal("tts-1-hd", options.TtsModel);
        Assert.Equal(1.5f, options.TtsSpeed);
        Assert.Equal("en", options.Language);
        Assert.False(options.Disabled);
        Assert.NotNull(options.Tts);
        Assert.Equal("openai", options.Tts.Provider);
        Assert.NotNull(options.Stt);
        Assert.Equal("openai", options.Stt.Provider);
        Assert.NotNull(options.Vad);
        Assert.Equal("silero", options.Vad.Provider);
    }

    [Fact]
    public void AllNativePropertiesCanBeSet()
    {
        // Native mode — only ProcessingMode, IOMode, Language, Disabled are valid
        var options = new AudioRunConfig
        {
            ProcessingMode = AudioProcessingMode.Native,
            IOMode = AudioIOMode.AudioToAudio,
            Language = "en",
            Disabled = false
        };

        // Assert
        Assert.Equal(AudioProcessingMode.Native, options.ProcessingMode);
        Assert.Equal(AudioIOMode.AudioToAudio, options.IOMode);
        Assert.Equal("en", options.Language);
        Assert.False(options.Disabled);
    }

    [Fact]
    public void DefaultValues_AreAllNull()
    {
        // Act
        var options = new AudioRunConfig();

        // Assert - all properties should be null (use middleware defaults)
        Assert.Null(options.ProcessingMode);
        Assert.Null(options.IOMode);
        Assert.Null(options.Voice);
        Assert.Null(options.TtsModel);
        Assert.Null(options.TtsSpeed);
        Assert.Null(options.Language);
        Assert.Null(options.Disabled);
        Assert.Null(options.Tts);
        Assert.Null(options.Stt);
        Assert.Null(options.Vad);
    }

    [Fact]
    public void ToFullConfig_ConvertsAllProperties()
    {
        // Pipeline mode — all properties valid
        var options = new AudioRunConfig
        {
            ProcessingMode = AudioProcessingMode.Pipeline,
            IOMode = AudioIOMode.AudioToAudioAndText,
            Voice = "nova",
            TtsModel = "tts-1-hd",
            TtsSpeed = 1.2f,
            Language = "es",
            Disabled = true,
            Tts = new TtsConfig { Provider = "elevenlabs", Voice = "Rachel" }
        };

        // Act
        var config = options.ToFullConfig();

        // Assert - direct mappings
        Assert.Equal(AudioProcessingMode.Pipeline, config.ProcessingMode);
        Assert.Equal(AudioIOMode.AudioToAudioAndText, config.IOMode);
        Assert.Equal("es", config.Language);
        Assert.True(config.Disabled);

        // Assert - TTS config with shortcuts merged
        Assert.NotNull(config.Tts);
        Assert.Equal("elevenlabs", config.Tts.Provider);
        Assert.Equal("Rachel", config.Tts.Voice); // Original value preserved
        Assert.Equal("tts-1-hd", config.Tts.ModelId); // Shortcut applied
        Assert.Equal(1.2f, config.Tts.Speed); // Shortcut applied
    }

    [Fact]
    public void ToFullConfig_NativeMode_NoRoleConfigs_Succeeds()
    {
        // Native mode with only IOMode/Language is valid
        var options = new AudioRunConfig
        {
            ProcessingMode = AudioProcessingMode.Native,
            IOMode = AudioIOMode.AudioToAudioAndText,
            Language = "es"
        };

        // Act
        var config = options.ToFullConfig();

        // Assert
        Assert.Equal(AudioProcessingMode.Native, config.ProcessingMode);
        Assert.Equal(AudioIOMode.AudioToAudioAndText, config.IOMode);
        Assert.Equal("es", config.Language);
        Assert.Null(config.Tts);
        Assert.Null(config.Stt);
        Assert.Null(config.Vad);
    }

    [Fact]
    public void ToFullConfig_ShortcutsOnlyAppliedWhenNotNull()
    {
        // Arrange - shortcuts set, but no Tts config
        var options = new AudioRunConfig
        {
            Voice = "nova",
            TtsModel = "tts-1",
            TtsSpeed = 1.5f
        };

        // Act
        var config = options.ToFullConfig();

        // Assert - Tts config created with shortcuts
        Assert.NotNull(config.Tts);
        Assert.Equal("nova", config.Tts.Voice);
        Assert.Equal("tts-1", config.Tts.ModelId);
        Assert.Equal(1.5f, config.Tts.Speed);
    }

    [Fact]
    public void ToFullConfig_ShortcutsDoNotOverrideExistingValues()
    {
        // Arrange - Tts config has Voice, shortcut also has Voice
        var options = new AudioRunConfig
        {
            Voice = "alloy",
            Tts = new TtsConfig { Provider = "openai", Voice = "nova" } // This should win
        };

        // Act
        var config = options.ToFullConfig();

        // Assert - original Tts.Voice preserved
        Assert.NotNull(config.Tts);
        Assert.Equal("nova", config.Tts.Voice); // NOT "alloy"
    }

    [Fact]
    public void ToFullConfig_RoleConfigsPreserved()
    {
        // Arrange
        var ttsConfig = new TtsConfig { Provider = "openai", Voice = "alloy" };
        var sttConfig = new SttConfig { Provider = "openai", Language = "en" };
        var vadConfig = new VadConfig { Provider = "silero" };

        var options = new AudioRunConfig
        {
            Tts = ttsConfig,
            Stt = sttConfig,
            Vad = vadConfig
        };

        // Act
        var config = options.ToFullConfig();

        // Assert - same instances preserved
        Assert.Same(ttsConfig, config.Tts);
        Assert.Same(sttConfig, config.Stt);
        Assert.Same(vadConfig, config.Vad);
    }

    [Fact]
    public void Validate_ValidOptions_DoesNotThrow()
    {
        // Arrange
        var options = new AudioRunConfig
        {
            Voice = "nova",
            TtsSpeed = 1.0f,
            Language = "en"
        };

        // Act & Assert
        options.Validate();
    }

    [Fact]
    public void Validate_InvalidTtsSpeed_Throws()
    {
        // Arrange - speed too low
        var options1 = new AudioRunConfig { TtsSpeed = 0.1f };

        // Act & Assert
        Assert.Throws<ArgumentException>(() => options1.Validate());

        // Arrange - speed too high
        var options2 = new AudioRunConfig { TtsSpeed = 5.0f };

        // Act & Assert
        Assert.Throws<ArgumentException>(() => options2.Validate());
    }

    [Fact]
    public void Validate_ValidTtsSpeedRange_DoesNotThrow()
    {
        // Arrange - min valid
        var options1 = new AudioRunConfig { TtsSpeed = 0.25f };
        // Arrange - max valid
        var options2 = new AudioRunConfig { TtsSpeed = 4.0f };

        // Act & Assert
        options1.Validate();
        options2.Validate();
    }

    [Fact]
    public void JsonSerialization_RoundTrip()
    {
        // Arrange
        var options = new AudioRunConfig
        {
            ProcessingMode = AudioProcessingMode.Pipeline,
            IOMode = AudioIOMode.AudioToAudioAndText,
            Voice = "nova",
            TtsModel = "tts-1",
            TtsSpeed = 1.2f,
            Language = "fr"
        };

        // Act
        var json = JsonSerializer.Serialize(options, AudioJsonContext.Default.AudioRunConfig);
        var deserialized = JsonSerializer.Deserialize(json, AudioJsonContext.Default.AudioRunConfig);

        // Assert
        Assert.NotNull(deserialized);
        Assert.Equal(options.ProcessingMode, deserialized.ProcessingMode);
        Assert.Equal(options.IOMode, deserialized.IOMode);
        Assert.Equal(options.Voice, deserialized.Voice);
        Assert.Equal(options.TtsModel, deserialized.TtsModel);
        Assert.Equal(options.TtsSpeed, deserialized.TtsSpeed);
        Assert.Equal(options.Language, deserialized.Language);
    }

    [Fact]
    public void JsonSerialization_NullValuesOmitted()
    {
        // Arrange - only set Voice
        var options = new AudioRunConfig { Voice = "nova" };

        // Act
        var json = JsonSerializer.Serialize(options, AudioJsonContext.Default.AudioRunConfig);

        // Assert - should only contain non-null properties
        Assert.Contains("\"voice\"", json);
        Assert.DoesNotContain("\"processingMode\"", json);
        Assert.DoesNotContain("\"ioMode\"", json);
        Assert.DoesNotContain("\"ttsModel\"", json);
    }

    [Fact]
    public void Validate_Native_WithStt_Throws()
    {
        var options = new AudioRunConfig
        {
            ProcessingMode = AudioProcessingMode.Native,
            Stt = new SttConfig()
        };
        Assert.Throws<InvalidOperationException>(() => options.Validate());
    }

    [Fact]
    public void Validate_Native_WithTts_Throws()
    {
        var options = new AudioRunConfig
        {
            ProcessingMode = AudioProcessingMode.Native,
            Tts = new TtsConfig()
        };
        Assert.Throws<InvalidOperationException>(() => options.Validate());
    }

    [Fact]
    public void Validate_Native_WithVad_Throws()
    {
        var options = new AudioRunConfig
        {
            ProcessingMode = AudioProcessingMode.Native,
            Vad = new VadConfig()
        };
        Assert.Throws<InvalidOperationException>(() => options.Validate());
    }

    [Fact]
    public void Validate_Native_WithVoiceShortcut_Throws()
    {
        var options = new AudioRunConfig
        {
            ProcessingMode = AudioProcessingMode.Native,
            Voice = "alloy"
        };
        Assert.Throws<InvalidOperationException>(() => options.Validate());
    }

    [Fact]
    public void Validate_Native_WithTtsModelShortcut_Throws()
    {
        var options = new AudioRunConfig
        {
            ProcessingMode = AudioProcessingMode.Native,
            TtsModel = "tts-1"
        };
        Assert.Throws<InvalidOperationException>(() => options.Validate());
    }

    [Fact]
    public void Validate_Native_WithTtsSpeedShortcut_Throws()
    {
        var options = new AudioRunConfig
        {
            ProcessingMode = AudioProcessingMode.Native,
            TtsSpeed = 1.2f
        };
        Assert.Throws<InvalidOperationException>(() => options.Validate());
    }

    [Fact]
    public void Validate_Native_WithOnlyIOModeAndLanguage_Succeeds()
    {
        var options = new AudioRunConfig
        {
            ProcessingMode = AudioProcessingMode.Native,
            IOMode = AudioIOMode.AudioToAudio,
            Language = "fr"
        };
        // Should not throw
        options.Validate();
    }

    [Fact]
    public void ToFullConfig_Native_WithVoiceShortcut_Throws()
    {
        // Validate is called inside ToFullConfig — shortcut conflict caught before config is built
        var options = new AudioRunConfig
        {
            ProcessingMode = AudioProcessingMode.Native,
            Voice = "nova"
        };
        Assert.Throws<InvalidOperationException>(() => options.ToFullConfig());
    }
}
