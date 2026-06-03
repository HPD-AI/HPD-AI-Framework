using HPD.Agent;
using Microsoft.Extensions.AI;

namespace HPD.Agent.Providers.Audio.Meai;

public sealed class InputMediaSpeechToTextProviderOptions
{
    public ClientProviderConfig ProviderConfig { get; set; } = new();

    public string ProviderKey { get; set; } = string.Empty;

    public string? ModelId { get; set; }

    public string? SpeechLanguage { get; set; }

    public string? TextLanguage { get; set; }

    public int? SpeechSampleRate { get; set; }

    public string? Prompt { get; set; }

    public float? Temperature { get; set; }

    public string? ResponseFormat { get; set; }

    public string[]? TimestampGranularities { get; set; }

    public bool? IncludeLogprobs { get; set; }

    public Dictionary<string, object?>? AdditionalProperties { get; set; }

    public Func<ISpeechToTextClient, object>? RawRepresentationFactory { get; set; }

    public bool TreatEmptyTranscriptAsError { get; set; } = true;

    public bool DisposeCreatedClient { get; set; } = true;
}
