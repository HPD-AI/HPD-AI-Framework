// Copyright 2026 Einstein Essibu
// SPDX-License-Identifier: AGPL-3.0-only

using HPD.Agent.Audio.Stt;
using HPD.Agent.Audio.Tts;
using Microsoft.Extensions.AI;

namespace HPD.Agent.Audio.Tests;

public class AudioOptionsMappingTests
{
    [Fact]
    public void SttConfig_ToOptions_MapsMicrosoftExtensionsAiOptions()
    {
        var config = new SttConfig
        {
            Provider = "openai-audio",
            ModelId = "whisper-1",
            Language = "en",
            SpeechSampleRate = 16000,
            TextLanguage = "es",
            Temperature = 0.2f,
            ResponseFormat = "verbose_json",
            AdditionalProperties = new Dictionary<string, object>
            {
                ["prompt"] = "HPD"
            }
        };

        var options = config.ToOptions();

        Assert.Equal("whisper-1", options.ModelId);
        Assert.Equal("en", options.SpeechLanguage);
        Assert.Equal(16000, options.SpeechSampleRate);
        Assert.Equal("es", options.TextLanguage);
        Assert.Equal(0.2f, options.AdditionalProperties?["temperature"]);
        Assert.Equal("verbose_json", options.AdditionalProperties?["responseFormat"]);
        Assert.Equal("HPD", options.AdditionalProperties?["prompt"]);
    }

    [Fact]
    public void TtsConfig_ToOptions_MapsMicrosoftExtensionsAiOptions()
    {
        var config = new TtsConfig
        {
            Provider = "openai-audio",
            ModelId = "tts-1-hd",
            Voice = "nova",
            Language = "en-US",
            OutputFormat = "audio/opus",
            Speed = 1.15f,
            Pitch = 0.95f,
            Volume = 0.8f,
            SampleRate = 24000,
            AdditionalProperties = new Dictionary<string, object>
            {
                ["style"] = "warm"
            }
        };

        var options = config.ToOptions();

        Assert.Equal("tts-1-hd", options.ModelId);
        Assert.Equal("nova", options.VoiceId);
        Assert.Equal("en-US", options.Language);
        Assert.Equal("audio/opus", options.AudioFormat);
        Assert.Equal(1.15f, options.Speed);
        Assert.Equal(0.95f, options.Pitch);
        Assert.Equal(0.8f, options.Volume);
        Assert.Equal(24000, options.AdditionalProperties?["sampleRate"]);
        Assert.Equal("warm", options.AdditionalProperties?["style"]);
    }
}
