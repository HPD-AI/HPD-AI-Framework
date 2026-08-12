using System.Text.Json;
using HPD.Base;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Metadata;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace HPD.Base.AspNetCore;

internal static class RegisteredReadEndpoints
{
    internal static bool HasExposure(IServiceProvider services, BaseReadExposure exposure) =>
        services.GetService<BaseReadRegistry>()?.Registrations.Values.Any(read => read.Exposure == exposure) == true;

    internal static void Map(IEndpointRouteBuilder endpoints, BaseReadExposure exposure, HPDBaseEndpointAudience audience, Action<IEndpointConventionBuilder, HPDBaseEndpointDescriptor>? convention = null)
    {
        BaseReadRegistry? registry = endpoints.ServiceProvider.GetService<BaseReadRegistry>();
        if (registry is null) return;
        foreach (IBaseReadRegistration registration in registry.Registrations.Values
            .Where(value => value.Exposure == exposure && value.Audience == audience)
            .OrderBy(static value => value.Id, StringComparer.Ordinal))
        {
            IBaseReadRegistration captured = registration;
            if (!IsValidHttpReadId(captured.Id))
                throw new InvalidOperationException("base.http.endpoint.idInvalid");
            string operationId = "base.reads." + (exposure == BaseReadExposure.Admin ? "admin." : "public.") + captured.Id;
            string capability = captured.RequiredGrantId;
            var route = endpoints.MapPost("/reads/" + captured.Id, (RequestDelegate)(context => Execute(context, captured)))
                .WithHPDBaseEndpoint(operationId, audience, HPDBaseEndpointOperation.RegisteredRead, capability, convention)
                .WithHPDBaseRegisteredReadOpenApi(
                    operationId,
                    captured.ParameterJsonTypeInfo.Type,
                    captured.ResponseType,
                    exposure == BaseReadExposure.Admin)
                .WithName(operationId);
            route.Add(builder =>
            {
                builder.Metadata.Add(new AcceptsMetadata(["application/json"], captured.ParameterJsonTypeInfo.Type, false));
                builder.Metadata.Add(new ResponseMetadata(captured.ResponseType, StatusCodes.Status200OK, "application/json"));
                foreach (int status in audience is HPDBaseEndpointAudience.Application or HPDBaseEndpointAudience.ControlPlane
                    ? new[] { 400, 401, 403, 413, 424, 500, 503 }
                    : new[] { 400, 403, 413, 424, 500, 503 })
                    builder.Metadata.Add(new ResponseMetadata(typeof(ProblemDetails), status, "application/problem+json"));
            });
        }
    }

    internal static bool IsValidHttpReadId(string value) => value is { Length: >= 1 and <= 96 }
        && IsAlphaNumeric(value[0]) && IsAlphaNumeric(value[^1])
        && value.All(static character => IsAlphaNumeric(character) || character is '.' or '-')
        && !value.Contains("..", StringComparison.Ordinal);

    private static bool IsAlphaNumeric(char character) =>
        character is >= 'a' and <= 'z' or >= '0' and <= '9';

    private static async Task Execute(HttpContext context, IBaseReadRegistration registration)
    {
        object? parameters;
        long maximumBody = context.RequestServices.GetRequiredService<HPDBaseAspNetCoreSnapshot>().Limits.MaxRequestBodyLength;
        if (context.Request.ContentLength is { } length && length > maximumBody) { await BodyTooLarge(context); return; }
        await using var body = new LimitedRequestBodyStream(context.Request.Body, maximumBody);
        try { parameters = await JsonSerializer.DeserializeAsync(body, registration.ParameterJsonTypeInfo, context.RequestAborted).ConfigureAwait(false); }
        catch (RequestBodyTooLargeException) { await BodyTooLarge(context); return; }
        catch (JsonException) { await InvalidBody(context); return; }
        if (parameters is null) { await Problem(context, OperationStatus.ValidationFailed, new BaseError { Code = "base.http.body.required", Message = "Registered read parameters are required.", Category = ErrorCategory.Validation }); return; }
        if (!int.TryParse(context.Request.Query["page"], out int page)) page = 1;
        if (!int.TryParse(context.Request.Query["perPage"], out int perPage)) perPage = 50;
        BaseReadPageRequest request;
        try { request = BaseReadPageRequest.Create(page, perPage); }
        catch (ArgumentOutOfRangeException) { await Problem(context, OperationStatus.ValidationFailed, new BaseError { Code = "base.relational.read.invalid", Message = "The registered read page is invalid.", Category = ErrorCategory.Validation }); return; }

        IServiceProvider services = context.RequestServices;
        PrincipalContext principal = await services.GetRequiredService<IBaseHttpPrincipalContextFactory>().CreateAsync(context, context.RequestAborted).ConfigureAwait(false);
        OperationContext operation = services.GetRequiredService<IBaseHttpOperationContextFactory>().Create(context, principal, BaseOperationKind.Query, registration.Id);
        BaseUntypedRegisteredReadResult result = await registration.ExecuteAsync(services.GetRequiredService<IBaseRegisteredReadRuntime>(), parameters, request, principal, operation, context.RequestAborted).ConfigureAwait(false);
        if (result.Items is null || result.Page is null) { await Problem(context, result.Status, result.Error); return; }

        context.Response.StatusCode = StatusCodes.Status200OK;
        context.Response.ContentType = "application/json";
        await using var writer = new Utf8JsonWriter(context.Response.BodyWriter);
        writer.WriteStartObject(); writer.WritePropertyName("items"); writer.WriteStartArray();
        foreach (object item in result.Items) JsonSerializer.Serialize(writer, item, registration.RowJsonTypeInfo);
        writer.WriteEndArray(); writer.WritePropertyName("page"); JsonSerializer.Serialize(writer, result.Page, HPDBaseJsonSerializerContext.Default.PageInfo);
        if (result.Count is not null) { writer.WritePropertyName("count"); JsonSerializer.Serialize(writer, result.Count, HPDBaseJsonSerializerContext.Default.CountInfo); }
        writer.WriteEndObject(); await writer.FlushAsync(context.RequestAborted).ConfigureAwait(false);
    }

    private static Task Problem(HttpContext context, OperationStatus status, BaseError? error) => Results.Problem(
        statusCode: BaseHttpStatusCodeMapper.ToStatusCode(status), title: "BASE registered read failed.", detail: error?.Message,
        extensions: new Dictionary<string, object?> { ["hpd.error.code"] = error?.Code ?? "base.relational.read.resultInvalid" }).ExecuteAsync(context);

    private static Task InvalidBody(HttpContext context) => Problem(context, OperationStatus.ValidationFailed,
        new BaseError { Code = "base.http.body.invalidJson", Message = "Request body is not valid JSON.", Category = ErrorCategory.Validation });

    private static Task BodyTooLarge(HttpContext context) => Results.Problem(
        statusCode: StatusCodes.Status413PayloadTooLarge,
        title: "BASE registered read request is too large.",
        detail: "Request body exceeds the configured maximum length.",
        extensions: new Dictionary<string, object?> { ["hpd.error.code"] = "base.http.body.tooLarge" })
        .ExecuteAsync(context);

    private sealed record ResponseMetadata(Type Type, int StatusCode, string ContentType) : IProducesResponseTypeMetadata
    {
        /// <summary>Gets the description.</summary>
        public string? Description => null;
        /// <summary>Gets the content types.</summary>
        public IEnumerable<string> ContentTypes => [ContentType];
    }
}
