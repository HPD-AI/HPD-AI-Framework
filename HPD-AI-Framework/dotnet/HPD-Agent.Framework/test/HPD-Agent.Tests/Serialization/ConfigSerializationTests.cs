using System.Text.Json;
using HPD.Agent;
using HPD.Agent.Audio.Output;
using HPD.Agent.Audio.Policies;
using Xunit;

namespace HPD.Agent.Tests.Serialization;

/// <summary>
/// Tests for config serialization.
/// Verifies that AgentConfig with Harneses and Middlewares can be
/// serialized to JSON and deserialized back.
/// </summary>
public class ConfigSerializationTests
{
    [Fact]
    public void HarnessReference_SimpleString_RoundTrip()
    {
        // Arrange
        var reference = new HarnessReference { Name = "MathHarness" };
        var options = new JsonSerializerOptions { WriteIndented = true };

        // Act
        var json = JsonSerializer.Serialize(reference, options);
        var deserialized = JsonSerializer.Deserialize<HarnessReference>(json);

        // Assert
        Assert.NotNull(deserialized);
        Assert.Equal("MathHarness", deserialized.Name);
        Assert.Null(deserialized.Functions);
        Assert.Null(deserialized.Config);
        Assert.Null(deserialized.Metadata);
    }

    [Fact]
    public void HarnessReference_ImplicitConversion_FromString()
    {
        // Arrange & Act
        HarnessReference reference = "SearchHarness";

        // Assert
        Assert.Equal("SearchHarness", reference.Name);
    }

    [Fact]
    public void HarnessReference_RichSyntax_RoundTrip()
    {
        // Arrange
        var json = """
            {
              "name": "FileHarness",
              "functions": ["ReadFile", "WriteFile"],
              "config": { "basePath": "/tmp" },
              "metadata": { "allowDelete": false }
            }
            """;

        // Act
        var reference = JsonSerializer.Deserialize<HarnessReference>(json);
        var serialized = JsonSerializer.Serialize(reference);
        var roundTripped = JsonSerializer.Deserialize<HarnessReference>(serialized);

        // Assert
        Assert.NotNull(reference);
        Assert.Equal("FileHarness", reference.Name);
        Assert.NotNull(reference.Functions);
        Assert.Equal(2, reference.Functions.Count);
        Assert.Contains("ReadFile", reference.Functions);
        Assert.Contains("WriteFile", reference.Functions);
        Assert.True(reference.Config.HasValue);
        Assert.True(reference.Metadata.HasValue);

        Assert.NotNull(roundTripped);
        Assert.Equal("FileHarness", roundTripped.Name);
    }

    [Fact]
    public void MiddlewareReference_SimpleString_RoundTrip()
    {
        // Arrange
        var reference = new MiddlewareReference { Name = "LoggingMiddleware" };
        var options = new JsonSerializerOptions { WriteIndented = true };

        // Act
        var json = JsonSerializer.Serialize(reference, options);
        var deserialized = JsonSerializer.Deserialize<MiddlewareReference>(json);

        // Assert
        Assert.NotNull(deserialized);
        Assert.Equal("LoggingMiddleware", deserialized.Name);
        Assert.Null(deserialized.Config);
    }

    [Fact]
    public void MiddlewareReference_ImplicitConversion_FromString()
    {
        // Arrange & Act
        MiddlewareReference reference = "RetryMiddleware";

        // Assert
        Assert.Equal("RetryMiddleware", reference.Name);
    }

    [Fact]
    public void MiddlewareReference_RichSyntax_RoundTrip()
    {
        // Arrange
        var json = """
            {
              "name": "RateLimitMiddleware",
              "config": { "requestsPerMinute": 60 }
            }
            """;

        // Act
        var reference = JsonSerializer.Deserialize<MiddlewareReference>(json);
        var serialized = JsonSerializer.Serialize(reference);
        var roundTripped = JsonSerializer.Deserialize<MiddlewareReference>(serialized);

        // Assert
        Assert.NotNull(reference);
        Assert.Equal("RateLimitMiddleware", reference.Name);
        Assert.True(reference.Config.HasValue);

        Assert.NotNull(roundTripped);
        Assert.Equal("RateLimitMiddleware", roundTripped.Name);
    }

    [Fact]
    public void AgentConfig_WithHarneses_RoundTrip()
    {
        // Arrange
        var config = new AgentConfig
        {
            Name = "TestAgent",
            SystemInstructions = "You are a helpful assistant.",
            Harneses = new List<HarnessReference>
            {
                "MathHarness",
                new HarnessReference
                {
                    Name = "SearchHarness",
                    Functions = new List<string> { "WebSearch" }
                }
            }
        };

        // Act
        var json = JsonSerializer.Serialize(config, HPDJsonContext.Default.AgentConfig);
        var deserialized = JsonSerializer.Deserialize<AgentConfig>(json, HPDJsonContext.Default.AgentConfig);

        // Assert
        Assert.NotNull(deserialized);
        Assert.Equal("TestAgent", deserialized.Name);
        Assert.Equal(2, deserialized.Harneses.Count);
        Assert.Equal("MathHarness", deserialized.Harneses[0].Name);
        Assert.Equal("SearchHarness", deserialized.Harneses[1].Name);
    }

    [Fact]
    public void AgentConfig_WithMiddlewares_RoundTrip()
    {
        // Arrange
        var config = new AgentConfig
        {
            Name = "TestAgent",
            Middlewares = new List<MiddlewareReference>
            {
                "LoggingMiddleware",
                new MiddlewareReference { Name = "RetryMiddleware" }
            }
        };

        // Act
        var json = JsonSerializer.Serialize(config, HPDJsonContext.Default.AgentConfig);
        var deserialized = JsonSerializer.Deserialize<AgentConfig>(json, HPDJsonContext.Default.AgentConfig);

        // Assert
        Assert.NotNull(deserialized);
        Assert.Equal(2, deserialized.Middlewares.Count);
        Assert.Equal("LoggingMiddleware", deserialized.Middlewares[0].Name);
        Assert.Equal("RetryMiddleware", deserialized.Middlewares[1].Name);
    }

    [Fact]
    public void AgentConfig_WithAudio_RoundTripsThroughSourceGeneratedContext()
    {
        var config = new AgentConfig
        {
            Name = "VoiceAgent",
            Audio = new AudioConfig
            {
                Enabled = true,
                InputMode = AudioInputMode.BatchSpeechToText,
                OutputMode = AudioOutputMode.TextToSpeech,
                AssistantOutputMode = AssistantOutputSynthesisMode.ProgressiveWithFinalFallback,
                ProgressiveRouteMode = ProgressiveTextToSpeechRouteMode.ForcePushText,
                PushTextAggregationMode = PushTextInputAggregationMode.Sentence,
                ArtifactCapturePolicy = AssistantAudioArtifactCapturePolicy.MetadataOnly,
                EnablePlayback = true,
                Policy = new AudioPolicySet
                {
                    InputMedia = new InputMediaPolicy
                    {
                        HandlingMode = InputMediaHandlingMode.TranscribeOnly,
                        AllowBatchTranscription = true,
                        RetainInputMediaArtifact = true,
                        AllowDerivedTextPersistence = false,
                        AllowDigestCapture = false
                    },
                    Trace = new TraceCapturePolicy
                    {
                        CaptureTraceRecords = true,
                        CaptureRawMedia = false,
                        CaptureProviderPayloads = true
                    },
                    Privacy = new PrivacyPolicy
                    {
                        RedactRawAudioByDefault = true,
                        AllowMetadataOnlyReplay = true,
                        AllowTranscriptReplay = false
                    },
                    BranchProjection = new BranchProjectionPolicy
                    {
                        ProjectCommittedUserTurns = true,
                        ProjectCommittedAssistantOutputs = true,
                        ProjectInputContentMetadata = true,
                        ProjectRawInputMedia = false
                    }
                },
                Pacing = new TextToSpeechPacingOptions
                {
                    Mode = TextToSpeechPacingMode.Phrase,
                    First = new TextToSpeechFirstSegmentOptions
                    {
                        MinCharacters = 12,
                        MaxCharacters = 80,
                        EmitFirstSafeSentenceImmediately = false
                    },
                    Continuation = new TextToSpeechContinuationOptions
                    {
                        MaxCharacters = 160,
                        PreferSentenceBoundaries = false,
                        AllowPhraseBoundaries = true,
                        MaxInFlightSynthesisRequests = 2
                    },
                    Filtering = new TextToSpeechFilteringOptions
                    {
                        Enabled = true,
                        RemoveCodeBlocks = false,
                        EmojiPolicy = TextToSpeechEmojiPolicy.Verbalize
                    }
                }
            }
        };

        var json = JsonSerializer.Serialize(config, HPDJsonContext.Default.AgentConfig);
        var deserialized = JsonSerializer.Deserialize<AgentConfig>(json, HPDJsonContext.Default.AgentConfig);

        Assert.NotNull(deserialized?.Audio);
        Assert.Equal(AudioInputMode.BatchSpeechToText, deserialized.Audio.InputMode);
        Assert.Equal(AudioOutputMode.TextToSpeech, deserialized.Audio.OutputMode);
        Assert.Equal(AssistantOutputSynthesisMode.ProgressiveWithFinalFallback, deserialized.Audio.AssistantOutputMode);
        Assert.Equal(ProgressiveTextToSpeechRouteMode.ForcePushText, deserialized.Audio.ProgressiveRouteMode);
        Assert.Equal(PushTextInputAggregationMode.Sentence, deserialized.Audio.PushTextAggregationMode);
        Assert.Equal(AssistantAudioArtifactCapturePolicy.MetadataOnly, deserialized.Audio.ArtifactCapturePolicy);
        Assert.True(deserialized.Audio.EnablePlayback);
        Assert.NotNull(deserialized.Audio.Policy);
        Assert.Equal(InputMediaHandlingMode.TranscribeOnly, deserialized.Audio.Policy.InputMedia.HandlingMode);
        Assert.False(deserialized.Audio.Policy.InputMedia.AllowDerivedTextPersistence);
        Assert.True(deserialized.Audio.Policy.Trace.CaptureProviderPayloads);
        Assert.False(deserialized.Audio.Policy.Privacy.AllowTranscriptReplay);
        Assert.NotNull(deserialized.Audio.Pacing);
        Assert.Equal(TextToSpeechPacingMode.Phrase, deserialized.Audio.Pacing.Mode);
        Assert.Equal(12, deserialized.Audio.Pacing.First.MinCharacters);
        Assert.Equal(160, deserialized.Audio.Pacing.Continuation.MaxCharacters);
        Assert.Equal(TextToSpeechEmojiPolicy.Verbalize, deserialized.Audio.Pacing.Filtering.EmojiPolicy);
    }

    [Fact]
    public void AgentRunConfig_WithAudio_RoundTripsThroughSourceGeneratedContext()
    {
        var config = new AgentRunConfig
        {
            Audio = new AudioRunConfig
            {
                Enabled = false,
                InputMode = AudioInputMode.ReferenceOnly,
                OutputMode = AudioOutputMode.TextOnly,
                AssistantOutputMode = AssistantOutputSynthesisMode.FinalText,
                ProgressiveRouteMode = ProgressiveTextToSpeechRouteMode.ForceSegment,
                PushTextAggregationMode = PushTextInputAggregationMode.ManualFlush,
                ArtifactCapturePolicy = AssistantAudioArtifactCapturePolicy.DigestOnly,
                VoiceId = "voice-run",
                Language = "en-US",
                OutputFormat = "pcm16",
                ContentType = "audio/pcm",
                Speed = 1.25f,
                EnablePlayback = true,
                Pacing = new TextToSpeechPacingOptions
                {
                    Mode = TextToSpeechPacingMode.TokenBatch,
                    First = new TextToSpeechFirstSegmentOptions
                    {
                        MinCharacters = 8
                    }
                }
            }
        };

        var json = JsonSerializer.Serialize(config, HPDJsonContext.Default.AgentRunConfig);
        var deserialized = JsonSerializer.Deserialize<AgentRunConfig>(json, HPDJsonContext.Default.AgentRunConfig);

        Assert.NotNull(deserialized?.Audio);
        Assert.False(deserialized.Audio.Enabled);
        Assert.Equal(AudioInputMode.ReferenceOnly, deserialized.Audio.InputMode);
        Assert.Equal(AudioOutputMode.TextOnly, deserialized.Audio.OutputMode);
        Assert.Equal(AssistantOutputSynthesisMode.FinalText, deserialized.Audio.AssistantOutputMode);
        Assert.Equal(ProgressiveTextToSpeechRouteMode.ForceSegment, deserialized.Audio.ProgressiveRouteMode);
        Assert.Equal(PushTextInputAggregationMode.ManualFlush, deserialized.Audio.PushTextAggregationMode);
        Assert.Equal(AssistantAudioArtifactCapturePolicy.DigestOnly, deserialized.Audio.ArtifactCapturePolicy);
        Assert.Equal("voice-run", deserialized.Audio.VoiceId);
        Assert.Equal("en-US", deserialized.Audio.Language);
        Assert.Equal("pcm16", deserialized.Audio.OutputFormat);
        Assert.Equal("audio/pcm", deserialized.Audio.ContentType);
        Assert.Equal(1.25f, deserialized.Audio.Speed);
        Assert.True(deserialized.Audio.EnablePlayback);
        Assert.NotNull(deserialized.Audio.Pacing);
        Assert.Equal(TextToSpeechPacingMode.TokenBatch, deserialized.Audio.Pacing.Mode);
        Assert.Equal(8, deserialized.Audio.Pacing.First.MinCharacters);
    }

    [Fact]
    public void AgentConfig_CompleteExample_RoundTrip()
    {
        
        var json = """
            {
              "name": "ResearchAgent",
              "systemInstructions": "You are a research assistant.",
              "harnesses": [
                "MathHarness",
                { "name": "SearchHarness" },
                { "name": "FileHarness", "functions": ["ReadFile"] }
              ],
              "middlewares": [
                "LoggingMiddleware",
                "RetryMiddleware"
              ],
              "collapsing": {
                "enabled": true,
                "neverCollapse": ["MathHarness"]
              }
            }
            """;

        // Act
        var config = JsonSerializer.Deserialize<AgentConfig>(json, HPDJsonContext.Default.AgentConfig);

        // Assert
        Assert.NotNull(config);
        Assert.Equal("ResearchAgent", config.Name);
        Assert.Equal("You are a research assistant.", config.SystemInstructions);

        // Harneses
        Assert.Equal(3, config.Harneses.Count);
        Assert.Equal("MathHarness", config.Harneses[0].Name);
        Assert.Equal("SearchHarness", config.Harneses[1].Name);
        Assert.Equal("FileHarness", config.Harneses[2].Name);
        Assert.Single(config.Harneses[2].Functions!);
        Assert.Equal("ReadFile", config.Harneses[2].Functions![0]);

        // Middlewares
        Assert.Equal(2, config.Middlewares.Count);
        Assert.Equal("LoggingMiddleware", config.Middlewares[0].Name);
        Assert.Equal("RetryMiddleware", config.Middlewares[1].Name);

        // Collapsing
        Assert.True(config.Collapsing.Enabled);
        Assert.Contains("MathHarness", config.Collapsing.NeverCollapse);
    }

    [Fact]
    public void HarnessReference_StringSyntax_Serialization()
    {
        // Arrange - simple reference should serialize as string
        var reference = new HarnessReference { Name = "MathHarness" };

        // Act
        var json = JsonSerializer.Serialize(reference);

        // Assert - should be serialized as simple string
        Assert.Equal("\"MathHarness\"", json);
    }

    [Fact]
    public void HarnessReference_RichSyntax_Serialization()
    {
        // Arrange - reference with config should serialize as object
        var reference = new HarnessReference
        {
            Name = "SearchHarness",
            Functions = new List<string> { "WebSearch" }
        };

        // Act
        var json = JsonSerializer.Serialize(reference);
        var parsed = JsonDocument.Parse(json);

        // Assert - should be serialized as object
        Assert.Equal(JsonValueKind.Object, parsed.RootElement.ValueKind);
        Assert.Equal("SearchHarness", parsed.RootElement.GetProperty("name").GetString());
    }

    [Fact]
    public void MiddlewareReference_StringSyntax_Serialization()
    {
        // Arrange - simple reference should serialize as string
        var reference = new MiddlewareReference { Name = "LoggingMiddleware" };

        // Act
        var json = JsonSerializer.Serialize(reference);

        // Assert - should be serialized as simple string
        Assert.Equal("\"LoggingMiddleware\"", json);
    }
}
