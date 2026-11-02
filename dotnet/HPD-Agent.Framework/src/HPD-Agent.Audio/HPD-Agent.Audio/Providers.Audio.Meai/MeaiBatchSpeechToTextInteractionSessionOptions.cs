namespace HPD.Agent.Providers.Audio.Meai;

#pragma warning disable MEAI001 // HPD pins Microsoft.Extensions.AI 10.11 and exposes its experimental speech contracts.

using HPD.Agent.ErrorHandling;
using Microsoft.Extensions.AI;

public sealed record MeaiBatchSpeechToTextInteractionSessionOptions
{
    public string ProviderKey { get; init; } = "meai-stt";

    public string? ModelId { get; init; }

    public string? SpeechLanguage { get; init; }

    public string? TextLanguage { get; init; }

    public int? SpeechSampleRate { get; init; }

    public string? Prompt { get; init; }

    public float? Temperature { get; init; }

    public string? ResponseFormat { get; init; }

    public string[]? TimestampGranularities { get; init; }

    public bool? IncludeLogprobs { get; init; }

    public Dictionary<string, object?>? AdditionalProperties { get; init; }

    public Func<ISpeechToTextClient, object>? RawRepresentationFactory { get; init; }

    public IProviderErrorHandler? ProviderErrorHandler { get; init; }

    public bool TreatEmptyTranscriptAsError { get; init; } = true;

    public bool DisposeClient { get; init; }
}
