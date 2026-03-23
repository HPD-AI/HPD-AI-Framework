namespace HPD.Agent.SourceGenerator.Capabilities;

/// <summary>
/// Defines the types of capabilities that can be registered with the agent.
/// </summary>
internal enum CapabilityType
{
    /// <summary>
    /// A standard function capability that performs a specific operation.
    /// Decorated with [AIFunction] attribute.
    /// </summary>
    Function,

    /// <summary>
    /// A skill capability that groups related functions together.
    /// Decorated with [Skill] attribute. Skills are containers that expand to their constituent functions.
    /// </summary>
    Skill,

    /// <summary>
    /// A sub-agent capability that delegates to another agent.
    /// Decorated with [SubAgent] attribute. SubAgents are wrappers (not containers).
    /// </summary>
    SubAgent,

    /// <summary>
    /// A multi-agent workflow capability that orchestrates multiple agents.
    /// Decorated with [MultiAgent] attribute. MultiAgents are containers that execute graph workflows.
    /// </summary>
    MultiAgent,

    /// <summary>
    /// An MCP server capability that provides external tool connections.
    /// Decorated with [MCPServer] attribute. MCP servers are NOT containers themselves —
    /// their tools are either stamped flat under the parent toolkit or wrapped in an MCP_* container at runtime.
    /// </summary>
    MCPServer,

    /// <summary>
    /// An OpenAPI spec capability that converts REST API operations into AIFunctions at build time.
    /// Decorated with [OpenApi] attribute. The method returns OpenApiConfig (from HPD-Agent.OpenApi).
    /// Functions are generated at Build() time by the loader, not at source-gen time.
    /// OpenAPI is NOT a container at source-gen time — containers are created at runtime if CollapseWithinToolkit=true.
    /// </summary>
    OpenApi
}
