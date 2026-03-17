using HPD.Agent;
using HPD.Agent.AspNetCore.Lifecycle;
using HPD.Agent.Hosting.Data;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;
using System.Text.Json;

namespace HPD.Agent.AspNetCore.EndpointMapping.Endpoints;

/// <summary>
/// Session CRUD endpoints for the HPD-Agent API.
/// </summary>
internal static class SessionEndpoints
{
    /// <summary>
    /// Maps all session-related endpoints.
    /// </summary>
    internal static void Map(IEndpointRouteBuilder endpoints, AspNetCoreSessionManager manager)
    {
        // POST /sessions - Create new session
        endpoints.MapPost("/sessions", (CreateSessionRequest? request, CancellationToken ct) =>
                CreateSession(manager, request, ct))
            .WithName("CreateSession")
            .WithSummary("Create a new session with a default 'main' branch");

        // GET /sessions - List all sessions
        endpoints.MapGet("/sessions", (CancellationToken ct) =>
                SearchSessions(manager, null, ct))
            .WithName("ListSessions")
            .WithSummary("List all sessions");

        // POST /sessions/search - List/search sessions with filtering
        endpoints.MapPost("/sessions/search", (SearchSessionsRequest? request, CancellationToken ct) =>
                SearchSessions(manager, request, ct))
            .WithName("SearchSessions")
            .WithSummary("Search and list sessions with optional filtering");

        // GET /sessions/{sessionId} - Get session metadata
        endpoints.MapGet("/sessions/{sessionId}", (string sessionId, CancellationToken ct) =>
                GetSession(sessionId, manager, ct))
            .WithName("GetSession")
            .WithSummary("Get session metadata by ID");

        // PATCH /sessions/{sessionId} - Update session metadata (merge semantics)
        endpoints.MapPatch("/sessions/{sessionId}", (string sessionId, UpdateSessionRequest request, CancellationToken ct) =>
                UpdateSession(sessionId, request, manager, ct))
            .WithName("UpdateSession")
            .WithSummary("Update session metadata with merge semantics");

        // DELETE /sessions/{sessionId} - Delete session + all branches
        endpoints.MapDelete("/sessions/{sessionId}", (string sessionId, CancellationToken ct) =>
                DeleteSession(sessionId, manager, ct))
            .WithName("DeleteSession")
            .WithSummary("Delete a session and all its branches");
    }

    private static async Task<IResult> CreateSession(
        AspNetCoreSessionManager manager,
        CreateSessionRequest? request = null,
        CancellationToken ct = default)
    {
        try
        {
            // Create session directly in the store — no agent/provider needed.
            // Sessions are provider-agnostic; the agent is only needed during streaming.
            var (sessionId, _) = await manager.CreateSessionAsync(
                request?.SessionId,
                request?.Metadata,
                ct);

            var session = await manager.Store.LoadSessionAsync(sessionId, ct)
                ?? throw new InvalidOperationException($"Session '{sessionId}' not found after creation.");

            var dto = new SessionDto(
                session.Id,
                session.CreatedAt,
                session.LastActivity,
                session.Metadata);

            return ErrorResponses.Created($"/sessions/{session.Id}", dto);
        }
        catch (Exception ex)
        {
            return ErrorResponses.ValidationProblem(new Dictionary<string, string[]>
            {
                ["CreateSessionError"] = [ex.Message]
            });
        }
    }

    private static async Task<IResult> SearchSessions(
        AspNetCoreSessionManager manager,
        SearchSessionsRequest? request = null,
        CancellationToken ct = default)
    {
        try
        {
            var sessionIds = await manager.Store.ListSessionIdsAsync(ct);
            var dtos = new List<SessionDto>();

            foreach (var sessionId in sessionIds)
            {
                var session = await manager.Store.LoadSessionAsync(sessionId, ct);
                if (session == null) continue;

                // Apply metadata filtering if provided
                if (request?.Metadata != null && request.Metadata.Count > 0)
                {
                    var matchesFilter = true;
                    foreach (var filter in request.Metadata)
                    {
                        if (!session.Metadata.TryGetValue(filter.Key, out var value))
                        {
                            matchesFilter = false;
                            break;
                        }

                        // Compare values using string representation for robust comparison
                        // (handles JsonElement vs native type mismatches)
                        var sessionValue = value?.ToString() ?? "";
                        var filterValue = filter.Value?.ToString() ?? "";
                        if (sessionValue != filterValue)
                        {
                            matchesFilter = false;
                            break;
                        }
                    }

                    if (!matchesFilter)
                        continue;
                }

                dtos.Add(new SessionDto(
                    session.Id,
                    session.CreatedAt,
                    session.LastActivity,
                    session.Metadata));
            }

            // Apply offset and limit
            var offset = request?.Offset ?? 0;
            var limit = request?.Limit ?? 50;

            var result = dtos
                .OrderByDescending(s => s.LastActivity)
                .Skip(offset)
                .Take(limit)
                .ToList();

            return ErrorResponses.Json(result);
        }
        catch (Exception ex)
        {
            return ErrorResponses.ValidationProblem(new Dictionary<string, string[]>
            {
                ["SearchSessionsError"] = [ex.Message]
            });
        }
    }

    private static async Task<IResult> GetSession(
        string sessionId,
        AspNetCoreSessionManager manager,
        CancellationToken ct = default)
    {
        try
        {
            var session = await manager.Store.LoadSessionAsync(sessionId, ct);
            if (session == null)
            {
                return ErrorResponses.NotFound();
            }

            var dto = new SessionDto(
                session.Id,
                session.CreatedAt,
                session.LastActivity,
                session.Metadata);

            return ErrorResponses.Json(dto);
        }
        catch (Exception ex)
        {
            return ErrorResponses.ValidationProblem(new Dictionary<string, string[]>
            {
                ["GetSessionError"] = [ex.Message]
            });
        }
    }

    private static async Task<IResult> UpdateSession(
        string sessionId,
        UpdateSessionRequest request,
        AspNetCoreSessionManager manager,
        CancellationToken ct = default)
    {
        try
        {
            // Use session lock to prevent concurrent modification race conditions
            return await manager.WithSessionLockAsync(sessionId, async () =>
            {
                var session = await manager.Store.LoadSessionAsync(sessionId, ct);
                if (session == null)
                {
                    return ErrorResponses.NotFound();
                }

                session.Store = manager.Store;

                // Merge semantics: update or add provided keys, remove keys set to null
                if (request.Metadata != null)
                {
                    foreach (var kvp in request.Metadata)
                    {
                        // Check for null or JsonElement with Null/Undefined value kind
                        bool isNullValue = kvp.Value == null ||
                            (kvp.Value is JsonElement je && (
                                je.ValueKind == JsonValueKind.Null ||
                                je.ValueKind == JsonValueKind.Undefined));

                        if (isNullValue)
                        {
                            // Remove the key from metadata entirely
                            // Use TryRemove to be safe even if key doesn't exist
                            if (session.Metadata.ContainsKey(kvp.Key))
                            {
                                session.Metadata.Remove(kvp.Key);
                            }
                        }
                        else
                        {
                            // Add or update the metadata value
                            session.Metadata[kvp.Key] = kvp.Value;
                        }
                    }
                }

                // Update LastActivity timestamp after metadata changes
                session.LastActivity = DateTime.UtcNow;

                await manager.Store.SaveSessionAsync(session, ct);

                // Return DTO - ensure no null values in metadata and always return a dictionary
                var cleanedMetadata = session.Metadata
                    .Where(kvp => kvp.Value != null)
                    .ToDictionary(kvp => kvp.Key, kvp => kvp.Value);

                var dto = new SessionDto(
                    session.Id,
                    session.CreatedAt,
                    session.LastActivity,
                    cleanedMetadata);

                return ErrorResponses.Json(dto);
            }, ct);
        }
        catch (Exception ex)
        {
            return ErrorResponses.ValidationProblem(new Dictionary<string, string[]>
            {
                ["UpdateSessionError"] = [ex.Message]
            });
        }
    }

    private static async Task<IResult> DeleteSession(
        string sessionId,
        AspNetCoreSessionManager manager,
        CancellationToken ct = default)
    {
        try
        {
            var session = await manager.Store.LoadSessionAsync(sessionId, ct);
            if (session == null)
            {
                return ErrorResponses.NotFound();
            }

            // Delete the session (this will delete all branches and assets via ISessionStore)
            await manager.Store.DeleteSessionAsync(sessionId, ct);

            // Clean up in-memory stream/session locks for this session
            manager.RemoveSession(sessionId);

            return ErrorResponses.NoContent();
        }
        catch (Exception ex)
        {
            return ErrorResponses.ValidationProblem(new Dictionary<string, string[]>
            {
                ["DeleteSessionError"] = [ex.Message]
            });
        }
    }
}
