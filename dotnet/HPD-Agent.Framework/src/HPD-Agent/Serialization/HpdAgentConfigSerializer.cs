using HPD.Serialization;
using HPD.Agent.Providers;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace HPD.Agent.Serialization;

/// <summary>AOT-safe JSON/YAML serialization helpers for HPD agent configuration documents.</summary>
public static class HpdAgentConfigSerializer
{
    public static AgentConfig? ReadFile(string path)
        => Deserialize(File.ReadAllText(path), GetFileFormat(path));

    public static ValueTask<AgentConfig?> ReadFileAsync(
        string path,
        CancellationToken cancellationToken = default)
        => ReadPortableFileAsync(path, cancellationToken);

    /// <summary>Reads JSON or YAML and binds its provider-specific payloads through a generated composition.</summary>
    /// <param name="path">The configuration file path. The <c>.yaml</c> and <c>.yml</c> extensions select YAML; all others select JSON.</param>
    /// <param name="providerComposition">The consuming host's generated provider composition.</param>
    /// <returns>The deserialized configuration, or <see langword="null"/> when the document is null.</returns>
    public static AgentConfig? ReadFile(string path, ProviderComposition providerComposition)
        => Deserialize(File.ReadAllText(path), providerComposition, GetFileFormat(path));

    /// <summary>Reads JSON or YAML asynchronously and binds its provider-specific payloads through a generated composition.</summary>
    /// <param name="path">The configuration file path. The <c>.yaml</c> and <c>.yml</c> extensions select YAML; all others select JSON.</param>
    /// <param name="providerComposition">The consuming host's generated provider composition.</param>
    /// <param name="cancellationToken">A token that cancels file reading.</param>
    /// <returns>The deserialized configuration, or <see langword="null"/> when the document is null.</returns>
    public static async ValueTask<AgentConfig?> ReadFileAsync(
        string path,
        ProviderComposition providerComposition,
        CancellationToken cancellationToken = default)
        => Deserialize(
            await File.ReadAllTextAsync(path, cancellationToken).ConfigureAwait(false),
            providerComposition,
            GetFileFormat(path));

    public static void WriteFile(string path, AgentConfig config)
    {
        EnsureProviderCompositionNotRequired(config);
        HpdConfigSerializer.WriteFile(path, config, HPDJsonContext.Default.AgentConfig);
    }

    public static ValueTask WriteFileAsync(
        string path,
        AgentConfig config,
        CancellationToken cancellationToken = default)
    {
        EnsureProviderCompositionNotRequired(config);
        return HpdConfigSerializer.WriteFileAsync(
            path,
            config,
            HPDJsonContext.Default.AgentConfig,
            cancellationToken);
    }

    public static string Serialize(AgentConfig config, HpdConfigFormat format = HpdConfigFormat.Json)
    {
        EnsureProviderCompositionNotRequired(config);
        return HpdConfigSerializer.Serialize(config, HPDJsonContext.Default.AgentConfig, format);
    }

    public static AgentConfig? Deserialize(string text, HpdConfigFormat format = HpdConfigFormat.Json)
    {
        var root = ParseObject(text, format);
        var payload = root is null ? null : ExtractPayloads(root).FirstOrDefault();
        if (payload is not null)
            ThrowProviderCompositionNotInstalled(payload.Path);
        return root is null
            ? null
            : JsonSerializer.Deserialize(root.ToJsonString(), HPDJsonContext.Default.AgentConfig);
    }

    private static async ValueTask<AgentConfig?> ReadPortableFileAsync(
        string path,
        CancellationToken cancellationToken)
        => Deserialize(
            await File.ReadAllTextAsync(path, cancellationToken).ConfigureAwait(false),
            GetFileFormat(path));

    private static HpdConfigFormat GetFileFormat(string path)
        => Path.GetExtension(path).ToLowerInvariant() switch
        {
            ".yaml" or ".yml" => HpdConfigFormat.Yaml,
            _ => HpdConfigFormat.Json
        };

    private static void EnsureProviderCompositionNotRequired(AgentConfig config)
    {
        ValidatePortableClients("clients", config.Clients);
        EnsureProviderCompositionNotRequired("clients", config.Clients.GetFamilyConfig);
        for (var index = 0; index < config.ProviderProfiles.Count; index++)
        {
            ValidatePortableClients($"providerProfiles[{index}].clients", config.ProviderProfiles[index].Clients);
            EnsureProviderCompositionNotRequired(
                $"providerProfiles[{index}].clients",
                config.ProviderProfiles[index].Clients.GetFamilyConfig);
        }
    }

    private static void EnsureProviderCompositionNotRequired(
        string path,
        Func<ProviderClientFamily, ProviderClientConfig?> getFamilyConfig)
    {
        foreach (var (_, family) in FamilyNames)
        {
            var client = getFamilyConfig(family);
            if (client is not null &&
                (client.ProviderConfig is not null || GetOperationOptions(client) is not null))
                ThrowProviderCompositionNotInstalled($"{path}.{family}");
        }
    }

    private static void ThrowProviderCompositionNotInstalled(string path)
        => throw new AgentRunConfigurationException(
            "ProviderCompositionNotInstalled",
            path,
            $"Provider-specific configuration at '{path}' requires the consuming host's generated provider composition.");

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

        NormalizeProviderProfiles(root, providerComposition);
        var payloads = ExtractPayloads(root);
        var config = JsonSerializer.Deserialize(root.ToJsonString(), HPDJsonContext.Default.AgentConfig);
        if (config is null)
            return null;

        foreach (var payload in payloads)
        {
            var target = payload.ProfileIndex is null
                ? config.Clients.GetFamilyConfig(payload.Family)
                : payload.ProfileIndex.Value >= 0 && payload.ProfileIndex.Value < config.ProviderProfiles.Count
                    ? config.ProviderProfiles[payload.ProfileIndex.Value].Clients.GetFamilyConfig(payload.Family)
                    : null;
            if (target is null)
                continue;

            var providerKey = ResolvePayloadProviderKey(
                providerComposition, payload, target.Provider?.Key);
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

    /// <summary>
    /// Deserializes one run configuration and binds provider-specific family payloads
    /// through the immutable generated provider composition.
    /// </summary>
    /// <param name="text">The JSON or YAML document.</param>
    /// <param name="providerComposition">The generated provider composition installed by the host.</param>
    /// <param name="format">The document format.</param>
    /// <returns>The deserialized run configuration, or <see langword="null"/> for a null document.</returns>
    /// <exception cref="AgentRunConfigurationException">
    /// A provider key or generated payload contract is missing, or a payload is invalid.
    /// </exception>
    public static AgentRunConfig? DeserializeRunConfig(
        string text,
        ProviderComposition providerComposition,
        HpdConfigFormat format = HpdConfigFormat.Json)
    {
        ArgumentNullException.ThrowIfNull(providerComposition);
        var root = ParseObject(text, format);
        if (root is null)
            return null;

        var payloads = new List<ExtractedPayload>();
        if (GetObject(root, "clients") is { } clients)
            ExtractFamilies(clients, null, null, "clients", payloads);

        var config = JsonSerializer.Deserialize(root.ToJsonString(), HPDJsonContext.Default.AgentRunConfig);
        if (config is null)
            return null;

        BindFamilies(config.Clients, payloads, providerComposition);
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
        ValidatePortableClients("clients", config.Clients);
        for (var index = 0; index < config.ProviderProfiles.Count; index++)
            ValidatePortableClients($"providerProfiles[{index}].clients", config.ProviderProfiles[index].Clients);
        var root = JsonSerializer.SerializeToNode(config, HPDJsonContext.Default.AgentConfig) as JsonObject
            ?? throw new JsonException("Agent configuration did not serialize to a JSON object.");

        NormalizeProviderProfiles(root, providerComposition);
        if (GetObject(root, "clients") is { } clients)
            InjectFamilies(clients, config.Clients, providerComposition, "clients");
        if (GetArray(root, "providerProfiles") is { } profiles)
        {
            for (var index = 0; index < config.ProviderProfiles.Count; index++)
            {
                var profile = config.ProviderProfiles[index];
                if (profiles[index] is JsonObject profileNode && GetObject(profileNode, "clients") is { } clientsNode)
                    InjectProfile(
                        clientsNode,
                        profile.ProviderKey,
                        profile,
                        providerComposition,
                        $"providerProfiles[{index}].clients");
            }
        }

        return format switch
        {
            HpdConfigFormat.Json => root.ToJsonString(HPDJsonContext.Default.Options),
            HpdConfigFormat.Yaml => HpdConfigSerializer.WriteYaml(root),
            _ => throw new ArgumentOutOfRangeException(nameof(format), format, null)
        };
    }

    /// <summary>Serializes one run configuration and its typed provider payloads.</summary>
    /// <param name="config">The run configuration to serialize.</param>
    /// <param name="providerComposition">The generated provider composition installed by the host.</param>
    /// <param name="format">The output format.</param>
    /// <returns>The serialized JSON or YAML document.</returns>
    /// <exception cref="AgentRunConfigurationException">
    /// A provider key or generated payload contract is missing, or a payload is invalid.
    /// </exception>
    public static string Serialize(
        AgentRunConfig config,
        ProviderComposition providerComposition,
        HpdConfigFormat format = HpdConfigFormat.Json)
    {
        ArgumentNullException.ThrowIfNull(config);
        ArgumentNullException.ThrowIfNull(providerComposition);
        ValidatePortableClients("clients", config.Clients);
        var root = JsonSerializer.SerializeToNode(config, HPDJsonContext.Default.AgentRunConfig) as JsonObject
            ?? throw new JsonException("Agent run configuration did not serialize to a JSON object.");

        if (GetObject(root, "clients") is { } clients)
            InjectFamilies(clients, config.Clients, providerComposition, "clients");
        return WriteObject(root, format);
    }

    private static JsonObject? ParseObject(string text, HpdConfigFormat format) => format switch
    {
        HpdConfigFormat.Json => JsonNode.Parse(text) as JsonObject,
        HpdConfigFormat.Yaml => HpdConfigSerializer.ParseYamlToJsonNode(text) as JsonObject,
        _ => throw new ArgumentOutOfRangeException(nameof(format), format, null)
    };

    private static void ValidatePortableClients(string path, AgentClientsConfig clients)
    {
        ArgumentNullException.ThrowIfNull(clients);
        foreach (var (_, family) in FamilyNames)
        {
            var config = clients.GetFamilyConfig(family);
            if (config is null)
                continue;
            if (config.Provider?.Authentication is ExplicitApiKeyProviderAuthentication)
                ThrowRuntimeOnly($"{path}.{family}.provider.authentication", "explicit literal credential registration");
            var hasOverride = config switch
            {
                ChatClientConfig value => value.Override is not null,
                RealtimeClientConfig value => value.Override is not null,
                ImageGenerationClientConfig value => value.Override is not null,
                EmbeddingsClientConfig value => value.Override is not null,
                TextToSpeechClientConfig value => value.Override is not null,
                SpeechToTextClientConfig value => value.Override is not null,
                HostedFilesClientConfig value => value.Override is not null,
                _ => false
            };
            if (hasOverride)
                ThrowRuntimeOnly($"{path}.{family}.override", "client override");
            if (config is ChatClientConfig { RuntimeResponseFormat: not null })
                ThrowRuntimeOnly($"{path}.{family}.responseFormat", "runtime response format");
        }
    }

    private static void ThrowRuntimeOnly(string path, string kind) =>
        throw new AgentRunConfigurationException(
            "RuntimeOnlyProviderConfiguration",
            path,
            $"The {kind} at '{path}' is process-local and cannot be serialized.");

    private static string WriteObject(JsonObject root, HpdConfigFormat format) => format switch
    {
        HpdConfigFormat.Json => root.ToJsonString(HPDJsonContext.Default.Options),
        HpdConfigFormat.Yaml => HpdConfigSerializer.WriteYaml(root),
        _ => throw new ArgumentOutOfRangeException(nameof(format), format, null)
    };

    private static void BindFamilies(
        AgentClientsConfig clients,
        IEnumerable<ExtractedPayload> payloads,
        ProviderComposition composition)
    {
        foreach (var payload in payloads)
        {
            var target = clients.GetFamilyConfig(payload.Family);
            if (target is null)
                continue;
            var providerKey = payload.ProviderKey ?? target.Provider?.Key;
            if (payload.Configuration is not null)
                target.ProviderConfig = (IProviderConfig)BindPayload(
                    composition, providerKey, payload.Family, ProviderPayloadKind.Configuration,
                    payload.Configuration, payload.Path + ".providerConfig");
            if (payload.OperationOptions is not null)
                SetOperationOptions(target, BindPayload(
                    composition, providerKey, payload.Family, ProviderPayloadKind.OperationOptions,
                    payload.OperationOptions, payload.Path + ".providerOptions"));
        }
    }

    private static void InjectProfile(
        JsonObject owner,
        string profileName,
        AgentProviderBackendProfile profile,
        ProviderComposition composition,
        string path)
    {
        foreach (var (name, family) in FamilyNames)
            InjectFamily(owner, name, family, profile.Clients.GetFamilyConfig(family), composition, path, profileName);
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
        string path,
        string? profileName = null)
    {
        if (config is null || GetObject(owner, name) is not { } familyNode)
            return;
        var providerKey = ResolveProfileProviderKey(
            composition,
            profileName,
            config.Provider?.Key,
            $"{path}.{name}.providerKey");
        if (config.ProviderConfig is not null)
            familyNode["providerConfig"] = SerializePayload(
                composition,
                providerKey,
                family,
                ProviderPayloadKind.Configuration,
                config.ProviderConfig,
                $"{path}.{name}.providerConfig");
        var operationOptions = GetOperationOptions(config);
        if (operationOptions is not null)
            familyNode["providerOptions"] = SerializePayload(
                composition,
                providerKey,
                family,
                ProviderPayloadKind.OperationOptions,
                operationOptions,
                $"{path}.{name}.providerOptions");
    }

    private static string? ResolvePayloadProviderKey(
        ProviderComposition composition,
        ExtractedPayload payload,
        string? targetProviderKey)
        => ResolveProfileProviderKey(
            composition,
            payload.ProfileProviderKey,
            payload.ProviderKey ?? targetProviderKey,
            payload.Path + ".providerKey");

    private static string? ResolveProfileProviderKey(
        ProviderComposition composition,
        string? profileName,
        string? nestedProviderKey,
        string path)
    {
        if (profileName is null)
            return nestedProviderKey;

        var profileProviderKey = composition.Descriptors.Canonicalize(profileName);
        if (string.IsNullOrWhiteSpace(nestedProviderKey))
            return profileProviderKey;

        var nestedCanonicalKey = composition.Descriptors.Canonicalize(nestedProviderKey);
        if (!StringComparer.Ordinal.Equals(profileProviderKey, nestedCanonicalKey))
            throw new AgentRunConfigurationException(
                "ProviderProfileKeyMismatch",
                path,
                $"Provider profile '{profileName}' resolves to '{profileProviderKey}', but its nested providerKey resolves to '{nestedCanonicalKey}'.",
                profileProviderKey);

        return profileProviderKey;
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
            ExtractFamilies(clients, null, null, "clients", result);
        if (GetArray(root, "providerProfiles") is { } profiles)
        {
            for (var index = 0; index < profiles.Count; index++)
                if (profiles[index] is JsonObject profile && GetObject(profile, "clients") is { } profileClients)
                    ExtractFamilies(
                        profileClients,
                        index,
                        GetString(profile, "providerKey") ?? throw new AgentRunConfigurationException(
                            "ProviderKeyRequired", $"providerProfiles[{index}].providerKey",
                            "Every provider profile requires an explicit providerKey."),
                        $"providerProfiles[{index}].clients",
                        result);
        }
        return result;
    }

    private static void NormalizeProviderProfiles(
        JsonObject root,
        ProviderComposition composition)
    {
        if (GetArray(root, "providerProfiles") is not { } profiles)
            return;

        var identities = new HashSet<(string Provider, string Backend)>();
        for (var index = 0; index < profiles.Count; index++)
        {
            if (profiles[index] is not JsonObject profile)
                continue;
            var providerKey = GetString(profile, "providerKey")
                ?? throw new AgentRunConfigurationException(
                    "ProviderKeyRequired", $"providerProfiles[{index}].providerKey",
                    "Every provider profile requires an explicit providerKey.");
            var backendKey = GetString(profile, "backendKey")
                ?? throw new AgentRunConfigurationException(
                    "BackendKeyRequired", $"providerProfiles[{index}].backendKey",
                    "Every provider profile requires an explicit backendKey.");
            var canonical = composition.Descriptors.Canonicalize(providerKey);
            if (!identities.Add((canonical, backendKey)))
                throw new AgentRunConfigurationException(
                    "DuplicateProviderProfile",
                    $"providerProfiles[{index}]",
                    $"Provider/backend profile '{canonical}/{backendKey}' is already configured.",
                    canonical);
            profile["providerKey"] = canonical;
        }
    }

    private static void ExtractFamilies(
        JsonObject owner,
        int? profileIndex,
        string? profileProviderKey,
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
                profileIndex,
                profileProviderKey,
                family,
                GetObject(familyObject, "provider") is { } provider
                    ? GetString(provider, "key")
                    : null,
                config,
                options,
                $"{path}.{name}"));
        }
    }

    private static JsonObject? GetObject(JsonObject owner, string name)
        => owner.FirstOrDefault(pair => pair.Key.Equals(name, StringComparison.OrdinalIgnoreCase)).Value as JsonObject;

    private static JsonArray? GetArray(JsonObject owner, string name)
        => owner.FirstOrDefault(pair => pair.Key.Equals(name, StringComparison.OrdinalIgnoreCase)).Value as JsonArray;

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
        ("voiceActivity", ProviderClientFamily.VoiceActivityDetection),
        ("endOfTurn", ProviderClientFamily.EndOfTurnDetection)
    ];

    private sealed record ExtractedPayload(
        int? ProfileIndex,
        string? ProfileProviderKey,
        ProviderClientFamily Family,
        string? ProviderKey,
        JsonNode? Configuration,
        JsonNode? OperationOptions,
        string Path);
}
