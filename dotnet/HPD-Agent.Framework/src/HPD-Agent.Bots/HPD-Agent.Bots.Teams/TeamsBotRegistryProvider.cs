using HPD.Agent.Bots.AspNetCore;
using Microsoft.Agents.Hosting.AspNetCore;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;

namespace HPD.Agent.Bots.Teams;

internal sealed class TeamsBotRegistryProvider : IBotRegistryProvider
{
    private static readonly BotRegistration Registration = new(
        "teams",
        typeof(TeamsBot),
        MapEndpoint,
        "/api/messages");

    public IEnumerable<BotRegistration> GetAll()
        => [Registration];

    private static IEndpointConventionBuilder MapEndpoint(IEndpointRouteBuilder endpoints, string? path)
    {
        if (path is not null)
        {
            throw new NotSupportedException(
                "Teams uses the M365 Agents SDK endpoint mapping. Custom endpoint aliases are not supported yet.");
        }

        return endpoints.MapAgentApplicationEndpoints(requireAuth: true);
    }
}
