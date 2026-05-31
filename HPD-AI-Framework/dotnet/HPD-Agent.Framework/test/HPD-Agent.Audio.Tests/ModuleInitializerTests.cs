// Copyright (c) 2025 Einstein Essibu. All rights reserved.

using FluentAssertions;
using HPD.Agent;
using HPD.Agent.AudioProviders.ElevenLabs;
using HPD.Agent.AudioProviders.ElevenLabs.Tts;
using HPD.Agent.AudioProviders.OpenAI;
using HPD.Agent.AudioProviders.OpenAI.Stt;
using HPD.Agent.AudioProviders.OpenAI.Tts;
using HPD.Agent.Providers;

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
        var provider = new AgentBuilder().ProviderRegistry.GetProvider<ITextToSpeechClientProvider>("openai");

        // Assert
        provider.Should().NotBeNull("OpenAI TTS provider should be auto-registered by module initializer");
    }

    [Fact]
    public void OpenAI_SttProvider_IsRegistered()
    {
        // Act
        var provider = new AgentBuilder().ProviderRegistry.GetProvider<ISpeechToTextClientProvider>("openai");

        // Assert
        provider.Should().NotBeNull("OpenAI STT provider should be auto-registered by module initializer");
    }

    [Fact]
    public void ElevenLabs_TtsProvider_IsRegistered()
    {
        // Act
        var provider = new AgentBuilder().ProviderRegistry.GetProvider<ITextToSpeechClientProvider>("elevenlabs");

        // Assert
        provider.Should().NotBeNull("ElevenLabs TTS provider should be auto-registered by module initializer");
    }

    [Fact]
    public void OpenAI_TtsConfigType_IsRegistered()
    {
        // Act
        var configType = ProviderDiscovery.GetProviderConfigType("openai", ProviderClientFamily.TextToSpeech);

        // Assert
        configType.Should().NotBeNull("OpenAI TTS config type should be registered");
        configType!.ConfigType.Should().Be(typeof(OpenAITtsConfig));
    }

    [Fact]
    public void OpenAI_SttConfigType_IsRegistered()
    {
        // Act
        var configType = ProviderDiscovery.GetProviderConfigType("openai", ProviderClientFamily.SpeechToText);

        // Assert
        configType.Should().NotBeNull("OpenAI STT config type should be registered");
        configType!.ConfigType.Should().Be(typeof(OpenAISttConfig));
    }

    [Fact]
    public void ElevenLabs_TtsConfigType_IsRegistered()
    {
        // Act
        var configType = ProviderDiscovery.GetProviderConfigType("elevenlabs", ProviderClientFamily.TextToSpeech);

        // Assert
        configType.Should().NotBeNull("ElevenLabs TTS config type should be registered");
        configType!.ConfigType.Should().Be(typeof(ElevenLabsTtsConfig));
    }

    [Fact]
    public void UnknownProvider_ReturnsNull()
    {
        // Act
        var provider = new AgentBuilder().ProviderRegistry.GetProvider<ITextToSpeechClientProvider>("nonexistent-provider");

        // Assert
        provider.Should().BeNull();
    }
}
