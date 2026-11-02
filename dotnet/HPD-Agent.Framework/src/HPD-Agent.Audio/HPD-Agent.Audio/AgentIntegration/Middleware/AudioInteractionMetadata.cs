namespace HPD.Agent.Audio.AgentIntegration.Middleware;

using System.Runtime.CompilerServices;
using System.Text.Json.Serialization;
using HPD.Agent.Serialization;

public sealed record AudioInteractionInputMetadata(
    int ContentIndex,
    string InputContentId,
    string SourceKind,
    string? MediaType,
    string? Name,
    long? SizeBytes,
    string? Sha256);

public sealed record AudioInteractionRuntimeMetadata(
    int LedgerRecordCount,
    int TraceRecordCount,
    int ProjectedTurnCount,
    string? Transcript,
    string? ProviderKey,
    string? RouteDecisionKind,
    string? Topology,
    string? ResponseOwnership,
    IReadOnlyList<string> AssistantOutputTexts);

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(AudioInteractionInputMetadata[]))]
[JsonSerializable(typeof(AudioInteractionRuntimeMetadata[]))]
internal partial class AudioInteractionMetadataJsonContext : JsonSerializerContext
{
    #pragma warning disable CA2255
    [ModuleInitializer]
    internal static void RegisterJsonMetadata() =>
        HpdAgentJsonUtilities.RegisterTypeInfoResolver(Default);
    #pragma warning restore CA2255
}
