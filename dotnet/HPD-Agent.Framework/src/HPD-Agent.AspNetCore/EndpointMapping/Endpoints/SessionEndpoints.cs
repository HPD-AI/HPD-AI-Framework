using HPD.Agent.Hosting.Data;
using HPD.Agent.Hosting.Lifecycle;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;

namespace HPD.Agent.AspNetCore.EndpointMapping.Endpoints;

/// <summary>
/// Session CRUD endpoints for the HPD-Agent API.
/// </summary>
internal static class SessionEndpoints
{
    /// <summary>
    /// Maps all session-related endpoints.
    /// </summary>
    internal static void Map(IEndpointRouteBuilder endpoints, IAgentSessionService sessions)
    {
        // POST /sessions - Create new session
        endpoints.MapPost("/sessions", (CreateSessionRequest? request, CancellationToken ct) =>
                CreateSession(sessions, request, ct))
            .WithName("CreateSession")
            .WithSummary("Create a new session with a default 'main' thread");

        // GET /sessions - List all sessions
        endpoints.MapGet("/sessions", (CancellationToken ct) =>
                SearchSessions(sessions, null, ct))
            .WithName("ListSessions")
            .WithSummary("List all sessions");

        // POST /sessions/search - List/search sessions with filtering
        endpoints.MapPost("/sessions/search", (SearchSessionsRequest? request, CancellationToken ct) =>
                SearchSessions(sessions, request, ct))
            .WithName("SearchSessions")
            .WithSummary("Search and list sessions with optional filtering");

        // GET /sessions/{sessionId} - Get session metadata
        endpoints.MapGet("/sessions/{sessionId}", (string sessionId, CancellationToken ct) =>
                GetSession(RouteValue.Decode(sessionId), sessions, ct))
            .WithName("GetSession")
            .WithSummary("Get session metadata by ID");

        // PATCH /sessions/{sessionId} - Update session metadata (merge semantics)
        endpoints.MapPatch("/sessions/{sessionId}", (string sessionId, UpdateSessionRequest request, CancellationToken ct) =>
                UpdateSession(RouteValue.Decode(sessionId), request, sessions, ct))
            .WithName("UpdateSession")
            .WithSummary("Update session metadata with merge semantics");

        // DELETE /sessions/{sessionId} - Delete session + all threads
        endpoints.MapDelete("/sessions/{sessionId}", (string sessionId, CancellationToken ct) =>
                DeleteSession(RouteValue.Decode(sessionId), sessions, ct))
            .WithName("DeleteSession")
            .WithSummary("Delete a session and all its threads");
    }

    private static async Task<Results<Created<SessionDto>, ValidationProblem>> CreateSession(
        IAgentSessionService sessions,
        CreateSessionRequest? request = null,
        CancellationToken ct = default)
    {
        try
        {
            var dto = await sessions.CreateSessionAsync(request, ct);
            return TypedResults.Created($"/sessions/{dto.Id}", dto);
        }
        catch (Exception ex)
        {
            return TypedResults.ValidationProblem(new Dictionary<string, string[]>
            {
                ["CreateSessionError"] = [ex.Message]
            });
        }
    }

    private static async Task<Results<Ok<List<SessionDto>>, ValidationProblem>> SearchSessions(
        IAgentSessionService sessions,
        SearchSessionsRequest? request = null,
        CancellationToken ct = default)
    {
        try
        {
            return TypedResults.Ok((await sessions.SearchSessionsAsync(request, ct)).ToList());
        }
        catch (Exception ex)
        {
            return TypedResults.ValidationProblem(new Dictionary<string, string[]>
            {
                ["SearchSessionsError"] = [ex.Message]
            });
        }
    }

    private static async Task<Results<Ok<SessionDto>, NotFound, ValidationProblem>> GetSession(
        string sessionId,
        IAgentSessionService sessions,
        CancellationToken ct = default)
    {
        try
        {
            var session = await sessions.GetSessionAsync(sessionId, ct);
            if (session == null)
            {
                return TypedResults.NotFound();
            }
            return TypedResults.Ok(session);
        }
        catch (Exception ex)
        {
            return TypedResults.ValidationProblem(new Dictionary<string, string[]>
            {
                ["GetSessionError"] = [ex.Message]
            });
        }
    }

    private static async Task<Results<Ok<SessionDto>, NotFound, ValidationProblem>> UpdateSession(
        string sessionId,
        UpdateSessionRequest request,
        IAgentSessionService sessions,
        CancellationToken ct = default)
    {
        try
        {
            var session = await sessions.UpdateSessionAsync(sessionId, request, ct);
            return session == null ? TypedResults.NotFound() : TypedResults.Ok(session);
        }
        catch (Exception ex)
        {
            return TypedResults.ValidationProblem(new Dictionary<string, string[]>
            {
                ["UpdateSessionError"] = [ex.Message]
            });
        }
    }

    private static async Task<Results<NoContent, NotFound, ValidationProblem>> DeleteSession(
        string sessionId,
        IAgentSessionService sessions,
        CancellationToken ct = default)
    {
        try
        {
            if (!await sessions.DeleteSessionAsync(sessionId, ct))
            {
                return TypedResults.NotFound();
            }
            return TypedResults.NoContent();
        }
        catch (Exception ex)
        {
            return TypedResults.ValidationProblem(new Dictionary<string, string[]>
            {
                ["DeleteSessionError"] = [ex.Message]
            });
        }
    }

}
