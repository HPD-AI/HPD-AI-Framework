using HPD.Serialization;
using HPD.Agent.Providers;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace HPD.Agent.Serialization;

/// <summary>AOT-safe JSON/YAML serialization helpers for HPD agent configuration documents.</summary>
public static class HpdAgentConfigSerializer
{
    public static AgentConfig? ReadFile(string path)
        => HpdConfigSerializer.ReadFile(path, HPDJsonContext.Default.AgentConfig);

    public static ValueTask<AgentConfig?> ReadFileAsync(
        string path,
        CancellationToken cancellationToken = default)
        => HpdConfigSerializer.ReadFileAsync(
            path,
            HPDJsonContext.Default.AgentConfig,
            cancellationToken);

    public static void WriteFile(string path, AgentConfig config)
        => HpdConfigSerializer.WriteFile(path, config, HPDJsonContext.Default.AgentConfig);

    public static ValueTask WriteFileAsync(
        string path,
        AgentConfig config,
        CancellationToken cancellationToken = default)
        => HpdConfigSerializer.WriteFileAsync(
            path,
            config,
            HPDJsonContext.Default.AgentConfig,
            cancellationToken);

    public static string Serialize(AgentConfig config, HpdConfigFormat format = HpdConfigFormat.Json)
        => HpdConfigSerializer.Serialize(config, HPDJsonContext.Default.AgentConfig, format);

    public static AgentConfig? Deserialize(string text, HpdConfigFormat format = HpdConfigFormat.Json)
        => HpdConfigSerializer.Deserialize(text, HPDJsonContext.Default.AgentConfig, format);

    /// <summary>
    /// Deserializes an agent configuration and binds provider-specific family payloads through
    /// the immutable generated provider composition.
    /// </summary>
    public static AgentConfig? Deserialize(
        string text,
        ProviderComposition providerComposition,
        HpdConfigFormat format = HpdConfigFormat.Json)
    {
        ArgumentNullException.ThrowIfNull(providerComposition);
        var root = format switch
        {
            HpdConfigFormat.Json => JsonNode.Parse(text),
            HpdConfigFormat.Yaml => HpdConfigSerializer.ParseYamlToJsonNode(text),
            _ => throw new ArgumentOutOfRangeException(nameof(format), format, null)
        } as JsonObject;
        if (root is null)
            return null;

        var payloads = ExtractPayloads(root);
        var config = JsonSerializer.Deserialize(root.ToJsonString(), HPDJsonContext.Default.AgentConfig);
        if (config is null)
            return null;

        foreach (var payload in payloads)
        {
            var target = payload.ProfileName is null
                ? config.Clients.GetFamilyConfig(payload.Family)
                : config.ProviderProfiles.TryGetValue(payload.ProfileName, out var profile)
                    ? profile.GetFamilyConfig(payload.Family)
                    : null;
            if (target is null)
                continue;

            var providerKey = payload.ProviderKey ?? target.ProviderKey;
            if (payload.Configuration is not null)
            {
                target.ProviderConfig = (IProviderConfig)BindPayload(
                    providerComposition,
                    providerKey,
                    payload.Family,
                    ProviderPayloadKind.Configuration,
                    payload.Configuration,
                    payload.Path + ".providerConfig");
            }

            if (payload.OperationOptions is not null)
                SetOperationOptions(target, BindPayload(
                    providerComposition,
                    providerKey,
                    payload.Family,
                    ProviderPayloadKind.OperationOptions,
                    payload.OperationOptions,
                    payload.Path + ".providerOptions"));
        }

        return config;
    }

    /// <summary>Serializes typed provider-family payloads through the generated composition.</summary>
    public static string Serialize(
        AgentConfig config,
        ProviderComposition providerComposition,
        HpdConfigFormat format = HpdConfigFormat.Json)
    {
        ArgumentNullException.ThrowIfNull(config);
        ArgumentNullException.ThrowIfNull(providerComposition);
        var root = JsonSerializer.SerializeToNode(config, HPDJsonContext.Default.AgentConfig) as JsonObject
            ?? throw new JsonException("Agent configuration did not serialize to a JSON object.");

        if (GetObject(root, "clients") is { } clients)
            InjectFamilies(clients, config.Clients, providerComposition, "clients");
        if (GetObject(root, "providerProfiles") is { } profiles)
        {
            foreach (var pair in config.ProviderProfiles)
            {
                if (GetObject(profiles, pair.Key) is { } profileNode)
                    InjectProfile(profileNode, pair.Value, providerComposition, $"providerProfiles.{pair.Key}");
            }
        }

        return format switch
        {
            HpdConfigFormat.Json => root.ToJsonString(HPDJsonContext.Default.Options),
            HpdConfigFormat.Yaml => HpdConfigSerializer.WriteYaml(root),
            _ => throw new ArgumentOutOfRangeException(nameof(format), format, null)
        };
    }

    private static void InjectProfile(
        JsonObject owner,
        AgentProviderProfile profile,
        ProviderComposition composition,
        string path)
    {
        foreach (var (name, family) in FamilyNames)
            InjectFamily(owner, name, family, profile.GetFamilyConfig(family), composition, path);
    }

    private static void InjectFamilies(
        JsonObject owner,
        AgentClientsConfig clients,
        ProviderComposition composition,
        string path)
    {
        foreach (var (name, family) in FamilyNames)
            InjectFamily(owner, name, family, clients.GetFamilyConfig(family), composition, path);
    }

    private static void InjectFamily(
        JsonObject owner,
        string name,
        ProviderClientFamily family,
        ProviderClientConfig? config,
        ProviderComposition composition,
        string path)
    {
        if (config is null || GetObject(owner, name) is not { } familyNode)
            return;
        if (config.ProviderConfig is not null)
            familyNode["providerConfig"] = SerializePayload(
                composition,
                config.ProviderKey,
                family,
                ProviderPayloadKind.Configuration,
                config.ProviderConfig,
                $"{path}.{name}.providerConfig");
        var operationOptions = GetOperationOptions(config);
        if (operationOptions is not null)
            familyNode["providerOptions"] = SerializePayload(
                composition,
                config.ProviderKey,
                family,
                ProviderPayloadKind.OperationOptions,
                operationOptions,
                $"{path}.{name}.providerOptions");
    }

    private static JsonNode? SerializePayload(
        ProviderComposition composition,
        string? providerKey,
        ProviderClientFamily family,
        ProviderPayloadKind kind,
        object value,
        string path)
    {
        composition.ValidatePayload(providerKey, family, kind, value, path);
        var canonical = composition.Descriptors.Canonicalize(providerKey!);
        composition.Serialization.TryGet(canonical, family, kind, out var contract);
        return JsonSerializer.SerializeToNode(value, contract!.JsonTypeInfo);
    }

    private static object? GetOperationOptions(ProviderClientConfig config) => config switch
    {
        ChatClientConfig chat => chat.ProviderOptions,
        RealtimeClientConfig realtime => realtime.ProviderOptions,
        ImageGenerationClientConfig image => image.ProviderOptions,
        EmbeddingsClientConfig embeddings => embeddings.ProviderOptions,
        TextToSpeechClientConfig tts => tts.ProviderOptions,
        SpeechToTextClientConfig stt => stt.ProviderOptions,
        HostedFilesClientConfig files => files.ProviderOptions,
        _ => null
    };

    private static object BindPayload(
        ProviderComposition composition,
        string? providerKey,
        ProviderClientFamily family,
        ProviderPayloadKind kind,
        JsonNode node,
        string path)
    {
        if (string.IsNullOrWhiteSpace(providerKey))
            throw new AgentRunConfigurationException(
                "ProviderKeyRequired",
                path,
                $"{path} requires providerKey on the same client-family object.");

        var canonical = composition.Descriptors.Canonicalize(providerKey);
        if (!composition.Serialization.TryGet(canonical, family, kind, out var contract))
            throw new AgentRunConfigurationException(
                "ProviderPayloadNotRegistered",
                path,
                $"Provider '{canonical}' does not declare a generated {kind} payload for '{family}'.",
                canonical);

        var value = JsonSerializer.Deserialize(node.ToJsonString(), contract!.JsonTypeInfo)
            ?? throw new AgentRunConfigurationException(
                "ProviderPayloadInvalid",
                path,
                $"Provider payload at '{path}' deserialized to null.",
                canonical);
        composition.ValidatePayload(canonical, family, kind, value, path);
        var validation = contract.Validate(value);
        if (!validation.IsValid)
            throw new AgentRunConfigurationException(
                "ProviderPayloadInvalid",
                path,
                string.Join("; ", validation.Errors),
                canonical);
        return value;
    }

    private static void SetOperationOptions(ProviderClientConfig target, object value)
    {
        switch (target)
        {
            case ChatClientConfig chat:
                chat.ProviderOptions = (IChatRequestOptions)value;
                break;
            case RealtimeClientConfig realtime:
                realtime.ProviderOptions = (IRealtimeSessionProviderOptions)value;
                break;
            case ImageGenerationClientConfig image:
                image.ProviderOptions = (IImageGenerationProviderOptions)value;
                break;
            case EmbeddingsClientConfig embeddings:
                embeddings.ProviderOptions = (IEmbeddingGenerationProviderOptions)value;
                break;
            case TextToSpeechClientConfig tts:
                tts.ProviderOptions = (ITextToSpeechProviderOptions)value;
                break;
            case SpeechToTextClientConfig stt:
                stt.ProviderOptions = (ISpeechToTextProviderOptions)value;
                break;
            case HostedFilesClientConfig files:
                files.ProviderOptions = (IHostedFileProviderOptions)value;
                break;
        }
    }

    private static List<ExtractedPayload> ExtractPayloads(JsonObject root)
    {
        var result = new List<ExtractedPayload>();
        if (GetObject(root, "clients") is { } clients)
            ExtractFamilies(clients, null, "clients", result);
        if (GetObject(root, "providerProfiles") is { } profiles)
        {
            foreach (var pair in profiles)
                if (pair.Value is JsonObject profile)
                    ExtractFamilies(profile, pair.Key, $"providerProfiles.{pair.Key}", result);
        }
        return result;
    }

    private static void ExtractFamilies(
        JsonObject owner,
        string? profileName,
        string path,
        List<ExtractedPayload> result)
    {
        foreach (var (name, family) in FamilyNames)
        {
            if (GetObject(owner, name) is not { } familyObject)
                continue;
            var config = Remove(familyObject, "providerConfig");
            var options = Remove(familyObject, "providerOptions");
            if (config is null && options is null)
                continue;
            result.Add(new ExtractedPayload(
                profileName,
                family,
                GetString(familyObject, "providerKey"),
                config,
                options,
                $"{path}.{name}"));
        }
    }

    private static JsonObject? GetObject(JsonObject owner, string name)
        => owner.FirstOrDefault(pair => pair.Key.Equals(name, StringComparison.OrdinalIgnoreCase)).Value as JsonObject;

    private static string? GetString(JsonObject owner, string name)
    {
        var node = owner.FirstOrDefault(pair => pair.Key.Equals(name, StringComparison.OrdinalIgnoreCase)).Value;
        return node?.GetValue<string>();
    }

    private static JsonNode? Remove(JsonObject owner, string name)
    {
        var key = owner.Select(static pair => pair.Key)
            .FirstOrDefault(key => key.Equals(name, StringComparison.OrdinalIgnoreCase));
        if (key is null)
            return null;
        var node = owner[key]?.DeepClone();
        owner.Remove(key);
        return node;
    }

    private static readonly (string Name, ProviderClientFamily Family)[] FamilyNames =
    [
        ("chat", ProviderClientFamily.Chat),
        ("realtime", ProviderClientFamily.Realtime),
        ("imageGeneration", ProviderClientFamily.ImageGeneration),
        ("embeddings", ProviderClientFamily.Embeddings),
        ("textToSpeech", ProviderClientFamily.TextToSpeech),
        ("speechToText", ProviderClientFamily.SpeechToText),
        ("hostedFiles", ProviderClientFamily.HostedFiles),
        ("voiceActivityDetection", ProviderClientFamily.VoiceActivityDetection),
        ("endOfTurnDetection", ProviderClientFamily.EndOfTurnDetection)
    ];

    private sealed record ExtractedPayload(
        string? ProfileName,
        ProviderClientFamily Family,
        string? ProviderKey,
        JsonNode? Configuration,
        JsonNode? OperationOptions,
        string Path);
}
