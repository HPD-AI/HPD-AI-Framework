using HPD.Agent;

namespace HPD.Agent.Hosting.Data;

/// <summary>
/// Normal hosted-runtime text input request.
/// Route scope supplies agent, session, and branch identity.
/// </summary>
public sealed record StreamTextRequest(
    string Text,
    AgentRunConfig? RunConfig = null);
