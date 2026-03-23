// Copyright (c) 2025 Einstein Essibu. All rights reserved.

using HPD.Agent.Audio.Stt;
using HPD.Agent.Audio.Tts;
using FluentAssertions;

namespace HPD.Agent.Audio.Tests;

/// <summary>
/// Integration tests for module initializers and provider discovery.
/// These tests verify that audio providers are properly registered via [ModuleInitializer].
/// </summary>
public class ModuleInitializerTests
{
    [Fact]
    public void OpenAI_TtsProvider_IsRegistered()
    {
        // Act
        var factory = TtsProviderDiscovery.GetFactory("openai-audio");

        // Assert
        factory.Should().NotBeNull("OpenAI TTS provider should be auto-registered by module initializer");
    }

    [Fact]
    public void OpenAI_SttProvider_IsRegistered()
    {
        // Act
        var factory = SttProviderDiscovery.GetFactory("openai-audio");

        // Assert
        factory.Should().NotBeNull("OpenAI STT provider should be auto-registered by module initializer");
    }

    [Fact]
    public void ElevenLabs_TtsProvider_IsRegistered()
    {
        // Act
        var factory = TtsProviderDiscovery.GetFactory("elevenlabs");

        // Assert
        factory.Should().NotBeNull("ElevenLabs TTS provider should be auto-registered by module initializer");
    }

    [Fact]
    public void OpenAI_TtsConfigType_IsRegistered()
    {
        // Act
        var configType = TtsProviderDiscovery.GetConfigType("openai-audio");

        // Assert
        configType.Should().NotBeNull("OpenAI TTS config type should be registered");
        configType!.Name.Should().Be("OpenAITtsConfig");
    }

    [Fact]
    public void OpenAI_SttConfigType_IsRegistered()
    {
        // Act
        var configType = SttProviderDiscovery.GetConfigType("openai-audio");

        // Assert
        configType.Should().NotBeNull("OpenAI STT config type should be registered");
        configType!.Name.Should().Be("OpenAISttConfig");
    }

    [Fact]
    public void ElevenLabs_TtsConfigType_IsRegistered()
    {
        // Act
        var configType = TtsProviderDiscovery.GetConfigType("elevenlabs");

        // Assert
        configType.Should().NotBeNull("ElevenLabs TTS config type should be registered");
        configType!.Name.Should().Be("ElevenLabsTtsConfig");
    }

    [Fact]
    public void UnknownProvider_ThrowsException()
    {
        // Act
        var act = () => TtsProviderDiscovery.GetFactory("nonexistent-provider");

        // Assert
        act.Should().Throw<InvalidOperationException>()
            .WithMessage("TTS provider 'nonexistent-provider' not found*");
    }
}
