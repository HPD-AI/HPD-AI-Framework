namespace HPD.Agent.Hosting.Lifecycle;

/// <summary>
/// Complete set of hosting services used by the built-in HPD Agent endpoint groups.
/// </summary>
/// <remarks>
/// Applications that want to replace behavior behind built-in routes should replace
/// <see cref="IHPDAgentHostingServicesProvider"/>. Individual service interfaces are
/// still useful for composing HPD Agent behavior in application-owned routes or services.
/// </remarks>
public sealed record HPDAgentHostingServices(
    IAgentSessionService Sessions,
    IAgentThreadService Threads,
    IAgentThreadExecutionService ThreadExecutions,
    IAgentContentService Content,
    IAgentDefinitionService Agents,
    IAgentMiddlewareResponseService MiddlewareResponses,
    IAgentStreamingService Streaming);

/// <summary>
/// Resolves the hosting services used by built-in endpoint mappings for a default or named agent.
/// </summary>
/// <remarks>
/// This provider is the public customization contract for changing behavior behind
/// built-in HPD Agent routes. Endpoint mapping calls this provider for both
/// <c>MapHPDAgentApi()</c> and <c>MapHPDAgentApi(name)</c>.
/// </remarks>
public interface IHPDAgentHostingServicesProvider
{
    HPDAgentHostingServices Get(string name);
}
