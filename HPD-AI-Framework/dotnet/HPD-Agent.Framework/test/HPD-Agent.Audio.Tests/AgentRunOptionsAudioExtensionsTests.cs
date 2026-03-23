// Copyright (c) 2025 Einstein Essibu. All rights reserved.

using HPD.Agent;
using HPD.Agent.Audio;
using HPD.Agent.Audio.Tts;
using FluentAssertions;

namespace HPD.Agent.Audio.Tests;

/// <summary>
/// Tests for AgentRunConfig audio extension methods.
/// </summary>
public class AgentRunConfigAudioExtensionsTests
{
    [Fact]
    public void WithAudio_SetsAudioRunConfig()
    {
        // Arrange
        var options = new AgentRunConfig();
        var audioOptions = new AudioRunConfig { Language = "en" };

        // Act
        var result = options.WithAudio(audioOptions);

        // Assert
        result.Should().BeSameAs(options, "extension method should return the same instance for chaining");
        options.Audio.Should().BeSameAs(audioOptions);
    }

    [Fact]
    public void WithAudio_ConfigureAction_SetsAudioRunConfig()
    {
        // Arrange
        var options = new AgentRunConfig();

        // Act
        var result = options.WithAudio(config =>
        {
            config.Language = "es";
            config.ProcessingMode = AudioProcessingMode.Native;
        });

        // Assert
        result.Should().BeSameAs(options);
        var audioOptions = options.GetAudioRunConfig();
        audioOptions.Should().NotBeNull();
        audioOptions!.Language.Should().Be("es");
        audioOptions.ProcessingMode.Should().Be(AudioProcessingMode.Native);
    }

    [Fact]
    public void WithVoiceConversation_SetsCorrectIOMode()
    {
        // Arrange
        var options = new AgentRunConfig();

        // Act
        var result = options.WithVoiceConversation();

        // Assert
        result.Should().BeSameAs(options);
        var audioOptions = options.GetAudioRunConfig();
        audioOptions.Should().NotBeNull();
        audioOptions!.IOMode.Should().Be(AudioIOMode.AudioToAudioAndText);
    }

    [Fact]
    public void WithVoiceInput_SetsCorrectIOMode()
    {
        // Arrange
        var options = new AgentRunConfig();

        // Act
        var result = options.WithVoiceInput();

        // Assert
        result.Should().BeSameAs(options);
        var audioOptions = options.GetAudioRunConfig();
        audioOptions.Should().NotBeNull();
        audioOptions!.IOMode.Should().Be(AudioIOMode.AudioToText);
    }

    [Fact]
    public void WithVoiceOutput_SetsCorrectIOMode()
    {
        // Arrange
        var options = new AgentRunConfig();

        // Act
        var result = options.WithVoiceOutput();

        // Assert
        result.Should().BeSameAs(options);
        var audioOptions = options.GetAudioRunConfig();
        audioOptions.Should().NotBeNull();
        audioOptions!.IOMode.Should().Be(AudioIOMode.TextToAudioAndText);
    }

    [Fact]
    public void WithTextOnly_DisablesAudio()
    {
        // Arrange
        var options = new AgentRunConfig();

        // Act
        var result = options.WithTextOnly();

        // Assert
        result.Should().BeSameAs(options);
        var audioOptions = options.GetAudioRunConfig();
        audioOptions.Should().NotBeNull();
        audioOptions!.Disabled.Should().Be(true);
    }

    [Fact]
    public void WithFullVoice_SetsCorrectIOMode()
    {
        // Arrange
        var options = new AgentRunConfig();

        // Act
        var result = options.WithFullVoice();

        // Assert
        result.Should().BeSameAs(options);
        var audioOptions = options.GetAudioRunConfig();
        audioOptions.Should().NotBeNull();
        audioOptions!.IOMode.Should().Be(AudioIOMode.AudioToAudio);
    }

    [Fact]
    public void WithNativeAudio_SetsNativeProcessingModeAndCorrectIOMode()
    {
        // Arrange
        var options = new AgentRunConfig();

        // Act
        var result = options.WithNativeAudio();

        // Assert
        result.Should().BeSameAs(options);
        var audioOptions = options.GetAudioRunConfig();
        audioOptions.Should().NotBeNull();
        audioOptions!.ProcessingMode.Should().Be(AudioProcessingMode.Native);
        audioOptions.IOMode.Should().Be(AudioIOMode.AudioToAudioAndText);
    }

    [Fact]
    public void WithVoice_SetsVoiceShortcut()
    {
        // Arrange
        var options = new AgentRunConfig();

        // Act
        var result = options.WithVoice("nova");

        // Assert
        result.Should().BeSameAs(options);
        var audioOptions = options.GetAudioRunConfig();
        audioOptions.Should().NotBeNull();
        audioOptions!.Voice.Should().Be("nova");
    }

    [Fact]
    public void WithTtsModel_SetsTtsModelShortcut()
    {
        // Arrange
        var options = new AgentRunConfig();

        // Act
        var result = options.WithTtsModel("tts-1-hd");

        // Assert
        result.Should().BeSameAs(options);
        var audioOptions = options.GetAudioRunConfig();
        audioOptions.Should().NotBeNull();
        audioOptions!.TtsModel.Should().Be("tts-1-hd");
    }

    [Fact]
    public void WithTtsSpeed_SetsTtsSpeedShortcut()
    {
        // Arrange
        var options = new AgentRunConfig();

        // Act
        var result = options.WithTtsSpeed(1.5f);

        // Assert
        result.Should().BeSameAs(options);
        var audioOptions = options.GetAudioRunConfig();
        audioOptions.Should().NotBeNull();
        audioOptions!.TtsSpeed.Should().Be(1.5f);
    }

    [Fact]
    public void GetAudioRunConfig_ReturnsNull_WhenNotSet()
    {
        // Arrange
        var options = new AgentRunConfig();

        // Act
        var audioOptions = options.GetAudioRunConfig();

        // Assert
        audioOptions.Should().BeNull();
    }

    [Fact]
    public void GetAudioRunConfig_ReturnsAudioRunConfig_WhenSet()
    {
        // Arrange
        var options = new AgentRunConfig();
        var audioRunConfig = new AudioRunConfig { Language = "fr" };
        options.WithAudio(audioRunConfig);

        // Act
        var result = options.GetAudioRunConfig();

        // Assert
        result.Should().BeSameAs(audioRunConfig);
    }

    [Fact]
    public void ExtensionMethods_CanBeChained()
    {
        // Arrange
        var options = new AgentRunConfig();

        // Act
        var result = options
            .WithVoiceConversation()
            .WithVoice("alloy")
            .WithTtsSpeed(1.2f);

        // Assert
        result.Should().BeSameAs(options);
        var audioOptions = options.GetAudioRunConfig();
        audioOptions.Should().NotBeNull();
        audioOptions!.IOMode.Should().Be(AudioIOMode.AudioToAudioAndText);
        audioOptions.Voice.Should().Be("alloy");
        audioOptions.TtsSpeed.Should().Be(1.2f);
    }
}
