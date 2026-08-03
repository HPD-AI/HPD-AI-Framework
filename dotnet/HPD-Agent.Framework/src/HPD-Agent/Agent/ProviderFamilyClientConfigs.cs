using System.Text.Json.Serialization;
using HPD.Agent.Providers;
using Microsoft.Extensions.AI;

namespace HPD.Agent;

/// <summary>Marks provider-specific client-acquisition configuration.</summary>
public interface IProviderConfig;

/// <summary>Marks provider-specific Realtime session options.</summary>
public interface IRealtimeSessionProviderOptions;

/// <summary>Marks provider-specific image-generation operation options.</summary>
public interface IImageGenerationProviderOptions;

/// <summary>Marks provider-specific embedding-generation operation options.</summary>
public interface IEmbeddingGenerationProviderOptions;

/// <summary>Marks provider-specific text-to-speech operation options.</summary>
public interface ITextToSpeechProviderOptions;

/// <summary>Marks provider-specific speech-to-text operation options.</summary>
public interface ISpeechToTextProviderOptions;

/// <summary>Marks provider-specific hosted-file operation options.</summary>
public interface IHostedFileProviderOptions;

/// <summary>Serializable audio-format defaults for a Realtime session.</summary>
public sealed class RealtimeAudioFormatRunConfig
{
    /// <summary>Gets or sets the media type.</summary>
    public string? MediaType { get; set; }

    /// <summary>Gets or sets the sample rate in hertz.</summary>
    public int? SampleRate { get; set; }
}

/// <summary>Serializable input-transcription defaults for a Realtime session.</summary>
public sealed class RealtimeTranscriptionRunConfig
{
    /// <summary>Gets or sets the transcription model name.</summary>
    public string? ModelName { get; set; }

    /// <summary>Gets or sets the spoken language.</summary>
    public string? SpeechLanguage { get; set; }

    /// <summary>Gets or sets the transcription prompt.</summary>
    public string? Prompt { get; set; }
}

/// <summary>Provider selection and portable defaults for Realtime sessions.</summary>
public sealed class RealtimeClientConfig : ProviderClientConfig
{
    /// <summary>Gets or sets the output audio format.</summary>
    public RealtimeAudioFormatRunConfig? OutputAudioFormat { get; set; }

    /// <summary>Gets or sets the voice.</summary>
    public string? Voice { get; set; }

    /// <summary>Gets or sets the maximum output-token count.</summary>
    public int? MaxOutputTokens { get; set; }

    /// <summary>Gets or sets the requested output modalities.</summary>
    public IReadOnlyList<string>? OutputModalities { get; set; }

    /// <summary>Gets or sets input-transcription defaults.</summary>
    public RealtimeTranscriptionRunConfig? Transcription { get; set; }

    /// <summary>Gets or sets provider-specific session options.</summary>
    public IRealtimeSessionProviderOptions? ProviderOptions { get; set; }

    /// <summary>Gets or sets a borrowed Realtime client override.</summary>
    [JsonIgnore]
    public ClientOverride<IRealtimeClient>? Override { get; set; }
}

/// <summary>Serializable image dimensions.</summary>
public sealed class ImageSizeRunConfig
{
    /// <summary>Gets or sets image width in pixels.</summary>
    public int Width { get; set; }

    /// <summary>Gets or sets image height in pixels.</summary>
    public int Height { get; set; }
}

/// <summary>Provider selection and portable image-generation defaults.</summary>
public sealed class ImageGenerationClientConfig : ProviderClientConfig
{
    /// <summary>Gets or sets the requested image count.</summary>
    public int? Count { get; set; }

    /// <summary>Gets or sets image dimensions.</summary>
    public ImageSizeRunConfig? ImageSize { get; set; }

    /// <summary>Gets or sets the output media type.</summary>
    public string? MediaType { get; set; }

    /// <summary>Gets or sets the streaming image count.</summary>
    public int? StreamingCount { get; set; }

    /// <summary>Gets or sets provider-specific image options.</summary>
    public IImageGenerationProviderOptions? ProviderOptions { get; set; }

    /// <summary>Gets or sets a borrowed image-generator override.</summary>
    [JsonIgnore]
    public ClientOverride<IImageGenerator>? Override { get; set; }
}

/// <summary>Provider selection and portable embedding defaults.</summary>
public sealed class EmbeddingsClientConfig : ProviderClientConfig
{
    /// <summary>Gets or sets the requested embedding dimensions.</summary>
    public int? Dimensions { get; set; }

    /// <summary>Gets or sets provider-specific embedding options.</summary>
    public IEmbeddingGenerationProviderOptions? ProviderOptions { get; set; }

    /// <summary>Gets or sets a borrowed embedding-generator override.</summary>
    [JsonIgnore]
    public ClientOverride<IEmbeddingGenerator>? Override { get; set; }
}

/// <summary>Provider selection and portable text-to-speech defaults.</summary>
public sealed class TextToSpeechClientConfig : ProviderClientConfig
{
    /// <summary>Gets or sets the voice identifier.</summary>
    public string? VoiceId { get; set; }

    /// <summary>Gets or sets the language.</summary>
    public string? Language { get; set; }

    /// <summary>Gets or sets the audio format.</summary>
    public string? AudioFormat { get; set; }

    /// <summary>Gets or sets speech speed.</summary>
    public float? Speed { get; set; }

    /// <summary>Gets or sets speech pitch.</summary>
    public float? Pitch { get; set; }

    /// <summary>Gets or sets speech volume.</summary>
    public float? Volume { get; set; }

    /// <summary>Gets or sets provider-specific synthesis options.</summary>
    public ITextToSpeechProviderOptions? ProviderOptions { get; set; }

    /// <summary>Gets or sets a borrowed text-to-speech client override.</summary>
    [JsonIgnore]
    public ClientOverride<ITextToSpeechClient>? Override { get; set; }
}

/// <summary>Provider selection and portable speech-to-text defaults.</summary>
public sealed class SpeechToTextClientConfig : ProviderClientConfig
{
    /// <summary>Gets or sets the spoken language.</summary>
    public string? SpeechLanguage { get; set; }

    /// <summary>Gets or sets the speech sample rate in hertz.</summary>
    public int? SpeechSampleRate { get; set; }

    /// <summary>Gets or sets the output text language.</summary>
    public string? TextLanguage { get; set; }

    /// <summary>Gets or sets provider-specific recognition options.</summary>
    public ISpeechToTextProviderOptions? ProviderOptions { get; set; }

    /// <summary>Gets or sets a borrowed speech-to-text client override.</summary>
    [JsonIgnore]
    public ClientOverride<ISpeechToTextClient>? Override { get; set; }
}

/// <summary>Provider selection and portable hosted-file defaults.</summary>
public sealed class HostedFilesClientConfig : ProviderClientConfig
{
    /// <summary>Gets or sets the hosted-file scope.</summary>
    public string? Scope { get; set; }

    /// <summary>Gets or sets the hosted-file purpose.</summary>
    public string? Purpose { get; set; }

    /// <summary>Gets or sets the list-result limit.</summary>
    public int? Limit { get; set; }

    /// <summary>Gets or sets provider-specific hosted-file options.</summary>
    public IHostedFileProviderOptions? ProviderOptions { get; set; }

    /// <summary>Gets or sets a borrowed hosted-file client override.</summary>
    [JsonIgnore]
    public ClientOverride<IHostedFileClient>? Override { get; set; }
}

/// <summary>Common provider selection for voice-activity detection.</summary>
public sealed class VoiceActivityDetectionClientConfig : ProviderClientConfig
{
    /// <summary>Gets or sets a borrowed component factory override.</summary>
    [JsonIgnore]
    public Func<ProviderComponentLifetimeContext, IVoiceActivityDetector>? OverrideFactory { get; set; }
}

/// <summary>Common provider selection for end-of-turn detection.</summary>
public sealed class EndOfTurnDetectionClientConfig : ProviderClientConfig
{
    /// <summary>Gets or sets a borrowed component factory override.</summary>
    [JsonIgnore]
    public Func<ProviderComponentLifetimeContext, IEotDetector>? OverrideFactory { get; set; }
}
