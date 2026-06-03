using System.Security.Cryptography;
using HPD.Agent;
using HPD.Agent.Audio.Media;
using HPD.Agent.Audio.Output;

namespace HPD.Agent.Audio.AgentIntegration.Output;

internal sealed class OutputArtifactWriter
{
    public async ValueTask<StoredAudioArtifact> WriteAssistantAudioArtifactAsync(
        IContentStore contentStore,
        AudioSessionId sessionId,
        OutputFlowId outputFlowId,
        ResponseId responseId,
        string providerKey,
        string? modelId,
        AssistantTextToSpeechOutputOptions options,
        string mediaType,
        ReadOnlyMemory<byte> data,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(contentStore);
        ArgumentNullException.ThrowIfNull(options);

        var bytes = data.ToArray();
        var sha256 = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
        var name = $"assistant-output-{SanitizeNamePart(outputFlowId.Value)}-{SanitizeNamePart(responseId.Value)}{ExtensionFor(mediaType, options.OutputFormat)}";
        var tags = new Dictionary<string, string>
        {
            ["folder"] = "/artifacts",
            ["kind"] = "assistant-audio",
            ["outputFlowId"] = outputFlowId.Value,
            ["responseId"] = responseId.Value,
            ["provider"] = providerKey
        };
        AddIfPresent(tags, "model", modelId);
        AddIfPresent(tags, "voice", options.VoiceId);

        var info = await contentStore.WriteBytesAsync(
            scope: sessionId.Value,
            data: bytes,
            metadata: new ContentMetadata
            {
                ContentType = mediaType,
                Name = name,
                Origin = ContentSource.Agent,
                Tags = tags
            },
            options: new ContentWriteOptions { Mode = ContentWriteMode.Create },
            cancellationToken: cancellationToken).ConfigureAwait(false);

        return new StoredAudioArtifact(
            new AudioArtifactRef("hpd-content", info.Id, info.ContentType, info.SizeBytes, sha256),
            info.ContentType,
            info.SizeBytes,
            sha256);
    }

    public static string? ToMediaType(string? outputFormat)
    {
        return outputFormat?.ToLowerInvariant() switch
        {
            "mp3" => "audio/mpeg",
            "wav" => "audio/wav",
            "opus" => "audio/opus",
            "aac" => "audio/aac",
            "flac" => "audio/flac",
            "pcm" => "audio/pcm",
            var value when value?.StartsWith("audio/", StringComparison.OrdinalIgnoreCase) == true => value,
            _ => null
        };
    }

    private static string ExtensionFor(string mediaType, string? requestedFormat)
    {
        var normalized = FirstNonWhiteSpace(requestedFormat, mediaType)?.ToLowerInvariant();
        return normalized switch
        {
            "mp3" or "audio/mpeg" => ".mp3",
            "wav" or "audio/wav" or "audio/x-wav" => ".wav",
            "opus" or "audio/opus" => ".opus",
            "aac" or "audio/aac" => ".aac",
            "flac" or "audio/flac" => ".flac",
            "pcm" or "audio/pcm" => ".pcm",
            _ => ".bin"
        };
    }

    private static string SanitizeNamePart(string value)
    {
        var invalid = Path.GetInvalidFileNameChars().ToHashSet();
        var sanitized = new string(value.Select(ch => invalid.Contains(ch) ? '-' : ch).ToArray()).Trim('-', '.', ' ');
        return string.IsNullOrWhiteSpace(sanitized) ? "value" : sanitized;
    }

    private static void AddIfPresent(IDictionary<string, string> values, string key, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            values[key] = value!;
        }
    }

    private static string? FirstNonWhiteSpace(params string?[] values)
    {
        foreach (var value in values)
        {
            if (!string.IsNullOrWhiteSpace(value))
            {
                return value;
            }
        }

        return null;
    }
}

internal sealed record StoredAudioArtifact(
    AudioArtifactRef Artifact,
    string MediaType,
    long SizeBytes,
    string Sha256);
