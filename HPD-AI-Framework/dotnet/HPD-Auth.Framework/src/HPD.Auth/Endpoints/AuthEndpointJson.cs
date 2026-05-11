using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using Microsoft.AspNetCore.Http;

namespace HPD.Auth.Endpoints;

internal static class AuthEndpointJson
{
    public static async Task<T?> ReadJsonAsync<T>(
        HttpContext httpContext,
        JsonTypeInfo<T> jsonTypeInfo,
        CancellationToken cancellationToken)
    {
        if (httpContext.Request.ContentLength == 0)
            return default;

        return await httpContext.Request.ReadFromJsonAsync(jsonTypeInfo, cancellationToken);
    }

    public static IResult Ok<T>(T value, JsonTypeInfo<T> jsonTypeInfo)
        => new JsonTypeInfoResult<T>(StatusCodes.Status200OK, value, jsonTypeInfo);

    public static IResult Created<T>(string? location, T value, JsonTypeInfo<T> jsonTypeInfo)
        => new JsonTypeInfoResult<T>(StatusCodes.Status201Created, value, jsonTypeInfo, location);

    public static IResult BadRequest(AuthError error)
        => new JsonTypeInfoResult<AuthError>(
            StatusCodes.Status400BadRequest,
            error,
            Serialization.HPDAuthJsonSerializerContext.Default.AuthError);

    public static IResult NotFound(AuthError error)
        => new JsonTypeInfoResult<AuthError>(
            StatusCodes.Status404NotFound,
            error,
            Serialization.HPDAuthJsonSerializerContext.Default.AuthError);

    private sealed class JsonTypeInfoResult<T> : IResult
    {
        private readonly int _statusCode;
        private readonly T _value;
        private readonly JsonTypeInfo<T> _jsonTypeInfo;
        private readonly string? _location;

        public JsonTypeInfoResult(
            int statusCode,
            T value,
            JsonTypeInfo<T> jsonTypeInfo,
            string? location = null)
        {
            _statusCode = statusCode;
            _value = value;
            _jsonTypeInfo = jsonTypeInfo;
            _location = location;
        }

        public async Task ExecuteAsync(HttpContext httpContext)
        {
            httpContext.Response.StatusCode = _statusCode;
            httpContext.Response.ContentType = "application/json; charset=utf-8";
            if (_location is not null)
                httpContext.Response.Headers.Location = _location;

            await JsonSerializer.SerializeAsync(
                httpContext.Response.Body,
                _value,
                _jsonTypeInfo,
                httpContext.RequestAborted);
        }
    }
}
