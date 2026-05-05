using Microsoft.Agents.Hosting.AspNetCore;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Hosting;

namespace HPD.Agent.Bots.Teams;

public static class TeamsBotEndpointRouteBuilderExtensions
{
    /// <summary>
    /// Maps the standard M365 Agents SDK endpoints for Teams. Teams deliberately
    /// bypasses the generated HPD webhook endpoint because the SDK owns activity
    /// deserialization and authentication integration.
    /// </summary>
    public static WebApplication MapTeamsBot(
        this WebApplication app,
        bool? requireAuth = null,
        bool mapProactiveEndpoints = true)
    {
        ArgumentNullException.ThrowIfNull(app);

        var authRequired = requireAuth ?? !app.Environment.IsDevelopment();

        app.UseAuthentication();
        app.UseAuthorization();
        app.MapAgentRootEndpoint();
        app.MapAgentApplicationEndpoints(requireAuth: authRequired);

        if (mapProactiveEndpoints)
            app.MapAgentProactiveEndpoints<TeamsAgent>(requireAuth: authRequired);

        return app;
    }
}
