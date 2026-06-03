namespace HPD.Agent.Audio.AgentIntegration.Middleware;

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
