using HPD.Agent;

namespace HPD.Agent.Hosting.Data;

/// <summary>Describes one hosting store without requiring a specific implementation.</summary>
public sealed record HPDAgentStoreInfo(
    StorePersistenceCapability Persistence,
    bool Configured,
    bool UsingDefaultFallback);

/// <summary>Describes the stores owned by one hosted agent profile.</summary>
public sealed record HPDAgentStorageInfo(
    HPDAgentStoreInfo Sessions,
    HPDAgentStoreInfo AgentDefinitions,
    HPDAgentStoreInfo Content);
