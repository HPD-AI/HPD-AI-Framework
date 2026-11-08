using HPD.Agent.AspNetCore.EndpointMapping.Endpoints;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;

namespace HPD.Agent.AspNetCore.Evaluations;

/// <summary>Maps the optional evaluation HTTP surface.</summary>
public static class HPDAgentEvaluationEndpointRouteBuilderExtensions
{
    /// <summary>Maps evaluation scores, analytics, runs, datasets, and red-team endpoints.</summary>
    public static RouteGroupBuilder MapHPDAgentEvaluationApi(
        this IEndpointRouteBuilder endpoints,
        string routePrefix = "/evals")
    {
        ArgumentNullException.ThrowIfNull(endpoints);
        ArgumentException.ThrowIfNullOrWhiteSpace(routePrefix);
        return EvalEndpoints.Map(endpoints, routePrefix);
    }
}
