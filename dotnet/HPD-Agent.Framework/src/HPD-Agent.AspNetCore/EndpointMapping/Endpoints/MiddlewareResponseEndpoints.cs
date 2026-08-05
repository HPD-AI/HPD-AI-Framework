using HPD.Agent.Hosting.Lifecycle;
using HPD.Agent.Serialization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;

namespace HPD.Agent.AspNetCore.EndpointMapping.Endpoints;

/// <summary>
/// Middleware response endpoints for the HPD-Agent API.
/// These endpoints allow clients to respond to runtime request sessions.
/// </summary>
internal static class MiddlewareResponseEndpoints
{
    /// <summary>
    /// Maps all middleware response endpoints.
    /// </summary>
    internal static void Map(
        IEndpointRouteBuilder endpoints,
        IAgentMiddlewareResponseService responses)
    {
        endpoints.MapPost("/agents/{agentId}/sessions/{sid}/threads/{bid}/responses", (string agentId, string sid, string bid, HttpRequest request, CancellationToken ct) =>
                Respond(RouteValue.Decode(agentId), RouteValue.Decode(sid), RouteValue.Decode(bid), request, responses, ct))
            .WithName("RespondToAgentRequest")
            .WithSummary("Respond to a request from the agent");
    }

    private static async Task<IResult> Respond(
        string agentId,
        string sid,
        string bid,
        HttpRequest request,
        IAgentMiddlewareResponseService responses,
        CancellationToken ct = default)
    {
        try
        {
            using var reader = new StreamReader(request.Body);
            var json = await reader.ReadToEndAsync(ct);
            if (string.IsNullOrWhiteSpace(json))
            {
                return TypedResults.ValidationProblem(new Dictionary<string, string[]>
                {
                    ["RequestResponse"] = ["A response event envelope is required."]
                });
            }

            if (AgentEventSerializer.FromJson(json) is not AgentEvent evt)
            {
                return TypedResults.ValidationProblem(new Dictionary<string, string[]>
                {
                    ["RequestResponse"] = ["The request body must be a valid agent event envelope."]
                });
            }

            if (evt is not IAgentResponseEvent response)
            {
                return TypedResults.ValidationProblem(new Dictionary<string, string[]>
                {
                    ["RequestResponse"] = ["The event must implement IAgentResponseEvent."]
                });
            }

            return ToHttpResult(await responses.AnswerRequestAsync(agentId, sid, bid, response, ct));
        }
        catch (Exception ex)
        {
            return TypedResults.ValidationProblem(new Dictionary<string, string[]>
            {
                ["RequestResponseError"] = [ex.Message]
            });
        }
    }

    private static IResult ToHttpResult(AgentServiceResult<AgentRespondResult> result)
    {
        return result.Status switch
        {
            AgentServiceStatus.Success => TypedResults.Ok(result.Value),
            AgentServiceStatus.NotFound => TypedResults.NotFound(),
            AgentServiceStatus.Conflict when result.ErrorCode != null => TypedResults.Json(
                new
                {
                    title = result.ErrorMessage ?? "Middleware response conflict.",
                    result = result.Value,
                    errors = new Dictionary<string, string[]>
                    {
                        [result.ErrorCode] = [result.ErrorMessage ?? "Middleware response conflict."]
                    }
                },
                statusCode: StatusCodes.Status409Conflict),
            AgentServiceStatus.Conflict => TypedResults.Conflict(),
            _ => TypedResults.ValidationProblem(new Dictionary<string, string[]>
            {
                [result.ErrorCode ?? "MiddlewareResponseError"] = [result.ErrorMessage ?? "Middleware response failed."]
            })
        };
    }
}
