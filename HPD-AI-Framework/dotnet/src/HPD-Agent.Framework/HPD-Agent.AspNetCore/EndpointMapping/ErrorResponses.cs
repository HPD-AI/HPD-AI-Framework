using HPD.Agent.AspNetCore.Serialization;
using HPD.Agent.Hosting.Serialization;
using Microsoft.Extensions.AI;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using Microsoft.AspNetCore.Http;

namespace HPD.Agent.AspNetCore.EndpointMapping;

/// <summary>
/// Consistent response helpers that avoid PipeWriter.UnflushedBytes issues with TestHost.
/// These helpers use Results.Content for JSON serialization instead of WriteAsJsonAsync.
/// </summary>
internal static class ErrorResponses
{
    private static readonly JsonSerializerOptions JsonOptions = CreateJsonOptions();

    private static JsonSerializerOptions CreateJsonOptions()
    {
        var options = new JsonSerializerOptions
        {
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        };

        // HPD source-generated contexts first (API DTOs, ASP.NET types, core types)
        options.TypeInfoResolverChain.Add(HPDAgentAspNetCoreJsonSerializerContext.Default);
        options.TypeInfoResolverChain.Add(HPDAgentApiJsonSerializerContext.Default);
        options.TypeInfoResolverChain.Add(HPDJsonContext.Default);

        // M.E.AI resolver chain: covers all AIContent types + $type polymorphism
        // + HPD custom content type registrations (hpd:image, hpd:audio, etc.)
        foreach (var resolver in AIJsonUtilities.DefaultOptions.TypeInfoResolverChain)
        {
            if (resolver != null)
                options.TypeInfoResolverChain.Add(resolver);
        }

        options.MakeReadOnly();
        return options;
    }

    /// <summary>
    /// Returns a JSON result with the specified status code and data.
    /// Uses manual serialization to avoid PipeWriter.UnflushedBytes issues with TestHost.
    /// </summary>
    public static IResult Json<T>(T data, int statusCode = 200)
    {
        return Results.Content(
            JsonSerializer.Serialize(data, JsonOptions),
            contentType: "application/json",
            statusCode: statusCode);
    }

    /// <summary>
    /// Returns a 201 Created response with JSON body and Location header.
    /// </summary>
    public static IResult Created<T>(string location, T data)
    {
        return new CreatedResult(location, JsonSerializer.Serialize(data, JsonOptions));
    }

    /// <summary>
    /// Returns a 404 Not Found response (no body).
    /// </summary>
    public static IResult NotFound()
    {
        return Results.NotFound();
    }

    /// <summary>
    /// Returns a 404 Not Found with ValidationProblem shape.
    /// </summary>
    public static IResult NotFound(string errorKey, string errorMessage)
    {
        var errors = new Dictionary<string, string[]>
        {
            [errorKey] = [errorMessage]
        };
        return Json(new ErrorsWrapper(errors), 404);
    }

    /// <summary>
    /// Returns a 409 Conflict response (no body).
    /// </summary>
    public static IResult Conflict()
    {
        return Results.Conflict();
    }

    /// <summary>
    /// Returns a 409 Conflict with ValidationProblem shape.
    /// </summary>
    public static IResult Conflict(string errorKey, string errorMessage)
    {
        var errors = new Dictionary<string, string[]>
        {
            [errorKey] = [errorMessage]
        };
        return Json(new ErrorsWrapper(errors), 409);
    }

    /// <summary>
    /// Returns a 400 Bad Request response (no body).
    /// </summary>
    public static IResult BadRequest()
    {
        return Results.BadRequest();
    }

    /// <summary>
    /// Returns a 400 Bad Request with ValidationProblem shape.
    /// </summary>
    public static IResult BadRequest(string errorKey, string errorMessage)
    {
        var errors = new Dictionary<string, string[]>
        {
            [errorKey] = [errorMessage]
        };
        return Json(new ErrorsWrapper(errors), 400);
    }

    /// <summary>
    /// Returns a 204 No Content response.
    /// </summary>
    public static IResult NoContent()
    {
        return Results.NoContent();
    }

    /// <summary>
    /// Returns a ValidationProblem response with the specified errors.
    /// </summary>
    public static IResult ValidationProblem(Dictionary<string, string[]> errors)
    {
        return Json(errors, 400);
    }

    /// <summary>
    /// Returns a 500 Internal Server Error with ValidationProblem shape.
    /// </summary>
    public static IResult InternalServerError(string errorKey, string errorMessage)
    {
        var errors = new Dictionary<string, string[]>
        {
            [errorKey] = [errorMessage]
        };
        return Json(new ErrorsWrapper(errors), 500);
    }

    internal sealed record ErrorsWrapper(Dictionary<string, string[]> Errors);

    private class CreatedResult : IResult
    {
        private readonly string _location;
        private readonly string _json;

        public CreatedResult(string location, string json)
        {
            _location = location;
            _json = json;
        }

        public Task ExecuteAsync(HttpContext httpContext)
        {
            httpContext.Response.StatusCode = 201;
            httpContext.Response.Headers.Location = _location;
            httpContext.Response.ContentType = "application/json";
            return httpContext.Response.WriteAsync(_json);
        }
    }
}
