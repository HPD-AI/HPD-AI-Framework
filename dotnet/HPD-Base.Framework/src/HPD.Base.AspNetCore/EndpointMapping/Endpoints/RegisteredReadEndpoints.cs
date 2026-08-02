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
    internal static void Map(IEndpointRouteBuilder endpoints)
    {
        BaseReadRegistry? registry = endpoints.ServiceProvider.GetService<BaseReadRegistry>();
        if (registry is null) return;
        foreach (IBaseReadRegistration registration in registry.Registrations.Values.OrderBy(static value => value.Id, StringComparer.Ordinal))
        {
            IBaseReadRegistration captured = registration;
            string operationId = "base.reads." + captured.Id;
            var route = endpoints.MapPost("/reads/" + captured.Id, (RequestDelegate)(context => Execute(context, captured)))
                .WithHPDBaseRegisteredReadOpenApi(operationId, captured.ParameterJsonTypeInfo.Type, captured.ResponseType)
                .WithName(operationId);
            route.Add(builder =>
            {
                builder.Metadata.Add(new AcceptsMetadata(["application/json"], captured.ParameterJsonTypeInfo.Type, false));
                builder.Metadata.Add(new ResponseMetadata(captured.ResponseType, StatusCodes.Status200OK, "application/json"));
                foreach (int status in new[] { 400, 403, 424, 500 }) builder.Metadata.Add(new ResponseMetadata(typeof(ProblemDetails), status, "application/problem+json"));
            });
        }
    }

    private static async Task Execute(HttpContext context, IBaseReadRegistration registration)
    {
        object? parameters;
        long maximumBody = context.RequestServices.GetRequiredService<IOptions<HPDBaseAspNetCoreOptions>>().Value.Limits.MaxRequestBodyLength;
        if (context.Request.ContentLength is { } length && length > maximumBody) { await InvalidBody(context); return; }
        await using var body = new LimitedRequestBodyStream(context.Request.Body, maximumBody);
        try { parameters = await JsonSerializer.DeserializeAsync(body, registration.ParameterJsonTypeInfo, context.RequestAborted).ConfigureAwait(false); }
        catch (JsonException) { await InvalidBody(context); return; }
        if (parameters is null) { await Problem(context, OperationStatus.ValidationFailed, new BaseError { Code = "base.http.body.required", Message = "Registered read parameters are required.", Category = ErrorCategory.Validation }); return; }
        if (!int.TryParse(context.Request.Query["page"], out int page)) page = 1;
        if (!int.TryParse(context.Request.Query["perPage"], out int perPage)) perPage = 50;
        BaseReadPageRequest request;
        try { request = BaseReadPageRequest.Create(page, perPage); }
        catch (ArgumentOutOfRangeException) { await Problem(context, OperationStatus.ValidationFailed, new BaseError { Code = "base.relational.read.invalid", Message = "The registered read page is invalid.", Category = ErrorCategory.Validation }); return; }

        IServiceProvider services = context.RequestServices;
        PrincipalContext principal = await services.GetRequiredService<IBaseHttpPrincipalContextFactory>().CreateAsync(context, HPDBaseEndpointKind.Records, context.RequestAborted).ConfigureAwait(false);
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

    private sealed record ResponseMetadata(Type Type, int StatusCode, string ContentType) : IProducesResponseTypeMetadata
    {
        /// <summary>Gets the description.</summary>
        public string? Description => null;
        /// <summary>Gets the content types.</summary>
        public IEnumerable<string> ContentTypes => [ContentType];
    }
}
