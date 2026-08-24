namespace HPD.Agent.Providers.Audio.ElevenLabs;

internal static class ElevenLabsRealtimeSpeechToTextProtocol
{
    internal static Uri BuildUri(
        ElevenLabsSttRuntimeSettings settings,
        ElevenLabsRealtimeSpeechToTextRequest request)
    {
        var baseUri = ResolveWebSocketBaseUri(settings);
        var builder = new UriBuilder(baseUri)
        {
            Path = $"{baseUri.AbsolutePath.TrimEnd('/')}/speech-to-text/realtime"
        };
        var query = new List<string>
        {
            $"model_id={Uri.EscapeDataString(request.ModelId!)}"
        };

        AddQuery(query, "include_timestamps", request.IncludeTimestamps);
        AddQuery(query, "include_language_detection", request.IncludeLanguageDetection);
        AddQuery(query, "audio_format", request.AudioFormat);
        AddQuery(query, "language_code", request.LanguageCode);
        AddQuery(query, "commit_strategy", request.CommitStrategy);
        AddQuery(query, "no_verbatim", request.NoVerbatim);
        AddQuery(query, "vad_silence_threshold_secs", request.VadSilenceThresholdSeconds);
        AddQuery(query, "vad_threshold", request.VadThreshold);
        AddQuery(query, "min_speech_duration_ms", request.MinSpeechDurationMilliseconds);
        AddQuery(query, "min_silence_duration_ms", request.MinSilenceDurationMilliseconds);
        AddQuery(query, "enable_logging", request.EnableLogging);

        if (request.Keyterms is { Length: > 0 })
        {
            foreach (var keyterm in request.Keyterms.Where(static value => !string.IsNullOrWhiteSpace(value)))
                query.Add($"keyterms={Uri.EscapeDataString(keyterm)}");
        }

        builder.Query = string.Join('&', query);
        return builder.Uri;
    }

    private static Uri ResolveWebSocketBaseUri(ElevenLabsSttRuntimeSettings settings)
    {
        if (!string.IsNullOrWhiteSpace(settings.WebSocketBaseUrl))
            return new Uri(settings.WebSocketBaseUrl, UriKind.Absolute);
        if (!string.IsNullOrWhiteSpace(settings.BaseUrl))
        {
            var baseUri = new Uri(settings.BaseUrl, UriKind.Absolute);
            var scheme = string.Equals(baseUri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)
                ? "wss"
                : "ws";
            return new UriBuilder(baseUri) { Scheme = scheme }.Uri;
        }
        return new Uri(ElevenLabsAudioProvider.DefaultWebSocketBaseUrl, UriKind.Absolute);
    }

    private static void AddQuery(List<string> query, string name, object? value)
    {
        if (value is null)
            return;
        var serialized = value switch
        {
            bool boolean => boolean.ToString().ToLowerInvariant(),
            double number => number.ToString(System.Globalization.CultureInfo.InvariantCulture),
            _ => Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture)
        };
        if (!string.IsNullOrWhiteSpace(serialized))
            query.Add($"{name}={Uri.EscapeDataString(serialized)}");
    }
}
