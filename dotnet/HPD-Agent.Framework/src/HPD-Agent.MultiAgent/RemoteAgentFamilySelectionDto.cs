using System.Text.Json.Nodes;
using HPD.Agent;
using HPD.Agent.Providers;
using HPD.Agent.Serialization;

namespace HPD.MultiAgent;

/// <summary>
/// Carries one provider-family selection across a remote multi-agent boundary.
/// </summary>
/// <remarks>
/// The payload contains configuration only. Runtime client overrides and raw API keys are
/// rejected before serialization; the receiving host resolves <see cref="ProviderClientConfig.AuthenticationKey"/>
/// against its own credential registry.
/// </remarks>
public sealed record RemoteAgentFamilySelectionDto
{
    /// <summary>Gets the schema version emitted by this framework version.</summary>
    public const int CurrentSchemaVersion = 1;

    /// <summary>Gets the transport schema version.</summary>
    public int SchemaVersion { get; init; } = CurrentSchemaVersion;

    /// <summary>Gets the provider client family represented by <see cref="Selection"/>.</summary>
    public required ProviderClientFamily Family { get; init; }

    /// <summary>Gets the safe serialized family configuration.</summary>
    public required JsonObject Selection { get; init; }

    /// <summary>Gets the absolute deadline shared by every invocation hop.</summary>
    public AgentInvocationDeadline? Deadline { get; init; }

    /// <summary>Creates a transport payload through the shared generated provider serializer.</summary>
    /// <param name="family">The provider client family to transport.</param>
    /// <param name="configuration">The resolved portable family configuration.</param>
    /// <param name="providerComposition">The receiver-compatible generated provider composition.</param>
    /// <param name="deadline">The optional absolute invocation deadline.</param>
    /// <returns>A versioned safe transport payload.</returns>
    /// <exception cref="InvalidOperationException">
    /// The configuration contains a raw API key or runtime client override.
    /// </exception>
    public static RemoteAgentFamilySelectionDto Create(
        ProviderClientFamily family,
        ProviderClientConfig configuration,
        ProviderComposition providerComposition,
        AgentInvocationDeadline? deadline = null)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(providerComposition);
        EnsurePortable(configuration);

        var clients = new AgentClientsConfig();
        clients.SetFamilyConfig(family, ProviderClientConfigResolver.Clone(configuration));
        var json = HpdAgentConfigSerializer.Serialize(
            new AgentRunConfig { Clients = clients }, providerComposition);
        var root = JsonNode.Parse(json) as JsonObject
            ?? throw new InvalidOperationException("The run configuration did not serialize to an object.");
        var familyName = GetFamilyPropertyName(family);
        var selection = root["clients"]?[familyName] as JsonObject
            ?? throw new InvalidOperationException($"The {family} family did not serialize to an object.");

        return new RemoteAgentFamilySelectionDto
        {
            Family = family,
            Selection = (JsonObject)selection.DeepClone(),
            Deadline = deadline
        };
    }

    /// <summary>Binds the transported selection through the receiver's local provider composition.</summary>
    /// <param name="providerComposition">The receiver's generated provider composition.</param>
    /// <returns>The typed portable family configuration.</returns>
    /// <exception cref="InvalidOperationException">The schema version or payload is invalid.</exception>
    public ProviderClientConfig Bind(ProviderComposition providerComposition)
    {
        ArgumentNullException.ThrowIfNull(providerComposition);
        if (SchemaVersion != CurrentSchemaVersion)
            throw new InvalidOperationException(
                $"Unsupported remote agent family selection schema version '{SchemaVersion}'. Expected '{CurrentSchemaVersion}'.");

        var familyName = GetFamilyPropertyName(Family);
        var root = new JsonObject
        {
            ["clients"] = new JsonObject { [familyName] = Selection.DeepClone() }
        };
        var config = HpdAgentConfigSerializer.DeserializeRunConfig(
            root.ToJsonString(), providerComposition)
            ?? throw new InvalidOperationException("The remote family selection was empty.");
        var family = config.Clients.GetFamilyConfig(Family)
            ?? throw new InvalidOperationException($"The remote {Family} family selection was empty.");
        EnsurePortable(family);
        return family;
    }

    private static void EnsurePortable(ProviderClientConfig configuration)
    {
        if (!string.IsNullOrWhiteSpace(configuration.ApiKey))
            throw new InvalidOperationException(
                "Raw API keys cannot cross a remote agent boundary. Use AuthenticationKey and register the credential on the receiving host.");

        var hasOverride = configuration switch
        {
            ChatClientConfig value => value.Override is not null,
            RealtimeClientConfig value => value.Override is not null,
            ImageGenerationClientConfig value => value.Override is not null,
            EmbeddingsClientConfig value => value.Override is not null,
            TextToSpeechClientConfig value => value.Override is not null,
            SpeechToTextClientConfig value => value.Override is not null,
            HostedFilesClientConfig value => value.Override is not null,
            VoiceActivityDetectionClientConfig value => value.OverrideFactory is not null,
            EndOfTurnDetectionClientConfig value => value.OverrideFactory is not null,
            _ => false
        };
        if (hasOverride)
            throw new InvalidOperationException("Runtime client overrides cannot cross a remote agent boundary.");
    }

    private static string GetFamilyPropertyName(ProviderClientFamily family) => family switch
    {
        ProviderClientFamily.Chat => "chat",
        ProviderClientFamily.TextToSpeech => "textToSpeech",
        ProviderClientFamily.SpeechToText => "speechToText",
        ProviderClientFamily.Realtime => "realtime",
        ProviderClientFamily.ImageGeneration => "imageGeneration",
        ProviderClientFamily.Embeddings => "embeddings",
        ProviderClientFamily.HostedFiles => "hostedFiles",
        ProviderClientFamily.VoiceActivityDetection => "voiceActivityDetection",
        ProviderClientFamily.EndOfTurnDetection => "endOfTurnDetection",
        _ => throw new ArgumentOutOfRangeException(nameof(family), family, null)
    };
}
